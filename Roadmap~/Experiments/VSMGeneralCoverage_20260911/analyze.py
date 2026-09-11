from pathlib import Path
import sys,json
package=Path(__file__).resolve().parents[3]
source=(package/'Roadmap~/Experiments/VSMTransition4096_20260907/analyze_transition.py').read_text(encoding='utf-8')
source=source[:source.index("if __name__=='__main__':")]
a=source.index('    if all(v in means for v in VARIANTS):'); b=source.index('    return result',a)
source=source[:a]+source[b:]
source=source.replace("VARIANTS=['baseline','width010','width005','width002']", "VARIANTS=['base256','base512','base768','view256020','view512020','view512005','view384005','view768005']")
source=source.replace('WIDTHS=[.2,.1,.05,.02]','WIDTHS=[.2,.2,.2,.2,.2,.05,.05,.05]')
source=source.replace("reference='focused020' if v=='focused005' else 'baseline'", "reference={'base512':'base256','base768':'base512','view768005':'base768','view256020':'base256','view512020':'base512','view512005':'view512020','view384005':'view512005'}[v]")
source=source.replace("base=root/'baseline_static'", "base=root/'base256_static'")
source=source.replace('Coverage width only; LOD-fraction width remains .2. Layouts, PCF and 4096/256 pool unchanged.', '4096; general view coverage; 256/384/512/768 page budgets; coverage .2/.05, LOD .2; fixed exposure/PCF/TSR.')
source=source.replace('7.3 ms is the user-provided reference.', 'Use a separate run for timing.')
source=source.replace('            result[\'stages\'][d.name]=item', '            result[\'stages\'][d.name]=item; print(d.name, flush=True)')
smrt=len(sys.argv)>3 and sys.argv[3] in ('smrt','highsmrt')
if smrt:source=source.replace("'base768':'base512'", "'base768':'base256'").replace('fixed exposure/PCF/TSR.', 'fixed exposure/SMRT 4x8/TSR; aligned 256-frame sampling phase.')
high=len(sys.argv)>3 and sys.argv[3] in ('high','highsmrt')
if high:source=source.replace("base=root/'base256_static'", "base=root/'base1024_static'").replace("'base512':'base256'", "'view1024005':'base1024','base512':'base256'")
ns={};exec(compile(source,'general-coverage-analysis','exec'),ns)
if smrt:ns['VARIANTS']=['base256','base768','view768005']
if high:ns['VARIANTS']=['base1024','view1024005']
r=ns['analyze'](Path(sys.argv[1]).resolve(),Path(sys.argv[2]).resolve())
print('complete',r['complete'],flush=True)
for name,item in r['stages'].items():
 print(name, 'req',item['counters']['requested']['max'], 'overflow',item['counters']['overflow']['max'],'new',item['counters']['new']['median'], item['counters']['new']['max'],flush=True)
 for region,v in item.get('regions',{}).items():
  print(region,'sampled',v['sampled'],'footprint',v['footprint'],'blend',v['actual_coarse_blend']['median'],'fallback',v['fallback_fraction'],flush=True)
for name,c in r['comparisons'].items():
 for scenario in ['static','translate','yaw']:
  f=c.get(scenario,{}).get('frames',[])
  if f:print(name,'vs',c['reference'],scenario,'finer',max(x['sampled_finer'] for x in f),'coarser',max(x['sampled_coarser'] for x in f),'unavailable',max(x['new_unavailable'] for x in f),'matched',all(x['pose_match'] and x['jitter_match'] for x in f),flush=True)
