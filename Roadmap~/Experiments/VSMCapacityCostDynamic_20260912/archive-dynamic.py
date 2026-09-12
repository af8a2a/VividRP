"""Archive baseline float buffers losslessly, with reproducible SHA256 validation.

pack ROOT OUTPUT: per-case solid xz archives, decompressed GPU bytes preserved.
restore ARCHIVES OUTPUT: restore the .gz/JSON layout used by analyze-dynamic.py.
Screenshots are omitted; selected preview images are archived separately.
"""
import argparse, gzip, hashlib, io, json, lzma, tarfile
from pathlib import Path

def digest(data): return hashlib.sha256(data).hexdigest()
def pack(root, output):
    output.mkdir(parents=True, exist_ok=True)
    request = json.loads((root/'request.json').read_text(encoding='utf-8-sig'))
    for name in ['request.json','status.txt','original-camera.json','original-light.json','original-settings.json']:
        if (root/name).is_file(): (output/name).write_bytes((root/name).read_bytes())
    manifest={'source':str(root.resolve()), 'format':'tar.xz; .gz payloads stored decoded as .raw; all hashes cover decoded bytes', 'cases':{}}
    payloads={}
    for scenario in request['scenarios']:
      for mode in request['modes']:
        case=scenario+'_'+mode; path=root/case; archive=output/(case+'.tar.xz')
        records=[]
        # Group the same GPU buffer across frames for solid compression.
        files=sorted(path.glob('*.json'))+sorted(path.glob('*.txt'))
        for kind in ['reference','signal','world','pages']:
            files+=sorted(path.glob('frame_*_'+kind+'.gz'))
        with tarfile.open(archive,'w:xz',preset=3) as tar:
            for file in files:
                data=gzip.decompress(file.read_bytes()) if file.suffix=='.gz' else file.read_bytes()
                stored=file.stem+'.raw' if file.suffix=='.gz' else file.name
                sha=digest(data); record={'original':file.name,'stored':stored,'bytes':len(data),'sha256':sha}
                if file.suffix=='.gz' and sha in payloads:
                    record['alias']=payloads[sha]
                else:
                    entry=tarfile.TarInfo(stored); entry.size=len(data); entry.mtime=0; entry.mode=0o644
                    tar.addfile(entry,io.BytesIO(data))
                    if file.suffix=='.gz': payloads[sha]={'case':case,'original':file.name}
                records.append(record)
        expected={r['stored']:r for r in records if 'alias' not in r}
        with tarfile.open(archive,'r:xz') as tar:
            members=tar.getmembers(); assert len(members)==len(expected)
            for member in members:
                data=tar.extractfile(member).read(); record=expected[member.name]
                assert len(data)==record['bytes'] and digest(data)==record['sha256']
        manifest['cases'][case]={'archive':archive.name,'archive_bytes':archive.stat().st_size,'archive_sha256':digest(archive.read_bytes()),'files':records,'roundtrip':'pass'}
        print(case,archive.stat().st_size,flush=True)
    (output/'archive-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')

def restore(root,output):
    manifest=json.loads((root/'archive-manifest.json').read_text())
    output.mkdir(parents=True,exist_ok=True)
    for name in ['request.json','status.txt','original-camera.json','original-light.json','original-settings.json']:
        if (root/name).is_file(): (output/name).write_bytes((root/name).read_bytes())
    for case,entry in manifest['cases'].items():
        assert Path(case).name==case
        archive=root/entry['archive']; assert digest(archive.read_bytes())==entry['archive_sha256']
        destination=output/case; destination.mkdir(exist_ok=True)
        expected={r['stored']:r for r in entry['files']}
        with tarfile.open(archive,'r:xz') as tar:
            for member in tar:
                record=expected[member.name]; assert Path(record['original']).name==record['original']
                data=tar.extractfile(member).read(); assert len(data)==record['bytes'] and digest(data)==record['sha256']
                target=destination/record['original']
                target.write_bytes(gzip.compress(data,compresslevel=1,mtime=0) if target.suffix=='.gz' else data)
        for record in entry['files']:
            if 'alias' not in record: continue
            alias=record['alias']; assert Path(alias['case']).name==alias['case'] and Path(alias['original']).name==alias['original']
            data=gzip.decompress((output/alias['case']/alias['original']).read_bytes())
            assert len(data)==record['bytes'] and digest(data)==record['sha256']
            (destination/record['original']).write_bytes(gzip.compress(data,compresslevel=1,mtime=0))
        print(case,'restored',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__); p.add_argument('action',choices=['pack','restore']);p.add_argument('source',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    (pack if a.action=='pack' else restore)(a.source,a.output)
