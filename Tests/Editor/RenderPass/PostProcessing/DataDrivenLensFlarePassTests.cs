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
    public class DataDrivenLensFlarePassTests
    {
        private sealed class AutoRegisteredDataDrivenLensFlarePassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(DataDrivenLensFlarePass);
        }

        [Test]
        public void Initialize_RegistersSourceAndDepthResources()
        {
            IRenderPass renderPass = new DataDrivenLensFlarePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "DepthTexture",
                "source",
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "source").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "source").Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Texture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void DataDrivenLensFlarePass_InheritsFromUnsafePass_AndAllowsGlobalState()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(DataDrivenLensFlarePass)), Is.True);
            Assert.That(typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(DataDrivenLensFlarePass)), Is.True);
        }

        [Test]
        public void DataDrivenLensFlarePassNode_DefinesSourceReadWriteAndDepthInputPorts()
        {
            var node = new AutoRegisteredDataDrivenLensFlarePassNode();

            Assert.That(node.GetInputPortByName("source_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("source_Out"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("depthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("depthTexture"), Is.Null);
        }
    }
}
