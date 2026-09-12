#!/usr/bin/env python3
"""Paired finite-reference convergence; retains the original production signal."""
import argparse
import importlib.util
import json
from pathlib import Path
import numpy as np

spec = importlib.util.spec_from_file_location('dynamic', Path(__file__).with_name('analyze-dynamic.py'))
dynamic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dynamic)


def difference(a, b, mask):
    delta = np.abs(a - b)
    return {'absolute_difference': dynamic.stats(delta[mask]),
            'nonzero_fraction': float((delta[mask] != 0).mean()) if mask.any() else None,
            'fraction_over_0_01': float((delta[mask] > .01).mean()) if mask.any() else None,
            'fraction_over_0_05': float((delta[mask] > .05).mean()) if mask.any() else None}


def set_comparison(a, b, domain):
    a, b = a & domain, b & domain
    union = a | b
    return {'base_events': int(a.sum()), 'higher_ray_events': int(b.sum()),
            'intersection': int((a & b).sum()), 'base_only': int((a & ~b).sum()),
            'higher_ray_only': int((b & ~a).sum()), 'union': int(union.sum()),
            'jaccard': float((a & b).sum() / union.sum()) if union.any() else None,
            'status': 'event_sets_compared' if union.any() else 'no_events_in_either_reference'}


def compare_case(base_frames, base, high_frames, high, request, options):
    pairing = dynamic.pair_metadata(base_frames, high_frames, options.pair_tolerance,
                                    allow_reference_rays_difference=True)
    base_plan = dynamic.check_plan(base_frames,request['frames'],options.pair_tolerance)
    higher_plan = dynamic.check_plan(high_frames,request['frames'],options.pair_tolerance)
    pairing['base_plan'] = base_plan
    pairing['higher_plan'] = higher_plan
    pairing['valid'] &= base_plan['valid'] and higher_plan['valid']
    valid = dynamic.valid_mask(base) & dynamic.valid_mask(high)
    world_delta = np.max(abs(base['world'] - high['world']), axis=-1)
    depth_delta = abs(base['signal'][..., 2] - high['signal'][..., 2])
    matched = valid & (world_delta <= options.pair_tolerance) & (depth_delta <= options.pair_tolerance)
    pairing.update({'shared_valid_samples': int(valid.sum()), 'matched_world_and_depth_samples': int(matched.sum()),
                    'world_xyz_normal_max_delta': float(world_delta.max()),
                    'device_depth_max_delta': float(depth_delta.max()),
                    'world_arrays_bitwise_equal': bool(np.array_equal(base['world'], high['world'])),
                    'same_validity_mask': bool(np.array_equal(dynamic.valid_mask(base), dynamic.valid_mask(high)))})
    pairing['valid'] &= (pairing['same_validity_mask']
                         and pairing['world_xyz_normal_max_delta'] <= options.pair_tolerance
                         and pairing['device_depth_max_delta'] <= options.pair_tolerance)
    if not matched.any():
        pairing['valid'] = False
        pairing['issues'].append('No shared valid world/depth-matched samples')
        return {'pairing': pairing, 'quality_evidence_status': 'insufficient_shared_valid_samples',
                'floor_samples': 0, 'biases': {}, 'thresholds': {}}
    floor_y, floor_selection = dynamic.select_floor(base, options.floor_y)
    floor = matched & (base['world'][...,3] > .9) & (abs(base['world'][...,1] - floor_y) < options.floor_tolerance)
    adjacent = floor[1:] & floor[:-1]
    continuous = np.linalg.norm(base['world'][1:,...,:3] - base['world'][:-1,...,:3], axis=-1) <= options.world_tolerance
    result = {'pairing': pairing, 'floor_y': floor_y, 'floor_selection': floor_selection,
              'quality_evidence_status': 'usable_with_stated_coverage_limits' if pairing['valid'] else 'invalid_reference_pairing',
              'base_reference_rays': base_frames[0]['referenceRays'],
              'higher_reference_rays': high_frames[0]['referenceRays'],
              'floor_samples': int(floor.sum()), 'adjacent_floor_pairs': int(adjacent.sum()),
              'biases': {}, 'thresholds': {}}
    for bias, key in enumerate(('normal_bias_0_001', 'normal_bias_0_01')):
        a, b = base['reference'][...,bias], high['reference'][...,bias]
        da, db = a[1:] - a[:-1], b[1:] - b[:-1]
        result['biases'][key] = {
            'floor_field': difference(a,b,floor),
            'all_valid_field_diagnostic_only': difference(a,b,matched),
            'floor_adjacent_delta': difference(da,db,adjacent),
            'base_absolute_floor_adjacent_delta': dynamic.stats(abs(da)[adjacent]),
            'higher_absolute_floor_adjacent_delta': dynamic.stats(abs(db)[adjacent]),
            'same_base_signal_error_vs_base_reference': dynamic.error_metrics(base['signal'][...,0],a,floor),
            'same_base_signal_error_vs_higher_reference': dynamic.error_metrics(base['signal'][...,0],b,floor)}
    base_insensitive = abs(base['reference'][...,0] - base['reference'][...,1]) <= options.bias_threshold
    high_insensitive = abs(high['reference'][...,0] - high['reference'][...,1]) <= options.bias_threshold
    result['bias_sensitive_classification'] = set_comparison(~base_insensitive,~high_insensitive,floor)
    replacement = dict(base, reference=high['reference'])
    for threshold in (.5,.1):
        options.jump_threshold = threshold
        base_events = abs(np.diff(base['reference'][...,0],axis=0)) >= threshold
        high_events = abs(np.diff(high['reference'][...,0],axis=0)) >= threshold
        base_eligible = base_events & base_insensitive[1:] & base_insensitive[:-1] & continuous
        high_eligible = high_events & high_insensitive[1:] & high_insensitive[:-1] & continuous
        result['thresholds'][str(threshold)] = {
            'raw_floor_event_sets': set_comparison(base_events,high_events,adjacent),
            'eligible_floor_event_sets': set_comparison(base_eligible,high_eligible,adjacent),
            'same_base_signal_base_reference_response': dynamic.dynamic_metrics(base,floor,options,request),
            'same_base_signal_higher_reference_response': dynamic.dynamic_metrics(replacement,floor,options,request)}
    if base_frames[0]['scenario'] == 'receiver_lift':
        result['receiver_top_base_reference'] = dynamic.receiver_top_metrics(base_frames,base,options)
        result['receiver_top_higher_reference_same_base_signal'] = dynamic.receiver_top_metrics(base_frames,replacement,options)
        centers = np.array([[f['fixturePosition'][axis] for axis in ('x','y','z')] for f in base_frames])
        delta = base['world'][...,:3] - centers[:,None,None,:]
        geometry = matched & (abs(delta[...,1] - .02) < .002) & (abs(delta[...,0]) <= 1.252) & (abs(delta[...,2]) <= 1.002)
        result['receiver_top_normal_contract_diagnostic'] = {
            'geometry_only_samples': int(geometry.sum()),
            'geometry_only_samples_per_frame': [int(x.sum()) for x in geometry],
            'decoded_normal_y': dynamic.stats(base['world'][...,3][geometry]),
            'fraction_normal_y_over_0_9': float((base['world'][...,3][geometry]>.9).mean()) if geometry.any() else None,
            'note': 'Height and footprint alone identify candidate top geometry, but normals incompatible with the requested opaque-floor contract invalidate that ROI. These samples are not promoted to accuracy truth.'}
    return result


