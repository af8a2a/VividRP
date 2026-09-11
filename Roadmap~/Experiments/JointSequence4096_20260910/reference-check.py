from pathlib import Path
import gzip,json,sys,numpy as np
root=Path(sys.argv[1]);out=Path('Temp~/joint-sequence/analysis')
ref=Path('Temp~/stbn-reference-captures/20260909_154308_096/bnd_static')
def records(p):return {r['step']:r for r in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}
br=records(root/'bnd_static');rr=records(ref)
def load(d,s,k):return np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),'<f2').astype(np.float32)
fields=['jitter','cameraPosition','cameraEuler','gpuVP','angle','rays','steps','capacity','resolution','depthBias','slopeBias']
audit={'reference':str(ref),'parameter_matches':{k:all(rr[s][k]==br[s][k] for s in rr) for k in fields},
       'first_frame_max_delta':float(np.abs(load(root/'bnd_static',0,'shadow')-load(ref,0,'shadow')).max()),
       'settled_baseline_max_delta':{f'{new}/{old}':float(np.abs(load(root/'bnd_static',new,'shadow')-load(ref,old,'shadow')).max()) for new,old in [(256,0),(287,287)]},
       'note':'First-frame differences are retained and reported. Reuse requires exact settled raw-shadow reproduction at matching 256-frame BND phases.'}
audit['valid']=all(audit['parameter_matches'].values()) and max(audit['settled_baseline_max_delta'].values())==0
(out/'reference-reuse-check.json').write_text(json.dumps(audit,indent=2))
print(audit,flush=True)
assert audit['valid'],'Reference cannot be reused; investigate geometry/depth changes first.'
data=np.load('Roadmap~/Experiments/PublicBlueNoise4096_20260910/reference.npz');mean=data['mean'];mc=data['half_difference'];jitters=data['jitters']
masks=np.load('Temp~/joint-sequence/edge-masks.npz');boxes={'roof':(20,0,380,70),'ledge':(0,295,390,80),'arches':(15,385,360,110)}
def mask(a,name):
    x,y,w,h=boxes[name];return a[...,y:y+h,x:x+w][...,masks[name]]
def rms(a):return float(np.sqrt(np.mean(a*a)))
result={'reference':'1024 independent four-dimensional phase estimates per jitter; same 4x8 estimator, not geometry truth',
        'uncertainty':{n:rms(mask(mc,n)) for n in boxes},'variants':{}}
for p in out.glob('*-moments.npz'):
    name=p.name.removesuffix('-moments.npz');d=np.load(p);assert np.array_equal(d['jitters'],jitters)
    delta=d['shadow_phase_mean']-mean;var=d['shadow_phase_variance']
    result['variants'][name]={n:{'fixed_rms':rms(mask(delta,n)),'raw_single_frame_rms':float(np.sqrt(np.mean(mask(var,n)+mask(delta,n)**2))),'mean_error':float(mask(delta,n).mean())} for n in boxes}
(out/'reference-comparison.json').write_text(json.dumps(result,indent=2))
