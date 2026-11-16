"""Compare actual native origin cases against the fixed Cornell physical reference."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeCornellPixelOracle import analyze as oracle,comparison
from AnalyzeScreenProbeQuadratureHistory import qualify_replay,energy_and_noise
from TemporalSamplingExperiment import read,LUMA


def qualify_case_pair(native):
    a,b=native;pa,pb=(q[1] for q in native)
    assert pa['nativeOriginLimitMeters']==.04 and pb['nativeOriginLimitMeters']==.01
    for key in ('sequence','warmup','measuredFrames','spacing','rays','originTraceSteps','originAdaptiveCandidate','originStepPolicy'):
        assert pa[key]==pb[key],key
    assert pa['originAdaptiveCandidate'] and pa['originTraceSteps']==2048
    for name,digest in pa['files'].items():
        if name.endswith('.compute.txt') or name=='targets.json':assert pb['files'][name]==digest,'Case shader/target differs: '+name
    for field in ('experimentalTracePrograms','privatePrograms','originReplayPrograms','quadraturePrograms','oraclePrograms'):
        assert a[0][field]==b[0][field],'Compiled programs differ: '+field
    start,count=pa['warmup'],pa['measuredFrames'];inputs=0
    for f in range(start,start+count):
        for tag in ('input-depth','input-gbuffer0','input-gbuffer1','geometry','pixel-geometry'):
            ra,rb=(q[2][f,tag] for q in native)
            assert all(ra[k]==rb[k] for k in ('sha256','width','height','format')),'Geometry input differs: '+str((f,tag))
            inputs+=1
    states=[{q['frame']:q for q in v[0]['records'] if 'state' in q} for v in native]
    for f in range(start,start+count):
        assert states[0][f]['vp']==states[1][f]['vp'] and states[0][f]['state']['inverseVp']==states[1][f]['state']['inverseVp']
    return inputs


def analyze(control,candidate,reference):
    native=[];within=[]
    for root in (control,candidate):
        within.append(oracle(root,reference))
        native.append(qualify_replay(root))
    inputs=qualify_case_pair(native)
    pa=native[0][1];start,count=pa['warmup'],pa['measuredFrames']
    points=json.loads((reference/'points.json').read_text());records=list(map(json.loads,(reference/'reference.jsonl').read_text().splitlines()))
    records.sort(key=lambda q:q['index']);assert len(records)==len(points)
    ref=np.array([q['estimate']['meanRgb'] for q in records]);cov=np.array([q['estimate']['iidCovarianceMeanRgb'] for q in records])
    values=[]
    for v in native:
        x=np.stack([read(v[3][f,'oracle-final','candidate'])[0].astype(float) for f in range(start,start+count)])
        values.append(x[...,:3]*x[...,3:4])
    groups={};full={}
    for region in pa['regions']:
        for obj in (None,*sorted({p['object'] for p in points if p['region']==region})):
            selected=np.array([p['region']==region and (obj is None or p['object']==obj) for p in points]);assert selected.any()
            x,y=(v[:,selected] for v in values);variance=np.maximum(np.einsum('i,nij,j->n',LUMA,cov[selected],LUMA),0)
            result=comparison(x@LUMA,y@LUMA,ref[selected]@LUMA,variance)
            result['rgb']={name:comparison(x[...,i],y[...,i],ref[selected,i],cov[selected,i,i]) for i,name in enumerate(('r','g','b'))}
            result['points']=int(selected.sum());groups[region if obj is None else region+'::'+obj]=result
        colors=[np.stack([read(v[3][f,'final-'+region,'candidate']).astype(float) for f in range(start,start+count)]) for v in native]
        full[region]=energy_and_noise(*colors)
    return dict(schema='screen-probe-native-origin-physical-comparison.v1',qualified=True,geometryInputAaPairs=inputs,
        originStepPolicy=pa['originStepPolicy'],originAndBudgetChangedTogether=pa['originStepPolicy']!='field-diagonal-all-origins',
        actualNativeHistoryAaPairs=[v[4] for v in native],groups=groups,fullRegionNoise=full,withinCaseOracle=within,
        productionQualified=False,limits=[
            'Field budget is shared by all origins only for originStepPolicy=field-diagonal-all-origins; earlier paired cases changed origin and step budget together.',
            'Two separately converged native origin histories and radiance-driven guides; the final-pixel pair is not a same-input wavelet counterfactual.',
            'External geometry, shaders, target pixels, matrices and frozen source are qualified; cross-run bit-exact history repeatability is not claimed.',
            'Origin correction intentionally changes irradiance. Original denoising mean-drift gates are not relabeled as passed.',
            'One static sampling sequence. Motion, independent sequence, full-frame artifacts and native GPU cost remain unqualified.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('control',type=Path);p.add_argument('candidate',type=Path);p.add_argument('reference',type=Path);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();assert not a.output.exists();r=analyze(a.control.resolve(),a.candidate.resolve(),a.reference.resolve())
    a.output.write_text(json.dumps(r,indent=2)+'\n')
    for name,q in r['groups'].items():print(json.dumps(dict(group=name,points=q['points'],meanError=q['meanErrorPercent'],mseChange=q['meanMapMse']['deltaPercentOfBaseline'])))
