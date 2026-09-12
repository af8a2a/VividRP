#!/usr/bin/env python3
"""Analyze paired VSM dynamic captures; finite RTAS quadrature is a reference, not exact truth."""
import argparse
import gzip
import json
import math
import re
from pathlib import Path
import numpy as np

MODES = ('fixed4', 'fixed8', 'adaptive')


def stats(values):
    values = np.asarray(values, dtype=np.float64)
    if values.size == 0:
        return {'count': 0, 'mean': None, 'median': None, 'p95': None, 'p99': None, 'max': None}
    return {'count': int(values.size), 'mean': float(values.mean()), 'median': float(np.median(values)),
            'p95': float(np.quantile(values, .95)), 'p99': float(np.quantile(values, .99)), 'max': float(values.max())}


def numeric(value):
    if isinstance(value, dict):
        return np.array([value[key] for key in sorted(value)], dtype=np.float64)
    return np.asarray(value, dtype=np.float64)


def rotation_y(degrees):
    angle = math.radians(degrees); c, sn = math.cos(angle), math.sin(angle)
    return np.array([[c,0,sn],[0,1,0],[-sn,0,c]])


def unity_euler_rotation(value):
    x, y, z = (math.radians(value[key]) for key in ('x','y','z'))
    cx,sx,cz,sz = math.cos(x),math.sin(x),math.cos(z),math.sin(z)
    rx = np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])
    rz = np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])
    return rotation_y(math.degrees(y)) @ rx @ rz


def check_plan(frames, count, tolerance=1e-4):
    issues = []
    first = frames[0]; scenario = first['scenario']
    base_position = numeric(first['cameraPosition'])
    base_rotation = unity_euler_rotation(first['cameraEuler'])
    base_light = numeric(first['lightDirection'])
    base_fixture = numeric(first['fixturePosition'])
    max_motion = 0.0
    for frame in frames:
        step = frame['step']; motion = min(1.0, max(0.0, (step - 4.0) / max(1.0, count - 9.0)))
        max_motion = max(max_motion, motion)
        position = base_position + (base_rotation[:,0] * (.7 * motion) if scenario == 'camera_slide' else 0)
        rotation = rotation_y(7 * motion) @ base_rotation if scenario == 'camera_turn' else base_rotation
        light = rotation_y(3 * motion) @ base_light if scenario == 'sun_rotate' else base_light
        if scenario == 'sun_step' and step >= 8: light = rotation_y(3) @ base_light
        fixture = base_fixture.copy()
        if scenario == 'thin_sweep': fixture[0] += 3 * motion
        if scenario in ('receiver_slide', 'receiver_lift'): fixture[0] += 1.4 * motion
        if scenario == 'receiver_lift': fixture[1] += .6 * motion
        for key, actual, expected in (
            ('camera position',numeric(frame['cameraPosition']),position),
            ('camera rotation',unity_euler_rotation(frame['cameraEuler']),rotation),
            ('light direction',numeric(frame['lightDirection']),light),
            ('fixture position',numeric(frame['fixturePosition']),fixture)):
            if float(np.max(abs(actual - expected))) > tolerance: issues.append(f'{key} differs from scenario plan at step {step}')
        if 'fixturePhase' in frame and abs(frame['fixturePhase'] - motion * 6.283185) > tolerance:
            issues.append(f'fixture phase differs from scenario plan at step {step}')
    return {'valid': not issues, 'issues': issues, 'maximum_plan_progress': max_motion,
            'full_motion_plan_observed': scenario == 'static' or max_motion == 1.0}


def check_frames(frames, expected_count):
    issues = []
    if len(frames) != expected_count:
        issues.append(f'frame_count={len(frames)}, expected={expected_count}')
    for i, frame in enumerate(frames):
        if frame['step'] != i: issues.append(f'step mismatch at {i}')
        if frame['phase'] != (frame['frame'] & 255): issues.append(f'frame/phase mismatch at {i}')
        if frame['phase'] != (i & 255): issues.append(f'phase not aligned to step at {i}')
        if i and frame['frame'] != frames[i - 1]['frame'] + 1: issues.append(f'nonconsecutive frame at {i}')
        if frame['state'] != 'Active' or frame['fallback'] != 'None': issues.append(f'VSM fallback at {i}')
        expected_rays = 8 if frame['mode'] == 'fixed8' else 4
        if frame['smrt']['x'] != expected_rays: issues.append(f'wrong production ray count at {i}')
        if i:
            for key in ('scenario', 'mode', 'width', 'height', 'gridWidth', 'gridHeight', 'pages', 'levels', 'referenceRays'):
                if frame[key] != frames[0][key]: issues.append(f'{key} changed at {i}')
    return issues


