from pathlib import Path
import gzip,json,sys,numpy as np
root=Path(sys.argv[1]);out=Path('Temp~/tsr-local-clamp-final/analysis')
regions={'roof':(20,0,380,70),'ledge':(0,295,390,80),'arches':(15,385,360,110)}
masks=np.load('Temp~/tsr-local-clamp-final/edge-masks.npz')
for d in sorted(root.iterdir()):
 if not d.is_dir() or not (d/'status.txt').exists() or d.name.startswith(('baseline','priming')):continue
 target=out/(d.name+'-input-audit.json')
 if target.exists():continue
 scenario=d.name.rsplit('_',1)[1];base=root/('baseline_'+scenario)
 rows=[json.loads(s) for s in (d/'frames.jsonl').read_text().splitlines()]
 data={}
 for kind,channels in [('shadow',1),('source',4)]:
  sums={n:0. for n in regions};count={n:0 for n in regions};maxdiff=0.;nonzero=0;total=0
  for row in rows:
   if not row['capture']:continue
   file=row['prefix']+'_'+kind+'.bin.gz'
   a=np.frombuffer(gzip.decompress((d/file).read_bytes()),'<f2').reshape(495,400,channels)[::-1].astype(np.float32)
   b=np.frombuffer(gzip.decompress((base/file).read_bytes()),'<f2').reshape(495,400,channels)[::-1].astype(np.float32)
   diff=a-b;maxdiff=max(maxdiff,float(abs(diff).max()));nonzero+=int(np.count_nonzero(diff));total+=diff.size
   luma=diff[...,0] if channels==1 else diff[...,:3]@np.array([.2126,.7152,.0722],np.float32)
   for n,(x,y,w,h) in regions.items():
    delta=luma[y:y+h,x:x+w][masks[n]];sums[n]+=float(np.sum(delta.astype(np.float64)**2));count[n]+=delta.size
  data[kind]={'max_component_delta':maxdiff,'fraction_different_components':nonzero/total,'region_luma_rms':{n:(sums[n]/count[n])**.5 for n in regions}}
 target.write_text(json.dumps(data,indent=2))
 print(d.name,data,flush=True)
