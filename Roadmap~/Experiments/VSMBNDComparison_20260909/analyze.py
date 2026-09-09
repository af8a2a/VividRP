from pathlib import Path
import json,sys,numpy as np
sys.path.insert(0,str(Path('Roadmap~/Experiments/VSMTransition4096_20260907').resolve()))
import analyze_transition as t
from scipy.ndimage import gaussian_filter
root=Path(sys.argv[1]);out=Path('Temp~/vsm-bnd-compare/analysis');out.mkdir(exist_ok=True)
summary={'capture':str(root),'regions':t.REGIONS,'stages':{},'pairs':{}}
cache={};recs={}
for d in sorted(root.iterdir()):
 if not d.is_dir():continue
 r=t.records(d)
 if not (d/'status.txt').exists() or len(r)<96:continue
 recs[d.name]=r;c=np.stack([t.load(d,s,'counters')[:4] for s in sorted(r)])
 item={'frames':len(r),'states':t.counts([x['state'] for x in r.values()]),'capacity':sorted({x['capacity'] for x in r.values()}),'overflow_median':float(np.median(c[:,3])),'overflow_max':int(c[:,3].max()),'settings_match':all(x['settingsMatch'] for x in r.values()),'replay_error_max':max(x['replayMaxError'] for x in r.values()),'missing_max':max(x['replayMissingPixels'] for x in r.values()),'jitter_phases':len({tuple(x['jitter'].values()) for x in r.values()})}
 cache[d.name]={};summary['stages'][d.name]=item
 for kind in ['shadow','source','output']:
  samples={}
  for s,x in r.items():
   if not x['capture']:continue
   a=t.load(d,s,kind)
   if kind!='shadow':a=t.luma(a)
   samples[s]={n:t.crop(a,b).copy() for n,b in t.REGIONS.items()}
  cache[d.name][kind]=samples
# A fixed common mask from the two 512-page 4-ray static means.
bases=['legacy4_512_static','bnd4_512_static']
if all(x in cache for x in bases):
 masks={}
 for n in t.REGIONS:
  a=sum(np.mean([z[n] for s,z in cache[x]['shadow'].items() if s>=32],axis=0) for x in bases)/2
  g=np.zeros_like(a);g[:,:-1]+=abs(np.diff(a,axis=1));g[:-1]+=abs(np.diff(a,axis=0))
  masks[n]=t.dilate(t.dilate((g>.03)|((a>.05)&(a<.95))))
 for name,data in cache.items():
  if not name.endswith('_static'):continue
  temporal={}
  for kind,samples in data.items():
   temporal[kind]={}
   steps=sorted(s for s in samples if s>=32)
   keys=[tuple(recs[name][s]['jitter'].values()) for s in steps]
   for n in t.REGIONS:
    a=np.stack([samples[s][n] for s in steps]);res=a.copy()
    for key in set(keys):
     ids=[i for i,k in enumerate(keys) if k==key];res[ids]-=a[ids].mean(0)
    temporal[kind][n]={'same_jitter_rms':t.rms(res[:,masks[n]]),'all_phase_rms':t.rms((a-a.mean(0))[:,masks[n]]),'edge_pixels':int(masks[n].sum()),'spatial_sigma1_rms':t.rms(gaussian_filter(res,(0,1,1))[:,masks[n]]),'spatial_sigma2_rms':t.rms(gaussian_filter(res,(0,2,2))[:,masks[n]])}
  summary['stages'][name]['temporal']=temporal
  print(name,'overflow',summary['stages'][name]['overflow_median'],'output',[round(temporal['output'][n]['same_jitter_rms'],7) for n in t.REGIONS])
 for a,b in [('legacy4_512_static','bnd4_512_static'),('legacy8_512_static','bnd8_512_static'),('legacy4_256_static','bnd4_256_static')]:
  if a not in cache or b not in cache:continue
  matches={k:all(recs[a][s][k]==recs[b][s][k] for s in recs[a]) for k in ['jitter','shaderFrameSeed','cameraPosition','cameraEuler','angle','capacity','gpuVP']}
  ratios={kind:{n:summary['stages'][b]['temporal'][kind][n]['same_jitter_rms']/summary['stages'][a]['temporal'][kind][n]['same_jitter_rms'] for n in t.REGIONS} for kind in ['shadow','source','output']}
  summary['pairs'][a+'--'+b]={'matches':matches,'rms_ratio_bnd_over_legacy':ratios};print('PAIR',a,ratios['output'],matches)
 for name,data in cache.items():
  if name.endswith('_static'):continue
  base=name.rsplit('_',1)[0]+'_static'
  if base not in cache:continue
  rows=[]
  for s in sorted(data['output']):
   if s<64 or s not in cache[base]['output']:continue
   match={k:recs[name][s][k]==recs[base][s][k] for k in ['jitter','shaderFrameSeed','cameraPosition','cameraEuler','gpuVP']}
   match['angle_within_1e-5_degrees']=abs(recs[name][s]['angle']-recs[base][s]['angle'])<1e-5
   row={'step':s,'frames_after_stop':s-64,'matches':match,'error':{}}
   for kind in ['shadow','source','output']:
    row['error'][kind]={n:t.rms((data[kind][s][n]-cache[base][kind][s][n])[masks[n]]) for n in t.REGIONS}
   rows.append(row)
  summary['stages'][name]['post_stop_same_sample']=rows
  if rows:print(name,'first',rows[0],'last',rows[-1])
 np.savez_compressed(out/'edge-masks.npz',**masks)
(out/'summary.json').write_text(json.dumps(summary,indent=2))
