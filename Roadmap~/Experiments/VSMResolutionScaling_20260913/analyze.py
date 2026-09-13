import csv
import json
import re
import sys
from pathlib import Path
import numpy as np

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
windows = []
for path in sorted((root / 'raw').glob('*.csv')):
    match = re.fullmatch(r'(\d+)_r(\d+)_p(\d+)', path.stem)
    stage, resolution, capacity = map(int, match.groups())
    rows = list(csv.DictReader(path.open()))
    markers = {}
    for name in sorted({row['marker'] for row in rows}):
        selected = [row for row in rows if row['marker'] == name]
        times = np.array([int(row['gpu_ns']) / 1e6 for row in selected])
        counts = np.array([int(row['gpu_sample_count']) for row in selected])
        markers[name] = dict(p50_ms=float(np.median(times)), p95_ms=float(np.percentile(times, 95)),
                             mean_ms=float(times.mean()), observations=len(times),
                             counts=np.unique(counts).tolist(), zero_observations=int((counts == 0).sum()))
    metadata = np.fromfile(path.with_name(path.stem + '-metadata.bin'), dtype='<u4').reshape(-1, 4)
    per_level = (resolution // 128) ** 2
    levels = []
    for index, meta in enumerate(metadata.reshape(-1, per_level, 4)):
        allocated = (meta[:, 0] & 2) != 0
        flags = meta[:, 3]
        requested = (flags & 1) != 0
        levels.append(dict(level=index, resident=int(allocated.sum()), requested=int(requested.sum()),
                           requested_resident=int((requested & allocated).sum()),
                           primary=int(((flags & 512) != 0).sum()),
                           parent=int(((flags & 2048) != 0).sum())))
    counters = list(map(int, path.with_name(path.stem + '-counters.txt').read_text().split(',')))
    windows.append(dict(stage=stage, resolution=resolution, capacity=capacity,
                        table_entries=len(metadata), counters=dict(zip(['allocated', 'requested', 'new', 'overflow'], counters)),
                        levels=levels, markers=markers))

result = dict(status=(root / 'raw/status.txt').read_text() if (root / 'raw/status.txt').exists() else 'running', windows=windows)
before = root / 'raw/settings-before.json'
after = root / 'raw/settings-after.json'
if before.exists() and after.exists():
    a, b = json.loads(before.read_text()), json.loads(after.read_text())
    result['restoration'] = dict(serialized_settings_equal=a == b,
                                effective_values_equal=all(v.get('m_Value') == b[k].get('m_Value') if isinstance(v, dict) else v == b[k] for k, v in a.items()),
                                differences={k: [a[k], b[k]] for k in a if a[k] != b[k]},
                                camera_equal=(root / 'raw/camera-before.json').read_text() == (root / 'raw/camera-after.json').read_text(),
                                transform_equal=(root / 'raw/camera-transform-before.json').read_text() == (root / 'raw/camera-transform-after.json').read_text(),
                                scenes_equal=(root / 'raw/scenes-before.txt').read_text() == (root / 'raw/scenes-after.txt').read_text())
(root / 'results.json').write_text(json.dumps(result, indent=2) + '\n')
print('stage R pages resolve trace allocate mark pagecull staticraster new/overflow')
for w in windows:
    names = ['VSM.Resolve', 'VSM.ResolveTrace', 'VSM.Allocate', 'VSM.MarkReceiverPages', 'VSM.PageCull', 'VSM.StaticRaster']
    print(w['stage'], w['resolution'], w['capacity'], *(round(w['markers'][name]['p50_ms'], 4) for name in names), w['counters'])
if 'restoration' in result:
    print(json.dumps(result['restoration'], ensure_ascii=False))
print(result['status'])
