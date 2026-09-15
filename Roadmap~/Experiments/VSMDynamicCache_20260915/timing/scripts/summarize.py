from pathlib import Path
import csv,json,statistics,sys

root=Path(sys.argv[1])
stages=[]
for path in sorted(root.glob('[0-9][0-9].json')):
    stage=json.loads(path.read_text(encoding='utf-8'))
    rows=list(csv.DictReader(path.with_suffix('.csv').open(encoding='utf-8')))
    metrics={}
    for marker in dict.fromkeys(row['marker'] for row in rows):
        group=[row for row in rows if row['marker']==marker]
        values=sorted(int(row['gpu_ns'])/1e6 for row in group)
        def quantile(q):
            x=(len(values)-1)*q;i=int(x);return values[i]+(values[min(i+1,len(values)-1)]-values[i])*(x-i)
        metrics[marker]=dict(p50_ms=statistics.median(values),p95_ms=quantile(.95),minimum_ms=min(values),maximum_ms=max(values),
                             samples=len(values),sample_counts=sorted(set(int(row['gpu_sample_count']) for row in group)),
                             zero_samples=sum(v==0 for v in values),unique_values=len(set(values)))
    stage.pop('state',None)
    stage['gpu']=metrics
    stages.append(stage)
restored=json.loads((root/'before.json').read_text(encoding='utf-8'))==json.loads((root/'after.json').read_text(encoding='utf-8')) if (root/'after.json').exists() else None
report=dict(status=(root/'status.txt').read_text(encoding='utf-8') if (root/'status.txt').exists() else 'running',restored=restored,stages=stages)
(root/'summary.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
for s in stages:
    print({k:s[k] for k in ('stage','resolution','scenario','force','allocated','dynamicDirty','dynamicNonempty')})
    print({m:round(s['gpu'][m]['p50_ms'],6) for m in ('VSM.ClearPhysicalPages','VSM.DynamicCasterCull','VSM.DynamicRasterDraw','VSM.PageOccupancy','VSM.ResolveTrace')})
print({'status':report['status'],'restored':restored})
