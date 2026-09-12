"""Validate and summarize the 18-window Meshlet adaptive4 timing experiment."""
import argparse
import csv
import hashlib
import importlib.util
import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('vsm_stage', HERE / 'analyze-stage-timing.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
MARKERS = base.MARKERS
PARENTS = tuple(x for x in MARKERS if x not in base.PARENTS)
SCENARIOS = ('static', 'camera_slide', 'camera_turn', 'thin_sweep', 'deform',
             'receiver_slide', 'receiver_lift', 'sun_rotate', 'sun_step')
REQUIRED = ('VSM.Resolve', 'VSM.ResolveTrace', 'VSM.FilterHorizontal', 'VSM.FilterTemporalVertical')


def stats(values):
    x = np.asarray(values, dtype=float)
    if not x.size:
        return dict(count=0, mean=None, p50=None, p95=None, p99=None, max=None)
    return dict(count=int(x.size), mean=float(x.mean()), p50=float(np.quantile(x, .5)),
                p95=float(np.quantile(x, .95)), p99=float(np.quantile(x, .99)), max=float(x.max()))


def read_window(folder):
    meta = json.loads((folder / 'window.json').read_text(encoding='utf-8-sig'))
    start = json.loads((folder / 'warm-start.json').read_text(encoding='utf-8-sig'))
    for key, expected in [('warmFrames', 64), ('observationFrames', 128), ('trajectoryFrames', 32),
                          ('observed', 128), ('resolution', 2048), ('capacity', 256), ('depthLayers', 16)]:
        if meta[key] != expected:
            raise ValueError(f'{folder.name}: {key}={meta[key]}, expected {expected}')
    if not meta['historyPresent'] or meta['smrt']['x'] != 4:
        raise ValueError(f'{folder.name}: temporal history/adaptive4 metadata mismatch')
    if meta['lastWarmFrame'] - meta['firstWarmFrame'] != 63 or meta['firstObservedFrame'] != meta['lastWarmFrame'] + 1:
        raise ValueError(f'{folder.name}: warm/capture frame range mismatch')
    if meta['lastObservedFrame'] - meta['firstObservedFrame'] != 127 or meta['metadataFrame'] != meta['lastObservedFrame']:
        raise ValueError(f'{folder.name}: observation frame range mismatch')
    for key in ('scenario', 'order', 'window', 'width', 'height', 'quality', 'smrt', 'aa', 'fixtureBackend'):
        if start[key] != meta[key]:
            raise ValueError(f'{folder.name}: changed {key}')
    settings = json.loads((folder / 'settings.json').read_text(encoding='utf-8-sig'))
    final_settings = json.loads((folder / 'settings-final.json').read_text(encoding='utf-8-sig'))
    if settings != final_settings:
        raise ValueError(f'{folder.name}: settings changed')
    for key in ('virtualShadowMapSMRT', 'virtualShadowMapSMRTAdaptiveRays',
                'screenSpaceShadowDenoise', 'virtualShadowMapSMRTTemporalDenoise'):
        if settings[key]['m_Value'] is not True:
            raise ValueError(f'{folder.name}: {key} disabled')
    rows, stamps = defaultdict(dict), {}
    with (folder / 'stage-timing.csv').open(newline='', encoding='utf-8-sig') as stream:
        for row in csv.DictReader(stream):
            i, marker = int(row['observation']), row['marker']
            stamp = (int(row['camera_frame_observed']), float(row['editor_time_observed']), int(row['vsm_active_observed']))
            if marker not in MARKERS or marker in rows[i] or (i in stamps and stamps[i] != stamp):
                raise ValueError(f'{folder.name}: duplicate/unknown marker or inconsistent stamp')
            rows[i][marker] = (int(row['gpu_ns']), int(row['gpu_sample_count']))
            stamps[i] = stamp
    if set(rows) != set(range(128)) or any(set(row) != set(MARKERS) for row in rows.values()):
        raise ValueError(f'{folder.name}: missing observations/markers')
    rejected, accepted = {}, []
    for i in range(128):
        frame, _, active = stamps[i]
        if frame != meta['firstObservedFrame'] + i:
            raise ValueError(f'{folder.name}: non-consecutive camera frame')
        reasons = []
        if active != 1:
            reasons.append('vsm_inactive')
        for marker, (ns, count) in rows[i].items():
            if ns < 0 or count < 0 or count > (2 if marker == 'VSM.PageCull' else 1):
                reasons.append('invalid_count_or_value:' + marker)
            if count == 0 and ns != 0:
                reasons.append('value_without_sample:' + marker)
        if any(rows[i][marker][1] != 1 for marker in REQUIRED) or rows[i]['VSM.FilterVertical'][1] != 0:
            reasons.append('resolve_filter_counts_not_adaptive_temporal')
        if reasons:
            rejected[str(i)] = reasons
        else:
            accepted.append(i)
    stages = {}
    for marker in MARKERS:
        values = [rows[i][marker][0] / 1e6 for i in accepted if rows[i][marker][1] > 0]
        stages[marker] = {'executed_gpu_ms': stats(values),
                          'sample_count_histogram': dict(Counter(str(rows[i][marker][1]) for i in range(128))),
                          'positive_count_fraction': sum(rows[i][marker][1] > 0 for i in accepted) / len(accepted) if accepted else None,
                          'distinct_recorded_ns': len(set(rows[i][marker][0] for i in accepted if rows[i][marker][1] > 0)),
                          'inclusive_parents': base.PARENTS.get(marker, [])}
    parent_sums, pre_resolve, cull_sums, raster_sums, remainder, trace_fraction = [], [], [], [], [], []
    for i in accepted:
        sample = rows[i]
        def recorded(names):
            return sum(sample[m][0] / 1e6 for m in names if sample[m][1] > 0)
        parent_sums.append(recorded(PARENTS))
        pre_resolve.append(recorded(m for m in PARENTS if m != 'VSM.Resolve'))
        cull_sums.append(recorded(('VSM.StaticCasterCull', 'VSM.DynamicCasterCull')))
        raster_sums.append(recorded(('VSM.StaticRaster', 'VSM.DynamicRaster', 'VSM.UnityCompatibilityRaster')))
        resolve = sample['VSM.Resolve'][0] / 1e6
        remainder.append(resolve - recorded(MARKERS[14:]))
        if resolve > 0:
            trace_fraction.append(sample['VSM.ResolveTrace'][0] / 1e6 / resolve)
    peak_order = sorted(range(len(accepted)), key=lambda j: parent_sums[j], reverse=True)[:5]
    conditioned = {}
    for label, enabled in [('invalidation_recorded', True), ('no_invalidation_sample', False)]:
        selected = [j for j, i in enumerate(accepted) if (rows[i]['VSM.InvalidateStatic'][1] > 0) == enabled]
        conditioned[label] = {'count': len(selected), 'recorded_outer_scope_sum_ms': stats([parent_sums[j] for j in selected]),
                              'static_raster_ms': stats([rows[accepted[j]]['VSM.StaticRaster'][0] / 1e6 for j in selected]),
                              'allocate_ms': stats([rows[accepted[j]]['VSM.Allocate'][0] / 1e6 for j in selected])}
    heavy = [i for i in accepted if rows[i]['VSM.StaticRaster'][1] > 0 and rows[i]['VSM.StaticRaster'][0] > 1_000_000]
    peaks = [{'observation': accepted[j], 'camera_frame_observed': stamps[accepted[j]][0],
              'recorded_outer_scope_sum_ms': parent_sums[j],
              'recorded_stages_ms': {m: rows[accepted[j]][m][0] / 1e6 for m in PARENTS if rows[accepted[j]][m][1] > 0}}
             for j in peak_order]
    result = {'source': str(folder.resolve()), 'metadata': meta, 'observation_count': 128,
              'accepted_observation_count': len(accepted), 'rejected_observations': rejected,
              'observation_span_seconds': stamps[127][1] - stamps[0][1], 'stages': stages,
              'recorded_outer_scope_sum_ms': stats(parent_sums), 'recorded_pre_resolve_sum_ms': stats(pre_resolve),
              'recorded_cull_parent_sum_ms': stats(cull_sums), 'recorded_raster_parent_sum_ms': stats(raster_sums),
              'resolve_minus_paired_children_ms': stats(remainder),
              'negative_remainder_below_minus_0_05_ms': sum(x < -.05 for x in remainder),
              'trace_fraction_of_resolve': stats(trace_fraction), 'outer_scope_peaks': peaks,
              'invalidation_conditioned_observations': conditioned,
              'static_raster_over_1ms_observations': heavy,
              'static_raster_over_1ms_without_invalidation_sample': [i for i in heavy if rows[i]['VSM.InvalidateStatic'][1] == 0],
              'distinct_scope_vectors': len(set(tuple(rows[i][m] for m in MARKERS) for i in accepted)),
              'unexpected_unity_compatibility_observations': sum(rows[i]['VSM.UnityCompatibilityRaster'][1] != 0 for i in range(128))}
    payload = {'stages': {m: [rows[i][m][0] / 1e6 for i in accepted if rows[i][m][1] > 0] for m in MARKERS},
               'outer': parent_sums, 'pre_resolve': pre_resolve, 'cull': cull_sums, 'raster': raster_sums}
    return result, payload


def analyze(capture):
    if (capture / 'status.txt').read_text(encoding='utf-8-sig').strip() != 'complete':
        raise ValueError('Capture is not complete')
    request = json.loads((capture / 'request.json').read_text(encoding='utf-8-sig'))
    expected = [(s, 'forward') for s in SCENARIOS] + [(s, 'reverse') for s in reversed(SCENARIOS)]
    if not request['timingOnly'] or request['modes'] != ['adaptive'] or request['frames'] != 32:
        raise ValueError('Expected timingOnly adaptive4 32-frame trajectory request')
    windows, payloads = {}, {}
    for index, (scenario, order) in enumerate(expected):
        name = f'{scenario}_adaptive_{order}'
        window, payload = read_window(capture / name)
        if (window['metadata']['scenario'], window['metadata']['order'], window['metadata']['window']) != (scenario, order, index):
            raise ValueError(f'{name}: experiment order mismatch')
        windows[name], payloads[name] = window, payload
    scenarios = {}
    for s in SCENARIOS:
        names = [f'{s}_adaptive_{d}' for d in ('forward', 'reverse')]
        pooled = {'stages': {m: stats(sum((payloads[n]['stages'][m] for n in names), [])) for m in MARKERS}}
        for key in ('outer', 'pre_resolve', 'cull', 'raster'):
            pooled[key + '_recorded_sum_ms'] = stats(sum((payloads[n][key] for n in names), []))
        a, b = (windows[n]['recorded_outer_scope_sum_ms']['p50'] for n in names)
        pooled['reverse_vs_forward_outer_p50_fraction'] = b / a - 1 if a else None
        scenarios[s] = pooled
    files = sorted(p for p in capture.rglob('*') if p.is_file())
    rejected = sum(len(w['rejected_observations']) for w in windows.values())
    unexpected = sum(w['unexpected_unity_compatibility_observations'] for w in windows.values())
    result = {'source': str(capture.resolve()), 'status': 'pass' if rejected == unexpected == 0 else 'review-required',
              'windows': windows, 'scenarios_pooled_after_direction_review': scenarios,
              'window_count': 18, 'observations': 2304, 'rejected_observations': rejected,
              'unexpected_unity_compatibility_observations': unexpected,
              'outer_markers': PARENTS, 'nested_excluded': list(base.PARENTS),
              'interpretation': 'Per-observation sum of non-nested recorded scopes, then quantiles; not summed medians, full VSM elapsed time, whole-frame time or a certified GPU/camera critical path. Zero-count optional markers are missing execution measurements; no sample is not a measured zero cost.',
              'attribution': 'ProfilerRecorder last completed global GPU profiling frame. Observation IDs are camera timestamps, not GPU frame IDs; peaks cannot be assigned to the exact pose/reset frame. Direction windows and earlier static experiment remain separate.',
              'fixture': 'Rigid dynamic meshlet transforms; deform is a prebaked registered-renderer sequence, not Skinned streaming. UnityCompatibilityRaster should have no executions.',
              'source_sha256': {str(p.relative_to(capture)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
    return result


def artifacts(result, output):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    result_path = output / 'dynamic-stage-timing.json'
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    fig, axes = plt.subplots(1, 2, figsize=(15.5, 6.6), layout='constrained')
    labels = [s.replace('_', ' ') for s in SCENARIOS]
    for direction, color, offset in [('forward', '#2873a6', -.13), ('reverse', '#cc7832', .13)]:
        cases = [result['windows'][f'{s}_adaptive_{direction}'] for s in SCENARIOS]
        for ax, field, title in [(axes[0], 'recorded_outer_scope_sum_ms', 'Non-nested recorded scopes'),
                                  (axes[1], 'recorded_pre_resolve_sum_ms', 'Recorded scopes before Resolve')]:
            p50 = [c[field]['p50'] for c in cases]; p95 = [c[field]['p95'] for c in cases]
            y = np.arange(len(SCENARIOS)) + offset
            ax.scatter(p50, y, color=color, label=f'{direction}: P50', zorder=3)
            ax.scatter(p95, y, color=color, marker='|', s=120, zorder=3)
            ax.hlines(y, p50, p95, color=color, alpha=.6)
            ax.set_title(title); ax.set_xlabel('ms per observation; dot=P50, bar=P95')
            ax.set_yticks(np.arange(len(SCENARIOS)), labels); ax.set_ylim(len(SCENARIOS) - .5, -.5); ax.grid(axis='x', alpha=.2)
    axes[0].legend(loc='lower right')
    fig.suptitle('Dynamic Meshlet VSM — adaptive4, 18 windows × 128 observations', fontsize=15)
    fig.get_layout_engine().set(rect=(0, .055, 1, .87))
    fig.text(.5, .013, 'Recorded-scope diagnostic, not VSM wall-clock cost. No GPU-frame identity; direction drift retained. Deform = prebaked sequence.', ha='center', fontsize=9)
    fig.savefig(output / 'dynamic-stage-timing.png', dpi=160)
    plt.close(fig)
    def number(x):
        return '—' if x is None else f'{x:.4f}'
    lines = ['# 动态 Meshlet 阶段计时', '', f'来源：`{result["source"]}`。18 个窗口、2304 次观测；解析状态 `{result["status"]}`。', '',
             '每窗口独立预热64帧后观测128次；adaptive4、2048/256页/16层。32帧轨迹按原速度循环并包含复位。以下为毫秒，正反序保持独立；各列不能相加中位数。', '',
             '| 情景 / 顺序 | 外层已记录区间 P50 / P95 | Trace P50 / P95 | Cull父阶段 P50 / P95 | Raster父阶段 P50 / P95 | Invalidate执行 P50 / P95（样本） |',
             '|---|---:|---:|---:|---:|---:|']
    for name, w in result['windows'].items():
        values = [w['recorded_outer_scope_sum_ms'], w['stages']['VSM.ResolveTrace']['executed_gpu_ms'],
                  w['recorded_cull_parent_sum_ms'], w['recorded_raster_parent_sum_ms'], w['stages']['VSM.InvalidateStatic']['executed_gpu_ms']]
        cells = [number(v['p50']) + ' / ' + number(v['p95']) for v in values]
        cells[-1] += f' ({values[-1]["count"]})'
        lines.append('| ' + name + ' | ' + ' | '.join(cells) + ' |')
    lines += ['', f'拒绝观测 {result["rejected_observations"]}；UnityCompatibilityRaster 非零次数观测 {result["unexpected_unity_compatibility_observations"]}。H/TemporalV、每一剔除/raster marker、调用计数直方图、外层最高5次观测及其同时记录的父阶段分解见 JSON。', '',
              '外层合计在每次观测内先排除 PageCull、Trace、H/V 子区间，再对已执行父区间求和，最后求分位数。它是非嵌套已记录GPU区间合计诊断，不是完整VSM墙钟耗时；没有GPU frame身份，也未覆盖scope之间的间隔。可定位同时出现的高成本阶段，不能把峰值指认为某一准确的运动或复位帧。', '',
              'Invalidate 等零计数表示本窗口没有其执行成本样本；表中“—”不替换为零。具有调用但计时值为零的样本保留，并与未调用分开。deform 包含预烘焙renderer注册切换成本，不代表实时Skinned变形。旧暖静态窗口保留在原阶段报告中，不作为本轮混合统计。', '']
    (output / 'dynamic-stage-timing-results.md').write_text('\n'.join(lines), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--output', type=Path, default=HERE)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = analyze(args.capture)
    artifacts(report, args.output)
    print(json.dumps({k: report[k] for k in ['source', 'status', 'window_count', 'observations', 'rejected_observations', 'unexpected_unity_compatibility_observations']}, indent=2))
