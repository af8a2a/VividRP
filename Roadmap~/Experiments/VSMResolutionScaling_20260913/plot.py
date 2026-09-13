import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root = Path(__file__).parent
adaptive = json.loads((root / 'results.json').read_text())['windows']
fixed = json.loads((root / 'fixed4/results.json').read_text())['windows']
fig, axes = plt.subplots(1, 2, figsize=(12, 4.4), layout='constrained')
for label, windows, budget, color in [('Adaptive, 256 pages', adaptive, 256, '#c87924'),
                                     ('Adaptive, 1024 pages', adaptive, 1024, '#2379b2'),
                                     ('Fixed 4 rays, 1024 pages', fixed, 1024, '#9163b2')]:
    selected = [w for w in windows if w['capacity'] == budget]
    resolutions = sorted({w['resolution'] for w in selected})
    for ax, marker in zip(axes, ['VSM.Resolve', 'VSM.Allocate']):
        values = [np.median([w['markers'][marker]['p50_ms'] for w in selected if w['resolution'] == r]) for r in resolutions]
        ax.plot(resolutions, values, 'o-', color=color, label=label, linewidth=2)
        ax.set_xscale('log', base=2)
        ax.set_xticks([2048,4096,8192,16384], ['2048','4096','8192','16384'])
        ax.set_xlabel('Virtual resolution per clipmap')
        ax.set_ylabel('GPU milliseconds (median of window medians)')
        ax.grid(alpha=.22)
axes[0].set_title('Resolve: finer resident depth raises cost')
axes[1].set_title('Allocator: dense virtual-table work grows')
axes[0].annotate('All receivers fall back to level 9\nin the 256-page diagnostic', xy=(8192,1.41), xytext=(3600,7.5),
                 fontsize=9, arrowprops={'arrowstyle':'->','color':'#555'})
axes[0].legend(loc='upper left', fontsize=8)
fig.suptitle('VividRP resolution scaling | RTX 5070 Ti | 1080p | 7.1 degree light', fontsize=13)
fig.savefig(root / 'resolution-scaling.png', dpi=160)
