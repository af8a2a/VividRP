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
    public class PreDepthPassTests
    {
        [Test]
        public void Resize_KeepsDefaultDepthAttachmentDepthOnly()
        {
            var pass = new PreDepthPass();

            pass.Resize(1280, 720);

            var depthTexture = GetDepthTexture(pass);

            Assert.That(depthTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(depthTexture.desc.Height, Is.EqualTo(720));
            Assert.That(depthTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.None));
            Assert.That(depthTexture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
        }

        [Test]
        public void Resize_PreservesBoundDepthFormatWithoutInjectingStencilFormat()
        {
            var pass = new PreDepthPass();

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

            pass.Resize(1920, 1080);

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

        [Test]
        public void Constructor_IncludesObjectMotionVectorRenderers_InPreDepthRenderList()
        {
            var pass = new PreDepthPass();
            var renderList = GetRenderList(pass);

            Assert.That(renderList.desc.ExcludeObjectMotionVectors, Is.False);
        }

        [Test]
        public void ResourceLayout_UsesReadWriteDepthAttachment()
        {
            var pass = new PreDepthPass();

            var resources = pass.Collect();

            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(resources.Textures[0].Name, Is.EqualTo("Depth"));
            Assert.That(resources.Textures[0].IsDepthAttachment, Is.True);
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.ReadWrite));
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

        private static RenderGraphRenderList GetRenderList(PreDepthPass pass)
        {
            var field = typeof(PreDepthPass).GetField("m_RenderList", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphRenderList)field.GetValue(pass);
        }
    }
}
