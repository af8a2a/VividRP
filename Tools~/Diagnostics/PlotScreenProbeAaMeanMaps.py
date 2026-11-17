"""Plot finite-window, same-code final mean-map differences (not reference error)."""
import argparse
import json
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

from AnalyzeScreenProbeColdReplay import load


def final_mean(root):
    capture = load(root/'capture.json')
    assert capture['complete'] and not capture['errors']
    rows = sorted((r for r in capture['records'] if r.get('tag') == 'oracle-final'
                   and r.get('side') == 'candidate'), key=lambda r: r['frame'])
    assert len(rows) == 128
    values = [np.fromfile(r['path'], '<f2').astype(np.float64).reshape(-1, 4) for r in rows]
    return np.mean([v[:, :3]*v[:, 3:4] for v in values], axis=0) @ np.array([.2126, .7152, .0722])


def plot(reports, output):
    assert not output.exists()
    fig, axes = plt.subplots(len(reports), 4, figsize=(13, 3.5*len(reports)), squeeze=False, layout='constrained')
    for row_index, report_path in enumerate(reports):
        report = load(report_path)
        assert report['qualityAccepted'] is False and len(report['comparisons']) == 1
        comparison = report['comparisons'][0]
        assert comparison['finalMeanMap'] and not comparison['finalMeanMap']['acceptanceEligible']
        roots = [Path(report['baseline']), Path(comparison['other'])]
        targets = load(roots[0]/'targets.json')
        assert targets == load(roots[1]/'targets.json')
        a, b = (final_mean(root) for root in roots)
        for column, region in enumerate(comparison['finalMeanMap']['regions']):
            mask = np.array([t['region'] == region['region'] for t in targets])
            selected = [t for t, take in zip(targets, mask) if take]
            xs, ys = (sorted({t[key] for t in selected}) for key in ('x', 'y'))
            assert len(xs)*len(ys) == len(selected)
            scale = (a[mask].mean()+b[mask].mean())*.5
            delta = (b[mask]-a[mask])/scale*100
            image = np.full((len(ys), len(xs)), np.nan)
            for t, value in zip(selected, delta):
                image[ys.index(t['y']), xs.index(t['x'])] = value
            assert np.isfinite(image).all()
            ax = axes[row_index, column]
            artist = ax.imshow(image, origin='lower', cmap='RdBu_r', vmin=-5, vmax=5,
                               extent=[min(xs), max(xs), min(ys), max(ys)], aspect='auto', interpolation='nearest')
            ax.set_title(f"{region['region']} | RMS {region['meanMapRmsPercent']:.3f}%\nmean delta {region['energyDeltaPercent']:+.3f}%")
            ax.set_xlabel('pixel x')
            if column == 0:
                ax.set_ylabel(report_path.stem+'\npixel y')
    fig.colorbar(artist, ax=axes, label='(mean B - mean A) / ROI mean [%]', shrink=.75)
    fig.suptitle('Same-code final GI: 1024 warm-up + 128-frame means\nSampled ROI grids; not physical-reference error or converged bias')
    fig.savefig(output, dpi=160)
    plt.close(fig)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reports', nargs='+', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    plot(args.reports, args.output)
