using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class RenderListResourceNodeDataTests
    {
        [Test]
        public void RenderListNode_DefinesOutPortOnly()
        {
            var node = new RenderListResourceNodeData();

            var output = node.GetOutputPortByName(RenderListResourceNodeData.OutputPortName);

            Assert.That(node.GetInputPortByName(RenderListResourceNodeData.InputPortName), Is.Null);
            Assert.That(output, Is.Not.Null);
        }

        [Test]
        public void GetDescriptor_ReturnsDefaultRenderListDescriptor()
        {
            var node = new RenderListResourceNodeData();

            var descriptor = node.GetDescriptor();

            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.ShaderTagNames, Is.Not.Null);
            Assert.That(descriptor.ShaderTagNames.Length, Is.GreaterThan(0));
        }
    }
}
