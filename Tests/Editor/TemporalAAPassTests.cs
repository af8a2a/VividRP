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
    public class TemporalAAPassTests
    {
        [Test]
        public void Initialize_RegistersExpectedTextureResources()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CameraDepth",
                "Color",
                "MotionVectors",
                "TAAHistoryColor",
                "TAAHistoryColorCurrent",
                "TAAOutput",
            }));
        }

        [Test]
        public void Initialize_ColorInput_IsReadOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var colorEntry = resources.Textures.First(e => e.Name == "Color");

            Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Initialize_MotionVectors_IsReadOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var motionVectorEntry = resources.Textures.First(e => e.Name == "MotionVectors");

            Assert.That(motionVectorEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Initialize_TAAOutput_IsWriteOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var outputEntry = resources.Textures.First(e => e.Name == "TAAOutput");

            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
        }

        [Test]
        public void Initialize_HasNoBuffersOrRenderLists()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void TemporalAAPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(TemporalAAPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresOutputDimensionsFromCameraData()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_ConfiguresHistoryCurrentDimensionsFromCameraData()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var historyCurrentTexture = GetTextureField(pass, "m_HistoryColorCurrent");
            Assert.That(historyCurrentTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(historyCurrentTexture.desc.Height, Is.EqualTo(720));
            Assert.That(historyCurrentTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_FallsBackToPixelDimensions_WhenActualDimensionsAreZero()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 0;
            cameraData.actualHeight = 0;
            cameraData.pixelWidth = 800;
            cameraData.pixelHeight = 600;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(800));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(600));
        }

        [Test]
        public void Constructor_OutputFormat_IsR16G16B16A16_SFloat()
        {
            var pass = new TemporalAAPass();

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        private static RenderGraphTexture GetTextureField(TemporalAAPass pass, string fieldName)
        {
            var field = typeof(TemporalAAPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on TemporalAAPass");
            return (RenderGraphTexture)field.GetValue(pass);
        }
    }
}
