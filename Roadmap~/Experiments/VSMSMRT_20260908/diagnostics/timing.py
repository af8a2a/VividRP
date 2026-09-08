from pathlib import Path
import json
root=Path('Temp~/vsm-smrt-timing/20260908_140535_869');report={}
for p in sorted(root.rglob('case_000.json')):
 j=json.loads(p.read_text());m={x['name']:x for x in j['metrics']};n=j['settings']['name'];report[n]={'status':j['status'],'samples':j['observations'],'counters':j['finalPageCounters'],'comparable':j['comparable'],'issues':j['issues'],'metrics':m}
 print(n,[(k,v['median']) for k,v in m.items() if k in ['gpu_frame_ms','frame_gpu_ms','VSM.ResolveAndFeedback_gpu_ms','VSM.Allocate_gpu_ms','gc_allocated_bytes','VSM.StaticRaster_gpu_ms']])
Path('Temp~/vsm-smrt/analysis/timing.json').write_text(json.dumps(report,indent=2));print('names',list(m))
