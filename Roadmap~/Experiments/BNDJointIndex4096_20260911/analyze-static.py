from pathlib import Path
import json,gzip,sys,numpy as np
root=Path(sys.argv[1]);out=Path(sys.argv[2]) if len(sys.argv)>2 else Path("Temp~/bnd-joint/analysis");out.mkdir(exist_ok=True)
ref=np.load("Roadmap~/Experiments/JointSequence4096_20260910/reference.npz");masks=np.load(Path(__file__).resolve().parent/"edge-masks.npz");boxes={"roof":(20,0,380,70),"ledge":(0,295,390,80),"arches":(15,385,360,110)}
fields=['width','height','resolution','capacity','cameraPosition','cameraEuler','jitter','gpuVP','lightIntensity','lightEuler','rays','steps','firstLevel','pcf','screenDensity','targetTexelPixels','resolutionLodBias','transition','depthBias','slopeBias','exposureMode','exposureManualEV100','exposureFixedScale','historySampleCount','jitterPhaseCount','tsrQuality','shaderFrameSeed','angle']
def records(p):return {r['step']:r for r in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}
def load(p,s,k):
 a=np.frombuffer(gzip.decompress((p/f"frame_{s:03}_{k}.bin.gz").read_bytes()),'<f2').reshape(495,400,-1)[::-1].astype(np.float32)
 return a[...,0] if k=='shadow' else a[...,:3]@np.array([.2126,.7152,.0722],np.float32)
def mask(a,n):
 x,y,w,h=boxes[n];return a[...,y:y+h,x:x+w][...,masks[n]]
def rms(a):return float(np.sqrt(np.mean(a*a)))
base=records(root/'bnd_native_static') if (root/'bnd_native_static/status.txt').exists() else None
result={'capture':str(root),'window':[32,2079],'reference_uncertainty':{n:rms(mask(ref['half_difference'],n)) for n in boxes},'stages':{}}
for variant in ['bnd_native','bnd_joint','stbn_joint']:
 d=root/(variant+'_static');cache=out/(variant+'.json')
 if not (d/'status.txt').exists():continue
 if cache.exists():result['stages'][variant]=json.loads(cache.read_text());continue
 rec=records(d);assert set(rec)==set(range(2080));keys=sorted({tuple(r['jitter'].values()) for r in rec.values()});assert np.array_equal(np.array(keys),ref['jitters'])
 mean={k:np.zeros((8,495,400),np.float64) for k in ['shadow','output']};m2={k:np.zeros_like(v) for k,v in mean.items()};counts=np.zeros(8,np.int64);prefix={}
 for s in range(32,2080):
  j=keys.index(tuple(rec[s]['jitter'].values()));counts[j]+=1
  for kind in mean:
   a=load(d,s,kind);delta=a-mean[kind][j];mean[kind][j]+=delta/counts[j];m2[kind][j]+=delta*(a-mean[kind][j])
  length=s-31
  if length in [32,64,128,256,512,1024,2048]:prefix[str(length)]={n:rms(mask(mean['shadow']-ref['mean'],n)) for n in boxes}
 assert np.all(counts==256)
 c=np.stack([np.frombuffer(gzip.decompress((d/f"frame_{s:03}_counters.bin.gz").read_bytes()),'<u4')[:4] for s in rec]);checks={k:all(rec[s][k]==base[s][k] for s in rec) for k in fields}
 item={'frames':len(rec),'all_active':all(r['state']=='Active' and r['settingsMatch'] for r in rec.values()),'parameter_matches':checks,'overflow_max':int(c[:,3].max()),'missing_max':max(r['replayMissingPixels'] for r in rec.values()),'replay_error_max':max(r['replayMaxError'] for r in rec.values()),'distinct_indices_per_jitter':[len({rec[s]['atlasSlice'] for s in range(32,2080) if tuple(rec[s]['jitter'].values())==key}) for key in keys],'regions':{},'prefix_fixed_rms':prefix}
 for n in boxes:
  delta=mask(mean['shadow']-ref['mean'],n);var=mask(m2['shadow']/counts[:,None,None],n)
  item['regions'][n]={'fixed_rms':rms(delta),'raw_single_frame_rms':float(np.sqrt(np.mean(var+delta*delta))),'raw_temporal_rms':float(np.sqrt(np.mean(var))),'output_temporal_rms':float(np.sqrt(np.mean(mask(m2['output']/counts[:,None,None],n))))}
 np.savez_compressed(out/(variant+'-moments.npz'),jitters=np.array(keys),**{k+'_mean':v.astype(np.float32) for k,v in mean.items()},**{k+'_var':(v/counts[:,None,None]).astype(np.float32) for k,v in m2.items()})
 cache.write_text(json.dumps(item,indent=2));result['stages'][variant]=item;print(variant,item['regions'],flush=True)
if 'bnd_native' in result['stages']:
 result['change_percent']={v:{n:{k:100*(a/result['stages']['bnd_native']['regions'][n][k]-1) for k,a in vals.items()} for n,vals in item['regions'].items()} for v,item in result['stages'].items()}
(out/'summary.json').write_text(json.dumps(result,indent=2));print('complete',len(result['stages']),flush=True)
