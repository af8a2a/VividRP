from pathlib import Path
import json,sys,numpy as np
sys.path.insert(0,str(Path('Roadmap~/Experiments/VSMTransition4096_20260907').resolve()))
import analyze_transition as t
root=Path('Temp~/vsm-smrt-captures/20260908_135958_277');out=Path('Temp~/vsm-smrt/analysis')
base=root/'pcf512_static'; rec=t.records(base)
bs=np.stack([t.load(base,s,'shadow') for s in range(32)]);mean=bs.mean(0)
g=np.zeros_like(mean);g[:,:-1]+=abs(np.diff(mean,axis=1));g[:-1]+=abs(np.diff(mean,axis=0));edge=t.dilate(t.dilate((g>.05)|((mean>.05)&(mean<.95))))
masks={n:t.crop(edge,b) for n,b in t.REGIONS.items()};summary={};cache={}
for d in sorted(root.iterdir()):
 r=t.records(d) if d.is_dir() else {}
 if not r or not d.name.endswith('_static') or len(r)!=32:continue
 item={};cache[d.name]={}
 keys=[tuple(r[s]['jitter'][k] for k in ('x','y')) for s in range(32)]
 for kind in ('shadow','source','output'):
  a=np.stack([t.load(d,s,kind) for s in range(32)])
  if kind!='shadow':a=t.luma(a)
  cache[d.name][kind]=a
  residual=a.copy()
  for key in set(keys):
   ids=[i for i,k in enumerate(keys) if k==key];residual[ids]-=a[ids].mean(0)
  item[kind]={}
  for name,b in t.REGIONS.items():
   z=np.stack([t.crop(x,b) for x in a]); rr=np.stack([t.crop(x,b) for x in residual]); m=masks[name]
   item[kind][name]={'all_phase_rms':t.rms((z-z.mean(0))[:,m]),'same_phase_rms':t.rms(rr[:,m]),'edge_pixels':int(m.sum())}
 summary[d.name]=item
for d in sorted(root.iterdir()):
 if not d.is_dir() or d.name.endswith('_static'):continue
 r=t.records(d)
 if not r:continue
 name=d.name.rsplit('_',1)[0]+'_static'
 if name not in cache:continue
 sr=t.records(root/name);stop=96 if d.name.endswith('yaw') else 64
 rows=[]
 for s in sorted(r):
  if not r[s]['capture'] or s<stop:continue
  same=[i for i in range(32) if sr[i]['jitter']==r[s]['jitter']]
  v={'step':s,'after_stop':s-stop,'camera_match':r[s]['cameraPosition']==sr[0]['cameraPosition'] and r[s]['cameraEuler']==sr[0]['cameraEuler'],'light_match':r[s]['lightEuler']==sr[0]['lightEuler'],'error':{}}
  for kind in ('shadow','source','output'):
   a=t.load(d,s,kind)
   if kind!='shadow':a=t.luma(a)
   error=a-cache[name][kind][same].mean(0)
   v['error'][kind]={n:{'mae':float(np.mean(abs(t.crop(error,b)[masks[n]]))),'rms':t.rms(t.crop(error,b)[masks[n]])} for n,b in t.REGIONS.items()}
  rows.append(v)
 summary[d.name]={'frames':len(r),'post_stop':rows}
(out/'temporal.json').write_text(json.dumps(summary,indent=2))
for name,item in summary.items():
 if name.endswith('_static'):print(name,json.dumps(item['output']))
 elif item['post_stop']:print(name,'last',json.dumps(item['post_stop'][-1]))
