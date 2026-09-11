"""Compare real production clipmap continuation with independent geometry."""
import argparse, csv, hashlib, json, math, struct, sys
from pathlib import Path
import numpy as np
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2];WORK=ROOT/'Temp~/smrt-path'
sys.path.insert(0,str(HERE.parent/'VSMSMRTGeometry_20260911'))
import geometry as reference


def configurations():
    for scene in reference.SCENES:
        for res in (64,128,256,512):
            for angle in (.5,1.,7.1):
                for budget in (4,8):
                    yield dict(scene=scene,resolution=res,diameter=angle,budget=budget,max_length=50.,scrolled=False,terminal_levels=0)
                    if res in (128,256) and angle==7.1:
                        yield dict(scene=scene,resolution=res,diameter=angle,budget=budget,max_length=50.,scrolled=True,terminal_levels=0)
                    if res==256 and angle==7.1:
                        yield dict(scene=scene,resolution=res,diameter=angle,budget=budget,max_length=50.,scrolled=False,terminal_levels=2)
                    if scene=='far_sheet' and res in (128,512) and angle==7.1:
                        yield dict(scene=scene,resolution=res,diameter=angle,budget=budget,max_length=50.,scrolled=False,terminal_levels=1)
                    if scene=='far_sheet':
                        yield dict(scene=scene,resolution=res,diameter=angle,budget=budget,max_length=10.,scrolled=False,terminal_levels=0)


