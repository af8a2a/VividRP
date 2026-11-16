"""Validate classified black misses against same-frame native ray/energy data."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeColorHistoryReplay import analyze as qualify
from TemporalSamplingExperiment import read


def analyze(root):
    common=qualify(root);assert common['qualified'] and common['nativeBaselineQualified']
    load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
    capture,protocol=load('capture.json'),load('protocol.json')
    rows={(r['frame'],r['tag'],r.get('side','source')):r for r in capture['records'] if 'tag' in r}
    count=protocol['measuredFrames'];start=protocol['warmup'];gates=protocol['gates']
    changed=flags_total=0;maximum_error=maximum_relative=0.0;means=[];coverages=[]
    for f in range(start,start+count):
        for role in ('geometry','ray-rgb'):
            assert rows[f,'micro-'+role,'baseline']['sha256']==rows[f,'micro-'+role,'corrected']['sha256'],(f,role)
        flags=[]
        for side in ('baseline','corrected'):
            row=rows[f,'micro-flags',side]
            encoded=np.fromfile(row['path'],'<f4');assert np.isfinite(encoded).all()
            flags.append(np.floor(encoded).astype(np.uint32))
        a,b=flags;different=a!=b
        allowed=((a&7)==4)&((a&(8|256|64))==0)
        assert np.all(~different|allowed),(f,'Reclassified an unresolved/accepted ray')
        assert np.array_equal(b[different],(a[different]&np.uint32(0xfffffff8))|7),(f,'Flag mutation')
        changed+=int(different.sum());flags_total+=len(a)
        before,after=(read(rows[f,'micro-raw',side]).astype(float) for side in ('baseline','corrected'))
        assert (after[...,3]>=before[...,3]).all(),(f,'Coverage decreased')
        x,y=(v[...,:3]*v[...,3:4] for v in (before,after))
        error=abs(x-y);scale=np.maximum(abs(x),abs(y))
        assert np.all(error<=gates['consumedAbsoluteTolerance']+gates['consumedRelativeTolerance']*scale),(f,'Consumed energy changed')
        maximum_error=max(maximum_error,float(error.max()))
        maximum_relative=max(maximum_relative,float((error/np.maximum(scale,1e-2)).max()))
        means.append([float(x.sum()),float(y.sum())])
        geometry=read(rows[f,'micro-geometry','baseline']);valid=geometry[...,3]>=0
        coverages.append([float(v[...,3][valid].mean()) for v in (before,after)])
    means=np.asarray(means).sum(0)
    assert changed>0,'No black misses exercised'
    return dict(schema='screen-probe-black-environment-micro-analysis.v1',qualified=True,
        measuredFrames=count,rayObservations=flags_total,reclassifiedBlackRays=changed,
        geometryAndAllRayRgbByteExact=True,protectedRayStatusChanges=0,coverageDecreases=0,
        maximumConsumedRgbAbsError=maximum_error,maximumConsumedRelativeError=maximum_relative,
        totalConsumedRgbChangePercent=100*(means[1]/means[0]-1),
        meanCoverage=np.asarray(coverages).mean(0).tolist(),
        nativePrivateHistoryAaPairs=common['aaPairs'],productionQualified=False,
        note='Same-frame private trace outputs only. Establishes classification/energy invariants, not final renderer noise, motion or GPU cost.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('root',type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve());(args.root/'micro-analysis.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
