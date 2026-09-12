#!/usr/bin/env python3
"""Summarize raw VSMStageTimingCapture CSV without summing stage medians."""
import argparse
import csv
import json
import statistics
from collections import defaultdict
from pathlib import Path

MARKERS = (
    'VSM.LayoutRemap', 'VSM.ResetFeedback', 'VSM.MarkReceiverPages',
    'VSM.Allocate', 'VSM.InvalidateStatic', 'VSM.ClearPhysicalPages',
    'VSM.StaticCasterCull', 'VSM.DynamicCasterCull', 'VSM.PageCull',
    'VSM.StaticRaster', 'VSM.FinalizePages', 'VSM.DynamicRaster',
    'VSM.UnityCompatibilityRaster', 'VSM.Resolve', 'VSM.ResolveTrace',
    'VSM.FilterHorizontal', 'VSM.FilterVertical', 'VSM.FilterTemporalVertical',
)
PARENTS = {
    'VSM.PageCull': ['VSM.StaticCasterCull', 'VSM.DynamicCasterCull'],
    **{name: ['VSM.Resolve'] for name in MARKERS[14:]},
}


def distribution(values):
    if not values:
        return {'count': 0, 'median': None, 'mean': None, 'p10': None, 'p90': None}
    ordered = sorted(values)
    def quantile(fraction):
        index = fraction * (len(ordered) - 1)
        low = int(index)
        high = min(low + 1, len(ordered) - 1)
        return ordered[low] + (ordered[high] - ordered[low]) * (index - low)
    return {'count': len(values), 'median': statistics.median(values),
            'mean': statistics.fmean(values), 'p10': quantile(.1), 'p90': quantile(.9)}


def analyze(path):
    observations = defaultdict(dict)
    identity = {}
    with Path(path).open(newline='', encoding='utf-8-sig') as stream:
        for row in csv.DictReader(stream):
            index = int(row['observation'])
            marker = row['marker']
            if marker not in MARKERS or marker in observations[index]:
                raise ValueError(f'{path}: unknown or repeated marker at observation {index}: {marker}')
            stamp = (int(row['camera_frame_observed']), float(row['editor_time_observed']),
                     int(row['vsm_active_observed']))
            if index in identity and identity[index] != stamp:
                raise ValueError(f'{path}: inconsistent observation identity: {index}')
            identity[index] = stamp
            value, count = int(row['gpu_ns']), int(row['gpu_sample_count'])
            if value < 0 or count < 0:
                raise ValueError(f'{path}: negative GPU value/count: {index}/{marker}')
            observations[index][marker] = (value * 1e-6, count)
    invalid = defaultdict(list)
    stages = {marker: {'values': [], 'count_histogram': defaultdict(int)} for marker in MARKERS}
    resolve_remainder = []
    accepted = []
    previous_frame = None
    for index in sorted(observations):
        samples = observations[index]
        if set(samples) != set(MARKERS):
            raise ValueError(f'{path}: incomplete marker set at observation {index}')
        frame, _, active = identity[index]
        non_increasing = previous_frame is not None and frame <= previous_frame
        previous_frame = frame
        for marker, (_, count) in samples.items():
            stages[marker]['count_histogram'][str(count)] += 1
        if non_increasing:
            invalid['non_increasing_observed_frame'].append(index)
            continue
        if active != 1:
            invalid['vsm_inactive_when_observed'].append(index)
            continue
        if samples['VSM.Resolve'][1] != 1 or samples['VSM.ResolveTrace'][1] != 1:
            invalid['resolve_or_trace_count_not_one'].append(index)
            continue
        children = MARKERS[14:]
        h, v, t = (samples[name][1] for name in MARKERS[15:])
        if h not in (0, 1) or v not in (0, 1) or t not in (0, 1) or v + t != h:
            invalid['filter_execution_count_inconsistent'].append(index)
            continue
        accepted.append(index)
        for marker, (value, count) in samples.items():
            # Multiple PageCull executions are intentionally aggregated under two
            # inclusive caster-cull parents. Other multiple counts are excluded.
            if count == 1 or (marker == 'VSM.PageCull' and count == 2):
                stages[marker]['values'].append(value)
        # Paired samples only. This is a hierarchy consistency diagnostic, not a
        # constructed VSM/frame total, and never a difference/sum of medians.
        resolve_remainder.append(samples['VSM.Resolve'][0] - sum(
            samples[name][0] for name in children if samples[name][1] == 1))
    result = {
        'source': str(Path(path).resolve()), 'observation_count': len(observations),
        'accepted_resolve_observation_count': len(accepted),
        'rejected_observations': dict(invalid), 'stages': {},
        'resolve_minus_paired_children_ms': distribution(resolve_remainder),
        'paired_remainder_below_minus_0_05_ms': sum(value < -.05 for value in resolve_remainder),
        'attribution': 'Last completed global profiling-frame values. Camera frame/time label observation only; count one is necessary, not sufficient, for selected-camera attribution.',
        'aggregation': 'No parent/child or stage medians are added. Zero-count stages are missing measurements. PageCull can have two executions and is nested in both caster-cull parents.',
    }
    for marker, stage in stages.items():
        result['stages'][marker] = {
            'gpu_ms': distribution(stage['values']),
            'sample_count_histogram_all_observations': dict(stage['count_histogram']),
            'inclusive_parents': PARENTS.get(marker, []),
        }
    return result


