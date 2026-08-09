using System;
using System.Linq;
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
    public class ScreenSpaceLensFlarePassTests
    {
        private sealed class AutoRegisteredScreenSpaceLensFlarePassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ScreenSpaceLensFlarePass);
        }

        [Test]
        public void Initialize_RegistersBloomMipAndTransientIntermediateResources()
        {
            IRenderPass renderPass = new ScreenSpaceLensFlarePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "BloomTexture",
                "ScreenSpaceLensFlareBloomMipTexture",
                "ScreenSpaceLensFlareResult",
                "ScreenSpaceLensFlareStreakTmp",
                "ScreenSpaceLensFlareStreakTmp2",
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "BloomTexture").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(
                textureEntries.Single(entry => entry.Name == "ScreenSpaceLensFlareBloomMipTexture").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                textureEntries.Single(entry => entry.Name == "ScreenSpaceLensFlareResult").Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(textureEntries.Single(entry => entry.Name == "ScreenSpaceLensFlareResult").IsTransient, Is.True);
            Assert.That(textureEntries.Single(entry => entry.Name == "ScreenSpaceLensFlareStreakTmp").IsTransient, Is.True);
            Assert.That(textureEntries.Single(entry => entry.Name == "ScreenSpaceLensFlareStreakTmp2").IsTransient, Is.True);
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void ScreenSpaceLensFlarePass_InheritsFromUnsafePass_AndAllowsGlobalState()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(ScreenSpaceLensFlarePass)), Is.True);
            Assert.That(typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(ScreenSpaceLensFlarePass)), Is.True);
        }

        [Test]
        public void ScreenSpaceLensFlarePassNode_DefinesBloomReadWriteAndMipInputPorts()
        {
            var node = new AutoRegisteredScreenSpaceLensFlarePassNode();

            Assert.That(node.GetInputPortByName("bloomTexture_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("bloomTexture_Out"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("bloomMipTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("bloomMipTexture"), Is.Null);
            Assert.That(node.GetInputPortByName("resultTexture_In"), Is.Null);
            Assert.That(node.GetOutputPortByName("resultTexture_Out"), Is.Null);
        }

        [Test]
        public void BloomPass_ExposesScreenSpaceLensFlareMipOutput()
        {
            IRenderPass renderPass = new BloomPass();

            var resources = renderPass.Initialize();
            var mipEntry = resources.Textures.Single(entry => entry.Name == "ScreenSpaceLensFlareBloomMipTexture");
            var field = typeof(BloomPass).GetField(
                "screenSpaceLensFlareBloomMipTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var attr = field?.GetCustomAttribute<RenderGraphResource>();

            Assert.That(mipEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.BindingMode, Is.EqualTo(RenderGraphResourceBindingMode.PassOwnedOverrideable));
        }
    }
}
