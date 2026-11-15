"""Locate observed Hybrid differences without treating non-reproduction as a fix."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read


def analyze(root):
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    assert capture["schema"]=="screen-probe-hybrid-input-witness.v1"
    assert capture["complete"] and not capture["errors"] and load("source-verification.json")["unchanged"]
    assert (capture["warmup"],capture["measuredFrames"],capture["spacing"],capture["rays"])==(832,64,16,8)
    assert capture["blockCounts"]==[64,64]
    for name,digest in protocol["files"].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    rows={(r["arm"],r["frame"],r["tag"]):r for r in capture["records"] if "tag" in r}
    assert len(rows)==sum("tag" in r for r in capture["records"])
    matrices={(r["arm"],r["frame"]):r["inputMatrices"] for r in capture["records"] if "inputMatrices" in r}
    states={(r["arm"],r["frame"]):r["state"] for r in capture["records"] if "state" in r}
    expected={(a,f) for a in protocol["arms"] for f in range(832,896)}
    assert set(states)==set(matrices)==expected
    payload=0
    for row in capture["records"]:
        if not row.get("saved") and "aggregate" not in row:continue
        path=Path(row["path"]).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
        payload+=len(data)
    assert payload==capture["payloadBytes"]<=capture["payloadBudget"]==protocol["payloadBudget"]
    geometry=[np.fromfile(root/name,"<f4").reshape(136,120,4) for name in ("geometry-input.bin","geometry-input-1.bin")]
    common=np.all(geometry[0][59,46:74].view("u4")==geometry[1][59,46:74].view("u4"),axis=1)
    common_rays=np.repeat(common,8)
    differences={tag:[] for tag in ("input-hzb","input-motion","input-screen-radiance","input-screen-geometry","input-gbuffer0","input-gbuffer1")}
    raw_differences=[];ray_differences={tag:[] for tag in ("screen-rays","ray-diagnostics","first-hit")};matrix_differences=[]
    aa=0;source=set()
    for frame in range(832,896):
        for index,arm in enumerate(protocol["arms"]):
            state=states[arm,frame]
            assert state["traceMode"]=="Hybrid" and not state["restir"] and not state["guide"]
            assert (state["order"],state["batch"],state["samples"],state["seed"])==(8,32,64,0)
            assert rows[arm,frame,"geometry"]["baseGeometrySha256"]==protocol["baseGeometrySha256"][index]
            assert rows[arm,frame,"normal"]["baseGeometrySha256"]==capture["referenceNormalHash"]
            for tag in ("geometry","raw"):
                assert rows[arm,frame,"aa-native-"+tag]["sha256"]==rows[arm,frame,"aa-forced-"+tag]["sha256"]
                aa+=1
        a,b=protocol["arms"]
        for tag,frames in differences.items():
            if rows[a,frame,tag]["sha256"]!=rows[b,frame,tag]["sha256"]:frames.append(frame)
        if matrices[a,frame]!=matrices[b,frame]:matrix_differences.append(frame)
        first,second=(read(rows[arm,frame,"raw"])[59,46:74] for arm in (a,b))
        changed=np.any(first.view("u4")!=second.view("u4"),axis=1)&common
        if np.any(changed):raw_differences.append(dict(frame=frame,probes=(np.flatnonzero(changed)+46).tolist()))
        for tag,frames in ray_differences.items():
            first,second=(read(rows[arm,frame,tag])[:,common_rays] for arm in (a,b))
            if not np.array_equal(first.view("u4"),second.view("u4")):frames.append(frame)
    for arm in protocol["arms"]:
        source.update(rows[arm,f,"radiosity"]["sha256"] for f in (832,895))
    assert source=={"40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"}
    sums=np.fromfile(root/"block-sums.f64","<f8").reshape(2,68,120,5)/64
    return dict(schema="screen-probe-hybrid-input-witness-analysis.v1",qualified=True,payloadBytes=payload,
        commonProbeXs=(np.flatnonzero(common)+46).tolist(),aaPairObservations=aa,inputDifferenceFrames=differences,
        matrixDifferenceFrames=matrix_differences,commonReceiverRawDifferences=raw_differences,
        commonReceiverRayDifferences=ray_differences,targetBlockMeanY=sums[:,59,46:74,4].mean(1).tolist(),
        commonReceiverBlockMeanDifferences=(sums[0,59,46:74,4]-sums[1,59,46:74,4])[common].tolist(),
        priorConflictReproduced=bool(raw_differences),priorJointReferenceQualified=False,
        limitations=["One measured 64-frame window; matching inputs do not explain an older unreproduced conflict",
            "CPU matrix bits are recorded; previous inverse VP is explicitly recomputed, not a GPU constant-buffer witness",
            "No performance or production-quality improvement claim"])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("root",type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve())
    (args.root/"analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))
