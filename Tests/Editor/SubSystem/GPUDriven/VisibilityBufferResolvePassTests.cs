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
    public sealed class VisibilityBufferResolvePassTests
    {
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
        public void VisibilityBufferResolvePassSource_BindsTexturesAndDrawsFullscreen()
        {
            var passSource = File.ReadAllText(GetPassSourcePath());

            Assert.That(passSource, Does.Contain("CoreUtils.DrawFullScreen("));
            Assert.That(passSource, Does.Contain("m_VisibilityBuffer"));
            Assert.That(passSource, Does.Contain("m_DepthTexture"));
            Assert.That(passSource, Does.Contain("m_DebugMode"));
            Assert.That(passSource, Does.Contain("m_WireframeThickness"));
        }

        [Test]
        public void VisibilityBufferResolveShader_ReconstructsTrianglesAndSupportsDebugModes()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl\""));
            Assert.That(shaderSource, Does.Contain("VIVID_VISIBILITY_RESOLVE_DEBUG_WIREFRAME"));
            Assert.That(shaderSource, Does.Contain("UnpackVisibilityBufferValue("));
            Assert.That(shaderSource, Does.Contain("IsPackedVisibilityBufferValueValid("));
            Assert.That(shaderSource, Does.Contain("CalculateFullBarycentric("));
            Assert.That(shaderSource, Does.Contain("PullIndex(result.meshlet"));
            Assert.That(shaderSource, Does.Contain("ScreenCoordsToNDC(input.positionCS)"));
            Assert.That(shaderSource, Does.Contain("ResolveVisibilityDepth("));
            Assert.That(shaderSource, Does.Contain("IsVisibilitySampleVisible("));
            Assert.That(shaderSource, Does.Contain("IsSceneDepthValid("));
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture) field.GetValue(pass);
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
                "VisibilityBufferResolvePass.cs"));

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
                "VisibilityBufferResolve.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
