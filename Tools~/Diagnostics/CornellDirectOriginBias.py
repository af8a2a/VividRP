"""Independent direct-emitter sensitivity to moving the GI query off the floor.

This is a geometric model experiment, not a renderer-bias fix or a new reference.
"""
import argparse
from concurrent.futures import ProcessPoolExecutor
import hashlib
import json
from pathlib import Path

import numpy as np

from CornellLambertReference import Scene
from TemporalSamplingExperiment import LUMA


OFFSETS=(0.,.005,.01,.04)
_scene=None


def paired_direct(scene,position,normal,samples,seed,offsets=OFFSETS):
    normal=np.array(normal,dtype=float,copy=True);normal/=np.linalg.norm(normal)
    uv=np.random.default_rng(seed).random((samples,2))
    p=np.broadcast_to(position,(samples,3));n=np.broadcast_to(normal,p.shape)
    values=[scene.direct(p+offset*n,n,uv) for offset in offsets]
    rows=[]
    for offset,value in zip(offsets,values):
        delta=value-values[0]
        rows.append(dict(offsetMeters=offset,meanRgb=value.mean(0).tolist(),
            pairedDeltaRgb=delta.mean(0).tolist(),pairedDeltaCovarianceMeanRgb=(np.cov(delta,rowvar=False,ddof=1)/samples).tolist()))
    return rows


def initialize(fixture):
    global _scene
    _scene=Scene(fixture,'black')


def job(args):
    point,samples,seed=args
    return dict(index=point['index'],object=point['object'],position=point['position'],normal=point['normal'],samples=samples,seed=seed,
        estimates=paired_direct(_scene,point['position'],point['normal'],samples,seed))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('fixture',type=Path);p.add_argument('reference',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--samples',type=int,default=16384);p.add_argument('--workers',type=int,default=8);p.add_argument('--seed',type=int,default=100261117)
    a=p.parse_args();a.output.mkdir(parents=True,exist_ok=False)
    metadata=json.loads((a.reference/'metadata.json').read_text());assert metadata['complete']
    fixture=json.loads(a.fixture.read_text());assert digest(a.fixture)==metadata['primaryQualification']['fixtureSha256']
    inputs={str(path.resolve()):digest(path) for path in (a.fixture,a.reference/'metadata.json',a.reference/'points.json',a.reference/'reference.jsonl')}
    assert digest(a.reference/'reference.jsonl')==metadata['referenceSha256']
    scripts={str(path.resolve()):digest(path) for path in (Path(__file__),Path(__file__).with_name('CornellLambertReference.py'))}
    points=json.loads((a.reference/'points.json').read_text());points=[q for q in points if q['region']=='floor']
    assert len(points)==384 and {q['object'] for q in points}=={'Floor','Short White Block','Tall White Block'} and a.samples>=16384
    ref={q['index']:q for q in map(json.loads,(a.reference/'reference.jsonl').read_text().splitlines())}
    for q in points:assert all(ref[q['index']][k]==v for k,v in q.items())
    protocol=dict(schema='cornell-direct-origin-bias.v1',complete=False,points=len(points),samples=a.samples,offsetsMeters=OFFSETS,
        sourceInputs=inputs,scripts=scripts,quantity='Paired exact-geometry direct-emitter E/pi; no SDF/cache/Gather',
        limits=['All384 pre-existing floor-ROI targets, including blocks; selection is not based on renderer error. Actual Floor subset290 points is reported separately.',
            'Moving the query also changes visibility; does not change light/material energy.',
            'Direct-emitter sensitivity only. It does not prove an engine error is caused by bias or model Surface Cache bias.'])
    (a.output/'metadata.json').write_text(json.dumps(protocol,indent=2)+'\n')
    with ProcessPoolExecutor(max_workers=a.workers,initializer=initialize,initargs=(fixture,)) as pool:
        result=list(pool.map(job,((q,a.samples,a.seed+q['index']) for q in points)))
    summary=[]
    for group in ('floor-roi','Floor','Short White Block','Tall White Block'):
        selected=[q for q in result if group=='floor-roi' or q['object']==group]
        baseline=np.mean([np.array(ref[q['index']]['estimate']['meanRgb'])@LUMA for q in selected])
        reference_direct=np.mean([np.array(ref[q['index']]['estimate']['meanRgbByOrder'][0])@LUMA for q in selected])
        for i,offset in enumerate(OFFSETS):
            delta=np.array([q['estimates'][i]['pairedDeltaRgb'] for q in selected])
            cov=np.array([q['estimates'][i]['pairedDeltaCovarianceMeanRgb'] for q in selected])
            mean=float((delta@LUMA).mean());sem=float(np.sqrt(max(0.,np.einsum('i,nij,j->',LUMA,cov,LUMA)))/len(selected))
            summary.append(dict(group=group,points=len(selected),offsetMeters=offset,pairedDirectDeltaY=mean,pairedDirectDeltaMeanStandardError=sem,
                percentOfReferenceTotal=100*mean/baseline,referenceTotalMeanY=float(baseline),referenceDirectMeanY=float(reference_direct)))
    for path,sha in {**inputs,**scripts}.items():assert digest(Path(path))==sha
    payload=''.join(json.dumps(q)+'\n' for q in result);(a.output/'points.jsonl').write_text(payload)
    protocol.update(complete=True,summary=summary,resultSha256=digest(a.output/'points.jsonl'))
    (a.output/'metadata.json').write_text(json.dumps(protocol,indent=2)+'\n');print(json.dumps(summary,indent=2))
