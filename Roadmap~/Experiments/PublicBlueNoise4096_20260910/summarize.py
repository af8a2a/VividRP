from pathlib import Path
import json,numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=Path('Temp~/public-noise/analysis');s=json.loads((p/'summary.json').read_text());ref=json.loads((p/'reference-comparison.json').read_text())
names=['bnd','stbn_scalar','stbn_vec2','fast_scalar','fast_vec2'];regions=['roof','ledge','arches'];labels=['BND 256','STBN scalar 64','STBN vec2 64','FAST scalar 32','FAST vec2 32']
decision={'default':'BND retained','scope':'Public scalar or paired vector2 textures drive the existing four-dimensional stratified SMRT phase estimator. All texture bytes are publicly pregenerated; no generator used for these variants. Vector2 preserves paired RG phases, not a direct cosine-disk ray replacement.', 'variants':{},'reference_uncertainty':ref['reference_mc_uncertainty_rms']}
for name in names:
 static=s['stages'][name+'_static'];base=s['stages']['bnd_static']; row={}
 for n in regions:
  row[n]={'final_output_temporal_rms_change_percent':100*(static['temporal']['output'][n]['same_jitter_rms']/base['temporal']['output'][n]['same_jitter_rms']-1),
   'final_output_mean_change_percent':100*(static['temporal']['output'][n]['mean']/base['temporal']['output'][n]['mean']-1),
   'raw_shadow_fixed_phase_mean_reference_error_rms':ref['samplers'][name][n]['fixed_phase_mean_error_rms'],
   'raw_shadow_single_frame_reference_error_rms':ref['samplers'][name][n]['single_frame_error_rms']}
  for scenario in ['angle','light']:
   rows=s['stages'][name+'_'+scenario]['post_stop'];baseRows=s['stages']['bnd_'+scenario]['post_stop']
   def measure(items):return float(np.sqrt(np.mean([x['error']['output'][n]**2 for x in items if x['after_stop']<32])))
   row[n][scenario+'_early_recovery_rms']=measure(rows)
   row[n][scenario+'_early_recovery_change_percent']=100*(measure(rows)/measure(baseRows)-1)
 decision['variants'][name]=row
(p/'decision.json').write_text(json.dumps(decision,indent=2))
fig,axs=plt.subplots(1,3,figsize=(13.5,3.7));x=np.arange(len(names));colors=['#356a9a','#b97330','#508655']
for i,n in enumerate(regions):
 offset=(i-1)*.24
 axs[0].bar(x+offset,[decision['variants'][k][n]['final_output_temporal_rms_change_percent'] for k in names],width=.23,color=colors[i],label=n)
 axs[1].bar(x+offset,[decision['variants'][k][n]['raw_shadow_fixed_phase_mean_reference_error_rms'] for k in names],width=.23,color=colors[i],label=n)
 axs[2].bar(x+offset,[decision['variants'][k][n]['raw_shadow_single_frame_reference_error_rms'] for k in names],width=.23,color=colors[i],label=n)
for ax in axs:
 ax.set_xticks(x,labels,rotation=25,ha='right');ax.grid(axis='y',alpha=.2);ax.set_axisbelow(True)
axs[0].set(title='TSR output: temporal noise change',ylabel='% vs BND (lower is less flicker)');axs[0].legend(fontsize=8)
axs[1].set(title='Raw shadow: fixed-phase mean error',ylabel='RMS vs 1024-estimate reference')
axs[2].set(title='Raw shadow: total single-frame error',ylabel='RMS (variance + fixed residual)')
fig.tight_layout();fig.savefig(p/'comparison.png',dpi=160);plt.close(fig)
fig,axes=plt.subplots(2,3,figsize=(13,6),sharex=True)
for j,scenario in enumerate(['angle','light']):
 for i,n in enumerate(regions):
  ax=axes[j,i]
  for name,label in zip(names,labels):
   rows=s['stages'][name+'_'+scenario]['post_stop']
   ax.plot([r['after_stop'] for r in rows],[r['error']['output'][n] for r in rows],label=label,lw=1)
  ax.set_title(scenario+' recovery / '+n);ax.set_ylabel('Output RMS vs same-sampler static');ax.set_xlim(0,64);ax.grid(alpha=.2)
  if j==1:ax.set_xlabel('Frames after restore')
axes[0,0].legend(fontsize=8);fig.tight_layout();fig.savefig(p/'dynamic-response.png',dpi=160);plt.close(fig)
print(json.dumps(decision,indent=2))
