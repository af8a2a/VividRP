from pathlib import Path
import json,hashlib,numpy as np
root=Path.cwd();out=root/'Temp~/joint-sequence'
offsets=(128*np.mod(np.arange(8)[:,None]*[.754877666,.569840296],1)).astype(int)
assert len(set(map(tuple,offsets)))==8
report={'formula':'native: f % D; joint: (f + floor(f/8)) % D','jitter_phases':8,'frames':512,
        'pair_offsets':offsets.tolist(),'mapping':'Shared disk/receiver pairs 0/1. Per ray r uses pairs 2r/2r+1. Paired RG and radial strata preserved; no atlas regeneration.',
        'temporal_caveat':'Joint indexing skips one native slice at each jitter cycle boundary; native STBN temporal ordering is intentionally modified.','atlases':{}}
for name,depth in [('stbn_vec2',64),('fast_vec2',32)]:
    data=(root/'Temp~/public-noise'/f'{name}.raw').read_bytes()
    assert len(data)==128*128*depth*2
    a=np.frombuffer(data,np.uint8).reshape(depth,128,128,2).astype(np.float32)/255
    item={'sha256':hashlib.sha256(data).hexdigest(),'depth':depth,'sequences':{}}
    for joint in [False,True]:
        f=np.arange(512);indices=(f+(f//8 if joint else 0))%depth
        counts=[np.bincount(indices[j::8],minlength=depth).tolist() for j in range(8)]
        unique=[np.count_nonzero(c) for c in counts]
        assert unique==[depth if joint else depth//8]*8
        if joint:assert all(min(c)==max(c) for c in counts)
        item['sequences']['joint' if joint else 'native']={'different_slices_per_jitter':list(map(int,unique)), 'per_jitter_slice_counts':counts,'first32_indices':indices[:32].tolist()}
    # Match the shader's four ray marginal domains with all native atlas RG pairs.
    u=np.minimum(a,.99999994)
    item['radial_strata']=[{'ray':r,'radius_min':float(np.sqrt((r+u[...,0].min())/4)), 'radius_max':float(np.sqrt((r+u[...,0].max())/4))} for r in range(4)]
    report['atlases'][name]=item
(out/'sequence-audit.json').write_text(json.dumps(report,indent=2))
print({n:{k:v['different_slices_per_jitter'] for k,v in r['sequences'].items()} for n,r in report['atlases'].items()})
