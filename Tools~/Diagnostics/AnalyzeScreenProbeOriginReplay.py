"""Qualify exact native tracing replay and receiver-origin safety counters."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_replay


def counters(row):
    values=np.array(row['counters'],dtype='<u4');assert values.shape==(32,)
    assert hashlib.sha256(values.tobytes()).hexdigest()==row['sha256']
    assert values[0]>0 and values[1]<=values[0] and values[2]+values[4]==values[1]
    assert values[5]<=values[2] and values[6]==values[11]==0,'Invalid or nonblack culled ray'
    return values.astype(np.int64)


def analyze(capture,protocol,rows):
    assert protocol['originTraceReplay'] and protocol['configuredRayBiasMeters']==.04
    frames=protocol['originReplayFrames'];assert frames==[0,*range(protocol['warmup'],protocol['warmup']+protocol['measuredFrames'])]
    states={r['frame']:r for r in capture['records'] if r.get('originReplayState')}
    assert set(states)==set(frames) and len(states)==sum(bool(r.get('originReplayState')) for r in capture['records'])
    pairs=0;totals={};safety={};roles_by_frame={};mask_rows=[]
    for f in frames:
        s=states[f];assert s['originNativeLimit']==protocol['nativeOriginLimitMeters']
        if protocol.get('originAdaptiveCandidate',False):
            assert s['originAdaptiveCandidate'] and s['originScreenBuffersVerified']
        roles=[r for r in s['roles'] if r!='screen-rays' or s['screen']];assert len(roles)==len(set(roles))
        assert {'directional-rays','geometry','hit-lighting','trace-sources','ray-diagnostics','first-hit'}<=set(roles)
        roles_by_frame[f]=roles
        if protocol.get('originIncompleteMaskComparison',False):
            row=rows[f,'origin-incomplete-pair','origin-paired'];v=np.array(row['counters'],dtype='<u4')
            assert v.shape==(32,) and hashlib.sha256(v.tobytes()).hexdigest()==row['sha256']
            assert v[:4].astype(np.int64).sum()==v[4] and v[4]>0
            mask_rows.append(dict(frame=f,newlyIncomplete=int(v[0]),rescued=int(v[1]),bothIncomplete=int(v[2])))
        for role in roles:
            a,b=(rows[f,'origin-'+role+'-full',side] for side in ('origin-native','origin-aa'))
            assert all(a[k]==b[k] for k in ('sha256','format','width','height')),'Native trace replay differs'
            pairs+=1
        a,b=(rows[f,'origin-counters',side] for side in ('origin-native','origin-aa'))
        assert a['sha256']==b['sha256'];counters(a);counters(b);pairs+=1
        frame_rows=[r for r in rows.values() if r['frame']==f and r['tag']=='origin-counters' and r['side']!='origin-aa']
        by_limit={r['originLimitMeters']:counters(r) for r in frame_rows}
        assert set(by_limit)=={.04,.01,.005}
        for limit,value in by_limit.items():
            totals.setdefault(limit,np.zeros(32,dtype=np.int64));totals[limit]+=value
            ref=by_limit[.04]
            q=safety.setdefault(limit,dict(coveredSelfHitMaximumIncrease=0,incompleteRayMaximumIncrease=0,coveredRayMaximumLoss=0,anySelfHitMaximumIncrease=0))
            q['coveredSelfHitMaximumIncrease']=max(q['coveredSelfHitMaximumIncrease'],int(value[9]-ref[9]))
            q['anySelfHitMaximumIncrease']=max(q['anySelfHitMaximumIncrease'],int(value[8]-ref[8]))
            q['incompleteRayMaximumIncrease']=max(q['incompleteRayMaximumIncrease'],int(value[7]-ref[7]))
            q['coveredRayMaximumLoss']=max(q['coveredRayMaximumLoss'],int(ref[2]-value[2]))
    assert pairs==capture['originAaPairs']
    for q in safety.values():
        q['selfHitGatePassed']=q['coveredSelfHitMaximumIncrease']<=protocol['gates']['coveredSelfHitIncrease']
        q['incompleteGatePassed']=q['incompleteRayMaximumIncrease']<=protocol['gates']['incompleteRayIncrease']
    return dict(schema='screen-probe-origin-replay.v1',qualified=True,frames=len(frames),fullTraceAndCounterAaPairs=pairs,
        nativeOriginLimitMeters=protocol['nativeOriginLimitMeters'],configuredBiasMeters=.04,
        countersByLimit={str(k):v.tolist() for k,v in totals.items()},safetyByLimit={str(k):v for k,v in safety.items()},
        blackBackHitsStayCoveredAndBlack=True,productionQualified=False,
        incompleteMaskComparison=mask_rows,newIncompleteMaskGatePassed=not any(q['newlyIncomplete'] for q in mask_rows) if mask_rows else None,
        limits=['Same-frame raw tracing with identical receiver/guide/history bindings. Replays do not own independent histories or final pixels.',
            'Safety counters flag same-entity hits in the convex Cornell fixture; not a general concave-geometry rejection rule.',
            'Native final pixels require independent physical-reference analysis. No GPU-cost acceptance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();assert not a.output.exists()
    c,p,_,rows,*_=qualify_replay(a.capture.resolve());r=analyze(c,p,rows)
    a.output.write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r,indent=2))
