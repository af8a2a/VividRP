using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class PreDepthPassTests
    {
        [Test]
        public void Prepare_KeepsDefaultDepthAttachmentDepthOnly()
        {
            var pass = new PreDepthPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var depthTexture = GetDepthTexture(pass);

            Assert.That(depthTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(depthTexture.desc.Height, Is.EqualTo(720));
            Assert.That(depthTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.None));
            Assert.That(depthTexture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
        }

        [Test]
        public void Prepare_PreservesBoundDepthFormatWithoutInjectingStencilFormat()
        {
            var pass = new PreDepthPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            var sharedDepth = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 200,
                    ColorFormat = GraphicsFormat.None,
                    DepthBufferBits = DepthBits.Depth16,
                    MsaaSamples = MSAASamples.MSAA4x,
                    Dimension = TextureDimension.Tex2DArray,
                    Slices = 2,
                    UseDynamicScale = true,
                    UseDynamicScaleExplicit = true,
                    ScaleFactor = new Vector2(0.5f, 0.5f),
                }
            };

            SetDepthTexture(pass, sharedDepth);

            pass.Prepare(frameData);

            Assert.That(sharedDepth.desc.Width, Is.EqualTo(1920));
            Assert.That(sharedDepth.desc.Height, Is.EqualTo(1080));
            Assert.That(sharedDepth.desc.ColorFormat, Is.EqualTo(GraphicsFormat.None));
            Assert.That(sharedDepth.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth16));
            Assert.That(sharedDepth.desc.MsaaSamples, Is.EqualTo(MSAASamples.MSAA4x));
            Assert.That(sharedDepth.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(sharedDepth.desc.Slices, Is.EqualTo(2));
            Assert.That(sharedDepth.desc.UseDynamicScale, Is.True);
            Assert.That(sharedDepth.desc.UseDynamicScaleExplicit, Is.True);
            Assert.That(sharedDepth.desc.ScaleFactor, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        private static RenderGraphTexture GetDepthTexture(PreDepthPass pass)
        {
            var field = typeof(PreDepthPass).GetField("m_DepthAttachment", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static void SetDepthTexture(PreDepthPass pass, RenderGraphTexture texture)
        {
            var field = typeof(PreDepthPass).GetField("m_DepthAttachment", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, texture);
        }
    }
}
