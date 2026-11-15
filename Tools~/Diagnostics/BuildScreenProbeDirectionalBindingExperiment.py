"""Prepare a short original-shader capture with bounded first-divergence inputs."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeRecorderCadenceExperiment import build as cadence_build


def build(template: str, output: Path) -> str:
    source = cadence_build(template, output)

    def replace(old: str, new: str) -> None:
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("const int warmup = 1024, flush = 128, measured = 512, drain = 64;",
            "const int warmup = 64, flush = 0, measured = 0, drain = 0;")
    replace("// Original-shader 2x2 recorder/cadence diagnostic. No performance comparison.",
            "// Original-shader short history binding capture. No GPU instrumentation or performance comparison.")
    replace("    long savedPayloadBytes=0;", """    long savedPayloadBytes=0, retainedPayloadBytes=0;
    const long retainedPayloadBudget=3L*1024*1024*1024;
    float[] V(Vector4 value) => new[]{value.x,value.y,value.z,value.w};
    var baselinePayloads=new Dictionary<string,byte[]>();
    var currentPayloads=new Dictionary<string,byte[]>();
    var payloadRows=new Dictionary<string,object>();
    var savedPayloads=new List<object>();
    var functionalDifferences=new List<(string arm,int frame,string tag)>();
    object firstBundle=null;
    bool Retain(string tag) => tag=="geometry" || tag=="raw" || tag=="composite"
        || tag=="directional-temporal" || tag=="previous-directional" || tag=="previous-geometry"
        || tag=="rays" || tag=="proposal";
    void SaveBundle(string name,int f,Dictionary<string,byte[]> payloads) {
        foreach(var entry in payloads.Where(p=>p.Key.StartsWith(f+"/",StringComparison.Ordinal))) {
            string key=entry.Key; string tag=key.Substring(key.IndexOf('/')+1);
            byte[] bytes=entry.Value;
            if(bytes.LongLength>savedPayloadBudget-savedPayloadBytes)
                throw new System.IO.IOException("First-divergence payload budget exceeded.");
            string path=output+"/"+name+"/"+f.ToString("0000")+"-"+tag+".bin";
            System.IO.File.WriteAllBytes(path,bytes); savedPayloadBytes+=bytes.LongLength;
            savedPayloads.Add(new {arm=name,frame=f,tag,path,bytes=bytes.Length,observation=payloadRows[name+"/"+key]});
        }
    }
    void SaveFirstBundle() {
        if(firstBundle!=null) return;
        var failures=functionalDifferences.Where(d=>d.arm==arm).OrderBy(d=>d.frame).ThenBy(d=>d.tag).ToArray();
        if(failures.Length==0) return;
        int f=failures[0].frame;
        foreach(int selected in new[]{Math.Max(0,f-1),f}.Distinct()) {
            SaveBundle("00-r0-t0",selected,baselinePayloads);
            SaveBundle(arm,selected,currentPayloads);
        }
        firstBundle=new {reference="00-r0-t0",repeated=arm,firstFrame=f,firstTags=failures.Where(d=>d.frame==f).Select(d=>d.tag).ToArray()};
    }""")
    start = source.index("                // Hash all observations; persist only the declared bounded list.")
    end = source.index("            } catch (Exception e)", start)
    replace(source[start:end], """                if(mismatch && (tag=="raw" || tag=="composite" || tag=="directional-temporal" || tag=="directional-filtered"))
                    functionalDifferences.Add((name,f,tag));
                var row=new {arm=name,frame=f,tag,width,height,format,sha256};
                records.Add(row);
                if(Retain(tag)) {
                    if(bytes.LongLength>retainedPayloadBudget-retainedPayloadBytes)
                        throw new System.IO.IOException("Owned in-memory payload budget exceeded.");
                    string payloadKey=f+"/"+tag;
                    (name.StartsWith("00-",StringComparison.Ordinal)?baselinePayloads:currentPayloads).Add(payloadKey,bytes);
                    retainedPayloadBytes+=bytes.LongLength;
                    payloadRows.Add(name+"/"+payloadKey,row);
                }
