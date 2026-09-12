"""Analyze captured, independently enumerated geometric depth capacity.

The default status remains pending-reference-validation. Promote only after
the root agent confirms the reference GPU fixtures and supplies their evidence.
This script never contacts Unity or interprets full production layers as overflow.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
QUANTILES = (0, .5, .9, .95, .99, 1)


def require(value, message):
    if not bool(value):
        raise AssertionError(message)


def load_gzip(path, dtype, shape):
    blob = gzip.decompress(path.read_bytes())
    data = np.frombuffer(blob, dtype=dtype)
    require(data.size == int(np.prod(shape)), f'{path}: unexpected element count {data.size}')
    return data.reshape(shape)


def quantiles(values):
    if not len(values):
        return None
    return {f'p{int(q * 100)}': float(v) for q, v in zip(QUANTILES, np.quantile(values, QUANTILES))}


def group_stats(counts, errors):
    n = len(counts)
    unique, production, matched = (counts[:, i].astype(np.int64) for i in (1, 2, 3))
    missing = errors[:, 2].astype(np.int64)
    flags = errors[:, 3].astype(np.uint32)
    comparison = (unique != 0) & (production != 0)
    high_capacity = unique > 16
    return dict(
        valid_pool_texel_samples=n,
        triangle_candidate_sum=int(counts[:, 0].sum(dtype=np.uint64)),
        geometric_nonempty_samples=int(np.count_nonzero(unique)),
        geometric_unique_buckets_gt16_samples=int(np.count_nonzero(high_capacity)),
        geometric_unique_buckets_gt16_fraction=float(np.mean(high_capacity)) if n else None,
        geometric_unique_buckets_lower_bound_65_samples=int(np.count_nonzero(unique >= 65)),
        geometric_unique_buckets_lower_bound_65_fraction=float(np.mean(unique >= 65)) if n else None,
        geometric_unique_bucket_count_quantiles=quantiles(unique),
        geometric_unique_bucket_histogram_0_to_65=np.bincount(unique, minlength=66).tolist(),
        production_occupied_slots=int(production.sum()),
        production_nonempty_samples=int(np.count_nonzero(production)),
        production_full16_samples=int(np.count_nonzero(production == 16)),
        production_slots_matched_to_any_retained_reference_bucket=int(matched.sum()),
        production_match_fraction=float(matched.sum() / production.sum()) if production.sum() else None,
        production_unmatched_slots=int((production - matched).sum()),
        reference_near16_missing_surface_count=int(missing.sum()),
        reference_near16_missing_samples=int(np.count_nonzero(missing)),
        reference_near16_missing_sample_fraction=float(np.mean(missing != 0)) if n else None,
        reference_near16_missing_surface_fraction=float(missing.sum() / np.minimum(unique, 16).sum())
            if np.minimum(unique, 16).sum() else None,
        reference_near16_missing_quantiles=quantiles(missing),
        production_nonempty_reference_empty_samples=int(np.count_nonzero(flags & 4)),
        reference_nonempty_production_empty_samples=int(np.count_nonzero(flags & 8)),
        samples_with_error_comparison=int(np.count_nonzero(comparison)),
        per_sample_maximum_nearest_error_world_quantiles=quantiles(errors[comparison, 0]),
        per_sample_mean_nearest_error_world_quantiles=quantiles(errors[comparison, 1]),
        geometric_gt16_and_production_full16_samples=int(np.count_nonzero(high_capacity & (production == 16))),
        geometric_gt16_but_production_below16_samples=int(np.count_nonzero(high_capacity & (production < 16))),
        geometric_at_most16_but_production_full16_samples=int(np.count_nonzero(~high_capacity & (production == 16))),
    )


def analyze(args):
    prefix = args.prefix.resolve()
    folder = prefix.parent
    frame = json.loads(Path(str(prefix) + '.json').read_text(encoding='utf-8'))
    request_path = folder / 'request.json' if (folder / 'request.json').is_file() else folder.parent / 'request.json'
    request = json.loads(request_path.read_text(encoding='utf-8'))
    settings = json.loads((folder / 'settings.json').read_text(encoding='utf-8'))
    capacity, levels, grid = frame['pages'], frame['levels'], request['capacityGrid']
    resolution = settings['virtualShadowMapResolution']['m_Value']
    page_size = 128
    require(grid > 0 and grid <= page_size and resolution % page_size == 0, 'Unsupported sample/page grid')
    pages_per_axis = resolution // page_size
    samples = capacity * grid * grid
    paths = {name: Path(str(prefix) + suffix) for name, suffix in {
        'counts': '_capacity-counts.gz', 'errors': '_capacity-errors.gz',
        'reference_depths': '_capacity-reference-depths.gz', 'owners': '_owners.gz',
        'projections': '_projections.gz', 'table': '_table.gz'}.items()}
    counts = load_gzip(paths['counts'], '<u4', (samples, 2, 4))
    errors = load_gzip(paths['errors'], '<f4', (samples, 2, 4))
    depths = load_gzip(paths['reference_depths'], '<f4', (samples, 2, 64))
    owners = load_gzip(paths['owners'], '<u4', (capacity,))
    table = load_gzip(paths['table'], '<u4', (levels * pages_per_axis ** 2,))
    projection_blob = gzip.decompress(paths['projections'].read_bytes())
    projections = np.frombuffer(projection_blob, dtype='<f4').reshape(-1, 40)
    require(len(projections) >= levels, 'Missing projection records')
    # Unity Matrix4x4's 16 scalar fields are column-major in the buffer ABI.
    world_to_shadow = projections[:levels, 16:32].reshape(levels, 4, 4).transpose(0, 2, 1)
    depth_scale = np.linalg.norm(world_to_shadow[:, 2, :3].astype(np.float64), axis=1)
    require(np.all(depth_scale > 0), 'Noninvertible shadow depth mapping')
    depth_span = 1 / depth_scale
    epsilon = np.maximum(np.maximum(args.unique_tolerance, 1e-7), depth_span / 16777214)
    owner_levels = np.full(capacity, -1, dtype=np.int32)
    valid_page = np.zeros(capacity, dtype=bool)
    owner_errors = []
    for physical, encoded in enumerate(owners):
        owner = int(encoded) - 1
        if not encoded:
            continue
        if 0 <= owner < len(table) and table[owner] == physical + 1:
            owner_levels[physical] = owner // pages_per_axis ** 2
            valid_page[physical] = owner_levels[physical] < levels
        else:
            owner_errors.append(physical)
    sample_levels = np.repeat(owner_levels, grid * grid)
    sample_valid = np.repeat(valid_page, grid * grid)
    flags = errors[:, :, 3]
    require(np.all(np.isfinite(errors)), 'Nonfinite reference diagnostics')
    require(np.all(flags == flags.astype(np.uint32)), 'Noninteger flag bits')
    flags = flags.astype(np.uint32)
    require(np.all(flags < 16), 'Unknown reference flags')
    require(np.array_equal((flags[:, 0] & 1) != 0, sample_valid)
            and np.array_equal((flags[:, 1] & 1) != 0, sample_valid), 'GPU valid samples disagree with owner/table')
    require(np.all(counts[:, :, 1] <= 65), 'Unique lower bound outside [0,65]')
    require(np.all(counts[:, :, 2] <= 16), 'Production occupancy above 16')
    require(np.all(counts[:, :, 3] <= counts[:, :, 2]), 'Matched slots exceed occupied slots')
    require(np.all(counts[:, :, 1] <= counts[:, :, 0]), 'Unique buckets exceed triangle candidates')
    require(np.array_equal((flags & 2) != 0, counts[:, :, 1] == 65), 'Reference truncation flag/count mismatch')
    require(np.all(errors[:, :, 2] == errors[:, :, 2].astype(np.uint32)), 'Noninteger missing-near16 count')
    require(np.all(errors[:, :, 2] <= np.minimum(counts[:, :, 1], 16)), 'Missing count exceeds nearest reference set')
    retained = np.minimum(counts[:, :, 1], 64)
    require(np.array_equal(np.sum(depths >= 0, axis=2), retained), 'Archived depth count differs from retained buckets')
    active_depths = np.arange(64)[None, None, :] < retained[:, :, None]
    require(np.all(depths[~active_depths] == -1), 'Unused reference slots not -1')
    require(np.all(np.isfinite(depths[active_depths])), 'Nonfinite retained reference depth')
    sorted_pairs = np.arange(63)[None, None, :] < np.maximum(retained.astype(np.int32) - 1, 0)[:, :, None]
    require(np.all(np.diff(depths, axis=2)[sorted_pairs] > 0), 'Retained geometric distances not strictly increasing')
    comparison = (counts[:, :, 1] != 0) & (counts[:, :, 2] != 0)
    require(np.all(errors[:, :, :2][~comparison] == -1), 'Absent comparison must have -1 error sentinels')
    require(np.all(errors[:, :, 0][comparison] >= errors[:, :, 1][comparison]), 'Mean error above maximum')
    require(np.all(counts[~sample_valid] == 0), 'Invalid physical slot contributed reference counts')
    for sample in np.flatnonzero(sample_valid):
        maximum = depth_span[sample_levels[sample]]
        require(np.all(depths[sample][active_depths[sample]] <= maximum + 1e-3), 'Reference depth exceeds ray span')
    pool_reports = {}
    for pool, name in enumerate(('static', 'dynamic')):
        report = group_stats(counts[sample_valid, pool], errors[sample_valid, pool])
        report['clipmaps'] = []
        for level in range(levels):
            selected = sample_valid & (sample_levels == level)
            level_report = group_stats(counts[selected, pool], errors[selected, pool])
            level_report.update(clipmap=level, allocated_pages=int(np.count_nonzero(valid_page & (owner_levels == level))),
                                world_units_per_texel=float(projections[level, 36]),
                                projection_depth_span_world=float(depth_span[level]),
                                effective_unique_bucket_epsilon_world=float(epsilon[level]))
            report['clipmaps'].append(level_report)
        ranked = np.flatnonzero(sample_valid & (counts[:, pool, 1] > 16))
        ranked = sorted(ranked, key=lambda i: (-int(counts[i, pool, 1]), -float(errors[i, pool, 2]), int(i)))[:32]
        report['largest_capacity_samples'] = []
        for sample in ranked:
            physical = int(sample) // (grid * grid)
            local = int(sample) % (grid * grid)
            report['largest_capacity_samples'].append(dict(sample_index=int(sample), physical_slot=physical,
                virtual_page=int(owners[physical]) - 1, clipmap=int(sample_levels[sample]), sample_x=local % grid, sample_y=local // grid,
                local_texel_x=int(np.floor((local % grid + .5) * page_size / grid)),
                local_texel_y=int(np.floor((local // grid + .5) * page_size / grid)),
                geometric_unique_bucket_lower_bound=int(counts[sample, pool, 1]),
                production_occupied=int(counts[sample, pool, 2]), production_matched=int(counts[sample, pool, 3]),
                missing_nearest_reference16=int(errors[sample, pool, 2])))
        pool_reports[name] = report
    evidence = None
    if args.validation_status == 'pass':
        require(args.validation_evidence is not None and args.validation_evidence.is_file(),
                'PASS status requires the root-confirmed GPU fixture evidence file')
        evidence = dict(path=str(args.validation_evidence.resolve()),
                        sha256=hashlib.sha256(args.validation_evidence.read_bytes()).hexdigest())
    return dict(capture_prefix=str(prefix), status='pending-reference-validation' if args.validation_status == 'pending' else 'reference-gpu-validation-pass',
        reference_gpu_validation_evidence=evidence, data_invariants='pass',
        frame=frame, geometry=(folder / 'geometry.txt').read_text(encoding='utf-8'),
        sampling=dict(grid_per_page_axis=grid, samples_per_allocated_page=grid * grid,
                      valid_allocated_pages=int(np.count_nonzero(valid_page)), physical_capacity=capacity,
                      sample_fraction_of_valid_page_texels=grid * grid / page_size ** 2,
                      total_valid_samples_per_pool=int(np.count_nonzero(sample_valid)),
                      zero_owner_pages=int(np.count_nonzero(owners == 0)), owner_table_errors=owner_errors),
        tolerances=dict(requested_unique_bucket_world=args.unique_tolerance, production_match_world=args.match_tolerance,
                        source='Arguments must match the capture harness bindings; frame JSON did not archive these two values.'),
        pools=pool_reports,
        limitations=[
            'Geometric bucket counts are from forced-opaque, two-sided source triangles, not observed production atomic-discard events.',
            'Alpha tests, production face culling, mesh LOD, raster coverage, skin/vertex deformation and caster eligibility can change the represented surface set.',
            'At most64 nearest quantized buckets are retained; 65 means a lower bound. The reported maximum is censored if any65 exists.',
            'Unique buckets use floor(t/epsilon+0.5); surfaces merge within a bucket and near surfaces can straddle a bucket boundary.',
            'Production matching and missing-near16 are nearest-distance tolerance queries, not one-to-one matching or proof of capacity loss.',
            'A sampled geometric count above16 is capacity pressure for that idealized pool; it is not a measured production overflow percentage or visibility error.',
            f'The uniform {grid}x{grid} grid samples {100 * grid * grid / page_size ** 2:g}% of each {page_size}x{page_size} page, without receiver weighting; unsampled texels are not bounded.',
            'The depth-span reference does not prove that every extra surface affects a receiver SMRT ray.',
            'This is a separate frame from the full occupancy scan; ratios must not be combined as an exactly paired per-texel comparison.'],
        sha256={name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('prefix', type=Path)
    parser.add_argument('--unique-tolerance', type=float, default=.0001)
    parser.add_argument('--match-tolerance', type=float, default=.01)
    parser.add_argument('--validation-status', choices=('pending', 'pass'), default='pending')
    parser.add_argument('--validation-evidence', type=Path)
    parser.add_argument('--output', type=Path, default=HERE / 'capacity-reference-analysis.json')
    args = parser.parse_args()
    result = analyze(args)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    compact = dict(status=result['status'], invariants=result['data_invariants'], sampling=result['sampling'])
    compact['pools'] = {name: {key: value for key, value in data.items() if key not in ('clipmaps', 'largest_capacity_samples',
        'geometric_unique_bucket_histogram_0_to_65')} for name, data in result['pools'].items()}
    print(json.dumps(compact, indent=2))


if __name__ == '__main__':
    main()
