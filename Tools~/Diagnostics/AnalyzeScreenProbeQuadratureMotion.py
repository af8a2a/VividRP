"""Measure disocclusion history in independent stratified Resolve recurrences."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_replay
from AnalyzeScreenProbeColorHistoryMotion import exposed_mask
from GatherHistoryExperiment import unpack_pixel_history
from TemporalSamplingExperiment import read,LUMA


def analyze(root):
    capture,protocol,source_rows,rows,pairs,initial=qualify_replay(root)
    assert protocol['motion'] and protocol['regions']==['edge'] and protocol['measuredFrames']==128
    start=protocol['warmup'];frames=range(start,start+128)
    states={r['frame']:r for r in capture['records'] if 'state' in r}
    vp=np.array([states[f]['vp'] for f in frames]).reshape(128,4,4)
    assert np.max(abs(vp-vp[0]))>.01,'Fixed trajectory did not move'
    def images(role,side):
        return np.stack([read(rows[f,role+'-edge',side]) for f in frames])
    geometry=images('geometry','baseline')[...,0]
    assert np.array_equal(geometry,images('geometry','candidate')[...,0])
    current=images('resolved','source').astype(float)
    current_y=(current[...,:3]*current[...,3:4])@LUMA
    mask=exposed_mask(geometry,vp,protocol['motionRegion'],current[...,3])
    assert mask.any(),'Trajectory did not exercise clear disocclusion'
    eligibility={}
    for roi in ('ceiling','floor','back','edge'):
        marked=np.stack([read(source_rows[f,'quadrature-'+roi])[...,3]<0 for f in frames])
        eligibility[roi]=dict(pixelFrameFraction=float(marked.mean()),frames=int(marked.any((1,2)).sum()),
                              perFrameFraction=marked.mean((1,2)).tolist())
        if roi=='edge':eligibility[roi]['newlyExposedMarkedSamples']=int((marked&mask).sum())
    signed_current=np.stack([read(source_rows[f,'quadrature-edge']) for f in frames])
    positive=images('resolved','candidate')
    assert np.array_equal(signed_current[...,:3],positive[...,:3]) and np.array_equal(abs(signed_current[...,3]),positive[...,3])
    arms={}
    for side in ('baseline','candidate'):
        temporal=images('temporal',side).astype(float);final=images('final',side).astype(float)
        history=images('history',side)
        age,_,status=unpack_pixel_history(history[...,3].astype('<f2'))
        own_current=current if side=='baseline' else images('resolved','candidate').astype(float)
        assert (own_current[...,3]>=0).all(),'Signed coverage reached the temporal input'
        own_y=(own_current[...,:3]*own_current[...,3:4])@LUMA
        temporal_y=(temporal[...,:3]*temporal[...,3:4])@LUMA
        error=abs(temporal_y-own_y)[mask]
        common_error=abs(temporal_y-current_y)[mask]
        final_error=abs((final[...,:3]*final[...,3:4])@LUMA-current_y)[mask]
        arms[side]=dict(samples=int(mask.sum()),frames=int(mask.any((1,2)).sum()),
            oldAgeFraction=float((age[mask]>1).mean()),acceptedHistoryFraction=float((status[mask]==1).mean()),
            temporalMinusOwnCurrentP99=float(np.quantile(error,.99)),temporalMinusOwnCurrentMax=float(error.max()),
            temporalMinusCommonCurrentP99=float(np.quantile(common_error,.99)),temporalMinusCommonCurrentMax=float(common_error.max()),
            finalMinusCommonCurrentP99=float(np.quantile(final_error,.99)),finalMinusCommonCurrentMax=float(final_error.max()))
    a,b=arms['baseline'],arms['candidate'];gates=protocol['gates']
    p99=b['temporalMinusOwnCurrentP99']<=a['temporalMinusOwnCurrentP99']*(1+gates['disocclusionP99IncreasePercent']/100)+1e-6
    maximum=b['temporalMinusOwnCurrentMax']<=a['temporalMinusOwnCurrentMax']*(1+gates['disocclusionMaxIncreasePercent']/100)+1e-6
    common_p99=b['temporalMinusCommonCurrentP99']<=a['temporalMinusCommonCurrentP99']*(1+gates['disocclusionP99IncreasePercent']/100)+1e-6
    common_max=b['temporalMinusCommonCurrentMax']<=a['temporalMinusCommonCurrentMax']*(1+gates['disocclusionMaxIncreasePercent']/100)+1e-6
    age=b['oldAgeFraction']<=a['oldAgeFraction']+gates['newlyExposedOldAgeFractionIncrease']
    return dict(schema='screen-probe-quadrature-motion-analysis.v1',qualified=True,nativeBaselineQualified=True,
        fullImageResolveAaPairs=len(protocol['aaFrames']),privateHistoryAaPairs=pairs,initialState=initial,
        arms=arms,quadratureEligibility=eligibility,disocclusionP99Passed=p99,disocclusionMaxPassed=maximum,disocclusionAgePassed=age,
        commonCurrentP99Passed=common_p99,commonCurrentMaxPassed=common_max,
        candidateExecutedAfterFirstMeasuredFrame=any(any(f>0 for f in v['perFrameFraction'][1:]) for v in eligibility.values()),
        movingDenoisingQualityMeasured=False,
        motionPassed=p99 and maximum and age and common_p99 and common_max,productionQualified=False,
        limits=['Temporal minus own current isolates inherited history. Common native Resolve also measures changes from the interpolation itself.',
                'Noisy Resolve is not an absolute reference; final minus common current includes spatial filtering and is descriptive.',
                'Eligibility is reported frame by frame. A disabled candidate tests fallback/history transitions, not active moving denoising quality.',
                'One fixed128-frame camera cycle; no stop-and-settle recovery, dynamic-lighting or full transport feedback acceptance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();result=analyze(args.root.resolve());args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))
