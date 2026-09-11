"""Independent analytic geometry vs GPU SMRT. Run from any directory; see README.
Only geometry visibility is an oracle. march() implements candidates, not truth.
"""
import argparse, csv, hashlib, json, math, struct
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
WORK = ROOT / "Temp~/smrt-geometry"
RULES = ("current", "slab_only", "surface", "halfspace")
SCALE = np.float32(.01)
RAYS = 64


def box(x0, x1, z0, z1, y0=-5., y1=5.):
    return ("box", (x0, y0, z0), (x1, y1, z1))


def plane(gx=0., gy=0., h=0., extent=7.9):
    return ("plane", gx, gy, h, extent)


SCENES = {
    "empty": [],
    "flat_receiver": [plane()],
    "slope_receiver": [plane(.5, .2)],
    "steep_receiver": [plane(4, .3)],
    "thin_sheet": [plane(), box(0, 5, 3.98, 4)],
    "solid_block": [plane(), box(0, 5, .2, 4)],
    "contact": [plane(), box(0, 5, .04, .07)],
    "far_sheet": [plane(), box(0, 5, 16, 16.02)],
    "thin_rod": [plane(), box(-.018, .018, 1, 3)],
    "disconnected": [plane(), box(-5, -.1, .98, 1), box(.1, 5, 2.98, 3)],
    "overlap": [plane(), box(-.4, .4, .98, 1), box(0, .9, 3.98, 4)],
    "sloped_sheet": [plane(), plane(.5, .2, 2, 2)],
    "gap_trap": [plane(), box(-5, 0, .98, 1), box(0, .14, 3.98, 4)],
    "gap_hidden": [plane(), box(-5, .14, .98, 1), box(0, .14, 3.98, 4)],
}


def intersect(shapes, origin, direction, limit):
    """Float64 line/AABB slabs and line/plane intersections; no depth/DDA access."""
    hit = np.zeros(len(origin), dtype=bool)
    for shape in shapes:
        if shape[0] == "box":
            low, high = np.array(shape[1]), np.array(shape[2])
            parallel = np.abs(direction) < 1e-15
            safe = np.where(parallel, 1., direction)
            a, b = (low-origin)/safe, (high-origin)/safe
            near = np.max(np.where(parallel, -np.inf, np.minimum(a, b)), axis=1)
            far = np.min(np.where(parallel, np.inf, np.maximum(a, b)), axis=1)
            inside = np.all(~parallel | ((origin >= low) & (origin <= high)), axis=1)
            hit |= inside & (far >= np.maximum(near, 1e-7)) & (near <= limit)
        else:
            _, gx, gy, h, extent = shape
            denominator = direction[:,2] - gx*direction[:,0] - gy*direction[:,1]
            safe = np.where(np.abs(denominator) < 1e-15, 1., denominator)
            t = (h + gx*origin[:,0] + gy*origin[:,1] - origin[:,2])/safe
            xy = origin[:,:2] + direction[:,:2]*t[:,None]
            hit |= (np.abs(denominator) >= 1e-15) & (t > 1e-7) & (t <= limit) & np.all(np.abs(xy) <= extent, axis=1)
    return hit


def truth(shapes, origin, direction, length):
    straight = intersect(shapes, origin, direction, 64.)
    tail_origin = origin + direction*length
    vertical = np.zeros_like(direction); vertical[:,2] = 1
    bent = intersect(shapes, origin, direction, length) | intersect(shapes, tail_origin, vertical, 64.)
    return straight, bent


def depths(shapes, resolution):
    axis = (np.arange(resolution)+.5)*(16./resolution)-8
    x, y = np.meshgrid(axis, axis)
    z = np.full(x.shape, -np.inf)
    for shape in shapes:
        if shape[0] == "box":
            low, high = shape[1:]
            covered = (x >= low[0]) & (x <= high[0]) & (y >= low[1]) & (y <= high[1])
            height = high[2]
        else:
            _, gx, gy, h, extent = shape
            covered = (abs(x) <= extent) & (abs(y) <= extent)
            height = gx*x + gy*y + h
        z = np.maximum(z, np.where(covered, height, -np.inf))
    return np.where(np.isfinite(z), .5+z*.01, 0).astype('<f4')


