"""Prepare a bounded four-frame capture through the existing PIX session owner."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeDirectionalBindingExperiment import build as binding_build


SOURCE_GATE = """
class GiPixFourFrameSourceGate : IVividCaptureObserver
{
    public IVividCaptureObserver Inner;
    public Camera Target;
    public Func<bool> Start;
    public bool Prepared,Started;
    public int PreparationFrame,Ended;
    public System.Exception Error;
    public readonly List<int> CameraFrames=new List<int>();
    public void BeginCamera(ScriptableRenderContext context,Camera camera,int frame)
    {
        if(camera!=Target) {if(Started)Inner.BeginCamera(context,camera,frame);return;}
        try {
            if(!Prepared) {PreparationFrame=frame;Inner.BeginCamera(context,camera,frame);return;}
            if(!Started && !Start())return;
            Started=true;CameraFrames.Add(frame);Inner.BeginCamera(context,camera,frame);
        } catch(System.Exception e) {Error=e;}
    }
    public void EndCamera(ScriptableRenderContext context,Camera camera,int frame,VividCaptureOutcome result)
    {
        if(camera!=Target) {if(Started)Inner.EndCamera(context,camera,frame,result);return;}
        try {
            if(!Prepared) {Inner.EndCamera(context,camera,frame,result);Prepared=true;return;}
            if(!Started)return;
            Inner.EndCamera(context,camera,frame,result);
            if(++Ended==4) {Target.enabled=false;VividCaptureHooks.Release(this);}
        } catch(System.Exception e) {Error=e;}
    }
    public void CameraSkipped(Camera camera,VividCaptureOutcome result) {Inner.CameraSkipped(camera,result);}
}

"""


def build(template: str, output: Path, marker: str, pix: str, analyzer: str) -> str:
    source = binding_build(template, output)

    def replace(old: str, new: str) -> None:
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("using Unity.Profiling;", "using Unity.Profiling;\nusing VividRP.AgenticDebugger;")
    replace("// Original-shader short history binding capture. No GPU instrumentation or performance comparison.",
            "// Original-shader four-frame PIX capture. Capture boundaries alter scheduling.")
    replace("using VividRP.Editor.Tools;\n{", "using VividRP.Editor.Tools;\n" + SOURCE_GATE + "{")
    replace("const int timingStart = warmup + flush, lastFrame = timingStart + measured + drain;",
            "const int timingStart = warmup + flush, lastFrame = 4;")
    replace("        foreach(int selected in new[]{Math.Max(0,f-1),f}.Distinct()) {\n            SaveBundle(\"00-r0-t0\",selected,baselinePayloads);\n            SaveBundle(arm,selected,currentPayloads);\n        }", "")
    replace("        foreach(var mode in Enumerable.Repeat((false,false),8))", "        foreach(var mode in Enumerable.Repeat((false,false),2))")
    replace("    var referenceHashes = new Dictionary<string,string>();", """    string pixSession=null;GiPixFourFrameSourceGate pixGate=null;
    var pixRecords=new List<object>();
    var sessionField=typeof(PixCaptureBridge).GetField("session",stat);
    var observerField=typeof(VividCaptureHooks).GetField("owner",stat);
    var referenceHashes = new Dictionary<string,string>();""")
    start = source.index("            double deadline=EditorApplication.timeSinceStartup+300;")
    end = source.index("            AsyncGPUReadback.WaitAllRequests();", start)
    replace(source[start:end], """            if(observerField.GetValue(null)!=null) throw new InvalidOperationException("Another capture observer is active.");
            camera.enabled=true;
            var pixState=PixCaptureBridge.Execute("capture",captureFrames:4,path:output+"/"+arm+"/epochs0-3.wpix",cameraName:camera.name,
                expectedPass:MARKER,timeoutSeconds:180,pixInstall:PIX_PATH,analyzerPath:ANALYZER_PATH);
            pixSession=(string)pixState["sessionId"];
            pixRecords.Add(new {arm,stage="requested",state=pixState});
            if(string.IsNullOrEmpty(pixSession)) throw new InvalidOperationException("PIX request failed: "+pixState);
            var session=sessionField.GetValue(null);
            var operations=session.GetType().GetField("operations",inst).GetValue(session);
            var inner=operations as IVividCaptureObserver;
            if(inner==null || !System.Object.ReferenceEquals(observerField.GetValue(null),inner))
                throw new InvalidOperationException("PIX observer ownership changed.");
            pixGate=new GiPixFourFrameSourceGate {Inner=inner,Target=camera,Start=()=>{
                if(!resetPending)return false;
                var state=PixCaptureBridge.Execute("status",sessionId:pixSession);
                if((string)state["state"]!="armed") throw new InvalidOperationException("PIX not armed before epoch0: "+state);
                return true;
            }};
            VividCaptureHooks.Release(inner);
            if(!VividCaptureHooks.TryAcquire(pixGate)) throw new InvalidOperationException("Unable to acquire owned source gate.");
            double deadline=EditorApplication.timeSinceStartup+180;
            while(EditorApplication.timeSinceStartup<deadline && errors.Count==0) {
                if(System.IO.File.Exists(output+"/cancel.txt")) throw new OperationCanceledException("Owner cancelled capture.");
                if(pixGate.Error!=null) throw pixGate.Error;
                pixState=PixCaptureBridge.Execute("status",sessionId:pixSession);
                string status=(string)pixState["state"];
                if(status=="ready" || status=="failed")break;
                await System.Threading.Tasks.Task.Delay(16);
            }
            active=false;
            pixRecords.Add(new {arm,stage="terminal",state=pixState,preparationFrame=pixGate.PreparationFrame,
                ended=pixGate.Ended,cameraFrames=pixGate.CameraFrames});
            System.IO.File.WriteAllText(output+"/"+arm+"/pix-status.json",pixState.ToString());
            if((string)pixState["state"]!="ready" || frame!=4 || pixGate.Ended!=4
                || (int)pixState["requestedCameraFrames"]!=4 || (int)pixState["submittedCameraFrames"]!=4)
                throw new InvalidOperationException("Four-frame PIX capture failed: "+pixState+" epoch="+frame);
            var released=PixCaptureBridge.Execute("release",sessionId:pixSession);
            pixRecords.Add(new {arm,stage="released",state=released});
            if((string)released["state"]!="idle") throw new InvalidOperationException("PIX release incomplete: "+released);
            pixSession=null;VividCaptureHooks.Release(pixGate);pixGate=null;
