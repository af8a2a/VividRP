"""Verify parking/reentry continuity without treating moving pixels as noise."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import energy_and_noise, ROLES
from TemporalSamplingExperiment import read, LUMA


def reentry_steps(baseline,candidate,eligible,stationary_start):
    a,b=((v[...,:3]*v[...,3:4])@LUMA for v in (baseline,candidate))
    events=np.flatnonzero((np.diff(eligible)>0)&(np.arange(1,len(eligible))>stationary_start))+1
    assert events.size,'Quadrature did not reenter after stopping'
    windows=sorted({f for i in events for f in range(max(stationary_start+1,i-1),min(len(a),i+9))})
    step=np.diff(b-a,axis=0)
    means=[];p99=[]
    for f in windows:
        scale=max(abs(float(a[f].mean())),1e-12)
        means.append(100*abs(float(step[f-1].mean()))/scale)
        p99.append(100*float(np.quantile(abs(step[f-1]),.99))/scale)
    return dict(firstReentryIndex=int(events[0]),reentryEventFrames=events.tolist(),
                evaluatedFrames=windows,maximumRoiMeanStepPercent=max(means),maximumP99GainStepPercent=max(p99))


def analyze(root):
    root=root.resolve();load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
    c,p=load('capture.json'),load('protocol.json')
    assert c['complete'] and not c['errors'] and load('source-verification.json')['unchanged']
    assert p['schema']=='screen-probe-quadrature-recovery-protocol.v1'
    for name,digest in p['files'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    total=0
    for row in c['records']:
        if not row.get('path'):continue
        path=Path(row['path']).resolve();assert path.is_relative_to(root)
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row['sha256'];total+=len(data)
    assert total==c['payloadBytes']<=c['payloadBudget']==p['payloadBudget']
    assert c['epochReset'] and not c['experimentalTracePrograms'] and c['privateInitialized']
    required={'ProbeHistoryId','AnchorStatisticsHistoryId','GeometryHistoryId','DirectionalHistoryId',
              'DirectionalGuideHistoryId','CoveredPixelHistoryId','ScreenProbeMomentsHistoryId','PixelGeometryHistoryId'}
    assert required.issubset(c['epochInvalidated'])
    first=[r for r in c['records'] if 'epochFirstFrame' in r]
    assert len(first)==1 and first[0]['epochFirstFrame']==0 and not first[0]['hasHistory'] and not first[0]['anchorStatisticsHistoryValid']
    assert (first[0]['publishedOrder'],first[0]['batch'],first[0]['sourceUpdates'])==(8,32,0)
    source_rows={(r['frame'],r['tag']):r for r in c['records'] if 'tag' in r and 'side' not in r}
    for roi in ('ceiling','floor','back','edge'):
        statistics,geometry,anchor,raw=(read(source_rows[0,'epoch-'+name+'-'+roi]).astype(float)
                                        for name in ('statistics','geometry','anchor','raw'))
        valid=(geometry[...,3]>=0)&(raw[...,3]>0)
        assert valid.any() and (statistics[...,0][valid]==1).all() and (statistics[...,1:][valid]==0).all()
        assert np.array_equal(anchor[...,3],raw[...,3])
        a,b=(v[...,:3]*v[...,3:4] for v in (anchor,raw))
        assert np.all(abs(a-b)<=.0002+.002*np.maximum(abs(a),abs(b)))
    assert (c['warmup'],c['measuredFrames'],c['spacing'],c['rays'])==(1024,256,16,8)
    aa=[r for r in c['records'] if r.get('componentAa')]
    assert len(aa)==len(p['aaFrames'])*2 and c['componentAaPairs']==len(p['aaFrames'])
    rows={(r['frame'],r['tag'],r['side']):r for r in c['records'] if 'tag' in r and 'side' in r}
    assert len(rows)==sum('tag' in r and 'side' in r for r in c['records'])
    pairs=0
    for f in p['aaFrames']:
        comp=[r for r in aa if r['frame']==f]
        assert len(comp)==2 and {r['side'] for r in comp}=={'native','copied'} and len({r['sha256'] for r in comp})==1
        for role in ROLES:
            for roi in (*p['regions'],'full'):
                a,b=(rows[f,role+'-'+roi,s] for s in ('native','baseline'))
                assert a['sha256']==b['sha256'],(f,role,roi);pairs+=1
                if roi!='full' and (role=='geometry' or f==0):assert b['sha256']==rows[f,role+'-'+roi,'candidate']['sha256']
    assert pairs==c['privateAaPairs']==len(p['aaFrames'])*15
    initial=[r for r in c['records'] if r.get('privateInitialization')]
    assert len(initial)==1 and initial[0]['frame']==0 and not initial[0]['hasHistory'] and not initial[0]['momentsValid']
    source=[r for r in c['records'] if r.get('recoverySource')]
    assert {r['frame'] for r in source}=={1024,1279} and len(source)==2
    assert {r['sha256'] for r in source}=={'40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8'}
    states={r['frame']:r for r in c['records'] if r.get('recoveryState')}
    assert set(states)==set(range(1024,1280))
    for state in states.values():
        assert (state['order'],state['batch'],state['updates'],state['samples'],state['seed'],state['width'],state['height'],state['flags'])==(8,32,0,64,0,1920,1080,0)
    vp=np.array([states[f]['vp'] for f in range(1024,1280)])
    assert np.max(abs(vp[:128]-vp[0]))>.01,'Camera did not follow motion'
    stationary=(np.max(abs(vp-vp[-1]),axis=1)<=1e-7)&(np.arange(256)>=128)
    start=int(np.flatnonzero(stationary)[0]);assert stationary[start:].all()
    g=p['gates'];stationary_frames=int(stationary.sum());assert stationary_frames>=g['minimumStationaryFrames']
    eligibility={(r['frame'],r['recoveryEligibility']):r for r in c['records'] if 'recoveryEligibility' in r}
    assert len(eligibility)==256*len(p['regions'])
    regions={}
    for roi in p['regions']:
        eligible=np.array([eligibility[f,roi]['eligible'] for f in range(1024,1280)])
        pixels=eligibility[1024,roi]['pixels'];assert ((eligible>=0)&(eligible<=pixels)).all()
        a,b=(np.stack([read(rows[f,'final-'+roi,s]).astype(float) for f in range(1024,1280)]) for s in ('baseline','candidate'))
        steps=reentry_steps(a,b,eligible,start)
        regions[roi]=dict(steps,firstReentryFrame=1024+steps['firstReentryIndex'],
            finalEligibleFraction=float(eligible[-1]/pixels),eligibleFractions=(eligible/pixels).tolist(),
            late64=energy_and_noise(a[-64:],b[-64:]))
    mean=all(abs(v['late64']['comparison']['mean_change_percent'])<=g['recoveredMeanChangeAbsPercent']
        and all(abs(x)<=g['recoveredMeanChangeAbsPercent'] for x in v['late64']['rgbMeanChangePercent']) for v in regions.values())
    step=all(v['maximumRoiMeanStepPercent']<=g['reentryRoiMeanStepAbsPercent'] for v in regions.values())
    p99=all(v['maximumP99GainStepPercent']<=g['reentryP99GainStepAbsPercent'] for v in regions.values())
    eligible=regions['ceiling']['finalEligibleFraction']>=g['minimumFinalCeilingEligibleFraction']
    return dict(schema='screen-probe-quadrature-recovery-analysis.v1',qualified=True,nativeBaselineQualified=True,
        resolveAaPairs=len(p['aaFrames']),privateAaPairs=pairs,payloadBytes=total,stationaryFrames=stationary_frames,
        firstStationaryIndex=start,regions=regions,recoveredMeanGatePassed=mean,reentryMeanStepGatePassed=step,
        reentryP99GainStepGatePassed=p99,eligibilityRecoveryGatePassed=eligible,recoveryPassed=mean and step and p99 and eligible,
        productionQualified=False,limits=['Shared native probe input; original4pass wavelet replay with independent pixel histories.',
        'Gain step subtracts native baseline change to isolate candidate reentry; normalized by baseline ROI mean, not each dark pixel.',
        'Late64 temporal variance is descriptive during continuing anchor convergence; no absolute physical or Unreal reference.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();v=analyze(args.root);args.output.write_text(json.dumps(v,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({**{k:v for k,v in v.items() if k!='regions'},'regions':{r:{k:v for k,v in d.items() if k not in ('eligibleFractions','late64','evaluatedFrames','reentryEventFrames')} for r,d in v['regions'].items()}},indent=2))
