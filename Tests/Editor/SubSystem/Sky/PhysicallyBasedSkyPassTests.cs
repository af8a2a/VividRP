using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SkyInjectionPassTests
    {
        [Test]
        public void SkyInjectionPass_RegistersDepthShadowSkyViewInputsWithColorOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new SkyInjectionPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "CSMShadowAtlas", "Color", "Depth", "DirectionalShadowTexture", "SkyViewLUT" }));
            Assert.That(textureEntries.Single(entry => entry.Name == "CSMShadowAtlas").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries.Single(entry => entry.Name == "Depth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "DirectionalShadowTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyViewLUT").Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Source_UsesSkyInjectionPassForRendererDrivenSkyDrawing()
        {
            var injectionPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "SkyInjectionPass.cs"));
            var registrySource = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));
            var rendererSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));
            var skyManagerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));
            var skyRendererContextSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyRendererContext.cs"));
            var renderGraphSource = File.ReadAllText(GetProjectFilePath("Assets", "Vivid Render Graph.vrdg"));

            Assert.That(injectionPassSource, Does.Contain("m_SkyViewLUT = RenderGraphTexture.CreateInput(\"SkyViewLUT\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(injectionPassSource, Does.Contain("m_DirectionalShadowTexture = RenderGraphTexture.CreateInput(\"DirectionalShadowTexture\", GraphicsFormat.R16_SFloat);"));
            Assert.That(injectionPassSource, Does.Contain("m_LocalCSMShadowAtlas = RenderGraphTexture.CreateInput(\"CSMShadowAtlas\", GraphicsFormat.None, DepthBits.Depth16);"));
            Assert.That(injectionPassSource, Does.Contain("public class SkyInjectionPass : UnsafePass, IAllowGlobalStateModificationPass"));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.PrepareSkyInjection("));
            Assert.That(injectionPassSource, Does.Contain("nativeCmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.RenderSkyInjection(context);"));
            Assert.That(injectionPassSource, Does.Not.Contain("SkyManager.ImportSkyViewLut("));
            Assert.That(registrySource, Does.Contain("internal sealed class SkyInjectionPass : RenderPassNodeData"));
            Assert.That(registrySource, Does.Not.Contain("internal sealed class AtmosphereLUTPass : RenderPassNodeData"));
            Assert.That(registrySource, Does.Not.Contain("internal sealed class PhysicallyBasedSkyPass : RenderPassNodeData"));
            Assert.That(renderGraphSource, Does.Contain("type: {class: SkyInjectionPass, ns: VividRP.Editor.RenderGraph.Generated, asm: VividRP.Editor}"));
            Assert.That(renderGraphSource, Does.Not.Contain("type: {class: AtmosphereLUTPass, ns: VividRP.Editor.RenderGraph.Generated, asm: VividRP.Editor}"));
            Assert.That(renderGraphSource, Does.Not.Contain("PhysicallyBasedSkyPass"));
            Assert.That(renderGraphSource, Does.Not.Contain("HDRISkyPass"));

            Assert.That(rendererSource, Does.Contain("private const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(rendererSource, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(rendererSource, Does.Contain("public void RenderSky(UnsafePassContext context)"));
            Assert.That(rendererSource, Does.Contain("ImportSkyViewLutForPass(skyViewLut);"));
            Assert.That(rendererSource, Does.Contain("PassRecorder.ImportTexture(skyViewLut, handle);"));
            Assert.That(rendererSource, Does.Not.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(rendererSource, Does.Contain("cmd.SetViewport(m_RenderViewport);"));
            Assert.That(rendererSource, Does.Contain("m_AtmosphereLutCache.RenderAtmosphericScattering("));
            Assert.That(rendererSource, Does.Contain("m_HasConnectedCSMShadowAtlas ? m_CSMShadowAtlas : null"));
            Assert.That(rendererSource, Does.Contain("UpdateLocalSkyPrecomputation(context, skyData, cmd);"));
            Assert.That(rendererSource, Does.Contain("var skyViewTexture = ResolveSkyViewTexture();"));
            Assert.That(rendererSource, Does.Contain("Shader.GetGlobalTexture(DirectionalShadowTextureId)"));
            Assert.That(rendererSource, Does.Contain("VividAutoExposureSystem.ResolvePreExposureBuffer(m_RenderContext.exposureData)"));
            Assert.That(rendererSource, Does.Contain("properties.SetBuffer(PreExposureBufferId, preExposureBuffer);"));
            Assert.That(rendererSource, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_SkyMaterial, properties, 0);"));
            Assert.That(skyManagerSource, Does.Not.Contain("private static readonly PhysicallyBasedSkyAtmosphereLutCache"));
            Assert.That(skyManagerSource, Does.Contain("renderer.UpdateFrameResources(context, s_CachedSkyData, cmd);"));
            Assert.That(skyManagerSource, Does.Contain("frameData.GetOrCreate<VividExposureData>()"));
            Assert.That(skyManagerSource, Does.Not.Contain("s_PhysicallyBasedSkyAtmosphereLutCache.Update(context, cmd);"));
            Assert.That(skyRendererContextSource, Does.Contain("VividExposureData exposureData = null"));
            Assert.That(skyRendererContextSource, Does.Contain("internal VividExposureData exposureData { get; }"));
        }

        [Test]
        public void Shader_Source_UsesHdrpStyleRenderingAndEvaluationSplit()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSky.shader"));
            var renderingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl"));
            var evaluationSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyEvaluation.hlsl"));
            var bridgeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyBridge.hlsl"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(source, Does.Contain("#pragma multi_compile_fragment _ LOCAL_SKY"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyRendering.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl\""));
            Assert.That(source, Does.Contain("float4 RenderSky(Varyings input)"));
            Assert.That(source, Does.Contain("EvaluateDistantAtmosphereWithLut(-V, skyColor, skyOpacity);"));
            Assert.That(source, Does.Contain("value.rgb = ClampToFloat16Max(VividApplyPreExposure(value.rgb));"));
            Assert.That(renderingSource, Does.Contain("float3 RenderSunDisk(inout float tFrag, float tExit, float3 V)"));
            Assert.That(evaluationSource, Does.Contain("void EvaluateDistantAtmosphere("));
            Assert.That(bridgeSource, Does.Contain("float _SkyUseLUT;"));
            Assert.That(bridgeSource, Does.Contain("EvaluateDistantAtmosphereWithLut"));
            Assert.That(bridgeSource, Does.Not.Contain("EvaluateAtmosphericFallback"));
            Assert.That(bridgeSource, Does.Not.Contain("EvaluateSky"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
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

        private static string GetProjectFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, Path.Combine(relativeParts));
        }
    }
}
