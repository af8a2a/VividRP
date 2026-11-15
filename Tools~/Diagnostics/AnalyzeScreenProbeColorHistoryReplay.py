"""Qualify full private recurrence before interpreting a feedback comparison."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from TemporalSamplingExperiment import read,paired,metrics


ROIS=("ceiling","floor","back","edge")
ROLES=("temporal","history","moments","geometry","final")
LUMA=np.array([.2126,.7152,.0722])


def publication_equal(before,after,role,full_metadata):
    if full_metadata:
        assert all(isinstance(row.get("metadataSha256"),str) and len(row["metadataSha256"])==64 for row in (before,after))
        return before["metadataSha256"]==after["metadataSha256"]
    # The first targeted witness predates full Y/Z/W metadata hashing, but
    # saved its complete half-float crops at every observed publication.
    a,b=(read(row).view("u4") for row in (before,after));start=3 if role=="history" else 1
    return np.array_equal(a[...,start:],b[...,start:])


def failure_details(capture):
    groups={}
    for row in capture["records"]:
        if "failureKey" in row:groups.setdefault(row["failureKey"],[]).append(row)
    result=[]
    for key,rows in sorted(groups.items(),key=lambda pair:(int(pair[0].split("/")[0]),pair[0])):
        if len(rows)!=2:continue
        a,b=rows
        assert a["format"]==b["format"] and a["width"]==b["width"] and a["height"]==b["height"]
        stride=8 if a["format"]=="R16G16B16A16_SFloat" else 4
        first,second=(Path(row["path"]).read_bytes() for row in rows)
        different=np.flatnonzero(np.frombuffer(first,np.uint8)!=np.frombuffer(second,np.uint8))
        pixel=int(different[0])//stride
        dtype="<f2" if stride==8 else "<u4";channels=4 if stride==8 else 1
        av,bv=(np.frombuffer(v,dtype).reshape(-1,channels) for v in (first,second))
        aw,bw=(np.frombuffer(v,"<u2" if stride==8 else "<u4").reshape(-1,channels) for v in (first,second))
        result.append(dict(key=key,frame=a["frame"],format=a["format"],byteDifferences=len(different),
            firstLocalPixel=[pixel%a["width"],pixel//a["width"]],
            sides=[a["side"],b["side"]],values=[av[pixel].astype(float).tolist(),bv[pixel].astype(float).tolist()],
            words=[aw[pixel].astype(int).tolist(),bw[pixel].astype(int).tolist()],
            maxAbsoluteDifference=float(np.max(np.abs(av.astype(float)-bv.astype(float)))),paths=[a["path"],b["path"]]))
    return result


def analyze(root):
    assert not (root/"raw-retention.json").exists(),"Raw sequence archived"
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    rois=tuple(protocol.get("regions",ROIS))
    assert rois and set(rois)<=set(ROIS)
    assert capture["schema"]=="screen-probe-private-color-history-replay.v1"
    assert load("source-verification.json")["unchanged"]
    for name,digest in protocol["files"].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    payload=0
    for row in capture["records"]:
        if not row.get("path"):continue
        path=Path(row["path"]).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row["sha256"]
        payload+=len(data)
    assert payload==capture["payloadBytes"]<=capture["payloadBudget"]==protocol["payloadBudget"]
    if not capture["complete"] or capture["errors"]:
        return dict(schema="screen-probe-private-color-history-replay-analysis.v1",qualified=False,
            errors=capture["errors"],firstFailures=failure_details(capture)[:12],productionQualified=False,
            note="Private baseline failed strict AA; no candidate quality comparison")
    warmup,count=protocol["warmup"],protocol["measuredFrames"]
    assert (warmup,capture["measuredFrames"],capture["spacing"],capture["rays"])==(1024,count,16,8)
    assert capture["blockCounts"]==[64]*(count//64)
    states={r["frame"]:r["state"] for r in capture["records"] if "state" in r}
    assert set(states)==set(range(warmup,warmup+count))
    for state in states.values():
        assert (state["order"],state["batch"],state["samples"],state["seed"])==(8,32,64,0)
        assert state["traceMode"]=="Hybrid" and not state["restir"] and state["guide"]
        assert (state["atrousIterations"],state["anchorMaxAge"],state["pixelMaxAge"])==(4,512,32)
    post_near=any(state.get("postNearOcclusion",False) for state in states.values())
    rows={(r["frame"],r["tag"],r.get("side","source")):r for r in capture["records"] if "tag" in r}
    assert len(rows)==sum("tag" in r for r in capture["records"])
    initial=[r for r in capture["records"] if r.get("privateInitialization")]
    assert len(initial)==1 and initial[0]["frame"]==0
    pairs=0;metadata_failures=[]
    for frame in range(warmup+count):
        for role in ROLES:
            for roi in rois:
                native,baseline=(rows[frame,role+"-"+roi,side] for side in ("native","baseline"))
                assert native["sha256"]==baseline["sha256"],(frame,role,roi)
                pairs+=1
                if protocol["method"]=="none":assert baseline["sha256"]==rows[frame,role+"-"+roi,"candidate"]["sha256"]
                if protocol.get("metadataWitnesses"):
                    candidate=rows[frame,role+"-"+roi,"candidate"]
                    if role in ("history","moments") and baseline["metadataSha256"]!=candidate["metadataSha256"]:
                        metadata_failures.append(dict(frame=frame,role=role,roi=roi,baseline=baseline["metadataSha256"],candidate=candidate["metadataSha256"]))
                    if role=="geometry":assert baseline["sha256"]==candidate["sha256"],(frame,roi,"geometry")
    full_frames=(*range(3),warmup-1,*range(warmup,warmup+count))
    for frame in full_frames:
        for role in ROLES:
            assert rows[frame,role+"-full","native"]["sha256"]==rows[frame,role+"-full","baseline"]["sha256"]
            pairs+=1
    assert pairs==capture["aaPairs"]
    # RectifyPixelHistory modifies fastUpdate from RGB, so independent histories
    # can naturally produce different ages. The publication itself must preserve
    # alpha and moment variance/age/coverage, not later native rejection choices.
    publication_frames=sorted({f for f,tag,side in rows if tag=="feedback-history-before-ceiling" and side=="candidate"})
    publication_failures=[]
    for frame in publication_frames:
        for roi in rois:
            for role in ("history","moments"):
                before,after=(rows[frame,"feedback-"+role+"-"+phase+"-"+roi,"candidate"] for phase in ("before","after"))
                if protocol.get("feedbackPublicationWitnesses"):
                    assert protocol["publicationMomentChannels"]=="variance-age-coverage"
                equal=publication_equal(before,after,role,protocol.get("feedbackPublicationWitnesses",False))
                if not equal:publication_failures.append(dict(frame=frame,role=role,roi=roi))
    publication_full=protocol["method"] in ("none","stable-unclamped") or publication_frames==list(range(warmup+count))
    if publication_failures:
        return dict(schema="screen-probe-private-color-history-replay-analysis.v1",qualified=False,nativeBaselineQualified=True,
            aaPairs=pairs,publicationFailures=publication_failures,productionQualified=False,
            note="Feedback publication directly changed protected metadata; no quality comparison")
    source={rows[f,"radiosity","source"]["sha256"] for f in (warmup,warmup+count-1)}
    assert source=={"40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8"}
    regions={}
    for roi in rois:
        values={}
        for side in ("baseline","candidate"):
            data=np.stack([read(rows[f,"final-"+roi,side]).astype(float) for f in range(warmup,warmup+count)])
            values[side]=(data[...,:3]*data[...,3:4])@LUMA
        regions[roi]=dict(arms={side:metrics(data) for side,data in values.items()},comparison=paired(values["baseline"],values["candidate"]))
    gates=protocol["gates"]
    motion=protocol.get("motion",False)
    noise=False if motion else regions["ceiling"]["comparison"]["variance_reduction_percent"]>=gates["ceilingVarianceReductionPercent"]
    energy=False if motion else all(abs(v["comparison"]["mean_change_percent"])<=gates["meanChangeAbsPercent"] for v in regions.values())
    return dict(schema="screen-probe-private-color-history-replay-analysis.v1",qualified=publication_full,nativeBaselineQualified=True,method=protocol["method"],
        payloadBytes=payload,aaPairs=pairs,fullFrameAaFrames=len(full_frames),roiAaFrames=warmup+count,
        postNearOcclusionAppliedByRenderer=post_near,
        crossPathStateDifferenceCount=len(metadata_failures),crossPathStateDifferences=metadata_failures[:24],
        publicationObservedFrames=len(publication_frames),publicationObservedExact=not publication_failures,publicationFullSequenceQualified=publication_full,
        initialState=initial[0],regions=regions,pilotNoiseGatePassed=noise,pilotMeanGatePassed=energy,
        staticNoiseComparisonApplicable=not motion,
        reconstructionGatesPassed=noise and energy,pilotPassed=publication_full and noise and energy and protocol["method"]!="none",productionQualified=False,
        limitations=["Candidate histories do not affect renderer sampling or source cache; production installation still requires independent sequence, motion, energy reference and GPU validation",
            "Final output is after four wavelet passes and before optional post-wavelet near occlusion"])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("root",type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve());(args.root/"analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result if not result["qualified"] else {k:v for k,v in result.items() if k not in ("regions","initialState")},indent=2))
    if not result["qualified"]:raise SystemExit(1)
