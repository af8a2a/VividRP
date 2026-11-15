"""Qualify exact-geometry references without tolerances or discarded frames."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read


ARMS=("uniform-a","uniform-b")
FRAMES=range(512,1536)
AA_FRAMES=(*range(512,576),1535)
LUMA=np.array([.2126,.7152,.0722])


def runtime_source_identity(snapshot):
    rows=sorted((r for r in snapshot["files"] if r["path"].startswith(("Runtime/","Shaders/"))),key=lambda r:r["path"])
    assert rows,"Missing runtime/shader source identities"
    return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()


def qualify_variant(root):
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    trace_mode=protocol["traceMode"]
    assert trace_mode in ("SdfOnly","Hybrid")
    assert capture["schema"]=="screen-probe-exact-geometry-reference.v1"
    assert capture["complete"] and not capture["errors"] and load("source-verification.json")["unchanged"]
    assert (capture["warmup"],capture["measuredFrames"],capture["spacing"],capture["rays"])==(512,1024,16,8)
    assert capture["blockCounts"]==[64]*32 and protocol["shape"]==[32,68,120,5]
    for name,key in (("geometry-input.bin","geometryInputSha256"),("reference-tracing.compute.txt","tracingSourceSha256")):
        assert hashlib.sha256((root/name).read_bytes()).hexdigest()==protocol[key]
    geometry=np.fromfile(root/"geometry-input.bin","<f4").reshape(136,120,4)[:68]
    assert hashlib.sha256(geometry.tobytes()).hexdigest()==protocol["baseGeometrySha256"]
    programs=capture["referencePrograms"]
    assert len(programs)==10 and len({p["name"] for p in programs})==10
    assert all(p["bytes"]>0 and len(p["sha256"])==64 for p in programs)
    rows={(r["arm"],r["frame"],r["tag"]):r for r in capture["records"] if "tag" in r}
    assert len(rows)==sum("tag" in r for r in capture["records"])
    payload=0
    for row in capture["records"]:
        if not row.get("saved") and "aggregate" not in row:continue
        path=Path(row["path"]).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
        payload+=len(data)
    assert payload==capture["payloadBytes"]<=capture["payloadBudget"]==protocol["payloadBudget"]
    states={(r["arm"],r["frame"]):r for r in capture["records"] if "state" in r}
    assert set(states)=={(a,f) for a in ARMS for f in FRAMES}
    vp=next(iter(states.values()))["vp"]
    for arm in ARMS:
        for frame in FRAMES:
            state=states[arm,frame]["state"]
            if "traceMode" in state:assert state["traceMode"]==trace_mode
            assert states[arm,frame]["vp"]==vp
            assert (state["order"],state["batch"],state["samples"],state["seed"])==(8,32,64,0)
            assert not state["guide"] and not state["restir"] and state["sun"]==[0,0,0] and state["sky"]==0
            assert rows[arm,frame,"geometry"]["baseGeometrySha256"]==protocol["baseGeometrySha256"]
            assert rows[arm,frame,"normal"]["baseGeometrySha256"]==capture["referenceNormalHash"]
        for frame in AA_FRAMES:
            for kind in ("geometry","raw"):
                a,b=(rows[arm,frame,"aa-"+side+"-"+kind] for side in ("native","forced"))
                assert all(a[k]==b[k] for k in ("sha256","x","y","width","height","format"))
                assert (a["x"],a["y"],a["width"],a["height"])==(46,59,28,1)
    inputs={}
    for tag in ("input-gbuffer0","input-gbuffer1","radiosity"):
        hashes={rows[arm,f,tag]["sha256"] for arm in ARMS for f in (512,1535)}
        assert len(hashes)==1
        inputs[tag]=hashes.pop()
    assert inputs["radiosity"]=="40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"
    sums=np.fromfile(root/"block-sums.f64","<f8").reshape(protocol["shape"])
    squares=np.fromfile(root/"block-squares.f64","<f8").reshape(protocol["shape"])
    assert np.isfinite(sums).all() and np.isfinite(squares).all() and (squares>=0).all()
    assert (squares*64>=sums*sums-1e-7).all()
    verification=[]
    for arm,block in ((ARMS[0],0),(ARMS[1],16)):
        values=[]
        for f in range(512,576):
            raw=read(rows[arm,f,"raw"])[:68].astype(float)
            rgb=raw[...,:3]*raw[...,3:4]
            values.append(np.concatenate([rgb,raw[...,3:4],(rgb@LUMA)[...,None]],axis=-1))
        values=np.stack(values);a,b=values.sum(0),(values*values).sum(0)
        assert np.allclose(a,sums[block],rtol=1e-11,atol=1e-10)
        assert np.allclose(b,squares[block],rtol=1e-11,atol=1e-10)
        verification.append(dict(arm=arm,sumMaxError=float(abs(a-sums[block]).max()),squareMaxError=float(abs(b-squares[block]).max())))
    source=load("source-before-capture.json")
    result=dict(qualified=True,root=str(root),traceMode=trace_mode,
        runtimeSourceSha256=runtime_source_identity(source),
        tracingPrograms=capture["referencePrograms"],
        baseGeometrySha256=protocol["baseGeometrySha256"],normalSha256=capture["referenceNormalHash"],
        inputs=inputs,vp=vp,payloadBytes=payload,referenceRaysPerProbe=16384,geometryHashObservations=2048,normalHashObservations=2048,
        aaPairObservations=260,aaPointRgbaSamples=130*28,aggregationVerification=verification,
        targetMeanY=float((sums/64).mean(0)[59,46:74,4].mean()),
        limitations=["AA trust gate covers 28 recorded probe centres, not the entire grid",trace_mode+" raw current-frame reference; not a full-resolution or absolute Unreal reference",
            "Only TraceProbe receiver positions are forced; Hybrid screen tracing still uses native geometry and history inputs"])
    return result,geometry,sums/64


def target_lookup(variants):
    lookup={}
    for report,geometry,blocks in variants:
        for x in range(46,74):
            words=np.ascontiguousarray(geometry[59,x].astype("<f4")).view("<u4").tobytes()
            key=(x,words);value=blocks[:,59,x,4].copy()
            if key in lookup:
                assert np.array_equal(value,lookup[key]), "Same receiver/source/sequence has inconsistent reference blocks"
            else:lookup[key]=value
    return lookup


def match_target_geometry(lookup, geometry):
    keys=[(x,np.ascontiguousarray(geometry[59,x].astype("<f4")).view("<u4").tobytes()) for x in range(46,74)]
    missing=[x for x,key in zip(range(46,74),keys) if key not in lookup]
    return missing, None if missing else np.stack([lookup[key] for key in keys],axis=1)


def validate_variant_contracts(variants):
    for value in variants[1:]:
        for key in ("traceMode","runtimeSourceSha256","tracingPrograms","inputs","vp","normalSha256"):
            assert value[0][key]==variants[0][0][key],"Incompatible reference "+key


def reference_conflicts(variants):
    """Describe conflicting duplicates without averaging or dropping either source."""
    previous={};conflicts=[]
    for index,(_,geometry,blocks) in enumerate(variants):
        for x in range(46,74):
            words=np.ascontiguousarray(geometry[59,x].astype("<f4")).view("<u4").tobytes()
            key=(x,words);value=blocks[:,59,x,4]
            if key in previous:
                prior,baseline=previous[key]
                changed=np.flatnonzero(value!=baseline)
                if len(changed):
                    conflicts.append(dict(probeX=x,probeY=59,receiverWords=words.hex(),
                        variants=[prior,index],blocks=changed.tolist(),maxAbsBlockMeanDifference=float(abs(value-baseline).max())))
            else:previous[key]=(index,value.copy())
    return len(previous),conflicts


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("variants",type=Path,nargs="+")
    parser.add_argument("--comparison-capture",type=Path)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    variants=[qualify_variant(p.resolve()) for p in args.variants]
    validate_variant_contracts(variants)
    entries,conflicts=reference_conflicts(variants)
    if not conflicts:target_lookup(variants)
    keys={(x,np.ascontiguousarray(g[59,x].astype("<f4")).view("<u4").tobytes())
          for _,g,_ in variants for x in range(46,74)}
    comparison=None
    if args.comparison_capture:
        capture=json.loads((args.comparison_capture/"capture.json").read_text(encoding="utf-8-sig"))
        missing=[];observations=0
        for row in capture["records"]:
            if row.get("tag")!="geometry" or row["frame"]<1024:continue
            data=Path(row["path"]).read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
            geometry=np.frombuffer(data,"<f4").reshape(row["height"],row["width"],4)
            unmatched=[x for x in range(46,74) if (x,np.ascontiguousarray(geometry[59,x].astype("<f4")).view("<u4").tobytes()) not in keys]
            observations+=28
            missing.extend([row["arm"],row["frame"],x] for x in unmatched)
        assert observations>0,"No measured geometry frames in comparison capture"
        comparison=dict(receiverGeometryQualified=not missing,pointFrameObservations=observations,missing=missing,
            lightingComparisonQualified=False,note="Geometry coverage only; lighting mode and other source inputs still require their own qualification")
    result=dict(schema="screen-probe-exact-geometry-reference-analysis.v1",qualified=not conflicts,variants=[v[0] for v in variants],
        targetGeometryEntries=entries,conflictingReferences=conflicts,comparison=comparison)
    args.output.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))
    if conflicts:raise SystemExit(1)
