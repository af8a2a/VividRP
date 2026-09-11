from pathlib import Path
import gzip,json,numpy as np
from scipy.ndimage import gaussian_filter
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
root=sorted(Path('Temp~/public-noise-captures').iterdir())[-1];out=Path('Temp~/public-noise/analysis')
names=['bnd','stbn_scalar','stbn_vec2','fast_scalar','fast_vec2'];masks=np.load('Temp~/stbn-fast/edge-masks.npz')
boxes={'roof':(20,0,380,70),'ledge':(0,295,390,80),'arches':(15,385,360,110)}
def crop(a,n):
 x,y,w,h=boxes[n];return a[...,y:y+h,x:x+w]
def load(d,s,k):
 a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),'<f2').reshape(495,400,-1)[::-1].astype(np.float32)
 return a[...,0] if k=='shadow' else a[...,:3]@np.array([.2126,.7152,.0722],np.float32)
means={};result={}
for name in names:
 d=root/(name+'_static')
 if not (d/'status.txt').exists():continue
 dest=out/(name+'-moments.npz')
 if not dest.exists():
  records={r['step']:r for r in map(json.loads,(d/'frames.jsonl').read_text().splitlines())}
  keys=[tuple(records[s]['jitter'].values()) for s in range(32,288)];unique=sorted(set(keys)); sums={};square={};full={}
  for k in ('shadow','output'):
   arr=np.stack([load(d,s,k) for s in range(32,288)])
   sums[k]=np.stack([arr[[i for i,x in enumerate(keys) if x==j]].mean(0) for j in unique])
   square[k]=np.stack([arr[[i for i,x in enumerate(keys) if x==j]].var(0) for j in unique])
   full[k]=arr.mean(0)
  np.savez_compressed(dest,**{k+'_phase_mean':v for k,v in sums.items()},**{k+'_phase_variance':v for k,v in square.items()},**{k+'_mean':v for k,v in full.items()})
 means[name]=dict(np.load(dest))
 if name=='bnd':continue
 result[name]={}
 for k in ('shadow','output'):
  delta=means[name][k+'_phase_mean']-means['bnd'][k+'_phase_mean']
  result[name][k]={n:{'mean_delta':float(crop(delta,n)[:,masks[n]].mean()),'phase_mean_difference_rms':float(np.sqrt((crop(delta,n)[:,masks[n]]**2).mean())),'gaussian_phase_mean_difference_rms':float(np.sqrt((crop(gaussian_filter(delta,(0,1,1)),n)[:,masks[n]]**2).mean()))} for n in boxes}
(out/'spatial-differences.json').write_text(json.dumps({'note':'Differences to BND finite-period means are not ground-truth errors. Periods are BND256, STBN64, FAST32; same-jitter means retain sampler-period bias.', 'results':result},indent=2))
fig,axes=plt.subplots(2,len(means),figsize=(3*len(means),5.2),squeeze=False)
for i,(n,m) in enumerate(means.items()):
 # Scientific displays of raw visibility moments, not final-color screenshot edits.
 axes[0,i].imshow(crop(m['shadow_mean'],'ledge'),vmin=0,vmax=1,cmap='gray',interpolation='nearest');axes[0,i].set_title(n+' mean visibility')
 axes[1,i].imshow(crop(np.sqrt(m['shadow_phase_variance'].mean(0)),'ledge'),vmin=0,vmax=.2,cmap='magma',interpolation='nearest');axes[1,i].set_title('Temporal RMS (0 to .2)')
 for ax in axes[:,i]:ax.axis('off')
fig.tight_layout();fig.savefig(out/'visibility-moments.png',dpi=160)
print('Moments:',list(means))
