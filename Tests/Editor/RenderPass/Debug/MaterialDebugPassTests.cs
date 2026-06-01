using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialDebugPassTests
    {
        [TearDown]
        public void TearDown()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [Test]
        public void Initialize_RegistersGBufferInputsColorOutputAndBypass()
        {
            IRenderPass renderPass = new MaterialDebugPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "SourceTexture",
                "DepthTexture",
                "GBuffer0",
                "GBuffer1",
                "GBuffer2",
                "GBuffer3",
                "GBuffer4",
                "OutputTexture",
            }));
            Assert.That(resources.Textures.Where(entry => entry.Access == AccessFlags.Read), Has.Exactly(7).Count);

            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));

            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_SourceTexture"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_OutputTexture"));
        }

        [Test]
        public void IsActive_TracksRenderingDebuggerMaterialMode()
        {
            var pass = new MaterialDebugPass();

            VividRenderingDebugDisplaySettings.Data.materialDebugMode = MaterialDebugVisualizationMode.None;
            Assert.That(pass.IsActive(new ContextContainer()), Is.False);

            VividRenderingDebugDisplaySettings.Data.materialDebugMode = MaterialDebugVisualizationMode.BaseColor;
            Assert.That(pass.IsActive(new ContextContainer()), Is.True);
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                materialDebugMode = MaterialDebugVisualizationMode.BakedGI,
                materialDebugExposure = 2.5f,
            };

            var settings = MaterialDebugPass.ResolveSettings(
                data,
                MaterialDebugVisualizationMode.BaseColor,
                0f);

            Assert.That(settings.visualizationMode, Is.EqualTo(MaterialDebugVisualizationMode.BakedGI));
            Assert.That(settings.exposure, Is.EqualTo(2.5f));
        }

        [Test]
        public void ResolveSettings_ClampsExposure()
        {
            var settings = MaterialDebugPass.ResolveSettings(
                new VividRenderingDebugSettingsData
                {
                    materialDebugExposure = 32f,
                },
                MaterialDebugVisualizationMode.BaseColor,
                0f);

            Assert.That(settings.exposure, Is.EqualTo(16f));
        }

        [Test]
        public void ResolveSettings_UsesPassDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = MaterialDebugPass.ResolveSettings(
                null,
                MaterialDebugVisualizationMode.NormalWS,
                1.5f);

            Assert.That(settings.visualizationMode, Is.EqualTo(MaterialDebugVisualizationMode.NormalWS));
            Assert.That(settings.exposure, Is.EqualTo(1.5f));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndDescriptor()
        {
            var pass = new MaterialDebugPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 1600;
            sourceTexture.desc.Height = 900;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            sourceTexture.desc.FilterMode = FilterMode.Point;
            sourceTexture.desc.WrapMode = TextureWrapMode.Repeat;
            sourceTexture.desc.Slices = 2;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1600));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(900));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(outputTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(outputTexture.desc.WrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(outputTexture.desc.Slices, Is.EqualTo(2));
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerModeAndExposure()
        {
            VividRenderingDebugDisplaySettings.Data.materialDebugMode = MaterialDebugVisualizationMode.MaterialId;
            VividRenderingDebugDisplaySettings.Data.materialDebugExposure = 4f;

            var pass = new MaterialDebugPass
            {
                VisualizationMode = MaterialDebugVisualizationMode.BaseColor,
                Exposure = -2f,
            };

            var frameData = new ContextContainer();
            frameData.GetOrCreate<VividCameraData>().actualWidth = 64;
            frameData.GetOrCreate<VividCameraData>().actualHeight = 64;

            pass.Prepare(frameData);

            var resolvedSettings = GetFieldValue<MaterialDebugPass.MaterialDebugSettingsData>(
                pass,
                "m_ResolvedSettings");
            Assert.That(resolvedSettings.visualizationMode, Is.EqualTo(MaterialDebugVisualizationMode.MaterialId));
            Assert.That(resolvedSettings.exposure, Is.EqualTo(4f));
        }

        [Test]
        public void Prepare_SetsSkipExecution_ForPreviewCamera()
        {
            PreviewRenderUtility preview = new();
            var pass = new MaterialDebugPass();

            try
            {
                var frameData = CreateFrameData(preview.camera);

                pass.Prepare(frameData);

                Assert.That(GetFieldValue<bool>(pass, "m_ShouldSkipExecution"), Is.True);
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static ContextContainer CreateFrameData(Camera camera)
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
            cameraData.actualWidth = 256;
            cameraData.actualHeight = 144;
            cameraData.pixelWidth = 256;
            cameraData.pixelHeight = 144;
            cameraData.pixelRect = new Rect(0f, 0f, 256f, 144f);
            return frameData;
        }

        private static RenderGraphTexture GetTextureField(MaterialDebugPass pass, string fieldName)
        {
            return GetFieldValue<RenderGraphTexture>(pass, fieldName);
        }

        private static T GetFieldValue<T>(MaterialDebugPass pass, string fieldName)
        {
            var field = typeof(MaterialDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
