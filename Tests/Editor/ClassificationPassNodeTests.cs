using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class ClassificationPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredClassificationPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(ClassificationPass).AssemblyQualifiedName;
        }

        [Test]
        public void ClassificationPassNode_DefinesGBufferInputsAndClassificationOutputs()
        {
            var node = new AutoRegisteredClassificationPassNode();

            Assert.That(node.GetInputPortByName("m_GBuffer0"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_StandardMaterialIndices"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_FabricMaterialIndices"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ClearCoatMaterialIndices"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MaterialClassCounts"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_StandardIndirectArgs"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_FabricIndirectArgs"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ClearCoatIndirectArgs"), Is.Not.Null);
        }

        [Test]
        public void ClassificationPassNode_ExposesAsyncComputeOption()
        {
            var node = new AutoRegisteredClassificationPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.True);
            Assert.That(node.GetEnableAsyncCompute(), Is.False);
        }
    }
}