def pair_metadata(left, right, tolerance, allow_reference_rays_difference=False):
    issues = []
    maximum = {}
    if len(left) != len(right): return {'valid': False, 'issues': ['different frame counts'], 'maximum_delta': {}}
    scalar = ('step', 'phase', 'scenario', 'width', 'height', 'gridWidth', 'gridHeight', 'pages', 'levels', 'referenceRays')
    if allow_reference_rays_difference:
        scalar = tuple(key for key in scalar if key != 'referenceRays')
    vector = ('cameraPosition', 'cameraEuler', 'lightDirection', 'fixturePosition', 'viewProjection', 'quality')
    for i, (a, b) in enumerate(zip(left, right)):
        for key in scalar:
            if a[key] != b[key]: issues.append(f'{key} mismatch at step {i}')
        for key in vector:
            delta = float(np.max(np.abs(numeric(a[key]) - numeric(b[key]))))
            maximum[key] = max(maximum.get(key, 0), delta)
            if delta > tolerance: issues.append(f'{key} mismatch at step {i}: {delta}')
        # x is intentionally 4 versus 8; y/z/w must describe the same estimator.
        delta = max(abs(a['smrt'][key] - b['smrt'][key]) for key in ('y', 'z', 'w'))
        maximum['smrt_yzw'] = max(maximum.get('smrt_yzw', 0), delta)
        if delta > tolerance: issues.append(f'SMRT path settings mismatch at step {i}')
        for key in ('fixtureBackend', 'fixturePhase', 'deformPhase', 'fixtureGeometryHash', 'geometryHash'):
            if key in a or key in b:
                if a.get(key) != b.get(key): issues.append(f'{key} mismatch at step {i}')
    deformation_recorded = left[0]['scenario'] != 'deform' or any(
        key in left[0] and key in right[0] for key in ('fixturePhase', 'deformPhase', 'fixtureGeometryHash', 'geometryHash'))
    return {'valid': not issues, 'issues': issues, 'maximum_delta': maximum,
            'fixture_backends': sorted(set(frame.get('fixtureBackend','not-recorded') for frame in left + right)),
            'deformation_state_recorded': deformation_recorded,
            'deformation_note': None if deformation_recorded else 'Only step and fixture pose match; actual deformed vertices/phase were not saved.'}


def load_case(directory, expected_count):
    files = sorted(p for p in directory.glob('frame_*.json') if re.fullmatch(r'frame_\d{3}\.json', p.name))
    if not files: raise ValueError(f'No frames: {directory}')
    frames = [json.loads(p.read_text(encoding='utf-8-sig')) for p in files]
    arrays = {}
    h, w = frames[0]['gridHeight'], frames[0]['gridWidth']
    for kind in ('reference', 'signal', 'world'):
        data = []
        for path in files:
            blob = gzip.decompress(path.with_name(path.stem + '_' + kind + '.gz').read_bytes())
            values = np.frombuffer(blob, dtype='<f4')
            if values.size != h * w * 4: raise ValueError(f'Wrong grid byte length: {path}/{kind}')
            data.append(values.reshape(h, w, 4))
        arrays[kind] = np.stack(data)
    return frames, arrays, check_frames(frames, expected_count)


def valid_mask(data):
    r, s, w = data['reference'], data['signal'], data['world']
    return (s[..., 3] > .5) & np.isfinite(s).all(-1) & np.isfinite(w).all(-1) & np.isfinite(r).all(-1) & (r[..., :2] >= 0).all(-1)


