"""Verify a cold Gather epoch over frozen radiosity and strict cross-run A/A."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeBlackEnvironmentQuality import inspect,ROIS
from TemporalSamplingExperiment import read,LUMA,metrics


def qualify(root):
    capture,protocol,rows,states=inspect(root)
    assert protocol['historyEpoch'] and capture['epochReset']
    if protocol.get('originTraceReplay',False):
        from ScreenProbeOriginReplay import TRACE_KERNELS,REPLAY_KERNELS
        assert protocol['configuredRayBiasMeters']==.04 and protocol['nativeOriginLimitMeters'] in (.04,.01)
        assert capture['originNativeLimit']==protocol['nativeOriginLimitMeters'] and capture['originInitialized']
        assert len(capture['experimentalTracePrograms'])==len(TRACE_KERNELS)
        assert {r['name'] for r in capture['experimentalTracePrograms']}==set(TRACE_KERNELS)
        replay_kernels=REPLAY_KERNELS if protocol.get('originIncompleteMaskComparison',False) else REPLAY_KERNELS[:-1]
        assert len(capture['originReplayPrograms'])==len(replay_kernels)
        assert {r['name'] for r in capture['originReplayPrograms']}==set(replay_kernels)
    else:
        assert not capture['experimentalTracePrograms']
    required={'ProbeHistoryId','AnchorStatisticsHistoryId','GeometryHistoryId','DirectionalHistoryId',
        'DirectionalGuideHistoryId','CoveredPixelHistoryId','ScreenProbeMomentsHistoryId','PixelGeometryHistoryId'}
    assert required.issubset(capture['epochInvalidated'])
    first=[r for r in capture['records'] if 'epochFirstFrame' in r];assert len(first)==1
    first=first[0]
    assert first['epochFirstFrame']==0 and not first['hasHistory'] and not first['anchorStatisticsHistoryValid']
    assert (first['publishedOrder'],first['batch'],first['sourceUpdates'])==(8,32,0)
    maximum_energy_error=0.0
    for roi in ROIS:
        statistics,geometry,anchor,raw=(read(rows[0,tag+'-'+roi]).astype(float) for tag in ('epoch-statistics','epoch-geometry','epoch-anchor','epoch-raw'))
        valid=(geometry[...,3]>=0)&(raw[...,3]>0)
        assert (statistics[...,0][valid]==1).all() and (statistics[...,1:][valid]==0).all()
        assert np.array_equal(anchor[...,3],raw[...,3])
        x,y=(v[...,:3]*v[...,3:4] for v in (anchor,raw));error=abs(x-y)
        assert np.all(error<=.0002+.002*np.maximum(abs(x),abs(y)))
        maximum_energy_error=max(maximum_energy_error,float(error.max()))
    return capture,protocol,rows,states,maximum_energy_error


def compare(before,after):
    a,b=qualify(before),qualify(after)
    for key in ('guided','corrected','sequence','warmup','measuredFrames','spacing','rays'):assert a[1][key]==b[1][key]
    assert a[2].keys()==b[2].keys() and a[3].keys()==b[3].keys()
    differences=[];counts={}
    for key in a[2]:
        if any(a[2][key][k]!=b[2][key][k] for k in ('sha256','width','height','format')):
            differences.append(list(key));counts[key[1]]=counts.get(key[1],0)+1
    state_differences=[f for f in a[3] if a[3][f]['vp']!=b[3][f]['vp'] or a[3][f]['state']!=b[3][f]['state']]
    final={}
    for roi in ROIS:
        signals=[consumed(c,'final-'+roi) for c in (a,b)]
        final[roi]=signal_comparison(*signals)
        final[roi]['pairedDeltaRms']=float(np.sqrt(np.mean((signals[1]-signals[0])**2)))
    return dict(schema='screen-probe-history-epoch-aa.v1',sourceEpochQualified=True,
        allTexturePayloadsByteExact=not differences,allStateAndMatricesExact=not state_differences,
        aaPassed=not differences and not state_differences,pairedRecords=len(a[2]),
        firstEpochConsumedRgbMaxErrors=[a[4],b[4]],differences=differences,differencesByTag=counts,stateDifferences=state_differences,
        finalRepeatability=final,
        productionQualified=False,note='Strict same-sequence A/A of initialized history. Does not establish a quality improvement, independent energy truth, motion stability or GPU cost.')


def consumed(c,tag):
    start=c[1]['warmup'];v=np.stack([read(c[2][f,tag]).astype(float) for f in range(start,start+c[1]['measuredFrames'])])
    return (v[...,:3]*abs(v[...,3:4]))@LUMA


def signal_comparison(a,b):
    x,y=metrics(a),metrics(b)
    return dict(baseline=x,candidate=y,meanChangePercent=100*(y['mean_y']/x['mean_y']-1),
        varianceChangePercent=100*(y['temporal_variance']/x['temporal_variance']-1) if x['temporal_variance']>0 else None)


def guidance(before,after):
    a,b=qualify(before),qualify(after)
    assert not a[1]['guided'] and b[1]['guided']
    for key in ('corrected','sequence','warmup','measuredFrames','spacing','rays'):assert a[1][key]==b[1][key]
    start=a[1]['warmup'];differences=[]
    for f in range(start,start+a[1]['measuredFrames']):
        for tag in ('geometry','pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1'):
            if any(a[2][f,tag][k]!=b[2][f,tag][k] for k in ('sha256','width','height','format')):differences.append([f,tag])
        sa,sb=(dict(c[3][f]['state']) for c in (a,b));sa.pop('guide');sb.pop('guide')
        if a[3][f]['vp']!=b[3][f]['vp'] or sa!=sb:differences.append([f,'stateOrMatrices'])
    regions={}
    for roi in ROIS:
        stages={}
        for name in ('anchor','prepared','reconstructed','resolved-points','temporal-points','final-points','final'):
            stages[name]=signal_comparison(*(consumed(c,name+'-'+roi) for c in (a,b)))
        regions[roi]=stages
    return dict(schema='screen-probe-history-epoch-guidance.v1',sourceEpochQualified=True,
        inputGeometryExact=not differences,inputDifferences=differences,regions=regions,
        qualityAttributionQualified=not differences,productionQualified=False,
        note='Descriptive default-guide vs uniform-direction diagnostic at128frames. Does not promote uniform sampling or erase strict A/A failures. Probe strips and final pixel ROIs have different supports; no physical-truth or GPU-cost claim.')


def stages(root):
    c=qualify(root);assert c[1]['pixelStages'] and c[1]['measuredFrames']==128
    regions={}
    for roi in ROIS:
        values={stage:consumed(c,stage+'-'+roi) for stage in ('resolved','temporal','final')}
        regions[roi]=dict(metrics={name:metrics(v) for name,v in values.items()},
            temporalVsResolve=signal_comparison(values['resolved'],values['temporal']),
            finalVsTemporal=signal_comparison(values['temporal'],values['final']),
            finalVsResolve=signal_comparison(values['resolved'],values['final']))
    return dict(schema='screen-probe-history-epoch-pixel-stages.v1',qualified=True,regions=regions,
        productionQualified=False,note='Same frames and same final pixel ROI support for Resolve, Temporal and final output. Stage differences are within one run; no cross-run A/A, physical-energy, motion or GPU-cost acceptance.')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('before',type=Path);p.add_argument('after',type=Path,nargs='?');p.add_argument('--output',type=Path,required=True);mode=p.add_mutually_exclusive_group();mode.add_argument('--guidance',action='store_true');mode.add_argument('--pixel-stages',action='store_true')
    args=p.parse_args()
    if args.pixel_stages:v=stages(args.before.resolve())
    else:
        assert args.after is not None
        v=(guidance if args.guidance else compare)(args.before.resolve(),args.after.resolve())
    args.output.write_text(json.dumps(v,indent=2)+'\n');print(json.dumps({k:v for k,v in v.items() if k not in ('differences','regions')},indent=2))
