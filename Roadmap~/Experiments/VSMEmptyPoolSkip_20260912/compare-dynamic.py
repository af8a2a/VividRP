"""Strict same-phase raw-signal comparison; values are float bit patterns, not image colors."""
from pathlib import Path
import argparse,gzip,json,hashlib
import numpy as np
p=argparse.ArgumentParser(description=__doc__);p.add_argument('baseline',type=Path);p.add_argument('candidate',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
report={'baseline':str(a.baseline.resolve()),'candidate':str(a.candidate.resolve()),'cases':{},'status':'pass'}
for case in sorted(x for x in a.baseline.iterdir() if x.is_dir()):
 other=a.candidate/case.name
 if not other.is_dir():raise ValueError('missing case '+case.name)
 stats={'frames':0,'world_different_float_words':0,'reference_different_float_words':0,'signal_different_float_words':0,'pages_different_uint_words':0,'visibility_max_abs':0.,'history_max_abs':0.,'metadata_mismatches':[]}
 for meta in sorted(case.glob('frame_*.json')):
  b=read(meta);c=read(other/meta.name)
  for k in ['step','phase','width','height','gridWidth','gridHeight','pages','levels','referenceRays','scenario','mode','state','fallback','fixtureBackend','cameraPosition','cameraEuler','lightDirection','fixturePosition','viewProjection','quality','smrt','historyPresent','fixturePhase','fixtureGeometryHash']:
   if b[k]!=c[k]:stats['metadata_mismatches'].append({'frame':b['step'],'key':k})
  for kind in ['world','reference','signal','pages']:
   name=meta.stem+'_'+kind+'.gz';x=gzip.decompress((case/name).read_bytes());y=gzip.decompress((other/name).read_bytes());xb=np.frombuffer(x,dtype='<u4');yb=np.frombuffer(y,dtype='<u4');assert xb.shape==yb.shape
   key=kind+'_different_'+('uint' if kind=='pages' else 'float')+'_words';stats[key]+=int(np.count_nonzero(xb!=yb))
   if kind=='signal':
    xf=xb.view('<f4').reshape(-1,4);yf=yb.view('<f4').reshape(-1,4)
    stats['visibility_max_abs']=max(stats['visibility_max_abs'],float(np.nanmax(np.abs(xf[:,0]-yf[:,0]))));stats['history_max_abs']=max(stats['history_max_abs'],float(np.nanmax(np.abs(xf[:,1]-yf[:,1]))))
  stats['frames']+=1
 stats['strict_equal']=not stats['metadata_mismatches'] and all(stats[k]==0 for k in stats if '_different_' in k)
 if not stats['strict_equal']:report['status']='differences'
 report['cases'][case.name]=stats
assert len(report['cases'])==9 and all(c['frames']==32 for c in report['cases'].values())
a.output.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'status':report['status'],'cases':report['cases']}))
