"""Validate planar-rim coverage, material sides and actual returned radiance."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from CornellLambertReference import Scene,estimate
from TemporalSamplingExperiment import LUMA


def analyze(root,output):
    protocol=json.loads((root/'protocol.json').read_text())
    rays=np.fromfile(root/'rays.bin','<f4').reshape(-1,3,4)
    assert hashlib.sha256((root/'rays.bin').read_bytes()).hexdigest()==protocol['raysSha256']
    fixture=json.loads((root/'baseline-a-v3/live-fixture.json').read_text());scene=Scene(fixture,'black')
    result=dict(schema='screen-probe-card-endpoint.v1',qualified=True,productionQualified=False,rayCount=len(rays),origins={})
    for label in ('1cm','4cm'):
        old=np.fromfile(root/f'results-{label}.bin','<f4').reshape(-1,8,4)
        new=np.fromfile(root/'candidate-lighting-v3'/f'results-{label}.bin','<f4').reshape(-1,10,4)
        assert np.isfinite(old).all() and np.isfinite(new).all()
        prior=old[:,4,3]>0;added=~prior&(new[:,4,3]>0)
        assert np.array_equal(old[prior],new[prior,:8]),'An already accepted sample changed'
        assert np.array_equal(old[:,7],new[:,7]),'SDF first hit or incomplete state changed'
        distance,hit,normal=scene.intersect(new[:,0,:3].astype(float),rays[:,2,:3].astype(float))
        assert (hit[added]==scene.light).all(),'Recovered sample is not the exact first-hit emitter'
        front=added&(new[:,8,3]>0);back=added&~front
        assert (new[back,9,:3]==0).all() and (new[added,9,3]==1).all()
        assert (new[front,9,:3]>=new[front,8,:3]).all()
        points=[]
        for index in np.where(added)[0]:
            point=new[index,1,:3].astype(float);point[1]=scene.centers[scene.light,1]
            record=dict(ray=int(index),position=point.tolist(),visible=bool(front[index]),
                gpuRgb=new[index,9,:3].astype(float).tolist(),emissionRgb=new[index,8,:3].astype(float).tolist())
            if front[index]:
                ref=estimate(scene,point,scene.light_normal,131072,8,20261116+int(index))
                albedo=scene.albedo[scene.light];mean=scene.emission[scene.light]+albedo*np.array(ref['meanRgb'])
                covariance=np.array(ref['iidCovarianceMeanRgb'])*albedo[:,None]*albedo[None,:]
                record.update(referenceRgb=mean.tolist(),referenceYIidStandardError=float(np.sqrt(LUMA@covariance@LUMA)),
                    returnedYErrorPercent=100*float((new[index,9,:3]-mean)@LUMA)/(mean@LUMA),reference=ref)
            else:record.update(referenceRgb=[0,0,0],returnedYErrorPercent=0)
            points.append(record);print(json.dumps(dict(origin=label,ray=int(index),visible=record['visible'],error=record['returnedYErrorPercent'])),flush=True)
        result['origins'][label]=dict(recovered=int(added.sum()),front=int(front.sum()),back=int(back.sum()),
            unchangedAccepted=int(prior.sum()),addedFalseIntersections=0,points=points)
    result['limits']=['Archived field-diagonal march policy for endpoint diagnosis, not the192-step production quality capture.',
        'Same-direction raw SDF diagnostic excludes the native screen-hit path.',
        'Eight-bounce exact Lambert reference at recovered emitter points; no full-scene bias-removal claim.',
        'Previously accepted inflated-silhouette hits remain unchanged; these need separate traversal analysis.']
    output.write_text(json.dumps(result,indent=2)+'\n')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();assert not a.output.exists();analyze(a.root,a.output)