def select_floor(data, explicit):
    world = data['world'][0]
    mask = valid_mask(data)[0] & (world[..., 3] > .9) & (abs(world[..., 1]) < .5)
    heights = world[..., 1][mask]
    if heights.size == 0:
        if explicit is None: raise ValueError('No near-origin horizontal floor samples; pass --floor-y after inspecting world capture.')
        return explicit, {'candidate_count': 0, 'peaks': []}
    bins, counts = np.unique(np.rint(heights / .02).astype(np.int32), return_counts=True)
    order = np.argsort(counts)[::-1][:8]
    peaks = [{'y_bin': float(bins[i] * .02), 'count': int(counts[i])} for i in order]
    chosen = bins[order[0]]
    inferred = float(np.median(heights[np.rint(heights / .02).astype(np.int32) == chosen]))
    return (inferred if explicit is None else explicit), {'candidate_count': int(heights.size), 'inferred_dominant_y': inferred, 'peaks': peaks}


def error_metrics(signal, reference, mask):
    error = (signal - reference)[mask].astype(np.float64)
    if not error.size: return {'count': 0, 'mae': None, 'positive_visibility_error': None, 'negative_visibility_error': None}
    return {'count': int(error.size), 'mae': float(np.abs(error).mean()),
            'positive_visibility_error': float(np.maximum(error, 0).mean()),
            'negative_visibility_error': float(np.maximum(-error, 0).mean()),
            'absolute_error_p95': float(np.quantile(abs(error), .95)),
            'absolute_error_p99': float(np.quantile(abs(error), .99)),
            'absolute_error_max': float(abs(error).max())}


def uniform_neighborhood(reference, eligible, radius, settled):
    # Conservative square envelope; out-of-ROI or missing samples invalidate it.
    bright = eligible & (reference >= 1 - settled)
    dark = eligible & (reference <= settled)
    h, w = reference.shape
    pb = np.pad(bright, radius, constant_values=False)
    pd = np.pad(dark, radius, constant_values=False)
    all_bright = np.ones_like(bright)
    all_dark = np.ones_like(dark)
    for y in range(2 * radius + 1):
        for x in range(2 * radius + 1):
            all_bright &= pb[y:y + h, x:x + w]
            all_dark &= pd[y:y + h, x:x + w]
    return all_bright, all_dark


