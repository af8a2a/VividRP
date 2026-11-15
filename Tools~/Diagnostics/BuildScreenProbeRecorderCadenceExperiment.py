"""Build a bounded 2x2 observation-control script from the original timing harness.

This is a diagnostic experiment, not a renderer candidate or a GPU benchmark.
The generated C# runs through the live Unity script command; no assets are saved.
"""
import argparse
import hashlib
import json
from pathlib import Path


def build(template: str, output: Path) -> str:
    def replace(old: str, new: str) -> None:
        nonlocal template
        if template.count(old) != 1:
            raise ValueError(f"Expected exactly one template fragment: {old[:100]!r}")
        template = template.replace(old, new, 1)

    replace("// PlayMode-only steady GPU timing. Cold-epoch A/A, then two ABBA blocks.",
            "// Original-shader 2x2 recorder/cadence diagnostic. No performance comparison.")
    replace("const long savedPayloadBudget = 1024L * 1024 * 1024;",
            "const long savedPayloadBudget = 192L * 1024 * 1024;")
    replace('string output = "E:/VividRP_GI_Evidence/ScreenProbeAnchorGpuTiming-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");',
            "string output = " + json.dumps(output.as_posix()) + ";")
    replace("long savedPayloadBytes=0;", """long savedPayloadBytes=0;
    bool recorderMode=false, tickMode=false;
    int tickIssued=0, tickCompleted=0, tickPending=0;
    void Tick(CommandBuffer cmd, Texture geometry) {
        tickIssued++; tickPending++;
        cmd.RequestAsyncReadback(geometry,0,0,1,0,1,0,1,request => {
            try {
                if(request.hasError || request.GetData<byte>().Length!=16)
                    throw new InvalidOperationException("Geometry pulse readback failed.");
                tickCompleted++;
            } catch(Exception e) { errors.Add(e.Message); }
            finally { tickPending--; }
        });
    }""")
    start = template.index("                bool densePreflight=")
    end = template.index("                records.Add(new { arm=name", start)
    old = template[start:end]
    replace(old, """                bool compare=f<warmup || f==lastFrame-1;
                if(f==0 && tag.StartsWith("previous-",StringComparison.Ordinal)) compare=false;
                if(compare) {
                    if(name.StartsWith("00-",StringComparison.Ordinal)) referenceHashes.Add(key,sha256);
                    else mismatch=!referenceHashes.TryGetValue(key,out string expected) || expected!=sha256;
                }
                // Hash all observations; persist only the declared bounded list.
                save=(name.StartsWith("00-",StringComparison.Ordinal) && f<2
                        && (tag=="geometry" || tag=="raw" || tag=="composite" || tag=="directional-temporal"))
                    || ((f==warmup-1 || f==lastFrame-1)
                        && (tag=="geometry" || tag=="raw" || tag=="radiosity"))
                    || (f==lastFrame-1 && (tag=="proposal" || tag=="rays" || tag=="screen-rays"
                        || tag=="directional-temporal" || tag=="previous-directional-filtered"));
                if(save) {
                    if(bytes.LongLength>savedPayloadBudget-savedPayloadBytes)
                        throw new System.IO.IOException("Saved payload budget exceeded; capture stopped without deleting evidence.");
                    System.IO.File.WriteAllBytes(path,bytes); savedPayloadBytes+=bytes.LongLength;
                }
""")
    replace("sha256, saved=save||mismatch, path=save||mismatch?path:null",
            "sha256, mismatch, saved=save, path=save?path:null")
    replace("if (phase==1 && frame == timingStart)",
            "if (phase==1 && frame == timingStart && recorderMode)")
    replace('if ((arm=="00-half128" || arm=="01-half128") && frame<warmup-1)',
            "if (frame<warmup || frame==lastFrame-1)")
    replace("if (frame<=16)", "if (frame<=16 || frame==warmup-1 || frame==lastFrame-1)")
    replace('            Read(diagnostics,geometry,"geometry",save); Read(diagnostics,Resolve("m_HitLighting"),"raw",save);',
            '''            Read(diagnostics,geometry,"geometry",save); Read(diagnostics,Resolve("m_HitLighting"),"raw",save);
            if(frame==0 || frame==warmup-1 || frame==lastFrame-1)
                Read(diagnostics,Resolve("m_RadiosityInput"),"radiosity",false);''')
    replace("        if (frame != warmup-1 && frame != lastFrame-1) return;\n        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);\n        Read(cmd,geometry,\"geometry\"); Read(cmd,Resolve(\"m_HitLighting\"),\"raw\"); Read(cmd,Resolve(\"m_RadiosityInput\"),\"radiosity\");",
            """        if(tickMode && frame>=warmup && frame<lastFrame-1)
            Tick((CommandBuffer)wrapped.GetValue(context.cmd),geometry);""")
    replace("        for (int i=0;i<names.Length;++i) {\n            var samples",
            "        if(recorderMode) for (int i=0;i<names.Length;++i) {\n            var samples")
    replace("        foreach(int requestedAge in new[] {128,128,128,512,512,128,128,512,512,128}) {\n            age=requestedAge; arm=(index++).ToString(\"00\")+\"-\"+(age>128?\"float512\":\"half128\");",
            """        foreach(var mode in new[] { (false,false),(true,false),(true,true),(false,true),
                (false,true),(true,true),(true,false),(false,false) }) {
            recorderMode=mode.Item1; tickMode=mode.Item2; age=128;
            arm=(index++).ToString("00")+"-r"+(recorderMode?1:0)+"-t"+(tickMode?1:0);
            tickIssued=tickCompleted=tickPending=0;""")
    replace("if(active || pending!=0 || errors.Count!=0 || otherCount!=0)",
            "if(active || pending!=0 || tickPending!=0 || tickIssued!=tickCompleted || errors.Count!=0 || otherCount!=0)")
    replace('records.Add(new { arm,age,targetCount,otherCount,sourceFrames,role=index<=2?"qualification":"measured",completed=true });',
            '''records.Add(new { arm,age,targetCount,otherCount,sourceFrames,recorderMode,tickMode,
                tickIssued,tickCompleted,tickPending,completed=true });''')
    replace('schema="anchor-gpu-timing.v12"', 'schema="screen-probe-recorder-cadence.v1"')
    replace("lastArm=arm,frame,phase,pending,targetCount,otherCount,sourceFrames,readyFrames,",
            "lastArm=arm,frame,phase,pending,targetCount,otherCount,sourceFrames,readyFrames,recorderMode,tickMode,tickIssued,tickCompleted,tickPending,")
    return template


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="Fresh evidence directory (must not exist).")
    args = parser.parse_args()
    template_path = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs")
    # Validate every replacement before creating the evidence directory.
    source = build(template_path.read_text(encoding="utf-8-sig"), args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    protocol = {
        "schema": "screen-probe-recorder-cadence-protocol.v1",
        "templateSha256": hashlib.sha256(template_path.read_bytes()).hexdigest(),
        "arms": [{"recorder": r, "tick": t} for r, t in
                 [(False, False), (True, False), (True, True), (False, True),
                  (False, True), (True, True), (True, False), (False, False)]],
        "warmup": 1024, "recorderStart": 1152, "lastFrame": 1728,
        "tick": {"firstFrame": 1024, "lastFrame": 1726, "expectedRequests": 703,
                 "texture": "geometry", "region": [0, 0, 1, 1], "bytesPerRequest": 16},
        "gpuProfilerEnabledInAllArms": True,
        "denseStageHashes": "0..1023 and 1727; reset previous-* excluded only at 0",
        "savedPayloadBudget": 192 * 1024 * 1024,
        "performanceComparisonValid": False,
        "interpretationGate": "Common pre-window hashes and fixed geometry/source must validate; compare both replicates, do not discard divergent arms."
    }
    (args.output / "protocol.json").write_text(json.dumps(protocol, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output.resolve()), "script": str(args.output.resolve() / "capture-script.cs")}))


if __name__ == "__main__":
    main()
