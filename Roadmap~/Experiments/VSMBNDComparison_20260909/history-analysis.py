from pathlib import Path
import gzip,json,sys,numpy as np
sys.path.insert(0,str(Path('Roadmap~/Experiments/VSMTransition4096_20260907').resolve()))
import analyze_transition as t
root=Path(sys.argv[1]);masks=np.load('Temp~/vsm-bnd-compare/analysis/edge-masks.npz');result={}
def load(d,s,k,c):
 raw=gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes())
 return np.frombuffer(raw,dtype='<f2').reshape(t.H,t.W,c)[::-1].astype(np.float32)
for d in root.iterdir():
 if not d.is_dir() or not (d/'status.txt').exists():continue
 by_region={n:{'clip':[],'confirm':[],'accept':[],'meta':[]} for n in t.REGIONS}
 for s in range(32,64):
  before=load(d,s,'historyBefore',4);after=load(d,s,'historyAfter',4);accept=load(d,s,'accept',1)[...,0];meta=load(d,s,'meta',2)[...,0]
  clip=abs(t.luma(after)-t.luma(before))
  for n,b in t.REGIONS.items():
   mask=masks[n];a=by_region[n]
   a['clip'].append(t.crop(clip,b)[mask]);a['confirm'].append((t.crop(after[...,3],b)[mask]<-1.5));a['accept'].append(t.crop(accept,b)[mask]>.5);a['meta'].append(t.crop(meta,b)[mask])
 item={}
 for n,v in by_region.items():
  c=np.concatenate(v['clip']);a=np.concatenate(v['accept']);m=np.concatenate(v['meta']);confirm=np.concatenate(v['confirm'])
  item[n]={'samples':len(c),'accepted_fraction':float(a.mean()),'accepted_clip_fraction_gt_1e-3':float((c[a]>1e-3).mean()),'accepted_clip_luma_rms':t.rms(c[a]),'accepted_clip_luma_p95':float(np.percentile(c[a],95)),'stationary_change_fraction':float(confirm.mean()),'history_sample_count_p10_p50_p90':list(map(float,np.percentile(m,[10,50,90])))}
 result[d.name]=item;print(d.name,json.dumps(item))
Path('Temp~/vsm-bnd-compare/analysis/history-clipping.json').write_text(json.dumps(result,indent=2))
