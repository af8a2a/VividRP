using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class ResourceNodeDataTests
    {
        [Test]
        public void TextureNode_DefinesOutPortOnly()
        {
            var node = new TextureResourceNodeData();

            Assert.That(node.GetInputPortByName(TextureResourceNodeData.InputPortName), Is.Null);
            Assert.That(node.GetOutputPortByName(TextureResourceNodeData.OutputPortName), Is.Not.Null);
        }

        [Test]
        public void BufferNode_DefinesOutPortOnly()
        {
            var node = new BufferResourceNodeData();

            Assert.That(node.GetInputPortByName(BufferResourceNodeData.InputPortName), Is.Null);
            Assert.That(node.GetOutputPortByName(BufferResourceNodeData.OutputPortName), Is.Not.Null);
        }

        [Test]
        public void ClassificationResourceNode_DefinesMultipleOutPortsOnly()
        {
            var node = new ClassificationResourceNodeData();

            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.StandardMaterialIndicesOutputPortName), Is.Not.Null);
        }
    }
}
