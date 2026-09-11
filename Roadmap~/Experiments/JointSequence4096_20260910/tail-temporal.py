from pathlib import Path
import json,gzip,numpy as np
root=Path("Temp~/joint-sequence-captures/20260910_150147_007");out=Path("Temp~/joint-sequence/analysis/tail-temporal.json")
masks=np.load("Temp~/joint-sequence/edge-masks.npz");boxes={"roof":(20,0,380,70),"ledge":(0,295,390,80),"arches":(15,385,360,110)}
result={"window":[32,511],"note":"480 samples / 60 per jitter; sensitivity check, not a complete joint period","stages":{}}
for d in sorted(root.iterdir()):
 if not d.is_dir() or d.name.startswith("priming"):continue
 rec=[json.loads(l) for l in (d/"frames.jsonl").read_text().splitlines()][32:512]
 sums={};sqs={};counts={}
 for r in rec:
  s=r["step"];a=np.frombuffer(gzip.decompress((d/f"frame_{s:03}_output.bin.gz").read_bytes()),"<f2").reshape(495,400,4)[::-1].astype(np.float64)[...,:3]@np.array([.2126,.7152,.0722]);key=tuple(r["jitter"].values())
  for n,(x,y,w,h) in boxes.items():
   k=(key,n);v=a[y:y+h,x:x+w][masks[n]]
   if k not in sums:sums[k]=np.zeros_like(v);sqs[k]=np.zeros_like(v);counts[k]=0
   sums[k]+=v;sqs[k]+=v*v;counts[k]+=1
 result["stages"][d.name]={n:float(np.sqrt(np.mean(np.concatenate([np.maximum(0,sqs[k]/counts[k]-(sums[k]/counts[k])**2) for k in sums if k[1]==n])))) for n in boxes}
 print(d.name,result["stages"][d.name],flush=True)
 out.write_text(json.dumps(result,indent=2))
result["change_percent"]={k:{n:100*(v/result["stages"]["bnd_static"][n]-1) for n,v in vals.items()} for k,vals in result["stages"].items()};out.write_text(json.dumps(result,indent=2))
