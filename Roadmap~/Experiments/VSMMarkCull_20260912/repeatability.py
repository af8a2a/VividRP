from pathlib import Path
import gzip,json
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]/'Temp~/vsm-general-captures'
A=ROOT/'20260912_022432_142';B=ROOT/'20260912_023249_826'
report={'same_shader_sources':json.loads((A/'shader-source-sha256.json').read_text())['sources']==json.loads((B/'shader-source-sha256.json').read_text())['sources'],'stages':{}}
for a in sorted(A.glob('view*')):
 b=B/a.name;pixels=changed=large=0;absolute=maximum=0.;poses=[]
 for d in [a,b]:
  frames=list(map(json.loads,(d/'frames.jsonl').read_text().splitlines()));poses.append([{k:f[k] for k in ('cameraPosition','cameraEuler','jitter','gpuVP')}|{'phase':f['shaderFrameSeed']%256} for f in frames])
 for f in sorted(a.glob('*_shadow.bin.gz')):
  x=np.frombuffer(gzip.decompress(f.read_bytes()),'<f2').astype('f4');y=np.frombuffer(gzip.decompress((b/f.name).read_bytes()),'<f2').astype('f4');delta=abs(x-y);pixels+=len(x);changed+=int((delta>0).sum());large+=int((delta>.001).sum());absolute+=float(delta.sum(dtype='f8'));maximum=max(maximum,float(delta.max()))
 report['stages'][a.name]={'matched_pose_projection_and_phase':poses[0]==poses[1],'roi_pixels':pixels,'changed':changed,'over_1e3':large,'mae':absolute/pixels,'max':maximum}
(HERE/'repeatability.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
