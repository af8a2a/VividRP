"""Keep native tracing mode and diagnostic-shader conformance separate from energy truth."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeGeometryReference import qualify_variant, runtime_source_identity, LUMA
from TemporalSamplingExperiment import read


def analyze(root, sdf_reference, hybrid_reference):
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    source_identity=runtime_source_identity(load("source-before-capture.json"))
    assert capture["schema"]=="screen-probe-native-trace-mode-conformance.v1"
    assert capture["complete"] and not capture["errors"] and load("source-verification.json")["unchanged"]
    assert protocol["frameBase"]==7919 and protocol["originalTraceShader"]
    assert hashlib.sha256((root/"capture-script.cs").read_bytes()).hexdigest()==protocol["scriptSha256"]
    assert (capture["warmup"],capture["measuredFrames"],capture["spacing"],capture["rays"])==(512,64,16,8)
    assert capture["blockCounts"]==[64,64]
    rows={(r["arm"],r["frame"],r["tag"]):r for r in capture["records"] if "tag" in r}
    states={(r["arm"],r["frame"]):r for r in capture["records"] if "state" in r}
    assert set(states)=={(a,f) for a in ("sdf-only","hybrid") for f in range(512,576)}
    payload=0
    for row in capture["records"]:
        if not row.get("saved") and "aggregate" not in row:continue
        path=Path(row["path"]).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
        payload+=len(data)
    assert payload==capture["payloadBytes"]<=capture["payloadBudget"]==protocol["payloadBudget"]
    sums=np.fromfile(root/"block-sums.f64","<f8").reshape(2,68,120,5)
    squares=np.fromfile(root/"block-squares.f64","<f8").reshape(2,68,120,5)
    result=[]
    for index,(arm,mode,reference) in enumerate((("sdf-only","SdfOnly",sdf_reference),("hybrid","Hybrid",hybrid_reference))):
        report,geometry,_=qualify_variant(reference)
        assert report["traceMode"]==mode
        assert report["runtimeSourceSha256"]==source_identity,"Native/reference runtime or shader sources differ"
        rp=json.loads((reference/"protocol.json").read_text(encoding="utf-8-sig"))
        assert rp["frameBases"][0]==protocol["frameBase"]
        rc=json.loads((reference/"capture.json").read_text(encoding="utf-8-sig"))
        aa={(v["frame"],v["tag"]):v for v in rc["records"] if v.get("arm")=="uniform-a" and v.get("frame",0)<576 and "tag" in v}
        values=[];differences=[];geometry_equal=0
        for frame in range(512,576):
            state=states[arm,frame]["state"]
            assert state["traceMode"]==mode and state["useSdf"] and not state["guide"] and not state["restir"]
            assert (state["order"],state["batch"],state["samples"],state["seed"])==(8,32,64,0)
            assert states[arm,frame]["vp"]==report["vp"] and state["sun"]==[0,0,0] and state["sky"]==0
            assert rows[arm,frame,"geometry"]["baseGeometrySha256"]==report["baseGeometrySha256"]
            native=read(rows[arm,frame,"raw"])
            ref=read(aa[frame,"aa-native-raw"])[0]
            assert (rows[arm,frame,"raw"]["format"],aa[frame,"aa-native-raw"]["format"])==("R16G16B16A16_SFloat",)*2
            if not np.array_equal(native[59,46:74].view("u4"),ref.view("u4")):differences.append(frame)
            ref_geometry=read(aa[frame,"aa-native-geometry"])[0]
            if np.array_equal(ref_geometry.view("u4"),geometry[59,46:74].view("u4")):geometry_equal+=1
            consumed=native[:68,:,:3].astype(np.float64)*native[:68,:,3:4]
            values.append(np.concatenate((consumed,native[:68,:,3:4],(consumed@LUMA)[...,None]),axis=-1))
        for tag in ("input-gbuffer0","input-gbuffer1","radiosity"):
            assert {rows[arm,f,tag]["sha256"] for f in (512,575)}=={report["inputs"][tag]}
        values=np.stack(values)
        assert np.allclose(values.sum(0),sums[index],rtol=1e-11,atol=1e-10)
        assert np.allclose((values*values).sum(0),squares[index],rtol=1e-11,atol=1e-10)
        result.append(dict(arm=arm,traceMode=mode,rawPointRgbaSamples=64*28,rawDifferences=differences,
            rawConformanceQualified=not differences,geometryMatchedFrames=geometry_equal,geometryFrames=64,
            targetMeanY=float(values[:,59,46:74,4].mean())))
    return dict(schema="screen-probe-native-trace-mode-conformance-analysis.v1",qualified=all(v["rawConformanceQualified"] for v in result),
        arms=result,payloadBytes=payload,lightingReferenceQualified=False,
        limitations=["Short output conformance only; no converged energy or performance claim",
            "Native and reference captures can have distinct one-ULP receiver geometry even when observed raw outputs match"])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root",type=Path)
    parser.add_argument("--sdf-reference",type=Path,required=True)
    parser.add_argument("--hybrid-reference",type=Path,required=True)
    args=parser.parse_args()
    result=analyze(args.root.resolve(),args.sdf_reference.resolve(),args.hybrid_reference.resolve())
    (args.root/"analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))
    if not result["qualified"]:raise SystemExit(1)
