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
    public sealed class OverlayDebugPassTests
    {
        [Test]
        public void Initialize_RegistersSourceDebugAndOutputTextures()
        {
            IRenderPass renderPass = new OverlayDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var debugEntry = resources.Textures.Single(entry => entry.Name == "DebugTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(debugEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndFormat_WhenConfigured()
        {
            var pass = new OverlayDebugPass();
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
            Assert.That(GetVectorField(pass, "m_OverlayRect"), Is.EqualTo(new Vector4(0.65f, 0.65f, 0.35f, 0.35f)));
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                overlayAmount = 0.75f,
                arraySlice = 5,
                overlayExposure = 2f,
                overlayOpacity = 0.4f,
                visualizationMode = OverlayDebugVisualizationMode.MotionVectors,
                depthMode = OverlayDebugDepthMode.Linear01,
                depthMipLevel = 0.5f,
                depthRemapEnabled = true,
                depthRemapMin = 0.2f,
                depthRemapMax = 0.75f,
                channelMode = OverlayDebugChannelMode.Green,
            };

            var settings = OverlayDebugPass.ResolveSettings(data);

            Assert.That(settings.overlayAmount, Is.EqualTo(0.75f));
            Assert.That(settings.arraySlice, Is.EqualTo(5));
            Assert.That(settings.exposure, Is.EqualTo(2f));
            Assert.That(settings.opacity, Is.EqualTo(0.4f));
            Assert.That(settings.visualizationMode, Is.EqualTo(OverlayDebugVisualizationMode.MotionVectors));
            Assert.That(settings.depthMode, Is.EqualTo(OverlayDebugDepthMode.Linear01));
            Assert.That(settings.depthMipLevel, Is.EqualTo(0.5f));
            Assert.That(settings.depthRemapEnabled, Is.True);
            Assert.That(settings.depthRemapMin, Is.EqualTo(0.2f));
            Assert.That(settings.depthRemapMax, Is.EqualTo(0.75f));
            Assert.That(settings.channelMode, Is.EqualTo(OverlayDebugChannelMode.Green));
        }

        [Test]
        public void ResolveVisualizationMode_UsesDepthForSingleChannelTextures()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.Auto,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R32_SFloat
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.Depth));
        }

        [Test]
        public void ResolveVisualizationMode_UsesMotionVectorsForTwoChannelTextures()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.Auto,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R16G16_SFloat
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.MotionVectors));
        }

        [Test]
        public void ResolveVisualizationMode_UsesColorForUint2Textures_WhenVisibilityBufferDebugIsSeparatePass()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.Auto,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R32G32_UInt
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.Color));
        }

        [Test]
        public void ResolveVisualizationMode_PreservesExplicitMotionVectorMode()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.MotionVectors,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.MotionVectors));
        }

        [Test]
        public void ResolveVisualizationMode_PreservesExplicitColorMode()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.Color,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.Color));
        }

        [Test]
        public void ResolveVisualizationMode_NormalizesRemovedVisibilityBufferMode()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                (OverlayDebugVisualizationMode)4,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.Color));
        }

        [Test]
        public void ResolveSliceIndex_ClampsToValidArrayRange()
        {
            Assert.That(OverlayDebugPass.ResolveSliceIndex(5, 4), Is.EqualTo(3));
            Assert.That(OverlayDebugPass.ResolveSliceIndex(-1, 4), Is.EqualTo(0));
            Assert.That(OverlayDebugPass.ResolveSliceIndex(0, 0), Is.EqualTo(0));
        }

        [Test]
        public void ResolveSettings_ClampsExposureIntoSupportedRange()
        {
            var settings = OverlayDebugPass.ResolveSettings(new VividRenderingDebugSettingsData
            {
                overlayExposure = 32f,
                overlayOpacity = -1f,
                depthMipLevel = 2f,
                depthRemapMax = 0.5f,
                depthRemapMin = 0.75f,
                channelMode = (OverlayDebugChannelMode)999,
            });

            Assert.That(settings.exposure, Is.EqualTo(16f));
            Assert.That(settings.opacity, Is.EqualTo(0f));
            Assert.That(settings.depthMipLevel, Is.EqualTo(1f));
            Assert.That(settings.depthRemapMin, Is.EqualTo(0.5f));
            Assert.That(settings.depthRemapMax, Is.EqualTo(0.5f));
            Assert.That(settings.channelMode, Is.EqualTo(OverlayDebugChannelMode.RGB));
        }

        [Test]
        public void ResolveSettings_UsesDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = OverlayDebugPass.ResolveSettings(null);

            Assert.That(settings.overlayAmount, Is.EqualTo(0f));
            Assert.That(settings.arraySlice, Is.EqualTo(0));
            Assert.That(settings.exposure, Is.EqualTo(0f));
            Assert.That(settings.opacity, Is.EqualTo(1f));
            Assert.That(settings.visualizationMode, Is.EqualTo(OverlayDebugVisualizationMode.Auto));
            Assert.That(settings.depthMode, Is.EqualTo(OverlayDebugDepthMode.Raw));
            Assert.That(settings.depthMipLevel, Is.EqualTo(0f));
            Assert.That(settings.depthRemapEnabled, Is.False);
            Assert.That(settings.depthRemapMin, Is.EqualTo(0f));
            Assert.That(settings.depthRemapMax, Is.EqualTo(1f));
            Assert.That(settings.channelMode, Is.EqualTo(OverlayDebugChannelMode.RGB));
        }

        [Test]
        public void ResolveDepthMipIndex_MapsNormalizedValueToMipChain()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                UseMipMap = true,
                MipCount = 5,
            };

            Assert.That(OverlayDebugPass.ResolveDepthMipIndex(0f, descriptor, null), Is.EqualTo(0));
            Assert.That(OverlayDebugPass.ResolveDepthMipIndex(0.5f, descriptor, null), Is.EqualTo(2));
            Assert.That(OverlayDebugPass.ResolveDepthMipIndex(1f, descriptor, null), Is.EqualTo(4));
            Assert.That(OverlayDebugPass.ResolveDepthMipIndex(0.75f, new RenderGraphTextureDesc(), null), Is.EqualTo(0));
        }

        [Test]
        public void ResolveDepthRemapParams_UsesDefaultsWhenDisabledAndCameraRangeWhenEnabled()
        {
            var disabled = OverlayDebugPass.ResolveDepthRemapParams(false, 0.2f, 0.8f, 0.3f, 100f);
            AssertDepthRemap(disabled, 0f, 1f, 0f, 1f);

            var enabled = OverlayDebugPass.ResolveDepthRemapParams(true, 0.2f, 0.8f, 0.3f, 100f);
            AssertDepthRemap(enabled, 0.2f, 0.8f, 0.3f, 100f);

            var clamped = OverlayDebugPass.ResolveDepthRemapParams(true, 0.9f, 0.25f, -1f, 0f);
            AssertDepthRemap(clamped, 0.25f, 0.25f, 0.0001f, 0.0002f);
        }

        [Test]
        public void NormalizeChannelMode_PreservesSupportedModes()
        {
            Assert.That(
                OverlayDebugPass.NormalizeChannelMode(OverlayDebugChannelMode.Red),
                Is.EqualTo(OverlayDebugChannelMode.Red));
            Assert.That(
                OverlayDebugPass.NormalizeChannelMode(OverlayDebugChannelMode.Alpha),
                Is.EqualTo(OverlayDebugChannelMode.Alpha));
        }

        private static void AssertDepthRemap(Vector4 actual, float x, float y, float z, float w)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(z).Within(0.0001f));
            Assert.That(actual.w, Is.EqualTo(w).Within(0.0001f));
        }
        private static RenderGraphTexture GetTextureField(OverlayDebugPass pass, string fieldName)
        {
            var field = typeof(OverlayDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static Vector4 GetVectorField(OverlayDebugPass pass, string fieldName)
        {
            var field = typeof(OverlayDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (Vector4)field.GetValue(pass);
        }
    }
}
