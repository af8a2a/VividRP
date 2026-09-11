"""Audit the matched 50 m Sponza runs; captures are not performance runs."""
from pathlib import Path
import csv,gzip,json,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
RUNS={'before':'20260911_140927_048','after':'20260911_141314_738'}

def raw(d,i,k):
    a=np.frombuffer(gzip.decompress((d/f'frame_{i:03}_{k}.bin.gz').read_bytes()),'<f4' if k.startswith('debug') else '<u4')
    return a.reshape(-1,4) if k.startswith('debug') else a

def main():
    report={};poses={}
    for revision,name in RUNS.items():
        root=ROOT/'Temp~/vsm-general-captures'/name;assert (root/'status.txt').read_text().strip()=='complete'
        out=HERE/('scene-'+revision);out.mkdir(exist_ok=True)
        for f in ('shader-source-sha256.json','run-plan.json'):shutil.copyfile(root/f,out/f)
        result={};poses[revision]={}
        for d in sorted(x for x in root.iterdir() if x.is_dir()):
            frames=[json.loads(l) for l in (d/'frames.jsonl').read_text().splitlines()];rows=[]
            poses[revision][d.name]=[{k:f[k] for k in ('cameraPosition','cameraEuler','jitter','shaderFrameSeed','gpuVP')} for f in frames]
            for pose in poses[revision][d.name]: pose['shaderFrameSeed'] %= 256
            for f in frames:
                if not f['capture'] or not f['receiverSnapshot']:continue
                i=f['step'];lev=raw(d,i,'debug0');mask=lev[:,0]>=0;a=raw(d,i,'debug4')[mask];b=raw(d,i,'debug5')[mask];s=raw(d,i,'debug9')[mask];c=raw(d,i,'counters')
                rows.append(dict(step=i,receivers=int(mask.sum()),reads=float(a[:,1].sum()),rays=float(s[:,0].sum()),pcf_fallback=int(s[:,3].sum()),failed_footprints=int(s[:,2].sum()),missing=int((b[:,0]>0).sum()),replay_over_1e3=int((b[:,3]>.001).sum()),replay_max=float(b[:,3].max()),replay_abs_sum=float(b[:,3].sum()),unavailable=int((lev[mask,1]<0).sum()),requested=int(c[1]),overflow=int(c[3]),new_pages=int(c[2])))
            assert len(rows)==sum(f["capture"] and f["receiverSnapshot"] for f in frames)
            with (out/(d.name+'.csv')).open('w',newline='') as file:
                w=csv.DictWriter(file,fieldnames=rows[0].keys());w.writeheader();w.writerows(rows)
            n=sum(r['receivers'] for r in rows)
            result[d.name]=dict(frames=len(frames),gpu_snapshots=len(rows),receiver_observations=n,mean_reads=sum(r['reads'] for r in rows)/n,pcf_fallback=sum(r['pcf_fallback'] for r in rows),failed_footprints=sum(r['failed_footprints'] for r in rows),final_unavailable=sum(r['unavailable'] for r in rows),missing=sum(r['missing'] for r in rows),overflow_max=max(r['overflow'] for r in rows),requested_max=max(r['requested'] for r in rows),replay_over_1e3=sum(r['replay_over_1e3'] for r in rows),replay_mae=sum(r['replay_abs_sum'] for r in rows)/n,replay_max=max(r['replay_max'] for r in rows))
        shutil.copyfile(root/'view1024005_static'/'first.png',out/'first.png')
        report[revision]={'capture':str(root),'stages':result}
    report['matched_camera_jitter_256frame_phase_and_projection']=poses['before']==poses['after']
    (HERE/'scene-summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
    timing=[]
    for p in sorted((ROOT/'Temp~/smrt-path/timing').rglob('case_000_summary.csv')):
        for row in csv.DictReader(p.open()):
            timing.append(dict(revision=p.parents[2].name,**row))
    with (HERE/'timing.csv').open('w',newline='') as f:
        w=csv.DictWriter(f,fieldnames=timing[0].keys());w.writeheader();w.writerows(timing)
    for d in (ROOT/'Temp~/smrt-path/timing').iterdir():
        dest=HERE/'timing'/d.name
        if not dest.exists():shutil.copytree(d,dest)

if __name__=='__main__':main()
