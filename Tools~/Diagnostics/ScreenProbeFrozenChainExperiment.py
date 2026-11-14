"""Counterfactual SH/jitter isolation through production pixel temporal and Atrous."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import unpack_pixel_history
from LinearGiExport import exr_read
from TemporalSamplingExperiment import LUMA, metrics, paired, read


def compare(before, after):
    if metrics(before)['temporal_variance'] == 0:
        return dict(variance_reduction_percent=None, beforeConstant=True,
                    afterConstant=metrics(after)['temporal_variance'] == 0)
    return paired(before, after)


def analyze(root, linear):
    m = json.loads((root/'capture.json').read_text(encoding='utf-8'))
    assert m['complete'] and not m['errors'] and m['schema'] == 'screen-probe-frozen-chain.v1'
    assert (m['warmup'], m['measuredFrames'], m['spacing'], m['rays']) == (512, 256, 16, 8)
    q = json.loads((root/'qualification.json').read_text(encoding='utf-8'))
    assert q['files'] > 0 and not q['mismatch']
    frames = range(512, 768)
    f = {(r['arm'], r['frame'], r['tag']): r for r in m['records'] if 'tag' in r}
    states = [r for r in m['records'] if 'state' in r]
    arms = ['baseline-a', 'baseline-b', 'frozen-mean', 'frozen-fixed']
    initial_vp = states[0]['vp']
    input_root = Path(m['inputRoot'])
    expected_source = (input_root/'expected-radiosity.bin').read_bytes()
    expected_geometry = (input_root/'expected-geometry.bin').read_bytes()
    frozen = (input_root/'mean-sh.bin').read_bytes()
    report = dict(complete=False, scope='Fixed-pose input-isolation diagnostic, not a quality improvement or physical reference',
                  aa=q, qualifiedProbeInputs=False, regions={}, history={}, shaderVariants=m['variants'])
    for arm in arms:
        rows = [r for r in states if r['arm'] == arm]
        assert [r['frame'] for r in rows] == list(frames)
        for r in rows:
            s = r['state']
            assert s['order'] == 8 and s['batch'] == 32 and s['samples'] == 64 and s['seed'] == 0
            assert s['preFilter'] and s['anchorMaxAge'] == 128 and s['atrous'] == 4 and not s['restir'] and s['guide']
            assert s['frozenInput'] == arm.startswith('frozen') and s['fixedInterpolation'] == (arm == 'frozen-fixed')
            assert s['sun'] == [0, 0, 0] and s['sky'] == 0 and s['nearOcclusion'][1:3] == [0, 0]
            assert r['vp'] == initial_vp
        for i in [512, 767]:
            raw = Path(f[arm, i, 'radiosity']['path']).read_bytes()
            assert raw == expected_source
            assert Path(f[arm, i, 'geometry']['path']).read_bytes() == expected_geometry
            if arm.startswith('frozen'):
                assert Path(f[arm, i, 'frozen-input']['path']).read_bytes() == frozen
    report['sourceSha256'] = hashlib.sha256(expected_source).hexdigest()
    report['frozenInputSha256'] = hashlib.sha256(frozen).hexdigest()
    for arm in arms[2:]:
        for i in frames:
            for tag in ['geometry', 'raw', 'anchor', 'statistics', 'sh']:
                if not np.array_equal(read(f['baseline-b', i, tag]), read(f[arm, i, tag])):
                    report['inputFailure'] = dict(arm=arm, frame=i, tag=tag)
                    (root/'unqualified-analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
                    raise AssertionError('Probe producer input changed: '+str(report['inputFailure']))
    report['qualifiedProbeInputs'] = True
    ids = exr_read(linear/'vivid-ev4/object_id.exr')[::-1]
    stack = lambda arm, tag: np.stack([read(f[arm, i, tag]) for i in frames])
    for region, label in [('left', 4), ('right', 5), ('back', 3), ('floor', 1), ('ceiling', 2)]:
        meta = f['baseline-b', 512, 'final-'+region]
        x, y, w, h = [meta[k] for k in ['x', 'y', 'width', 'height']]
        mask = ids[y:y+h, x:x+w] == label
        assert mask.sum() > 8
        report['regions'][region] = {}
        for stage in ['resolved', 'temporal', 'final']:
            values = {arm: stack(arm, stage+'-'+region) for arm in arms[1:]}
            y_values = {arm: (v[..., :3]*abs(v[..., 3:4]))@LUMA for arm, v in values.items()}
            row = {arm: metrics(v[:, mask]) for arm, v in y_values.items()}
            row['frozen_vs_dynamic'] = compare(y_values['baseline-b'][:, mask], y_values['frozen-mean'][:, mask])
            row['fixed_vs_frozen_jitter'] = compare(y_values['frozen-mean'][:, mask], y_values['frozen-fixed'][:, mask])
            row['frozen_fixed_all_frames_exact'] = bool(np.all(values['frozen-fixed'] == values['frozen-fixed'][:1]))
            row['coverage'] = {arm: dict(mean=float(abs(v[..., 3][:, mask]).mean()),
                uncoveredSamples=int(np.count_nonzero(v[..., 3][:, mask] == 0))) for arm, v in values.items()}
            report['regions'][region][stage] = row
        report['history'][region] = {}
        for arm in arms[1:]:
            history = stack(arm, 'history-'+region)
            age, coverage, status = unpack_pixel_history(history[..., 3].astype('<f2'))
            valid = mask[None] & (coverage > 0)
            report['history'][region][arm] = dict(ageMean=float(age[valid].mean()), ageMin=int(age[valid].min()),
                ageMax=int(age[valid].max()), statusCounts={str(int(k)): int(np.count_nonzero(status[valid] == k)) for k in np.unique(status[valid])})
    report['complete'] = True
    (root/'analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({r: {s: dict(dynamic=row['baseline-b']['temporal_variance'], frozen=row['frozen-mean']['temporal_variance'],
        frozenFixed=row['frozen-fixed']['temporal_variance'], frozenReductionPercent=row['frozen_vs_dynamic']['variance_reduction_percent'],
        fixedExact=row['frozen_fixed_all_frames_exact']) for s, row in stages.items()} for r, stages in report['regions'].items()}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path); parser.add_argument('--linear', type=Path, required=True)
    args = parser.parse_args(); analyze(args.capture, args.linear)
