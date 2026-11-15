"""Qualify unchanged-program ray references and compare paired pixel energy."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeColorHistoryReplay import analyze as history_qualification
from ProbeAnchorCalibration import normal_from_packed
from TemporalSamplingExperiment import LUMA,read


def reason_fractions(a,b,coverage,sets):
    assert np.isfinite(a).all() and np.isfinite(b).all()
    assert (a>=0).all() and (b>=0).all()
    assert np.array_equal(a,np.rint(a)) and np.array_equal(b,np.rint(b))
    assert np.all(b[...,3]==sets*8)
    assert np.array_equal(a.sum(-1)+b[...,:3].sum(-1),b[...,3])
    assert np.max(abs(b[...,1]/(sets*8)-coverage))<1e-6
    names=("unblockedCompleteMiss","screenRejectedMiss","traceIncomplete","missingSurface",
        "backfaceRejected","accepted","other")
    values=np.concatenate([a,b[...,:3]],axis=-1).sum(tuple(range(a.ndim-1)))/(a.shape[0]*a.shape[1]*sets*8)
    return dict(zip(names,map(float,values)))


def black_boundary_margin(geometry,low,high,trace_range,bias):
    assert np.all(np.asarray(high)>=low) and trace_range>0 and bias>=0
    # Any possible biased origin is within |bias| of the stored receiver.
    # Use an all-directions enclosing radius, avoiding quantized ray normals.
    far=np.linalg.norm(np.maximum(abs(geometry[...,:3]-low),abs(geometry[...,:3]-high)),axis=-1)+bias
    return trace_range-far


def compare(reference,baseline,candidate):
    mean_reference=reference.mean(0)
    reference_energy=float(reference.mean())
    assert reference_energy>0
    result={}
    for name,data in (("baseline",baseline),("candidate",candidate)):
        error=data.mean(0)-mean_reference
        result[name]=dict(meanY=float(data.mean()),meanErrorPercent=100*float(error.mean())/reference_energy,
            meanMapMse=float(np.mean(error**2)),meanMapRms=float(np.sqrt(np.mean(error**2))),
            meanMapAbsoluteP99=float(np.quantile(abs(error),.99)))
    a,b=result["baseline"],result["candidate"]
    result.update(referenceMeanY=reference_energy,
        referenceBlock32StandardErrorRms=float(np.sqrt(reference.reshape(-1,32,reference.shape[-1]).mean(1).var(0,ddof=1).mean()/(len(reference)//32))),
        meanChangePercent=100*(b["meanY"]/a["meanY"]-1),
        addedAbsoluteMeanErrorPercent=abs(b["meanErrorPercent"])-abs(a["meanErrorPercent"]),
        meanMapMseChangePercent=100*(b["meanMapMse"]/max(a["meanMapMse"],1e-20)-1))
    return result


def analyze(root):
    common=history_qualification(root)
    if not common["qualified"]:
        return dict(schema="screen-probe-same-frame-energy-analysis.v1",qualified=False,
            historyQualification=common,productionQualified=False)
    load=lambda name:json.loads((root/name).read_text(encoding="utf-8-sig"))
    capture,protocol=load("capture.json"),load("protocol.json")
    assert protocol["schema"]=="screen-probe-same-frame-energy-protocol.v1"
    sets=protocol["setsPerFrame"];warmup=protocol["warmup"];count=protocol["measuredFrames"]
    assert (capture["energySets"],capture["energySeed"])==(sets,protocol["referenceSeed"])
    rows={(r["frame"],r["tag"],r.get("side","source")):r for r in capture["records"] if "tag" in r}
    states=[r for r in capture["records"] if "energySets" in r]
    assert {r["frame"] for r in states}==set(range(warmup,warmup+count))
    assert all(r["traceShader"]=="Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute" for r in states)
    for f in range(warmup,warmup+count):
        for role in ("screen","geometry","raw"):
            a,b=(rows[f,"energy-aa-"+role,side] for side in ("native","replay"))
            assert a["sha256"]==b["sha256"],(f,role)
    assert capture["energyAaPairs"]==3*count
    bounds={r["frame"]:r for r in capture["records"] if r.get("tag")=="energy-root-node"}
    if protocol.get("recordGpuBounds"):
        assert set(bounds)==set(range(warmup,warmup+count))
        for row in bounds.values():
            raw=np.fromfile(row["path"],"<f4")
            assert len(raw)==12 and np.array_equal(raw[:3].view("u4"),np.asarray(row["boundsMin"],"<f4").view("u4"))
            assert np.array_equal(raw[4:7].view("u4"),np.asarray(row["boundsMax"],"<f4").view("u4"))
            assert not row["skyTexturePresent"]
    regions={}
    for roi in protocol["targetRegions"]:
        def images(role,side):
            return np.stack([read(rows[f,"energy-"+role+"-"+roi,side])[0] for f in range(warmup,warmup+count)]).astype(float)
        geometry=images("geometry","native")
        assert np.array_equal(geometry,images("reference-geometry","source")),roi
        assert (geometry[...,3]>=0).all(),"Invalid reference receiver"
        # Sum already contains RGB * coverage. Do not multiply by its accumulated alpha again.
        reference=images("reference","source")/sets
        assert ((reference[...,3]>=0)&(reference[...,3]<=1)).all()
        baseline,candidate=(images("final",s) for s in ("baseline","candidate"))
        result=compare(reference[...,:3]@LUMA,(baseline[...,:3]*baseline[...,3:4])@LUMA,
            (candidate[...,:3]*candidate[...,3:4])@LUMA)
        result.update(points=geometry.shape[1],pointFrameObservations=geometry.shape[0]*geometry.shape[1],
            receiverPositionMin=geometry[...,:3].min((0,1)).tolist(),receiverPositionMax=geometry[...,:3].max((0,1)).tolist(),
            receiverNormals=np.unique(np.round(normal_from_packed(geometry[...,3]),2).reshape(-1,3),axis=0).tolist(),
            minimumReferenceCoverage=float(reference[...,3].min()),
            meanReferenceCoverage=float(reference[...,3].mean()),
            minimumPointMeanReferenceCoverage=float(reference[...,3].mean(0).min()),
            uniqueReceiverGeometryHashes=len({rows[f,"energy-geometry-"+roi,"native"]["sha256"] for f in range(warmup,warmup+count)}))
        regions[roi]=result
        if protocol.get("recordReasons"):
            result["referenceRayFractions"]=reason_fractions(images("reasons-a","source"),
                images("reasons-b","source"),reference[...,3],sets)
        if protocol.get("recordGpuBounds"):
            margins=np.stack([black_boundary_margin(geometry[i],bounds[f]["boundsMin"],bounds[f]["boundsMax"],
                bounds[f]["energyRange"],bounds[f]["energyBias"]) for i,f in enumerate(range(warmup,warmup+count))])
            eligible=margins>1e-3
            a=images("reasons-a","source")
            potential=reference[...,3]+a[...,0]/(sets*8)*eligible
            result["blackBoundaryProof"]=dict(allReceiverRangesEncloseScene=bool(eligible.all()),
                minimumRangeMarginMetres=float(margins.min()),
                knownBlackMissFraction=float((a[...,0]*eligible).sum()/(count*geometry.shape[1]*sets*8)),
                potentialMeanCoverage=float(potential.mean()),minimumPotentialPointMeanCoverage=float(potential.mean(0).min()),
                note="Counterfactual classification only. No reference RGB, renderer coverage, quality gate or production shader was changed.")
    gates=protocol["gates"]
    reference_qualified=sets*8*count>=16384
    coverage_qualified=all(r["minimumPointMeanReferenceCoverage"]>=protocol["gates"].get("minimumPointMeanReferenceCoverage",.99) for r in regions.values())
    energy=all(abs(r["meanChangePercent"])<=gates["meanChangeAbsPercent"] and
        r["addedAbsoluteMeanErrorPercent"]<=gates["addedMeanErrorPercentOfReference"] and
        r["meanMapMseChangePercent"]<=gates["addedReferenceMsePercent"] for r in regions.values())
    return dict(schema="screen-probe-same-frame-energy-analysis.v1",qualified=True,referenceAaQualified=True,
        referenceSampleCountQualified=reference_qualified,referenceRaysPerPoint=sets*8*count,
        referenceCoverageQualified=coverage_qualified,
        referenceAaFullImagePairs=capture["energyAaPairs"],
        historyQualification={k:v for k,v in common.items() if k not in ("regions","crossPathStateDifferences","initialState")},
        regions=regions,pairedEnergyComparisonPassed=energy,
        energyGatePassed=reference_qualified and coverage_qualified and energy,productionQualified=False,
        limitations=["Raw reference estimates incident E/pi at actual probe centres, not every full-resolution pixel.",
            "Each frame uses unchanged original screen/trace programs and native resources, with uniform directions for reference only.",
            "Reference shares Surface Cache and SDF approximation; it does not establish absolute physical accuracy or Unreal parity.",
            "Exact same-frame receiver matching does not repair the previous cross-run Hybrid reference conflict."])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("root",type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve())
    (args.root/"energy-analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))
    if not result["qualified"]:raise SystemExit(1)
