"""Static production-path sampling A/B. 256 consecutive, phase-matched frames."""
import argparse, gzip, json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('capture',type=Path)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--variants',nargs='+',default=['golden','uniform'])
a=p.parse_args()
data={}
for variant in a.variants:
    files=sorted((a.capture/variant).glob('frame_*.json'))
    assert len(files)==256
    meta=[json.loads(x.read_text(encoding='utf-8-sig')) for x in files]
    assert [x['phase'] for x in meta]==list(range(256))
    data[variant]=np.stack([np.frombuffer(gzip.decompress(x.with_suffix('.gz').read_bytes()),'<f4').reshape(180,240,4) for x in files])
    assert np.isfinite(data[variant]).all()
    if variant==a.variants[0]: before=meta
    else:
        assert all(all(x[k]==y[k] for k in ['phase','step','width','height','vp','cameraPosition']) for x,y in zip(before,meta))
base=data[a.variants[0]];mean=base[...,0].mean(0);valid=(base[...,1]>=1).all(0)
normal=base[0,...,2:]*2-1
z=1-np.abs(normal).sum(-1)
t=np.maximum(-z,0)
xy=normal+np.where(normal>=0,-t[...,None],t[...,None])
n=np.concatenate([xy,z[...,None]],axis=-1);n/=np.linalg.norm(n,axis=-1)[...,None]
masks={'penumbra':valid&(mean>.02)&(mean<.98),'floor_penumbra':valid&(mean>.02)&(mean<.98)&(n[...,1]>.95),'fully_shadowed':valid&(mean<.0001)}
result={'source':str(a.capture.resolve()),'frames_per_variant':256,'phase_and_camera_match':True,'regions':{}}
for region,mask in masks.items():
    if not mask.any():continue
    stats={}
    for variant,x in data.items():
        signal=x[...,0];drift=signal.mean(0)-mean;std=signal.std(0,dtype=np.float64)
        stats[variant]={'mean_temporal_std':float(std[mask].mean()),'rms_temporal_std':float(np.sqrt(np.mean(std[mask]**2))),
            'mean_adjacent_delta':float(np.abs(np.diff(signal,axis=0))[:,mask].mean()),'mean_drift_abs':float(np.abs(drift[mask]).mean()),
            'p95_drift_abs':float(np.percentile(np.abs(drift[mask]),95)),'mean_visibility':float(signal[:,mask].mean()),
            'mean_history_age':float(x[...,1][:,mask].mean())}
    result['regions'][region]={'pixels':int(mask.sum()),'variants':stats}
a.output.write_text(json.dumps(result,indent=2),encoding='utf-8')
np.savez_compressed(a.output.with_suffix('.npz'),**{v:x[...,0] for v,x in data.items()},mask=masks['penumbra'])
fig,axes=plt.subplots(len(data),3,figsize=(12,3.25*len(data)),constrained_layout=True)
for row,(v,x) in enumerate(data.items()):
    sig=x[...,0];axes[row,0].imshow(sig.mean(0),cmap='gray',vmin=0,vmax=1,origin='lower');axes[row,0].set_title(v+' mean visibility')
    axes[row,1].imshow(sig.std(0),cmap='magma',vmin=0,vmax=.06,origin='lower');axes[row,1].set_title('Temporal standard deviation (0 to 0.06)')
    axes[row,2].imshow(np.abs(np.diff(sig,axis=0)).mean(0),cmap='magma',vmin=0,vmax=.04,origin='lower');axes[row,2].set_title('Mean adjacent difference (0 to 0.04)')
for ax in axes.flat:ax.axis('off')
fig.savefig(a.output.with_suffix('.png'),dpi=150)
print(json.dumps(result,indent=2))
