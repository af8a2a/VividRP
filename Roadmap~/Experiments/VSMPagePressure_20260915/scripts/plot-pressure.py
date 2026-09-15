from pathlib import Path
import json
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root = Path(__file__).resolve().parents[1]
rows = [r for r in json.loads((root / 'budget-sweep/frames.json').read_text()) if r['adaptive']]
x = list(range(len(rows)))
fig, axes = plt.subplots(3, 1, figsize=(12, 8), sharex=True,
                         gridspec_kw={'height_ratios': [2, 1, 1]}, layout='constrained')
fig.suptitle('VSM page pressure: stable density across budget changes', fontsize=17, weight='bold')
axes[0].plot(x, [r['budget'] for r in rows], color='#39465a', ls='--', label='Physical page budget')
axes[0].plot(x, [r['essential'] for r in rows], color='#007c91', label='Required page demand')
axes[0].set_ylabel('Pages')
axes[0].legend(loc='upper right', frameon=False)
axes[1].plot(x, [r['bias'] for r in rows], color='#8753b4')
axes[1].set_ylabel('Adaptive LOD bias\n(higher = coarser)')
axes[2].plot(x, [r['essentialMiss'] for r in rows], color='#ba4a36')
axes[2].set_ylabel('Required misses')
axes[2].set_yscale('symlog', linthresh=1)
axes[2].set_xlabel('Observed camera renders (480 per adaptive budget window)')
for n, budget in enumerate([128, 256, 512, 1024, 512, 256, 128]):
    for ax in axes:
        if n % 2 == 0: ax.axvspan(n * 480, (n + 1) * 480, color='#dbe4ee', alpha=.3, zorder=-1)
        if n: ax.axvline(n * 480, color='#bbc5d2', lw=.6)
    axes[0].text((n + .5) * 480, 1.02, str(budget), ha='center',
                 transform=axes[0].get_xaxis_transform(), fontsize=10)
for ax in axes:
    ax.grid(axis='y', alpha=.2)
    ax.spines[['top', 'right']].set_visible(False)
    ax.set_xlim(0, len(rows) - 1)
fig.savefig(root / 'budget-trajectory.png', dpi=150)
fig.savefig(root / 'budget-trajectory.svg')
print(root / 'budget-trajectory.png')
