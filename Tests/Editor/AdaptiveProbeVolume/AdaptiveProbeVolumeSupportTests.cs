using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class AdaptiveProbeVolumeSupportTests
    {
        [Test]
        public void PipelineAsset_ImplementsProbeVolumeEnabledInterface_WithExpectedDefaults()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var probeVolumeAsset = asset as IProbeVolumeEnabledRenderPipeline;

                Assert.That(probeVolumeAsset, Is.Not.Null);
                Assert.That(probeVolumeAsset.supportProbeVolume, Is.False);
                Assert.That(probeVolumeAsset.maxSHBands, Is.EqualTo(ProbeVolumeSHBands.SphericalHarmonicsL2));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RenderPipeline_AndFrameContext_WireProbeReferenceVolumeLifecycle()
        {
            var renderPipelineSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs"));
            var frameContextSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));
            var utilitySource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPipeline", "VividAdaptiveProbeVolumeUtility.cs"));

            Assert.That(renderPipelineSource, Does.Contain("VividAdaptiveProbeVolumeUtility.Initialize(asset)"));
            Assert.That(renderPipelineSource, Does.Contain("VividAdaptiveProbeVolumeUtility.Cleanup(m_Asset)"));
            Assert.That(frameContextSource, Does.Contain("VividAdaptiveProbeVolumeUtility.UpdatePerCamera("));
            Assert.That(utilitySource, Does.Contain("ProbeReferenceVolume.instance.Initialize"));
            Assert.That(utilitySource, Does.Contain("ProbeReferenceVolume.instance.Cleanup()"));
            Assert.That(utilitySource, Does.Contain("UpdateShaderVariablesProbeVolumes("));
            Assert.That(utilitySource, Does.Contain("BindAPVRuntimeResources(cmd, enableProbeVolumes)"));
        }

        [Test]
        public void ShaderSources_DeclareCoreProbeVolumeEntryPoints()
        {
            var wrapperSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Core", "Public", "VividProbeVolume.hlsl"));
            var lightingSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));
            var deferredComputeSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Material", "DeferredLit.compute"));
            var deferredDirectionalSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl"));
            var simpleDeferredSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl"));
            var standardLitInputSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Material", "StandardLit", "StandardLitInput.hlsl"));
            var indirectDiffuseSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Material", "ShaderPass", "IndirectDiffuse.hlsl"));
            var visibilityBufferResolveSource = File.ReadAllText(
                GetPackageFilePath("Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferGBufferResolve.shader"));

            Assert.That(wrapperSource, Does.Contain("ProbeVolume.hlsl"));
            Assert.That(wrapperSource, Does.Contain("uint _EnableProbeVolumes;"));
            Assert.That(wrapperSource, Does.Contain("VividHasProbeVolumeGI()"));
            Assert.That(wrapperSource, Does.Contain("SampleVividProbeVolume("));
            Assert.That(wrapperSource, Does.Contain("EvaluateAmbientProbe(float3 normalWS)"));
            Assert.That(lightingSource, Does.Contain("SampleVividProbeVolume(positionWS, surfaceData.normalWS, viewDirectionWS, 0xFFFFFFFFu)"));
            Assert.That(deferredComputeSource, Does.Contain("#pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2"));
            Assert.That(deferredDirectionalSource, Does.Contain("EvaluateBSDF_Env(positionWS, viewDirectionWS, preLightData, surfaceData, bsdfData)"));
            Assert.That(simpleDeferredSource, Does.Contain("EvaluateBSDF_Env(positionWS, viewDirectionWS, preLightData, surfaceData, bsdfData)"));
            Assert.That(simpleDeferredSource, Does.Contain("useAmbientFallback = useAmbientFallback && _EnableProbeVolumes == 0;"));
            Assert.That(standardLitInputSource, Does.Contain("SampleVividProbeVolume("));
            Assert.That(indirectDiffuseSource, Does.Contain("SampleVividProbeVolume("));
            Assert.That(visibilityBufferResolveSource, Does.Contain("SampleVividProbeVolume("));
        }

        [Test]
        public void GlobalSettings_SourceStoresProbeVolumeSceneData()
        {
            var source = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipelineGlobalSettings.cs"));

            Assert.That(source, Does.Contain("ProbeVolumeSceneData"));
            Assert.That(source, Does.Contain("GetOrCreateAPVSceneData()"));
            Assert.That(source, Does.Contain("ProbeVolumeGlobalSettings"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
