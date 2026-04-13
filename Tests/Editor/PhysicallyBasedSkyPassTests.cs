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
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyPassTests
    {
        [Test]
        public void SkyInjectionPass_RegistersDepthShadowSkyViewInputsWithColorOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new SkyInjectionPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Color", "Depth", "DirectionalShadowTexture", "SkyViewLUT" }));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries.Single(entry => entry.Name == "Depth").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries.Single(entry => entry.Name == "DirectionalShadowTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyViewLUT").Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void LegacyPhysicallyBasedSkyPass_InheritsSkyInjectionPass()
        {
            Assert.That(typeof(PhysicallyBasedSkyPass).BaseType, Is.EqualTo(typeof(SkyInjectionPass)));
        }

        [Test]
        public void VividRPCoreResources_DeclaresPhysicallyBasedSkyShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.PhysicallyBasedSkyShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/PhysicallyBasedSky"));
        }

        [Test]
        public void Source_UsesSkyInjectionPassForRendererDrivenSkyDrawing()
        {
            var injectionPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "SkyInjectionPass.cs"));
            var legacyPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "PhysicallyBasedSkyPass.cs"));
            var rendererSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(injectionPassSource, Does.Contain("m_SkyViewLUT = RenderGraphTexture.CreateInput(\"SkyViewLUT\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(injectionPassSource, Does.Contain("m_DirectionalShadowTexture = RenderGraphTexture.CreateInput(\"DirectionalShadowTexture\", GraphicsFormat.R16_SFloat);"));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.PrepareSkyInjection("));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.RenderSkyInjection(cmd);"));
            Assert.That(legacyPassSource, Does.Contain("public class PhysicallyBasedSkyPass : SkyInjectionPass"));

            Assert.That(rendererSource, Does.Contain("private const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(rendererSource, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(rendererSource, Does.Contain("public void RenderSky(CommandBuffer cmd)"));
            Assert.That(rendererSource, Does.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(rendererSource, Does.Contain("var skyViewTexture = ResolveSkyViewTexture();"));
            Assert.That(rendererSource, Does.Contain("Shader.GetGlobalTexture(DirectionalShadowTextureId)"));
            Assert.That(rendererSource, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_SkyMaterial, properties, 0);"));
        }

        [Test]
        public void Shader_Source_UsesHdrpEvaluationForSkyViewLutAndFallback()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PhysicallyBasedSky.shader"));
            var bridgeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyBridge.hlsl"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(source, Does.Contain("#pragma multi_compile_fragment _ LOCAL_SKY"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl\""));
            Assert.That(source, Does.Contain("return float4(VividApplyPreExposure(EvaluateSkyColor(input.positionCS.xy)), 1.0f);"));
            Assert.That(bridgeSource, Does.Contain("float _SkyUseLUT;"));
            Assert.That(bridgeSource, Does.Contain("TEXTURE2D(_DirectionalShadowTexture);"));
            Assert.That(bridgeSource, Does.Contain("EvaluateSky"));
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
    }
}
