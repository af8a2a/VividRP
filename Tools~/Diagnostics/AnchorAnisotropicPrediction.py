"""Predict geometry-guarded axis filtering from captured GPU energy, not synthetic GI."""
import argparse
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from ProbeAnchorCalibration import evaluate_sh, normal_from_packed
from ScalarAnchorPreFilterExperiment import grid_reference
from ScalarAnchorSpatialPrediction import connections, filter_flux
from TemporalSamplingExperiment import LUMA, paired, read


def guard(geometry, rx, ry, clip_viewport=False):
    g = geometry[:68, :120]; adaptive = geometry[68:136, :120]
    n = normal_from_packed(np.maximum(g[..., 3], 0))
    yy, xx = np.mgrid[:68, :120]
    result = g[..., 3] < 0
    for dy in range(-ry, ry+1):
        for dx in range(-rx, rx+1):
            ox, oy = xx+dx, yy+dy
            outside = (ox < 0) | (ox >= 120) | (oy < 0) | (oy >= 68)
            if not clip_viewport:
                result |= outside
            ox, oy = np.clip(ox, 0, 119), np.clip(oy, 0, 67)
            other, on = g[oy, ox], n[oy, ox]
            delta = other[..., :3]-g[..., :3]
            tolerance = .002+.008*np.linalg.norm(delta, axis=-1)
            plane = np.maximum(abs(np.sum(delta*n, -1)), abs(np.sum(delta*on, -1)))
            failed = (other[..., 3] < 0) | (np.sum(n*on, -1) < .995) | (plane >= tolerance) | (adaptive[oy, ox, 3] >= 0)
            result |= failed & ~outside if clip_viewport else failed
    return result


