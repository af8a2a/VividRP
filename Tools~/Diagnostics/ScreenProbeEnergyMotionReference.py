"""Independent Lambert reference on sampled newly exposed moving receivers."""
import os
for _name in ('OPENBLAS_NUM_THREADS','MKL_NUM_THREADS','OMP_NUM_THREADS'):
    os.environ[_name]='1'

import argparse
from concurrent.futures import ProcessPoolExecutor,as_completed
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_replay
from AnalyzeScreenProbeColorHistoryMotion import exposed_mask
from CornellLambertReference import Scene
from CornellPixelOracle import initialize,solve
from TemporalSamplingExperiment import read,LUMA


def boundary_receiver(scene,position,normal):
    position=np.asarray(position,float);normal=np.asarray(normal,float);normal/=np.linalg.norm(normal)
    distance,hit,face=scene.intersect((position+4e-5*normal)[None],-normal[None],face_forward=False)
    assert hit[0]>=0 and np.dot(normal,face[0])>=.9999,'Moving receiver does not match an analytic face'
    on_face=position+(4e-5-distance[0])*normal
    correction=float(np.dot(on_face-position,face[0]));assert abs(correction)<=.00002,'Moving receiver surface correction exceeds20micrometers'
    projected=position+correction*face[0]
    return projected,normal,int(hit[0]),abs(correction)


def prepare(root):
    c,p,_,rows,_,_=qualify_replay(root);assert p['motion'] and p['consumedEnergyMoments']
    fixture_path=root/'live-fixture.json';fixture=json.loads(fixture_path.read_text())
    record=[r for r in c['records'] if 'oracleFixturePath' in r];assert len(record)==1
    assert hashlib.sha256(fixture_path.read_bytes()).hexdigest()==record[0]['fixtureSha256']
    scene=Scene(fixture,'black');targets=json.loads((root/'targets.json').read_text())
    frames=range(p['warmup'],p['warmup']+p['measuredFrames']);states={r['frame']:r for r in c['records'] if 'state' in r}
    vp=np.array([states[f]['vp'] for f in frames]).reshape(128,4,4)
    geometry=np.stack([read(rows[f,'geometry-edge','baseline'])[...,0] for f in frames])
    current=np.stack([read(rows[f,'resolved-edge','source']) for f in frames])
    mask=exposed_mask(geometry,vp,p['motionRegion'],current[...,3]);assert mask.any()
    points=[];values={'baseline':[],'candidate':[]};maximum=0.
    for k,f in enumerate(frames):
        g,n=(read(rows[f,'oracle-'+tag,'source'])[0] for tag in ('geometry','normal'))
        finals={side:read(rows[f,'oracle-final',side])[0] for side in values}
        for index,target in enumerate(targets):
            if target['region']!='edge' or not mask[k,target['y']-414,target['x']-880]:continue
            assert g[index,3]>=0 and n[index,3]==1
            position,normal,hit,error=boundary_receiver(scene,g[index,:3],n[index,:3]);maximum=max(maximum,error)
            points.append(dict(index=len(points),frame=f,x=target['x'],y=target['y'],object=scene.items[hit]['name'],
                position=position.tolist(),normal=normal.tolist()))
            for side in values:values[side].append((finals[side][index,:3]*finals[side][index,3]).tolist())
    assert points
    return fixture,points,values,dict(fullExposedSamples=int(mask.sum()),fullExposedFrames=int(mask.any((1,2)).sum()),
        sampledExposedOccurrences=len(points),maximumSurfaceCorrectionMeters=maximum)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--samples',type=int,default=16384);p.add_argument('--workers',type=int,choices=(1,2,3,4),default=3)
    a=p.parse_args();root=a.capture.resolve();fixture,points,values,qualification=prepare(root)
    out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    inputs={name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in ('capture.json','protocol.json','live-fixture.json','targets.json')}
    scripts={str(q):hashlib.sha256(q.read_bytes()).hexdigest() for q in (Path(__file__).resolve(),Path(__file__).with_name('CornellLambertReference.py'),Path(__file__).with_name('CornellPixelOracle.py'))}
    protocol=dict(schema='screen-probe-motion-oracle.v1',complete=False,quantity='Lambert E/pi, linear RGB',samples=a.samples,bounces=9,
        seed=80261117,qualification=qualification,inputs=inputs,scripts=scripts,
        gates=dict(physicalP99IncreasePercent=5,physicalMaxIncreasePercent=5),
        note='Fixed4pixel grid intersected with the existing clear-disocclusion mask; no error-based target selection. Not full-image or sustained-motion quality acceptance.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n');(out/'points.json').write_text(json.dumps(points,indent=2)+'\n')
    records=[]
    with (out/'reference.jsonl').open('x') as stream:
        with ProcessPoolExecutor(max_workers=a.workers,initializer=initialize,initargs=(fixture,)) as pool:
            jobs=[pool.submit(solve,q,a.samples,9,protocol['seed']) for q in points]
            for job in as_completed(jobs):
                r=job.result();records.append(r);stream.write(json.dumps(r)+'\n');stream.flush()
    for name,sha in inputs.items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==sha
    for name,sha in scripts.items():assert hashlib.sha256(Path(name).read_bytes()).hexdigest()==sha
    records.sort(key=lambda q:q['index']);assert len(records)==len(points)
    ref=np.array([q['estimate']['meanRgb'] for q in records])@LUMA
    covariance=np.array([q['estimate']['iidCovarianceMeanRgb'] for q in records]);variance=np.maximum(np.einsum('i,nij,j->n',LUMA,covariance,LUMA),0)
    errors={side:abs(np.array(v)@LUMA-ref) for side,v in values.items()}
    summary={side:dict(p99=float(np.quantile(e,.99)),maximum=float(e.max()),meanAbsolute=float(e.mean()),meanSquared=float((e*e).mean())) for side,e in errors.items()}
    p99=100*(summary['candidate']['p99']/summary['baseline']['p99']-1);maximum=100*(summary['candidate']['maximum']/summary['baseline']['maximum']-1)
    protocol.update(complete=True,referenceSha256=hashlib.sha256((out/'reference.jsonl').read_bytes()).hexdigest())
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n')
    report=dict(schema='screen-probe-motion-oracle-analysis.v1',qualified=True,qualification=qualification,arms=summary,
        p99ChangePercent=p99,maxChangePercent=maximum,physicalP99GatePassed=p99<=5,physicalMaxGatePassed=maximum<=5,
        medianReferenceYStandardError=float(np.median(np.sqrt(variance))),productionQualified=False,
        limits=['Sampled newly exposed receivers only, not all3894 pixel-frame occurrences or the full moving image.',
            'P99/max are finite sampled estimates with nonzero oracle error; no full probabilistic bound.',
            'Exact Lambert geometry differs from SDF/cache approximations; not OpenPBR/Unreal parity.'])
    (out/'analysis.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2),flush=True)


if __name__=='__main__':main()
