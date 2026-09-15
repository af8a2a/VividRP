from pathlib import Path
import hashlib,json,shutil,struct,subprocess

root=Path.cwd();work=root/'Temp~/vsm-dynamic-timing'
archive=root/'Roadmap~/Experiments/VSMDynamicCache_20260915/timing'
archive.mkdir(parents=True,exist_ok=True)
for source_name,target_name in [('live-abba-v2','live'),('depth-oracle','depth-oracle'),('live-pilot','excluded/pilot-serialization'),('live-pilot-v2','pilot'),('live-abba','excluded/unsynchronized-motion')]:
    source=work/source_name;target=archive/target_name;target.mkdir(parents=True,exist_ok=True)
    for f in source.iterdir():
        if f.is_file() and f.suffix in ('.csv','.json','.txt'):shutil.copyfile(f,target/f.name)
scripts=archive/'scripts';scripts.mkdir(exist_ok=True)
for name in ['live.cs','live-abba.cs','live-abba-v2.cs','depth-oracle.cs','compare.compute.txt','prepare-v2.py','prepare-oracle.py','summarize.py','archive.py']:
    shutil.copyfile(work/name,scripts/(name+'.txt' if name.endswith('.cs') else name))
shutil.copyfile(Path('E:/VividRP_Reborn/Temp/VividRPDiagnostics/20260915_154443_cb39f29248a14aa487a9b8cf47adf8ad/snapshot.json'),archive/'initial-snapshot.json')
final=list((work/'final-snapshot').glob('*/snapshot.json'));assert len(final)==1
shutil.copyfile(final[0],archive/'final-snapshot.json')
summary=json.loads((archive/'live/summary.json').read_text(encoding='utf-8'))
assert summary['status']=='complete' and summary['restored'] and len(summary['stages'])==24
stages=summary['stages'];comparisons=[]
for i in range(0,24,4):
    group=stages[i:i+4];a=[group[0],group[3]];b=group[1:3]
    for s in group:
        assert s['otherCameraCallbacks']==0
        if s['scenario']==2:assert s['revisionAfter']-s['revisionBefore']==320 and s['dynamicDirty']>0
        if not s['force'] and s['scenario']!=2:assert s['dynamicDirty']==0
    assert len({tuple(s['counters']) for s in group})==1
    assert len({s['pressure'][0] for s in group})==1
    metrics={}
    for marker in group[0]['gpu']:
        va=[s['gpu'][marker]['p50_ms'] for s in a];vb=[s['gpu'][marker]['p50_ms'] for s in b]
        metrics[marker]=dict(forced_window_p50_ms=va,reuse_window_p50_ms=vb,
                            forced_p95_ms=[s['gpu'][marker]['p95_ms'] for s in a],reuse_p95_ms=[s['gpu'][marker]['p95_ms'] for s in b],
                            change_percent=(1-sum(vb)/sum(va))*100 if sum(va) else None)
    instances=lambda s:sum(s['args'][j+1] for j in range(0,len(s['args']),4))
    slots=lambda s:sum(s['args'][j]*s['args'][j+1] for j in range(0,len(s['args']),4))
    comparisons.append(dict(resolution=group[0]['resolution'],scenario=group[0]['scenario'],
                            allocated=group[0]['allocated'],primary=group[0]['pressure'][4:6],
                            pressure_bias=struct.unpack('f',struct.pack('I',group[0]['pressure'][0]))[0],
                            forced_dynamic_dirty=[s['dynamicDirty'] for s in a],reuse_dynamic_dirty=[s['dynamicDirty'] for s in b],
                            forced_instances=[instances(s) for s in a],reuse_instances=[instances(s) for s in b],
                            forced_vertex_slots=[slots(s) for s in a],reuse_vertex_slots=[slots(s) for s in b],metrics=metrics))
oracle=json.loads((archive/'depth-oracle/results.json').read_text())
assert len(oracle)==8 and all(s['ownerDifferences']==s['staticDifferences']==s['dynamicDifferences']==0 and s['valuesCheckedPerPool']>0 for s in oracle)
for folder in ['live','depth-oracle']:
    assert json.loads((archive/folder/'before.json').read_text())==json.loads((archive/folder/'after.json').read_text())
source_manifest=json.loads((root/'Roadmap~/Experiments/VSMDynamicCache_20260915/source-manifest.json').read_text())
for f in source_manifest['files']:
    assert hashlib.sha256((root/f['path']).read_bytes()).hexdigest()==f['final_sha256'],f['path']
result=dict(commit=subprocess.check_output(['git','rev-parse','HEAD']).decode().strip(),
            source_hashes_match_implementation=True,timing_windows=24,warmup_camera_callbacks=24*64,sampled_camera_callbacks=24*96,
            comparisons=comparisons,depth_cases=8,depth_values_checked_both_pools=sum(s['valuesCheckedPerPool']*2 for s in oracle),
            scene_restored=True)
(archive/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='comparisons'}))
