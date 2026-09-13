"""Lossless final GPU capture archive. Hashes cover decoded float bytes."""
import argparse, gzip, hashlib, io, json, tarfile
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['pack','restore'])
p.add_argument('source',type=Path)
p.add_argument('output',type=Path)
a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True)
digest=lambda b:hashlib.sha256(b).hexdigest()
if a.action=='pack':
    records=[]
    with tarfile.open(a.output/'capture.tar.xz','w:xz',preset=3) as archive:
        for f in sorted(x for x in a.source.rglob('*') if x.is_file()):
            original=f.relative_to(a.source).as_posix()
            raw=gzip.decompress(f.read_bytes()) if f.suffix=='.gz' else f.read_bytes()
            stored=original+'.decoded' if f.suffix=='.gz' else original
            entry=tarfile.TarInfo(stored);entry.size=len(raw);entry.mtime=0
            archive.addfile(entry,io.BytesIO(raw))
            records.append({'original':original,'stored':stored,'decodedGzip':f.suffix=='.gz','bytes':len(raw),'sha256':digest(raw)})
    manifest={'source':str(a.source.resolve()),'archive':'capture.tar.xz','sha256':digest((a.output/'capture.tar.xz').read_bytes()),'files':records}
    (a.output/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    with tarfile.open(a.output/'capture.tar.xz','r:xz') as archive:
        for entry,record in zip(archive,records):
            raw=archive.extractfile(entry).read()
            assert entry.name==record['stored'] and len(raw)==record['bytes'] and digest(raw)==record['sha256']
    print(json.dumps({'status':'pass','files':len(records),'compressed_bytes':(a.output/'capture.tar.xz').stat().st_size}))
else:
    manifest=json.loads((a.source/'manifest.json').read_text(encoding='utf-8'))
    assert digest((a.source/manifest['archive']).read_bytes())==manifest['sha256']
    records={x['stored']:x for x in manifest['files']}
    with tarfile.open(a.source/manifest['archive'],'r:xz') as archive:
        for entry in archive:
            record=records[entry.name];raw=archive.extractfile(entry).read()
            assert len(raw)==record['bytes'] and digest(raw)==record['sha256']
            target=(a.output/record['original']).resolve()
            assert target.is_relative_to(a.output.resolve())
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(gzip.compress(raw,compresslevel=1,mtime=0) if record['decodedGzip'] else raw)
    print(json.dumps({'status':'pass','restored_files':len(records)}))
