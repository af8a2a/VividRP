"""Check private temporal inheritance at clear disocclusions on shared inputs."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeColorHistoryReplay import analyze as qualify
from GatherHistoryExperiment import unpack_pixel_history
from GatherHistoryMotion import world
from ProbeAnchorCalibration import LUMA, normal_from_packed
from TemporalSamplingExperiment import read


def exposed_mask(geometry, vp, region, coverage):
    x,y,width,height=region
    yy,xx=np.mgrid[y:y+height,x:x+width]
    pixels=np.stack([xx,yy],-1)
    depth=(geometry&65535).astype("<u2").view("<f2").astype(float)
    normals=normal_from_packed((geometry>>16).astype(float))
    inverse=np.linalg.inv(vp)
    exposed=np.zeros(depth.shape,bool)
    for i in range(1,len(depth)):
        pos=world(pixels,depth[i],inverse[i])
        clip=np.concatenate([pos,np.ones((*pos.shape[:-1],1))],-1)@vp[i-1].T
        projected=clip[...,:3]/clip[...,3:4]
        nearest=np.rint(np.stack([(projected[...,0]+1)*960-.5,(1-projected[...,1])*540-.5],-1)).astype(int)
        valid=((depth[i]>0)&(clip[...,3]>0)&(nearest[...,0]>=x)&(nearest[...,0]<x+width)
               &(nearest[...,1]>=y)&(nearest[...,1]<y+height))
        ix,iy=np.clip(nearest[...,0]-x,0,width-1),np.clip(nearest[...,1]-y,0,height-1)
        old_depth=depth[i-1,iy,ix]
        delta=world(nearest,old_depth,inverse[i-1])-world(nearest,projected[...,2],inverse[i-1])
        plane=np.maximum(abs(np.sum(delta*normals[i],-1)),abs(np.sum(delta*normals[i-1,iy,ix],-1)))
        exposed[i]=(valid&(old_depth>projected[...,2])&(plane>.075)
                    &(np.linalg.norm(delta,axis=-1)>.10)&(coverage[i]>0))
    return exposed


def analyze(root):
    common=qualify(root)
    assert common["qualified"] and common["nativeBaselineQualified"]
    capture=json.loads((root/"capture.json").read_text(encoding="utf-8-sig"))
    protocol=json.loads((root/"protocol.json").read_text(encoding="utf-8"))
    assert protocol["motion"] and protocol["measuredFrames"]==128
    frames=range(protocol["warmup"],protocol["warmup"]+128)
    rows={(r["frame"],r["tag"],r.get("side","source")):r for r in capture["records"] if "tag" in r}
    states={r["frame"]:r for r in capture["records"] if "state" in r}
    vp=np.array([states[f]["vp"] for f in frames]).reshape(128,4,4)
    assert np.max(abs(vp-vp[0]))>.01,"Fixed trajectory did not move"

    def images(role,side):
        return np.stack([read(rows[f,role+"-edge",side]) for f in frames])

    geometry=images("geometry","baseline")[...,0]
    assert np.array_equal(geometry,images("geometry","candidate")[...,0])
    current=images("resolved","source").astype(float)
    current_y=(current[...,:3]*current[...,3:4])@LUMA
    mask=exposed_mask(geometry,vp,protocol["motionRegion"],current[...,3])
    assert mask.any(),"Trajectory did not exercise clear disocclusion"
    result={}
    for side in ("baseline","candidate"):
        temporal=images("temporal",side).astype(float)
        final=images("final",side).astype(float)
        history=images("history",side)
        age,_,status=unpack_pixel_history(history[...,3].astype("<f2"))
        error=abs(((temporal[...,:3]*temporal[...,3:4])@LUMA)-current_y)[mask]
        final_error=abs(((final[...,:3]*final[...,3:4])@LUMA)-current_y)[mask]
        result[side]=dict(samples=int(mask.sum()),frames=int(mask.any((1,2)).sum()),
            oldAgeFraction=float((age[mask]>1).mean()),acceptedHistoryFraction=float((status[mask]==1).mean()),
            temporalMinusCurrentP99=float(np.quantile(error,.99)),temporalMinusCurrentMax=float(error.max()),
            finalMinusCurrentP99=float(np.quantile(final_error,.99)),finalMinusCurrentMax=float(final_error.max()))
    a,b=result["baseline"],result["candidate"];gates=protocol["gates"]
    p99=b["temporalMinusCurrentP99"]<=a["temporalMinusCurrentP99"]*(1+gates["disocclusionP99IncreasePercent"]/100)+1e-6
    maximum=b["temporalMinusCurrentMax"]<=a["temporalMinusCurrentMax"]*(1+gates["disocclusionMaxIncreasePercent"]/100)+1e-6
    age=b["oldAgeFraction"]<=a["oldAgeFraction"]+gates["newlyExposedOldAgeFractionIncrease"]
    return dict(schema="screen-probe-private-color-history-motion-analysis.v1",qualified=True,
        commonQualification={k:v for k,v in common.items() if k not in ("regions","crossPathStateDifferences","initialState")},
        arms=result,disocclusionP99Passed=p99,disocclusionMaxPassed=maximum,disocclusionAgePassed=age,
        motionPassed=p99 and maximum and age,productionQualified=False,
        limits=["Temporal minus current Resolve isolates inherited history; current Resolve is noisy and is not an absolute lighting truth.",
                "Final minus current includes spatial filtering and is descriptive, not a temporal-history gate.",
                "One 128-frame fixed cycle, conservative clear-foreground disocclusion mask; no claim about arbitrary motion or dynamic lighting."])


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("root",type=Path);args=parser.parse_args()
    result=analyze(args.root.resolve())
    (args.root/"motion-analysis.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))
