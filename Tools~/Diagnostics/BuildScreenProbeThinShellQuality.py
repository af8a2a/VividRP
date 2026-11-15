"""Build matched static/moving GI controls with a ceiling/emitter-gap region."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeAnchorQuality import build as anchor_build


def build(template, output, motion_history=False, trace_inputs=False):
    source = anchor_build(template, output, 512, "float", production=True)

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one original fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("const long savedPayloadBudget = 192L * 1024 * 1024;", "const long savedPayloadBudget = 256L * 1024 * 1024;")
    start = source.index("    Action<ComputePassContext,int,Texture> stage=")
    end = source.index("    Action<ComputePassContext,Texture,Texture,Texture,Texture> linear =", start)
    replace(source[start:end], """    Action<ComputePassContext,int,Texture> stage=(context,index,texture)=> {
        if(!active || phase!=1 || frame<warmup || index!=0 || context.Get<VividCameraData>().camera!=camera)return;
        var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        if(moving)ReadRegion(cmd,texture,"resolved-edge",880,414,160,128);
        else ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);
    };
""")
    replace('                ReadRegion(cmd,lighting,"final-back",1100,600,96,64);', """                ReadRegion(cmd,lighting,"final-back",1100,600,96,64);
                ReadRegion(cmd,lighting,"final-emitter-gap",720,936,480,40);
                ReadRegion(cmd,Resolve("m_CurrentPixelGeometry"),"geometry-emitter-gap",720,936,480,40);""")
    replace("savedPayloadBudget,savedPayloadBytes,anchorConfiguration=", "savedPayloadBudget,savedPayloadBytes,thinShellReceiverRoi=new { x=720,y=936,width=480,height=40 },anchorConfiguration=")
    if motion_history:
        replace("const long savedPayloadBudget = 256L * 1024 * 1024;", "const long savedPayloadBudget = 384L * 1024 * 1024;")
        replace("frame<warmup || index!=0 ||", "frame<warmup ||")
        replace('        if(moving)ReadRegion(cmd,texture,"resolved-edge",880,414,160,128);\n        else ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);', """        if(moving) {
            if(index==0)ReadRegion(cmd,texture,"resolved-edge",880,414,160,128);
            if(index==1) {
                ReadRegion(cmd,texture,"temporal-edge",880,414,160,128);
                ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-edge",880,414,160,128);
            }
        } else if(index==0)ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);""")
        replace("thinShellReceiverRoi=new", "motionHistoryRecorded=true,thinShellReceiverRoi=new")
    if trace_inputs:
        budget = 384 if motion_history else 256
        replace(f"const long savedPayloadBudget = {budget}L * 1024 * 1024;",
                "const long savedPayloadBudget = 512L * 1024 * 1024;")
        replace('            ReadRegion(cmd,geometry,"geometry",save:endpoint);', '''            ReadRegion(cmd,geometry,"geometry");
            ReadRegion(cmd,Resolve("m_GBuffer0"),"input-gbuffer0",save:false);
            ReadRegion(cmd,Resolve("m_GBuffer1"),"input-gbuffer1",save:false);
            ReadRegion(cmd,Resolve("m_AdaptivePlacement"),"input-placement",save:false);
            cmd.SetComputeTextureParam(depthCopy,depthCopyKernel,"_InputDepth",Resolve("m_Depth"));
            cmd.SetComputeTextureParam(depthCopy,depthCopyKernel,"rw_spd_mip0",depthReadback);
            cmd.DispatchCompute(depthCopy,depthCopyKernel,240,135,1);
            ReadRegion(cmd,depthReadback,"input-depth",save:false);''')
        replace('                vp=Enumerable.Range(0,16).Select(i=>vp[i/4,i%4]).ToArray(),', '''                inverseVp=Enumerable.Range(0,16).Select(i=>((Matrix4x4)Field("m_InverseViewProjection"))[i/4,i%4]).ToArray(),
                vp=Enumerable.Range(0,16).Select(i=>vp[i/4,i%4]).ToArray(),''')
        replace("thinShellReceiverRoi=new", "traceInputsRecorded=true,thinShellReceiverRoi=new")
    replace("        } finally { EditorApplication.UnlockReloadAssemblies(); }", "        } finally { EditorApplication.isPaused=true;EditorApplication.UnlockReloadAssemblies(); }")
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--motion-history", action="store_true")
    parser.add_argument("--trace-inputs", action="store_true", help="Hash actual receiver SRVs/matrices and save all derived probe geometry")
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve(), args.motion_history, args.trace_inputs)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-thin-shell-quality-protocol.v1",
        arms=["static-a", "static-b", "motion-a", "motion-b"], warmup=1024, measured=128,
        spacing=16, rays=8, anchorAge=512, precision="float", settingsPath=True,
        savedPayloadBudget=(512 if args.trace_inputs else (384 if args.motion_history else 256)) * 1024**2,
        motionHistory=args.motion_history, traceInputs=args.trace_inputs, gapRoi=dict(x=720, y=936, width=480, height=40),
        note="Additional Resolve, final GI and packed geometry near the emitter. No asset changes; pauses on completion."), indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
