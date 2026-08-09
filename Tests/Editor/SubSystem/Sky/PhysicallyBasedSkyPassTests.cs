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
        public void BuildRendererContext_AllowsMissingExposureData_WhenPhysicalSkyRunsBeforeAutoExposure()
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var method = typeof(SkyManager).GetMethod("BuildRendererContext", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var context = (SkyRendererContext)method.Invoke(null, new object[] { frameData, cameraData, SkyType.PhysicallyBased });

            Assert.That(context.cameraData, Is.SameAs(cameraData));
            Assert.That(context.lightData, Is.Not.Null);
            Assert.That(context.exposureData, Is.Null);
            Assert.That(frameData.Contains<VividExposureData>(), Is.False);
        }
    }
}
