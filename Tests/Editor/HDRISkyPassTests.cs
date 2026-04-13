using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class HDRISkyPassTests
    {
        [Test]
        public void LegacyHDRISkyPass_InheritsSkyInjectionPass()
        {
            Assert.That(typeof(HDRISkyPass).BaseType, Is.EqualTo(typeof(SkyInjectionPass)));
        }

        [Test]
        public void HdriSkyExposure_UsesEvStopMultiplierSoZeroRemainsNeutral()
        {
            Assert.That(HDRISkyVolume.ResolveExposureMultiplier(0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(HDRISkyVolume.ResolveExposureMultiplier(1f), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(HDRISkyVolume.ResolveExposureMultiplier(-1f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void VividRPCoreResources_DeclaresDefaultHDRISkyCubemap()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DefaultHDRISkyCubemap));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Texture/Default/DefaultHDRISky.exr"));
        }

        [Test]
        public void CreateInstance_AssignsDefaultSkyCubemap_WhenVolumeComponentIsCreated()
        {
            var component = ScriptableObject.CreateInstance<HDRISkyVolume>();

            try
            {
                Assert.That(HDRISkyVolume.GetDefaultSkyCubemap(), Is.Not.Null);
                Assert.That(component.skyCubemap.value, Is.SameAs(HDRISkyVolume.GetDefaultSkyCubemap()));
                Assert.That(component.HasSkyCubemap(), Is.True);
                Assert.That(component.exposure.value, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void SkyInjection_DelegatesHdriDrawingToRenderer()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "HDRISky.shader"));
            var injectionPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "SkyInjectionPass.cs"));
            var legacyPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "HDRISkyPass.cs"));
            var rendererSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "HDRI", "HDRISkyRenderer.cs"));
            var frameContextSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));
            var bindingSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureShaderBindings.cs"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/HDRISky\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(shaderSource, Does.Contain("return float4(VividApplyPreExposure(EvaluateSkyColor(input.positionCS.xy)), 1.0);"));
            Assert.That(shaderSource, Does.Contain("Name \"HDRISkyBaking\""));
            Assert.That(shaderSource, Does.Contain("return float4(EvaluateSkyColor(input.positionCS.xy), 1.0);"));

            Assert.That(injectionPassSource, Does.Contain("public class SkyInjectionPass : UnsafePass"));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.PrepareSkyInjection("));
            Assert.That(injectionPassSource, Does.Contain("SkyManager.RenderSkyInjection(cmd);"));
            Assert.That(legacyPassSource, Does.Contain("public class HDRISkyPass : SkyInjectionPass"));

            Assert.That(rendererSource, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(rendererSource, Does.Contain("public void RenderSky(CommandBuffer cmd)"));
            Assert.That(rendererSource, Does.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(rendererSource, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_Material, properties, 0);"));
            Assert.That(rendererSource, Does.Not.Contain("HDRISkyPass.GetParameters("));

            Assert.That(frameContextSource, Does.Contain("AutoExposureShaderBindings.BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(bindingSource, Does.Contain("cmd.SetGlobalBuffer(PreExposureBufferId, preExposureBuffer);"));
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
