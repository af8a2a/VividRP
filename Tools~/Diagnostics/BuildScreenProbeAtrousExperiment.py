"""Build matched static Cornell readbacks for every full-resolution wavelet scale."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeThinShellQuality import build as quality_build


def without_moments(source):
    replacements = {
        "    EditorApplication.LockReloadAssemblies();": '''    var momentsKernelField=type.GetField("m_ScreenProbeMomentsKernel",inst);
    int savedMomentsKernel=(int)momentsKernelField.GetValue(pass);
    EditorApplication.LockReloadAssemblies();''',
        "        volume.profile=profile; SceneManager.SetActiveScene(scene);":
            "        momentsKernelField.SetValue(pass,-1);\n        volume.profile=profile; SceneManager.SetActiveScene(scene);",
        "            foreach(var p in hooks) p.Value.SetValue(null,previousHooks[p.Key]);":
            "            momentsKernelField.SetValue(pass,savedMomentsKernel);\n            foreach(var p in hooks) p.Value.SetValue(null,previousHooks[p.Key]);",
    }
    for old, new in replacements.items():
        assert source.count(old) == 1
        source = source.replace(old, new, 1)
    return source


def build(template, output, disable_moments=False):
    source = quality_build(template, output, trace_inputs=True)

    def replace(old, new):
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("Enumerable.Repeat((false,false),4)", "Enumerable.Repeat((false,false),2)")
    replace('"EditorGatherStageCapture", "EditorGatherFrameOverride",',
            '"EditorGatherStageCapture", "EditorAtrousDispatchCapture", "EditorGatherFrameOverride",')
    replace("frame<warmup || index!=0 ||", "frame<warmup ||")
    replace('        else ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);', '''        else if(index==0)ReadRegion(cmd,texture,"resolved-emitter-gap",720,936,480,40);
        else if(index==1)ReadRegion(cmd,texture,"temporal-emitter-gap",720,936,480,40);''')
    replace('    Action<ComputePassContext,Texture,Texture,Texture> trace =', '''    Action<ComputePassContext,int,Texture,Texture,Texture,Texture> wavelet=(context,index,input,result,variance,varianceOutput)=> {
        if(!active || phase!=1 || frame<warmup || context.Get<VividCameraData>().camera!=camera)return;
        var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        if(index<3)ReadRegion(cmd,result,"wavelet-"+index,720,936,480,40);
        if(varianceOutput!=null)ReadRegion(cmd,varianceOutput,"variance-"+index,720,936,480,40);
        if(index==0) {
            ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);
            var momentField=type.GetField("m_CurrentScreenProbeMoments",inst);
            if(momentField!=null) {
                var moments=(TextureHandle)(RenderGraphTexture)momentField.GetValue(pass);
                if(moments.IsValid() && ((Texture)moments).width==1920)
                    ReadRegion(cmd,(Texture)moments,"native-moments",720,936,480,40);
            }
        }
    };
    Action<ComputePassContext,Texture,Texture,Texture> trace =''')
    replace('hooks["EditorGatherStageCapture"].SetValue(null,stage);',
            'hooks["EditorGatherStageCapture"].SetValue(null,stage); hooks["EditorAtrousDispatchCapture"].SetValue(null,wavelet);')
    replace('schema="screen-probe-anchor-quality.v1"', 'schema="screen-probe-atrous-stages.v1"')
    return without_moments(source) if disable_moments else source


def build_motion(template, output, disable_moments=False):
    source = quality_build(template, output, motion_history=True, trace_inputs=True)
    old = '                ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-edge",880,414,160,128);'
    assert source.count(old) == 1
    source = source.replace(old, old + '''
                if((int)Field("m_ScreenProbeMomentsKernel")>=0)
                    ReadRegion(cmd,Resolve("m_CurrentScreenProbeMoments"),"native-moments-edge",880,414,160,128);''', 1)
    return without_moments(source) if disable_moments else source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--disable-native-moments", action="store_true",
                        help="Use the existing no-moments kernel in the same assembly; restore it in finally")
    parser.add_argument("--motion-history", action="store_true",
                        help="Use two static/two fixed-motion arms and read moments at the moving edge")
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = (build_motion if args.motion_history else build)(template, args.output.resolve(), args.disable_native_moments)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-atrous-protocol.v1",
        arms=[f"{i:02}-r0-t0" for i in range(4 if args.motion_history else 2)],
        warmup=1024, measured=128, spacing=16, rays=8, motionHistory=args.motion_history,
        payloadBudget=512*1024**2, nativeMomentsEnabled=not args.disable_native_moments,
        roi=dict(x=720, y=936, width=480, height=40),
        note="Native Gather, frozen 8/64/32 source; temporal input and wavelet steps 1/2/4/8, plus propagated variance"), indent=2)+"\n", encoding="utf-8")
    print(json.dumps(dict(root=str(args.output.resolve()))))
