"""Replay a current-frame dense variance prefilter at coarse wavelet scales."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeSameFrameAtrous import build as replay_build, shader_source


def build(template, output, motion_history=False, sample_frame_base=1733):
    if not 0 <= sample_frame_base <= 2**31 - 4096:
        raise ValueError("Sample frame base is out of range")
    source = replay_build(template, output, motion_history)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, old[:100]
        source = source.replace(old, new, 1)

    replace("    RenderTexture replayInput=null;", "    RenderTexture replayInput=null, prefilteredMoments=null;")
    replace('            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousScreenProbeMoments",moments);',
            '            raw.SetComputeTextureParam(shader,kernel,"_SdfAtrousScreenProbeMoments",label=="control-" && i>=2?prefilteredMoments:moments);')
    marker = "            cmd.CopyTexture(input,replayInput);"
    replace(marker, marker + '''
            int prefilter=replayControl.FindKernel("PrefilterSdfMomentVarianceCS");
            bindReceiver(context.cmd,replayControl,prefilter);bindFrame(context.cmd,replayControl);
            cmd.SetComputeTextureParam(replayControl,prefilter,"_SdfAtrousScreenProbeMoments",Resolve("m_CurrentScreenProbeMoments"));
            cmd.SetComputeTextureParam(replayControl,prefilter,"_SdfAtrousPrefilteredMoments",prefilteredMoments);
            context.cmd.DispatchCompute(replayControl,prefilter,240,135,1);
            foreach(var roi in replayRois)ReadRegion(cmd,prefilteredMoments,"variance-prefiltered-moments-"+roi.Item1,roi.Item2,roi.Item3,roi.Item4,roi.Item5);''')
    replace('        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);', '''        {
            const string name="PrefilterSdfMomentVarianceCS";
            var variant=compile.Invoke(null,new object[]{replayControl,replayControl.FindKernel(name),Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
            if(!(bool)variant.GetType().GetProperty("Success").GetValue(variant))throw new InvalidOperationException("Variance prefilter compilation failed.");
            var bytes=(byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);using var sha=System.Security.Cryptography.SHA256.Create();
            replayPrograms.Add(new {reference="variance-prefilter",name,bytes=bytes.Length,
                sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()});
        }
        prefilteredMoments=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
        replayInput=ReplayTexture(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);''')
    replace('if(arm.StartsWith("00-",StringComparison.Ordinal) && frame==lastFrame-1)ReadRegion(cmd,lighting,"full-final");',
            '// Keep current-moment prefilter diagnostics within the fixed payload cap.')
    if not motion_history:
        replace('ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40);',
                'ReadRegion(cmd,Resolve("m_CurrentPixelLighting"),"pixel-history-emitter-gap",720,936,480,40,save:false);')
    if sample_frame_base != 1733:
        assert source.count("1733") == 2
        source = source.replace("1733", str(sample_frame_base))
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--motion-history", action="store_true")
    parser.add_argument("--sample-frame-base", type=int, default=1733)
    args = parser.parse_args()
    output = args.output.resolve()
    workspace = Path(__file__).resolve().parents[2]
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, output, args.motion_history, args.sample_frame_base)
    body = (workspace / "Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl").read_text(encoding="utf-8-sig")
    prefilter = Path(__file__).with_name("ScreenProbeVariancePrefilter.hlsl.txt").read_text(encoding="utf-8-sig")
    references = {"aa": shader_source(body), "control": shader_source(body) + prefilter}
    output.mkdir(parents=True, exist_ok=False)
    hashes = {}
    for label, text in references.items():
        path = output / f"reference-{label}.compute.txt"
        path.write_text(text, encoding="utf-8")
        hashes[label] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / "capture-script.cs").write_text(source, encoding="utf-8")
    protocol = dict(schema="screen-probe-variance-prefilter-protocol.v1",
        arms=["02-r0-t0", "03-r0-t0"] if args.motion_history else ["00-r0-t0", "01-r0-t0"],
        motionHistory=args.motion_history, sampleFrameBase=args.sample_frame_base,
        warmup=1024, measured=128, spacing=16, rays=8, aaIterations=4, controlIterations=4,
        controlMomentVariancePrefilter=True, controlPrefilteredMomentScales=[4, 8],
        planeGuidance=True, waveletStagesSaved=False, varianceStagesSaved=False,
        payloadBudget=512 * 1024**2, referenceSourceSha256=hashes,
        note="Dense 5x5 current-frame variance prefilter; mean/age/coverage and all color histories unchanged. Fine scales use actual moments. No frozen guide or performance claim.")
    (output / "protocol.json").write_text(json.dumps(protocol, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
