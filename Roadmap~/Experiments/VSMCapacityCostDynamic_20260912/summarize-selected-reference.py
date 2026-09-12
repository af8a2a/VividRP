#!/usr/bin/env python3
"""Export compact selected-reference summaries and a reviewable evidence table."""
import argparse
import json
from pathlib import Path

MODES=('fixed4','fixed8','adaptive')


def make_summary(comparison, base_report, threshold):
    scenarios=[s for s in base_report['request']['scenarios'] if s+'_adaptive' in comparison['cases']]
    result={'kind':'selected_reference_quality_summary', 'status':'analyzed_with_stated_coverage_limits',
            'base_source':comparison['base_source'], 'reference_source':comparison['higher_ray_source'],
            'request':dict(base_report['request'],scenarios=scenarios,modes=list(MODES),referenceRays=1024),
            'thresholds':dict(base_report['thresholds'],jump_threshold=threshold),
            'definition':'Original fixed4/fixed8/adaptive outputs from the 256-ray run, each independently matched against the same 1024-ray adaptive-capture reference. Higher-run output is ignored. This compact summary contains only the selected cases and metrics, not uncaptured 1024 camera or receiver-slide results.',
            'cases':{}, 'pairing':{}}
    for name,c in comparison['cases'].items():
        if not c['pairing']['valid'] or not c['floor_samples'] or c['quality_evidence_status'] != 'usable_with_stated_coverage_limits':
            raise ValueError(name+': no complete valid pairing; subset metrics need separate explicit review before final comparison.')
        scenario,mode=name.rsplit('_',1)
        result['pairing'][name]=c['pairing']
        metrics={'scenario':scenario, 'mode':mode,'quality_evidence_status':c['quality_evidence_status'],
                 'floor_receiver_samples':c['floor_samples'],
                 'reference_bias_0_001':c['biases']['normal_bias_0_001']['same_base_signal_error_vs_higher_reference'],
                 'reference_bias_0_01':c['biases']['normal_bias_0_01']['same_base_signal_error_vs_higher_reference'],
                 'bias_sensitive_fraction_floor':c['bias_sensitive_classification']['higher_ray_events']/c['floor_samples'],
                 'dynamic_response':c['thresholds'][str(threshold)]['same_base_signal_higher_reference_response']}
        if 'receiver_top_higher_reference_same_base_signal' in c:
            metrics['moving_receiver_top']=c['receiver_top_higher_reference_same_base_signal']
        result['cases'][name]=metrics
    return result


def number(value,places=6):
    return 'not measured' if value is None else f'{value:.{places}f}'


