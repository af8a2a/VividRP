from pathlib import Path
import json,numpy as np
from scipy.ndimage import gaussian_filter
root=Path.cwd()/'Temp~/public-noise'  # Run from the package root after extracting the public atlases.
manifest=json.loads((root/'texture-provenance.json').read_text());result={}
offsets=(128*np.mod(np.arange(4)[:,None]*[.754877666,.569840296],1)).astype(int)
for name,meta in manifest['textures'].items():
 c=meta['channels'];d=meta['depth'];a=np.frombuffer((root/(name+'.raw')).read_bytes(),np.uint8).reshape(d,128,128,c).astype(np.float32)/255
 phases=[]
 for dim in range(4):
  x,y=offsets[dim if c==1 else dim//2]
  phases.append(np.roll(a[...,0 if c==1 else dim%2],(-y,-x),(1,2)))
 phases=np.stack(phases,axis=-1);f=(phases[...,0]<.5).astype(np.float32)-.5
 history=np.zeros_like(f[0]);ema=[]
 for i in range(4*d):
  history=.9*history+.1*f[i%d]
  if i>=3*d:ema.append(history.copy())
 ema=np.stack(ema)
 phase_mean=np.stack([f[i::8].mean(0) for i in range(8)])
 r={'depth':d,'distinct_slices_per_8_phase_jitter':d//8,'channels':c,'scalar_offsets':offsets.tolist() if c==1 else offsets[:2].tolist(),
  'range':[float(a.min()),float(a.max())],'channel_means':a.mean((0,1,2)).tolist(),'channel_variances':a.var((0,1,2)).tolist(),
  'phase_correlation':np.corrcoef(phases.reshape(-1,4).T).tolist(),
  'threshold_ema_rms':float(np.sqrt((ema**2).mean())),
  'threshold_gauss_ema_rms':float(np.sqrt((gaussian_filter(ema,(0,1,1),mode='wrap')**2).mean())),
  'threshold_same_jitter_cycle_mean_rms':float(np.sqrt((phase_mean**2).mean()))}
 result[name]=r
(root/'source-audit.json').write_text(json.dumps({'note':'CPU audit of source samples and a simple threshold integrand; not a render quality metric. Native slice counts preserved.', 'variants':result},indent=2))
print({n:{k:v for k,v in r.items() if k in ['channel_means','threshold_ema_rms','threshold_same_jitter_cycle_mean_rms']} for n,r in result.items()})