""")
    replace("if (frame<=16 || frame==warmup-1 || frame==lastFrame-1)", "if (true)")
    replace("        foreach(var mode in new[] { (false,false),(true,false),(true,true),(false,true),\n                (false,true),(true,true),(true,false),(false,false) })",
            "        foreach(var mode in Enumerable.Repeat((false,false),8))")
    replace("            tickIssued=tickCompleted=tickPending=0;", """            tickIssued=tickCompleted=tickPending=0;
            retainedPayloadBytes-=currentPayloads.Values.Sum(v=>(long)v.Length);
            currentPayloads.Clear();""")
    replace("            records.Add(new { arm,age,targetCount,otherCount,sourceFrames,recorderMode,tickMode,",
            """            SaveFirstBundle();
            records.Add(new { arm,age,targetCount,otherCount,sourceFrames,recorderMode,tickMode,""")
    replace("        complete=true; return new { output,complete,errors };", """        if(firstBundle==null) {
            foreach(int f in new[]{0,1,lastFrame-1}) SaveBundle("00-r0-t0",f,baselinePayloads);
            SaveBundle(arm,lastFrame-1,currentPayloads);
        }
        complete=true; return new { output,complete,errors,firstBundle };""")
    replace('schema="screen-probe-recorder-cadence.v1"', 'schema="screen-probe-directional-binding.v1"')
    replace("savedPayloadBudget,savedPayloadBytes,",
            "savedPayloadBudget,savedPayloadBytes,retainedPayloadBudget,retainedPayloadBytes,firstBundle,savedPayloads,")
    # These are CPU-side identities/selected constants. No native-pointer calls or new GPU commands.
    replace('        if (frame<warmup || frame==lastFrame-1) {', """        {
            object State(string n) => currentState.GetType().GetField(n,inst|System.Reflection.BindingFlags.Public).GetValue(currentState);
            bool Valid(string n) => Resolve(n)!=null;
            bool useScreen=(bool)Field("m_HasScreenHistory") && Valid("m_ScreenRays") && Valid("m_ScreenHzb") && Valid("m_ScreenMotion");
            if(new[]{"m_ReceiverTraceKernel","m_ReceiverCachedKernel","m_ReceiverCachedSunKernel"}.Any(n=>(int)Field(n)<0))
                throw new InvalidOperationException("Unsupported receiver-kernel layout for history predicate reconstruction.");
            var receiver=(TextureHandle)Field("m_ReceiverVisibility");
            bool hasReceiver=(bool)Field("m_UseSdf") && receiver.IsValid() && (int)Field("m_ReceiverCount")>0;
            bool correct=hasReceiver && (bool)Field("m_CorrectReceiverSelfHits");
            bool screenCard=hasReceiver && useScreen && (bool)Field("m_ScreenCardLightingRequested") && Valid("m_RadiosityInput");
            bool pixelTemporal=Field("m_PixelHistory")!=null && Field("m_PixelGeometryHistory")!=null
                && new[]{"m_ScreenMotion","m_RawResolved","m_CurrentPixelLighting","m_CurrentPixelGeometry",
                    "m_PreviousPixelLighting","m_PreviousPixelGeometry"}.All(Valid);
            object clipmap=Field("m_GlobalClipmap");
            bool global=false;
            if(clipmap!=null) {
                var ct=clipmap.GetType(); var cf=ct.GetField("Recorded",inst|System.Reflection.BindingFlags.Public);
                global=(bool)(cf!=null?cf.GetValue(clipmap):ct.GetProperty("Recorded",inst|System.Reflection.BindingFlags.Public).GetValue(clipmap));
            }
            global&=(bool)Field("m_UseSdf");
            bool history=(bool)Field("m_HasHistory") && Valid("m_PreviousGeometry")
                && ((bool)Field("m_DirectionalCache") || Valid("m_PreviousProbes"))
                && (bool)State("HasScreenRays")==useScreen && (bool)State("PixelTemporal")==pixelTemporal
                && (bool)State("RestirOpenPbrDiffuse")== (bool)Field("m_RestirOpenPbrDiffuse")
                && (bool)State("CorrectReceiverSelfHits")==correct && (bool)State("ScreenCardLighting")==screenCard
                && (bool)State("GlobalClipmapUsed")==global;
            bool directionalHistory=(bool)Field("m_DirectionalCache") && history
                && (bool)Field("m_DirectionalHistoryValid") && Valid("m_PreviousDirectional");
            var directionalOwner=(CameraHistoryTexture)Field("m_DirectionalHistory");
            var geometryOwner=(CameraHistoryTexture)Field("m_GeometryHistory");
            var bindingNames=new[]{"m_Geometry","m_PreviousGeometry","m_CurrentDirectional","m_DirectionalTemporal","m_PreviousDirectional",
                "m_CurrentDirectionalFiltered","m_PreviousDirectionalFiltered"};
            records.Add(new {arm,frame,bindings=bindingNames.Select(n=>new {field=n,id=Resolve(n).GetEntityId().ToString(),name=Resolve(n).name}).ToArray(),
                historyIds=new {directionalCurrent=directionalOwner.GetCurrent().rt.GetEntityId().ToString(),directionalPrevious=directionalOwner.GetPrevious().rt.GetEntityId().ToString(),
                    geometryCurrent=geometryOwner.GetCurrent().rt.GetEntityId().ToString(),geometryPrevious=geometryOwner.GetPrevious().rt.GetEntityId().ToString()},
                predicates=new {useScreen,hasReceiver,correct,screenCard,pixelTemporal,global,history,directionalHistory,
                    directionalValid=Field("m_DirectionalHistoryValid"),guideValid=Field("m_DirectionalGuideHistoryValid"),
                    previousScreen=State("HasScreenRays"),previousPixel=State("PixelTemporal"),previousCorrect=State("CorrectReceiverSelfHits"),
                    previousCard=State("ScreenCardLighting"),previousGlobal=State("GlobalClipmapUsed")},
                constants=new {
                    previousVP=Enumerable.Range(0,16).Select(i=>((Matrix4x4)State("ViewProjection"))[i/4,i%4]).ToArray(),
                    screenSize=new[]{1920f,1080f,1f/1920,1f/1080},
                    cameraForward=V((Vector4)Field("m_ProbeCameraForward")),
                    historyLightScale=V((bool)type.GetProperty("PreserveRelitHistory",inst).GetValue(pass)?(Vector4)Field("m_RadiosityHistoryScale"):Vector4.one),
                    restirDense=(bool)Field("m_RestirPixelIntegration"),adaptive=(bool)Field("m_AdaptiveProbes"),flags=hooks["EditorGatherHistoryDiagnostics"].GetValue(null)
                }});
        }
        if (frame<warmup || frame==lastFrame-1) {""")
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs")
    source = build(template.read_text(encoding="utf-8-sig"), args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps({
        "schema": "screen-probe-directional-binding-protocol.v1", "arms": 8, "frames": 64,
        "shaderChanged": False, "gpuInstrumentation": False, "firstPreviousFrameRead": 0,
        "allFramePreviousReads": True, "ownedPayloadBudget": 3*1024**3, "savedPayloadBudget": 192*1024**2,
        "note": "CPU-selected predicates and texture identities, not a claim of physical GPU bindings/constants"
    }, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"root": str(args.output.resolve())}))
