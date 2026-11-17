"""Strict native card-query A/B and final-pixel spatial/energy reference checks."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from AnalyzeCornellPixelOracle import comparison
from CornellPixelOracle import Scene, match_receivers
from TemporalSamplingExperiment import read, LUMA
from ScreenProbeOriginReplay import TRACE_KERNELS

ROIS=('ceiling','floor','back','edge')
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()


def qualify(root):
    c,p=load(root/'capture.json'),load(root/'protocol.json')
    precision_protocol=p['schema']=='screen-probe-cold-replay.v1'
    if precision_protocol:
        from AnalyzeScreenProbeColdReplay import qualify as qualify_repeatability
        qualify_repeatability(root)
        assert p['frames']==1152 and p['probeFrame']==-1
        assert not any(p.get(k,False) for k in ('prefixOnly','dense','fingerprints','witness'))
        assert all(p[k] for k in ('coordinatePrecise','anchorPrecise','resolvePrecise','pixelPrecise','filterPrecise','receiverPrecise'))
        # v1 originally captured only the original query; the candidate flag
        # was added when the same precision configuration became eligible for A/B.
        p=dict(p,schema='screen-probe-card-quality.v1',captureSchema=p['schema'],
            cardCoverageCandidate=p.get('cardCoverageCandidate',False),quadrature=False,motion=False,warmup=1024,measuredFrames=128,
            gates=dict(inputGeometryExact=True,nativePrivateHistoryByteExact=True,addedMeanMapMsePercent=1,
                addedAbsoluteMeanErrorPercentOfReference=.2,otherVarianceIncreasePercent=2,frameDifferenceRmsIncreasePercent=2))
    assert p['schema']=='screen-probe-card-quality.v1' and not p['quadrature'] and not p['motion']
    assert c['complete'] and not c['errors'] and c['epochReset'] and c['privateInitialized']
    assert load(root/'source-verification.json')['unchanged']
    snapshot=load(root/'source-before-capture.json')
    for kind in ('inputs','scripts'):
        for row in snapshot[kind]:assert digest(Path(row['path']))==row['sha256']
    assert (c['warmup'],c['measuredFrames'],c['spacing'],c['rays'])==(1024,128,16,8)
    assert (p['warmup'],p['measuredFrames'],p['spacing'],p['rays'])==(1024,128,16,8)
    assert {q['name'] for q in c['experimentalTracePrograms']}==set(TRACE_KERNELS)
    assert len(c['experimentalTracePrograms'])==9 and len(c['privatePrograms'])==(7 if precision_protocol else 4)
    assert len(c['quadraturePrograms'])==6 and len(c['oraclePrograms'])==1
    total=0
    for row in c['records']:
        if not row.get('path'):continue
        path=Path(row['path']).resolve();assert path.is_relative_to(root.resolve())
        assert digest(path)==row['sha256'];total+=path.stat().st_size
    assert total==c['payloadBytes']<=c['payloadBudget']==p['payloadBudget']
    source={(r['frame'],r['tag']):r for r in c['records'] if 'tag' in r and 'side' not in r}
    rows={(r['frame'],r['tag'],r['side']):r for r in c['records'] if 'tag' in r and 'side' in r}
    states={r['frame']:r for r in c['records'] if 'state' in r}
    assert len(source)+len(rows)==sum('tag' in r for r in c['records'])
    assert set(states)==set(range(1024,1152))
    for r in states.values():
        s=r['state']
        assert (s['order'],s['batch'],s['samples'],s['seed'],s['flags'])==(8,32,64,0,0)
        assert (s['anchorMaxAge'],s['pixelMaxAge'],s['atrous'])==(512,32,4)
        assert s['guide'] and not s['restir'] and s['traceMode']=='Hybrid'
        assert s['activeSkyType']=='None' and s['sun']==[0,0,0] and s['sky']==0
    assert {source[f,'radiosity']['sha256'] for f in (1024,1151)}=={'40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8'}
    required={'ProbeHistoryId','AnchorStatisticsHistoryId','GeometryHistoryId','DirectionalHistoryId',
        'DirectionalGuideHistoryId','CoveredPixelHistoryId','ScreenProbeMomentsHistoryId','PixelGeometryHistoryId'}
    assert required.issubset(c['epochInvalidated'])
    first=[r for r in c['records'] if 'epochFirstFrame' in r];assert len(first)==1
    assert first[0]['epochFirstFrame']==0 and not first[0]['hasHistory'] and not first[0]['anchorStatisticsHistoryValid']
    assert (first[0]['publishedOrder'],first[0]['batch'],first[0]['sourceUpdates'])==(8,32,0)
    for roi in ROIS:
        statistics,geometry,anchor,raw=(read(source[0,tag+'-'+roi]).astype(float) for tag in ('epoch-statistics','epoch-geometry','epoch-anchor','epoch-raw'))
        valid=(geometry[...,3]>=0)&(raw[...,3]>0)
        assert (statistics[...,0][valid]==1).all() and (statistics[...,1:][valid]==0).all()
        assert np.array_equal(anchor[...,3],raw[...,3])
        a,b=(v[...,:3]*v[...,3:4] for v in (anchor,raw))
        assert np.all(abs(a-b)<=.0002+.002*np.maximum(abs(a),abs(b)))
    frames=[0,1,2,1023,*range(1024,1152)]
    component=[r for r in c['records'] if r.get('componentAa')]
    assert len(component)==2*len(frames) and c['componentAaPairs']==len(frames)
    for f in frames:
        pair=[r for r in component if r['frame']==f]
        assert len(pair)==2 and {r['side'] for r in pair}=={'native','copied'}
        assert len({r['sha256'] for r in pair})==1
        for roi in (*ROIS,'full'):
            for role in ('temporal','history','moments','geometry','final'):
                pair=[rows[f,role+'-'+roi,side] for side in ('native','candidate')]
                assert all(pair[0][key]==pair[1][key] for key in ('sha256','width','height','format'))
    assert c['privateAaPairs']==len(frames)*25==3300
    initial=[r for r in c['records'] if r.get('privateInitialization')]
    assert len(initial)==1 and initial[0]['frame']==0 and not initial[0]['hasHistory'] and not initial[0]['momentsValid']
    for f in range(1024,1152):
        for roi in ROIS:assert source[f,'final-'+roi]['sha256']==rows[f,'final-'+roi,'candidate']['sha256']
        assert rows[f,'oracle-final','baseline']['sha256']==rows[f,'oracle-final','candidate']['sha256']
    fixture=load(root/'live-fixture.json');targets=load(root/'targets.json')
    records=[r for r in c['records'] if 'oracleFixturePath' in r];assert len(records)==1
    assert digest(root/'live-fixture.json')==records[0]['fixtureSha256'] and len(targets)==2336
    assert len(fixture['geometry'])==8 and all(q['cullMode']==2 and np.max(abs(np.array(q['meshCenter'])))<1e-8 for q in fixture['geometry'])
    assert np.max(abs(np.array(fixture['camera']['euler'])))<1e-8
    scene=Scene(fixture,'black');points=None
    for f in range(1024,1152):
        g,n=(read(rows[f,'oracle-'+tag,'source'])[0].astype(float) for tag in ('geometry','normal'))
        if points is None:
            position,normal,hit,primary=match_receivers(scene,fixture['camera'],targets,g,n)
            first_g,first_n=g,n
            points=[dict(index=i,**q,object=scene.items[hit[i]]['name'],position=position[i].tolist(),normal=normal[i].tolist()) for i,q in enumerate(targets)]
        else:assert np.array_equal(first_g,g) and np.array_equal(first_n,n)
    values=np.stack([read(rows[f,'oracle-final','candidate'])[0].astype(float) for f in range(1024,1152)])
    assert values.shape==(128,2336,4) and np.isfinite(values).all() and (values[...,3]>=0).all()
    return dict(c=c,p=p,source=source,rows=rows,states=states,points=points,primary=primary,rgb=values[...,:3]*values[...,3:4])


def reference(root,case):
    m=load(root/'metadata.json');assert m['complete'] and m['schema']=='cornell-pixel-oracle.v1'
    assert m['samples']>=16384 and m['bounces']==9 and m['points']==2336
    old=Path(m['capture']);assert load(root/'points.json')==case['points']
    for name in ('targets.json','live-fixture.json'):assert (old/name).read_bytes()==(Path(case['root'])/name).read_bytes()
    for name,h in m['inputs'].items():assert digest(old/name)==h
    for name,h in m['scripts'].items():assert digest(Path(name))==h
    assert digest(root/'reference.jsonl')==m['referenceSha256']
    records=sorted([json.loads(s) for s in (root/'reference.jsonl').read_text().splitlines()],key=lambda q:q['index'])
    assert len(records)==len({q['index'] for q in records})==2336
    refinement=m.get('refinement')
    if refinement:
        parent=Path(refinement['parent'])
        assert digest(parent/'metadata.json')==refinement['parentMetadataSha256']
        assert digest(parent/'reference.jsonl')==refinement['parentReferenceSha256']
        assert digest(root/'refinement.jsonl')==m['refinementSha256']
        prior={q['index']:q for q in map(json.loads,(parent/'reference.jsonl').read_text().splitlines())}
        refined={q['index']:q for q in map(json.loads,(root/'refinement.jsonl').read_text().splitlines())}
        assert set(refined)==set(refinement['indices'])
        for q in records:assert q==(refined if q['index'] in refined else prior)[q['index']]
    for point,record in zip(case['points'],records):assert all(record[k]==v for k,v in point.items())
    ref=np.array([q['estimate']['meanRgb'] for q in records]);cov=np.array([q['estimate']['iidCovarianceMeanRgb'] for q in records])
    assert ref.shape==(2336,3) and cov.shape==(2336,3,3) and (ref>=0).all()
    assert np.isfinite(ref).all() and np.isfinite(cov).all() and np.linalg.eigvalsh(cov).min()>=-1e-12
    return ref,cov


def analyze(before,after,refroot,descriptive=False,baseline_repeat=None,candidate_repeat=None):
    repeatability=None
    if not descriptive:
        assert baseline_repeat is not None and candidate_repeat is not None, 'Strict quality acceptance requires baseline and candidate A/A repeats'
        from AnalyzeScreenProbeColdReplay import compare
        repeatability=[compare([before,baseline_repeat]),compare([after,candidate_repeat])]
        assert all(r['exact'] for r in repeatability), 'Cross-run A/A failed; no quality acceptance'
    a,b=(qualify(r) for r in (before,after));a['root']=str(before);b['root']=str(after)
    assert not a['p']['cardCoverageCandidate'] and b['p']['cardCoverageCandidate']
    assert a['p']['sequence']==b['p']['sequence'] and a['p']['gates']==b['p']['gates']
    assert a['points']==b['points'] and a['states']==b['states']
    pairs=0;differences=[]
    for f in range(1024,1152):
        for tag in ('geometry','pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1'):
            ar,br=(q['source'][f,tag] for q in (a,b))
            if not all(ar[k]==br[k] for k in ('sha256','width','height','format')):
                differences.append([f,tag])
            pairs+=1
    assert descriptive or not differences,('Unqualified input geometry',differences)
    ref,cov=reference(refroot,a);regions={}
    # Mixed edge support also gets separate object groups, so opposing energy
    # errors cannot cancel at the short-box / tall-box / back-wall boundary.
    groups={r:np.array([q['region']==r for q in a['points']]) for r in ROIS}
    groups.update({'object:'+name:np.array([q['object']==name for q in a['points']]) for name in sorted({q['object'] for q in a['points']})})
    for name,mask in groups.items():
        ar,br=a['rgb'][:,mask],b['rgb'][:,mask]
        result=comparison(ar@LUMA,br@LUMA,ref[mask]@LUMA,np.maximum(np.einsum('i,nij,j->n',LUMA,cov[mask],LUMA),0))
        result['rgb']={n:comparison(ar[...,i],br[...,i],ref[mask,i],cov[mask,i,i]) for i,n in enumerate(('r','g','b'))}
        result['points']=int(mask.sum())
        result['fixedNonuniformErrorRmsPercentOfReference']={n:100*float(np.std((v.mean(0)-ref[mask])@LUMA))/result['referenceMean'] for n,v in (('baseline',ar),('candidate',br))}
        result['frameDifferenceRmsChangePercent']=100*(np.sqrt(np.mean(np.diff(br@LUMA,axis=0)**2))/max(np.sqrt(np.mean(np.diff(ar@LUMA,axis=0)**2)),1e-20)-1)
        channels=[result,*result['rgb'].values()];g=a['p']['gates']
        result['mseGatePassed']=all(q['meanMapMse']['deltaPlusThreeSigmaPercentOfBaseline']<=g['addedMeanMapMsePercent'] for q in channels)
        result['energyGatePassed']=all(q['addedAbsoluteMeanErrorUpperBoundPercentOfReference']<=g['addedAbsoluteMeanErrorPercentOfReference'] for q in channels)
        result['varianceChangePercent']=100*(result['candidate']['temporal_variance']/max(result['baseline']['temporal_variance'],1e-20)-1)
        result['temporalGatePassed']=bool(result['varianceChangePercent']<=g['otherVarianceIncreasePercent'] and result['frameDifferenceRmsChangePercent']<=g['frameDifferenceRmsIncreasePercent'])
        regions[name]=result
    report=dict(schema='screen-probe-card-quality-analysis.v1',qualified=not differences,inputDifferences=differences,
        descriptiveOnly=descriptive,acceptanceEligible=not descriptive and not differences,productionQualified=False,
        sequence=a['p']['sequence'],inputGeometryCheckedPairs=pairs,
        nativePrivateAaPairs=[a['c']['privateAaPairs'],b['c']['privateAaPairs']],repeatability=repeatability,primary=a['primary'],points=2336,regions=regions,
        spatialAndEnergyPassed=not descriptive and not differences and all(v['mseGatePassed'] and v['energyGatePassed'] for v in regions.values()),
        temporalPassed=all(v['temporalGatePassed'] for v in regions.values()),
        limits=['128 correlated frames, fixed2336 pixel support; not a whole-image or asymptotic fixed-bias claim.',
            'Independent exact Lambert reference, not OpenPBR or Lumen parity. Reference uncertainty only.',
            'Same production ray/history budgets; no motion or GPU-performance acceptance.'])
    return report,a,b,ref


def plot(path,a,b,ref,qualified=False):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    fig,axs=plt.subplots(4,4,figsize=(13,8),layout='constrained')
    for row,region in enumerate(ROIS):
        ids=[i for i,q in enumerate(a['points']) if q['region']==region]
        pts=[a['points'][i] for i in ids];xs=sorted({p['x'] for p in pts});ys=sorted({p['y'] for p in pts})
        v=[a['rgb'][:,ids].mean(0)@LUMA,b['rgb'][:,ids].mean(0)@LUMA,ref[ids]@LUMA]
        for col,array in enumerate([v[0],v[1],v[0]-v[2],v[1]-v[2]]):
            grid=np.full((len(ys),len(xs)),np.nan)
            for p,x in zip(pts,array):grid[ys.index(p['y']),xs.index(p['x'])]=x
            limit=max(np.max(abs(v[0]-v[2])),np.max(abs(v[1]-v[2]))) if col>=2 else max(v[0].max(),v[1].max())
            im=axs[row,col].imshow(grid,origin='lower',aspect='auto',cmap='coolwarm' if col>=2 else 'magma',vmin=-limit if col>=2 else 0,vmax=limit)
            axs[row,col].set_title(region+' / '+('baseline mean','candidate mean','baseline-reference','candidate-reference')[col],fontsize=9)
            axs[row,col].set_xticks([]);axs[row,col].set_yticks([]);fig.colorbar(im,ax=axs[row,col],shrink=.65)
    status='Matched inputs and same-code repeats; see spatial and energy gates in report' if qualified else 'EXPLORATORY ONLY: no acceptance or causal claim'
    fig.suptitle('Final GI: 128-frame mean and spatial error, linear E/pi luminance\n4-pixel sampled ROIs; independent Lambert reference\n'+status,fontsize=11)
    fig.savefig(path,dpi=160);plt.close(fig)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('baseline',type=Path);p.add_argument('candidate',type=Path);p.add_argument('reference',type=Path);p.add_argument('--output',type=Path,required=True);p.add_argument('--plot',type=Path)
    p.add_argument('--descriptive',action='store_true',help='Retain failed qualification and show observed errors; never eligible for acceptance')
    p.add_argument('--baseline-repeat',type=Path);p.add_argument('--candidate-repeat',type=Path)
    args=p.parse_args();assert not args.output.exists()
    report,a,b,ref=analyze(args.baseline.resolve(),args.candidate.resolve(),args.reference.resolve(),args.descriptive,
        args.baseline_repeat.resolve() if args.baseline_repeat else None,args.candidate_repeat.resolve() if args.candidate_repeat else None)
    args.output.write_text(json.dumps(report,indent=2)+'\n')
    if args.plot:plot(args.plot,a,b,ref,report['acceptanceEligible'])
    print(json.dumps({k:v for k,v in report.items() if k!='regions'},indent=2))
    print(json.dumps({n:dict(energy=q['meanErrorPercent'],mseDelta=q['meanMapMse']['deltaPercentOfBaseline'],fixedRms=q['fixedNonuniformErrorRmsPercentOfReference'],variance=q['varianceChangePercent'],passed=q['mseGatePassed'] and q['energyGatePassed']) for n,q in report['regions'].items()},indent=2))
