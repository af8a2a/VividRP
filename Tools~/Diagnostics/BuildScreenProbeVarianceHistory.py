"""Replay independent temporal-moment history while sharing real pixel history."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeSameFrameAtrous import build as wavelet_build, shader_source


def temporal_source(source):
    for name in ("SdfScreenProbeCommon.hlsl", "SdfDiffuseReSTIR.hlsl"):
        source = source.replace('#include "' + name + '"',
            '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/' + name + '"')
    return source


def build(template, output, motion_history=False, dual_variance=False, packed_dual_variance=False, sample_frame_base=1733):
    source = wavelet_build(template, output, motion_history)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    helper = Path(__file__).with_name("ScreenProbeVarianceHistoryReplay.cs.txt").read_text(encoding="utf-8-sig")
    helper = "    const bool temporalControlDualVariance=" + str(dual_variance).lower() + ";\n" + helper
    helper = "    const bool temporalControlPackedDualVariance=" + str(packed_dual_variance).lower() + ";\n" + helper
    function = helper.index("    void ReplayTemporalMoments(")
    replace("    ComputeShader depthCopy=null;", helper[:function] + "\n    ComputeShader depthCopy=null;")
    replace("    void ReadRegion(CommandBuffer cmd,", helper[function:] + "\n    void ReadRegion(CommandBuffer cmd,")
    replace('        var moments=Resolve("m_CurrentScreenProbeMoments");',
            '        var moments=label=="control-"?temporalControlMoments[frame&1]:Resolve("m_CurrentScreenProbeMoments");')
    replace('        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);', '''        temporalReplayAa=ReplayShader(output+"/temporal-aa.compute.txt");
        temporalReplayControl=ReplayShader(output+"/temporal-control.compute.txt");
        foreach(var shader in new[]{temporalReplayAa,temporalReplayControl}) {
            const string name="ScreenProbeTemporalReprojectionWithMomentsCS";
            var variant=compile.Invoke(null,new object[]{shader,shader.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))
                throw new InvalidOperationException("Temporal replay compilation failed.");
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
            using var sha=System.Security.Cryptography.SHA256.Create();
            temporalReplayPrograms.Add(new {reference=shader==temporalReplayAa?"aa":"control",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        if(temporalControlDualVariance) {
            const string name="InitializeDualVarianceHistoryCS";
            var variant=compile.Invoke(null,new object[]{temporalReplayControl,temporalReplayControl.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))throw new InvalidOperationException("History initializer compilation failed.");
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);using var sha=System.Security.Cryptography.SHA256.Create();
            temporalReplayPrograms.Add(new {reference="initialize",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        if(temporalControlPackedDualVariance) {
            const string name="ScreenProbeUpdateSlowVarianceCS";
            var variant=compile.Invoke(null,new object[]{temporalReplayControl,temporalReplayControl.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))throw new InvalidOperationException("Slow-variance compilation failed.");
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);using var sha=System.Security.Cryptography.SHA256.Create();
            temporalReplayPrograms.Add(new {reference="slow",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        temporalReplayAaMoments=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
        temporalReplayGeometry=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt);
        for(int i=0;i<2;++i)temporalControlMoments[i]=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);''')
    replace('            if(replayAa!=null)UnityEngine.Object.DestroyImmediate(replayAa);',
            '            if(temporalReplayAa!=null)UnityEngine.Object.DestroyImmediate(temporalReplayAa);\n            if(temporalReplayControl!=null)UnityEngine.Object.DestroyImmediate(temporalReplayControl);\n            if(replayAa!=null)UnityEngine.Object.DestroyImmediate(replayAa);')
    replace('replayPrograms,complete,errors,', 'replayPrograms,temporalReplayPrograms,complete,errors,')
    replace('if(arm.StartsWith("00-",StringComparison.Ordinal) && frame==lastFrame-1)ReadRegion(cmd,lighting,"full-final");',
            '// Omit the full-screen still to keep independent moments under 512 MiB.')
    if not motion_history:
        replace('ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);',
                'ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40,save:false);')
    # This runs from frame zero, before any wavelet replay and before RawResolved
    # is reused as a wavelet scratch target. The renderer's histories are read only.
    marker = "    Action<ComputePassContext,int,Texture> stage=(context,index,texture)=> {"
    replace(marker, marker + '''
        if(active && phase==1 && index==1 && context.Get<VividCameraData>().camera==camera)
            ReplayTemporalMoments(context);''')
    if sample_frame_base != 1733:
        assert source.count("1733") == 2
        source = source.replace("1733", str(sample_frame_base))
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--control-temporal", type=Path, required=True)
    parser.add_argument("--motion-history", action="store_true")
    parser.add_argument("--control-wavelet", type=Path, help="Optional spatial filter body for the independent moment layout")
    layout = parser.add_mutually_exclusive_group()
    layout.add_argument("--dual-variance", action="store_true", help="Control stores mean / fast variance / age / slow variance")
    layout.add_argument("--packed-dual-variance", action="store_true", help="Keep the original fast kernel; run slow variance separately with a positive normal offset")
    parser.add_argument("--sample-frame-base", type=int, default=1733, help="Independent direction-sequence base for holdout validation")
    args = parser.parse_args()
    output = args.output.resolve()
    workspace = Path(__file__).resolve().parents[2]
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    assert 0 <= args.sample_frame_base <= 2**31 - 4096
    source = build(template, output, args.motion_history, args.dual_variance or args.packed_dual_variance, args.packed_dual_variance, args.sample_frame_base)
    actual_temporal = workspace / "Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute"
    body = (workspace / "Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl").read_text(encoding="utf-8-sig")
    control_body = args.control_wavelet.read_text(encoding="utf-8-sig") if args.control_wavelet else body
    references = {"reference-aa.compute.txt": shader_source(body), "reference-control.compute.txt": shader_source(control_body),
        "temporal-aa.compute.txt": temporal_source(actual_temporal.read_text(encoding="utf-8-sig")),
        "temporal-control.compute.txt": temporal_source(args.control_temporal.read_text(encoding="utf-8-sig"))}
    if args.dual_variance or args.packed_dual_variance:
        references["temporal-control.compute.txt"] += '''
#pragma kernel InitializeDualVarianceHistoryCS
Texture2D<float4> _SdfInitialMomentsInput;
RWTexture2D<float4> _SdfInitialMomentsOutput;
[numthreads(8,8,1)]
void InitializeDualVarianceHistoryCS(uint3 id:SV_DispatchThreadID) {
    if(any(id.xy>=uint2(1920,1080)))return;
    float4 value=_SdfInitialMomentsInput.Load(int3(id.xy,0));
    _SdfInitialMomentsOutput[id.xy]=float4(value.xyz,value.y);
}
'''
        if args.packed_dual_variance:
            references["temporal-control.compute.txt"] = references["temporal-control.compute.txt"].replace(
                "float4(value.xyz,value.y);", "SdfRoundToNearestHalf(float4(value.xyz,value.w>0.0&&value.z>=1.0?value.y+0.00006103515625:0.0));")
    output.mkdir(parents=True, exist_ok=False)
    hashes = {}
    for name, text in references.items():
        path = output / name
        path.write_text(text, encoding="utf-8")
        hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / "capture-script.cs").write_text(source, encoding="utf-8")
    protocol = dict(schema="screen-probe-variance-history-protocol.v1",
        arms=["02-r0-t0", "03-r0-t0"] if args.motion_history else ["00-r0-t0", "01-r0-t0"],
        motionHistory=args.motion_history, warmup=1024, measured=128, spacing=16, rays=8,
        sampleFrameBase=args.sample_frame_base,
        planeGuidance=True, waveletStagesSaved=False, varianceStagesSaved=False, temporalMomentReplay=True,
        controlMomentLayout="packed-dual-variance" if args.packed_dual_variance else "dual-variance" if args.dual_variance else "legacy-variance",
        payloadBudget=512 * 1024**2, aaIterations=4, controlIterations=4,
        referenceSourceSha256={label: hashes[f"reference-{label}.compute.txt"] for label in ("aa", "control")},
        temporalSourceSha256={label: hashes[f"temporal-{label}.compute.txt"] for label in ("aa", "control")},
        note="Control moments evolve from frame zero in owned ping-pong textures. Pixel RGB/history and geometry remain shared; the control wavelet source is pinned separately. Require byte-exact temporal AA, control RGB/history/geometry and layout-specific invariant channels. No GPU timing claim.")
    (output / "protocol.json").write_text(json.dumps(protocol, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