def markdown(report,comparison):
    rows=['# Selected 1024-ray dynamic reference comparison','',
          'This table supersedes the 256-ray response-tail interpretation for the six selected scenarios. All 18 original production outputs were independently compared with the matching 1024-ray reference capture: complete world/normal arrays are bitwise identical, device-depth maximum delta is zero, and camera/light/fixture pose, phase, geometry hash, and fixture backend match. No unmatched subset was silently treated as a complete pair.', '',
          'The original production signals remain fixed. The higher-ray capture contributes only reference visibility; its own shadow output and history never enter the comparison. The reference remains finite fixed quadrature with .001/.01 m normal-bias sensitivity checks, forced-opaque alpha, and sampled opaque-floor ROI. It is not exact whole-scene visibility truth.', '',
          '## Floor visibility error','',
          '| Scenario | Fixed 4 MAE | Fixed 8 MAE | Adaptive MAE |',
          '|---|---:|---:|---:|']
    for scenario in report['request']['scenarios']:
        values=[report['cases'][scenario+'_'+m]['reference_bias_0_001']['mae'] for m in MODES]
        rows.append('| '+scenario+' | '+' | '.join(number(v) for v in values)+' |')
    rows+=['','MAE and its positive (under-occlusion) / negative (over-occlusion) components use the .001 m reference. Both bias versions and sensitive-sample fractions are retained in `selected-1024-quality-jump01.json` and `selected-1024-three-modes.json`.','',
           '## Response at the common .1 visibility-jump threshold','',
           'Detection is a sustained two-frame crossing of the event midpoint, within delay 0–8. P95/P99 condition on detected events; the undetected denominator is detected plus fully observed, never-detected events. Reference reversal, changed surface, and capture-end censoring are reported separately. Different numbers of detected events must not be read as different geometric-event populations.', '',
           '| Scenario / mode | Detected | Not detected through delay 8 | Undetected fraction | Conditional P95 | Conditional P99 |',
           '|---|---:|---:|---:|---:|---:|']
    for scenario in ('thin_sweep','sun_step'):
        for mode in MODES:
            d=report['cases'][scenario+'_'+mode]['dynamic_response'];c=d['counts'];p=d['first_detection_frames_conditional_on_detection']
            rows.append(f"| {scenario} / {mode} | {c['detected']} | {c['not_detected_within_horizon']} | {100*d['not_detected_fraction']:.4f}% | {p['p95']:g} | {p['p99']:g} |")
    rows+=['','| Scenario / mode | Eligible geometric events | Already on new side | Capture-end censoring | Reference reversal / surface loss |',
           '|---|---:|---:|---:|---:|']
    for scenario in ('thin_sweep','sun_step'):
        for mode in MODES:
            c=report['cases'][scenario+'_'+mode]['dynamic_response']['counts']
            rows.append(f"| {scenario} / {mode} | {c['eligible_events']} | {c['output_already_on_new_side']} | {c['right_censored']} | {c['reference_reversal_or_surface_loss']} |")
    rows+=['',
           'At .5, thin_sweep has 312 eligible events in each mode: 3 were already on the new side, 158 were detected with conditional P95/P99 both zero, and 151 lost the reference-direction/surface condition. There were no fully observed undetected events. Sun_step never reaches .5. This strong-jump result does not establish zero delay for all 312 events or all dynamic behavior.', '',
           'The 1024 reference changes the thin_sweep conclusion from its 256-ray estimate: adaptive P95 becomes 1 rather than 0, and its two apparent fully observed misses disappear. The thin event-set Jaccard is 97.57% at .1. Sun_step is more stable: adaptive P99 remains 3, with 31 fully observed undetected events. Fixed 8 improves conditional P99 to 1 in both measured scenarios, but the miss counts do not support a universal “fewer misses” claim.', '',
           '## Sampled residual beyond the filter envelope','',
           '| Scenario / mode | Eligible event observations | Old-side residual observations | Maximum old-side error |',
           '|---|---:|---:|---:|']
    for scenario in ('thin_sweep','sun_step'):
        for mode in MODES:
            d=report['cases'][scenario+'_'+mode]['dynamic_response']['sampled_support_residual']
            rows.append(f"| {scenario} / {mode} | {d['eligible_event_observations']} | {d['residual_event_observations']} | {number(d['maximum_old_side_error'])} |")
    rows+=['','These are stride-4 sampled event observations, not unique pixels or full-resolution proof. The conservative sampled envelope may miss unsampled geometry, and residuals can include estimator or geometry bias; they are not all established temporal ghosting. Zero counted residuals is limited to that sampled coverage.', '',
           '## Moving receiver and remaining coverage limits','',
           '| Receiver top / mode | Samples | MAE | Positive error | Negative error | Error > .25 | Median history age |',
           '|---|---:|---:|---:|---:|---:|---:|']
    for mode in MODES:
        t=report['cases']['receiver_lift_'+mode]['moving_receiver_top'];e=t['reference_bias_0_001']
        rows.append(f"| {mode} | {t['visible_top_samples']} | {e['mae']:.6f} | {e['positive_visibility_error']:.6f} | {e['negative_visibility_error']:.6f} | {100*t['strong_error_fraction']:.5f}% | {t['history_age']['median']:g} |")
    rows+=['',
           'The receiver-lift top meets the normal contract for 99.983% of height-and-footprint candidates over all 32 frames. Its history-age median drops from 4 while stationary to 1 during motion, then recovers when motion stops. This supports active history rejection on the sampled top; screen-grid captures still do not establish material-point tracking latency.', '',
           'Deform and continuous sun rotation have real reference changes, but neither supplies .1/.5 tracked-floor events; they support along-trajectory error only. The deform backend is a prebaked meshlet frame sequence, not a live skinned or arbitrary vertex-deformation implementation. Camera slide/turn and receiver_slide have no 1024 capture, so their 256-ray per-frame results remain separate and do not inherit this convergence validation. The camera samples also lack motion-corresponded response events.', '',
           '## Reproduce','',
           'From the package root (restore the archived raw captures first if the Temp directories are absent):','',
           '```powershell',
           'python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-reference-convergence.py Temp~/vsm-baseline/dynamic/20260912_075412_506 Temp~/vsm-baseline/dynamic/20260912_075856_113 --all-base-modes --output Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/selected-1024-three-modes.json',
           'python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/summarize-selected-reference.py Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/selected-1024-three-modes.json --base-report Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/final-dynamic-quality-jump01.json --output-directory Roadmap~/Experiments/VSMCapacityCostDynamic_20260912',
           '```','']
    return '\n'.join(rows)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('comparison',type=Path);parser.add_argument('--base-report',required=True,type=Path)
    parser.add_argument('--output-directory',required=True,type=Path);args=parser.parse_args()
    comparison=json.loads(args.comparison.read_text(encoding='utf-8'))
    base=json.loads(args.base_report.read_text(encoding='utf-8'))
    args.output_directory.mkdir(parents=True,exist_ok=True)
    for threshold,suffix in ((.5,'05'),(.1,'01')):
        report=make_summary(comparison,base,threshold)
        (args.output_directory/f'selected-1024-quality-jump{suffix}.json').write_text(json.dumps(report,indent=2,allow_nan=False)+'\n',encoding='utf-8')
    (args.output_directory/'selected-1024-results.md').write_text(markdown(report,comparison),encoding='utf-8')
