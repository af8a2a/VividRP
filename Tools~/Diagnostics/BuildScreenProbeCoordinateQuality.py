"""Build bounded static/moving final-pixel controls for temporal precision."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeRecorderCadenceExperiment import build as cadence_build


def build(template, output):
    source = cadence_build(template, output)

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one original fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("const int warmup = 1024, flush = 128, measured = 512, drain = 64;",
            "const int warmup = 1024, flush = 0, measured = 128, drain = 0;")
    replace('"EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride",',
            '"EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride",')
    replace('    var cameraExtra = camera.GetComponent<VividAdditionalCameraData>();',
            '    var cameraExtra = camera.GetComponent<VividAdditionalCameraData>();\n    var originalPose=(camera.transform.position,camera.transform.rotation);\n    bool moving=false;')
    start = source.index("    void Read(CommandBuffer cmd,")
    end = source.index("    void Begin(", start)
    replace(source[start:end], """    void ReadRegion(CommandBuffer cmd,Texture texture,string tag,int x=0,int y=0,int width=0,int height=0,bool save=true) {
        if(texture==null)throw new InvalidOperationException("Missing texture: "+tag);
        if(width==0)width=texture.width;if(height==0)height=texture.height;
        string name=arm;int f=frame;string format=texture.graphicsFormat.ToString();
        string path=output+"/"+name+"/"+f.ToString("0000")+"-"+tag+".bin";
        pending++;
        cmd.RequestAsyncReadback(texture,0,x,width,y,height,0,1,request=> {
            try {
                if(request.hasError)throw new InvalidOperationException("Readback failed: "+path);
                var bytes=request.GetData<byte>().ToArray();
                using var hash=System.Security.Cryptography.SHA256.Create();
                string sha256=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                if(save) {
                    if(bytes.LongLength>savedPayloadBudget-savedPayloadBytes)throw new System.IO.IOException("Quality payload budget exceeded.");
                    System.IO.File.WriteAllBytes(path,bytes);savedPayloadBytes+=bytes.LongLength;
                }
                records.Add(new {arm=name,frame=f,tag,x,y,width,height,format,sha256,saved=save,path=save?path:null});
            }catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }
""")
    replace('        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + (phase==1 ? frame : 0));', """        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + (phase==1 ? frame : 0));
        float t=moving && phase==1 && frame>=warmup?Mathf.Sin(2f*Mathf.PI*(frame-warmup)/128f):0f;
        camera.transform.SetPositionAndRotation(originalPose.Item1+originalPose.Item2*new Vector3(.28f*t,0,.06f*t),
            originalPose.Item2*Quaternion.Euler(0,-7f*t,0));""")
    start = source.index("        if (frame<warmup || frame==lastFrame-1) {")
    end = source.index("        if(tickMode", start)
    replace(source[start:end], """        if(frame>=warmup) {
            var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
            bool endpoint=frame==warmup || frame==lastFrame-1;
            ReadRegion(cmd,geometry,"geometry",save:endpoint);
            ReadRegion(cmd,Resolve("m_HitLighting"),"raw",save:false);
            if(endpoint)ReadRegion(cmd,Resolve("m_RadiosityInput"),"radiosity");
            var vp=(Matrix4x4)Field("m_ViewProjection");
            records.Add(new {arm,frame,qualityState=new {
                moving,order=Field("m_RadiosityPublishedOrder"),batch=Field("m_RadiosityBatch"),updates=Field("m_RadiosityUpdateCards"),
                bias=Field("m_Bias"),anchorAge=Field("m_ProbeAnchorMaxAge"),atrous=Field("m_AtrousIterations"),
                vp=Enumerable.Range(0,16).Select(i=>vp[i/4,i%4]).ToArray(),
                pose=new[]{camera.transform.position.x,camera.transform.position.y,camera.transform.position.z}}});
        }
""")
    replace("    Action<ComputePassContext,Texture,Texture,Texture,Texture> linear =", """    Action<ComputePassContext,int,Texture> stage=(context,index,texture)=> {
        if(active && moving && phase==1 && frame>=warmup && index==0 && context.Get<VividCameraData>().camera==camera)
            ReadRegion((CommandBuffer)wrapped.GetValue(context.cmd),texture,"resolved-edge",880,414,160,128);
    };
    Action<ComputePassContext,Texture,Texture,Texture,Texture> linear =""")
    replace("        targetCount++;\n        if (++frame < lastFrame) return;", """        if(frame>=warmup) {
            var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
            if(moving) {
                ReadRegion(cmd,lighting,"final-edge",880,414,160,128);
                ReadRegion(cmd,Resolve("m_CurrentPixelGeometry"),"geometry-edge",880,414,160,128);
            } else {
                ReadRegion(cmd,lighting,"final-ceiling",912,970,96,48);
                ReadRegion(cmd,lighting,"final-floor",912,130,96,64);
                ReadRegion(cmd,lighting,"final-back",1100,600,96,64);
                if(arm.StartsWith("00-",StringComparison.Ordinal) && frame==lastFrame-1)ReadRegion(cmd,lighting,"full-final");
            }
        }
        targetCount++;
        if (++frame < lastFrame) return;""")
    replace('hooks["EditorLinearCapture"].SetValue(null,linear);', 'hooks["EditorLinearCapture"].SetValue(null,linear); hooks["EditorGatherStageCapture"].SetValue(null,stage);')
    replace("        foreach(var mode in new[] { (false,false),(true,false),(true,true),(false,true),\n                (false,true),(true,true),(true,false),(false,false) })",
            "        foreach(var mode in Enumerable.Repeat((false,false),4))")
    replace('            tickIssued=tickCompleted=tickPending=0;', """            tickIssued=tickCompleted=tickPending=0;moving=index>2;
            camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2);cameraExtra.ResetPostProcessingHistory();
            int normalStart=Time.frameCount;double normalDeadline=EditorApplication.timeSinceStartup+30;
            while(Time.frameCount<normalStart+3) {
                if(EditorApplication.isPaused || EditorApplication.timeSinceStartup>normalDeadline)throw new InvalidOperationException("Could not flush the recentered camera.");
                await System.Threading.Tasks.Task.Delay(16);
            }""")
    replace('            AsyncGPUReadback.WaitAllRequests(); volume.profile=previousProfile;',
            '            AsyncGPUReadback.WaitAllRequests(); camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2); cameraExtra.ResetPostProcessingHistory(); volume.profile=previousProfile;')
    replace('schema="screen-probe-recorder-cadence.v1"', 'schema="screen-probe-coordinate-quality.v1"')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-coordinate-quality-protocol.v1",
        arms=["static-a","static-b","motion-a","motion-b"],warmup=1024,measured=128,spacing=16,rays=8,
        trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7),
        staticRegions=dict(ceiling=[912,970,96,48],floor=[912,130,96,64],back=[1100,600,96,64]),
        motionRegion=[880,414,160,128],savedPayloadBudget=192*1024**2,
        interpretation="Consumed scene-linear GI; require fixed source, paired geometry/trajectory and within-configuration replicates before quality attribution"),indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
