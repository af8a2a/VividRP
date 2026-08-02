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
    public sealed class VisibilityBufferGBufferResolvePassTests
    {
        [Test]
        public void Initialize_RegistersVisibilityDepthInputsAndGBufferOutputs()
        {
            IRenderPass renderPass = new VisibilityBufferGBufferResolvePass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "Depth");
            var gbuffer0Entry = resources.Textures.Single(entry => entry.Name == "GBuffer0");
            var gbuffer1Entry = resources.Textures.Single(entry => entry.Name == "GBuffer1");
            var gbuffer2Entry = resources.Textures.Single(entry => entry.Name == "GBuffer2");
            var gbuffer3Entry = resources.Textures.Single(entry => entry.Name == "GBuffer3");
            var gbuffer4Entry = resources.Textures.Single(entry => entry.Name == "GBuffer4");

            Assert.That(resources.Textures, Has.Length.EqualTo(7));
            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
            Assert.That(gbuffer0Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer0Entry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(gbuffer0Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer1Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer1Entry.AttachmentIndex, Is.EqualTo(1));
            Assert.That(gbuffer1Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(gbuffer2Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer2Entry.AttachmentIndex, Is.EqualTo(2));
            Assert.That(gbuffer2Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer3Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer3Entry.AttachmentIndex, Is.EqualTo(3));
            Assert.That(gbuffer3Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(gbuffer4Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer4Entry.AttachmentIndex, Is.EqualTo(4));
            Assert.That(gbuffer4Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_UsesVisibilityTextureSize_WhenConfigured()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var gbuffer0Texture = GetTextureField(pass, "m_GBuffer0");

            visibilityTexture.desc.Width = 1600;
            visibilityTexture.desc.Height = 900;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(gbuffer0Texture.desc.Width, Is.EqualTo(1600));
            Assert.That(gbuffer0Texture.desc.Height, Is.EqualTo(900));
            Assert.That(gbuffer0Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_DoesNotAllocate_WhenFrameDataIsStable()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                pass.Prepare(frameData);

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Prepare_DoesNotOverwriteOverriddenGBufferDescriptors()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var externalGBuffer0 = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 240,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                }
            };

            SetTextureField(pass, "m_GBuffer0", externalGBuffer0);

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(externalGBuffer0.desc.Width, Is.EqualTo(320));
            Assert.That(externalGBuffer0.desc.Height, Is.EqualTo(240));
            Assert.That(externalGBuffer0.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void VisibilityBufferGBufferResolvePassSource_BindsTexturesAndDrawsFullscreen()
        {
            var passSource = File.ReadAllText(GetPassSourcePath());

            Assert.That(passSource, Does.Contain("CoreUtils.DrawFullScreen("));
            Assert.That(passSource, Does.Contain("m_VisibilityBuffer"));
            Assert.That(passSource, Does.Contain("m_DepthTexture"));
            Assert.That(passSource, Does.Contain("m_GBuffer0"));
            Assert.That(passSource, Does.Contain("m_GBuffer3"));
            Assert.That(passSource, Does.Contain("m_GBuffer4"));
            Assert.That(typeof(VisibilityBufferGBufferResolvePass).IsSubclassOf(typeof(UnsafePass)), Is.True);
            Assert.That(passSource, Does.Contain("VirtualTextureFeedbackBindingUtility.BindFeedbackTargets("));
            Assert.That(passSource, Does.Contain("ClearRandomWriteTargets()"));
        }

        [Test]
        public void VisibilityBufferGBufferResolveShader_ReconstructsMaterialSurfaceFromVisibility()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("GBuffer.hlsl"));
            Assert.That(shaderSource, Does.Contain("VividVisibilityBuffer.hlsl"));
            Assert.That(shaderSource, Does.Contain("VividBarycentric.hlsl"));
            Assert.That(shaderSource, Does.Contain("PackVividGBufferSurfaceData("));
            Assert.That(shaderSource, Does.Contain("UnpackVisibilityBufferValue("));
            Assert.That(shaderSource, Does.Contain("IsPackedVisibilityBufferValueValid("));
            Assert.That(shaderSource, Does.Contain("CalculateFullBarycentric("));
            Assert.That(shaderSource, Does.Contain("VividSurfaceSampling.hlsl"));
            Assert.That(shaderSource, Does.Contain("PullSurfaceBindingData(result.materialData.SurfaceBindingIndex)"));
            Assert.That(shaderSource, Does.Contain("VividSampleBaseColorGrad("));
            Assert.That(shaderSource, Does.Contain("VividSampleNormalGrad("));
            Assert.That(shaderSource, Does.Contain("VividCreateSurfaceSampleContextGrad("));
            Assert.That(shaderSource, Does.Contain("VividSurfaceHasNormal("));
            Assert.That(shaderSource, Does.Contain("VividSampleMaskGrad("));
            Assert.That(shaderSource, Does.Contain("VIVID_VT_ENABLE_FEEDBACK_RW"));
            Assert.That(shaderSource, Does.Contain("VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE"));
            Assert.That(shaderSource, Does.Not.Contain("GetBindlessTexture2D("));
            Assert.That(shaderSource, Does.Not.Contain("GPUDriven/Bindless.hlsl"));
            Assert.That(shaderSource, Does.Contain("ComputeDoubleSidedNormalFlipSign("));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2"));
            Assert.That(shaderSource, Does.Contain("SampleVividProbeVolume("));
            Assert.That(shaderSource, Does.Contain("surfaceData.builtinData = CreateVividBuiltinData("));
            Assert.That(shaderSource, Does.Contain("VividHasProbeVolumeGI() ? 1.0f : 0.0f"));
            Assert.That(shaderSource, Does.Contain("discard;"));
            Assert.That(shaderSource, Does.Contain("ResolveVisibilityDepth("));
            Assert.That(shaderSource, Does.Contain("IsVisibilitySampleVisible("));
            Assert.That(shaderSource, Does.Contain("IsSceneDepthValid("));
            Assert.That(shaderSource, Does.Contain("abs(visibilityDepth - sceneDepth)"));
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture) field.GetValue(pass);
        }

        private static void SetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }

        private static string GetPassSourcePath()
        {
            var passPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                "Runtime",
                "RenderPass",
                "Core",
                "GPUDriven",
                "VisibilityBufferGBufferResolvePass.cs"));

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
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
                "GPUDriven",
                "VisibilityBufferGBufferResolve.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
