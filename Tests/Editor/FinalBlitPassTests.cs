using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class FinalBlitPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredFinalBlitPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(FinalBlitPass).AssemblyQualifiedName;
        }

        [Test]
        public void GetCameraBackBufferTextureUVOrigin_UsesBottomLeft_ForSceneAndPreviewAndTargetTexture()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetCameraBackBufferTextureUVOrigin",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.SceneView, false }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Preview, false }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Game, true }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
        }

        [Test]
        public void GetCameraBackBufferTextureUVOrigin_UsesPlatformBackBufferOrientation_ForGameCamera()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetCameraBackBufferTextureUVOrigin",
                BindingFlags.Static | BindingFlags.NonPublic);
            var expected = SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Game, false }),
                Is.EqualTo(expected));
        }

        [Test]
        public void GetFinalBlitScaleBias_FlipsY_WhenOriginsDiffer()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetFinalBlitScaleBias",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (Vector4)method.Invoke(null, new object[] { new Vector2(1f, 1f), TextureUVOrigin.BottomLeft, TextureUVOrigin.TopLeft }),
                Is.EqualTo(new Vector4(1f, -1f, 0f, 1f)));
            Assert.That(
                (Vector4)method.Invoke(null, new object[] { new Vector2(1f, 1f), TextureUVOrigin.BottomLeft, TextureUVOrigin.BottomLeft }),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
        }

        [Test]
        public void ShouldSetViewport_IsFalse_ForSceneView()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "ShouldSetViewport",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new object[] { CameraType.SceneView }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { CameraType.Game }), Is.True);
        }

        [Test]
        public void GetViewport_UsesCameraPixelRect_WhenAvailable()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetViewport",
                BindingFlags.Static | BindingFlags.NonPublic);
            var cameraData = new VividCameraData
            {
                pixelRect = new Rect(12f, 34f, 320f, 180f),
                actualWidth = 640,
                actualHeight = 360,
            };

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (Rect)method.Invoke(null, new object[] { cameraData }),
                Is.EqualTo(new Rect(12f, 34f, 320f, 180f)));
        }

        [Test]
        public void Initialize_RegistersReadOnlySourceAndColorGradingTextures()
        {
            IRenderPass renderPass = new FinalBlitPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.Textures[0].Name, Is.EqualTo("source"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[0].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[0].IsDepthAttachment, Is.False);
            Assert.That(resources.Textures[1].Name, Is.EqualTo("ColorGradingTexture"));
            Assert.That(resources.Textures[1].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[1].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[1].IsDepthAttachment, Is.False);
        }

        [Test]
        public void Prepare_CachesViewportFromCameraDimensions()
        {
            var pass = new FinalBlitPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.pixelRect = new Rect(4f, 8f, 320f, 180f);

            pass.Prepare(frameData);

            var viewportField = typeof(FinalBlitPass).GetField("m_Viewport", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(viewportField, Is.Not.Null);
            Assert.That((Rect)viewportField.GetValue(pass), Is.EqualTo(new Rect(4f, 8f, 320f, 180f)));
        }

        [Test]
        public void VividRPCoreResources_DeclaresFinalBlitShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.FinalBlitShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<VividRP.Runtime.ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/FinalBlit"));
        }

        [Test]
        public void FinalBlitShader_ContainsColorGradingLogic_AndSharedBlitShaderDoesNot()
        {
            var finalBlitShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "FinalBlit.shader"));
            var blitShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Blit.shader"));
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "FinalBlitPass.cs"));
            var frameContextSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));

            Assert.That(finalBlitShaderSource, Does.Contain("Shader \"Hidden/VividRP/FinalBlit\""));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividColorGradingLut"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividColorGradingParams"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposureBuffer"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposurePreExposureBuffer"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposureParams"));
            Assert.That(finalBlitShaderSource, Does.Contain("oneOverPreExposure"));
            Assert.That(finalBlitShaderSource, Does.Contain("ApplyLut3D("));

            Assert.That(blitShaderSource, Does.Contain("Shader \"Hidden/VividRP/Blit\""));
            Assert.That(blitShaderSource, Does.Not.Contain("_VividColorGradingLut"));
            Assert.That(blitShaderSource, Does.Not.Contain("_VividColorGradingParams"));
            Assert.That(blitShaderSource, Does.Not.Contain("ApplyLut3D("));

            Assert.That(passSource, Does.Contain("resources.FinalBlitShader"));
            Assert.That(passSource, Does.Contain("resources.AutoExposureCompute"));
            Assert.That(passSource, Does.Contain("m_EnableExposure"));
            Assert.That(passSource, Does.Contain("m_AutoExposureSettings.mode == AutoExposureMode.Manual"));
            Assert.That(passSource, Does.Contain("m_ExposureData.currentExposureBuffer ?? m_ExposureData.previousExposureBuffer ?? defaultExposureBuffer"));
            Assert.That(passSource, Does.Not.Contain("SetBuffer(AutoExposurePreExposureBufferId"));
            Assert.That(frameContextSource, Does.Contain("AutoExposureShaderBindings.BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(passSource, Does.Contain("AutoExposureRuntimeManager.CommitFrame(m_Camera);"));
            Assert.That(passSource, Does.Not.Contain("resources.BlitShader"));
        }

        [Test]
        public void FinalBlitPassNode_DoesNotExposeAsyncComputeOption()
        {
            var node = new AutoRegisteredFinalBlitPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.False);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForFinalBlitPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(FinalBlitPass)), Is.False);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
