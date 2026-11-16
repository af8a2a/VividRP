"""Compare archived native ray identities with the exact Cornell box/quad fixture.

Reads verified ZIP members in memory; never restores or edits archived captures.
This is a first-hit experiment, not an independent full radiance reference.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import zipfile

import numpy as np

from AnalyzeScreenProbeOriginReplay import analyze as qualify_origin
from CornellLambertReference import Scene,EPS
from GiEvidenceRetention import no_link
from ProbeAnchorCalibration import normal_from_packed
from TemporalSamplingExperiment import LUMA


STATE_SOURCE=Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl'


def ray_states():
    source=STATE_SOURCE.read_text(encoding='utf-8-sig')
    return {name:int(value) for name,value in re.findall(r'static const uint (Ray\w+) = (\d+)u;',source)}


STATES=ray_states()


def digest(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()


class ArchivedRays:
    def __init__(self,root):
        self.root=root.resolve();no_link(self.root)
        self.capture=json.loads((root/'capture.json').read_text())
        self.protocol=json.loads((root/'protocol.json').read_text())
        assert self.capture['complete'] and not self.capture['errors']
        assert self.protocol['originStepPolicy']=='field-diagonal-all-origins'
        rows={(r['frame'],r['tag'],r['side']):r for r in self.capture['records']
            if r.get('tag','').startswith('origin-') and 'side' in r}
        self.qualification=qualify_origin(self.capture,self.protocol,rows);self.rows=rows
        assert all(self.qualification['countersByLimit'][str(k)][7]==0 for k in (.04,.01)),'First-hit identity comparison requires complete traces'
        verification=json.loads((root/'source-verification.json').read_text())
        assert verification['unchanged'] and verification['changedSources']==[]
        snapshot=json.loads((root/'source-before-capture.json').read_text())
        state_sources=[r for r in snapshot['files'] if r['path']=='Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl']
        assert len(state_sources)==1 and digest(STATE_SOURCE)==state_sources[0]['sha256'],'Captured ray-state ABI differs from the current decoder'
        journal=json.loads((root/'raw-retention.json').read_text())
        assert journal['complete'] and journal['roundTripVerified']
        archive=root/journal['archive'];no_link(archive)
        assert digest(archive)==journal['archiveSha256']
        self.zip=zipfile.ZipFile(archive)
        try:
            self.entries={r['path']:r for r in journal['files']}
            assert len(self.entries)==len(journal['files'])==len(self.zip.namelist())
            assert set(self.entries)==set(self.zip.namelist())
        except BaseException:
            self.zip.close();raise
        self.consumed={};self.archive_sha=journal['archiveSha256']

    def close(self):self.zip.close()

    def read(self,frame,tag,side):
        return self.read_row(self.rows[frame,tag,side])

    def read_row(self,row):
        path=Path(row['path']).resolve();assert path.is_relative_to(self.root)
        name=path.relative_to(self.root).as_posix();entry=self.entries[name]
        data=self.zip.read(name)
        assert len(data)==entry['bytes'] and hashlib.sha256(data).hexdigest()==entry['sha256']==row['sha256']
        dtype={'R32G32B32A32_SFloat':'<f4','R32G32B32A32_UInt':'<u4','R16G16B16A16_SFloat':'<f2'}[row['format']]
        values=np.frombuffer(data,dtype=dtype).reshape(row['height'],row['width'],4)
        assert np.isfinite(values).all();self.consumed[name]=entry['sha256']
        return values


def map_entities(scene,identities):
    """Join a live GPU export only when one whole layer matches the frozen fixture."""
    matches={}
    for instance in identities['instances']:
        matrix=np.array(instance['worldToLocal'],float);candidates=[]
        for i,item in enumerate(scene.items):
            linear=np.diag(1/np.array(item['scale']))@scene.rotations[i].T
            expected=np.column_stack((linear,-linear@scene.centers[i]))
            if np.max(abs(matrix-expected))<1e-5:
                assert instance['albedo'][3]==1 and instance['emission'][3]==item['cullMode']==2,'Fixture material side or validity changed'
                np.testing.assert_allclose(instance['albedo'][:3],scene.albedo[i],rtol=2e-6,atol=2e-7)
                np.testing.assert_allclose(instance['emission'][:3],scene.emission[i],rtol=2e-6,atol=2e-7)
                candidates.append(i)
        if len(candidates)==1:matches.setdefault(instance['layerMask'],[]).append((instance,candidates[0]))
    layers=[v for v in matches.values() if len(v)==len(scene.items) and {i for _,i in v}==set(range(len(scene.items)))]
    assert len(layers)==1,'Live GPU identities do not uniquely match the captured fixture'
    result={tuple(v['entity']):(i,v['index']) for v,i in layers[0]}
    assert len(result)==len(scene.items)
    return result


def project_receivers(scene,geometry,receivers,mapping):
    valid=receivers[...,3]!=0
    assert np.all(geometry[...,3][valid]>=0)
    object_index=np.full(valid.shape,-1,int)
    for entity,(i,instance) in mapping.items():
        mask=valid&np.all(receivers[...,:2]==entity,axis=-1)
        assert np.all(receivers[...,2][mask]==instance),'Archived entity/instance order changed'
        object_index[mask]=i
    assert (object_index[valid]>=0).all(),'Archived receiver entity is absent from the live fixture mapping'
    original=geometry[...,:3][valid].astype(float)
    decoded=normal_from_packed(geometry[...,3][valid]).astype(float)
    hit=object_index[valid];position=original.copy();normal=np.zeros_like(position)
    for i,rotation in enumerate(scene.rotations):
        mask=hit==i
        if not mask.any():continue
        local=(original[mask]-scene.centers[i])@rotation;n=decoded[mask]@rotation
        axis=np.argmax(abs(n),axis=1);sign=np.where(n[np.arange(len(n)),axis]>=0,1.,-1.)
        if i==scene.light:assert np.all(axis==1)
        corrected=local.copy();corrected[np.arange(len(n)),axis]=sign*scene.half[i,axis]
        assert np.all(abs(corrected)<=scene.half[i]+.00002),'Projected receiver leaves its box/quad'
        position[mask]=corrected@rotation.T+scene.centers[i]
        local_normal=np.zeros_like(n);local_normal[np.arange(len(n)),axis]=sign
        normal[mask]=local_normal@rotation.T
    shift=np.linalg.norm(position-original,axis=1)
    assert shift.max()<=.00002,'Full probe receiver is off its actual analytic face'
    assert np.min(np.sum(normal*decoded,axis=1))>=.9999,'Packed receiver normal disagrees with fixture face'
    return valid,hit,position,normal,dict(maximumSurfaceProjectionMeters=float(shift.max()),minimumNormalCosine=float(np.sum(normal*decoded,axis=1).min()))


def decode_entities(words,mapping):
    assert np.all(words==np.floor(words)) and np.all((words>=0)&(words<=65535))
    w=words.astype(np.uint32);low=w[...,0]|(w[...,1]<<16);high=w[...,2]|(w[...,3]<<16)
    result=np.full(low.shape,-1,int)
    for entity,(index,_) in mapping.items():result[(low==entity[0])&(high==entity[1])]=index
    assert np.all((result>=0)|((low==0)&(high==0))),'Unknown archived hit entity'
    return result


def require_same_direction_words(a,b):
    assert a.dtype==b.dtype==np.dtype('<f4') and a.shape==b.shape
    np.testing.assert_array_equal(a.view('<u4'),b.view('<u4'),err_msg='Origins did not use byte-identical ray directions')


def statistics(mask,truth,actual,state,rgb,direction,scene):
    n=int(mask.sum());known=mask&(actual>=0);correct=known&(actual==truth)
    source=state&7;covered=(source==STATES['RayScreenHit'])|(source==STATES['RaySdfHit'])|(source==STATES['RaySkyVisible'])|((state&STATES['RayFarCache'])!=0)
    miss=source==STATES['RayMiss']
    back=mask&(truth==scene.light)&((direction@scene.light_normal)>0)
    front=mask&(truth==scene.light)&((direction@scene.light_normal)<0)
    light_mean=float(scene.emission[scene.light]@LUMA)
    return dict(rays=n,knownHitIdentities=int(known.sum()),correctHitIdentities=int(correct.sum()),
        wrongHitIdentities=int((known&~correct).sum()),unknownHitIdentities=int((mask&~known).sum()),
        correctMisses=int((mask&miss&(truth<0)).sum()),falseMisses=int((mask&miss&(truth>=0)).sum()),
        unknownNonMissIdentities=int((mask&~known&~miss).sum()),
        covered=int((mask&covered).sum()),screen=int((mask&(source==1)).sum()),sdf=int((mask&(source==2)).sum()),
        incomplete=int((mask&((state&STATES['RayTraceIncomplete'])!=0)).sum()),missingSurface=int((mask&(source==STATES['RayMissingSurface'])).sum()),
        geometricBackfaceRejected=int((mask&(source==STATES['RayBackface'])).sum()),
        meanReturnedY=float((rgb[mask]@LUMA).mean()) if n else None,
        exactEmitterBackRays=int(back.sum()),exactEmitterBackWithNonblackReturn=int((back&np.any(rgb!=0,axis=1)).sum()),
        correctEmitterBackWithNonblackReturn=int((back&correct&np.any(rgb!=0,axis=1)).sum()),
        exactEmitterFrontRays=int(front.sum()),exactEmitterFrontBelowEmission=int((front&((rgb@LUMA)<.99*light_mean)).sum()),
        correctEmitterFrontBelowEmission=int((front&correct&((rgb@LUMA)<.99*light_mean)).sum()),
        actualEmitterWrongGeometricPathWithEmission=int((mask&(actual==scene.light)&~front&((rgb@LUMA)>=.99*light_mean)).sum()))


def decompose_delta(mask,actuals,states,colors,weights):
    delta=((colors[1]-colors[0])@LUMA)*weights
    same=actuals[0]==actuals[1];same_source=(states[0]&7)==(states[1]&7)
    categories=dict(changedIdentity=~same,sameIdentityChangedSource=same&~same_source,sameIdentitySameSource=same&same_source)
    result={name:dict(rays=int((mask&select).sum()),weightedDeltaYPerAllGroupRays=float(delta[mask&select].sum()/mask.sum()))
        for name,select in categories.items()}
    assert sum(q['rays'] for q in result.values())==int(mask.sum())
    assert np.isclose(sum(q['weightedDeltaYPerAllGroupRays'] for q in result.values()),delta[mask].mean())
    return result


def analyze_frame(reader,frame,scene,mapping):
    native_limit=reader.protocol['nativeOriginLimitMeters']
    sides={native_limit:'origin-native',.01 if native_limit==.04 else .04:'origin-10mm' if native_limit==.04 else 'origin-40mm'}
    sources=[r for r in reader.capture['records'] if r.get('frame')==frame and r.get('tag')=='radiosity']
    assert len(sources)==1
    reader.read_row(sources[0])
    state=[r['state'] for r in reader.capture['records'] if r.get('frame')==frame and 'state' in r]
    assert len(state)==1 and (state[0]['order'],state[0]['samples'],state[0]['batch'])==(8,64,32)
    receiver=reader.read(frame,'origin-receivers-full','origin-receivers')
    geometry=reader.read(frame,'origin-geometry-full','origin-native')
    valid,objects,positions,normals,projection=project_receivers(scene,geometry,receiver,mapping)
    ray_valid=np.repeat(valid,8,axis=1);object_rays=np.repeat(objects,8);p=np.repeat(positions,8,axis=0);normal=np.repeat(normals,8,axis=0)
    rays={};directions={};probe_light={};weights={};metadata={}
    for limit,side in sides.items():
        first=reader.read(frame,'origin-first-hit-full',side);raw=reader.read(frame,'origin-ray-diagnostics-full',side)
        direction=first[2::3,:,:3][ray_valid]
        assert np.max(abs(np.linalg.norm(direction,axis=1)-1))<2e-6,'Ray direction is undefined or not normalized'
        directions[limit]=direction
        metadata[limit]=first[1::3][ray_valid]
        rays[limit]=(decode_entities(first[0::3][ray_valid],mapping),np.floor(raw[...,3][ray_valid]).astype(np.uint32),raw[...,:3][ray_valid].astype(float))
        probe=reader.read(frame,'origin-hit-lighting-full',side)[valid].astype(float)
        probe_light[limit]=(probe[:,:3]*probe[:,3:4])@LUMA
        directional=reader.read(frame,'origin-directional-rays-full',side)
        height=valid.shape[0]
        weights[limit]=directional[height:2*height,:,1][ray_valid].astype(float)
        assert np.array_equal(weights[limit].reshape(-1,8).sum(axis=1),np.full(valid.sum(),8.0)),'Directional strata do not cover the hemisphere once'
    require_same_direction_words(directions[.04],directions[.01])
    np.testing.assert_array_equal(weights[.04],weights[.01],err_msg='Origins did not use identical proposal weights')
    direction=directions[.04];distance,truth,truth_normal=scene.intersect(p+EPS*normal,direction,face_forward=False)
    assert np.all(np.isfinite(distance[truth>=0]))
    groups={}
    group_masks={'all':np.ones(len(truth),bool)}
    for i,item in enumerate(scene.items):
        group_masks[item['name']]=object_rays==i
        for axis,name in enumerate(('x','y','z')):
            for sign in (-1,1):
                mask=(object_rays==i)&(normal[:,axis]*sign>.9999)
                if mask.any():group_masks[item['name']+'::'+name+('+' if sign>0 else '-')]=mask
    for name,mask in group_masks.items():
        if not mask.any():continue
        by_limit={str(limit):statistics(mask,truth,*data,direction,scene) for limit,data in rays.items()}
        for limit in rays:
            by_limit[str(limit)]['actualConsumedProbeMeanY']=float(probe_light[limit][mask.reshape(-1,8).all(axis=1)].mean())
            by_limit[str(limit)]['weightedReturnedMeanY']=float(((rays[limit][2]@LUMA)*weights[limit])[mask].mean())
            actual,state,_=rays[limit]
            missing=mask&((state&7)==STATES['RayMissingSurface'])
            reasons=Counter((scene.items[actual[i]]['name'] if actual[i]>=0 else 'unknown',int(metadata[limit][i,2]),bool(truth[i]==actual[i])) for i in np.flatnonzero(missing))
            by_limit[str(limit)]['missingSurfaceDetails']=[dict(object=k[0],rejection=k[1],exactIdentityCorrect=k[2],rays=v) for k,v in sorted(reasons.items())]
        classified=[(rays[v][0]>=0)|((rays[v][1]&7)==STATES['RayMiss']) for v in (.04,.01)]
        known=mask&classified[0]&classified[1]
        old,new=((rays[v][0]==truth)&classified[i] for i,v in enumerate((.04,.01)))
        by_limit['paired']=dict(bothKnown=int(known.sum()),identityImproved=int((known&~old&new).sum()),identityWorsened=int((known&old&~new).sum()),
            meanReturnedDeltaY=float(((rays[.01][2]-rays[.04][2])[mask]@LUMA).mean()),
            actualConsumedProbeDeltaY=by_limit['0.01']['actualConsumedProbeMeanY']-by_limit['0.04']['actualConsumedProbeMeanY'],
            weightedEnergyComponents=decompose_delta(mask,[rays[v][0] for v in (.04,.01)],[rays[v][1] for v in (.04,.01)],
                [rays[v][2] for v in (.04,.01)],weights[.04]))
        groups[name]=by_limit
    return dict(frame=frame,radiositySha256=sources[0]['sha256'],rayDirectionsByteExact=True,proposalWeightsByteExact=True,inverseRelativePdfSumPerProbe=8,probeReceiverProjection=projection,groups=groups,
        exactMisses=int((truth<0).sum()),exactBacksideHits=int(((truth>=0)&(np.sum(direction*truth_normal,axis=1)>0)).sum()))


def analyze(root,identities):
    reader=ArchivedRays(root)
    try:
        fixture_path=root/'live-fixture.json'
        fixture_record=[r for r in reader.capture['records'] if 'oracleFixturePath' in r];assert len(fixture_record)==1
        assert digest(fixture_path)==fixture_record[0]['fixtureSha256']
        fixture=json.loads(fixture_path.read_text())
        assert len(fixture['geometry'])==8 and all(item['cullMode']==2 and np.max(abs(np.array(item['meshCenter'])))<1e-8 for item in fixture['geometry'])
        scene=Scene(fixture,'black');mapping=map_entities(scene,identities)
        reports=[analyze_frame(reader,f,scene,mapping) for f in (reader.protocol['warmup'],reader.protocol['warmup']+reader.protocol['measuredFrames']-1)]
        return dict(capture=str(root),sequence=reader.protocol['sequence'],nativeOriginLimitMeters=reader.protocol['nativeOriginLimitMeters'],
            archiveSha256=reader.archive_sha,checkedArchiveEntries=reader.consumed,originAaPairs=reader.qualification['fullTraceAndCounterAaPairs'],frames=reports)
    finally:reader.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('identities',type=Path);parser.add_argument('output',type=Path);parser.add_argument('captures',type=Path,nargs='+')
    args=parser.parse_args();assert not args.output.exists()
    identities=json.loads(args.identities.read_text());results=[analyze(root.resolve(),identities) for root in args.captures]
    assert len({f['radiositySha256'] for r in results for f in r['frames']})==1,'Frozen lighting source differs between cases'
    report=dict(schema='screen-probe-first-hit-oracle.v1',qualified=True,productionQualified=False,results=results,
        identitiesSha256=digest(args.identities),scripts={str(p.resolve()):digest(p) for p in (Path(__file__),Path(__file__).with_name('CornellLambertReference.py'))},
        limits=['Same actual ray directions within each origin pair; eight endpoint snapshots, not all128frames or final-pixel noise.',
            'Exact box/zero-thickness quad first hit at the projected physical receiver; differs intentionally from thick SDF geometry and shifted query.',
            'Live identity export joined only by complete fixture transform/material match and archived receiver entity/instance/face checks; not an identity snapshot contemporaneous with the original capture.',
            'Unknown Screen Hit entities are reported explicitly, not counted as wrong or missing geometry.',
            'Returned radiance includes indirect bounce light; only front-emitter lower bounds and black-backside conditions have a simple per-ray truth here. No full radiance reference or PDF-weighted GI integral.'])
    args.output.write_text(json.dumps(report,indent=2)+'\n')
    for r in results:
        for f in r['frames']:
            for name in ('Ceiling::y-','Floor::y+','Short White Block'):
                if name in f['groups']:print(json.dumps(dict(sequence=r['sequence'],nativeOrigin=r['nativeOriginLimitMeters'],frame=f['frame'],group=name,values=f['groups'][name])))
