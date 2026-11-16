"""Audit the bounded uninstrumented native short control at128 or512 anchor age."""
import argparse
import hashlib
import json
from pathlib import Path

from AnalyzeScreenProbeUnusedBinWitness import cpu_words,load


FUNCTIONAL=('geometry','raw','composite','directional-temporal','directional-filtered')
HISTORY_PAIRS=(('previous-geometry','geometry'),('previous-directional','directional-temporal'),
               ('previous-directional-filtered','directional-filtered'))


def differences(entries,reference,repeated,frames,tags):
    return [[frame,tag] for frame in frames for tag in tags
        if entries[reference,frame,tag]['sha256']!=entries[repeated,frame,tag]['sha256']]


def analyze(root):
    root=root.resolve();c=load(root/'capture.json');p=load(root/'protocol.json')
    assert c['schema']=='screen-probe-directional-binding.v1' and c['complete'] and not c['errors']
    assert p['schema']=='screen-probe-directional-binding-protocol.v1' and p['anchorAge'] in (128,512) and p['arms'] in (2,8)
    assert c['lastFrame']==p['frames']==64 and load(root/'source-verification.json')['unchanged']
    completed=[r for r in c['records'] if r.get('completed')];arms=sorted(r['arm'] for r in completed)
    assert len(arms)==len(set(arms))==p['arms']
    assert all(r['targetCount']==64 and r['otherCount']==0 and r['age']==p['anchorAge']
        and r['sourceFrames']==264 and not r['recorderMode'] and not r['tickMode'] for r in completed)
    e={(r['arm'],r['frame'],r['tag']):r for r in c['records'] if 'tag' in r}
    assert len(e)==sum('tag' in r for r in c['records'])
    states={(r['arm'],r['frame']):r for r in c['records'] if 'constants' in r}
    cpu={(r['arm'],r['frame']):r['cpu'] for r in c['records'] if 'cpu' in r}
    assert len(states)==len(cpu)==len(arms)*64
    flags=32 if p['anchorAge']>128 else 0;assert p['automaticPrecisionBit']==flags
    history_errors=[];cpu_errors=[];owner_errors=[];history_checks=0
    for arm in arms:
        for f in range(64):
            state=states[arm,f];reference=states[arms[0],f]
            assert state['constants']['flags']==flags and cpu[arm,f]['frameOverride']==1733+f
            if cpu_words(state)!=cpu_words(reference) or state['predicates']!=reference['predicates']:cpu_errors.append([arm,f])
            bindings={v['field']:v['id'] for v in state['bindings']}
            for cur,prev,owner in (('m_Geometry','m_PreviousGeometry','geometry'),('m_DirectionalTemporal','m_PreviousDirectional','directional')):
                if bindings[cur]==bindings[prev] or bindings[cur]!=state['historyIds'][owner+'Current'] or bindings[prev]!=state['historyIds'][owner+'Previous']:
                    owner_errors.append([arm,f,owner])
            if f:
                for prev,cur in HISTORY_PAIRS:
                    history_checks+=1
                    if e[arm,f,prev]['sha256']!=e[arm,f-1,cur]['sha256']:history_errors.append([arm,f,prev])
    saved=0
    for r in c['savedPayloads']:
        path=Path(r['path']).resolve();assert path.is_relative_to(root)
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==e[r['arm'],r['frame'],r['tag']]['sha256'];saved+=len(data)
    assert saved==c['savedPayloadBytes']<=c['savedPayloadBudget']==p['savedPayloadBudget']
    assert c['retainedPayloadBytes']<=c['retainedPayloadBudget']==p['ownedPayloadBudget']
    comparisons={};qualified=[]
    for arm in arms[1:]:
        changed=differences(e,arms[0],arm,range(64),FUNCTIONAL)
        comparisons[arm]=dict(functionalDifferences=changed,firstFunctionalFrame=min((r[0] for r in changed),default=None))
        for f in range(1,64):
            same_inputs=not differences(e,arms[0],arm,[f],('geometry','composite','previous-geometry','previous-directional'))
            if same_inputs and [f,'directional-temporal'] in changed and cpu_words(states[arm,f])==cpu_words(states[arms[0],f]):
                qualified.append(dict(reference=arms[0],repeated=arm,frame=f));break
    return dict(schema='screen-probe-native-epoch-onset-analysis.v1',complete=True,anchorAge=p['anchorAge'],arms=arms,
        framesPerArm=64,functionalComparisons=(len(arms)-1)*64*len(FUNCTIONAL),comparisons=comparisons,
        historyComparisons=history_checks,historyContentErrors=history_errors,selectedCpuErrors=cpu_errors,cpuOwnershipErrors=owner_errors,
        matchingObservedInputsTemporalDifferences=qualified,firstSavedBundle=c['firstBundle'],savedPayloadBytes=saved,
        shortPassed=not (any(v['functionalDifferences'] for v in comparisons.values()) or history_errors or cpu_errors or owner_errors),
        productionQualified=False,finalQualityVerified=False,performanceQualified=False,
        limits=['Hash-only current stage observations over64frames; no claim about later warmup or final image noise.',
                'Previous history content and selected CPU state/ownership do not prove original dispatch b0 or physical resource accesses.',
                'Readbacks, depth copy and enabled GPU Profiler change observation workload; a pass does not withdraw prior long native control failures.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();value=analyze(args.root);args.output.write_text(json.dumps(value,indent=2)+'\n');print(json.dumps(value,indent=2))
