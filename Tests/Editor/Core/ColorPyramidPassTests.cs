using System;
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
    public sealed class ColorPyramidPassTests
    {
        [Test]
        public void Initialize_RegistersSourceCurrentPyramidAndSpdAtomicBuffer()
        {
            IRenderPass renderPass = new ColorPyramidPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EqualTo(new[] { "source", "ColorPyramid" }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "ColorPyramid").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(GetResourceAttribute("m_CurrentColorPyramid").BindingMode, Is.EqualTo(RenderGraphResourceBindingMode.PassOwnedOverrideable));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EqualTo(new[] { "ColorPyramidGlobalAtomic" }));
        }

        [Test]
        public void ColorPyramidPass_UsesStandardComputePassRecording()
        {
            Assert.That(typeof(IRenderGraphRecordingPass).IsAssignableFrom(typeof(ColorPyramidPass)), Is.False);
        }

        [Test]
        public void ColorPyramidPass_DeclaresRenderGraphSideEffect()
        {
            Assert.That(typeof(IRenderGraphSideEffectPass).IsAssignableFrom(typeof(ColorPyramidPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresMippedRandomWriteTextureAndSpdDispatch()
        {
            var pass = new ColorPyramidPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var colorPyramid = GetTextureField(pass, "m_CurrentColorPyramid");
                Assert.That(colorPyramid.desc.Width, Is.EqualTo(1920));
                Assert.That(colorPyramid.desc.Height, Is.EqualTo(1080));
                Assert.That(colorPyramid.desc.UseMipMap, Is.True);
                Assert.That(colorPyramid.desc.AutoGenerateMips, Is.False);
                Assert.That(colorPyramid.desc.EnableRandomWrite, Is.True);
                Assert.That(colorPyramid.desc.FilterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(colorPyramid.desc.MipCount, Is.EqualTo(11));
                Assert.That(colorPyramid.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));

                Assert.That(GetIntField(pass, "m_DispatchGroupCountX"), Is.EqualTo(30));
                Assert.That(GetIntField(pass, "m_DispatchGroupCountY"), Is.EqualTo(17));
                Assert.That(GetIntField(pass, "m_NumWorkGroups"), Is.EqualTo(510));

                var atomicBuffer = GetBufferField(pass, "m_GlobalAtomicBuffer");
                Assert.That(atomicBuffer.desc.Count, Is.EqualTo(1));
                Assert.That(atomicBuffer.desc.Stride, Is.EqualTo(sizeof(uint) * 6));
                Assert.That(atomicBuffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));

                var importedBuffer = GetGraphicsBufferField(pass, "m_GlobalAtomicImportedBuffer");
                Assert.That(importedBuffer, Is.Not.Null);
                Assert.That(importedBuffer.count, Is.EqualTo(1));
                Assert.That(importedBuffer.stride, Is.EqualTo(sizeof(uint) * 6));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void BindMipFallback_UsesLastAvailableMipView()
        {
            Assert.That(GetBoundMipIndex(0, 11), Is.EqualTo(0));
            Assert.That(GetBoundMipIndex(10, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(11, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(12, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(12, 13), Is.EqualTo(12));
        }

        private static RenderGraphTexture GetTextureField(ColorPyramidPass pass, string fieldName)
        {
            var field = typeof(ColorPyramidPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on ColorPyramidPass");
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static RenderGraphBuffer GetBufferField(ColorPyramidPass pass, string fieldName)
        {
            var field = typeof(ColorPyramidPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on ColorPyramidPass");
            return (RenderGraphBuffer)field.GetValue(pass);
        }

        private static GraphicsBuffer GetGraphicsBufferField(ColorPyramidPass pass, string fieldName)
        {
            var field = typeof(ColorPyramidPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on ColorPyramidPass");
            return (GraphicsBuffer)field.GetValue(pass);
        }

        private static int GetIntField(ColorPyramidPass pass, string fieldName)
        {
            var field = typeof(ColorPyramidPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on ColorPyramidPass");
            return (int)field.GetValue(pass);
        }

        private static int GetBoundMipIndex(int shaderMipIndex, int mipCount)
        {
            var method = typeof(ColorPyramidPass).GetMethod("GetBoundMipIndex", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "GetBoundMipIndex method not found on ColorPyramidPass");
            return (int)method.Invoke(null, new object[] { shaderMipIndex, mipCount });
        }

        private static RenderGraphResource GetResourceAttribute(string fieldName)
        {
            var field = typeof(ColorPyramidPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on ColorPyramidPass");
            var attribute = field.GetCustomAttribute<RenderGraphResource>();
            Assert.That(attribute, Is.Not.Null, $"Field '{fieldName}' has no RenderGraphResource attribute");
            return attribute;
        }
    }
}
