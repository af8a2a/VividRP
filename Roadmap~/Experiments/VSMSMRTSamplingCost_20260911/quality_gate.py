"""Test coarse slab thickness caps against independent geometry before adoption."""
import sys,inspect,json,csv
from pathlib import Path
import numpy as np
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
sys.path.insert(0,str(HERE.parent/'VSMSMRTContinuation_20260911'))
import path_geometry as g

def main():
    source=inspect.getsource(g.trace)
    source=source.replace('thickness=np.float32(.5)*w','thickness=np.float32(.5)*min(w, levels[0][0]*CAP)')
    rows=[];manifest=json.loads((g.WORK/'manifest.json').read_text())
    for cap in (1,2,4):
        ns=dict(g.__dict__,CAP=cap);exec(source,ns);trace=ns['trace']
        for c in manifest:
            if c['max_length']!=50 or c['scrolled'] or c['terminal_levels'] or c['diameter']!=7.1:continue
            levels=g.levels_for(c);o,r,_,_=g.reference.inputs(c['scene'],c['resolution'],c['diameter'],c['budget'],True,50)
            depths=[g.depth_for(g.reference.SCENES[c['scene']],c['resolution'],l) for l in levels]
            new,work,_=trace(depths,o,r,c['budget'],levels);a=np.load(g.WORK/f"{c['id']:03}.npz");truth=a['straight'];old=a['new']
            rows.append(dict(id=c['id'],scene=c['scene'],resolution=c['resolution'],budget=c['budget'],cap=cap,rays=len(new),old_fp=int(((old==1)&~truth).sum()),new_fp=int(((new==1)&~truth).sum()),old_fn=int(((old==0)&truth).sum()),new_fn=int(((new==0)&truth).sum()),changed=int((new!=old).sum()),new_mean_reads=float(work.mean())))
    with (HERE/'thickness-candidates.csv').open('w',newline='') as f:
        w=csv.DictWriter(f,fieldnames=rows[0].keys());w.writeheader();w.writerows(rows)
    report={}
    for cap in (1,2,4):
        subset=[r for r in rows if r['cap']==cap]
        report[cap]=dict(cases=len(subset),regressions=[{k:r[k] for k in ('id','scene','resolution','budget','old_fp','new_fp','old_fn','new_fn')} for r in subset if r['new_fp']>r['old_fp'] or r['new_fn']>r['old_fn']],old_fp=sum(r['old_fp'] for r in subset),new_fp=sum(r['new_fp'] for r in subset),old_fn=sum(r['old_fn'] for r in subset),new_fn=sum(r['new_fn'] for r in subset))
    (HERE/'thickness-candidates.json').write_text(json.dumps(report,indent=2))
    print({k:{**v,'regressions':len(v['regressions'])} for k,v in report.items()})
if __name__=='__main__':main()
