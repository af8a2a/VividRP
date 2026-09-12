"""Static publication-style plot from the two archived capacity analyses."""
import json
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.ticker import PercentFormatter
import numpy as np

here = Path(__file__).resolve().parent
actual = json.loads((here / 'capacity-analysis.json').read_text(encoding='utf-8'))
reference = json.loads((here / 'capacity-reference-analysis.json').read_text(encoding='utf-8'))
assert actual['validation']['all_invariants'] == 'pass'
assert reference['status'] == 'reference-gpu-validation-pass'
static = actual['pools']['static']
histogram = np.array(static['occupied_layer_histogram_0_to_16'], dtype=float)
histogram /= histogram.sum()
levels = np.arange(len(static['clipmaps']))
saturation = np.array([c['last_layer_nonempty_fraction_all_valid_texels'] or 0 for c in static['clipmaps']])
geometric = np.array([c['geometric_unique_buckets_gt16_fraction'] or 0 for c in reference['pools']['static']['clipmaps']])

plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 10, 'axes.spines.top': False,
    'axes.spines.right': False, 'axes.titleweight': 'bold', 'axes.labelcolor': '#263447',
    'xtick.color': '#435268', 'ytick.color': '#435268', 'axes.edgecolor': '#ccd3dc', 'figure.facecolor': '#fafbfd'})
fig, axes = plt.subplots(1, 2, figsize=(13, 5.9), gridspec_kw={'width_ratios': [1, 1.16]})
fig.subplots_adjust(left=.067, right=.98, bottom=.235, top=.74, wspace=.25)
fig.text(.067, .94, '16-layer directional VSM: capacity baseline', fontsize=20, weight='bold', color='#13243a')
fig.text(.067, .89, 'Sponza | 2048 virtual resolution | 256 physical pages | independent static and dynamic pools', fontsize=11, color='#526177')
fig.text(.067, .835, '512 MiB reserved depth pools  /  142.56 MiB nonzero payload (27.84%)  /  dynamic pool entirely empty in this frame',
    fontsize=10.5, color='#263447')

ax = axes[0]
colors = ['#315b94'] * 16 + ['#d87d2c']
ax.bar(np.arange(17), histogram, width=.78, color=colors, zorder=3)
ax.set(title='Stored layers per texel: static pool', xlabel='Number of nonzero depth slots', ylabel='Fraction of all allocated-page texels')
ax.set_xticks(range(0, 17, 2)); ax.set_ylim(0, .25); ax.set_xlim(-.65, 16.65)
ax.yaxis.set_major_formatter(PercentFormatter(1, decimals=0)); ax.grid(axis='y', color='#e2e7ee', zorder=0)
ax.annotate(f"Full 16 slots\n{histogram[16]:.2%}", xy=(16, histogram[16]), xytext=(12.2, .218),
    fontsize=10, color='#9a5319', arrowprops={'arrowstyle': '-', 'color': '#b56b2a'})
ax.text(.03, .95, 'Mean 8.91 layers  |  median 9', transform=ax.transAxes, va='top', color='#526177', fontsize=10)

ax = axes[1]
ax.bar(levels - .19, saturation, width=.36, label='Actual last layer nonempty (all texels)', color='#315b94', zorder=3)
ax.bar(levels + .19, geometric, width=.36, label='Reference >16 depth buckets (8x8/page)', color='#d87d2c', zorder=3)
ax.set(title='Depth capacity pressure by clipmap: static pool', xlabel='Clipmap index', ylabel='Fraction of sampled / scanned texels')
ax.set_xticks(levels); ax.set_ylim(0, .40); ax.set_xlim(-.65, len(levels) - .35)
ax.yaxis.set_major_formatter(PercentFormatter(1, decimals=0)); ax.grid(axis='y', color='#e2e7ee', zorder=0)
ax.legend(loc='upper right', fontsize=8.7, frameon=False)
ax.text(0, .01, 'No\npages', ha='center', va='bottom', fontsize=8, color='#718096')

fig.text(.067, .13, 'Full last layer is not a discard count. Geometric >16 = 2,294 / 16,384 samples (14.00%); largest count 37; no 65+ truncation.',
    fontsize=9.7, color='#263447')
fig.text(.067, .088, 'Separate completed static frames. Geometric reference uses two-sided opaque source triangles and 0.1 mm depth buckets; it does not reproduce alpha/cull/raster coverage.',
    fontsize=9, color='#526177')
fig.text(.067, .048, 'Nonzero payload is stored occupancy, not useful-shadow contribution or realizable compression savings. Both GPU diagnostic fixture suites passed.',
    fontsize=9, color='#526177')
output = here / 'capacity-baseline.png'
fig.savefig(output, dpi=180)
plt.close(fig)
print(output)
