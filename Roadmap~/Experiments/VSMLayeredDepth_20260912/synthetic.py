import sys,inspect,json
from pathlib import Path
import numpy as np
sys.dont_write_bytecode=True
sys.path.insert(0,str((Path(__file__).resolve().parent.parent/'VSMSMRTContinuation_20260911')))
import path_geometry as g
src=inspect.getsource(g.trace).replace('def trace(','def trace_layers(').replace('len(depths[0])','len(depths[0][0])').replace('raw=depths[li][','raw=depths[li][0,')
a=src.index('            result[active&hit]=1')
src=src[:a]+'''            for layer in range(1,len(depths[li])):
                hidden=depths[li][layer,np.clip(cell[:,1],0,res-1),np.clip(cell[:,0],0,res-1)]
                ht=np.where(hidden==0,np.float32(-1),(hidden-o[:,2])/depth_scale)
                hit|=(ht>enter)&(tail|(ht-thickness<=exit_time))
'''+src[a:]
ns=dict(g.__dict__);exec(src,ns);trace=ns['trace_layers'];rows=[]
for scene,shapes in g.reference.SCENES.items():
 for res in (64,128,256,512):
  c=dict(scene=scene,resolution=res,diameter=7.1,budget=8,max_length=50,scrolled=False,terminal_levels=0);levels=g.levels_for(c)
  o,r,world,direction=g.reference.inputs(scene,res,7.1,8,True,50)
  pools=[]
  for level in levels:
   a=np.stack([g.depth_for([shape],res,level) for shape in shapes] or [np.zeros((res,res),dtype='<f4')]);a=np.sort(a,axis=0)[::-1];pools.append(a)
  single,_,_=g.trace([a[0] for a in pools],o,r,8,levels);multi,_,_=trace(pools,o,r,8,levels);_,truth=g.reference.truth(shapes,world,direction,50)
  for name,pred in [('front',single),('layered',multi)]:
   rows.append(dict(scene=scene,resolution=res,rule=name,rays=len(o),false_shadow=int(((pred==1)&~truth).sum()),missed_shadow=int(((pred==0)&truth).sum())))
Path('Temp~/vsm-backfaces/synthetic.json').write_text(json.dumps(rows,indent=2))
for rule in ['front','layered']:
 rs=[r for r in rows if r['rule']==rule];print(rule,{k:sum(r[k] for r in rs) for k in ['rays','false_shadow','missed_shadow']})
for scene in g.reference.SCENES:
 d={r:sum(x['false_shadow'] for x in rows if x['rule']==r and x['scene']==scene) for r in ['front','layered']};print(scene,d)
