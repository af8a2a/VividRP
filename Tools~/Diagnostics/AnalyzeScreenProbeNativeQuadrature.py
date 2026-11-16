"""Compare native original/control A/A/candidate with explicit input checks."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeHistoryEpoch import qualify
from AnalyzeScreenProbeQuadratureHistory import energy_and_noise
from AnalyzeScreenProbeBlackEnvironmentQuality import ROIS
from TemporalSamplingExperiment import read,LUMA,metrics


def geometry_error(a,b,limit):
    assert a.shape==b.shape
    assert np.array_equal(a[...,3],b[...,3]),'Probe normal/validity changed'
    valid=a[...,3]>=0
    delta=abs(a[...,:3].astype(float)-b[...,:3].astype(float))[valid]
    error=float(delta.max()) if delta.size else 0.0
    assert error<=limit,'Probe world-position difference exceeds protocol bound'
    return error


def inspect(root):
    c=qualify(root);capture,protocol,rows,states=c[:4]
    assert protocol['schema']=='screen-probe-native-quadrature-protocol.v1'
    variant=protocol['variant'];assert len(capture['quadraturePrograms'])==(0 if variant=='original' else 6)
    for frame in range(1024,1152):
        for roi in ROIS:
            for stage in ('resolved','temporal','final'):
                a=read(rows[frame,stage+'-'+roi])
                assert ((a[...,3]>=0)&(a[...,3]<=1)).all(),'Signed private marker entered native GI chain'
    if variant=='candidate':
        observed={}
        for f in (1024,1151):
            for roi in ROIS:
                a,b=(read(rows[f,tag+'-'+roi]) for tag in ('resolved','native-eligibility'))
                assert np.array_equal(a[...,:3],b[...,:3]) and np.array_equal(a[...,3],abs(b[...,3])), 'Native/witness mismatch'
                marked=b[...,3]<0;assert (abs(b[...,3][marked])>=.4995).all()
                observed[f,roi]=int(marked.sum())
        assert observed[1024,'ceiling']>0 and observed[1151,'ceiling']>0,'Native quadrature branch did not execute'
    return c


def compare(a,b):
    for key in ('sequence','warmup','measuredFrames','spacing','rays'):assert a[1][key]==b[1][key]
    differences=[];maximum_geometry_error=0.0
    for f in range(1024,1152):
        for tag in ('pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1'):
            x,y=(c[2][f,tag] for c in (a,b))
            if any(x[k]!=y[k] for k in ('sha256','width','height','format')):differences.append([f,tag])
        if a[3][f]['vp']!=b[3][f]['vp'] or a[3][f]['state']['inverseVp']!=b[3][f]['state']['inverseVp']:differences.append([f,'matrices'])
        maximum_geometry_error=max(maximum_geometry_error,geometry_error(read(a[2][f,'geometry']),read(b[2][f,'geometry']),a[1]['gates']['geometryPositionMaxMeters']))
    result=dict(inputsQualified=not differences,inputDifferences=differences,maximumGeometryPositionDeltaMeters=maximum_geometry_error)
    if differences:return result
    regions={}
    for roi in ROIS:
        x,y=(np.stack([read(c[2][f,'final-'+roi]).astype(float) for f in range(1024,1152)]) for c in (a,b))
        regions[roi]=energy_and_noise(x,y)
    result['regions']=regions
    return result


def control_pass(result,gates):
    if not result['inputsQualified']:return False
    return all(abs(v['comparison']['mean_change_percent'])<=gates['controlMeanChangeAbsPercent']
        and all(abs(c)<=gates['controlMeanChangeAbsPercent'] for c in v['rgbMeanChangePercent'])
        and abs(v['comparison']['variance_reduction_percent'])<=gates['controlVarianceChangeAbsPercent']
        for v in result['regions'].values())


def diagnose_repeatability(a,b):
    """Locate differences among retained samples, without inventing warmup evidence."""
    start=a[1]['warmup'];count=a[1]['measuredFrames']
    assert (start,count)==(b[1]['warmup'],b[1]['measuredFrames'])
    regions={}
    coordinates=dict(ceiling=(46,59,28),floor=(54,8,12),back=(68,38,8),edge=(55,26,10))
    for roi,(x,y,points) in coordinates.items():
        stages={}
        for stage in ('raw','anchor','prepared','reconstructed','resolved-points','temporal-points','final-points','resolved','temporal','final'):
            tag='raw' if stage=='raw' else stage+'-'+roi
            arrays=[];first=None;changed=0;maximum=0.0
            for c in (a,b):
                v=np.stack([read(c[2][f,tag]).astype(float) for f in range(start,start+count)])
                if stage=='raw':v=v[:,y:y+1,x:x+points]
                assert ((v[...,3]>=0)&(v[...,3]<=1)).all()
                arrays.append(v)
            for index,(left,right) in enumerate(zip(*arrays)):
                if not np.array_equal(left,right):
                    if first is None:first=start+index
                    changed+=1
                maximum=max(maximum,float(abs(left-right).max()))
            signals=[(v[...,:3]*v[...,3:4])@LUMA for v in arrays]
            stages[stage]=dict(baseline=metrics(signals[0]),repeat=metrics(signals[1]),
                firstRetainedDifferentFrame=first,changedFrames=changed,maximumRgbaDifference=maximum,
                consumedYDifferenceRms=float(np.sqrt(np.mean((signals[1]-signals[0])**2))),
                support='final pixel ROI' if stage in ('resolved','temporal','final') else 'fixed probe receiver centers')
        epoch={tag:np.array_equal(read(a[2][0,'epoch-'+tag+'-'+roi]),read(b[2][0,'epoch-'+tag+'-'+roi]))
               for tag in ('statistics','geometry','anchor','raw')}
        regions[roi]=dict(firstEpochSamplesExact=epoch,stages=stages)
    return dict(regions=regions,firstRetainedMeasurementFrame=start,
        note='Epoch0 contains only sampled probe strips; no frames1..1023 were retained. A difference at1024 is not the first causal divergence. Probe-center and final pixel ROI statistics have different supports and cannot be subtracted as stage contributions.')


def analyze(root):
    original,a,b,c=(inspect(root/n) for n in ('original','control-a','control-b','candidate'))
    assert [x[1]['variant'] for x in (original,a,b,c)]==['original','control','control','candidate']
    program=compare(original,a);repeat=compare(a,b);candidate=compare(a,c);gates=a[1]['gates']
    qualification=control_pass(program,gates) and control_pass(repeat,gates) and candidate['inputsQualified']
    mean=noise=other=False
    if qualification:
        regions=candidate['regions']
        mean=all(abs(v['comparison']['mean_change_percent'])<=gates['meanChangeAbsPercent']
            and all(abs(channel)<=gates['meanChangeAbsPercent'] for channel in v['rgbMeanChangePercent']) for v in regions.values())
        noise=regions['ceiling']['comparison']['variance_reduction_percent']>=gates['ceilingVarianceReductionPercent']
        other=all(v['comparison']['variance_reduction_percent']>=-gates['otherVarianceIncreasePercent'] for v in regions.values())
    return dict(schema='screen-probe-native-quadrature-analysis.v1',nativeProgramControl=program,nativeRepeatability=repeat,
        repeatabilityStages=diagnose_repeatability(a,b),
        nativeCandidate=candidate,qualityAttributionQualified=qualification,meanGatePassed=mean,ceilingNoiseGatePassed=noise,
        otherNoiseGatePassed=other,nativePilotPassed=qualification and mean and noise and other,productionQualified=False,
        limits=['Full native GI chain with actual native pixel/transport histories, not shared-input private recurrence.',
                'Exact depth/GBuffer/pixel geometry and matrices; identical probe normal/validity; position bound2micrometers predeclared to address known float reconstruction variation.',
                'Static sequence only; no GPU-cost, physical-reference, native motion or relighting acceptance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();v=analyze(args.root.resolve());args.output.write_text(json.dumps(v,indent=2)+'\n')
    print(json.dumps({**{k:v for k,v in v.items() if k not in ('nativeProgramControl','nativeRepeatability','nativeCandidate','repeatabilityStages')},
        'comparisons':{k:{'inputsQualified':v[k]['inputsQualified'],'maximumGeometryPositionDeltaMeters':v[k]['maximumGeometryPositionDeltaMeters'],
            'final':{r:d['comparison'] for r,d in v[k].get('regions',{}).items()}} for k in ('nativeProgramControl','nativeRepeatability','nativeCandidate')}},indent=2))
