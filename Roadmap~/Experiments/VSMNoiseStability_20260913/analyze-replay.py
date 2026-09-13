"""Compare actual GPU histories from identical current-frame spatial inputs."""
import argparse, gzip, json
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('capture', type=Path)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--variants', nargs='+', default=['baseline4','cap8','responsive8','production'])
a = p.parse_args()
variants = a.variants
result = {'source': str(a.capture.resolve()), 'cases': {}}
for folder in sorted(x for x in a.capture.iterdir() if x.is_dir()):
    metas = sorted(folder.glob('frame_*.json'))
    if len(metas) != 128:
        raise ValueError(f'{folder.name}: expected 128 frames, got {len(metas)}')
    data = {v: np.stack([np.frombuffer(gzip.decompress((folder/(m.stem+'_'+v+'.gz')).read_bytes()), '<f4').reshape(180,240,4) for m in metas]) for v in variants}
    assert all(np.isfinite(x).all() for x in data.values())
    base = data['baseline4'][...,0]
    mean = base.mean(axis=0)
    valid = (data['baseline4'][...,1] >= 1).all(axis=0)
    penumbra = valid & (mean > .02) & (mean < .98)
    case = {'frames': len(metas), 'valid_pixels': int(valid.sum()), 'penumbra_pixels': int(penumbra.sum()), 'variants': {}}
    for v, x in data.items():
        signal = x[...,0]
        deviation = signal.std(axis=0, dtype=np.float64)
        delta = np.abs(np.diff(signal, axis=0))
        drift = signal.mean(axis=0)-mean
        case['variants'][v] = {
            'penumbra_mean_temporal_std': float(deviation[penumbra].mean()),
            'penumbra_rms_temporal_std': float(np.sqrt(np.square(deviation[penumbra]).mean())),
            'penumbra_mean_adjacent_delta': float(delta[:,penumbra].mean()),
            'penumbra_mean_drift_abs': float(np.abs(drift[penumbra]).mean()),
            'all_valid_mean_drift_abs': float(np.abs(drift[valid]).mean()),
            'penumbra_mean_history_age': float(x[...,1][:,penumbra].mean()),
            'penumbra_age_one_fraction': float((x[...,1][:,penumbra] <= 1.01).mean()),
            'max_abs_vs_baseline': float(np.abs(signal-base).max()),
        }
    case['baseline_production_different_words'] = int(np.count_nonzero(data['baseline4'].view('<u4') != data['production'].view('<u4')))
    result['cases'][folder.name] = case
    if folder.name == 'static':
        np.savez_compressed(a.output.with_suffix('.npz'), mask=penumbra, **{v: x[...,0] for v,x in data.items()})
a.output.write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