def dynamic_metrics(data, floor, options, request):
    r = data['reference'][..., 0]
    s = data['signal'][..., 0]
    world = data['world'][..., :3]
    insensitive = abs(data['reference'][..., 0] - data['reference'][..., 1]) <= options.bias_threshold
    good = floor & insensitive
    delta = r[1:] - r[:-1]
    adjacent_floor = floor[1:] & floor[:-1]
    strong = adjacent_floor & (abs(delta) >= options.jump_threshold)
    raw_event = strong & insensitive[1:] & insensitive[:-1]
    continuity = np.linalg.norm(world[1:] - world[:-1], axis=-1) <= options.world_tolerance
    event_mask = raw_event & continuity
    bright_delays, dark_delays, examples = [], [], []
    counts = {'raw_strong_events': int(strong.sum()), 'bias_sensitive_rejected': int((strong & ~raw_event).sum()), 'surface_discontinuity_rejected': int((raw_event & ~continuity).sum()),
              'eligible_events': int(event_mask.sum()), 'output_already_on_new_side': 0, 'detected': 0,
              'not_detected_within_horizon': 0, 'right_censored': 0, 'reference_reversal_or_surface_loss': 0}
    support_residual_observations = 0
    support_eligible_observations = 0
    support_peak_error = 0.0
    support_example_count = 0
    radius = math.ceil(options.support_radius_pixels / request['stride'])
    uniforms = [uniform_neighborhood(r[t], good[t], radius, options.settled_threshold) for t in range(len(r))]
    for t in range(1, len(r)):
        ys, xs = np.nonzero(event_mask[t - 1])
        for y, x in zip(ys.tolist(), xs.tolist()):
            change = float(delta[t - 1, y, x]); direction = 1 if change > 0 else -1
            old, new = float(r[t - 1, y, x]), float(r[t, y, x]); threshold = old + options.detect_fraction * change
            if direction * (float(s[t - 1, y, x]) - threshold) >= 0:
                counts['output_already_on_new_side'] += 1
                continue
            found, invalidated = None, False
            checked_through = -1
            for delay in range(options.max_delay + 1):
                j = t + delay
                if j + options.sustain_frames > len(r): break
                end = j + options.sustain_frames
                valid = good[t:end, y, x].all() and np.all(np.linalg.norm(world[t:end, y, x] - world[t - 1, y, x], axis=-1) <= options.world_tolerance)
                holds = np.all(direction * (r[t:end, y, x] - threshold) >= 0)
                if not valid or not holds:
                    invalidated = True
                    break
                checked_through = delay
                if np.all(direction * (s[j:end, y, x] - threshold) >= 0):
                    found = delay
                    break
            if found is not None:
                counts['detected'] += 1
                (bright_delays if direction > 0 else dark_delays).append(found)
            elif invalidated:
                counts['reference_reversal_or_surface_loss'] += 1
            elif checked_through == options.max_delay:
                counts['not_detected_within_horizon'] += 1
            else:
                counts['right_censored'] += 1
            # Count old-side residual observations only for this actual reference
            # event, while correspondence and reference direction remain valid.
            last = min(len(r), t + options.max_delay + 1)
            for j in range(t, last):
                if not good[j, y, x] or np.linalg.norm(world[j, y, x] - world[t - 1, y, x]) > options.world_tolerance: break
                if direction * (float(r[j, y, x]) - threshold) < 0: break
                uniform = uniforms[j][0 if direction > 0 else 1][y, x]
                support_eligible_observations += int(uniform)
                signed_error = direction * (float(r[j, y, x]) - float(s[j, y, x]))
                if uniform and signed_error > options.residual_threshold:
                    support_residual_observations += 1
                    support_peak_error = max(support_peak_error, signed_error)
                    support_example_count += 1
            if len(examples) < 32:
                examples.append({'step': t, 'grid_x': x, 'grid_y': y,
                    'screen_x': request['originX'] + request['stride'] * x,
                    'screen_y': request['originY'] + request['stride'] * y,
                    'old_reference': old, 'new_reference': new, 'detected_delay': found,
                    'reference_or_surface_lost': invalidated})
    evaluated = counts['detected'] + counts['not_detected_within_horizon']
    all_delays = bright_delays + dark_delays
    return {'status': 'measured_events' if evaluated else 'insufficient_detectable_events',
            'adjacent_reference_change_floor': stats(abs(delta)[adjacent_floor]),
            'nonzero_adjacent_reference_changes_floor': int((adjacent_floor & (delta != 0)).sum()),
            'adjacent_reference_change_world_continuous_bias_insensitive': stats(abs(delta)[adjacent_floor & continuity & insensitive[1:] & insensitive[:-1]]),
            'counts': counts, 'evaluated_events': evaluated,
            'not_detected_fraction': counts['not_detected_within_horizon'] / evaluated if evaluated else None,
            'first_detection_frames_conditional_on_detection': stats(all_delays),
            'brightening_detection_frames': stats(bright_delays), 'darkening_detection_frames': stats(dark_delays),
            'sampled_support_residual': {'eligible_event_observations': support_eligible_observations, 'residual_event_observations': support_residual_observations,
                'residual_fraction': support_residual_observations / support_eligible_observations if support_eligible_observations else None,
                'maximum_old_side_error': support_peak_error if support_example_count else None,
                'full_resolution_support_coverage': request['stride'] == 1,
                'support_radius_pixels': options.support_radius_pixels, 'grid_radius': radius,
                'status': 'insufficient_support_coverage' if not support_eligible_observations else ('sampled_proxy_only' if request['stride'] > 1 else 'full_grid_reference_envelope')},
            'examples': examples}