def inputs(name, resolution, diameter, budget, centered=True, max_length=10.):
    w = 16./resolution
    gx, gy = SCENES[name][0][1:3] if name in ("slope_receiver", "steep_receiver") else (0,0)
    x, y = np.meshgrid(np.linspace(-1.5, 1.5, 65)+.0037, [-.271, .037, .293])
    xy = np.stack((x.ravel(), y.ravel()), axis=1)
    if centered:
        xy = (np.floor((xy+8)/w)+.5)*w-8
    o = np.zeros((len(xy),4), dtype='<f4')
    o[:,:2] = (xy+8)/16
    # Deliberately tiny common axial bias; no normal bias or per-rule tuning.
    o[:,2] = .5 + (gx*xy[:,0]+gy*xy[:,1]+.01*w)*.01
    o = np.repeat(o,RAYS,axis=0)
    r = np.arange(RAYS)
    theta = 2*np.pi*np.mod(r*.6180339887498949+.137,1)
    disk = np.sqrt((r+.5)/RAYS)[:,None]*np.stack((np.cos(theta),np.sin(theta)),axis=1)
    tangent = math.tan(math.radians(diameter*.5))
    length = np.float32(min(max_length, (budget-2.001)*.70710678*w/tangent))
    rays = np.zeros_like(o)
    rays[:,:2] = np.tile(disk,(len(xy),1))*(tangent/w)
    rays[:,2] = length; rays[:,3] = .5*w
    # Decode precisely the serialized float32 inputs for the float64 truth rays.
    world = np.column_stack((o[:,:2].astype(float)*16-8, (o[:,2].astype(float)-.5)/float(SCALE)))
    direction = np.column_stack((rays[:,:2].astype(float)*w, np.ones(len(o))))
    return o, rays, world, direction


def march(depth, origins, rays, budget, rule):
    """Float32 model, cross-checked against all four GPU implementations."""
    n, resolution = len(origins), len(depth)
    start = origins[:,:2]*np.float32(resolution)
    cell = np.floor(start).astype(np.int32)
    direction = np.where(rays[:,:2] >= 0,1,-1)
    step = np.float32(1)/np.maximum(abs(rays[:,:2]),np.float32(1e-20))
    boundary = (cell+(direction>0)).astype(np.float32)
    times = abs(boundary-start)*step
    times[abs(rays[:,:2])<1e-10] = np.float32(1e20)
    enter = np.zeros(n,dtype=np.float32); previous = np.full(n,-1,dtype=np.float32)
    result = np.full(n,-1,dtype=np.int8)  # -1 unavailable, 0 lit, 1 occluded
    thickness = rays[:,3] if rule != 'surface' else np.zeros(n,dtype=np.float32)
    for _ in range(budget):
        active = result == -1
        inside = np.all((cell>=0)&(cell<resolution),axis=1)
        # All benchmark rays are guaranteed in map. Do not alias out-of-map to lit.
        assert np.all(inside[active])
        raw = depth[np.clip(cell[:,1],0,resolution-1),np.clip(cell[:,0],0,resolution-1)]
        surface = np.where(raw==0,np.float32(-1),(raw-origins[:,2])/SCALE)
        exit_time = np.minimum(times[:,0],times[:,1])
        tail = exit_time >= rays[:,2]
        hit = (surface > enter) & (tail | (surface-thickness <= exit_time))
        if rule == 'current':
            hit |= (raw!=0) & (surface>previous+thickness) & (previous>enter) & (previous-thickness<=np.minimum(exit_time,rays[:,2]))
        elif rule == 'halfspace':
            hit = surface > enter
        result[active & hit] = 1
        result[active & ~hit & tail] = 0
        previous = surface
        enter = exit_time
        cross = times <= times[:,::-1]
        cell += cross*direction
        times += cross*step
    return result


