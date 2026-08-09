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
    public class AtmosphericScatteringPassTests
    {
        [Test]
        public void Initialize_RegistersColorDepthInputsAndOutput()
        {
            IRenderPass renderPass = new AtmosphericScatteringPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CameraDepth",
                "Color",
                "OutputColor",
                "SkyTexture",
                "VBufferLighting"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "OutputColor").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                textureEntries.Where(entry => entry.Name != "OutputColor").Select(entry => entry.Access).Distinct(),
                Is.EqualTo(new[] { AccessFlags.Read }));
        }

        [Test]
        public void Prepare_ConfiguresOutputToCameraDimensions_WhenSourceUsesPlaceholderSize()
        {
            var pass = new AtmosphericScatteringPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 800;
            cameraData.actualHeight = 450;

            pass.Prepare(frameData);

            var outputTexture = GetFieldValue<RenderGraphTexture>(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(800));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(450));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_DoesNotAllocate_WhenSkyDataIsStable()
        {
            var pass = new AtmosphericScatteringPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            cameraData.actualWidth = 800;
            cameraData.actualHeight = 450;
            skyData.activeSkyType = SkyType.None;

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                pass.Prepare(frameData);

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Initialize_ConfiguresCameraDepthInputForPointSampling()
        {
            var pass = new AtmosphericScatteringPass();
            var depthTexture = GetFieldValue<RenderGraphTexture>(pass, "m_DepthTexture");

            Assert.That(depthTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void AtmosphericScatteringPass_InheritsFromRasterPass()
        {
            Assert.That(typeof(RasterPass).IsAssignableFrom(typeof(AtmosphericScatteringPass)), Is.True);
            Assert.That(typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(AtmosphericScatteringPass)), Is.True);
        }

        private static T GetFieldValue<T>(AtmosphericScatteringPass pass, string fieldName)
        {
            var field = typeof(AtmosphericScatteringPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