def receiver_top_metrics(frames, data, options):
    valid = valid_mask(data)
    centers = np.array([[frame['fixturePosition'][axis] for axis in ('x','y','z')] for frame in frames], dtype=np.float64)
    expected_y = centers[:,1] + .02
    delta = data['world'][...,:3] - centers[:,None,None,:]
    geometry = valid & (abs(delta[...,1] - .02) < .002) & (abs(delta[...,0]) <= 1.252) & (abs(delta[...,2]) <= 1.002)
    top = geometry & (data['world'][...,3] > .9)
    sensitive = abs(data['reference'][...,0] - data['reference'][...,1]) > options.bias_threshold
    error = abs(data['signal'][...,0] - data['reference'][...,0])
    per_frame = []
    for t, mask in enumerate(top):
        n = int(mask.sum())
        per_frame.append({'step': frames[t]['step'], 'expected_top_y': float(expected_y[t]),
            'visible_top_samples': n,
            'geometry_candidate_samples_before_normal_filter': int(geometry[t].sum()),
            'reference_bias_0_001': error_metrics(data['signal'][t,...,0],data['reference'][t,...,0],mask),
            'reference_bias_0_01': error_metrics(data['signal'][t,...,0],data['reference'][t,...,1],mask),
            'history_age': stats(data['signal'][t,...,1][mask]),
            'bias_sensitive_fraction': float(sensitive[t][mask].mean()) if n else None,
            'strong_error_fraction': float((error[t][mask] > options.strong_error_threshold).mean()) if n else None})
    count = int(top.sum())
    return {'status': 'visible_top_samples_measured' if count else 'receiver_top_not_observed',
        'roi': 'normalY > 0.9; abs(worldY - (fixturePosition.y + 0.02)) < 0.002; abs(worldX-centerX)<=1.252; abs(worldZ-centerZ)<=1.002. Axis-aligned 2.5 x 0.04 x 2 cube.',
        'overlap_note': 'May overlap the broad floor-height ROI during the initial low lift; these metrics are separate diagnostics, not a partition.',
        'motion_latency_status': 'not_measured: screen-grid samples do not establish material-point motion correspondence; history age or low error is not a low-lag proof',
        'visible_top_samples': count, 'frames_with_top': sum(row['visible_top_samples'] > 0 for row in per_frame),
        'geometry_candidate_samples_before_normal_filter': int(geometry.sum()),
        'geometry_candidates_normal_y': stats(data['world'][...,3][geometry]),
        'normal_contract_coverage_fraction': count / int(geometry.sum()) if geometry.any() else None,
        'reference_bias_0_001': error_metrics(data['signal'][...,0],data['reference'][...,0],top),
        'reference_bias_0_01': error_metrics(data['signal'][...,0],data['reference'][...,1],top),
        'history_age': stats(data['signal'][...,1][top]),
        'bias_sensitive_fraction': float(sensitive[top].mean()) if count else None,
        'strong_error_threshold': options.strong_error_threshold,
        'strong_error_fraction': float((error[top] > options.strong_error_threshold).mean()) if count else None,
        'per_frame': per_frame}


def analyze_case(frames, data, issues, floor_y, options, request):
    plan = check_plan(frames, request['frames'], options.pair_tolerance)
    issues = issues + plan['issues']
    valid = valid_mask(data)
    floor = valid & (data['world'][..., 3] > .9) & (abs(data['world'][..., 1] - floor_y) < options.floor_tolerance)
    bias_delta = abs(data['reference'][..., 0] - data['reference'][..., 1])
    insensitive = bias_delta <= options.bias_threshold
    result = {'scenario': frames[0]['scenario'], 'mode': frames[0]['mode'], 'frame_count': len(frames),
        'frame_validation': {'valid': not issues, 'issues': issues}, 'scenario_plan': plan,
        'floor_receiver_samples': int(floor.sum()), 'floor_samples_per_frame': [int(mask.sum()) for mask in floor],
        'bias_sensitive_fraction_floor': float((floor & ~insensitive).sum() / floor.sum()) if floor.any() else None,
        'bias_delta_floor': stats(bias_delta[floor]),
        'signal_outside_visibility_range': int((valid & ((data['signal'][..., 0] < -1e-5) | (data['signal'][..., 0] > 1 + 1e-5))).sum()),
        'history_age_floor': stats(data['signal'][..., 1][floor]),
        'reference_bias_0_001': error_metrics(data['signal'][..., 0], data['reference'][..., 0], floor),
        'reference_bias_0_01': error_metrics(data['signal'][..., 0], data['reference'][..., 1], floor),
        'bias_insensitive_floor': error_metrics(data['signal'][..., 0], data['reference'][..., 0], floor & insensitive),
        'all_valid_roi_diagnostic_only': error_metrics(data['signal'][..., 0], data['reference'][..., 0], valid),
        'per_frame_floor': [error_metrics(data['signal'][t, ..., 0], data['reference'][t, ..., 0], floor[t]) for t in range(len(frames))]}
    if frames[0]['scenario'] == 'receiver_lift':
        result['moving_receiver_top'] = receiver_top_metrics(frames, data, options)
    result['dynamic_response'] = dynamic_metrics(data, floor, options, request) if not issues else {'status': 'invalid_frame_sequence'}
    return result, floor


