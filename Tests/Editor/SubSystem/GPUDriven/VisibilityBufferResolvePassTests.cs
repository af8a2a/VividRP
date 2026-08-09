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
    public sealed class VisibilityBufferResolvePassTests
    {
        [SetUp]
        public void SetUp()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [Test]
        public void Initialize_RegistersVisibilityDepthInputsAndColorOutput()
        {
            IRenderPass renderPass = new VisibilityBufferResolvePass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "Depth");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_UsesVisibilityTextureSize_WhenConfigured()
        {
            var pass = new VisibilityBufferResolvePass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            visibilityTexture.desc.Width = 1280;
            visibilityTexture.desc.Height = 720;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_UsesSharedRenderingDebuggerVisibilitySettings()
        {
            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode =
                VisibilityBufferDebugVisualizationMode.ClusterLOD;
            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure = 3f;
            VividRenderingDebugDisplaySettings.Data.visibilityBufferWireframeThickness = 6f;
            var pass = new VisibilityBufferResolvePass();

            pass.Prepare(new ContextContainer());

            Assert.That(
                GetFieldValue<VisibilityBufferDebugVisualizationMode>(pass, "m_ResolvedDebugMode"),
                Is.EqualTo(VisibilityBufferDebugVisualizationMode.ClusterLOD));
            Assert.That(GetFieldValue<float>(pass, "m_ResolvedExposure"), Is.EqualTo(3f));
            Assert.That(GetFieldValue<float>(pass, "m_ResolvedWireframeThickness"), Is.EqualTo(6f));
        }

        [Test]
        public void ResolveSettings_UsesDebuggerDefaults_WhenDataIsUnavailable()
        {
            var settings = VisibilityBufferResolvePass.ResolveSettings(null);

            Assert.That(
                settings.debugMode,
                Is.EqualTo(VisibilityBufferDebugVisualizationMode.Cluster));
            Assert.That(settings.exposure, Is.EqualTo(0f));
            Assert.That(
                settings.wireframeThickness,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness));
        }

        [Test]
        public void ResolvePass_DoesNotExposeSerializedDebugParameters()
        {
            var serializedDebugFields = typeof(VisibilityBufferResolvePass)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.GetCustomAttribute<SerializeField>() != null)
                .ToArray();

            Assert.That(serializedDebugFields, Is.Empty);
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture) field.GetValue(pass);
        }

        private static T GetFieldValue<T>(VisibilityBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferResolvePass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
