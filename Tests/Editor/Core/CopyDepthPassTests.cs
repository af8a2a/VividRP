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
    public class CopyDepthPassTests
    {
        [Test]
        public void Initialize_RegistersDepthInputAndFloatOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new CopyDepthPass();

            var resources = renderPass.Initialize();
            var inputEntry = resources.Textures.Single(entry => entry.Name == "DepthAttachment");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "DepthTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(inputEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(inputEntry.IsDepthAttachment, Is.False);
            Assert.That(inputEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));

            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.IsDepthAttachment, Is.False);
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(outputEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));
        }

        [Test]
        public void Prepare_ResizesDefaultDescriptorsToCameraSize_WhenSourceDepthUsesPlaceholderSize()
        {
            var pass = new CopyDepthPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_DepthAttachment", 960, 540);
            AssertTextureSize(pass, "m_DepthTexture", 960, 540);
        }

        [Test]
        public void Prepare_UsesConfiguredSourceDepthSize_WhenInputDescriptorAlreadyHasDimensions()
        {
            var pass = new CopyDepthPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            var inputTexture = GetTextureField(pass, "m_DepthAttachment");
            inputTexture.desc.Width = 512;
            inputTexture.desc.Height = 256;
            inputTexture.desc.Dimension = TextureDimension.Tex2DArray;
            inputTexture.desc.Slices = 2;
            inputTexture.desc.UseDynamicScale = true;
            inputTexture.desc.UseDynamicScaleExplicit = true;

            pass.Prepare(frameData);

            Assert.That(inputTexture.desc.Width, Is.EqualTo(512));
            Assert.That(inputTexture.desc.Height, Is.EqualTo(256));

            var outputTexture = GetTextureField(pass, "m_DepthTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(512));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(256));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(outputTexture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));
            Assert.That(outputTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(outputTexture.desc.Slices, Is.EqualTo(2));
            Assert.That(outputTexture.desc.UseDynamicScale, Is.True);
            Assert.That(outputTexture.desc.UseDynamicScaleExplicit, Is.True);
        }

        private static void AssertTextureSize(CopyDepthPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetTextureField(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static RenderGraphTexture GetTextureField(CopyDepthPass pass, string fieldName)
        {
            var field = typeof(CopyDepthPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }
    }
}
