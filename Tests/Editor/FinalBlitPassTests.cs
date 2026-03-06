using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class FinalBlitPassTests
    {
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
        public void Initialize_RegistersReadOnlySourceTexture()
        {
            IRenderPass renderPass = new FinalBlitPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.Textures[0].Name, Is.EqualTo("source"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[0].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[0].IsDepthAttachment, Is.False);
        }

        [Test]
        public void Prepare_CachesViewportFromCameraDimensions()
        {
            var pass = new FinalBlitPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            pass.Prepare(frameData);

            var viewportField = typeof(FinalBlitPass).GetField("m_Viewport", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(viewportField, Is.Not.Null);
            Assert.That((Rect)viewportField.GetValue(pass), Is.EqualTo(new Rect(0f, 0f, 320f, 180f)));
        }
    }
}