def triangle_distance(shapes, origin, direction):
    """Second geometric implementation: Moller-Trumbore over explicit triangles."""
    nearest=np.full(len(origin),np.inf)
    for shape in shapes:
        if shape[0]=='box':
            low,high=np.array(shape[1]),np.array(shape[2])
            vertices=np.array([[high[k] if (i>>k)&1 else low[k] for k in range(3)] for i in range(8)])
            quads=((0,1,3,2),(4,5,7,6),(0,1,5,4),(2,3,7,6),(0,2,6,4),(1,3,7,5))
        else:
            _,gx,gy,h,e=shape
            vertices=np.array([[x,y,h+gx*x+gy*y] for x,y in ((-e,-e),(e,-e),(e,e),(-e,e))])
            quads=((0,1,2,3),)
        for a,b,c,d in quads:
            for indices in ((a,b,c),(a,c,d)):
                v0,v1,v2=vertices[list(indices)];e1,e2=v1-v0,v2-v0
                p=np.cross(direction,e2);det=p@e1
                safe=np.where(abs(det)>1e-12,det,1)
                relative=origin-v0;u=np.sum(relative*p,axis=1)/safe
                q=np.cross(relative,e1);v=np.sum(direction*q,axis=1)/safe;t=q@e2/safe
                valid=(abs(det)>1e-12)&(u>=0)&(v>=0)&(u+v<=1)&(t>1e-7)
                nearest=np.minimum(nearest,np.where(valid,t,np.inf))
    return nearest


def oracle_checks():
    o = np.array([[-1.,0,0],[-1,0,0],[.5,0,0],[.5,0,3],[2,0,0]])
    d = np.array([[.5,0,1],[.1,0,1],[0,0,1],[0,0,1],[0,0,1]])
    np.testing.assert_array_equal(intersect([box(0,1,1,2)],o,d,64),[True,False,True,False,False])
    np.testing.assert_array_equal(intersect([plane(.5)],np.array([[0.,0,.1],[0,0,-.1]]),np.array([[0.,0,1],[0,0,1]]),64),[False,True])
    # Far thin sheet: straight and bounded geometry must intentionally disagree.
    a,b = truth([box(0,5,16,16.02)],np.array([[-.5,0,.001]]),np.array([[.04,0,1]]),2.)
    assert a[0] and not b[0]
    # Same top depth, distinct solid geometry: the oracle must distinguish them.
    # Leaving the silhouette before reaching the top is the ambiguous direction.
    o=np.array([[.1,0,.001]]);d=np.array([[-.04,0,1]])
    assert intersect([box(0,5,.2,4)],o,d,64)[0] and not intersect([box(0,5,3.98,4)],o,d,64)[0]
    assert np.array_equal(depths(SCENES['thin_sheet'],128),depths(SCENES['solid_block'],128))
    assert np.array_equal(depths(SCENES['gap_trap'],256),depths(SCENES['gap_hidden'],256))
    random=np.random.default_rng(20260911)
    for shapes in SCENES.values():
        origin=np.column_stack((random.uniform(-2,2,(1024,2)),np.full(1024,.001)))
        direction=np.column_stack((random.uniform(-.065,.065,(1024,2)),np.ones(1024)))
        np.testing.assert_array_equal(intersect(shapes,origin,direction,64),triangle_distance(shapes,origin,direction)<=64)
        # Independently raster-check 1024 texel centers from above the scene.
        pixels=random.integers(0,128,(1024,2))
        origin=np.column_stack(((pixels+.5)*.125-8,np.full(1024,64.)))
        direction=np.tile([0.,0.,-1.],(1024,1))
        t=triangle_distance(shapes,origin,direction)
        expected=np.where(np.isfinite(t),.5+(64-t)*.01,0).astype(np.float32)
        np.testing.assert_allclose(depths(shapes,128)[pixels[:,1],pixels[:,0]],expected,rtol=0,atol=1e-7)



def configurations():
    for name in SCENES:
        for res in (64,128,256,512):
            for angle in (.5,1.,7.1):
                for budget in (4,8):
                    for centered in ((True,False) if name in ('slope_receiver','steep_receiver') else (True,)):
                        yield dict(scene=name,resolution=res,diameter=angle,budget=budget,centered=centered,max_length=10.)
                        if name == "far_sheet":
                            yield dict(scene=name,resolution=res,diameter=angle,budget=budget,centered=centered,max_length=50.)


def write_csv(path, rows):
    with path.open('w',newline='',encoding='utf-8') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)


