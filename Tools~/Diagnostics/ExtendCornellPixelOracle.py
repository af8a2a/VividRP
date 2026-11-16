"""Refine another region without changing a previously pinned oracle driver."""
import os
for _name in ('OPENBLAS_NUM_THREADS','MKL_NUM_THREADS','OMP_NUM_THREADS'):
    os.environ[_name]='1'

import argparse
from concurrent.futures import ProcessPoolExecutor,as_completed
import json
from pathlib import Path
import time

from CornellPixelOracle import initialize,solve
from RefineCornellPixelOracle import digest


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('parent',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--region',choices=('ceiling','floor','back','edge'),required=True)
    p.add_argument('--samples',type=int,default=131072);p.add_argument('--seed',type=int,default=60261117)
    p.add_argument('--workers',type=int,choices=(1,2,3,4),default=3)
    a=p.parse_args();parent=a.parent.resolve();meta=json.loads((parent/'metadata.json').read_text())
    assert meta['schema']=='cornell-pixel-oracle.v1' and meta['complete'] and a.samples>=meta['samples']
    capture=Path(meta['capture']);parent_sha=digest(parent/'metadata.json')
    assert digest(parent/'reference.jsonl')==meta['referenceSha256']
    for name,sha in meta['inputs'].items():assert digest(capture/name)==sha
    for name,sha in meta['scripts'].items():assert digest(Path(name))==sha
    records=[json.loads(q) for q in (parent/'reference.jsonl').read_text().splitlines()];records.sort(key=lambda q:q['index'])
    points=json.loads((parent/'points.json').read_text());assert len(records)==len(points)==meta['points']
    assert [q['index'] for q in records]==list(range(len(points)))
    selected=[q for q in points if q['region']==a.region];assert selected
    assert not {a.seed+q['index'] for q in selected}&{q['estimate']['seed'] for q in records}
    out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);(out/'points.json').write_bytes((parent/'points.json').read_bytes())
    refinement=dict(region=a.region,indices=[q['index'] for q in selected],samples=a.samples,seed=a.seed,
        parent=str(parent),parentMetadataSha256=parent_sha,parentReferenceSha256=meta['referenceSha256'])
    meta=dict(meta,complete=False,refinement=refinement,scripts=dict(meta['scripts']))
    meta['scripts'][str(Path(__file__).resolve())]=digest(Path(__file__))
    (out/'metadata.json').write_text(json.dumps(meta,indent=2)+'\n');started=time.monotonic();done=0
    fixture=json.loads((capture/'live-fixture.json').read_text())
    with (out/'refinement.jsonl').open('x') as stream:
        with ProcessPoolExecutor(max_workers=a.workers,initializer=initialize,initargs=(fixture,)) as pool:
            jobs=[pool.submit(solve,q,a.samples,meta['bounces'],a.seed) for q in selected]
            for job in as_completed(jobs):
                value=job.result();records[value['index']]=value;done+=1;stream.write(json.dumps(value)+'\n');stream.flush()
                if done%16==0 or done==len(selected):
                    progress=dict(completed=done,total=len(selected),elapsedSeconds=time.monotonic()-started)
                    (out/'progress.json').write_text(json.dumps(progress)+'\n');print(json.dumps(progress),flush=True)
    assert digest(parent/'metadata.json')==parent_sha and digest(parent/'reference.jsonl')==refinement['parentReferenceSha256']
    for name,sha in meta['inputs'].items():assert digest(capture/name)==sha
    for name,sha in meta['scripts'].items():assert digest(Path(name))==sha
    with (out/'reference.jsonl').open('x') as stream:
        for record in records:stream.write(json.dumps(record)+'\n')
    meta.update(complete=True,referenceSha256=digest(out/'reference.jsonl'),refinementSha256=digest(out/'refinement.jsonl'),
        refinementElapsedSeconds=time.monotonic()-started,refinementWorkers=a.workers)
    (out/'metadata.json').write_text(json.dumps(meta,indent=2)+'\n');print(json.dumps(dict(complete=True,refinedPoints=done)),flush=True)


if __name__=='__main__':main()