def predict(root, reference, output, radius_scale=1.0, axis_fallback=False, clip_viewport=False):
    if not np.isfinite(radius_scale) or radius_scale <= 0:
        raise ValueError('Expected a positive finite radius scale')
    m = json.loads((root/'capture.json').read_text(encoding='utf-8'))
    assert m['complete'] and not m['errors'] and m['spacing'] == 16 and m['rays'] == 8
    first = m['warmup']; frames = range(first, first+m['measuredFrames']); arm = 'baseline-b'
    f = {(r['arm'], r['frame'], r['tag']): r for r in m['records'] if 'tag' in r}
    g = read(f[arm, first, 'geometry'])
    assert all(np.array_equal(read(f[arm, i, 'geometry']), g) for i in frames)
    vp = np.asarray(next(r['vp'] for r in m['records'] if r.get('arm') == arm and 'vp' in r)).reshape(4, 4)
    links = connections(g, vp)
    wide_links = connections(g, vp, radius_scale)
    normalization = 1.0/sum(2**(-.55*x*x) for x in range(-2, 3))
    axis_links = []
    for axis in (0, 1):
        selected = []
        for center, other, weight in wide_links:
            same = center//120 == other//120 if axis == 0 else center%120 == other%120
            selected.append((center[same], other[same], weight[same]*normalization/.12743124892))
        axis_links.append(selected)
    a = np.stack([read(f[arm, i, 'anchor'])[:68, :120] for i in frames])
    s = np.stack([read(f[arm, i, 'statistics'])[:68, :120] for i in frames])
    full = filter_flux(a, s, links)
    half = ((a+full)*.5).astype('<f2').astype(np.float32)
    cascade = filter_flux(half, s, links)
    protected = guard(g, 4, 4)
    candidate_protected = guard(g, 4, 4, clip_viewport)
    horizontal = candidate_protected & ~guard(g, 4, 2, clip_viewport)
    vertical = candidate_protected & ~horizontal & ~guard(g, 2, 4, clip_viewport)
    baseline = np.where(protected[None, ..., None], full, cascade)
    wide_half = ((a+filter_flux(a, s, wide_links))*.5).astype('<f2').astype(np.float32)
    candidate = np.where(candidate_protected[None, ..., None], full, filter_flux(wide_half, s, links))
    if not axis_fallback:
        horizontal[:], vertical[:] = False, False
    for mask, selected in zip([horizontal, vertical], axis_links):
        step = ((a+filter_flux(a, s, selected))*.5).astype('<f2').astype(np.float32)
        reconstruction = filter_flux(step, s, links)
        candidate[:, mask] = reconstruction[:, mask]
    target, target_variance, _ = grid_reference(reference, Path(f[arm, first, 'radiosity']['path']).read_bytes(), g)
    masks = plane_masks(g)
    normal = normal_from_packed(np.maximum(g[:68, :120, 3], 0)).reshape(1, -1, 3)
    sh = read(f[arm, first, 'sh'])[:68].reshape(68, 7, 120, 4).transpose(1, 0, 2, 3).reshape(1, 7, -1, 4)
    actual = evaluate_sh(sh, normal).reshape(68, 120, 3)*a[0, ..., 3:4]
    energies = [v[..., :3]*v[..., 3:4] for v in [baseline, candidate]]
    report = dict(scope='Offline prediction only; actual SH calibration and final pixel/motion validation required',
                  normalization1D=normalization, radiusScale=radius_scale, axisFallback=axis_fallback, clipViewport=clip_viewport, baseline_validation={}, planes={})
    for name, mask in masks.items():
        delta = (energies[0][0]-actual)[mask]@LUMA
        denominator = np.mean(actual[mask]@LUMA)
        report['baseline_validation'][name] = dict(mean_error_percent=float(100*delta.mean()/denominator),
            normalized_rms_percent=float(100*np.sqrt(np.mean(delta**2))/denominator))
        row = paired(*[(v@LUMA)[:, mask] for v in energies])
        row.update(probes=int(mask.sum()), baseline_eligible=int((mask & ~protected).sum()),
                   viewport_unblocked=int((mask & protected & ~candidate_protected).sum()),
                   additional_horizontal=int((mask & horizontal).sum()), additional_vertical=int((mask & vertical).sum()))
        row['reference'] = {}
        for label, value in zip(['baseline', 'candidate'], energies):
            difference = ((value-target)@LUMA)[:, mask]
            row['reference'][label] = dict(mean_error_percent=float(100*difference.mean()/(target@LUMA)[mask].mean()),
                per_frame_rmse=float(np.sqrt(np.mean(difference**2))), mean_field_rmse=float(np.sqrt(np.mean(difference.mean(0)**2))))
        report['planes'][name] = row
    # Preserve local energy checks at the same probe footprints as final pixel ROIs.
    clip = np.concatenate([g[:68, :120, :3], np.ones((68, 120, 1))], -1)@vp.T
    pixels = np.stack([.5+.5*clip[..., 0]/clip[..., 3], .5-.5*clip[..., 1]/clip[..., 3]], -1)*[1920, 1080]
    report['contact_probe_footprints'] = {}
    canonical_contacts = {'short-contact': (720, 64, 64, 64), 'tall-contact': (1093, 153, 64, 64), 'wall-corner': (1288, 468, 64, 64)}
    for region in ['short-contact', 'tall-contact', 'wall-corner']:
        captured = (arm, first, 'final-'+region) in f
        meta = f[arm, first, 'final-'+region] if captured else dict(zip(['x', 'y', 'width', 'height'], canonical_contacts[region]))
        mask = (g[:68, :120, 3] >= 0) & np.all(pixels >= [meta['x'], meta['y']], -1) & np.all(pixels < [meta['x']+meta['width'], meta['y']+meta['height']], -1)
        report['contact_probe_footprints'][region] = dict(pixelDataCaptured=captured, probes=int(mask.sum()),
            changed=int((mask&((protected&~candidate_protected)|horizontal|vertical)).sum()),
            mean_change_percent=float(100*((energies[1]-energies[0])@LUMA)[:, mask].mean()/(energies[0]@LUMA)[:, mask].mean()))
    output.mkdir(parents=True, exist_ok=False)
    (output/'prediction.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path); parser.add_argument('reference', type=Path); parser.add_argument('output', type=Path)
    parser.add_argument('--radius-scale', type=float, default=1.0); parser.add_argument('--axis-fallback', action='store_true')
    parser.add_argument('--clip-viewport', action='store_true')
    args = parser.parse_args(); predict(args.capture, args.reference, args.output, args.radius_scale, args.axis_fallback, args.clip_viewport)
