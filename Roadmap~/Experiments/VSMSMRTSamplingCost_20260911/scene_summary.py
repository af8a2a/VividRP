"""Audit the matched 50 m Sponza runs; captures are not performance runs."""
from pathlib import Path
import csv,gzip,json,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
RUNS={'before':'20260911_141314_738','after':'20260911_144910_966'}

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
    comparison={}
    for name in report['after']['stages']:
        a=ROOT/'Temp~/vsm-general-captures'/RUNS['after']/name
        b=ROOT/'Temp~/vsm-general-captures'/RUNS['before']/name
        pixels=changed=large=0;absolute=maximum=0.;levels_changed=reads_changed=0
        for file in sorted(a.glob('frame_*_shadow.bin.gz')):
            x=np.frombuffer(gzip.decompress(file.read_bytes()),'<f2').astype(np.float32)
            y=np.frombuffer(gzip.decompress((b/file.name).read_bytes()),'<f2').astype(np.float32)
            assert len(x)==364000  # Captured 700 x 520 ROI, not the full output.
            d=abs(x-y);pixels+=len(x);changed+=int((d>0).sum());large+=int((d>.001).sum())
            absolute+=float(d.sum(dtype=np.float64));maximum=max(maximum,float(d.max()))
            i=int(file.name.split('_')[1])
            levels_changed+=int(np.any(raw(a,i,'debug0')!=raw(b,i,'debug0'),axis=1).sum())
            reads_changed+=int(np.any(raw(a,i,'debug4')!=raw(b,i,'debug4'),axis=1).sum())
        comparison[name]=dict(roi_pixels=pixels,changed=changed,over_1e3=large,mae=absolute/pixels,max=maximum,level_or_blend_changed=levels_changed,work_changed=reads_changed)
    (HERE/'shadow-comparison.json').write_text(json.dumps(comparison,indent=2));print(json.dumps(comparison,indent=2))

if __name__=='__main__':main()
