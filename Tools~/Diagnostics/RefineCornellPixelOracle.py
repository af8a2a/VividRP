"""Replace one region's oracle samples while preserving other physical targets."""
import os
for _name in ('OPENBLAS_NUM_THREADS','MKL_NUM_THREADS','OMP_NUM_THREADS'):
    os.environ[_name]='1'

import argparse
from concurrent.futures import ProcessPoolExecutor,as_completed
import hashlib
import json
from pathlib import Path
import time

from CornellPixelOracle import initialize,solve


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('parent',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--region',choices=('ceiling','floor','back','edge'),required=True)
    p.add_argument('--samples',type=int,default=131072);p.add_argument('--seed',type=int,default=40261117)
    p.add_argument('--workers',type=int,choices=(1,2,3,4),default=3)
    a=p.parse_args();assert a.samples>=2
    parent=a.parent.resolve();metadata=json.loads((parent/'metadata.json').read_text())
    assert metadata['schema']=='cornell-pixel-oracle.v1' and metadata['complete'] and 'refinement' not in metadata
    assert digest(parent/'reference.jsonl')==metadata['referenceSha256']
    parent_metadata_sha=digest(parent/'metadata.json');capture=Path(metadata['capture'])
    for name,sha in metadata['inputs'].items():assert digest(capture/name)==sha
    for name,sha in metadata['scripts'].items():assert digest(Path(name))==sha
    points=json.loads((parent/'points.json').read_text());records=[json.loads(q) for q in (parent/'reference.jsonl').read_text().splitlines()]
    assert len(records)==len(points)==metadata['points']
    records.sort(key=lambda q:q['index']);assert [q['index'] for q in records]==list(range(len(points)))
    selected=[q for q in points if q['region']==a.region];assert selected
    assert not {a.seed+q['index'] for q in selected}&{q['estimate']['seed'] for q in records},'Refinement seeds overlap parent paths'
    fixture=json.loads((capture/'live-fixture.json').read_text())
    out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    (out/'points.json').write_bytes((parent/'points.json').read_bytes())
    refinement=dict(region=a.region,indices=[q['index'] for q in selected],samples=a.samples,seed=a.seed,
        parent=str(parent),parentMetadataSha256=parent_metadata_sha,parentReferenceSha256=metadata['referenceSha256'])
    metadata=dict(metadata,complete=False,refinement=refinement,scripts=dict(metadata['scripts']))
    metadata['scripts'][str(Path(__file__).resolve())]=digest(Path(__file__))
    (out/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
    started=time.monotonic();done=0
    with (out/'refinement.jsonl').open('x') as stream:
        with ProcessPoolExecutor(max_workers=a.workers,initializer=initialize,initargs=(fixture,)) as pool:
            jobs=[pool.submit(solve,q,a.samples,metadata['bounces'],a.seed) for q in selected]
            for job in as_completed(jobs):
                value=job.result();records[value['index']]=value;done+=1;stream.write(json.dumps(value)+'\n');stream.flush()
                if done%16==0 or done==len(selected):
                    progress=dict(completed=done,total=len(selected),elapsedSeconds=time.monotonic()-started)
                    (out/'progress.json').write_text(json.dumps(progress)+'\n');print(json.dumps(progress),flush=True)
    assert digest(parent/'metadata.json')==parent_metadata_sha and digest(parent/'reference.jsonl')==refinement['parentReferenceSha256']
    for name,sha in metadata['inputs'].items():assert digest(capture/name)==sha
    for name,sha in metadata['scripts'].items():assert digest(Path(name))==sha
    with (out/'reference.jsonl').open('x') as stream:
        for record in records:stream.write(json.dumps(record)+'\n')
    metadata.update(complete=True,referenceSha256=digest(out/'reference.jsonl'),refinementSha256=digest(out/'refinement.jsonl'),
        refinementElapsedSeconds=time.monotonic()-started,refinementWorkers=a.workers)
    (out/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n');print(json.dumps(dict(complete=True,refinedPoints=done,seconds=metadata['refinementElapsedSeconds'])),flush=True)


if __name__=='__main__':main()
