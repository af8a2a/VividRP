"""Query the two Resolve dispatches through the maintained PIX analyzer client."""
import argparse
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import sys

sys.path.insert(0,str(Path(__file__).resolve().parent.parent/'PIX'))
from vivid_pix_agent import Workflow


def select_pair(rows):
    lookup={(r['queueId'],r['eventIndex']):r for r in rows}
    selected={}
    for label in ('Baseline','Stratified'):
        name='GI Quadrature '+label+' Resolve';matches=[]
        for row in rows:
            if not row.get('gpuWork'):continue
            current=row;seen=set()
            while current and current['eventIndex'] not in seen:
                seen.add(current['eventIndex'])
                if current['name']==name:matches.append(row);break
                current=lookup.get((row['queueId'],current['parentIndex']))
        assert len(matches)==1,('Missing/ambiguous dispatch under exact marker',name,matches)
        selected[label.lower()]=matches[0]
    assert len({(r['queueId'],r['eventIndex']) for r in selected.values()})==2
    assert all(r['queueType']==0 for r in selected.values())
    return selected


def run(capture_root,output,pix,repetitions):
    load=lambda p:json.loads(Path(p).read_text(encoding='utf-8-sig'))
    diagnosis=load(capture_root/'diagnosis.json');ready=load(capture_root/'capture-ready.json')
    assert diagnosis['success'] and diagnosis['state']=='complete' and not diagnosis['eventsTruncated']
    path=Path(diagnosis['capturePath']);digest=hashlib.sha256(path.read_bytes()).hexdigest()
    assert digest==diagnosis['captureHash']
    rows=[]
    for event_file in sorted(capture_root.glob('*-events.json')):
        value=load(event_file)
        assert value['captureHash']==digest and value['sessionId']==diagnosis['sessionId']
        rows.extend(value['data']['items'])
    assert len(rows)==diagnosis['eventTotal'];selected=select_pair(rows)
    output.mkdir(parents=True,exist_ok=False)
    args=SimpleNamespace(deadline=1800,timeout=120,page_size=128,
        analyzer=str(Path(__file__).resolve().parent.parent/'PIX/bin/vivid-pix-analyzer.exe'),pix=str(pix),marker=diagnosis['marker'])
    workflow=Workflow(args,output);workflow.capture=str(path);workflow.capture_hash=digest
    workflow.capture_session=diagnosis['sessionId'];workflow.mode=ready['boundaryMode']
    request=dict(capturePath=str(path),captureHash=digest,sessionId=workflow.capture_session,
                 expectedPass=args.marker,boundaryMode=workflow.mode,selected=selected,repetitions=repetitions)
    (output/'request.json').write_text(json.dumps(request,indent=2)+'\n')
    details={}
    for label,row in selected.items():
        selection=dict(queue=row['queueId'],event=row['eventIndex'])
        details[label]={action:workflow.analyze(action,**selection) for action in ('event','pipeline','resources')}
    timings=[]
    for repeat in range(repetitions):
        order=('baseline','stratified') if repeat%2==0 else ('stratified','baseline')
        for label in order:
            row=selected[label];value=workflow.analyze('timing',optional=True,queue=row['queueId'],event=row['eventIndex'])
            timings.append(dict(repeat=repeat,label=label,value=value))
    result=dict(schema='screen-probe-quadrature-pix-replay.v1',capture=request,details=details,timings=timings,
                evidence=workflow.evidence,limitations=workflow.limitations,
                performanceQualified=False,
                scope='Raw PIX timing queries on one captured pair. Require workload-scaling and output-equivalence qualification before performance claims; no unperturbed live-frame or total-GI timing.')
    (output/'replay.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(dict(selected=selected,timings=timings,limitations=workflow.limitations),indent=2))
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture_root',type=Path);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--pix',type=Path,required=True);p.add_argument('--repetitions',type=int,default=4,choices=range(1,9));args=p.parse_args()
    run(args.capture_root.resolve(),args.output.resolve(),args.pix.resolve(),args.repetitions)