def prepare():
    oracle_checks(); WORK.mkdir(parents=True,exist_ok=True)
    # A new input set must not inherit success from an earlier GPU dispatch.
    for name in ('gpu.bin','gpu-status.txt','gpu-validation.json'):
        path=WORK/name
        if path.exists(): path.unlink()
    manifest=[]; results=[]; curves=[]
    with (WORK/'inputs.bin').open('wb') as f:
        configs=list(configurations());f.write(struct.pack('<i',len(configs)))
        for index,c in enumerate(configs):
            depth=depths(SCENES[c['scene']],c['resolution'])
            o,r,world,direction=inputs(c['scene'],c['resolution'],c['diameter'],c['budget'],c['centered'],c['max_length'])
            straight,bent=truth(SCENES[c['scene']],world,direction,float(r[0,2]))
            predictions=np.stack([march(depth,o,r,c['budget'],rule) for rule in RULES],axis=1)
            np.savez(WORK/f'{index:03}.npz',predictions=predictions,straight=straight,bent=bent)
            f.write(struct.pack('<iii',c['resolution'],len(o),c['budget']))
            f.write(depth.tobytes());f.write(o.tobytes());f.write(r.tobytes())
            manifest.append(dict(id=index,**c,rays=len(o),length=float(r[0,2]),world_texel=16/c['resolution'],depth_sha256=hashlib.sha256(depth.tobytes()).hexdigest(),path_disagreement=int(np.count_nonzero(straight!=bent))))
            for ri,rule in enumerate(RULES):
                p=predictions[:,ri];valid=p>=0
                for label,reference in [('straight',straight),('bent',bent)]:
                    receiver_valid=valid.reshape(-1,RAYS).all(axis=1)
                    v=(p==0).reshape(-1,RAYS).mean(axis=1)
                    ref=(~reference).reshape(-1,RAYS).mean(axis=1)
                    results.append(dict(id=index,**c,rule=rule,reference=label,rays=len(o),unavailable=int((~valid).sum()),truth_shadow=int(reference.sum()),false_shadow=int((valid&(p==1)&~reference).sum()),missed_shadow=int((valid&(p==0)&reference).sum()),visibility_mae=float(abs(v-ref)[receiver_valid].mean()),visibility_max=float(abs(v-ref)[receiver_valid].max())))
            if c['resolution']==256 and c['diameter']==7.1 and c['budget']==8 and c['centered'] and c['max_length']==10.:
                for j in range(65):
                    # Middle receiver row; exact same quadrature for every curve.
                    k=65+j;sl=slice(k*RAYS,(k+1)*RAYS)
                    curves.append(dict(scene=c['scene'],x=float(world[k*RAYS,0]),straight=float((~straight[sl]).mean()),bent=float((~bent[sl]).mean()),**{rule:float((predictions[sl,i]==0).mean()) for i,rule in enumerate(RULES)}))
    (WORK/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    write_csv(WORK/'metrics.csv',results);write_csv(WORK/'curves.csv',curves)
    print(json.dumps(dict(datasets=len(manifest),rays=sum(c['rays'] for c in manifest),oracle_checks='passed')))


def verify():
    manifest=json.loads((WORK/'manifest.json').read_text())
    mismatch=[]; total=0
    with (WORK/'gpu.bin').open('rb') as f:
        assert struct.unpack('<i',f.read(4))[0]==len(manifest)
        for c in manifest:
            n=c['rays']; actual=np.frombuffer(f.read(n*4*8),dtype='<f4').reshape(n,4,2)
            cpu=np.load(WORK/f"{c['id']:03}.npz")['predictions']
            gpu=np.where(actual[:,:,0]==0,-1,1-actual[:,:,1]).astype(np.int8)
            for i,rule in enumerate(RULES):
                bad=np.flatnonzero(cpu[:,i]!=gpu[:,i]);total+=n
                if len(bad):mismatch.append(dict(id=c['id'],rule=rule,count=len(bad),examples=bad[:8].tolist()))
        assert f.read()==b''
    report=dict(comparisons=total,mismatches=sum(c['count'] for c in mismatch),details=mismatch)
    (WORK/'gpu-validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
    assert not mismatch


if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['prepare','verify']);args=parser.parse_args()
    prepare() if args.action=='prepare' else verify()
