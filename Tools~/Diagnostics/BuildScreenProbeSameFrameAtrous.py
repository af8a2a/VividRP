"""Replay two wavelet implementations against one frame's immutable GI input."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeAtrousExperiment import build as stage_build


def shader_source(body):
    prefix = """#pragma kernel FilterSdfDiffuseAtrous VIVID_SDF_ATROUS VIVID_SDF_ATROUS_GEOMETRY_GUARD
#pragma kernel FilterSdfDiffuseAtrousFinal VIVID_SDF_ATROUS VIVID_SDF_ATROUS_FINAL VIVID_SDF_ATROUS_GEOMETRY_GUARD
"""
    for name in ("SdfScreenProbeCommon.hlsl", "SdfScreenProbeTracingCommon.hlsl", "SdfShortRangeAOCommon.hlsl", "SdfDiffuseReSTIR.hlsl"):
        prefix += '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/' + name + '"\n'
    return prefix + body


def build(template, output, motion_history=False, control_iterations=4, freeze_control_moments=False):
    if control_iterations not in (4, 5):
        raise ValueError("The same-frame control supports four or five wavelet scales")
    if freeze_control_moments and motion_history:
        raise ValueError("Frozen moments are a static diagnostic, not a moving-history candidate")
    source = stage_build(template, output, plane_guidance=True)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace('    ComputeShader depthCopy=null;', '''    ComputeShader replayAa=null,replayControl=null;
    RenderTexture replayInput=null;
    var replayColors=new RenderTexture[2];var replayVariances=new RenderTexture[2];
    var replayOwned=new List<RenderTexture>();
    var replayPrograms=new List<object>();
    var bindReceiver=(Action<ComputeCommandBuffer,ComputeShader,int>)type.GetMethod("BindReceiver",inst)
        .CreateDelegate(typeof(Action<ComputeCommandBuffer,ComputeShader,int>),pass);
    var bindFrame=(Action<ComputeCommandBuffer,ComputeShader>)type.GetMethod("BindProbeFrame",inst)
        .CreateDelegate(typeof(Action<ComputeCommandBuffer,ComputeShader>),pass);
    var replayRois=new[]{("emitter-gap",720,936,480,40),("ceiling",912,970,96,48),
        ("floor",912,130,96,64),("back",1100,600,96,64)};
    RenderTexture ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat format) {
        var texture=new RenderTexture(new RenderTextureDescriptor(1920,1080) {
            graphicsFormat=format,depthBufferBits=0,enableRandomWrite=true,msaaSamples=1
        }) {hideFlags=HideFlags.HideAndDontSave};
        replayOwned.Add(texture);
        if(!texture.Create())throw new InvalidOperationException("Replay texture allocation failed.");
        return texture;
    }
    ComputeShader ReplayShader(string path) {
        var create=typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)
            .Single(m=>m.Name=="CreateComputeShaderAsset" && m.GetParameters().Length==1);
        var shader=(ComputeShader)create.Invoke(null,new object[]{System.IO.File.ReadAllText(path)});
        if(EditorUtility.IsPersistent(shader) || AssetDatabase.GetAssetPath(shader)!="") {
            UnityEngine.Object.DestroyImmediate(shader);
            throw new InvalidOperationException("Replay must not create a persistent Unity asset.");
        }
        return shader;
    }
    ComputeShader depthCopy=null;''')
    start = source.index("    Action<ComputePassContext,int,Texture,Texture,Texture,Texture> wavelet=")
    end = source.index("    Action<ComputePassContext,Texture,Texture,Texture> trace =", start)
    replace(source[start:end], '''    void Replay(ComputePassContext context,ComputeShader shader,string label,bool save) {
        var cmd=context.cmd;var raw=(CommandBuffer)wrapped.GetValue(cmd);
        bindFrame(cmd,shader);
        Texture input=replayInput,variance=Texture2D.blackTexture;
        var moments=Resolve("m_CurrentScreenProbeMoments");
        for(int i=0;i<4;++i) {
            bool last=i==3;int kernel=shader.FindKernel(last?"FilterSdfDiffuseAtrousFinal":"FilterSdfDiffuseAtrous");
            int slot=i&1;bindReceiver(cmd,shader,kernel);
            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousInput",input);
            raw.SetComputeTextureParam(shader,kernel,"_SdfDiffuseCurrentResolved",Texture2D.blackTexture);
            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousVariance",variance);
            raw.SetComputeTextureParam(shader,kernel,"_SdfDiffuseOutput",replayColors[slot]);
            if(!last)raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousVarianceOutput",replayVariances[slot]);
            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousScreenProbeMoments",moments);
            cmd.SetComputeIntParam(shader,"_SdfAtrousStep",1<<i);
            cmd.SetComputeIntParam(shader,"_SdfAtrousUseRestirDomain",0);
            cmd.SetComputeIntParam(shader,"_SdfAtrousHasVariance",1);
            cmd.SetComputeIntParam(shader,"_SdfAtrousUseScreenProbeMoments",1);
            cmd.DispatchCompute(shader,kernel,240,135,1);
            input=replayColors[slot];variance=replayVariances[slot];
        }
        foreach(var roi in replayRois)ReadRegion(raw,input,label+roi.Item1,roi.Item2,roi.Item3,roi.Item4,roi.Item5,save:save);
    }
    Action<ComputePassContext,int,Texture,Texture,Texture,Texture> wavelet=(context,index,input,result,variance,varianceOutput)=> {
        if(!active || phase!=1 || frame<warmup || context.Get<VividCameraData>().camera!=camera)return;
        var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        if(index==0) {
            if(!(bool)Field("m_DirectionalCache") || (int)Field("m_ScreenProbeMomentsKernel")<0)
                throw new InvalidOperationException("Replay requires the native moments path.");
            cmd.CopyTexture(input,replayInput);
            foreach(var roi in replayRois)ReadRegion(cmd,replayInput,"replay-input-"+roi.Item1,roi.Item2,roi.Item3,roi.Item4,roi.Item5,save:false);
            ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);
            ReadRegion(cmd,Resolve("m_CurrentScreenProbeMoments"),"native-moments",720,936,480,40);
        }
        if(index==3) {
            // Readbacks are recorded before reusing the scratch textures. Neither
            // replay writes the renderer's lighting, histories or source cache.
            Replay(context,replayAa,"replay-aa-",false);
            Replay(context,replayControl,"control-",true);
        }
    };
''')
    replace("    try {\n        depthCopy=", '''    try {
        replayAa=ReplayShader(output+"/reference-aa.compute.txt");
        replayControl=ReplayShader(output+"/reference-control.compute.txt");
        var compile=typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)
            .Single(m=>m.Name=="CompileComputeShaderVariant");
        foreach(var shader in new[]{replayAa,replayControl}) foreach(var name in new[]{"FilterSdfDiffuseAtrous","FilterSdfDiffuseAtrousFinal"}) {
            var variant=compile.Invoke(null,new object[]{shader,shader.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))
                throw new InvalidOperationException("Replay shader compilation failed: "+name);
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
            using var sha=System.Security.Cryptography.SHA256.Create();
            replayPrograms.Add(new {reference=shader==replayAa?"aa":"control",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
        for(int i=0;i<2;++i) {
            replayColors[i]=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
            replayVariances[i]=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat);
        }
        depthCopy=''')
    replace('            if(depthReadback!=null) {', '''            foreach(var texture in replayOwned) {texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            if(replayAa!=null)UnityEngine.Object.DestroyImmediate(replayAa);
            if(replayControl!=null)UnityEngine.Object.DestroyImmediate(replayControl);
            if(depthReadback!=null) {''')
    replace('schema="screen-probe-atrous-stages.v1",complete,errors,',
            'schema="screen-probe-atrous-same-frame.v1",replayPrograms,complete,errors,')
    if control_iterations != 4:
        replace('        for(int i=0;i<4;++i) {\n            bool last=i==3;',
                f'        int iterations=label.StartsWith("control-",StringComparison.Ordinal)?{control_iterations}:4;\n        for(int i=0;i<iterations;++i) {{\n            bool last=i==iterations-1;')
    if freeze_control_moments:
        replace('    RenderTexture replayInput=null;', '    RenderTexture replayInput=null, frozenControlMoments=null;')
        replace('        var moments=Resolve("m_CurrentScreenProbeMoments");',
                '        var moments=label=="control-"?frozenControlMoments:Resolve("m_CurrentScreenProbeMoments");')
        replace('            cmd.CopyTexture(input,replayInput);',
                '''            cmd.CopyTexture(input,replayInput);
            if(frame==warmup)cmd.CopyTexture(Resolve("m_CurrentScreenProbeMoments"),frozenControlMoments);
            foreach(var roi in replayRois)ReadRegion(cmd,frozenControlMoments,"control-moments-"+roi.Item1,roi.Item2,roi.Item3,roi.Item4,roi.Item5,save:false);''')
        replace('        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);',
                '        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);\n        frozenControlMoments=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);')
    if motion_history:
        replace("        int index=0;", "        int index=2;")
        replace('schema="screen-probe-atrous-same-frame.v1",',
                'schema="screen-probe-atrous-same-frame-motion.v1",motionHistoryRecorded=true,')
        replace('    var replayRois=new[]{("emitter-gap",720,936,480,40),("ceiling",912,970,96,48),\n        ("floor",912,130,96,64),("back",1100,600,96,64)};',
                '    var replayRois=new[]{("edge",880,414,160,128)};')
        replace('            ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);\n            ReadRegion(cmd,Resolve("m_CurrentScreenProbeMoments"),"native-moments",720,936,480,40);', '')
        start = source.index("    Action<ComputePassContext,int,Texture> stage=")
        end = source.index("    Action<ComputePassContext,Texture,Texture,Texture,Texture> linear =", start)
        replace(source[start:end], '''    Action<ComputePassContext,int,Texture> stage=(context,index,texture)=> {
        if(!active || phase!=1 || frame<warmup || context.Get<VividCameraData>().camera!=camera)return;
        var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        if(index==0)ReadRegion(cmd,texture,"resolved-edge",880,414,160,128);
        if(index==1) {
            ReadRegion(cmd,texture,"temporal-edge",880,414,160,128);
            ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-edge",880,414,160,128);
            ReadRegion(cmd,Resolve("m_CurrentScreenProbeMoments"),"native-moments-edge",880,414,160,128);
        }
    };
''')
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--control-body", type=Path, required=True)
    parser.add_argument("--motion-history", action="store_true", help="Replay two fixed-motion arms on the actual moving-edge inputs")
    parser.add_argument("--control-iterations", type=int, choices=(4, 5), default=4,
                        help="Number of spatial wavelet scales in the isolated control; A/A remains the actual four-pass renderer")
    parser.add_argument("--freeze-control-moments", action="store_true",
                        help="Static causal diagnostic: freeze only the control's moments at the first measured frame")
    args = parser.parse_args()
    output = args.output.resolve()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, output, args.motion_history, args.control_iterations, args.freeze_control_moments)
    output.mkdir(parents=True, exist_ok=False)
    workspace = Path(__file__).resolve().parents[2]
    aa = (workspace / "Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl").read_text(encoding="utf-8-sig")
    bodies = {"aa": aa, "control": args.control_body.read_text(encoding="utf-8-sig")}
    references = {}
    for name, body in bodies.items():
        text = shader_source(body)
        path = output / f"reference-{name}.compute.txt"
        path.write_text(text, encoding="utf-8")
        references[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / "capture-script.cs").write_text(source, encoding="utf-8")
    (output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-atrous-same-frame-protocol.v1",
        arms=["02-r0-t0", "03-r0-t0"] if args.motion_history else ["00-r0-t0", "01-r0-t0"],
        motionHistory=args.motion_history, warmup=1024, measured=128, spacing=16, rays=8,
        trajectory=dict(period=128, lateral=.28, forward=.06, yawDegrees=-7) if args.motion_history else None,
        aaIterations=4, controlIterations=args.control_iterations,
        controlMomentsFrozen=args.freeze_control_moments,
        controlMomentHashesRecorded=args.freeze_control_moments,
        planeGuidance=True, waveletStagesSaved=False, varianceStagesSaved=False,
        payloadBudget=512 * 1024**2, referenceSourceSha256=references,
        note="Identical copied pixel-temporal input and receiver textures; controlMomentsFrozen declares the control's moment source. AA must match the actual renderer byte-for-byte; no GPU timing claim"), indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
