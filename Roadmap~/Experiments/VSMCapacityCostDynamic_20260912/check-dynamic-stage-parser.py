"""Synthetic checks for timing quantiles, paired scope sums and metadata rejection."""
import csv
import importlib.util
import json
from pathlib import Path
import tempfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('dynamic_timing', HERE / 'analyze-dynamic-stage-timing.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

with tempfile.TemporaryDirectory(prefix='vsm-dynamic-stage-parser-') as temp:
    root = Path(temp).resolve()
    (root / 'status.txt').write_text('complete')
    (root / 'request.json').write_text(json.dumps({'timingOnly': True, 'modes': ['adaptive'], 'frames': 32}))
    order = [(s, 'forward') for s in module.SCENARIOS] + [(s, 'reverse') for s in reversed(module.SCENARIOS)]
    for window, (scenario, direction) in enumerate(order):
        folder = root / f'{scenario}_adaptive_{direction}'
        folder.mkdir()
        first = window * 193
        metadata = {'warmFrames': 64, 'observationFrames': 128, 'trajectoryFrames': 32, 'observed': 128,
                    'resolution': 2048, 'capacity': 256, 'depthLayers': 16, 'historyPresent': True,
                    'smrt': {'x': 4}, 'quality': {}, 'width': 1920, 'height': 1080, 'aa': 'None',
                    'firstWarmFrame': first + 1, 'lastWarmFrame': first + 64,
                    'firstObservedFrame': first + 65, 'lastObservedFrame': first + 192, 'metadataFrame': first + 192,
                    'scenario': scenario, 'order': direction, 'window': window, 'fixtureBackend': 'meshlet'}
        settings = {key: {'m_Value': True} for key in ['virtualShadowMapSMRT', 'virtualShadowMapSMRTAdaptiveRays',
                                                      'screenSpaceShadowDenoise', 'virtualShadowMapSMRTTemporalDenoise']}
        for filename, data in [('window.json', metadata), ('warm-start.json', metadata),
                               ('settings.json', settings), ('settings-final.json', settings)]:
            (folder / filename).write_text(json.dumps(data))
        with (folder / 'stage-timing.csv').open('w', newline='') as f:
            writer = csv.writer(f)
            writer.writerow(['observation', 'camera_frame_observed', 'editor_time_observed',
                             'vsm_active_observed', 'marker', 'gpu_ns', 'gpu_sample_count'])
            for i in range(128):
                for marker in module.MARKERS:
                    ns, count = 100_000, 1
                    if marker == 'VSM.Resolve': ns = 4_000_000 + i * 10_000
                    elif marker == 'VSM.ResolveTrace': ns = 3_000_000 + i * 10_000
                    elif marker == 'VSM.PageCull': ns, count = 50_000, 2
                    elif marker in ('VSM.UnityCompatibilityRaster', 'VSM.FilterVertical'): ns, count = 0, 0
                    elif marker == 'VSM.InvalidateStatic' and i % 4 != 0: ns, count = 0, 0
                    writer.writerow([i, first + 65 + i, (first + 65 + i) / 60, 1, marker, ns, count])
    report = module.analyze(root)
    first = report['windows']['static_adaptive_forward']
    assert report['status'] == 'pass' and report['observations'] == 2304
    assert abs(first['stages']['VSM.ResolveTrace']['executed_gpu_ms']['p50'] - 3.635) < 1e-12
    assert abs(first['stages']['VSM.ResolveTrace']['executed_gpu_ms']['p95'] - 4.2065) < 1e-12
    assert first['stages']['VSM.UnityCompatibilityRaster']['executed_gpu_ms']['p50'] is None
    assert first['stages']['VSM.InvalidateStatic']['executed_gpu_ms']['count'] == 32
    assert abs(first['resolve_minus_paired_children_ms']['p50'] - .8) < 1e-12
    assert abs(first['recorded_cull_parent_sum_ms']['p50'] - .2) < 1e-12
    assert abs(first['recorded_raster_parent_sum_ms']['p50'] - .2) < 1e-12
    assert first['outer_scope_peaks'][0]['observation'] == 124
    # One repeated GPU execution must be rejected, without fabricating zero cost.
    path = root / 'static_adaptive_forward' / 'stage-timing.csv'
    data = path.read_text().replace('VSM.Resolve,4000000,1', 'VSM.Resolve,4000000,2')
    path.write_text(data)
    rejected = module.analyze(root)
    assert rejected['status'] == 'review-required' and rejected['rejected_observations'] == 1
    # Out-of-sequence metadata cannot be silently pooled with valid windows.
    metadata_path = root / 'static_adaptive_forward' / 'window.json'
    meta = json.loads(metadata_path.read_text()); meta['firstObservedFrame'] += 1
    metadata_path.write_text(json.dumps(meta))
    try:
        module.analyze(root)
    except ValueError:
        pass
    else:
        raise AssertionError('Invalid frame ranges accepted')
print('dynamic stage parser: 11 checks passed (synthetic 18-window / 2304-observation fixture)')
