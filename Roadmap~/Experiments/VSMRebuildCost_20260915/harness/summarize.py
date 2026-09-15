from pathlib import Path
import json,csv,statistics
root=Path('Temp~/vsm-rebuild')
report={}
for folder in ['timing','group-timing','final-timing']:
 out=[]
 for f in sorted((root/folder).glob('*.csv')):
  d={}
  for r in csv.DictReader(f.open()):
   if int(r['gpu_sample_count'])>0:d.setdefault(r['marker'],[]).append(int(r['gpu_ns'])/1e6)
  meta=json.loads(f.with_suffix('.json').read_text());meta.pop('state',None)
  meta['window']=f.stem;meta['markers']={k:dict(p50=statistics.median(v),n=len(v),p95=sorted(v)[int((len(v)-1)*.95)]) for k,v in d.items()}
  out.append(meta)
  keys=['VSM.StaticRasterDraw','VSM.StaticRasterClear','VSM.ClearPhysicalPages','VSM.StaticCasterCull','VSM.PageCull','VSM.Resolve','VSM.DynamicRasterDraw']
  if folder!='timing':print(folder,f.stem, ' '.join(k[4:]+'='+str(round(meta['markers'][k]['p50'],4)) for k in keys if k in d))
 report[folder]=out
(root/'summary.json').write_text(json.dumps(report,indent=2))
v=json.loads((root/'native-v2/results.json').read_text())
print('quality',dict(comparisons=len(v),depthValues=sum(x['depth'][2]*2 for x in v),shadowPixels=sum(x['shadow'][2] for x in v)))
a=json.loads((root/'final-timing/before.json').read_text());b=json.loads((root/'final-timing/after.json').read_text())
for k in a:
 if a[k]!=b[k]:print('state difference',k,a[k],b[k])