""".replace("MARKER", json.dumps(marker)).replace("PIX_PATH", json.dumps(pix)).replace("ANALYZER_PATH", json.dumps(analyzer)))
    replace("            SaveFirstBundle();", """            SaveFirstBundle();
            for(int selected=0;selected<lastFrame;++selected)
                SaveBundle(arm,selected,index==1?baselinePayloads:currentPayloads);""")
    replace("        if(firstBundle==null) {\n            foreach(int f in new[]{0,1,lastFrame-1}) SaveBundle(\"00-r0-t0\",f,baselinePayloads);\n            SaveBundle(arm,lastFrame-1,currentPayloads);\n        }", "")
    replace("            active=false; RenderPipelineManager.beginCameraRendering-=Begin; Application.logMessageReceived-=Log;", """            active=false;
            if(pixSession!=null) pixRecords.Add(new {stage="finally-release",state=PixCaptureBridge.Execute("release",sessionId:pixSession)});
            if(pixGate!=null) VividCaptureHooks.Release(pixGate);
            RenderPipelineManager.beginCameraRendering-=Begin; Application.logMessageReceived-=Log;""")
    replace('schema="screen-probe-directional-binding.v1"', 'schema="screen-probe-four-frame-pix.v1"')
    replace("readyFrames,recorderMode", "readyFrames,pixRecords,recorderMode")
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--marker", required=True)
    parser.add_argument("--pix", required=True)
    parser.add_argument("--analyzer", required=True)
    args = parser.parse_args()
    assert Path(args.pix).is_dir() and Path(args.analyzer).is_file()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs")
    source = build(template.read_text(encoding="utf-8-sig"), args.output.resolve(), args.marker,
                   Path(args.pix).as_posix(), Path(args.analyzer).as_posix())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-four-frame-pix-protocol.v1",
        marker=args.marker, pix=args.pix, analyzer=args.analyzer, arms=2, cameraFrames=4, epochs=[0,1,2,3],
        sourceFrames=264, shaderChanged=False, savedPayloadBudget=192*1024**2,
        attributionGate="Functional divergence inside captured window with matching predecessor and current inputs"),indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"root":str(args.output.resolve())}))
