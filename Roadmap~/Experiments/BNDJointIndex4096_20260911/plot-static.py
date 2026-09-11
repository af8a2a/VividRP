from pathlib import Path
import json,numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=Path('Temp~/bnd-joint/analysis');s=json.loads((p/'summary.json').read_text());names=list(s['stages']);regions=['roof','ledge','arches'];colors=['#3676a4','#d48632','#4f956e']
fig,axs=plt.subplots(1,3,figsize=(14,4.8))
for j,n in enumerate(regions):
 for i,v in enumerate(names):
  off=i+(j-1)*.22
  axs[0].barh(off,s['stages'][v]['regions'][n]['fixed_rms'],height=.21,color=colors[j],label=n if i==0 else None)
  axs[1].barh(off,s['change_percent'][v][n]['output_temporal_rms'],height=.21,color=colors[j])
for ax in axs[:2]:ax.set_yticks(range(len(names)),[v.replace('_',' ') for v in names]);ax.invert_yaxis();ax.grid(axis='x',alpha=.2);ax.set_axisbelow(True)
axs[0].set(title='Raw shadow fixed residual',xlabel='RMS vs independent reference');axs[0].legend(fontsize=8)
axs[1].set(title='TSR temporal noise',xlabel='% vs native BND (lower is better)')
for i,v in enumerate(names):
 r=s['stages'][v]['prefix_fixed_rms'];x=list(map(int,r));y=[r[str(k)]['ledge'] for k in x];axs[2].loglog(x,y,'o-',label=v.replace('_',' '))
axs[2].set(title='Ledge: convergence by window length',xlabel='Recorded frames (8 jitter phases)',ylabel='Raw shadow fixed residual RMS');axs[2].grid(alpha=.2);axs[2].legend(fontsize=8);fig.tight_layout();fig.savefig(p/'comparison.png',dpi=150)
