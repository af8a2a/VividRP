"""Qualify same-frame reconstruction A/B and compare final means to the Cornell oracle."""
import argparse
import json
from pathlib import Path
import numpy as np

from AnalyzeScreenProbeCardQuality import load, digest, reference, comparison, ROIS, plot
from CornellPixelOracle import Scene, match_receivers
from TemporalSamplingExperiment import read, LUMA


def analyze(root, refroot):
    c, p = load(root/'capture.json'), load(root/'protocol.json')
    assert c['complete'] and not c['errors'] and c['epochReset'] and c['privateInitialized']
    assert p['schema'] == 'screen-probe-directional-irradiance.v1'
    assert load(root/'source-verification.json')['unchanged']
    assert (c['warmup'], c['measuredFrames'], c['spacing'], c['rays']) == (1024, 128, 16, 8)
    for row in c['records']:
        if row.get('path'):
            path = Path(row['path']).resolve()
            assert path.is_relative_to(root.resolve()) and digest(path) == row['sha256']
    rows = {(r['frame'],r['tag'],r['side']):r for r in c['records'] if 'tag' in r and 'side' in r}
    frames = [0, 1, 2, 1023, *range(1024, 1152)]
    aa = [r for r in c['records'] if r.get('componentAa')]
    assert c['componentAaPairs'] == len(frames) and c['privateAaPairs'] == len(frames)*25
    for f in frames:
        pair = [r for r in aa if r['frame'] == f]
        assert len(pair) == 2 and len({r['sha256'] for r in pair}) == 1
        for roi in (*ROIS, 'full'):
            for role in ('temporal', 'history', 'moments', 'geometry', 'final'):
                a,b = (rows[f,role+'-'+roi,side] for side in ('native','candidate'))
                assert all(a[k] == b[k] for k in ('sha256','width','height','format'))
    fixture, targets = load(root/'live-fixture.json'), load(root/'targets.json')
    scene = Scene(fixture, 'black')
    g,n = (read(rows[1024,'oracle-'+tag,'source'])[0].astype(float) for tag in ('geometry','normal'))
    position,normal,hit,primary = match_receivers(scene,fixture['camera'],targets,g,n)
    points = [dict(index=i,**q,object=scene.items[hit[i]]['name'],position=position[i].tolist(),normal=normal[i].tolist()) for i,q in enumerate(targets)]
    for f in range(1024,1152):
        assert np.array_equal(g, read(rows[f,'oracle-geometry','source'])[0])
        assert np.array_equal(n, read(rows[f,'oracle-normal','source'])[0])
    values = {}
    for side in ('baseline','candidate'):
        v = np.stack([read(rows[f,'oracle-final',side])[0].astype(float) for f in range(1024,1152)])
        assert v.shape == (128,2336,4) and np.isfinite(v).all() and (v[...,3]>=0).all()
        values[side] = dict(root=str(root), points=points, rgb=v[...,:3]*v[...,3:4])
    ref,cov = reference(refroot, values['baseline'])
    groups = {r:np.array([q['region']==r for q in points]) for r in ROIS}
    groups.update({'object:'+name:np.array([q['object']==name for q in points]) for name in sorted({q['object'] for q in points})})
    regions = {}
    for name,mask in groups.items():
        ar,br = (values[s]['rgb'][:,mask] for s in ('baseline','candidate'))
        result = comparison(ar@LUMA,br@LUMA,ref[mask]@LUMA,np.maximum(np.einsum('i,nij,j->n',LUMA,cov[mask],LUMA),0))
        result['rgb'] = {n:comparison(ar[...,i],br[...,i],ref[mask,i],cov[mask,i,i]) for i,n in enumerate(('r','g','b'))}
        result['fixedNonuniformErrorRmsPercentOfReference'] = {n:100*float(np.std((v.mean(0)-ref[mask])@LUMA))/result['referenceMean'] for n,v in (('baseline',ar),('candidate',br))}
        result['varianceChangePercent'] = 100*(result['candidate']['temporal_variance']/max(result['baseline']['temporal_variance'],1e-20)-1)
        result['mseGatePassed'] = all(q['meanMapMse']['deltaPlusThreeSigmaPercentOfBaseline']<=1 for q in (result,*result['rgb'].values()))
        result['energyGatePassed'] = all(q['addedAbsoluteMeanErrorUpperBoundPercentOfReference']<=.2 for q in (result,*result['rgb'].values()))
        regions[name] = result
    report = dict(schema='screen-probe-directional-irradiance-analysis.v1',sameFramePaired=True,
        nativeResolveAaPairs=c['componentAaPairs'],nativeHistoryAaPairs=c['privateAaPairs'],
        sequence=p['sequence'],points=len(points),regions=regions,
        spatialAndEnergyPassed=all(q['mseGatePassed'] and q['energyGatePassed'] for q in regions.values()),
        productionQualified=False,
        limits=['Same-frame reconstruction contrast; radiance/trace history shared, pixel histories independent.',
                'No cross-run byte-exact A/A claim; no motion or GPU timing acceptance.',
                '128 correlated frames and 2336 sampled pixels; exact Lambert reference, not OpenPBR/Lumen parity.'])
    return report,values['baseline'],values['candidate'],ref


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture',type=Path);parser.add_argument('reference',type=Path)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--plot',type=Path)
    args=parser.parse_args()
    report,a,b,ref=analyze(args.capture.resolve(),args.reference.resolve())
    args.output.write_text(json.dumps(report,indent=2)+'\n')
    if args.plot:plot(args.plot,a,b,ref,qualified=False)
    print(json.dumps(dict(output=str(args.output),passed=report['spatialAndEnergyPassed'],
        regions={k:dict(mse=v['meanMapMse'],fixed=v['fixedNonuniformErrorRmsPercentOfReference'],
            energyBound=v['addedAbsoluteMeanErrorUpperBoundPercentOfReference']) for k,v in report['regions'].items() if k in ROIS})))
