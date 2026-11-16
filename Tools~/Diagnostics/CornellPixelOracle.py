"""Independent many-pixel Lambert reference with actual GPU receiver qualification."""
import os
for _name in ('OPENBLAS_NUM_THREADS','MKL_NUM_THREADS','OMP_NUM_THREADS'):
    os.environ[_name]='1'

import argparse
from concurrent.futures import ProcessPoolExecutor,as_completed
import hashlib
import json
from pathlib import Path
import time

import numpy as np

from CornellLambertReference import Scene,estimate
from TemporalSamplingExperiment import read
from AnalyzeScreenProbeQuadratureHistory import qualify_replay


def match_receivers(scene,camera,targets,geometry,normals,maximum_shift=.00002,minimum_cosine=.9999):
    """Match the primary face, then remove only raster depth's normal error.

    The target is the actual GPU shading location. Preserve its tangential
    coordinates instead of moving it along an ideal camera ray to the plane.
    The latter amplifies depth error at grazing incidence. No transport value
    participates in this geometric correspondence.
    """
    scale=np.tan(np.radians(camera['verticalFov'])*.5)
    direction=np.array([[(q['x']+.5)/1920*2-1,(q['y']+.5)/1080*2-1,1] for q in targets],float)
    direction[:,0]*=scale*camera['aspect'];direction[:,1]*=scale;direction/=np.linalg.norm(direction,axis=1)[:,None]
    origin=np.broadcast_to(camera['position'],direction.shape);distance,hit,normal=scene.intersect(origin,direction)
    assert (hit>=0).all(),'Analytic primary rays miss the fixture'
    ideal=origin+distance[:,None]*direction
    g,n=np.asarray(geometry,float),np.asarray(normals,float)
    assert g.shape==n.shape==(len(targets),4) and np.isfinite(g).all() and np.isfinite(n).all()
    assert (g[:,3]>=0).all() and (n[:,3]==1).all(),'Invalid actual target receiver'
    signed_shift=np.sum((g[:,:3]-ideal)*normal,axis=1)
    position=g[:,:3]-signed_shift[:,None]*normal
    maximum=float(np.linalg.norm(position-g[:,:3],axis=1).max())
    cosine=float(np.sum(n[:,:3]*normal,axis=1).min())
    assert maximum<=maximum_shift,'GPU receiver is off the analytic surface'
    assert cosine>=minimum_cosine,'GPU/analytic normals disagree'
    # Match within the actual pixel footprint and retain the same closest face.
    footprint=distance*scale/1080
    assert (np.linalg.norm(position-ideal,axis=1)<=footprint).all(),'Receiver leaves the primary pixel footprint'
    probe_distance,probe_hit,probe_normal=scene.intersect(position+1e-6*normal,-normal)
    assert np.array_equal(probe_hit,hit) and np.all(abs(probe_distance-1e-6)<1e-8),'Projected receiver leaves the primary face'
    assert np.all(np.sum(probe_normal*normal,axis=1)>.999999)
    return position,n[:,:3],hit,dict(maximumSurfaceCorrectionMeters=maximum,minimumPrimaryNormalCosine=cosine,
        maximumIdealRayPositionErrorMeters=float(abs(g[:,:3]-ideal).max()),
        maximumPixelFootprintFraction=float((np.linalg.norm(position-ideal,axis=1)/footprint).max()))