def self_test():
    import tempfile
    fields = ('observation', 'camera_frame_observed', 'editor_time_observed',
              'vsm_active_observed', 'marker', 'gpu_ns', 'gpu_sample_count')
    with tempfile.TemporaryDirectory(prefix='vsm-stage-parser-') as directory:
        path = Path(directory) / 'fixture.csv'
        with path.open('w', newline='', encoding='utf-8') as stream:
            writer = csv.writer(stream)
            writer.writerow(fields)
            for index in range(4):
                for marker in MARKERS:
                    count = 1
                    value = 1_000_000
                    if marker == 'VSM.Resolve':
                        count, value = (2 if index == 2 else 1), 4_000_000
                    elif marker == 'VSM.PageCull':
                        count, value = 2, 400_000
                    elif marker == 'VSM.FilterVertical':
                        count, value = 0, 0
                    elif marker == 'VSM.InvalidateStatic':
                        count, value = 0, 0
                    writer.writerow((index, 100 + index, index / 60, int(index != 3), marker, value, count))
        report = analyze(path)
        assert report['accepted_resolve_observation_count'] == 2
        assert report['stages']['VSM.Resolve']['gpu_ms']['median'] == 4
        assert report['stages']['VSM.PageCull']['gpu_ms']['count'] == 2
        assert report['stages']['VSM.InvalidateStatic']['gpu_ms']['median'] is None
        assert report['resolve_minus_paired_children_ms']['median'] == 1
        assert report['rejected_observations']['resolve_or_trace_count_not_one'] == [2]
        assert report['rejected_observations']['vsm_inactive_when_observed'] == [3]
        rewound = Path(directory) / 'rewound.csv'
        rewound.write_text(path.read_text(encoding='utf-8').replace('1,101,', '1,100,'), encoding='utf-8')
        assert analyze(rewound)['rejected_observations']['non_increasing_observed_frame'] == [1]
        with path.open('a', encoding='utf-8') as stream:
            stream.write('0,100,0,1,VSM.Resolve,4000000,1\n')
        try:
            analyze(path)
        except ValueError:
            pass
        else:
            raise AssertionError('Duplicate marker accepted')
    print('stage timing parser: 9 focused checks passed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('csv', nargs='*', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    if args.csv:
        output = json.dumps([analyze(path) for path in args.csv], indent=2, ensure_ascii=False)
        if args.output:
            args.output.write_text(output + '\n', encoding='utf-8')
        else:
            print(output)
    elif not args.self_test:
        parser.error('provide CSV inputs or --self-test')