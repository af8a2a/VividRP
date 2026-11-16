"""Decompose same-pixel Resolve variance without assuming independent components."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeHistoryEpoch import qualify,consumed
from AnalyzeScreenProbeBlackEnvironmentQuality import ROIS
from ProbeAnchorCalibration import normal_from_packed,evaluate_sh,LUMA
from TemporalSamplingExperiment import read


def decompose(uncalibrated,calibrated):
    assert uncalibrated.shape==calibrated.shape and len(uncalibrated)>1
    assert np.isfinite(uncalibrated).all() and np.isfinite(calibrated).all()
    delta=calibrated-uncalibrated
    a=uncalibrated-uncalibrated.mean(0);b=delta-delta.mean(0);y=calibrated-calibrated.mean(0)
    va=float(np.mean(a*a));vb=float(np.mean(b*b));cov=float(np.mean(a*b));vy=float(np.mean(y*y))
    error=abs(vy-(va+vb+2*cov));scale=max(va,vb,abs(2*cov),vy,1e-12)
    assert error<=1e-10*scale,'Variance decomposition does not close'
    return dict(uncalibratedMean=float(uncalibrated.mean()),calibratedMean=float(calibrated.mean()),
        netCalibrationDeltaMean=float(delta.mean()),uncalibratedVariance=va,netCalibrationDeltaVariance=vb,
        twiceCovariance=2*cov,calibratedVariance=vy,closureAbsoluteError=error,
        calibratedToUncalibratedVarianceRatio=vy/va if va>0 else None)


def consumed_rgb_mean(rows,tag):
    total=np.zeros(3);pixels=0
    for f in range(1024,1152):
        value=read(rows[f,tag]).astype(float);rgb=value[...,:3]*abs(value[...,3:4])
        total+=rgb.sum((0,1));pixels+=rgb.shape[0]*rgb.shape[1]
    return total/pixels


def analyze(root):
    c=qualify(root);capture,protocol,rows,_=c[:4]
    assert protocol['resolveComponents'] and capture['componentAaPairs']==128
    aa=[r for r in capture['records'] if r.get('componentAa')];assert len(aa)==256
    for f in range(1024,1152):
        pair=[r for r in aa if r['frame']==f]
        assert {r['side'] for r in pair}=={'native','copied'} and len({r['sha256'] for r in pair})==1
    regions={};normal_points={};coverage={};without_jitter=protocol.get('withoutJitter',False);quadrature=protocol.get('quadrature',False)
    geometry=np.stack([read(rows[f,'geometry']) for f in range(1024,1152)])
    for roi in ROIS:
        role='quadrature' if quadrature else 'unjittered' if without_jitter else 'uncalibrated'
        post,pre=(consumed(c,tag+'-'+roi) for tag in ('resolved',role))
        changed=0;maximum=0.0;eligible=0;pixel_frames=0;minimum_marked=1.0
        for f in range(1024,1152):
            a=read(rows[f,'resolved-'+roi])[...,3];b=read(rows[f,role+'-'+roi])[...,3]
            if quadrature:
                marked=b<0;eligible+=int(marked.sum());pixel_frames+=b.size;b=abs(b)
                if marked.any():minimum_marked=min(minimum_marked,float(b[marked].min()))
                assert ((b>=0)&(b<=1)).all() and (b[marked]>=.4995).all()
            changed+=int((a!=b).sum());maximum=max(maximum,float(abs(a-b).max()))
        coverage[roi]=dict(changedPixelFrames=changed,maximumDifference=maximum)
        if quadrature:coverage[roi].update(eligiblePixelFrames=eligible,eligibleFraction=eligible/pixel_frames,minimumMarkedCoverage=minimum_marked if eligible else None)
        if not without_jitter and not quadrature:assert changed==0,'Calibration control changed coverage'
        v=decompose(pre,post)
        regions[roi]=dict(nativeMean=v['calibratedMean'],unjitteredMean=v['uncalibratedMean'],nativeVariance=v['calibratedVariance'],unjitteredVariance=v['uncalibratedVariance'],
            unjitteredVarianceChangePercent=100*(v['uncalibratedVariance']/v['calibratedVariance']-1),unjitteredMeanChangePercent=100*(v['uncalibratedMean']/v['calibratedMean']-1),
            jitterDeltaVariance=v['netCalibrationDeltaVariance'],twiceCovariance=v['twiceCovariance'],closureAbsoluteError=v['closureAbsoluteError']) if without_jitter or quadrature else v
        if quadrature:
            regions[roi]={k.replace('unjittered','quadrature'):value for k,value in regions[roi].items()}
            rgb_native=consumed_rgb_mean(rows,'resolved-'+roi);rgb_quad=consumed_rgb_mean(rows,'quadrature-'+roi)
            regions[roi].update(nativeRgbMean=rgb_native.tolist(),quadratureRgbMean=rgb_quad.tolist(),
                rgbMeanChangePercent=(100*(rgb_quad-rgb_native)/np.maximum(abs(rgb_native),1e-12)).tolist())
        x,y,n={'ceiling':(46,59,28),'floor':(54,8,12),'back':(68,38,8),'edge':(55,26,10)}[roi]
        words=np.stack([np.fromfile(rows[f,'packed-normal-points-'+roi]['path'],'<u4') for f in range(1024,1152)])
        assert all(rows[f,'packed-normal-points-'+roi]['format']=='A2B10G10R10_UNormPack32' for f in range(1024,1152))
        uv=np.stack([words&1023,(words>>10)&1023],-1)/1023*2-1
        exact=np.concatenate([uv,1-abs(uv).sum(-1,keepdims=True)],-1);fold=np.maximum(-exact[...,2:3],0)
        exact[...,:2]+=np.where(exact[...,:2]>=0,-fold,fold);exact/=np.linalg.norm(exact,axis=-1,keepdims=True)
        quantized=normal_from_packed(geometry[:,y,x:x+n,3])
        angles=np.degrees(np.arccos(np.clip((exact*quantized).sum(-1),-1,1)))
        sh=np.stack([read(rows[f,'sh-'+roi]) for f in range(1024,1152)]);alpha=np.stack([read(rows[f,'anchor-'+roi])[0,:,3] for f in range(1024,1152)])
        mismatch=((evaluate_sh(sh,exact)-evaluate_sh(sh,quantized))*alpha[...,None])@LUMA
        normal_points[roi]=dict(meanAngleDegrees=float(angles.mean()),maximumAngleDegrees=float(angles.max()),
            normalEvaluationDeltaTemporalRms=float(np.sqrt(mismatch.var(axis=0).mean())),normalEvaluationDeltaMean=float(mismatch.mean()),
            scope='Probe centers only, pre-clamp SH evaluation; not an upper bound for the final pixel ROI.')
    coverage_exact=not any(v['changedPixelFrames'] for v in coverage.values())
    gate_passed=coverage_exact or not protocol['gates']['coverageExact']
    report=dict(schema='screen-probe-quadrature-counterfactual.v1' if quadrature else 'screen-probe-jitter-counterfactual.v1' if without_jitter else 'screen-probe-resolve-components.v1',qualified=gate_passed,nativeCopiedResolveAaPairs=128,
        coverageExact=coverage_exact,coverage=coverage,originalCoverageGatePassed=gate_passed,
        regions=regions,normalPointDiagnostics=normal_points,productionQualified=False,
        limits=['Net delta includes the changed algorithm, nonnegative clamp, near-contact/repair interactions and FP16 rounding.',
            'Variance components are correlated; twiceCovariance must be retained.',
            'Diagnostic SH atlas has22planes, not the normal7plane layout; no GPU-cost or downstream candidate-quality acceptance.'])
    if without_jitter:
        report['limits'].append('Original jitter protocol inherited exact coverage from calibration control; a failed coverage gate remains failed. Jitter changes interpolation support, so its consumed-lighting results are descriptive only.')
    if quadrature:
        assert len(capture['quadraturePrograms'])==6
        assert sum(v['eligiblePixelFrames'] for v in coverage.values())>0,'Quadrature branch did not execute'
        mean=all(abs(v['quadratureMeanChangePercent'])<=protocol['gates']['meanChangeAbsPercent']
            and all(abs(channel)<=protocol['gates']['meanChangeAbsPercent'] for channel in v['rgbMeanChangePercent']) for v in regions.values())
        noise=regions['ceiling']['quadratureVarianceChangePercent']<=-protocol['gates']['varianceReductionPercent']
        other=all(v['quadratureVarianceChangePercent']<=protocol['gates']['otherVarianceIncreasePercent'] for v in regions.values())
        report.update(rawMeanGatePassed=mean,rawCeilingNoiseGatePassed=noise,rawNoRegressionGatePassed=other,
            rawPilotPassed=gate_passed and mean and noise and other,
            baselineProgram='In-memory prototype with quadrature flag disabled, not an unchanged production PSO.',
            candidateScope='Only private Resolve; negative alpha marks eligible pixels. No candidate pixel history or final-denoiser quality claim.')
    return report


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();v=analyze(args.root.resolve());args.output.write_text(json.dumps(v,indent=2)+'\n');print(json.dumps(v,indent=2))
