from pathlib import Path
import gzip,json,numpy as np
from scipy.ndimage import gaussian_filter
root=sorted(Path('Temp~/public-noise-captures').iterdir())[-1];ref=sorted(Path('Temp~/stbn-reference-captures').iterdir())[-1]/'bnd_static';out=Path('Temp~/public-noise/analysis')
def records(d):return {r['step']:r for r in map(json.loads,(d/'frames.jsonl').read_text().splitlines())}
rr=records(ref); br=records(root/'bnd_static');keys=[tuple(rr[s]['jitter'].values()) for s in range(8)];order=np.argsort(np.array(keys)[:,0],kind='stable')
order=np.array(sorted(range(8),key=lambda i:keys[i]))
halves=np.frombuffer(gzip.decompress((ref/'reference.bin.gz').read_bytes()),'<f4').reshape(2,8,495,400)[:,:,::-1].copy()/16
halves=halves[:,order];mean=halves.mean(0);mc_error=(halves[0]-halves[1])*.5
assert mean.min()>=0 and mean.max()<=1.00001
np.savez_compressed(out/'reference.npz',mean=mean,half_difference=mc_error,jitters=np.array(keys)[order])
masks=np.load('Temp~/stbn-fast/edge-masks.npz');boxes={'roof':(20,0,380,70),'ledge':(0,295,390,80),'arches':(15,385,360,110)}
def crop(a,n):
 x,y,w,h=boxes[n];return a[...,y:y+h,x:x+w]
def masked(a,n):return crop(a,n)[:,masks[n]]
def rms(a):return float(np.sqrt(np.mean(a*a)))
def load(d,s,k):
 a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),'<f2').reshape(495,400,-1)[::-1].astype(np.float32)
 return a[...,0] if k=='shadow' else a
result={'reference':str(ref),'reference_estimates_per_jitter':1024,'rays_per_estimate':4,'steps_per_ray':8,
 'reference_mc_uncertainty_rms':{n:rms(masked(mc_error,n)) for n in boxes},
 'geometry_matches':{k:all(rr[s][k]==br[s][k] for s in rr) for k in ['jitter','cameraPosition','cameraEuler','gpuVP','angle','rays','steps','capacity','resolution','depthBias','slopeBias']},
 'repeat_baseline_max_delta':{k:{s:float(abs(load(ref,s,k)-load(root/'bnd_static',s,k)).max()) for s in [0,287]} for k in ['shadow','source']},'samplers':{}}
for name in ['bnd','stbn_scalar','stbn_vec2','fast_scalar','fast_vec2']:
 data=np.load(out/(name+'-moments.npz'));delta=data['shadow_phase_mean']-mean;variance=data['shadow_phase_variance']
 # Reference integration uncertainty is reported separately, not silently removed.
 result['samplers'][name]={n:{'fixed_phase_mean_error_rms':rms(masked(delta,n)),
  'single_frame_error_rms':float(np.sqrt(np.mean(masked(variance,n)+masked(delta,n)**2))),
  'temporal_rms':float(np.sqrt(masked(variance,n).mean())),
  'gaussian_fixed_phase_mean_error_rms':rms(masked(gaussian_filter(delta,(0,1,1)),n)),
  'mean_visibility_error':float(masked(delta,n).mean())} for n in boxes}
(out/'reference-comparison.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
