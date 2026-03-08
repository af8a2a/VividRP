using System.Linq;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class ClassificationResourceNodeDataTests
    {
        [Test]
        public void ClassificationResourceNode_DefinesExpectedBufferPorts()
        {
            var node = new ClassificationResourceNodeData();

            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.StandardMaterialIndicesOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.FabricMaterialIndicesOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.ClearCoatMaterialIndicesOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.MaterialClassCountsOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.StandardIndirectArgsOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.FabricIndirectArgsOutputPortName), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(ClassificationResourceNodeData.ClearCoatIndirectArgsOutputPortName), Is.Not.Null);
        }

        [Test]
        public void ClassificationResourceNode_ProvidesExpectedDefaultDescriptors()
        {
            var node = new ClassificationResourceNodeData();

            var bufferDescriptors = node.EnumerateBufferDescriptors().ToDictionary(entry => entry.PortName, entry => entry.Descriptor);

            Assert.That(bufferDescriptors[ClassificationResourceNodeData.StandardMaterialIndicesOutputPortName].Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(bufferDescriptors[ClassificationResourceNodeData.StandardMaterialIndicesOutputPortName].Target, Is.EqualTo(UnityEngine.GraphicsBuffer.Target.Structured));
            Assert.That(bufferDescriptors[ClassificationResourceNodeData.MaterialClassCountsOutputPortName].Count, Is.EqualTo(3));
            Assert.That(bufferDescriptors[ClassificationResourceNodeData.StandardIndirectArgsOutputPortName].Count, Is.EqualTo(4));
            Assert.That(bufferDescriptors[ClassificationResourceNodeData.StandardIndirectArgsOutputPortName].Target, Is.EqualTo(UnityEngine.GraphicsBuffer.Target.Structured | UnityEngine.GraphicsBuffer.Target.IndirectArguments));
        }
    }
}
