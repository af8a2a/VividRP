from pathlib import Path
import gzip,json,sys,numpy as np
root=Path(sys.argv[1]);out=Path('Temp~/joint-sequence/dynamic-analysis');out.mkdir(exist_ok=True)
names=['bnd','stbn_shared_joint']
def load(d,s,k):
    a=np.frombuffer(gzip.decompress((d/f'frame_{s:03}_{k}.bin.gz').read_bytes()),'<f2').reshape(495,400,-1)[::-1].astype(np.float32)
    return a[...,0] if k=='shadow' else a[...,:3]@np.array([.2126,.7152,.0722],np.float32)
for name in names:
    d=root/(name+'_static');dest=out/(name+'-moments.npz')
    if dest.exists() or not (d/'status.txt').exists():continue
    rec={r['step']:r for r in map(json.loads,(d/'frames.jsonl').read_text().splitlines())}
    assert set(rec)==set(range(544))
    keys=[tuple(rec[s]['jitter'].values()) for s in range(32,544)];unique=sorted(set(keys));assert len(unique)==8
    result={'jitters':np.array(unique)}
    for kind in ['shadow']:
        mean=np.zeros((8,495,400),np.float64);m2=np.zeros_like(mean);n=np.zeros(8,np.int64)
        for s,key in enumerate(keys):
            j=unique.index(key);a=load(d,s+32,kind);n[j]+=1;delta=a-mean[j];mean[j]+=delta/n[j];m2[j]+=delta*(a-mean[j])
        assert np.all(n==64)
        result[kind+'_phase_mean']=mean.astype(np.float32)
        result[kind+'_phase_variance']=(m2/n[:,None,None]).astype(np.float32)
    np.savez_compressed(dest,**result)
    print('moments',name,flush=True)
