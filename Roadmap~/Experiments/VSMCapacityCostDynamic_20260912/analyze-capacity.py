"""Validate a completed production occupancy capture and summarize its capacity.

Reads only captures. Writes capacity-analysis.json / capacity-pages.csv beside
this script unless --output is supplied. No Unity/editor interaction.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
STRIDE = 64
QUANTILES = (0, .25, .5, .75, .9, .95, .99, 1)


def read_uints(path, shape):
    values = np.fromfile(path, dtype='<u4')
    expected = int(np.prod(shape))
    if values.size != expected:
        raise ValueError(f'{path}: {values.size} uints, expected {expected}')
    return values.reshape(shape).astype(np.uint64)


def require(condition, message):
    if not bool(condition):
        raise AssertionError(message)


def histogram_quantiles(histogram):
    total = int(sum(histogram))
    if total == 0:
        return None
    cumulative = np.cumsum(histogram, dtype=np.uint64)
    return {f'p{round(q * 100)}': int(np.searchsorted(cumulative, max(1, int(np.ceil(q * total)))))
            for q in QUANTILES}


def summarize(row, page_rows, preallocated_bytes):
    valid = page_rows[page_rows[:, 0] == 1]
    texels, occupied, empty, full = map(int, row[[4, 5, 6, 7]])
    histogram = row[32:49].copy()
    nonempty_histogram = histogram.copy()
    nonempty_histogram[0] = 0
    denominator = texels * 16
    result = {
        'owner_assigned_pages': int(row[0]), 'unassigned_physical_slots': int(row[1]),
        'valid_allocated_pages': int(row[2]), 'invalid_owner_pages': int(row[3]),
        'dirty_pages_at_capture': int(row[14]), 'texels_in_valid_pages': texels,
        'empty_texels': empty, 'nonempty_texels': texels - empty,
        'nonempty_texel_fraction': (texels - empty) / texels if texels else None,
        'occupied_depth_slots': occupied, 'empty_depth_slots_in_valid_pages': denominator - occupied,
        'last_layer_nonempty_texels': full, 'last_layer_nonempty_fraction_all_valid_texels': full / texels if texels else None,
        'last_layer_nonempty_fraction_nonempty_texels': full / (texels - empty) if texels > empty else None,
        'mean_occupied_layers_all_valid_texels': occupied / texels if texels else None,
        'mean_occupied_layers_nonempty_texels': occupied / (texels - empty) if texels > empty else None,
        'occupied_layer_quantiles_all_valid_texels': histogram_quantiles(histogram),
        'occupied_layer_quantiles_nonempty_texels': histogram_quantiles(nonempty_histogram),
        'occupied_layer_histogram_0_to_16': histogram.tolist(),
        'nonempty_texels_by_depth_layer_0_to_15': row[16:32].tolist(),
        'occupied_slot_fraction_in_valid_pages': occupied / denominator if denominator else None,
        'nonzero_payload_bytes': occupied * 4,
        'preallocated_depth_bytes': preallocated_bytes,
        'occupied_slot_fraction_of_preallocated_pool': occupied * 4 / preallocated_bytes if preallocated_bytes else None,
        'empty_pages': int(np.count_nonzero(valid[:, 5] == 0)),
        'pages_with_any_nonempty_slot': int(np.count_nonzero(valid[:, 5] != 0)),
        'pages_with_any_nonempty_last_layer': int(np.count_nonzero(valid[:, 7] != 0)),
        'page_max_layer_histogram_0_to_16': np.bincount(valid[:, 8].astype(np.int64), minlength=17).tolist(),
        'anomalies': dict(inverted_adjacent_pairs=int(row[9]), equal_adjacent_pairs=int(row[10]),
                          texels_with_interior_holes=int(row[11]), invalid_depth_slots=int(row[12])),
    }
    return result


def expected_summary(pages, levels):
    result = np.zeros((2, levels + 1, STRIDE), dtype=np.uint64)
    for pool in range(2):
        for level_row in range(levels + 1):
            selected = pages[pool]
            if level_row:
                selected = selected[(selected[:, 0] != 0) & (selected[:, 3] == level_row - 1)]
            states = selected[:, 0]
            valid = selected[states == 1]
            result[pool, level_row, :4] = [np.count_nonzero(states != 0), np.count_nonzero(states == 0),
                                          np.count_nonzero(states == 1), np.count_nonzero(states > 1)]
            for field in list(range(4, 15)) + list(range(16, 49)):
                result[pool, level_row, field] = max(valid[:, field], default=0) if field == 8 else sum(valid[:, field])
    return result


def validate(capture):
    folder = capture / 'capacity' if (capture / 'capacity').is_dir() else capture
    status_path = folder / 'status.txt' if (folder / 'status.txt').is_file() else folder.parent / 'status.txt'
    require(status_path.read_text(encoding='utf-8').strip() == 'complete', 'Capture is not complete')
    frame = json.loads((folder / 'frame.json').read_text(encoding='utf-8'))
    capacity, levels, page_size = frame['capacity'], frame['projectionCount'], frame['pageSize']
    require(frame['layerCount'] == 16, 'Analysis schema is for 16 layers')
    pages = read_uints(folder / 'pages.bin', (2, capacity, STRIDE))
    summary = read_uints(folder / 'summary.bin', (2, levels + 1, STRIDE))
    owners = read_uints(folder / 'owners.bin', (capacity,))
    table = read_uints(folder / 'pagetable.bin', (frame['tableEntries'],))
    metadata = read_uints(folder / 'metadata.bin', (frame['tableEntries'], 4))
    counters = read_uints(folder / 'counters.bin', (4,))
    pages_per_level = frame['pagesPerAxis'] ** 2
    expected = expected_summary(pages, levels)
    require(np.array_equal(summary, expected), 'GPU per-clipmap reduction differs from independent page reduction')
    require(np.array_equal(pages[0, :, :4], pages[1, :, :4]), 'Pool ownership headers disagree')
    require(np.array_equal(pages[0, :, 15], pages[1, :, 15]), 'Pool metadata owner headers disagree')
    for physical, owner_value in enumerate(owners):
        owner = int(owner_value)
        expected_state, level, flags, metadata_owner = 0, 0xffffffff, 0, 0
        if owner:
            virtual = owner - 1
            if virtual >= frame['tableEntries']:
                expected_state = 2
            else:
                level = virtual // pages_per_level
                flags, metadata_owner = map(int, metadata[virtual, :2])
                if level >= levels: expected_state = 2
                elif table[virtual] != physical + 1: expected_state = 3
                elif metadata_owner != physical + 1: expected_state = 5
                elif flags & 2 == 0: expected_state = 4
                else: expected_state = 1
        for pool in range(2):
            row = pages[pool, physical]
            require(row[:4].tolist() == [expected_state, owner, flags, level], f'Owner header mismatch at {pool}/{physical}')
            require(row[15] == metadata_owner, f'Metadata owner mismatch at {pool}/{physical}')
            if expected_state != 1:
                require(np.all(row[4:15] == 0) and np.all(row[16:] == 0), 'Invalid/unassigned page contributed occupancy')
                continue
            histogram, layer_counts = row[32:49], row[16:32]
            require(row[4] == page_size ** 2, f'Wrong texel denominator at {pool}/{physical}')
            require(sum(histogram) == row[4], 'Histogram texel conservation failed')
            require(sum(histogram * np.arange(17, dtype=np.uint64)) == row[5], 'Histogram slot conservation failed')
            require(sum(layer_counts) == row[5], 'Layer slot conservation failed')
            require(histogram[0] == row[6] and row[13] == row[4] - row[6], 'Empty/nonempty conservation failed')
            require(row[8] == max(np.flatnonzero(histogram), default=0), 'Per-page maximum inconsistent with histogram')
            if row[11] == 0:
                require(np.all(layer_counts[:-1] >= layer_counts[1:]), 'No-hole page has nonmonotonic layer occupancy')
                require(row[7] == histogram[16], 'Last layer and full-count disagree on no-hole page')
            require(row[14] == int(bool(flags & 4)), 'Dirty flag mismatch')
            require(np.all(row[49:] == 0), 'Reserved page fields nonzero')
    reciprocal_errors = []
    for virtual, encoded_value in enumerate(table):
        encoded = int(encoded_value)
        if encoded and (encoded > capacity or owners[encoded - 1] != virtual + 1 or metadata[virtual, 1] != encoded
                        or (metadata[virtual, 0] & np.uint64(2)) == 0):
            reciprocal_errors.append(virtual)
    require(not reciprocal_errors, f'Virtual->physical->virtual reciprocity failed: {reciprocal_errors[:8]}')
    require(counters[0] == summary[0, 0, 2], 'Allocator allocated count differs from valid owners')
    projection_blob = np.fromfile(folder / 'projections.bin', dtype='<f4')
    require(projection_blob.size == frame['projectionBufferCount'] * 40, 'Projection buffer ABI differs from 160 bytes')
    projection_parameters = projection_blob.reshape(-1, 40)[:levels, 36:40]
    pools = {}
    page_records = []
    for pool, name in enumerate(('static', 'dynamic')):
        bytes_allocated = frame[name + 'DepthBytes']
        pools[name] = summarize(summary[pool, 0], pages[pool], bytes_allocated)
        pools[name]['clipmaps'] = []
        for level in range(levels):
            selected = pages[pool][(pages[pool, :, 0] != 0) & (pages[pool, :, 3] == level)]
            level_summary = summarize(summary[pool, level + 1], selected, None)
            level_summary.update(clipmap=level, world_units_per_texel=float(projection_parameters[level, 0]))
            pools[name]['clipmaps'].append(level_summary)
        for physical, row in enumerate(pages[pool]):
            if row[0] != 1:
                continue
            page_records.append(dict(pool=name, physical_page=physical, virtual_page=int(row[1]) - 1,
                clipmap=int(row[3]), occupied_slots=int(row[5]), nonempty_texels=int(row[13]),
                empty_texels=int(row[6]), last_layer_nonempty=int(row[7]), max_occupied_layers=int(row[8]),
                slot_fraction=float(row[5]) / (page_size ** 2 * 16),
                last_layer_fraction=float(row[7]) / (page_size ** 2),
                layer_counts=' '.join(map(str, row[16:32])), occupancy_histogram=' '.join(map(str, row[32:49]))))
    total_bytes = frame['staticDepthBytes'] + frame['dynamicDepthBytes']
    occupied_bytes = sum(p['nonzero_payload_bytes'] for p in pools.values())
    files = ('frame.json', 'settings.json', 'pages.bin', 'summary.bin', 'owners.bin', 'pagetable.bin', 'metadata.bin', 'counters.bin', 'projections.bin')
    report = dict(capture=str(folder.resolve()), capture_status='complete', frame=frame,
                  validation=dict(all_invariants='pass', checked_page_rows=2 * capacity, checked_summary_rows=2 * (levels + 1),
                                  checked_table_entries=len(table), reciprocal_owner_errors=0),
                  allocator=dict(zip(('allocated_pages', 'requested_pages', 'newly_allocated_pages', 'page_budget_overflow'), map(int, counters))),
                  depth_storage=dict(preallocated_bytes=total_bytes, preallocated_mib=total_bytes / 2 ** 20,
                    occupied_nonzero_payload_bytes=occupied_bytes, occupied_nonzero_payload_mib=occupied_bytes / 2 ** 20,
                    occupied_slot_fraction=occupied_bytes / total_bytes,
                    static_and_dynamic_are_independent_pools=True), pools=pools,
                  limitations=[
                    'One completed frame at this camera; not a temporal/dynamic capacity bound.',
                    'Full last layer / 16 occupied slots does not prove an extra surface was discarded.',
                    'Nonzero-slot utilization is stored payload occupancy, not useful shadow contribution or attainable compression savings.',
                    'Allocator page_budget_overflow is page demand beyond budget, not depth-layer overflow.',
                    'No receiver weighting: these statistics cover every valid allocated page texel.',
                    'No geometric ReferenceCapacity data is included in this occupancy report.'],
                  sha256={name: hashlib.sha256((folder / name).read_bytes()).hexdigest() for name in files})
    return report, page_records


def latest_capacity():
    candidates = ROOT / 'Temp~/vsm-baseline/cost'
    for directory in sorted(candidates.iterdir(), reverse=True):
        if directory.is_dir() and (directory / 'mode.txt').is_file() and (directory / 'mode.txt').read_text().strip() == 'capacity':
            return directory
    raise FileNotFoundError('No capacity capture found')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('capture', type=Path, nargs='?')
    parser.add_argument('--output', type=Path, default=HERE)
    args = parser.parse_args()
    report, records = validate(args.capture or latest_capacity())
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'capacity-analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    with (args.output / 'capacity-pages.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys() if records else ('pool',))
        writer.writeheader()
        writer.writerows(records)
    compact = dict(validation=report['validation'], allocator=report['allocator'], depth_storage=report['depth_storage'])
    compact['pools'] = {name: {k: data[k] for k in ('nonempty_texels', 'last_layer_nonempty_texels',
        'last_layer_nonempty_fraction_all_valid_texels', 'mean_occupied_layers_all_valid_texels',
        'occupied_layer_quantiles_all_valid_texels', 'pages_with_any_nonempty_last_layer', 'anomalies')}
        for name, data in report['pools'].items()}
    print(json.dumps(compact, indent=2))


if __name__ == '__main__':
    main()
