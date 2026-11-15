"""Static causal replay: freeze one moment channel while keeping the others live."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeSameFrameAtrous import build as replay_build, shader_source


def composition_source(channel):
    if channel not in ("mean", "variance"):
        raise ValueError("Only mean or variance can be isolated")
    component = "x" if channel == "mean" else "y"
    return """
#pragma kernel ComposeSdfMomentGuideCS
Texture2D<float4> _SdfLiveMomentGuide;
Texture2D<float4> _SdfFrozenMomentGuide;
RWTexture2D<float4> _SdfComposedMomentGuide;
[numthreads(8,8,1)]
void ComposeSdfMomentGuideCS(uint3 id:SV_DispatchThreadID) {
    if(any(id.xy>=uint2(1920,1080)))return;
    float4 live=_SdfLiveMomentGuide.Load(int3(id.xy,0));
    float4 frozen=_SdfFrozenMomentGuide.Load(int3(id.xy,0));
    live.COMPONENT=frozen.COMPONENT;
    _SdfComposedMomentGuide[id.xy]=live;
}
""".replace("COMPONENT", component)


def build(template, output, sample_frame_base=1733, frozen_scales="all"):
    if not 0 <= sample_frame_base <= 2**31 - 4096:
        raise ValueError("Sample frame base is out of range")
    if frozen_scales not in ("all", "fine", "coarse"):
        raise ValueError("Frozen scales must be all, fine or coarse")
    source = replay_build(template, output, freeze_control_moments=True)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace("    RenderTexture replayInput=null, frozenControlMoments=null;",
            "    RenderTexture replayInput=null, frozenControlMoments=null, composedControlMoments=null;")
    replace('        var moments=label=="control-"?frozenControlMoments:Resolve("m_CurrentScreenProbeMoments");',
            '        var moments=Resolve("m_CurrentScreenProbeMoments");')
    scale_predicate = "true" if frozen_scales == "all" else "i < 2" if frozen_scales == "fine" else "i >= 2"
    replace('            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousScreenProbeMoments",moments);',
            '            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousScreenProbeMoments",label=="control-" && (' + scale_predicate + ')?composedControlMoments:moments);')
    marker = '            if(frame==warmup)cmd.CopyTexture(Resolve("m_CurrentScreenProbeMoments"),frozenControlMoments);'
    replace(marker, marker + '''
            int compose=replayControl.FindKernel("ComposeSdfMomentGuideCS");
            cmd.SetComputeTextureParam(replayControl,compose,"_SdfLiveMomentGuide",Resolve("m_CurrentScreenProbeMoments"));
            cmd.SetComputeTextureParam(replayControl,compose,"_SdfFrozenMomentGuide",frozenControlMoments);
            cmd.SetComputeTextureParam(replayControl,compose,"_SdfComposedMomentGuide",composedControlMoments);
            context.cmd.DispatchCompute(replayControl,compose,240,135,1);
            foreach(var roi in replayRois)ReadRegion(cmd,composedControlMoments,"control-composed-moments-"+roi.Item1,roi.Item2,roi.Item3,roi.Item4,roi.Item5);''')
    marker = '        frozenControlMoments=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);'
    replace(marker, marker + '\n        composedControlMoments=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);')
    replace('        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);', '''        {
            const string name="ComposeSdfMomentGuideCS";
            var variant=compile.Invoke(null,new object[]{replayControl,replayControl.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))
                throw new InvalidOperationException("Moment composition compilation failed.");
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
            using var sha=System.Security.Cryptography.SHA256.Create();
            replayPrograms.Add(new {reference="moment-composition",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);''')
    replace('if(arm.StartsWith("00-",StringComparison.Ordinal) && frame==lastFrame-1)ReadRegion(cmd,lighting,"full-final");',
            '// Channel isolation records ROI statistics instead of a full-screen still.')
    replace('ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);',
            'ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40,save:false);')
    if sample_frame_base != 1733:
        assert source.count("1733") == 2
        source = source.replace("1733", str(sample_frame_base))
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--frozen-channel", choices=("mean", "variance"), required=True)
    parser.add_argument("--sample-frame-base", type=int, default=1733)
    parser.add_argument("--frozen-scales", choices=("all", "fine", "coarse"), default="all")
    args = parser.parse_args()
    workspace = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, output, args.sample_frame_base, args.frozen_scales)
    body = (workspace / "Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl").read_text(encoding="utf-8-sig")
    references = {"aa": shader_source(body), "control": shader_source(body) + composition_source(args.frozen_channel)}
    output.mkdir(parents=True, exist_ok=False)
    hashes = {}
    for label, text in references.items():
        path = output / f"reference-{label}.compute.txt"
        path.write_text(text, encoding="utf-8")
        hashes[label] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / "capture-script.cs").write_text(source, encoding="utf-8")
    protocol = dict(schema="screen-probe-moment-channel-isolation-protocol.v1",
        arms=["00-r0-t0", "01-r0-t0"], motionHistory=False, warmup=1024, measured=128,
        spacing=16, rays=8, sampleFrameBase=args.sample_frame_base,
        aaIterations=4, controlIterations=4, controlMomentsFrozen=True,
        controlMomentHashesRecorded=True, controlMomentChannelFrozen=args.frozen_channel,
        controlFrozenMomentScales=[1, 2] if args.frozen_scales == "fine" else [4, 8] if args.frozen_scales == "coarse" else [1, 2, 4, 8],
        planeGuidance=True, waveletStagesSaved=False, varianceStagesSaved=False,
        payloadBudget=512 * 1024**2, referenceSourceSha256=hashes,
        note="Static causal diagnostic only. One guide channel freezes at the first measured frame; all other channels remain actual live values. Validate every composed half-word. Do not adopt as a moving or relighting implementation.")
    (output / "protocol.json").write_text(json.dumps(protocol, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
