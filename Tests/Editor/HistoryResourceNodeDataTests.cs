using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class HistoryResourceNodeDataTests
    {
        [Test]
        public void HistoryNode_DefinesPrevCurrPorts()
        {
            var node = new HistoryResourceNodeData();

            var previousOutput = node.GetOutputPortByName(HistoryResourceNodeData.PreviousOutputPortName);
            var currentOutput = node.GetOutputPortByName(HistoryResourceNodeData.CurrentOutputPortName);

            Assert.That(node.GetInputPortByName("CurrIn"), Is.Null);
            Assert.That(previousOutput, Is.Not.Null);
            Assert.That(currentOutput, Is.Not.Null);
            Assert.That(node.IsPreviousOutputPort(previousOutput), Is.True);
            Assert.That(node.IsCurrentOutputPort(currentOutput), Is.True);
        }

        [Test]
        public void GetDescriptor_ReturnsDefaultHistoryDescriptor()
        {
            var node = new HistoryResourceNodeData();

            var descriptor = node.GetDescriptor();

            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.Width, Is.GreaterThan(0));
            Assert.That(descriptor.Height, Is.GreaterThan(0));
        }
    }
}
