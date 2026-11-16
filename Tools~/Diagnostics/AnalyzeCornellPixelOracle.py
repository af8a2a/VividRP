"""Compare actual final pixels with an independently sampled Lambert reference."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_replay
from CornellPixelOracle import prepare
from TemporalSamplingExperiment import read,LUMA,metrics


def comparison(baseline,candidate,reference,reference_variance):
    """IID-reference uncertainty only; renderer frames are correlated."""
    a,b,r,v=(np.asarray(q,float) for q in (baseline,candidate,reference,reference_variance))
    assert a.shape==b.shape and a.ndim==2 and r.shape==v.shape==a.shape[1:]
    assert all(np.isfinite(q).all() for q in (a,b,r,v)) and (v>=0).all()
    ma,mb=a.mean(0),b.mean(0);denom=float(r.mean());assert denom>0
    reference_sem=float(np.sqrt(v.sum())/len(v))
    mse_a,mse_b=(float(np.mean((m-r)**2)) for m in (ma,mb))
    delta_mse=float(np.mean((mb-ma)*(mb+ma-2*r)))
    delta_sem=float(2*np.sqrt(np.sum((mb-ma)**2*v))/len(v))
    signed_mean_errors=[100*float((m-r).mean())/denom for m in (ma,mb)]
    # The triangle inequality bounds added absolute mean error without fitting
    # or treating the noisy reference mean as exact.
    added_mean_bound=100*float(abs((mb-ma).mean()))/max(denom-3*reference_sem,1e-12)
    assert np.isclose(delta_mse,mse_b-mse_a,atol=1e-12,rtol=1e-9)
    return dict(referenceMean=denom,referenceMeanIidStandardError=reference_sem,
        baseline=metrics(a),candidate=metrics(b),
        meanErrorPercent=dict(baseline=signed_mean_errors[0],candidate=signed_mean_errors[1]),
        addedAbsoluteMeanErrorPercentOfReference=abs(signed_mean_errors[1])-abs(signed_mean_errors[0]),
        addedAbsoluteMeanErrorUpperBoundPercentOfReference=added_mean_bound,
        meanMapMse=dict(baseline=mse_a,candidate=mse_b,referenceNoiseBias=float(v.mean()),
            baselineReferenceNoiseCorrected=mse_a-float(v.mean()),candidateReferenceNoiseCorrected=mse_b-float(v.mean()),
            delta=delta_mse,deltaReferenceIidStandardError=delta_sem,
            deltaPercentOfBaseline=100*delta_mse/max(mse_a,1e-20),
            deltaPlusThreeSigmaPercentOfBaseline=100*(delta_mse+3*delta_sem)/max(mse_a,1e-20)),
        absoluteMeanMapErrorP99=dict(baseline=float(np.quantile(abs(ma-r),.99)),candidate=float(np.quantile(abs(mb-r),.99))),
        uncertaintyScope='Independent reference sampling only; no renderer temporal independence or whole-image confidence claim')


def qualify_wavelet_inputs(rows,frames,control_side='quadrature-reference'):
    pairs=0
    for f in frames:
        for role in ('witness-input-full','witness-moments-full'):
            a,b=(rows[f,role,side] for side in ('candidate',control_side))
            assert all(a[key]==b[key] for key in ('sha256','width','height','format')),'Wavelet counterfactual inputs differ'
            pairs+=1
    assert pairs>0
    return pairs


def analyze(capture_root,reference_root,control_side='baseline'):
    c,p,_,rows,pairs,_=qualify_replay(capture_root)
    assert not p.get('motion',False),'Static oracle comparison cannot consume a moving camera capture'
    assert control_side in ('baseline','quadrature-reference')
    counterfactual_pairs=0
    if control_side=='quadrature-reference':
        assert p['coverageWaveletWitness'] and p['coverageWeightedWavelet']
        counterfactual_pairs=qualify_wavelet_inputs(rows,range(p['warmup'],p['warmup']+p['measuredFrames']),control_side)
    _,points,qualification=prepare(capture_root)
    metadata=json.loads((reference_root/'metadata.json').read_text());assert metadata['schema']=='cornell-pixel-oracle.v1' and metadata['complete']
    reference_capture=Path(metadata['capture']).resolve()
    assert metadata['points']==len(points)
    assert metadata['samples']>=p['gates']['referencePathsPerPixel'] and metadata['bounces']==9
    assert metadata['primaryQualification']==qualification
    for name,digest in metadata['inputs'].items():assert hashlib.sha256((reference_capture/name).read_bytes()).hexdigest()==digest
    # Reuse independent paths across renderer sequences only for byte-identical
    # effective fixtures, target locations and decoded shading normals.
    assert json.loads((reference_root/'points.json').read_text())==points
    for name in ('targets.json','live-fixture.json'):
        assert (capture_root/name).read_bytes()==(reference_capture/name).read_bytes()
    for name,digest in metadata['scripts'].items():assert hashlib.sha256(Path(name).read_bytes()).hexdigest()==digest
    payload=(reference_root/'reference.jsonl').read_bytes();assert hashlib.sha256(payload).hexdigest()==metadata['referenceSha256']
    records=[json.loads(line) for line in payload.decode().splitlines()]
    assert len(records)==len(points)==len({q['index'] for q in records})
    records.sort(key=lambda q:q['index'])
    refinement=metadata.get('refinement');refined=set()
    if refinement is not None:
        refined=set(refinement['indices']);assert len(refined)==len(refinement['indices'])
        assert refined=={q['index'] for q in points if q['region']==refinement['region']}
        parent=Path(refinement['parent'])
        assert hashlib.sha256((parent/'metadata.json').read_bytes()).hexdigest()==refinement['parentMetadataSha256']
        parent_payload=(parent/'reference.jsonl').read_bytes()
        assert hashlib.sha256(parent_payload).hexdigest()==refinement['parentReferenceSha256']
        parent_records={q['index']:q for q in (json.loads(line) for line in parent_payload.decode().splitlines())}
        refined_payload=(reference_root/'refinement.jsonl').read_bytes()
        assert hashlib.sha256(refined_payload).hexdigest()==metadata['refinementSha256']
        refined_records={q['index']:q for q in (json.loads(line) for line in refined_payload.decode().splitlines())}
        assert set(refined_records)==refined and refinement['samples']>=metadata['samples']
        for record in records:
            assert record==(refined_records if record['index'] in refined else parent_records)[record['index']]
    for point,record in zip(points,records):
        assert all(record[k]==v for k,v in point.items())
        e=record['estimate']
        if refinement is not None and point['index'] not in refined:
            prior=parent_records[point['index']]['estimate'];expected=(prior['samples'],prior['diffuseReflections'],prior['seed'])
        else:
            spec=refinement if point['index'] in refined else metadata
            expected=(spec['samples'],9,spec['seed']+point['index'])
        assert (e['samples'],e['diffuseReflections'],e['seed'])==expected
    ref=np.array([q['estimate']['meanRgb'] for q in records])
    cov=np.array([q['estimate']['iidCovarianceMeanRgb'] for q in records])
    sem=np.array([q['estimate']['iidStandardErrorRgb'] for q in records])
    orders=np.array([q['estimate']['meanRgbByOrder'] for q in records])
    assert ref.shape==(len(points),3) and cov.shape==(len(points),3,3) and orders.shape==(len(points),10,3)
    assert all(np.isfinite(q).all() for q in (ref,cov,sem,orders)) and (ref>=0).all() and (orders>=0).all()
    assert np.allclose(ref,orders.sum(1),rtol=1e-11,atol=1e-12)
    assert np.allclose(np.diagonal(cov,axis1=1,axis2=2),sem**2,rtol=1e-10,atol=1e-15)
    assert np.allclose(cov,cov.transpose(0,2,1),rtol=1e-12,atol=1e-15) and np.linalg.eigvalsh(cov).min()>=-1e-12
    paths={}
    for side in (control_side,'candidate'):
        values=np.stack([read(rows[f,'oracle-final',side])[0].astype(float) for f in range(p['warmup'],p['warmup']+p['measuredFrames'])])
        assert values.shape==(p['measuredFrames'],len(points),4) and (values[...,3]>=0).all()
        paths[side]=values[...,:3]*values[...,3:4]
    regions={};gates=p['gates']
    for region in p['regions']:
        selected=np.array([q['region']==region for q in points]);assert selected.any()
        a,b=(paths[k][:,selected] for k in (control_side,'candidate'))
        v=np.maximum(np.einsum('i,nij,j->n',LUMA,cov[selected],LUMA),0)
        result=comparison(a@LUMA,b@LUMA,ref[selected]@LUMA,v)
        result['rgb']={name:comparison(a[...,i],b[...,i],ref[selected,i],cov[selected,i,i]) for i,name in enumerate(('r','g','b'))}
        channels=[result,*result['rgb'].values()]
        result['meanErrorGatePassed']=all(q['addedAbsoluteMeanErrorUpperBoundPercentOfReference']<=gates['addedAbsoluteMeanErrorPercentOfReference'] for q in channels)
        result['meanMapMsePointEstimateGatePassed']=all(q['meanMapMse']['deltaPercentOfBaseline']<=gates['addedMeanMapMsePercent'] for q in channels)
        result['meanMapMseThreeSigmaGatePassed']=all(q['meanMapMse']['deltaPlusThreeSigmaPercentOfBaseline']<=gates['addedMeanMapMsePercent'] for q in channels)
        result['points']=int(selected.sum());result['objects']={name:sum(q['object']==name and q['region']==region for q in points) for name in sorted({q['object'] for q in points if q['region']==region})}
        result['ninthOrderMeanPercentOfReference']=100*float((orders[selected,9]@LUMA).mean())/result['referenceMean']
        result['eighthOrderMeanPercentOfReference']=100*float((orders[selected,8]@LUMA).mean())/result['referenceMean']
        result['reference8ReflectionMeanY']=float((orders[selected,:9].sum(1)@LUMA).mean())
        result['referenceIidPixelYStandardErrorMedian']=float(np.median(np.sqrt(v)))
        result['referenceSamplesPerPixel']=sorted({q['estimate']['samples'] for q in records if q['region']==region})
        regions[region]=result
    return dict(schema='cornell-pixel-oracle-analysis.v1',qualified=True,sourceIndependent=True,
        nativeResolveAaPairs=len(p['aaFrames']),nativeCandidateHistoryAaPairs=pairs,
        samplesPerPixel=metadata['samples'],refinedReferenceSamplesPerPixel=refinement['samples'] if refinement else None,
        refinedReferenceRegion=refinement['region'] if refinement else None,
        referenceDiffuseReflections=metadata['bounces'],points=len(points),measuredFrames=p['measuredFrames'],
        primaryQualification=qualification,regions=regions,comparisonControlSide=control_side,
        waveletCounterfactualInputAaPairs=counterfactual_pairs,
        addedMeanErrorGatePassed=all(q['meanErrorGatePassed'] for q in regions.values()),
        addedMeanMapMsePointEstimateGatePassed=all(q['meanMapMsePointEstimateGatePassed'] for q in regions.values()),
        addedMeanMapMseThreeSigmaGatePassed=all(q['meanMapMseThreeSigmaGatePassed'] for q in regions.values()),
        referenceReusedAcrossCaptures=reference_capture!=capture_root.resolve(),productionQualified=False,
        limits=['One static renderer sampling sequence and one independent oracle seed per pixel; fixed ROIs, not the whole image.',
            'Lambert constant-material exact geometry oracle; renderer uses biased SDF, finite cards and directional reconstruction. Not OpenPBR or Unreal parity.',
            'Reference9 reflections includes one additional order beyond8 published card orders plus the Gather query. The9th-order mean is reported, not fitted away.',
            'MSE uncertainty includes independent reference sampling, not finite correlated renderer-frame uncertainty.',
            'Moving activation quality and attributable native GPU cost remain separate acceptance gates.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('reference',type=Path);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--control-side',choices=('baseline','quadrature-reference'),default='baseline')
    args=p.parse_args();assert not args.output.exists(),'Refusing to overwrite an analysis'
    report=analyze(args.capture.resolve(),args.reference.resolve(),args.control_side);args.output.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('regions','limits')},indent=2))
    print(json.dumps({name:dict(meanError=q['meanErrorPercent'],mapMse=q['meanMapMse'],noise=q['baseline']['relative_rms_percent'],candidateNoise=q['candidate']['relative_rms_percent']) for name,q in report['regions'].items()},indent=2))
