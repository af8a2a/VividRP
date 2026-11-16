"""Check whether identical trace inputs leave only the predicted anchor EMA tail."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeBlackEnvironmentQuality import inspect
from TemporalSamplingExperiment import read


def recurrence(before,after,age,frames=None):
    assert before.shape==after.shape and before.ndim==4 and before.shape[-1]==4
    assert np.array_equal(before[...,3],after[...,3]),'Coverage differs'
    assert age>1 and np.isfinite(before).all() and np.isfinite(after).all()
    frames=np.arange(len(before)) if frames is None else np.asarray(frames)
    assert frames.shape==(len(before),) and (np.diff(frames)>0).all()
    decay=1-1/age;delta=after[...,:3]-before[...,:3]
    step=delta[1:]-decay**np.diff(frames).reshape(-1,1,1,1)*delta[:-1]
    predicted=delta[:1]*decay**(frames-frames[0]).reshape(-1,1,1,1)
    maximum_step=float(abs(step).max());maximum_prediction=float(abs(delta-predicted).max())
    return dict(age=age,coefficient=decay,initialMaximumDifference=float(abs(delta[0]).max()),
        recurrenceMaximumError=maximum_step,predictionMaximumError=maximum_prediction,
        measuredNormRatio=float(np.linalg.norm(delta[-1])/max(np.linalg.norm(delta[0]),1e-20)),
        expectedNormRatio=decay**int(frames[-1]-frames[0]),comparedFrames=frames.tolist(),
        emaTailExplainsDifference=maximum_step<=1e-6 and maximum_prediction<=1e-6)


def retained(root):
    if not (root/'raw-retention.json').exists():return inspect(root)
    load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
    capture,protocol,audit=load('capture.json'),load('protocol.json'),load('raw-retention.json')
    assert capture['complete'] and not capture['errors'] and load('source-verification.json')['unchanged']
    assert audit['complete'] and not audit['errors'] and not audit['rawReanalysisAvailable']
    for name,digest in protocol['files'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    # Full-window trace identities below compare retained hash records. Numeric
    # recurrence uses only actual surviving payloads, never fabricated frames.
    rows={(r['frame'],r['tag']):r for r in capture['records'] if 'tag' in r}
    states={r['frame']:r for r in capture['records'] if 'state' in r}
    return capture,protocol,rows,states


def analyze(before,after):
    a,b=retained(before),retained(after)
    for key in ('guided','corrected','sequence','warmup','measuredFrames','spacing','rays'):
        assert a[1][key]==b[1][key]
    assert not a[1]['guided'] and a[1]['measuredFrames']==128
    start=a[1]['warmup'];frames=range(start,start+128)
    tags=['raw','geometry','pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1']
    tags+=['directional-'+roi for roi in ('ceiling','floor','back','edge')]
    tags+=['directional-meta-'+roi for roi in ('ceiling','floor','back','edge')]
    for f in frames:
        for tag in tags:assert a[2][f,tag]['sha256']==b[2][f,tag]['sha256'],(f,tag)
        assert a[3][f]['vp']==b[3][f]['vp'] and a[3][f]['state']==b[3][f]['state']
    for c in (a,b):
        for f in (start,start+127):
            statistics=read(c[2][f,'statistics'])[59,46:74]
            assert (statistics[:,0]==512).all() and (statistics[:,3]==1).all()
    saved=[f for f in frames if all(Path(c[2][f,'anchor-ceiling']['path']).is_file() for c in (a,b))]
    assert len(saved)>=4 and saved[0]==start and saved[-1]==start+127
    for c in (a,b):
        for f,tag in [(f,'anchor-ceiling') for f in saved]+[(f,'statistics') for f in (start,start+127)]:
            row=c[2][f,tag];path=Path(row['path']).resolve()
            root=before if c is a else after
            assert path.is_relative_to(root.resolve())
            assert hashlib.sha256(path.read_bytes()).hexdigest()==row['sha256']
    values=[np.stack([read(c[2][f,'anchor-ceiling']).astype(float) for f in saved]) for c in (a,b)]
    result=recurrence(*values,512,saved)
    return dict(schema='anchor-history-initialization.v1',traceAndDirectionHashRecordsExact=True,
        fullRawSequenceAvailable=len(saved)==128,retainedPayloadFrames=len(saved),
        endpointGpuAgeVerified=512,ceiling=result,productionChange=False,
        limits=['Ceiling strip only; age is verified at the two saved endpoints, recurrence only across actual retained payloads.',
            'This checks decay of an existing difference, not the first frame where initialization diverged.',
            'No claim that final pixel variance or guiding quality improved.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('before',type=Path);p.add_argument('after',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();v=analyze(args.before.resolve(),args.after.resolve());args.output.write_text(json.dumps(v,indent=2)+'\n');print(json.dumps(v,indent=2))
