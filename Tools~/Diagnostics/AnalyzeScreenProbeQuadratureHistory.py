"""Qualify native replay and compare final stratified interpolation histories."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeHistoryEpoch import qualify
from AnalyzeScreenProbeBlackEnvironmentQuality import ROIS
from TemporalSamplingExperiment import read, LUMA, metrics, paired


ROLES = ('temporal', 'history', 'moments', 'geometry', 'final')


def qualify_cold_energy_moments(temporal,baseline,candidate):
    assert temporal.shape==baseline.shape==candidate.shape and temporal.shape[-1]==4
    assert np.array_equal(baseline[...,1:],candidate[...,1:]),'Cold moment variance/age/coverage changed'
    assert (candidate[...,1]==0).all(),'Cold moment variance must be zero'
    for value,coverage in ((baseline,1.0),(candidate,temporal[...,3])):
        expected=np.log2(1+np.maximum(temporal[...,:3],0)@LUMA*coverage).astype(np.float16)
        ulp=np.spacing(expected).astype(float)
        assert np.all(abs(value[...,0]-expected.astype(float))<=np.maximum(ulp,1e-7)*1.01),'Cold moment luminance domain mismatch'


def energy_and_noise(baseline, candidate):
    assert baseline.shape == candidate.shape and baseline.shape[-1] == 4
    assert np.isfinite(baseline).all() and np.isfinite(candidate).all()
    assert (baseline[...,3] >= 0).all() and (candidate[...,3] >= 0).all(), 'Signed eligibility entered pixel history'
    a, b = (v[...,:3]*v[...,3:4] for v in (baseline, candidate))
    rgb_a, rgb_b = (v.mean(axis=tuple(range(v.ndim-1))) for v in (a,b))
    def temporal(v):
        y=v@LUMA
        return dict(metrics(y),frame_difference_rms=float(np.sqrt(np.mean(np.diff(y,axis=0)**2))))
    return dict(baseline=temporal(a), candidate=temporal(b), comparison=paired(a@LUMA,b@LUMA),
                baselineRgbMean=rgb_a.tolist(), candidateRgbMean=rgb_b.tolist(),
                rgbMeanChangePercent=(100*(rgb_b-rgb_a)/np.maximum(abs(rgb_a),1e-12)).tolist())


def qualify_replay(root):
    capture, protocol, source_rows, _, _ = qualify(root)
    assert protocol['independentPixelHistories']
    assert protocol['schema'] in ('screen-probe-quadrature-history-protocol.v1','screen-probe-native-quadrature-history-protocol.v1')
    native_side=protocol.get('nativeSide','baseline')
    assert native_side==('candidate' if protocol['schema']=='screen-probe-native-quadrature-history-protocol.v1' else 'baseline')
    warmup, count = protocol['warmup'], protocol['measuredFrames']
    frames = [0,1,2,warmup-1,*range(warmup,warmup+count)]
    rois=tuple(protocol.get('regions',ROIS))
    assert rois and set(rois)<=set(ROIS)
    assert protocol['aaFrames']==frames
    covered_wavelet=protocol.get('coverageWeightedWavelet',False)
    energy_moments=protocol.get('consumedEnergyMoments',False)
    if protocol.get('conservativeFluxWavelet',False):
        assert energy_moments and covered_wavelet and not protocol['coverageWaveletWitness']
        programs=capture['fluxPrograms']
        assert len(programs)==3 and all(r['file']=='flux-wavelet.compute.txt' for r in programs)
        assert {r['name'] for r in programs}=={'PrepareScreenProbeFluxCS','FilterScreenProbeFluxCS','FilterScreenProbeFluxFinalCS'}
    if energy_moments:
        assert covered_wavelet and not protocol['coverageWaveletWitness']
        programs=[r for r in capture['privatePrograms'] if r['file']=='energy-temporal.compute.txt']
        assert len(programs)==1 and programs[0]['name']=='ScreenProbeTemporalReprojectionWithMomentsCS'
    if covered_wavelet:
        assert protocol['pixelOracle'] and native_side=='candidate'
        programs=[r for r in capture['privatePrograms'] if r['file']=='covered-wavelet.compute.txt']
        assert len(programs)==2 and {r['name'] for r in programs}=={'FilterSdfDiffuseAtrous','FilterSdfDiffuseAtrousFinal'}
    assert len(capture['quadraturePrograms'])==6 and len(capture['privatePrograms'])==(6 if covered_wavelet else 4)+int(energy_moments)
    assert capture['privateInitialized'] and capture['componentAaPairs']==len(frames)
    component = [r for r in capture['records'] if r.get('componentAa')]
    assert len(component)==2*len(frames)
    for frame in frames:
        values=[r for r in component if r['frame']==frame]
        assert len(values)==2 and {v['side'] for v in values}=={'native','copied'}
        assert len({v['sha256'] for v in values})==1
    rows = {(r['frame'],r['tag'],r['side']):r for r in capture['records'] if 'tag' in r and 'side' in r}
    assert len(rows)==sum('tag' in r and 'side' in r for r in capture['records'])
    initial=[r for r in capture['records'] if r.get('privateInitialization')]
    assert len(initial)==1 and initial[0]['frame']==0 and not initial[0]['hasHistory'] and not initial[0]['momentsValid']
    pairs=0
    for frame in frames:
        for role in ROLES:
            for roi in (*rois, 'full'):
                tag=role+'-'+roi
                native,matching=(rows[frame,tag,side] for side in ('native',native_side))
                assert native['sha256']==matching['sha256'], (frame,tag)
                pairs+=1
                # A changed wavelet may change cold final, and a moment-domain
                # change is checked numerically below. Cold colors and all
                # geometry still agree; every native/candidate role remains exact.
                if roi!='full' and (role=='geometry' or frame==0) and not (covered_wavelet and role=='final') and not (energy_moments and role=='moments'):
                    assert rows[frame,tag,'baseline']['sha256']==rows[frame,tag,'candidate']['sha256'], 'Cold paths/geometry must agree'
    if energy_moments:
        for roi in rois:
            qualify_cold_energy_moments(*(read(rows[0,tag+'-'+roi,side]).astype(float)
                for tag,side in (('temporal','candidate'),('moments','baseline'),('moments','candidate'))))
    assert pairs==capture['privateAaPairs']==len(frames)*5*(len(rois)+1)
    # The shared source final includes any renderer post-wavelet work. Requiring
    # this equality prevents reporting a pre-occlusion replay as final output.
    for frame in range(warmup,warmup+count):
        for roi in rois:
            assert source_rows[frame,'final-'+roi]['sha256']==rows[frame,'final-'+roi,native_side]['sha256']
    return capture,protocol,source_rows,rows,pairs,initial[0]


def analyze(root):
    capture,protocol,source_rows,rows,pairs,initial=qualify_replay(root)
    assert not protocol.get('motion',False),'Moving frames require the disocclusion analysis'
    warmup,count=protocol['warmup'],protocol['measuredFrames']
    # Temporal crops are saved only at endpoints; their independent recurrence
    # is verified by all-frame hashes, while final crops cover every frame.
    final={}
    for roi in ROIS:
        a,b=(np.stack([read(rows[f,'final-'+roi,side]).astype(float) for f in range(warmup,warmup+count)])
             for side in ('baseline','candidate'))
        final[roi]=energy_and_noise(a,b)
    gates=protocol['gates']
    mean=all(abs(v['comparison']['mean_change_percent'])<=gates['meanChangeAbsPercent']
             and all(abs(c)<=gates['meanChangeAbsPercent'] for c in v['rgbMeanChangePercent']) for v in final.values())
    noise=final['ceiling']['comparison']['variance_reduction_percent']>=gates['ceilingVarianceReductionPercent']
    others=all(v['comparison']['variance_reduction_percent']>=-gates['otherVarianceIncreasePercent'] for v in final.values())
    # Compare final sequence frame differences separately from temporal variance.
    frame_deltas={roi:100*(v['candidate']['frame_difference_rms']/max(v['baseline']['frame_difference_rms'],1e-12)-1)
                  for roi,v in final.items()}
    frame_gate=all(v<=gates['frameDifferenceRmsIncreasePercent'] for v in frame_deltas.values())
    native_candidate=protocol.get('nativeSide')=='candidate'
    return dict(schema='screen-probe-quadrature-history-analysis.v1', qualified=True,
        nativeBaselineQualified=not native_candidate,nativeCandidateQualified=native_candidate,
        fullImageResolveAaPairs=len(protocol['aaFrames']), privateHistoryAaPairs=pairs, measuredFrames=count, payloadBytes=capture['payloadBytes'],
        initialState=initial, regions=final, frameDifferenceRmsChangePercent=frame_deltas,
        finalMeanGatePassed=mean, finalCeilingNoiseGatePassed=noise, finalNoRegressionGatePassed=others,
        finalFrameDifferenceGatePassed=frame_gate, pilotPassed=mean and noise and others and frame_gate,
        productionQualified=False,
        limits=[('Native candidate writes actual pixel histories and final output; control uses independent private histories on the same native probes.' if native_candidate
                 else 'Native baseline is the in-memory Gather prototype with quadrature disabled, not the unchanged production PSO.'),
                'Shared probe transport: pixel output does not feed ray guidance or Surface Cache; screen source exports direct diffuse plus emission.',
                'One static sequence, incident diffuse GI before receiver reflectance; no physical-reference, motion or GPU-cost acceptance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();result=analyze(args.root.resolve())
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))
