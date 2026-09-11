from pathlib import Path
import json,numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=Path('Temp~/joint-sequence/analysis');s=json.loads((p/'summary.json').read_text());ref=json.loads((p/'reference-comparison.json').read_text())
names=['bnd','stbn_shared_native','stbn_shared_joint','stbn_ray_native','stbn_ray_joint','fast_shared_native','fast_shared_joint','fast_ray_native','fast_ray_joint']
names=[n for n in names if n in ref['variants'] and n+'_static' in s['stages']]
regions=['roof','ledge','arches'];result={'variants':{},'factorial':{},'reference_uncertainty':ref['uncertainty']}
for name in names:
 a=s['stages'][name+'_static'];b=s['stages']['bnd_static']
 result['variants'][name]={n:{'output_temporal_change_percent':100*(a['temporal']['output'][n]['same_jitter_rms']/b['temporal']['output'][n]['same_jitter_rms']-1),'fixed_change_percent':100*(ref['variants'][name][n]['fixed_rms']/ref['variants']['bnd'][n]['fixed_rms']-1),**ref['variants'][name][n]} for n in regions}
for atlas in ['stbn','fast']:
 for mapping in ['shared','ray']:
  native=atlas+'_'+mapping+'_native';joint=atlas+'_'+mapping+'_joint'
  if native in names and joint in names:
   result['factorial'][atlas+'_'+mapping]={n:{'joint_vs_native_fixed_change_percent':100*(ref['variants'][joint][n]['fixed_rms']/ref['variants'][native][n]['fixed_rms']-1),'joint_vs_native_output_change_percent':100*(s['stages'][joint+'_static']['temporal']['output'][n]['same_jitter_rms']/s['stages'][native+'_static']['temporal']['output'][n]['same_jitter_rms']-1)} for n in regions}
(p/'decision.json').write_text(json.dumps(result,indent=2))
fig,axs=plt.subplots(1,3,figsize=(16,5));y=np.arange(len(names));colors=['#356a9a','#b97330','#508655']
for i,n in enumerate(regions):
 off=(i-1)*.25
 for j,key in enumerate(['output_temporal_change_percent','fixed_rms','raw_single_frame_rms']):axs[j].barh(y+off,[result['variants'][k][n][key] for k in names],height=.24,color=colors[i],label=n)
labels=[n.replace('shared','set').replace('_',' ') for n in names]
for ax in axs:ax.set_yticks(y,labels);ax.invert_yaxis();ax.grid(axis='x',alpha=.2);ax.set_axisbelow(True)
axs[0].set(title='TSR temporal noise',xlabel='% vs BND (lower is better)');axs[0].legend(fontsize=8)
axs[1].set(title='Raw shadow fixed residual',xlabel='RMS vs independent reference')
axs[2].set(title='Raw shadow single-frame error',xlabel='RMS (variance + fixed residual)')
fig.tight_layout();fig.savefig(p/'comparison.png',dpi=160)
for name in names:print(name,{n:{k:round(v,4) for k,v in x.items() if k in ['output_temporal_change_percent','fixed_change_percent']} for n,x in result['variants'][name].items()})
