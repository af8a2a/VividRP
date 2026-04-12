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
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<ExposureDebugVolume>();

            try
            {
                volume.mode.overrideState = true;
                volume.mode.value = ExposureDebugMode.HistogramView;
                volume.debugExposure.overrideState = true;
                volume.debugExposure.value = 2f;
                volume.centerHistogramAroundMiddleGrey.overrideState = true;
                volume.centerHistogramAroundMiddleGrey.value = true;
                volume.showTonemapCurveAlongHistogramView.overrideState = true;
                volume.showTonemapCurveAlongHistogramView.value = false;
                volume.displayMaskOnly.overrideState = true;
                volume.displayMaskOnly.value = true;
                volume.displayOnSceneOverlay.overrideState = true;
                volume.displayOnSceneOverlay.value = false;

                var settings = ExposureDebugPass.ResolveSettings(
                    -1f,
                    ExposureDebugMode.SceneEV100Values,
                    volume);

                Assert.That(settings.debugExposure, Is.EqualTo(2f));
                Assert.That(settings.mode, Is.EqualTo(ExposureDebugMode.HistogramView));
                Assert.That(settings.centerHistogramAroundMiddleGrey, Is.True);
                Assert.That(settings.showTonemapCurveAlongHistogramView, Is.False);
                Assert.That(settings.displayMaskOnly, Is.True);
                Assert.That(settings.displayOnSceneOverlay, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ResolveSettings_UsesHdrpStyleDefaults_WhenVolumeMissing()
        {
            var settings = ExposureDebugPass.ResolveSettings(
                0f,
                ExposureDebugMode.None,
                null);

            Assert.That(settings.debugExposure, Is.EqualTo(0f));
            Assert.That(settings.mode, Is.EqualTo(ExposureDebugMode.None));
            Assert.That(settings.centerHistogramAroundMiddleGrey, Is.False);
            Assert.That(settings.showTonemapCurveAlongHistogramView, Is.True);
            Assert.That(settings.displayMaskOnly, Is.False);
            Assert.That(settings.displayOnSceneOverlay, Is.True);
        }

        [Test]
        public void ExposureDebugShader_ContainsDedicatedSceneMeteringAndHistogramPasses()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("Name \"SceneEV100\""));
            Assert.That(shaderSource, Does.Contain("Name \"Metering\""));
            Assert.That(shaderSource, Does.Contain("Name \"Histogram\""));
            Assert.That(shaderSource, Does.Contain("StructuredBuffer<uint> _AutoExposureHistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("StructuredBuffer<float4> _AutoExposureCurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("ExposureDebugSummary SummarizeExposureDebug()"));
            Assert.That(shaderSource, Does.Contain("ResolveMeteringWeight"));
            Assert.That(shaderSource, Does.Contain("ComputePixelPercentile"));
            Assert.That(shaderSource, Does.Contain("GetTonemappedValueAtLocation"));
            Assert.That(shaderSource, Does.Contain("DrawHeatSideBar("));
            Assert.That(shaderSource, Does.Contain("DrawHistogramFrame("));
            Assert.That(shaderSource, Does.Contain("DrawLiteralCurrentExposure("));
            Assert.That(shaderSource, Does.Contain("DrawLiteralTargetExposure("));
            Assert.That(shaderSource, Does.Contain("DrawLiteralExposureCompensation("));
            Assert.That(shaderSource, Does.Contain("FragSceneEV100"));
            Assert.That(shaderSource, Does.Contain("FragMetering"));
            Assert.That(shaderSource, Does.Contain("FragHistogram"));
            Assert.That(shaderSource, Does.Contain("VividGetOneOverPreExposure()"));
        }

        private static RenderGraphTexture GetTextureField(ExposureDebugPass pass, string fieldName)
        {
            var field = typeof(ExposureDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static string GetShaderSourcePath()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "ExposureDebug.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
