"""Reject missing or stale GPU samples before interpreting private Resolve cost."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read


def qualify_samples(capture,protocol):
    reasons=[];samples={}
    if not capture.get('complete') or capture.get('errors'):reasons.append('Capture incomplete or render errors present')
    for marker in protocol['markers']:
        rows=[r for r in capture.get('timingSamples',[]) if r['marker']==marker]
        if len(rows)!=1:reasons.append('Missing or duplicate marker: '+marker);continue
        row=rows[0];all_samples=row['samples']
        if not row['valid'] or row['wrappedAround'] or row['count']!=len(all_samples):
            reasons.append('Invalid GPU recorder: '+marker);continue
        retained=all_samples[protocol['censorFirstGpuSamples']:len(all_samples)-protocol['censorLastGpuSamples']]
        if len(retained)<protocol['minimumRetainedGpuSamples'] or any(v['count']!=1 or not 0<v['nanoseconds']<1_000_000_000 for v in retained):
            reasons.append('No sufficient positive single-dispatch GPU samples: '+marker);continue
        samples[marker]=np.array([v['nanoseconds'] for v in retained],float)/1e6
    return reasons,samples


def analyze(root):
    root=root.resolve();load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
    c,p=load('capture.json'),load('protocol.json')
    assert p['schema']=='screen-probe-quadrature-timing-protocol.v1' and load('source-verification.json')['unchanged']
    for name,digest in p['files'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    total=0
    for row in c['records']:
        if not row.get('path'):continue
        path=Path(row['path']).resolve();assert path.is_relative_to(root)
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row['sha256'];total+=len(data)
    assert total==c['payloadBytes']<=c['payloadBudget']==p['payloadBudget']
    assert c['componentAaPairs']==len(p['aaFrames']) and c['epochReset']
    aa=[r for r in c['records'] if r.get('componentAa')]
    assert len(aa)==2*len(p['aaFrames'])
    for f in p['aaFrames']:
        pair=[r for r in aa if r['frame']==f]
        assert len(pair)==2 and {r['side'] for r in pair}=={'native','copied'} and len({r['sha256'] for r in pair})==1
    sources=[r for r in c['records'] if r.get('timingSource')]
    assert len(sources)==2 and {r['frame'] for r in sources}=={1023,1663}
    assert {r['sha256'] for r in sources}=={'40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8'}
    controls=[r for r in c['records'] if r.get('tag')=='quadrature-ceiling']
    assert len(controls)==2 and all((read(r)[...,3]<0).all() for r in controls),'Candidate branch was not observed'
    reasons,samples=qualify_samples(c,p)
    result=dict(schema='screen-probe-quadrature-timing-analysis.v1',qualified=not reasons,available=not reasons,reasons=reasons,
        sourceEpochQualified=True,nativeCopiedResolveByteExact=True,candidateBranchObserved=True,
        scope='Private same-frame Resolve kernels only; excludes copy and readback; not production total GI.')
    if not reasons:result['markers']={name:dict(samples=len(v),meanMs=float(v.mean()),medianMs=float(np.median(v)),p05Ms=float(np.quantile(v,.05)),p95Ms=float(np.quantile(v,.95))) for name,v in samples.items()}
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();root=args.root
    result=analyze(root)
    args.output.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
