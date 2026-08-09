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
    public sealed class ExposureDebugPassTests
    {
        [Test]
        public void Initialize_RegistersSourceAndOutputTextures()
        {
            IRenderPass renderPass = new ExposureDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndFormat_WhenConfigured()
        {
            var pass = new ExposureDebugPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 1280;
            sourceTexture.desc.Height = 720;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                exposureMode = ExposureDebugMode.HistogramView,
                debugExposure = 2f,
                centerHistogramAroundMiddleGrey = true,
                showTonemapCurveAlongHistogramView = false,
                displayMaskOnly = true,
                displayOnSceneOverlay = false,
            };

            var settings = ExposureDebugPass.ResolveSettings(data);

            Assert.That(settings.debugExposure, Is.EqualTo(2f));
            Assert.That(settings.mode, Is.EqualTo(ExposureDebugMode.HistogramView));
            Assert.That(settings.centerHistogramAroundMiddleGrey, Is.True);
            Assert.That(settings.showTonemapCurveAlongHistogramView, Is.False);
            Assert.That(settings.displayMaskOnly, Is.True);
            Assert.That(settings.displayOnSceneOverlay, Is.False);
        }

        [Test]
        public void ResolveSettings_UsesDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = ExposureDebugPass.ResolveSettings(null);

            Assert.That(settings.debugExposure, Is.EqualTo(0f));
            Assert.That(settings.mode, Is.EqualTo(ExposureDebugMode.None));
            Assert.That(settings.centerHistogramAroundMiddleGrey, Is.False);
            Assert.That(settings.showTonemapCurveAlongHistogramView, Is.True);
            Assert.That(settings.displayMaskOnly, Is.False);
            Assert.That(settings.displayOnSceneOverlay, Is.True);
        }

        private static RenderGraphTexture GetTextureField(ExposureDebugPass pass, string fieldName)
        {
            var field = typeof(ExposureDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }
    }
}