def run(base_root, higher_root, invalid_fixture_scenarios=(), all_base_modes=False):
    request = json.loads((base_root/'request.json').read_text(encoding='utf-8-sig'))
    high_request = json.loads((higher_root/'request.json').read_text(encoding='utf-8-sig'))
    request_keys = ('frames','stride','originX','originY','width','height','fixtureForwardDistance','floorHeight')
    mismatch = [k for k in request_keys if request.get(k) != high_request.get(k)]
    if mismatch: raise ValueError('Capture contract mismatch: ' + ','.join(mismatch))
    if all_base_modes and len(high_request['modes']) != 1:
        raise ValueError('All-base-modes comparison requires one common higher-ray reference capture mode.')
    options = dynamic.defaults()
    result = {'base_source': str(base_root.resolve()), 'higher_ray_source': str(higher_root.resolve()),
              'status': 'invalid-dynamic-fixtures' if invalid_fixture_scenarios else 'analyzed_with_stated_coverage_limits',
              'invalid_fixture_scenarios': list(invalid_fixture_scenarios),
              'all_base_modes_against_common_reference': all_base_modes,
              'reference_status': 'Finite fixed quadrature comparison, not an exact or independently unbiased truth. Higher-ray visibility replaces only reference fields; original 256-run production output is held fixed.',
              'all_valid_roi_warning': 'Forced-opaque alpha and unsupported geometry prevent all-valid ROI from serving as accuracy truth.',
              'capture_request_contract_valid': True, 'cases': {}}
    for scenario in high_request['scenarios']:
        for reference_mode in high_request['modes']:
            reference_name = scenario+'_'+reference_mode
            bf,bd,be = dynamic.load_case(higher_root/reference_name,high_request['frames'])
            for mode in (request['modes'] if all_base_modes else [reference_mode]):
                name = scenario+'_'+mode
                af,ad,ae = dynamic.load_case(base_root/name,request['frames'])
                if ae or be: raise ValueError(name+': '+str(ae+be))
                if options.floor_y is None: options.floor_y = dynamic.select_floor(ad,None)[0]
                result['cases'][name] = compare_case(af,ad,bf,bd,request,options)
                result['cases'][name]['original_production_output_mode'] = mode
                result['cases'][name]['higher_reference_capture_mode'] = reference_mode
                if scenario in invalid_fixture_scenarios:
                    result['cases'][name]['quality_evidence_status'] = 'invalid-dynamic-fixtures'
                print(name, 'paired:',result['cases'][name]['pairing']['valid'])
    return result


