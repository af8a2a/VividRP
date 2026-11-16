"""Validate lossless final statistics and compare longer stationary GI windows."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeBlackEnvironmentQuality import inspect,ROIS
from TemporalSamplingExperiment import read,LUMA


def final_statistics(root,protocol,rows,roi):
    count=protocol['measuredFrames'];start=protocol['warmup']
    first=rows[start,'final-'+roi];pixels=first['width']*first['height']
    def values(name):
        v=np.fromfile(root/('final-'+roi+'-'+name+'.f64'),'<f8');assert np.isfinite(v).all();return v
    sums,squares,delta,blocks=(values(n) for n in ('sums','squares','delta-squares','blocks'))
    assert all(v.shape==(pixels,) for v in (sums,squares,delta))
    blocks=blocks.reshape(count//64,pixels)
    assert (squares>=0).all() and (delta>=0).all()
    assert np.allclose(blocks.sum(0),sums,rtol=1e-12,atol=1e-10)
    mean=sums/count;variance=squares/count-mean*mean
    assert variance.min()>-1e-9
    variance=np.maximum(variance,0)
    saved=[]
    prefix=128 if count==128 else 64
    for f in range(start,start+prefix):
        v=read(rows[f,'final-'+roi]).astype(float)
        saved.append(((v[...,:3]*abs(v[...,3:4]))@LUMA).reshape(-1))
    saved=np.stack(saved)
    assert np.allclose(saved[:64].sum(0),blocks[0],rtol=1e-12,atol=1e-10)
    if count==128:
        assert np.allclose(saved.sum(0),sums,rtol=1e-12,atol=1e-10)
        assert np.allclose((saved*saved).sum(0),squares,rtol=1e-12,atol=1e-10)
        assert np.allclose((np.diff(saved,axis=0)**2).sum(0),delta,rtol=1e-10,atol=1e-10)
    else:
        assert np.all(delta+1e-10>=(np.diff(saved,axis=0)**2).sum(0))
    return dict(mean_y=float(mean.mean()),temporal_variance=float(variance.mean()),
        temporal_rms=float(np.sqrt(variance.mean())),relative_rms_percent=float(100*np.sqrt(variance.mean())/mean.mean()),
        frameDifferenceRms=float(np.sqrt(delta.mean()/(count-1))),
        block64MeanTemporalRms=float(np.sqrt(np.mean((blocks/64-mean)**2))),
        perPixelAggregateVerifiedAgainstSavedPrefix=prefix,
        allAggregateTermsVerified=count==128)


def validate(root):
    _,protocol,rows,_=inspect(root)
    assert protocol.get('finalStatistics')
    return dict(schema='black-environment-final-statistics-validation.v1',qualified=True,
        measuredFrames=protocol['measuredFrames'],regions={roi:final_statistics(root,protocol,rows,roi) for roi in ROIS})


def compare(baseline,candidate):
    captures=[inspect(r) for r in (baseline,candidate)]
    a,b=captures;assert not a[1]['corrected'] and b[1]['corrected']
    assert all(c[1]['measuredFrames']==1024 and c[1]['guided'] and c[1].get('finalStatistics') for c in captures)
    assert a[1]['sequence']==b[1]['sequence']
    differences=[]
    warmup=a[1]['warmup']
    for f in range(warmup,warmup+1024):
        for tag in ('geometry','pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1'):
            if any(a[2][f,tag][k]!=b[2][f,tag][k] for k in ('sha256','width','height','format')):differences.append([f,tag])
        if a[3][f]['vp']!=b[3][f]['vp'] or a[3][f]['state']['inverseVp']!=b[3][f]['state']['inverseVp']:differences.append([f,'matrices'])
    if differences:return dict(schema='screen-probe-black-environment-long-comparison.v1',qualified=False,inputDifferences=differences,productionQualified=False)
    regions={};gates=a[1]['gates']
    for roi in ROIS:
        x,y=(final_statistics(root,c[1],c[2],roi) for root,c in zip((baseline,candidate),captures))
        regions[roi]=dict(baseline=x,candidate=y,comparison=dict(mean_change_percent=100*(y['mean_y']/x['mean_y']-1),
            variance_reduction_percent=100*(1-y['temporal_variance']/x['temporal_variance']),
            frameDifferenceRmsChangePercent=100*(y['frameDifferenceRms']/x['frameDifferenceRms']-1),
            block64MeanRmsChangePercent=100*(y['block64MeanTemporalRms']/x['block64MeanTemporalRms']-1)))
    raw=[]
    for root in (baseline,candidate):
        sums=np.fromfile(root/'block-sums.f64','<f8').reshape(16,68,120,5)
        raw.append(sums[...,:3].sum((0,1,2)))
    channel_change=100*(raw[1]/raw[0]-1)
    energy=all(abs(v['comparison']['mean_change_percent'])<=gates['meanChangeAbsPercent'] for v in regions.values())
    noise=any(regions[roi]['comparison']['variance_reduction_percent']>=gates['affectedVarianceReductionPercent'] for roi in ('floor','back'))
    no_regression=all(v['comparison']['variance_reduction_percent']>=-gates['otherVarianceIncreasePercent'] for v in regions.values())
    delta=all(v['comparison']['frameDifferenceRmsChangePercent']<=gates['frameDifferenceRmsIncreasePercent'] for v in regions.values())
    raw_gate=bool(np.all(abs(channel_change)<=1))
    return dict(schema='screen-probe-black-environment-long-comparison.v1',qualified=True,inputDifferences=[],
        measuredFrames=1024,sequence=a[1]['sequence'],regions=regions,rawBaseGridConsumedChannelChangePercent=channel_change.tolist(),
        meanGatePassed=energy,affectedNoiseGatePassed=noise,otherNoiseGatePassed=no_regression,
        frameDifferenceGatePassed=delta,rawMeanGatePassed=raw_gate,
        longWindowPassed=energy and noise and no_regression and delta and raw_gate,productionQualified=False,
        limits=['Exact aggregate variance, not a reconstructed or fitted time series.',
            'Long windows do not erase the failed original128frame guided edge gate.',
            'No independent-sequence, motion or GPU-cost claim; block64 means are descriptive, not IID confidence bounds.'])


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('baseline',type=Path);parser.add_argument('candidate',type=Path,nargs='?');parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();result=compare(args.baseline.resolve(),args.candidate.resolve()) if args.candidate else validate(args.baseline.resolve())
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps(result,indent=2))
