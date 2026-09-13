import gzip
import json
import sys
from pathlib import Path
import numpy as np

root = Path(__file__).parent
data_root = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'diagnostics'
results = []
for path in sorted(data_root.glob('r*_mode0.gz')):
    prefix = path.name.removesuffix('_mode0.gz')
    arrays = {i: np.frombuffer(gzip.decompress((path.parent / f'{prefix}_mode{i}.gz').read_bytes()), dtype='<f4').reshape(-1, 4)
              for i in (0, 4, 6, 7)}
    levels, work, quality, smrt = (arrays[i] for i in (0, 4, 6, 7))
    valid = levels[:, 0] >= 0
    policy_valid = valid & (quality[:, 0] >= -100) & (quality[:, 1] >= 0)
    item = dict(case=prefix, pixels=int(len(valid)), receivers=int(valid.sum()),
                finite=all(bool(np.isfinite(v).all()) for v in arrays.values()),
                desired_lod_mean=float(quality[policy_valid, 0].mean()),
                coverage_limited_fraction=float((quality[policy_valid, 1] > np.floor(np.clip(quality[policy_valid, 0], 0, 9))).mean()),
                preferred_level_mean=float(levels[valid, 0].mean()), sampled_level_mean=float(levels[valid, 1].mean()),
                sampled_world_texel_mean=float((np.exp2(levels[valid, 1]) * 2 / int(prefix.split('_')[0][1:])).mean()),
                fallback_fraction=float((levels[valid, 1] > levels[valid, 0]).mean()),
                attempted_queries_mean=float(work[valid, 0].mean()),
                combined_depth_reads_mean=float(work[valid, 1].mean()),
                attempted_projections_mean=float(work[valid, 2].mean()),
                rays_mean=float(smrt[valid, 0].mean()),
                footprint_failures_mean=float(smrt[valid, 2].mean()),
                pcf_fallback_fraction=float((smrt[valid, 3] > 0).mean()))
    item['preferred_histogram'] = np.bincount(levels[valid, 0].astype(int), minlength=10).tolist()
    item['sampled_histogram'] = np.bincount(levels[valid & (levels[:, 1] >= 0), 1].astype(int), minlength=10).tolist()
    results.append(item)
(root / 'diagnostics-results.json').write_text(json.dumps(results, indent=2) + '\n')
for item in results:
    print(item['case'], 'receivers', item['receivers'], 'queries', round(item['attempted_queries_mean'], 2),
          'depth', round(item['combined_depth_reads_mean'], 2), 'rays', round(item['rays_mean'], 2),
          'pcf%', round(item['pcf_fallback_fraction'] * 100, 2), 'coverage%', round(item['coverage_limited_fraction'] * 100, 2),
          'fallback%', round(item['fallback_fraction'] * 100, 2))