def self_test():
    mask = np.ones((2,2),bool); a=np.array([[0,1],[0,1]],bool); b=np.array([[0,1],[1,0]],bool)
    r=set_comparison(a,b,mask)
    assert r['intersection']==1 and r['base_only']==1 and r['higher_ray_only']==1 and r['jaccard']==1/3
    assert set_comparison(a&False,b&False,mask)['jaccard'] is None
    assert difference(np.zeros((2,2)),np.ones((2,2))*.02,mask)['fraction_over_0_01']==1
    frame={'scenario':'static','step':0,'phase':0,'width':3,'height':3,'gridWidth':3,'gridHeight':3,
           'pages':256,'levels':10,'referenceRays':256,'fixturePhase':0,'fixtureGeometryHash':0,
           'cameraPosition':{'x':0,'y':1,'z':0},'cameraEuler':{'x':0,'y':0,'z':0},
           'lightDirection':{'x':0,'y':1,'z':0},'fixturePosition':{'x':0,'y':0,'z':0},
           'viewProjection':{'e00':1},'quality':{'x':1,'y':1,'z':1,'w':1},'smrt':{'x':4,'y':8,'z':10,'w':.06}}
    frames=[frame,dict(frame,step=1,phase=1)]
    high_frames=[dict(f,referenceRays=1024) for f in frames]
    data={k:np.zeros((2,3,3,4),np.float32) for k in ('reference','signal','world')}
    data['signal'][...,3]=1;data['world'][...,3]=1
    request={'frames':2,'stride':1,'originX':0,'originY':0}
    options=dynamic.defaults()
    result=compare_case(frames,data,high_frames,data,request,options)
    assert result['pairing']['valid'] and result['quality_evidence_status']=='usable_with_stated_coverage_limits'
    invalid_frames=[dict(f,fixtureGeometryHash=1) for f in high_frames]
    result=compare_case(frames,data,invalid_frames,data,request,options)
    assert not result['pairing']['valid'] and result['quality_evidence_status']=='invalid_reference_pairing'
    empty={k:v.copy() for k,v in data.items()};empty['signal'][...,3]=0
    result=compare_case(frames,empty,high_frames,empty,request,options)
    assert not result['pairing']['valid'] and result['quality_evidence_status']=='insufficient_shared_valid_samples'
    shifted={k:v.copy() for k,v in data.items()};shifted['world'][...,0]+=1
    assert not compare_case(frames,data,high_frames,shifted,request,options)['pairing']['valid']
    signal_changed={k:v.copy() for k,v in data.items()};signal_changed['signal'][...,:2]=.75
    assert compare_case(frames,data,high_frames,data,request,options)==compare_case(frames,data,high_frames,signal_changed,request,options)
    print('reference convergence parser: 8 focused set/difference, invalid/empty pairing and signal-isolation checks passed')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('base',type=Path,nargs='?');p.add_argument('higher',type=Path,nargs='?')
    p.add_argument('--output',type=Path);p.add_argument('--self-test',action='store_true')
    p.add_argument('--invalid-fixture-scenarios',nargs='+',default=[])
    p.add_argument('--all-base-modes',action='store_true')
    args=p.parse_args()
    if args.self_test:self_test()
    if args.base and args.higher:
        result=run(args.base,args.higher,args.invalid_fixture_scenarios,args.all_base_modes)
        output=json.dumps(result,indent=2,allow_nan=False)+'\n'
        if args.output:args.output.write_text(output,encoding='utf-8')
        else: print(output)
    elif not args.self_test:p.error('provide both capture paths or --self-test')
