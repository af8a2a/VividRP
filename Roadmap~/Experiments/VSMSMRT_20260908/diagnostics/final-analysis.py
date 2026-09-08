from pathlib import Path
import sys,json,numpy as np, shutil
sys.path.insert(0,str(Path('Roadmap~/Experiments/VSMTransition4096_20260907').resolve()))
import analyze_transition as t
roots=[Path('Temp~/vsm-smrt-captures')/n for n in ('20260908_135958_277','20260908_141454_723')]
out=Path('Roadmap~/Experiments/VSMSMRT_20260908');out.mkdir(exist_ok=True)
dirs={d.name:d for r in roots for d in r.iterdir() if d.is_dir()};base=dirs['pcf512_static'];br=t.records(base)
bs=np.stack([t.load(base,s,'shadow') for s in range(32)]);bm=bs.mean(0);g=np.zeros_like(bm);g[:,:-1]+=abs(np.diff(bm,axis=1));g[:-1]+=abs(np.diff(bm,axis=0));edges=t.dilate(t.dilate((g>.05)|((bm>.05)&(bm<.95))));masks={n:t.crop(edges,b) for n,b in t.REGIONS.items()}
report={'roots':[str(r.resolve()) for r in roots],'stages':{},'static':{},'post_stop':{},'total_frames':0,'captured_frames':0,'diagnostic_frames':0,'ownership_errors':0,'nonfinite_shadows':0,'zero_angle_max_abs_difference':0}
means={};temporal={}
for name,d in sorted(dirs.items()):
 rec=t.records(d);steps=sorted(rec);capture=[s for s in steps if rec[s]['capture']];debug=[s for s in capture if (d/f'frame_{s:03}_debug0.bin.gz').exists()]
 report['total_frames']+=len(rec);report['captured_frames']+=len(capture);report['diagnostic_frames']+=len(debug)
 counters=np.stack([t.load(d,s,'counters') for s in steps]);sampled=[];unavailable=0;fallbackCount=0;pcfFallback=0
 for s in capture:
  a=t.load(d,s,'shadow');report['nonfinite_shadows']+=int((~np.isfinite(a)).sum())
  if s not in debug:continue
  levels=t.load(d,s,'debug0');smrt=t.load(d,s,'debug7');valid=levels[...,0]>=0
  unavailable+=int((valid&(levels[...,1]<0)).sum());fallbackCount+=int((valid&(levels[...,1]>levels[...,0])).sum());sampled.append(int((levels[...,1]>=9).sum()));pcfFallback+=int(np.maximum(smrt[...,3],0).sum())
  table=t.load(d,s,'pagetable');metadata=t.load(d,s,'metadata').reshape(-1,4);occupied=table[table>0]
  report['ownership_errors']+=int(len(occupied)!=len(np.unique(occupied)))+int((table!=metadata[:,1]).sum())+int((occupied>rec[s]['capacity']).sum())
 report['stages'][name]={'frames':len(rec),'capture':len(capture),'diagnostic':len(debug),'states':t.counts([r['state'] for r in rec.values()]),'fallbacks':t.counts([r['fallback'] for r in rec.values()]),'snapshots':sum(r['receiverSnapshot'] for r in rec.values()),'settings_match':all(r['settingsMatch'] for r in rec.values()),'capacity':rec[0]['capacity'],'angle':[min(r['angle'] for r in rec.values()),max(r['angle'] for r in rec.values())],'requested':t.dist(counters[:,1]),'new':t.dist(counters[:,2]),'overflow':t.dist(counters[:,3]),'terminal_level_samples_max':max(sampled,default=0),'unavailable':unavailable,'coarse_fallback_samples':fallbackCount,'final_pcf_fallback_samples':pcfFallback,'replay_error_max':max(r['replayMaxError'] for r in rec.values())}
 if not name.endswith('_static'):continue
 item={};means[name]={};temporal[name]={}
 jitter=[tuple(rec[s]['jitter'][k] for k in ('x','y')) for s in range(32)]
 for kind in ('shadow','source','output'):
  a=np.stack([t.load(d,s,kind) for s in range(32)])
  if kind!='shadow':a=t.luma(a)
  means[name][kind]=a.mean(0);temporal[name][kind]=a
  residual=a.copy()
  for key in set(jitter):
   ids=[i for i,k in enumerate(jitter) if k==key];residual[ids]-=a[ids].mean(0)
  item[kind]={}
  for n,b in t.REGIONS.items():
   z=np.stack([t.crop(x,b) for x in a]);rr=np.stack([t.crop(x,b) for x in residual]);mask=masks[n]
   item[kind][n]={'same_jitter_rms':t.rms(rr[:,mask]),'all_jitter_rms':t.rms((z-z.mean(0))[:,mask])}
 if name=='smrtZero_static':report['zero_angle_max_abs_difference']=float(np.max(abs(a))) if False else float(max(np.max(abs(t.load(d,s,'shadow')-t.load(base,s,'shadow'))) for s in range(32)))
 report['static'][name]=item
 target=out/'views'/name;target.mkdir(parents=True,exist_ok=True);shutil.copy2(d/'last.png',target/'last.png')
for name,d in sorted(dirs.items()):
 if name.endswith('_static'):continue
 rec=t.records(d);refname=name.rsplit('_',1)[0]+'_static';ref=dirs[refname];rr=t.records(ref);stop=96 if name.endswith('_yaw') else 64
 post=[]
 for s,r in sorted(rec.items()):
  if s<stop or not r['capture']:continue
  same=[i for i in range(32) if rr[i]['jitter']==r['jitter']];row={'after_stop':s-stop,'state':r['state'],'errors':{}}
  for kind in ('shadow','source','output'):
   a=t.load(d,s,kind)
   if kind!='shadow':a=t.luma(a)
   error=a-temporal[refname][kind][same].mean(0)
   row['errors'][kind]={n:{'rms':t.rms(t.crop(error,b)[masks[n]]),'mae':float(np.mean(abs(t.crop(error,b)[masks[n]])))} for n,b in t.REGIONS.items()}
  post.append(row)
 report['post_stop'][name]=post
(out/'quality-summary.json').write_text(json.dumps(report,indent=2))
np.savez_compressed(out/'static-means.npz',**{n+'_'+k:a for n,z in means.items() for k,a in z.items()})
print({k:v for k,v in report.items() if k not in ('stages','static','post_stop')})
for n,s in report['stages'].items(): print(n,s['frames'],s['states'],'overflow',s['overflow']['max'],'L9',s['terminal_level_samples_max'])
for n,s in report['static'].items():print('output same phase',n,{k:round(v['same_jitter_rms'],6) for k,v in s['output'].items()})
for n,post in report['post_stop'].items():
 if n.endswith('_angle'):print('angle post',n,[(p['after_stop'],round(p['errors']['output']['roof']['rms'],5)) for p in post])
