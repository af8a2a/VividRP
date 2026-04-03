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
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<OverlayDebugVolume>();

            try
            {
                volume.overlayAmount.overrideState = true;
                volume.overlayAmount.value = 0.75f;
                volume.arraySlice.overrideState = true;
                volume.arraySlice.value = 5;
                volume.exposure.overrideState = true;
                volume.exposure.value = 2f;
                volume.opacity.overrideState = true;
                volume.opacity.value = 0.4f;
                volume.visualizationMode.overrideState = true;
                volume.visualizationMode.value = OverlayDebugVisualizationMode.MotionVectors;
                volume.depthMode.overrideState = true;
                volume.depthMode.value = OverlayDebugDepthMode.Linear01;

                var settings = OverlayDebugPass.ResolveSettings(
                    0.1f,
                    1f,
                    -1f,
                    0.9f,
                    OverlayDebugVisualizationMode.Color,
                    OverlayDebugDepthMode.Raw,
                    volume);

                Assert.That(settings.overlayAmount, Is.EqualTo(0.75f));
                Assert.That(settings.arraySlice, Is.EqualTo(5));
                Assert.That(settings.exposure, Is.EqualTo(2f));
                Assert.That(settings.opacity, Is.EqualTo(0.4f));
                Assert.That(settings.visualizationMode, Is.EqualTo(OverlayDebugVisualizationMode.MotionVectors));
                Assert.That(settings.depthMode, Is.EqualTo(OverlayDebugDepthMode.Linear01));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
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
        public void ResolveVisualizationMode_UsesVisibilityBufferForUint2Textures()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.Auto,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R32G32_UInt
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.VisibilityBuffer));
        }

        [Test]
        public void ResolveVisualizationMode_PreservesExplicitAutoExposureMode()
        {
            var mode = OverlayDebugPass.ResolveVisualizationMode(
                OverlayDebugVisualizationMode.AutoExposure,
                new RenderGraphTextureDesc
                {
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat
                },
                null);

            Assert.That(mode, Is.EqualTo(OverlayDebugVisualizationMode.AutoExposure));
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
            var settings = OverlayDebugPass.ResolveSettings(
                0f,
                0f,
                32f,
                -1f,
                OverlayDebugVisualizationMode.Auto,
                OverlayDebugDepthMode.Raw,
                null);

            Assert.That(settings.exposure, Is.EqualTo(16f));
            Assert.That(settings.opacity, Is.EqualTo(0f));
        }

        [Test]
        public void ResolveSettings_PreservesFallbackDepthMode_WhenVolumeDoesNotOverrideIt()
        {
            var settings = OverlayDebugPass.ResolveSettings(
                0f,
                0f,
                0f,
                0.25f,
                OverlayDebugVisualizationMode.Depth,
                OverlayDebugDepthMode.Linear01,
                null);

            Assert.That(settings.opacity, Is.EqualTo(0.25f));
            Assert.That(settings.depthMode, Is.EqualTo(OverlayDebugDepthMode.Linear01));
        }

        [Test]
        public void OverlayDebugShader_SupportsTextureArraysAnchoredOverlayAndVisualizationModes()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#pragma target 4.5"));
            Assert.That(shaderSource, Does.Contain("TEXTURE2D_ARRAY(_DebugTextureArray);"));
            Assert.That(shaderSource, Does.Contain("TYPED_TEXTURE2D(float2, _DebugVisibilityTexture);"));
            Assert.That(shaderSource, Does.Contain("StructuredBuffer<uint> _AutoExposureHistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("StructuredBuffer<float4> _AutoExposureCurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("SAMPLE_TEXTURE2D_ARRAY(_DebugTextureArray"));
            Assert.That(shaderSource, Does.Contain("_OverlayRect"));
            Assert.That(shaderSource, Does.Contain("exp2(_DebugExposure)"));
            Assert.That(shaderSource, Does.Contain("Linear01Depth(depthValue, _ZBufferParams)"));
            Assert.That(shaderSource, Does.Contain("VIVID_OVERLAY_DEPTHMODE_LINEAR01"));
            Assert.That(shaderSource, Does.Contain("lerp(sourceColor, debugColor, saturate(_DebugOpacity))"));
            Assert.That(shaderSource, Does.Contain("motion * 0.5 + 0.5"));
            Assert.That(shaderSource, Does.Contain("VIVID_OVERLAY_MOTION_VECTOR_ARROW_SPACING"));
            Assert.That(shaderSource, Does.Contain("OverlayMotionVectorArrows"));
            Assert.That(shaderSource, Does.Contain("DistanceToSegment"));
            Assert.That(shaderSource, Does.Contain("ResolveMotionVectorCellCenterUv"));
            Assert.That(shaderSource, Does.Contain("SampleDebugTextureRaw(cellCenterUv).xy"));
            Assert.That(shaderSource, Does.Contain("VIVID_OVERLAY_VISUALIZATION_VISIBILITY_BUFFER"));
            Assert.That(shaderSource, Does.Contain("VIVID_OVERLAY_VISUALIZATION_AUTO_EXPOSURE"));
            Assert.That(shaderSource, Does.Contain("EvaluateAutoExposureDebugOverlay"));
            Assert.That(shaderSource, Does.Contain("SummarizeAutoExposureDebug"));
            Assert.That(shaderSource, Does.Contain("ResolveAutoExposureHistogramHeight"));
            Assert.That(shaderSource, Does.Contain("_AutoExposureDebugState"));
            Assert.That(shaderSource, Does.Contain("_AutoExposureRangeParams"));
            Assert.That(shaderSource, Does.Contain("UnpackVisibilityBufferValue"));
            Assert.That(shaderSource, Does.Contain("IsPackedVisibilityBufferValueValid"));
            Assert.That(shaderSource, Does.Contain("sampler_PointClamp"));
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
                "OverlayDebug.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