def run(root, options):
    request = json.loads((root / 'request.json').read_text(encoding='utf-8-sig'))
    paths = [root / (scenario + '_' + mode) for scenario in request['scenarios'] for mode in request['modes']]
    first_frames, first_data, _ = load_case(paths[0], request['frames'])
    floor_y, floor_diagnostic = select_floor(first_data, options.floor_y)
    del first_data
    result = {'source': str(root.resolve()), 'request': request, 'floor_y': floor_y, 'floor_selection': floor_diagnostic,
        'definitions': {
            'reference': 'Fixed full Hammersley disk quadrature, usually 256 rays, with independent 0.001/0.01 m normal biases. Finite reference, not exact visibility truth; forced-opaque alpha, skipped procedural/terrain/skinned geometry can differ from production.',
            'floor_roi': 'valid sample AND normalY > 0.9 AND abs(worldY - floor_y) < floor_tolerance; near-origin dominant horizontal surface inferred once and shared by all modes.',
            'signed_errors': 'positive=max(output-reference,0) denotes under-occlusion/leak tendency; negative=max(reference-output,0) denotes over-occlusion. Each is averaged over all eligible samples; MAE is their sum.',
            'event': 'Adjacent-frame reference jump >= jump_threshold, both frames bias-insensitive and floor-valid, and world displacement <= world_tolerance. This rejects screen-pixel surface switches; it is not full motion-vector correspondence.',
            'detection': 'First delay 0..max_delay where output crosses old_reference + detect_fraction*(new-old) for sustain_frames. Reference must remain on the new side, and receiver world position must remain within world_tolerance of pre-event position. Output already on new side is excluded.',
            'censoring': 'Reference reversal or surface loss, truncated capture horizon, and no detection throughout a fully observed horizon are separate. P95/P99 describe detected events only; undetected fraction is always reported. No eligible event produces insufficient, never a zero-lag pass.',
            'support_residual': 'At actual eligible transition pixels only: reference has moved to settled bright/dark across the sampled square support envelope, but output retains old-side error > residual_threshold. With stride>1 this is a sampled proxy, not proof about unsampled pixels or exact filter support. Geometry/quadrature bias can also produce residuals; not all residuals are temporal ghosting.',
            'camera_motion': 'No reprojection is invented: moving camera samples are only eligible where saved world positions stay within tolerance. Missing events do not prove moving-camera stability.'},
        'thresholds': {k: getattr(options, k) for k in ('floor_tolerance','bias_threshold','jump_threshold','world_tolerance','detect_fraction','sustain_frames','max_delay','settled_threshold','residual_threshold','support_radius_pixels','pair_tolerance','strong_error_threshold')},
        'cases': {}, 'pairing': {}}
    invalid_fixtures = set(getattr(options, 'invalid_fixture_scenarios', None) or [])
    result['status'] = 'invalid-dynamic-fixtures' if invalid_fixtures else 'analyzed_with_stated_coverage_limits'
    result['invalid_fixture_scenarios'] = sorted(invalid_fixtures)
    result['fixture_validation_note'] = ('Explicitly excluded after independent runtime fixture validation found geometry/normal-contract failures; their stored numbers are diagnostics, not quality acceptance.' if invalid_fixtures else 'Parser pairing and trajectory checks do not independently validate the RTAS fixture or GBuffer contract.')
    for scenario in request['scenarios']:
        base_frames = base_data = base_floor = None
        for mode in request['modes']:
            case = scenario + '_' + mode
            frames, data, issues = load_case(root / case, request['frames'])
            metrics, floor = analyze_case(frames, data, issues, floor_y, options, request)
            metrics['quality_evidence_status'] = ('invalid-dynamic-fixtures' if scenario in invalid_fixtures else 'usable_with_stated_coverage_limits')
            result['cases'][case] = metrics
            if base_frames is None:
                base_frames, base_data, base_floor = frames, data, floor
                base_mode = mode
                continue
            pairing = pair_metadata(base_frames, frames, options.pair_tolerance)
            pairing['valid'] &= metrics['frame_validation']['valid'] and result['cases'][scenario + '_' + base_mode]['frame_validation']['valid']
            if base_data['world'].shape == data['world'].shape:
                shared = base_floor & floor
                distance = np.linalg.norm(base_data['world'][..., :3] - data['world'][..., :3], axis=-1)
                paired = shared & (distance <= options.pair_tolerance)
                pairing['shared_floor_samples'] = int(shared.sum())
                pairing['world_matched_floor_samples'] = int(paired.sum())
                pairing['world_distance_shared_floor'] = stats(distance[shared])
                pairing['reference_difference_matched_floor'] = stats(abs(base_data['reference'][..., 0] - data['reference'][..., 0])[paired])
                pairing['output_difference_matched_floor'] = stats(abs(base_data['signal'][..., 0] - data['signal'][..., 0])[paired])
            result['pairing'][scenario + ':' + base_mode + '_vs_' + mode] = pairing
        missing = [mode for mode in MODES if mode not in request['modes']]
        if missing: result['pairing'][scenario + ':missing_modes'] = {'valid': False, 'missing': missing, 'status': 'not_comparable'}
    return result


