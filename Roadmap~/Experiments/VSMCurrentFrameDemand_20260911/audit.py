from pathlib import Path
import json,gzip,sys,numpy as np
root=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
def raw(d,s,k):
 a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),'<f4' if k.startswith('debug') else '<u4')
 return a.reshape(-1,4) if k.startswith('debug') or k=='metadata' else a
report={'capture':str(root),'stages':{},'ownership_errors':[],'captured_frames':0}
for d in sorted(root.iterdir()):
 if not d.is_dir() or not (d/'frames.jsonl').exists():continue
 frames=[json.loads(l) for l in (d/'frames.jsonl').read_text().splitlines()];stats=[]
 for f in frames:
  if not f['capture'] or not f['receiverSnapshot']:continue
  s=f['step'];t=raw(d,s,'pagetable');m=raw(d,s,'metadata');o=raw(d,s,'owners');lev=raw(d,s,'debug0');flags=raw(d,s,'debug5');valid=t>0;used=o>0
  errors=[]
  if len(o)!=f['capacity']:errors.append('capacity')
  if int(t.max())>len(o):errors.append('slot_range')
  if len(np.unique(t[valid]))!=int(valid.sum()):errors.append('duplicate_slot')
  if not np.all(m[valid,1]==t[valid]):errors.append('metadata_slot')
  if not np.all(o[t[valid]-1]==np.where(valid)[0]+1):errors.append('table_owner')
  if not np.all(t[o[used]-1]==np.where(used)[0]+1):errors.append('owner_table')
  if np.any((m[valid,0]&6)!=2):errors.append('unfinalized_page')
  if errors:report['ownership_errors'].append([d.name,s,errors])
  receiver=lev[:,0]>=0;sampled=lev[:,1]>=0
  stats.append({'step':s,'final_unavailable':int((receiver&~sampled).sum()),'terminal_samples':int((lev[:,1]==9).sum()),
   'fallback_pixels':int((receiver&sampled&(lev[:,1]>lev[:,0])).sum()),'receiver_pixels':int(receiver.sum()),'debug_missing_pixels':int((receiver&(flags[:,0]>0)).sum()),
   'resident':int(valid.sum()),'resident_by_level':valid.reshape(-1,1024).sum(axis=1).tolist(),'resident_current_demand_by_level':(((m[:,3]&1)!=0)&valid).reshape(-1,1024).sum(axis=1).tolist()})
  report['captured_frames']+=1
 report['stages'][d.name]={'frames':stats,'final_unavailable_max':max((x['final_unavailable'] for x in stats),default=0),'terminal_samples_max':max((x['terminal_samples'] for x in stats),default=0)}
 print(d.name,len(stats),flush=True)
(out/'page-audit.json').write_text(json.dumps(report,indent=2))
print('captured',report['captured_frames'],'ownership_errors',report['ownership_errors'],flush=True)
