using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class AccelerationStructureResourceNodeDataTests
    {
        [Test]
        public void AccelerationStructureNode_DefinesOutPortOnly()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AccelerationStructureResourceNodeData();
                graph.AddNode(node);

                var output = node.GetOutputPortByName(AccelerationStructureResourceNodeData.OutputPortName);

                Assert.That(node.GetInputPortByName(AccelerationStructureResourceNodeData.InputPortName), Is.Null);
                Assert.That(output, Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void GetDescriptor_ReturnsDefaultAccelerationStructureDescriptor()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AccelerationStructureResourceNodeData();
                graph.AddNode(node);

                var descriptor = node.GetDescriptor();

                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.Name, Is.EqualTo("AccelerationStructure"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
