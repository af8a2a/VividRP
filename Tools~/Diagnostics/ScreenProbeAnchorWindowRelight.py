"""Qualify the long scalar-anchor window's uniform-emission response.

Controls use identical rendered frames and physical source scaling, without an
output gain. These are response errors, not an independent physical GI truth.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from LinearGiExport import exr_read
from TemporalSamplingExperiment import LUMA, read


def response_metrics(control, value, scale):
    control, value = np.asarray(control, np.float64), np.asarray(value, np.float64)
    denominator = control.mean(1) * (scale if scale else 1)
    assert np.all(denominator > 0)
    delta = value - control * scale
    errors = 100 * delta.mean(1) / denominator
    settling = next((i for i in range(len(errors)-7) if np.all(abs(errors[i:i+8]) <= 1)), None)
    return dict(first_frame_error_percent=float(errors[0]),
                first_8_max_abs_mean_error_percent=float(abs(errors[:8]).max()),
                first_32_mean_abs_error_percent=float(abs(errors[:32]).mean()),
                positive_overshoot_percent=float(max(0, errors[:32].max())),
                negative_undershoot_percent=float(min(0, errors[:32].min())),
                tail_64_mean_error_percent=float(errors[-64:].mean()),
                first_frame_normalized_rms_percent=float(100*np.sqrt(np.mean(delta[0]**2))/denominator[0]),
                off_max_absolute_y=float(abs(value).max()) if scale == 0 else None,
                first_eight_consecutive_frames_within_one_percent=settling,
                error_percent_series=errors.tolist())


def plot_response(root, report):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    figure, axes = plt.subplots(2, 3, figsize=(12, 6), sharex=True, constrained_layout=True)
    for column, step in enumerate(['up', 'down', 'off']):
        for row, region in enumerate(['ceiling', 'floor']):
            axis = axes[row,column]
            for mode, label in [('half128','FP16 / 128'),('float512','FP32 / 512')]:
                values = report['arms'][mode+'-'+step]['regions'][region]['final']['error_percent_series']
                axis.plot(values,label=label,linewidth=1)
            axis.axhline(0,color='black',linewidth=.6)
            axis.set_title(region+' / '+step)
            axis.set_ylabel('Mean Y response error (%)')
            axis.set_xlabel('Rendered frames after emission step')
            axis.grid(alpha=.2)
    axes[0,0].legend()
    figure.suptitle('GI-only response to uniform source scaling; identical-frame constant controls\nNo SceneColor, fitted gain or independent physical reference')
    figure.savefig(root/'response-overview.png',dpi=150)
    plt.close(figure)


def analyze(root, static):
    capture = json.loads((root/'capture.json').read_text())
    assert capture['complete'] and not capture['errors']
    assert capture['schema'] == 'screen-probe-anchor-window-relight.v1'
    assert (capture['width'], capture['height'], capture['spacing'], capture['rays']) == (1920, 1080, 16, 8)
    assert (capture['warmup'], capture['measuredFrames'], capture['changeAt']) == (4096, 384, 64)
    verification = json.loads((root/'source-verification.json').read_text())
    assert verification['unchanged'] and verification['scripts'] == 1 and verification['inputs'] == 3
    gates = json.loads((root/'decision-gates.json').read_text())
    frames = list(range(4096, 4480)); change = 4160
    modes, steps = ['half128', 'float512'], dict(constant=1, up=4, down=.25, off=0)
    arms = [mode+'-'+step for mode in modes for step in steps]
    rows = capture['records']
    files = {(r['arm'], r['frame'], r['tag']): r for r in rows if 'tag' in r}
    states = [r for r in rows if 'state' in r]
    assert set(r['arm'] for r in states) == set(arms)
    assert len(files) == sum('tag' in r for r in rows), 'Duplicate readback key'
    saved_bytes = 0
    for r in files.values():
        assert (r['path'] is not None) == r['saved']
        if r['saved']:
            path = Path(r['path']); assert path.resolve().is_relative_to(root.resolve())
            data = path.read_bytes()
            assert hashlib.sha256(data).hexdigest() == r['sha256']
            saved_bytes += len(data)
    assert saved_bytes <= gates['maximumAdditionalBytes']
    previous = json.loads((static/'capture.json').read_text())
    assert {r['name']:r['sha256'] for r in capture['variants']} == {r['name']:r['sha256'] for r in previous['variants']}
    for r in capture['variants']:
        assert hashlib.sha256(Path(r['path']).read_bytes()).hexdigest() == r['sha256']
    canonical = '8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62'
    geometry = read(files['half128-constant', frames[0], 'geometry'])
    masks = plane_masks(geometry)
    labels = exr_read(Path('E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear/vivid-ev4/object_id.exr'))[::-1]
    reference_vp = next(r['vp'] for r in rows if 'vp' in r)
    assert all(r['vp'] == reference_vp for r in rows if 'vp' in r)
    report = dict(scope='Same-pose uniform emissive source steps, same-frame controls; no physical-truth or GPU-timing claim',
                  inputQualification={}, ages={}, arms={}, pairs={}, gates=[], additionalConformance=[], savedBytes=saved_bytes)
    barriers = []
    for arm in arms:
        mode, step = arm.split('-'); age = 128 if mode == 'half128' else 512
        arm_states = [r for r in states if r['arm'] == arm]
        assert [r['frame'] for r in arm_states] == frames
        for r in arm_states:
            s = r['state']; level = steps[step] if r['frame'] >= change else 1
            assert s['anchorMaxAge'] == age and s['preFilter'] and s['guide'] and not s['restir']
            assert s['samples'] == 64 and s['seed'] == 0 and s['atrous'] == 4
            assert s['sun'] == [0, 0, 0] and s['sky'] == 0 and s['requestedScale'] == level
            assert np.allclose(s['emission'], np.full(3, 200*level), rtol=0, atol=.002)
            if r['frame'] < change or step == 'constant':
                assert s['order'] == 8 and s['batch'] == 32
        resource = [r for r in rows if r.get('arm') == arm and 'resource' in r]
        assert {r['resource'] for r in resource} == {'m_Probes','m_PreviousProbes','m_PreparedAnchor','m_ReconstructedAnchor'}
        expected_format = 'R16G16B16A16_SFloat' if age == 128 else 'R32G32B32A32_SFloat'
        assert all(r['format'] == expected_format for r in resource)
        barrier = [r for r in rows if r.get('arm') == arm and r.get('sourceBarrier')]
        assert len(barrier) >= 8 and all(r['stableFrames'] >= i+1 for i,r in enumerate(barrier[-8:]))
        assert all(abs(r['peak']-200)<.01 and np.allclose(r['cpuEmission'], [200]*3, rtol=0, atol=.01)
                   for r in barrier[-8:])
        assert len(set(r['sha256'] for r in barrier[-8:])) == 1
        barriers.append(barrier[-1]['sha256'])
        for frame in frames:
            assert files[arm,frame,'geometry']['sha256'] == files['half128-constant',frame,'geometry']['sha256']
            if frame < change:
                assert files[arm,frame,'raw']['sha256'] == files['half128-constant',frame,'raw']['sha256']
        assert files[arm,frames[0],'radiosity']['sha256'] == canonical
        if step == 'constant':
            assert files[arm,frames[-1],'radiosity']['sha256'] == canonical
        statistics = read(files[arm,frames[0],'statistics'])[:68,:120,0]
        report['ages'][arm] = {name: np.quantile(statistics[mask],[0,.5,1]).tolist() for name,mask in masks.items()}
        assert all(report['ages'][arm][name][1] >= .95*age for name in masks), 'Long window was not populated'
    assert len(set(barriers)) == 1
    report['inputQualification'] = dict(passed=True, geometryHashPairs=len(frames)*7,
        preStepRawHashPairs=64*7, actualFormats=True, dx12KernelBinaries=4,
        initialSourceHash=canonical, sourceUnchanged=True, directSourceBarrierIdentical=True)
    for mode in modes:
        for step in ['up','down','off']:
            arm, control, scale = mode+'-'+step, mode+'-constant', steps[step]
            source = read(files[control,frames[-1],'radiosity'])[...,:3].astype(np.float64)
            value = read(files[arm,frames[-1],'radiosity'])[...,:3].astype(np.float64)
            delta = value-source*scale
            report['arms'][arm] = dict(sourceScaling=dict(
                normalizedRmsPercent=float(100*np.sqrt(np.mean(delta**2))/max(np.sqrt(np.mean((source*scale)**2)),1e-12)),
                maximumAbsoluteDifference=float(abs(delta).max())), regions={})
            if step == 'off':
                raw = read(files[arm,change,'raw'])
                base = read(files[control,change,'raw'])
                valid = geometry[...,3] >= 0
                report['arms'][arm]['blackCoverage'] = dict(
                    coveredBefore=int(np.count_nonzero(base[...,3][valid]>0)),
                    coveredAfter=int(np.count_nonzero(raw[...,3][valid]>0)),
                    maximumCoverageDifference=float(abs(raw[...,3]-base[...,3])[valid].max()),
                    maxAbsoluteRawRgb=float(abs(raw[...,:3][valid]).max()))
            for region, label in [('left',4),('right',5),('back',3),('floor',1),('ceiling',2)]:
                meta = files[control,frames[0],'final-'+region]
                x,y,w,h = (meta[k] for k in ['x','y','width','height'])
                mask = labels[y:y+h,x:x+w] == label
                assert mask.sum()>8
                report['arms'][arm]['regions'][region] = {}
                for stage in ['resolved','temporal','final']:
                    series = []
                    coverage_minimums, unweighted_rgb_maxima = [], []
                    for which in [control,arm]:
                        values=[]
                        minimum_coverage, maximum_rgb = 1., 0.
                        for frame in frames[64:]:
                            v=read(files[which,frame,stage+'-'+region]).astype(np.float64)
                            values.append(((v[...,:3]*abs(v[...,3:4]))@LUMA)[mask])
                            minimum_coverage = min(minimum_coverage, float(abs(v[...,3][mask]).min()))
                            maximum_rgb = max(maximum_rgb, float(abs(v[...,:3][mask]).max()))
                        series.append(np.asarray(values))
                        coverage_minimums.append(minimum_coverage)
                        unweighted_rgb_maxima.append(maximum_rgb)
                    metrics = response_metrics(*series,scale)
                    metrics.update(minimumCoverage=coverage_minimums[1], controlMinimumCoverage=coverage_minimums[0],
                                   maximumUnweightedAbsoluteRgb=unweighted_rgb_maxima[1])
                    report['arms'][arm]['regions'][region][stage] = metrics
                    if step == 'off':
                        report['additionalConformance'].append(dict(arm=arm,region=region,stage=stage,
                            passed=metrics['maximumUnweightedAbsoluteRgb'] == 0 and metrics['minimumCoverage'] > 0))
    limits = gates['finalStage']
    for step in ['up','down','off']:
        report['pairs'][step]={}
        for region in masks:
            b,c = [report['arms'][mode+'-'+step]['regions'][region]['final'] for mode in modes]
            checks = dict(
                firstFrame=abs(c['first_frame_error_percent']) <= abs(b['first_frame_error_percent'])+limits['firstAbsMeanErrorIncreasePercentagePoints'],
                first32=c['first_32_mean_abs_error_percent'] <= b['first_32_mean_abs_error_percent']+limits['first32MeanAbsIncreasePercentagePoints'],
                overshoot=c['positive_overshoot_percent'] <= b['positive_overshoot_percent']+limits['positiveOvershootIncreasePercentagePoints'],
                tail64=abs(c['tail_64_mean_error_percent']) <= abs(b['tail_64_mean_error_percent'])+limits['tail64AbsMeanIncreasePercentagePoints'])
            if step == 'off': checks['noOldLight']=c['off_max_absolute_y'] == limits['offMaximumAbsoluteY']
            report['pairs'][step][region] = dict(checks=checks,
                firstFramePercent=[b['first_frame_error_percent'],c['first_frame_error_percent']],
                first32MeanAbsPercent=[b['first_32_mean_abs_error_percent'],c['first_32_mean_abs_error_percent']],
                overshootPercent=[b['positive_overshoot_percent'],c['positive_overshoot_percent']],
                tail64Percent=[b['tail_64_mean_error_percent'],c['tail_64_mean_error_percent']])
            report['gates'].append(dict(step=step,region=region,passed=all(checks.values())))
    report['predeclaredResponseGatesPassed']=all(row['passed'] for row in report['gates'])
    report['additionalBlackConformancePassed']=all(row['passed'] for row in report['additionalConformance'])
    report['passed']=report['predeclaredResponseGatesPassed'] and report['additionalBlackConformancePassed']
    (root/'analysis.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    decision=dict(passed=report['passed'],gates=report['gates'],
                  next='GPU-cost and production-path validation remain required; default is unchanged')
    (root/'decision.json').write_text(json.dumps(decision,indent=2),encoding='utf-8')
    plot_response(root, report)
    print(json.dumps(dict(passed=report['passed'],inputQualification=report['inputQualification'],pairs=report['pairs']),indent=2))
    assert report['passed'], 'Uniform-relight regression exceeded the predeclared gate'


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture',type=Path)
    parser.add_argument('--static',type=Path,default=Path('E:/VividRP_GI_Evidence/ScreenProbeAnchorWindowRevisit-20261114-183607-958'))
    args=parser.parse_args(); analyze(args.capture,args.static)