def self_test():
    options = defaults()
    options.max_delay = 8
    options.support_radius_pixels = 2
    request = {'stride': 1, 'originX': 0, 'originY': 0}
    shape = (14, 9, 9, 4)
    def fixture(delay=None, dark=False):
        ref = np.zeros(shape, np.float32); signal = np.zeros(shape, np.float32); world = np.zeros(shape, np.float32)
        ref[2:, ..., :2] = 1
        if delay is not None: signal[2 + delay:, ..., 0] = 1
        if dark: ref[..., :2] = 1 - ref[..., :2]; signal[..., 0] = 1 - signal[..., 0]
        signal[..., 3] = 1; world[..., 3] = 1
        return {'reference': ref, 'signal': signal, 'world': world}
    data = fixture(2); floor = np.ones(shape[:-1], bool)
    out = dynamic_metrics(data, floor, options, request)
    assert out['counts']['detected'] == 81 and out['first_detection_frames_conditional_on_detection']['p99'] == 2
    assert out['sampled_support_residual']['residual_event_observations'] == 50
    assert dynamic_metrics(fixture(2, True), floor, options, request)['darkening_detection_frames']['median'] == 2
    still = fixture(0); still['reference'][...] = 0; still['signal'][..., 0] = 0
    assert dynamic_metrics(still, floor, options, request)['status'] == 'insufficient_detectable_events'
    assert dynamic_metrics(fixture(), floor, options, request)['counts']['not_detected_within_horizon'] == 81
    short = {key:value[:4] for key,value in fixture().items()}
    assert dynamic_metrics(short, floor[:4], options, request)['counts']['right_censored'] == 81
    moved = fixture(2); moved['world'][2:, ..., 0] += 1
    assert dynamic_metrics(moved, floor, options, request)['counts']['surface_discontinuity_rejected'] == 81
    bias = fixture(2); bias['reference'][..., 1] = 0
    assert dynamic_metrics(bias, floor, options, request)['counts']['eligible_events'] == 0
    request['stride'] = 4
    assert not dynamic_metrics(data, floor, options, request)['sampled_support_residual']['full_resolution_support_coverage']
    e = error_metrics(np.array([.1,.8]), np.array([.3,.5]), np.array([True,True]))
    assert abs(e['mae'] - e['positive_visibility_error'] - e['negative_visibility_error']) < 1e-8
    uniform, _ = uniform_neighborhood(np.ones((9,9)), np.ones((9,9),bool), 2, .1)
    assert uniform.sum() == 25
    base = {'step':0,'frame':256,'phase':0,'mode':'fixed4','scenario':'static','state':'Active','fallback':'None',
        'width':9,'height':9,'gridWidth':9,'gridHeight':9,'pages':256,'levels':10,'referenceRays':256,
        'smrt':{'x':4,'y':8,'z':10,'w':.062},'quality':{'x':1,'y':1,'z':.1,'w':1},
        'cameraPosition':{'x':0,'y':5,'z':4},'cameraEuler':{'x':30,'y':2,'z':.5},
        'lightDirection':{'x':0,'y':1,'z':0},'fixturePosition':{'x':0,'y':0,'z':0},
        'viewProjection':{'e00':1},'fixturePhase':0,'fixtureGeometryHash':0}
    second = dict(base,step=1,frame=257,phase=1)
    assert not check_frames([base,second],2) and check_frames([base,dict(second,frame=258)],2)
    assert pair_metadata([base,second],[dict(base,mode='adaptive'),dict(second,mode='adaptive')],1e-4)['valid']
    assert not pair_metadata([base],[dict(base,phase=1)],1e-4)['valid']
    assert not pair_metadata([base],[dict(base,referenceRays=1024)],1e-4)['valid']
    assert pair_metadata([base],[dict(base,referenceRays=1024)],1e-4,allow_reference_rays_difference=True)['valid']
    assert not pair_metadata([base],[dict(base,referenceRays=1024,fixtureGeometryHash=1)],1e-4,allow_reference_rays_difference=True)['valid']
    assert not pair_metadata([base],[dict(base,fixtureBackend='meshlet rigid')],1e-4)['valid']
    assert check_plan([base,second],2)['valid']
    sun_frames = []
    for i in range(14):
        direction = rotation_y(3 if i >= 8 else 0) @ np.array([.3,.9,.2])
        sun_frames.append(dict(base,scenario='sun_step',step=i,
            lightDirection=dict(zip(('x','y','z'),direction.tolist())),
            fixturePhase=min(1,max(0,(i-4)/5))*6.283185))
    assert check_plan(sun_frames,14)['valid']
    wrong_sun = list(sun_frames)
    wrong_sun[7] = dict(sun_frames[7],lightDirection=sun_frames[8]['lightDirection'])
    assert not check_plan(wrong_sun,14)['valid']
    lift_frames = [dict(base,scenario='receiver_lift',step=i,fixturePosition={'x':-0.7 + 1.4*min(1,max(0,(i-4)/5)), 'y':.05+.6*min(1,max(0,(i-4)/5)), 'z':0},fixturePhase=min(1,max(0,(i-4)/5))*6.283185) for i in range(14)]
    assert check_plan(lift_frames,14)['valid']
    lift = fixture(0)
    for i,frame in enumerate(lift_frames):
        lift['world'][i,...,0]=frame['fixturePosition']['x']; lift['world'][i,...,1]=frame['fixturePosition']['y']+.02
    lift['signal'][... ,1] = 3
    top = receiver_top_metrics(lift_frames,lift,options)
    assert top['visible_top_samples'] == 14*81 and top['history_age']['median'] == 3
    lift['world'][...,1] += .003
    assert receiver_top_metrics(lift_frames,lift,options)['status'] == 'receiver_top_not_observed'
    print('dynamic parser: 24 focused event, censoring, correspondence, bias, support and error checks passed')


def defaults():
    return argparse.Namespace(floor_y=None, floor_tolerance=.15, bias_threshold=.05, jump_threshold=.5,
        world_tolerance=.02, detect_fraction=.5, sustain_frames=2, max_delay=8, settled_threshold=.1,
        residual_threshold=.1, support_radius_pixels=5, pair_tolerance=1e-4, strong_error_threshold=.25)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', nargs='?', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--invalid-fixture-scenarios', nargs='+', default=[])
    for key, value in vars(defaults()).items():
        parser.add_argument('--' + key.replace('_','-'), type=int if isinstance(value,int) else float, default=value)
    args = parser.parse_args()
    if args.self_test: self_test()
    if args.capture:
        result = run(args.capture, args)
        output = json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False) + '\n'
        if args.output: args.output.write_text(output, encoding='utf-8')
        else: print(output)
    elif not args.self_test: parser.error('provide capture directory or --self-test')
