from pathlib import Path
import json
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

root=Path('Roadmap~/Experiments/VSMDynamicCache_20260915/timing')
data=json.loads((root/'summary.json').read_text())['comparisons']
fig,axes=plt.subplots(2,2,figsize=(12,7.2),layout='constrained')
titles=[('VSM.ClearPhysicalPages','Physical page clear'),('VSM.PageOccupancy','Page occupancy'),('VSM.DynamicRasterDraw','Dynamic raster draw'),('VSM.InvalidateDynamic','Dynamic invalidation')]
labels=[f"{x['resolution']}\n{['Empty','Still','Moving'][x['scenario']]}" for x in data]
for ax,(marker,title) in zip(axes.flat,titles):
    for mode,offset,color,label in [('forced',-.17,'#8b97a5','Forced refresh'),('reuse',.17,'#087f8c','Dynamic reuse')]:
        vals=np.array([x['metrics'][marker][mode+'_window_p50_ms'] for x in data])*1000
        centers=vals.mean(axis=1)
        ax.bar(np.arange(6)+offset,centers,.30,color=color,label=label)
        ax.plot(np.arange(6)+offset,vals[:,0],'.',color='#182a3a',markersize=3)
        ax.plot(np.arange(6)+offset,vals[:,1],'.',color='#182a3a',markersize=3)
    ax.set_title(title,loc='left',fontweight='bold',fontsize=12)
    ax.set_xticks(np.arange(6),labels,fontsize=9);ax.set_ylabel('GPU time (microseconds)')
    ax.grid(axis='y',alpha=.18);ax.set_axisbelow(True)
    ax.spines[['top','right']].set_visible(False)
axes[0,0].legend(frameon=False,fontsize=9)
fig.suptitle('VSM dynamic cache: same-build A/B/B/A',fontsize=17,fontweight='bold')
fig.supxlabel('Sponza + one opaque dynamic cube | RTX 5070 Ti | 1024 physical pages, 16 layers/pool\nBars: mean of two window medians; dots: each window median. 64 warm-up + 96 observations/window.',fontsize=10)
fig.savefig(root/'stage-timing.png',dpi=180)
fig.savefig(root/'stage-timing.svg')
print('Saved stage-timing.png and stage-timing.svg')
