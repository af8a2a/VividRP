from pathlib import Path
import sys,json
root=Path(__file__).resolve().parents[3]
source=(root/'Roadmap~/Experiments/VSMTransition4096_20260907/analyze_transition.py').read_text(encoding='utf-8')
source=source[:source.index("if __name__=='__main__':")]
a=source.index('    if all(v in means for v in VARIANTS):');b=source.index('    return result',a)
source=source[:a]+source[b:]
source=source.replace("VARIANTS=['baseline','width010','width005','width002']","VARIANTS=['baseline','bounded020','bounded005']").replace('WIDTHS=[.2,.1,.05,.02]','WIDTHS=[.2,.2,.05]')
source=source.replace("reference='focused020' if v=='focused005' else 'baseline'","reference='bounded020' if v=='bounded005' else 'baseline'")
source=source.replace("'Coverage width only; LOD-fraction width remains .2. Layouts, PCF and 4096/256 pool unchanged.'","'Current allocator; baseline and bounded L3 layout, then coverage width .2/.05; LOD width .2. PCF, TSR, 4096/256 fixed.'")
ns={};exec(compile(source,'coverage-analysis','exec'),ns)
capture=Path(sys.argv[1]).resolve();out=root/'Roadmap~/Experiments/VSMFineCoverage_20260908/results'
r=ns['analyze'](capture,out)
print('complete',r['complete'])
for name,item in r['stages'].items():
 print(name,'requests',item['counters']['requested']['max'],'new median/max',item['counters']['new']['median'],item['counters']['new']['max'],'overflow',item['counters']['overflow']['max'])
 for region,v in item.get('regions',{}).items():
  if region=='full_screen':continue
  print(' ',region,'sampled',v['sampled'],'coarse blend',v['actual_coarse_blend']['median'],'footprint',v['footprint']['median'],'fallback',v['fallback_fraction'])
for name,c in r['comparisons'].items():
 for scenario in ['static','translate','yaw']:
  f=c.get(scenario,{}).get('frames',[])
  if f:print(name,scenario,'finer max',max(x['sampled_finer'] for x in f),'coarser max',max(x['sampled_coarser'] for x in f),'new missing max',max(x['new_unavailable'] for x in f),'pose/jitter',all(x['pose_match'] and x['jitter_match'] for x in f))
