"""Independent long-sequence energy and temporal-noise gates for CDF proposals."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from LinearGiExport import exr_read
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read, verify_blocked_proposals


def series(records, mask, frames):
    assert [r['frame'] for r in records] == frames
    pixels = int(mask.sum())
    assert pixels > 0
    blocks = len(frames) // 128
    sums = np.zeros((blocks, pixels), np.float64)
    squares = np.zeros_like(sums)
    rgb_means = np.empty((len(frames), 3), np.float64)
    coverage = np.empty(len(frames), np.float64)
    for i, record in enumerate(records):
        value = read(record)[mask]
        rgb = value[:, :3] * np.abs(value[:, 3:4])
        y = (rgb @ LUMA).astype(np.float64)
        rgb_means[i] = rgb.mean(0, dtype=np.float64)
        coverage[i] = np.abs(value[:, 3]).mean(dtype=np.float64)
        sums[i // 128] += y
        squares[i // 128] += y * y
    mean_image = sums.sum(0) / len(frames)
    block_variance = (squares / 128 - 2 * mean_image * sums / 128 + mean_image**2).mean(1)
    assert np.all(block_variance >= -1e-8)
    block_variance = np.maximum(block_variance, 0)
    return {'mean_rgb_frames': rgb_means, 'block_variance': block_variance,
            'coverage': coverage, 'pixels': pixels}


def compare(before, after):
    b, a = before['mean_rgb_frames'], after['mean_rgb_frames']
    blocks = len(a) // 128
    draws = np.random.default_rng(20261114).integers(0, blocks, (4096, blocks))
    bm, am = b.reshape(blocks, 128, 3).mean(1), a.reshape(blocks, 128, 3).mean(1)
    rgb_change = 100 * (a.mean(0) / b.mean(0) - 1)
    rgb_interval = np.quantile(100 * (am[draws].mean(1) / bm[draws].mean(1) - 1), [.025, .975], axis=0)
    by, ay = bm @ LUMA, am @ LUMA
    y_change = 100 * (ay.mean() / by.mean() - 1)
    y_interval = np.quantile(100 * (ay[draws].mean(1) / by[draws].mean(1) - 1), [.025, .975])
    bv, av = before['block_variance'], after['block_variance']
    variance_reduction = 100 * (1 - av.mean() / bv.mean())
    variance_interval = np.quantile(100 * (1 - av[draws].mean(1) / bv[draws].mean(1)), [.025, .975])
    return {'mean_y_before': float(by.mean()), 'mean_y_after': float(ay.mean()),
            'mean_change_percent': float(y_change), 'block128_mean_change_95_percent': y_interval.tolist(),
            'mean_rgb_before': b.mean(0).tolist(), 'mean_rgb_after': a.mean(0).tolist(),
            'mean_rgb_change_percent': rgb_change.tolist(), 'block128_rgb_change_95_percent': rgb_interval.tolist(),
            'energy_passed': bool(abs(y_change) <= 1 and np.max(abs(y_interval)) <= 1
                                  and np.max(abs(rgb_change)) <= 1 and np.max(abs(rgb_interval)) <= 1),
            'temporal_variance_before': float(bv.mean()), 'temporal_variance_after': float(av.mean()),
            'variance_reduction_percent': float(variance_reduction),
            'block128_variance_reduction_95_percent': variance_interval.tolist(),
            'noise_reduction_confirmed': bool(variance_interval[0] > 0),
            'mean_coverage_before': float(before['coverage'].mean()),
            'mean_coverage_after': float(after['coverage'].mean()), 'pixels': before['pixels']}


def aligned_footprint(probe_record, pixel_record, spacing=16):
    # Same uniform-grid screen footprint, not an exact bilateral-weight replay.
    yy, xx = np.mgrid[probe_record['y']:probe_record['y'] + probe_record['height'],
                     probe_record['x']:probe_record['x'] + probe_record['width']]
    return ((xx >= pixel_record['x'] // spacing)
            & (xx < int(np.ceil((pixel_record['x'] + pixel_record['width']) / spacing)))
            & (yy >= pixel_record['y'] // spacing)
            & (yy < int(np.ceil((pixel_record['y'] + pixel_record['height']) / spacing))))


def run(capture, linear, short):
    m = json.loads((capture / 'capture.json').read_text())
    assert m['complete'] is True and not m['errors'] and m['experiment'] == 'reference-cdf'
    assert (m['width'], m['height'], m['spacing'], m['rays'], m['measuredFrames'], m['frameBase']) == (1920, 1080, 16, 8, 2048, 7919)
    assert m['warmup'] == 512 and not m['scene_dirty_before'] and not m['scene_dirty_after']
    assert m['camera']['position'] == [0, 1.5, -3.8] and m['camera']['vertical_fov'] == 42
    assert {v['sha256'] for v in m['compiledVariants'] if v['name'] == 'FilterSdfDiffuseAtrousFinal'} == {'31fbe5302c2a893310ee72f0bcd4f53ad81578da0ad5b154326f4f829b3d3148'}
    records = m['records']
    frames = list(range(512, 2560))
    arms = [prefix + '-reference-' + mode for mode in ['hybrid', 'sdf'] for prefix in ['white', 'cdf']]
    states = [r for r in records if 'state' in r]
    assert {r['arm'] for r in states} == set(arms)
    for arm in arms:
        values = [r for r in states if r['arm'] == arm]
        assert [r['frame'] for r in values] == frames
        for record in values:
            s = record['state']
            expected = arm.startswith('cdf-')
            assert s['order'] == 8 and s['batch'] == 32 and s['radioSamples'] == 64
            assert s['directionalCache'] and not s['restir'] and not s['frozenProposal']
            assert s['guide'] and s['hierarchical'] and s['atrousIterations'] == 3
            assert s['cdfGuide'] == expected and s['samplingFlags'] == (134217728 if expected else 0)
            assert s['sun'][:3] == [0, 0, 0] and s['sky'] == 0
    ids = exr_read(linear / 'vivid-ev4/object_id.exr')[::-1]
    files = {(r['arm'], r['frame'], r['tag']): r for r in records if 'tag' in r}
    matrices = {(r['arm'], r['frame']): r['vp'] for r in records if 'vp' in r}
    canonical = {r['radiosity_sha256'] for r in json.loads(short.read_text())['arms'].values()}
    assert len(canonical) == 1
    report = {'scope': 'Independent 2048-frame phase; conditional block128 intervals, not physical truth',
              'probe_scope': 'Broad ceiling window and aligned 16px grid footprint; not exact bilateral interpolation weights',
              'analysis_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'energy_gate_percent': 1.0, 'pairs': {}, 'sources': {}, 'probes': {}}
    data = {}
    reference_geometry = None
    for arm in arms:
        cache = [r for r in records if r['arm'] == arm and r.get('tag') == 'radiosity']
        assert len(cache) == 2
        hashes = {hashlib.sha256(Path(r['path']).read_bytes()).hexdigest() for r in cache}
        assert hashes == canonical, 'Radiosity changed across captures'
        report['sources'][arm] = next(iter(hashes))
        geometry = read(files[arm, frames[0], 'probe-geometry'])
        if reference_geometry is None:
            reference_geometry = geometry
        assert np.array_equal(geometry, reference_geometry)
        assert all(np.array_equal(read(files[arm, f, 'probe-geometry']), geometry) for f in frames)
        normal = normal_from_packed(np.maximum(geometry[..., 3], 0))
        probe_mask = (geometry[..., 3] >= 0) & (abs(geometry[..., 1] - 3) < .002) & (normal[..., 1] < -.99)
        aligned_mask = probe_mask & aligned_footprint(files[arm, frames[0], 'probe-geometry'], files[arm, frames[0], 'final-ceiling'])
        for tag in ['hit-lighting', 'anchor']:
            data[arm, tag] = series([files[arm, f, tag] for f in frames], probe_mask, frames)
            data[arm, tag + '-aligned'] = series([files[arm, f, tag] for f in frames], aligned_mask, frames)
        for region, object_id in [('ceiling', 2), ('floor', 1), ('right', 5)]:
            row = files[arm, frames[0], 'final-' + region]
            x, y, w, h = (row[k] for k in ['x', 'y', 'width', 'height'])
            mask = ids[y:y+h, x:x+w] == object_id
            data[arm, region] = series([files[arm, f, 'final-' + region] for f in frames], mask, frames)
    for mode in ['hybrid', 'sdf']:
        before, after = 'white-reference-' + mode, 'cdf-reference-' + mode
        assert all(matrices[before, f] == matrices[after, f] for f in frames)
        report['pairs'][mode] = {r: compare(data[before, r], data[after, r]) for r in ['ceiling', 'floor', 'right']}
        report['probes'][mode] = {r: compare(data[before, r], data[after, r])
                                 for r in ['hit-lighting', 'anchor', 'hit-lighting-aligned', 'anchor-aligned']}
    report['proposals'] = verify_blocked_proposals(m, files, frames, arms, require_frozen=False)
    assert all(v['cdf_probe_frames'] > 0 for k, v in report['proposals'].items() if k.startswith('cdf-'))
    report['all_energy_passed'] = all(v['energy_passed'] for regions in report['pairs'].values() for v in regions.values())
    report['all_noise_reductions_confirmed'] = all(v['noise_reduction_confirmed'] for regions in report['pairs'].values() for v in regions.values())
    output = capture.parent / 'energy-reference.json'
    output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(output), 'all_energy_passed': report['all_energy_passed'],
                      'all_noise_reductions_confirmed': report['all_noise_reductions_confirmed'], 'pairs': report['pairs']}, indent=2))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('capture', type=Path)
    p.add_argument('--linear', type=Path, required=True)
    p.add_argument('--short', type=Path, required=True, help='Completed short-sequence analysis.json with the identical source cache')
    args = p.parse_args()
    run(args.capture, args.linear, args.short)