def levels_for(c):
    tangent=np.float32(math.tan(math.radians(c['diameter']*.5)))
    radius=np.float32(np.float32(c['budget']-2.001)*np.float32(.70710678))
    levels=[]
    for level in range(16):
        w=np.float32(16/c['resolution']*(2**level))
        end=np.minimum(np.float32(c['max_length']),np.float32(radius*w/tangent))
        if level==c['terminal_levels']-1: end=np.float32(c['max_length'])
        # Whole-page scrolling; independent XY shifts and depth scale/translation.
        x=w*(c['resolution']//4) if c['scrolled'] and level%2 else 0.
        y=-x
        scale=np.float32(.005 if c['scrolled'] and level%2 else .01)
        offset=np.float32(.4 if c['scrolled'] and level%2 else .5)
        levels.append((w,end,x,y,scale,offset))
        if end>=c['max_length']:break
    assert levels[-1][1]>=c['max_length']
    return levels


def depth_for(shapes,res,level):
    w,_,cx,cy,scale,offset=level
    axis=(np.arange(res)+.5-res/2)*float(w)
    x,y=np.meshgrid(axis+cx,axis+cy);z=np.full(x.shape,-np.inf)
    for shape in shapes:
        if shape[0]=='box':
            low,high=shape[1:];covered=(x>=low[0])&(x<=high[0])&(y>=low[1])&(y<=high[1]);height=high[2]
        else:
            _,gx,gy,h,e=shape;covered=(abs(x)<=e)&(abs(y)<=e);height=gx*x+gy*y+h
        z=np.maximum(z,np.where(covered,height,-np.inf))
    return np.where(np.isfinite(z),float(offset)+z*float(scale),0).astype('<f4')


def trace(depths,origins,rays,budget,levels):
    n,res=len(origins),len(depths[0]);result=np.full(n,-1,dtype=np.int8);work=np.zeros(n,dtype=np.int32)
    visits=np.zeros((n,len(levels)),dtype=np.int32);begin=np.float32(0)
    for li,(w,end,cx,cy,zscale,zoffset) in enumerate(levels):
        scale=np.array([levels[0][0]/w,levels[0][0]/w,zscale/levels[0][4]],dtype=np.float32)
        o=(origins[:,:3]-np.array([.5,.5,.5],dtype=np.float32))*scale+np.array([.5-cx/(res*w),.5-cy/(res*w),zoffset],dtype=np.float32)
        slope=rays[:,:2]*scale[:2];depth_scale=np.float32(.01)*scale[2]
        start=o[:,:2]*np.float32(res)+slope*begin;cell=np.floor(start).astype(np.int32)
        sign=np.where(slope>=0,1,-1);cell-=((sign<0)&(start==cell))
        step=np.float32(1)/np.maximum(abs(slope),np.float32(1e-20))
        times=begin+abs((cell+(sign>0)).astype(np.float32)-start)*step
        times[abs(slope)<1e-10]=np.float32(1e20)
        enter=np.full(n,begin,dtype=np.float32);previous=np.full(n,-1,dtype=np.float32)
        done=result>=0
        segment_budget=budget
        if li==len(levels)-1:
            tangent=np.float32(np.max(np.linalg.norm(rays[:,:2],axis=1))*levels[0][0]/math.sqrt((reference.RAYS-.5)/reference.RAYS))
            segment_budget=max(budget,int(np.ceil((end-begin)*tangent/w*np.float32(1.41421357)+np.float32(.001)))+2)
        for _ in range(segment_budget):
            active=~done
            assert np.all(np.all((cell>=0)&(cell<res),axis=1)[active])
            work[active]+=1;visits[active,li]+=1
            raw=depths[li][np.clip(cell[:,1],0,res-1),np.clip(cell[:,0],0,res-1)]
            surface=np.where(raw==0,np.float32(-1),(raw-o[:,2])/depth_scale)
            exit_time=np.minimum(times[:,0],times[:,1]);at_end=exit_time>=end;tail=at_end&(li==len(levels)-1)
            exit_time=np.minimum(exit_time,end);thickness=np.float32(.5)*w
            hit=(surface>enter)&(tail|(surface-thickness<=exit_time))
            hit|=(raw!=0)&(surface>previous+thickness)&(previous>enter)&(previous-thickness<=exit_time)
            result[active&hit]=1
            if li==len(levels)-1:result[active&~hit&at_end]=0
            done|=hit|at_end
            previous=surface;enter=exit_time;cross=times<=times[:,::-1];cell+=cross*sign;times+=cross*step
        assert np.all(done),'Segment exhausted despite the phase-independent bound'
        begin=end
    return result,work,visits


def prepare():
    reference.oracle_checks();WORK.mkdir(exist_ok=True)
    for name in ('gpu.bin','gpu-status.txt','gpu-validation.json'):
        p=WORK/name
        if p.exists():p.unlink()
    manifest=[]
    with (WORK/'inputs.bin').open('wb') as f:
        configs=list(configurations());f.write(struct.pack('<i',len(configs)))
        for index,c in enumerate(configs):
            levels=levels_for(c)
            o,r,world,direction=reference.inputs(c['scene'],c['resolution'],c['diameter'],c['budget'],True,c['max_length'])
            depths=[depth_for(reference.SCENES[c['scene']],c['resolution'],l) for l in levels]
            straight,configured=reference.truth(reference.SCENES[c['scene']],world,direction,c['max_length'])
            old=reference.march(depths[0],o,r,c['budget'],'current')
            new,work,visits=trace(depths,o,r,c['budget'],levels)
            np.savez(WORK/f'{index:03}.npz',old=old,new=new,work=work,straight=straight,configured=configured,visits=visits)
            f.write(struct.pack('<iiii',c['resolution'],len(o),c['budget'],len(levels)))
            f.write(struct.pack('<ff',c['max_length'],math.tan(math.radians(c['diameter']*.5))))
            for level in levels:f.write(struct.pack('<ffffff',*level))
            for depth in depths:f.write(depth.tobytes())
            f.write(o.tobytes());f.write(r.tobytes())
            manifest.append(dict(id=index,**c,rays=len(o),levels=len(levels),endpoints=[float(l[1]) for l in levels],mean_reads=float(work.mean()),max_reads=int(work.max()),max_segment_reads=int(visits.max())))
    (WORK/'manifest.json').write_text(json.dumps(manifest,indent=2));print(dict(datasets=len(manifest),rays=sum(c['rays'] for c in manifest)))


def verify():
    manifest=json.loads((WORK/'manifest.json').read_text());rows=[];mismatches=[];work_mismatches=0
    with (WORK/'gpu.bin').open('rb') as f:
        assert struct.unpack('<i',f.read(4))[0]==len(manifest)
        for c in manifest:
            n=c['rays'];gpu=np.frombuffer(f.read(n*16),dtype='<f4').reshape(n,4);a=np.load(WORK/f"{c['id']:03}.npz")
            pred=np.where(gpu[:,0]==0,-1,1-gpu[:,1]).astype(np.int8)
            bad=np.flatnonzero(pred!=a['new'])
            if len(bad):mismatches.append(dict(id=c['id'],count=len(bad),examples=bad[:8].tolist()))
            assert np.all(gpu[:,3]==1),'Resident footprint unexpectedly unavailable'
            work_mismatches += int(np.count_nonzero(gpu[:,2] != a['work']))
            for rule,p in [('old',a['old']),('continuation',pred)]:
                for label in ('straight','configured'):
                    truth=a[label];valid=p>=0;v=(p==0).reshape(-1,64).mean(1);ref=(~truth).reshape(-1,64).mean(1)
                    rows.append(dict(**c,rule=rule,reference=label,unavailable=int((~valid).sum()),false_shadow=int(((p==1)&~truth).sum()),missed_shadow=int(((p==0)&truth).sum()),visibility_mae=float(abs(v-ref).mean()),visibility_max=float(abs(v-ref).max()),gpu_mean_reads=float(gpu[:,2].mean()) if rule=='continuation' else '',gpu_max_reads=int(gpu[:,2].max()) if rule=='continuation' else ''))
        assert f.read()==b''
    reference.write_csv(WORK/'metrics.csv',rows)
    report=dict(rays=sum(c['rays'] for c in manifest),mismatches=sum(r['count'] for r in mismatches),details=mismatches,work_mismatches=work_mismatches)
    (WORK/'gpu-validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));assert not mismatches and work_mismatches==0


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['prepare','verify']);args=parser.parse_args()
    prepare() if args.action=='prepare' else verify()
