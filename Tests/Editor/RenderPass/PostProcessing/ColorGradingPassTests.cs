using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ColorGradingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredColorGradingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ColorGradingPass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Test]
        public void Initialize_RegistersWriteOnlyColorGradingLut()
        {
            IRenderPass renderPass = new ColorGradingPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.Textures[0].Name, Is.EqualTo("ColorGradingTexture"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures[0].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[0].IsDepthAttachment, Is.False);
        }

        [Test]
        public void Prepare_ConfiguresHdrpCompatible3DLutDescriptor()
        {
            var pass = new ColorGradingPass();

            pass.Prepare(new ContextContainer());

            var field = typeof(ColorGradingPass).GetField("colorGradingTex", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(ColorGradingLutBuilder.LutSize));
            Assert.That(texture.desc.Height, Is.EqualTo(ColorGradingLutBuilder.LutSize));
            Assert.That(texture.desc.Slices, Is.EqualTo(ColorGradingLutBuilder.LutSize));
            Assert.That(texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(texture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void ColorGradingPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(ColorGradingPass)), Is.True);
        }

        [Test]
        public void ColorGradingPassNode_ExposesAsyncComputeOption()
        {
            var node = new AutoRegisteredColorGradingPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.True);
            Assert.That(node.GetEnableAsyncCompute(), Is.False);
        }

        [Test]
        public void ColorGradingPassNode_HidesInputPortAndKeepsOutputPort_ForPassOwnedLut()
        {
            var node = new AutoRegisteredColorGradingPassNode();

            Assert.That(node.HasOverrideOption("colorGradingTex"), Is.True);
            Assert.That(node.GetInputPortByName("colorGradingTex_In"), Is.Null);
            Assert.That(node.GetOutputPortByName("colorGradingTex"), Is.Not.Null);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsTrue_ForColorGradingPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(ColorGradingPass)), Is.True);
        }
    }
}
