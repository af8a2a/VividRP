from pathlib import Path
import json,gzip,sys,numpy as np
sys.path.insert(0,str(Path('Roadmap~/Experiments/VSMTransition4096_20260907').resolve()))
import analyze_transition as t
t.OX,t.OY,t.W,t.H=715,190,400,495
dynamic=Path('Temp~/tsr-clamp-captures/20260909_134044_569');static=Path('Temp~/tsr-clamp-static-captures/20260909_135011_413');out=Path('Temp~/tsr-clamp-compare/analysis-static')
masks=np.load('Roadmap~/Experiments/VSMBNDComparison_20260909/edge-masks.npz');result={}
def load(d,s,k):
 c=1 if k=='shadow' else 4
 a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),dtype='<f2').reshape(t.H,t.W,c)[::-1].astype(np.float32)
 return a[...,0] if c==1 else t.luma(a)
for variant in ['baseline','clamp_half','mix_half','both_half']:
 base=static/(variant+'_static')
 if not (base/'status.txt').exists():continue
 for scenario in ['angle','translate']:
  d=dynamic/(variant+'_'+scenario);rr=t.records(d);br=t.records(base);rows=[]
  for s,r in sorted(rr.items()):
   if s<64 or not r['capture']:continue
   row={'step':s,'after_stop':s-64,'parameter_matches':{k:r[k]==br[s][k] for k in ('jitter','shaderFrameSeed','cameraPosition','cameraEuler','gpuVP')},'error':{},'max_input_delta':{}}
   for k in ['shadow','source','output']:
    a=load(d,s,k)-load(base,s,k);row['error'][k]={n:t.rms(t.crop(a,b)[masks[n]]) for n,b in t.REGIONS.items()}
    if k!='output':row['max_input_delta'][k]=float(abs(a).max())
   rows.append(row)
  result[variant+'_'+scenario]={'post_stop':rows,'region_input_rms_max':{k:{n:max(r['error'][k][n] for r in rows) for n in t.REGIONS} for k in ['shadow','source']},'output_rms_first16_mean':{n:float(np.mean([r['error']['output'][n] for r in rows if r['after_stop']<=16])) for n in t.REGIONS},'output_rms_tail_mean':{n:float(np.mean([r['error']['output'][n] for r in rows if r['after_stop']>=160])) for n in t.REGIONS}}
 print(variant,json.dumps({k:{'input':v['region_input_rms_max'],'early':v['output_rms_first16_mean'],'tail':v['output_rms_tail_mean']} for k,v in result.items() if k.startswith(variant+'_')}),flush=True)
(out/'dynamic-against-fixed-static.json').write_text(json.dumps(result,indent=2))
