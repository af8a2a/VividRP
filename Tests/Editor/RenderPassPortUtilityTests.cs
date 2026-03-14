using NUnit.Framework;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderPassPortUtilityTests
    {
        private sealed class DummyNode
        {
        }

        [Test]
        public void GetInputPortName_UsesFieldName_WhenReadOnly()
        {
            Assert.That(
                RenderPassPortUtility.GetInputPortName("Color", AccessFlags.Read),
                Is.EqualTo("Color"));
            Assert.That(
                RenderPassPortUtility.GetOutputPortName("Color", AccessFlags.Read),
                Is.Null);
        }

        [Test]
        public void GetOutputPortName_UsesFieldName_WhenWriteOnly()
        {
            Assert.That(
                RenderPassPortUtility.GetInputPortName("Color", AccessFlags.Write),
                Is.EqualTo("Color_In"));
            Assert.That(
                RenderPassPortUtility.GetOutputPortName("Color", AccessFlags.Write),
                Is.EqualTo("Color"));
        }

        [Test]
        public void GetPortNames_UseDirectionalSuffixes_WhenReadWrite()
        {
            Assert.That(
                RenderPassPortUtility.GetInputPortName("History", AccessFlags.ReadWrite),
                Is.EqualTo("History_In"));
            Assert.That(
                RenderPassPortUtility.GetOutputPortName("History", AccessFlags.ReadWrite),
                Is.EqualTo("History_Out"));
        }

        [Test]
        public void GetInputPortName_ReturnsNull_WhenPassOwnedOverrideableOverrideIsDisabled()
        {
            Assert.That(
                RenderPassPortUtility.GetInputPortName(
                    "Color",
                    AccessFlags.Write,
                    RenderGraphResourceBindingMode.PassOwnedOverrideable,
                    overrideEnabled: false),
                Is.Null);
        }

        [Test]
        public void GetInputPortName_ReturnsLegacyName_WhenPassOwnedOverrideableOverrideIsEnabled()
        {
            Assert.That(
                RenderPassPortUtility.GetInputPortName(
                    "Color",
                    AccessFlags.Write,
                    RenderGraphResourceBindingMode.PassOwnedOverrideable,
                    overrideEnabled: true),
                Is.EqualTo("Color_In"));
        }

        [Test]
        public void ResolveConnectedNode_ReturnsSharedNode_WhenReadWriteUsesSameResource()
        {
            var node = new DummyNode();

            var resolved = RenderPassPortUtility.ResolveConnectedNode(AccessFlags.ReadWrite, node, node);

            Assert.That(resolved, Is.SameAs(node));
        }

        [Test]
        public void ResolveConnectedNode_ReturnsNull_WhenReadWriteUsesDifferentResources()
        {
            var readNode = new DummyNode();
            var writeNode = new DummyNode();

            var resolved = RenderPassPortUtility.ResolveConnectedNode(AccessFlags.ReadWrite, readNode, writeNode);

            Assert.That(resolved, Is.Null);
        }
    }
}
