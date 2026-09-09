from pathlib import Path
import gzip,json,sys,numpy as np
from scipy.ndimage import gaussian_filter
from types import SimpleNamespace
t=SimpleNamespace(OX=715,OY=190,W=400,H=495,
 REGIONS={'roof':(735,190,380,70),'ledge':(715,485,390,80),'arches':(730,575,360,110)})
def records(d):
 return {r['step']:r for r in map(json.loads,(d/'frames.jsonl').read_text().splitlines())}
def crop(a,box):
 x,y,w,h=box;return a[y-t.OY:y-t.OY+h,x-t.OX:x-t.OX+w]
t.records=records;t.crop=crop
t.luma=lambda a:a[...,:3]@np.array([.2126,.7152,.0722],np.float32)
t.rms=lambda a:float(np.sqrt(np.mean(a*a)))
t.counts=lambda a:{k:a.count(k) for k in sorted(set(a))}
root=Path(sys.argv[1]);out=Path(sys.argv[2]) if len(sys.argv)>2 else Path('Temp~/tsr-clamp-compare/analysis');out.mkdir(exist_ok=True)
masks=np.load(Path(__file__).resolve().parent/'edge-masks.npz')
result={'capture':str(root),'regions':t.REGIONS,'stages':{},'pairs':{}}
def load(d,s,k):
 if k=='counters':return np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),dtype='<u4').copy()
 c=1 if k in ('shadow','accept') else 2 if k=='meta' else 4
 a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),dtype='<f2')
 a=a.reshape(t.H,t.W,c)[::-1].astype(np.float32)
 return a[...,0] if c==1 else a
def cropm(a,n):return t.crop(a,t.REGIONS[n])[masks[n]]
def flat(a):return {n:cropm(a,n) for n in t.REGIONS}
for d in sorted(root.iterdir()):
 if not d.is_dir() or d.name.startswith('priming') or not (d/'status.txt').exists():continue
 cachefile=out/(d.name+'.json')
 if cachefile.exists():result['stages'][d.name]=json.loads(cachefile.read_text());continue
 rec=t.records(d); assert set(rec)==set(range(288)),(d,len(rec))
 counters=np.stack([load(d,s,'counters')[:4] for s in rec]);first=rec[0]
 item={'frames':len(rec),'states':t.counts([x['state'] for x in rec.values()]),'settings_match':all(x['settingsMatch'] and x['resolution']==4096 and x['capacity']==512 and x['rays']==4 and x['steps']==8 for x in rec.values()),'clampRelaxation':first['clampRelaxation'],'mixRetention':first['mixRetention'],'overflow_max':int(counters[:,3].max()),'missing_max':max(x['replayMissingPixels'] for x in rec.values()),'replay_error_max':max(x['replayMaxError'] for x in rec.values())}
 if d.name.endswith('_static'):
  samples={k:{n:[] for n in t.REGIONS} for k in ('shadow','source','output')};diag={n:{k:[] for k in ('clip','accept','confirm','count','weight')} for n in t.REGIONS}
  # Keep spatially filtered signals separate, with filter applied before edge masking.
  filtered={n:[] for n in t.REGIONS};steps=range(32,288);keys=[tuple(rec[s]['jitter'].values()) for s in steps]
  for s in steps:
   for k in samples:
    a=load(d,s,k);a=a if k=='shadow' else t.luma(a)
    for n in t.REGIONS:samples[k][n].append(cropm(a,n))
    if k=='output':
     for n,b in t.REGIONS.items():filtered[n].append(gaussian_filter(t.crop(a,b),1)[masks[n]])
   if not rec[s]["diagnosticCapture"]:continue
   before=load(d,s,'historyBefore');after=load(d,s,'historyAfter');ac=load(d,s,'accept');meta=load(d,s,'meta');updated=load(d,s,'updated')
   vals={'clip':abs(t.luma(after)-t.luma(before)),'accept':ac>.5,'confirm':after[...,3]<-1.5,'count':meta[...,0],'weight':updated[...,3]}
   for n in t.REGIONS:
    for k,a in vals.items():diag[n][k].append(cropm(a,n))
  def residual(a):
   r=a.copy()
   for key in set(keys):
    ids=[i for i,k in enumerate(keys) if k==key];r[ids]-=a[ids].mean(0)
   return r
  item['temporal']={k:{n:{'same_jitter_rms':t.rms(residual(np.stack(a))),'mean':float(np.mean(a))} for n,a in v.items()} for k,v in samples.items()}
  item['filtered_output_rms']={n:t.rms(residual(np.stack(a))) for n,a in filtered.items()}
  item['history']={}
  for n,v in diag.items():
   v={k:np.concatenate(a) for k,a in v.items()};ac=v['accept'];cl=v['clip'][ac]
   item['history'][n]={'accepted_fraction':float(ac.mean()),'clipped_fraction_gt_1e-3':float((cl>.001).mean()),'clip_rms':t.rms(cl),'stationary_confirm_fraction':float(v['confirm'].mean()),'weight_p10_p50_p90':list(map(float,np.percentile(v['weight'][ac],[10,50,90]))),'weight_mean':float(v['weight'][ac].mean()),'count_p10_p50_p90':list(map(float,np.percentile(v['count'],[10,50,90])))}
 else:
  variant=d.name.rsplit('_',1)[0];base=root/(variant+'_static');baseRec=t.records(base)
  if not (base/'status.txt').exists():continue
  item['post_stop']=[]
  for s,r in sorted(rec.items()):
   if s<64 or not r['capture']:continue
   row={'step':s,'after_stop':s-64,'matches':{k:r[k]==baseRec[s][k] for k in ('jitter','shaderFrameSeed','cameraPosition','cameraEuler','gpuVP')},'error':{}}
   for k in ('shadow','source','output'):
    a=load(d,s,k);b=load(base,s,k);delta=a-b if k=='shadow' else t.luma(a)-t.luma(b)
    row['error'][k]={n:t.rms(cropm(delta,n)) for n in t.REGIONS}
   item['post_stop'].append(row)
 cachefile.write_text(json.dumps(item,indent=2));result['stages'][d.name]=item
 print(d.name, 'RMS '+str([round(item['temporal']['output'][n]['same_jitter_rms'],6) for n in t.REGIONS]) if 'temporal' in item else 'tail '+str(item['post_stop'][-1]['error']['output']),flush=True)

# Exact input/phase A/B checks, independently cached after each completed pair.
for name,item in result['stages'].items():
 if name.startswith('baseline_'):continue
 scenario=name.rsplit('_',1)[1];base=root/('baseline_'+scenario);d=root/name
 if not (base/'status.txt').exists():continue
 pf=out/(name+'-pair.json')
 if pf.exists():result['pairs'][name]=json.loads(pf.read_text());continue
 rr=t.records(d);br=t.records(base);checks={k:all(rr[s][k]==br[s][k] for s in rr) for k in ('jitter','shaderFrameSeed','cameraPosition','cameraEuler','gpuVP','angle','capacity')}
 err={k:0. for k in ('shadow','source')}
 for s,r in rr.items():
  if not r['capture']:continue
  for k in err:err[k]=max(err[k],float(np.max(abs(load(d,s,k)-load(base,s,k)))))
 pair={'parameter_matches':checks,'max_absolute_input_delta':err};pf.write_text(json.dumps(pair,indent=2));result['pairs'][name]=pair
(out/'summary.json').write_text(json.dumps(result,indent=2))
print('complete stages',len(result['stages']),flush=True)
