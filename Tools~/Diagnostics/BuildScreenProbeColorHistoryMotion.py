"""Exercise private pixel-history recurrence on the fixed Cornell motion path."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeColorHistoryReplay import build as replay_build


def build(output, sequence):
    source, shaders = replay_build(output, "stable-unclamped", sequence, 128)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace('    var volume = roots.SelectMany',
            '    var originalPose=(camera.transform.position,camera.transform.rotation);\n    var volume = roots.SelectMany')
    replace('var privateRois=new[]{("ceiling",912,970,96,48),("floor",912,130,96,64),("back",1100,600,96,64),("edge",880,414,160,128)};',
            'var privateRois=new[]{("edge",880,414,160,128)};')
    marker='        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));'
    replace(marker, marker+'''
        if(active && current==camera) {
            float t=phase==1 && frame>=warmup?Mathf.Sin(2f*Mathf.PI*(frame-warmup)/128f):0f;
            camera.transform.SetPositionAndRotation(originalPose.Item1+originalPose.Item2*new Vector3(.28f*t,0,.06f*t),
                originalPose.Item2*Quaternion.Euler(0,-7f*t,0));
        }''')
    replace('        foreach(var item in new[]{("temporal",PrivateResolve',
            '        if(frame>=warmup)ReadPrivate(context,PrivateResolve("m_RawResolved"),"resolved","source",false,true);\n        foreach(var item in new[]{("temporal",PrivateResolve')
    replace('path==0,frame==0 || frame==warmup || frame==warmup+count-1);',
            'path==0,frame==0 || (frame>=warmup && item.Item1!="moments"));')
    replace('                var bytes=request.GetData<byte>().ToArray();\n                if(full)', '''                var bytes=request.GetData<byte>().ToArray();
                if(role=="final" && side!="native" && f==warmup+32) {
                    string file=output+"/"+currentArm+"/"+f.ToString("0000")+"-"+side+"-full-image.bin";
                    Write(file,bytes);records.Add(new{arm=currentArm,frame=f,tag="full-image",side,format,width=fullWidth,height=fullHeight,sha256=Hash(bytes),saved=true,path=file});
                }
                if(full)''')
    replace('CleanupPrivateReplay();volume.profile = previousProfile;', '''CleanupPrivateReplay();
            camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2);
            var owner=type.GetField("m_Cameras",inst).GetValue(privatePass);
            owner.GetType().GetMethod("CleanupCamera").Invoke(owner,new object[]{camera});
            var histories=type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem").GetField("s_Histories",stat).GetValue(null);
            histories.GetType().GetMethod("CleanupCamera").Invoke(histories,new object[]{camera});
            volume.profile = previousProfile;''')
    return source, shaders


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output",type=Path)
    parser.add_argument("--sequence",type=int,default=1733)
    args=parser.parse_args();output=args.output.resolve()
    source,shaders=build(output,args.sequence)
    output.mkdir(parents=True,exist_ok=False)
    (output/"capture-script.cs").write_text(source,encoding="utf-8")
    for name,body in shaders.items():(output/name).write_text(body,encoding="utf-8")
    protocol=dict(schema="screen-probe-private-color-history-motion-protocol.v1",method="stable-unclamped",
        sequence=args.sequence,warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=256*1024**2,
        regions=["edge"],motion=True,metadataWitnesses=True,feedbackPublicationWitnesses=True,
        trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7),
        motionRegion=[880,414,160,128],
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(nativeBaselineByteExact=True,disocclusionP99IncreasePercent=5,disocclusionMaxIncreasePercent=5,
            newlyExposedOldAgeFractionIncrease=.01),
        note="One motion cycle; shared native Resolve and motion inputs. Private histories only. ROI pairs each frame and full-image baseline AA on measured frames. Camera restored and native camera histories discarded in finally.")
    (output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
