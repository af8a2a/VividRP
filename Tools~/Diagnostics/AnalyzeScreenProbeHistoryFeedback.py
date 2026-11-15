"""Measure a color-history recurrence pilot; keep production promotion gated."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read,metrics,paired


ARMS=("baseline-a","baseline-b","feedback")
ROIS=("ceiling","floor","back","edge")
LUMA=np.array([.2126,.7152,.0722])


def analyze(root):
    assert not (root/"raw-retention.json").exists(),"Raw sequence archived; use its retained analysis and retention audit"
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    method=protocol.get("method","wavelet")
    assert capture["schema"]=="screen-probe-color-history-feedback-pilot.v1"
    assert capture["complete"] and not capture["errors"] and load("source-verification.json")["unchanged"]
    assert (capture["warmup"],capture["measuredFrames"],capture["spacing"],capture["rays"])==(1024,256,16,8)
    assert capture["blockCounts"]==[64]*12
    for name,key in (("feedback.compute.txt","shaderSha256"),("capture-script.cs","scriptSha256")):
        assert hashlib.sha256((root/name).read_bytes()).hexdigest()==protocol[key]
    rows={(r["arm"],r["frame"],r["tag"]):r for r in capture["records"] if "tag" in r}
    assert len(rows)==sum("tag" in r for r in capture["records"])
    states={(r["arm"],r["frame"]):r["state"] for r in capture["records"] if "state" in r}
    expected={(a,f) for a in ARMS for f in range(1024,1280)};assert set(states)==expected
    payload=0
    for row in capture["records"]:
        if not row.get("saved") and "aggregate" not in row:continue
        path=Path(row["path"]).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
        payload+=len(data)
    assert payload==capture["payloadBytes"]<=capture["payloadBudget"]==protocol["payloadBudget"]
    sources=set();configuration=set()
    for (arm,frame),state in states.items():
        assert state["traceMode"]=="Hybrid" and not state["restir"] and state["feedback"]==(arm=="feedback")
        assert (state["order"],state["batch"],state["samples"],state["seed"])==(8,32,64,0)
        assert (state["atrousIterations"],state["anchorMaxAge"],state["pixelMaxAge"])==(4,512,32)
        configuration.add(state["guide"])
    for arm in ARMS:
        sources.update(rows[arm,f,"radiosity"]["sha256"] for f in (1024,1279))
    assert len(configuration)==1 and sources=={"40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"}
    inputs={}
    for arm in ARMS[1:]:
        inputs[arm]={tag:[f for f in range(1024,1280) if rows[ARMS[0],f,tag]["sha256"]!=rows[arm,f,tag]["sha256"]]
            for tag in ("raw","geometry")}
    histories=[]
    for arm in ARMS:
        for frame in (*range(1024,1280,16),1279):
            before,after,source=(read(rows[arm,frame,tag]) for tag in ("history-before","history-after","feedback-source"))
            assert np.array_equal(before[...,3].view("u4"),after[...,3].view("u4")),"Packed history changed"
            if arm!="feedback":assert np.array_equal(before.view("u4"),after.view("u4"))
            elif method=="wavelet":
                # The captured pilot's decoder always returns a positive age;
                # alpha still remains unchanged, including any invalid history.
                expected_rgb=(source[...,:3]*source[...,3:4]).astype("<f2").astype(np.float32)
                valid=source[...,3:4]>0
                if protocol.get("validHistoryPredicate"):valid&=before[...,3:4]>0
                expected_rgb=np.where(valid,expected_rgb,before[...,:3])
                assert np.array_equal(after[...,:3].view("u4"),expected_rgb.view("u4")),"Feedback energy encoding differs"
            histories.append(dict(arm=arm,frame=frame,packedAlphaExact=True,
                beforeMeanY=float((before[...,:3]@LUMA).mean()),afterMeanY=float((after[...,:3]@LUMA).mean())))
    global_energy=[]
    if method.startswith("conservative"):
        energy={(r["arm"],r["frame"],r["energyTag"]):r for r in capture["records"] if "energyTag" in r}
        for arm in ARMS:
            for frame in (*range(1024,1280,16),1279):
                before,after=(energy[arm,frame,tag] for tag in ("before","after"))
                assert before["validPixels"]==after["validPixels"]>0
                b=np.array([before[k] for k in ("red","green","blue")]);a=np.array([after[k] for k in ("red","green","blue")])
                delta=100*(a/b-1)
                global_energy.append(dict(arm=arm,frame=frame,channelChangePercent=delta.tolist()))
        assert max(abs(v) for row in global_energy for v in row["channelChangePercent"])<=protocol["gates"]["globalFeedbackEnergyAbsPercent"],"Global feedback energy changed"
    moment_witnesses=[]
    if method=="conservative-moments":
        for frame in (*range(1024,1280,16),1279):
            before,after=(read(rows["feedback",frame,t]) for t in ("moments-before","moments-after"))
            assert np.array_equal(before[...,1:].view("u4"),after[...,1:].view("u4")),"Moment variance/age/coverage changed"
            moment_witnesses.append(dict(frame=frame,varianceAgeCoverageExact=True,meanChangedPixels=int(np.count_nonzero(before[...,0]!=after[...,0]))))
    values={};reports={}
    for roi in ROIS:
        values[roi]={}
        for arm in ARMS:
            samples=np.stack([read(rows[arm,f,"final-"+roi]).astype(float) for f in range(1024,1280)])
            assert ((samples[...,3]>=0)&(samples[...,3]<=1)).all()
            values[roi][arm]=(samples[...,:3]*samples[...,3:4])@LUMA
        reports[roi]=dict(arms={a:metrics(v) for a,v in values[roi].items()},
            baselineRepeatability=paired(values[roi][ARMS[0]],values[roi][ARMS[1]]),
            feedbackVsBaselines={a:paired(values[roi][a],values[roi]["feedback"]) for a in ARMS[:2]})
    gates=protocol["gates"]
    noise=all(reports["ceiling"]["feedbackVsBaselines"][a]["variance_reduction_percent"]>=gates["ceilingVarianceReductionPercent"] for a in ARMS[:2])
    energy=all(abs(reports[roi]["feedbackVsBaselines"][a]["mean_change_percent"])<=gates["meanChangeAbsPercent"] for roi in ROIS for a in ARMS[:2])
    raw_blocks=np.fromfile(root/"block-sums.f64","<f8").reshape(3,4,68,120,5)/64
    return dict(schema="screen-probe-color-history-feedback-pilot-analysis.v1",qualified=True,payloadBytes=payload,
        method=method,guided=configuration.pop(),inputDifferenceFrames=inputs,historyWitnesses=histories,globalEnergyWitnesses=global_energy,momentWitnesses=moment_witnesses,regions=reports,
        inputConformanceQualified=all(not frames for tags in inputs.values() for frames in tags.values()),
        rawTargetMeanY={a:float(raw_blocks[i,:,59,46:74,4].mean()) for i,a in enumerate(ARMS)},
        pilotNoiseGatePassed=noise,pilotMeanGatePassed=energy,pilotPassed=noise and energy,
        productionQualified=False,unverified=["Independent sequence","Motion/disocclusion and edge color","Qualified high-sample energy reference","GPU overhead"],
        limitations=["Static same-workload pilot; raw/geometry differences are reported, not discarded",
            "Temporal variance and paired resampling are not absolute physical GI truth",
            "Pairing aligns nominal frame IDs; separate runs do not guarantee identical stochastic probe inputs",
            "The original wavelet pilot had an invalid-history predicate issue; its packed zero alpha was preserved and the prototype was rejected"])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("root",type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve());(args.root/"analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(qualified=result["qualified"],pilotPassed=result["pilotPassed"],guided=result["guided"],
        regions={k:v["feedbackVsBaselines"] for k,v in result["regions"].items()},productionQualified=False),indent=2))
