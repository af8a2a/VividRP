"""Summarize post-finalize allocator counters; page overflow is not depth-layer overflow."""
import argparse, gzip, json
from pathlib import Path
import numpy as np
NAMES=['allocated_pages','requested_pages','newly_allocated_pages','page_budget_overflow']
def run(root):
 r=json.loads((root/'request.json').read_text(encoding='utf-8-sig')); result={'source':str(root.resolve()),'counter_schema':'CSMShadowResolve.compute allocator output indices 0..3','cases':{}}
 for scene in r['scenarios']:
  for mode in r['modes']:
   directory=root/(scene+'_'+mode); rows=[]
   for step in range(r['frames']):
    raw=gzip.decompress((directory/f'frame_{step:03d}_pages.gz').read_bytes()); assert len(raw)==16
    counters=np.frombuffer(raw,dtype='<u4'); metadata=json.loads((directory/f'frame_{step:03d}.json').read_text())
    assert counters[0]<=metadata['pages'] and counters[2]<=counters[0] and counters[3]<=counters[1]
    rows.append(counters)
   a=np.array(rows);entry={'frames':len(rows),'overflow_frames':int((a[:,3]>0).sum()),'per_frame':a.tolist()}
   for i,name in enumerate(NAMES): entry[name]={'min':int(a[:,i].min()),'median':float(np.median(a[:,i])),'p95':float(np.quantile(a[:,i],.95)),'max':int(a[:,i].max())}
   result['cases'][directory.name]=entry
 return result
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args();a.output.write_text(json.dumps(run(a.root),indent=2)+'\n')