def prepare(capture_root):
    c,p,_,_,_,_=qualify_replay(capture_root)
    assert c['complete'] and not c['errors'] and p['pixelOracle']
    fixture_path=capture_root/'live-fixture.json';raw=fixture_path.read_bytes();fixture=json.loads(raw)
    record=[r for r in c['records'] if 'oracleFixturePath' in r];assert len(record)==1
    assert Path(record[0]['oracleFixturePath']).resolve()==fixture_path.resolve() and hashlib.sha256(raw).hexdigest()==record[0]['fixtureSha256']
    assert len(fixture['geometry'])==8 and all(q['cullMode']==2 and np.max(np.abs(q['meshCenter']))<1e-8 for q in fixture['geometry'])
    camera=fixture['camera'];assert np.max(np.abs(camera['euler']))<1e-8
    targets=json.loads((capture_root/'targets.json').read_text());assert len(targets)==p['oracleTargetCount']==2336
    assert len({(q['x'],q['y']) for q in targets})==len(targets)
    assert all(not(q['x']%16==8 and q['y']%16==8) for q in targets)
    scene=Scene(fixture,'black')
    rows={(r['frame'],r['tag'],r['side']):r for r in c['records'] if 'tag' in r and 'side' in r}
    qualification=None;first_geometry=None;gpu_normal=None
    for f in range(p['warmup'],p['warmup']+p['measuredFrames']):
        g,n=(read(rows[f,'oracle-'+tag,'source'])[0].astype(float) for tag in ('geometry','normal'))
        if first_geometry is None:
            position,gpu_normal,hit,qualification=match_receivers(scene,camera,targets,g,n,
                p['gates']['analyticPrimaryPositionMaxMeters'],p['gates']['analyticPrimaryNormalMinCosine'])
            first_geometry=g
        else:
            assert np.array_equal(first_geometry,g) and np.array_equal(gpu_normal,n[:,:3]),'Receiver geometry changed during the static capture'
    points=[dict(index=i,**q,object=scene.items[hit[i]]['name'],position=position[i].tolist(),normal=gpu_normal[i].tolist()) for i,q in enumerate(targets)]
    qualification.update(fixtureSha256=hashlib.sha256(raw).hexdigest(),targetCount=len(points),
        method='Orthogonal primary-face projection of actual GPU receiver; preserve tangential position',
        note='20micrometer surface-correction bound; exact static geometry across all measured frames. An earlier ideal-ray20micrometer component gate failed at grazing incidence; this correspondence changes the target definition, not the bound. Actual decoded shading normals; no4cm SDF bias or transport fitting.')
    return fixture,points,qualification


_scene=None
def initialize(fixture):
    global _scene
    _scene=Scene(fixture,'black')


def solve(point,samples,bounces,seed):
    result=estimate(_scene,point['position'],point['normal'],samples,bounces,seed+point['index'])
    return dict(**point,estimate=result)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('capture',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--samples',type=int,default=16384);parser.add_argument('--bounces',type=int,default=9)
    parser.add_argument('--seed',type=int,default=20261116);parser.add_argument('--workers',type=int,choices=(1,2,3,4),default=3)
    args=parser.parse_args();assert args.samples>=2 and 0<=args.bounces<=32
    fixture,points,qualification=prepare(args.capture.resolve());args.output.mkdir(parents=True,exist_ok=False)
    (args.output/'points.json').write_text(json.dumps(points,indent=2)+'\n')
    inputs={name:hashlib.sha256((args.capture/name).read_bytes()).hexdigest() for name in ('capture.json','protocol.json','targets.json','live-fixture.json')}
    scripts={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (Path(__file__).resolve(),Path(__file__).with_name('CornellLambertReference.py'))}
    metadata=dict(schema='cornell-pixel-oracle.v1',complete=False,capture=str(args.capture.resolve()),samples=args.samples,bounces=args.bounces,
        seed=args.seed,workers=args.workers,primaryQualification=qualification,inputs=inputs,scripts=scripts,
        quantity='Lambert incident E/pi, linear RGB',backfaceDiffuse='black',sourceIndependent=True,
        note='Exact live box/quad fixture; NEE at every bounce. No SDF, Surface Cache, SH, fitted gain or production ray-count change. Not OpenPBR/Unreal parity.')
    (args.output/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
    begin=time.monotonic();results=[]
    with (args.output/'reference.jsonl').open('x') as stream:
        with ProcessPoolExecutor(max_workers=args.workers,initializer=initialize,initargs=(fixture,)) as pool:
            jobs=[pool.submit(solve,p,args.samples,args.bounces,args.seed) for p in points]
            for future in as_completed(jobs):
                value=future.result();results.append(value);stream.write(json.dumps(value)+'\n');stream.flush()
                if len(results)%32==0 or len(results)==len(points):
                    progress=dict(completed=len(results),total=len(points),elapsedSeconds=time.monotonic()-begin)
                    (args.output/'progress.json').write_text(json.dumps(progress)+'\n');print(json.dumps(progress),flush=True)
    for name,digest in inputs.items():assert hashlib.sha256((args.capture/name).read_bytes()).hexdigest()==digest
    for path,digest in scripts.items():assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
    metadata.update(complete=True,points=len(results),elapsedSeconds=time.monotonic()-begin,referenceSha256=hashlib.sha256((args.output/'reference.jsonl').read_bytes()).hexdigest())
    (args.output/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n');print(json.dumps(dict(complete=True,points=len(results),seconds=metadata['elapsedSeconds'])),flush=True)


if __name__=='__main__':main()
