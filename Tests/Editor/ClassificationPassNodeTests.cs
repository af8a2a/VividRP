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
            internal override Type GetRegisteredPassType() => typeof(ClassificationPass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
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
            Assert.That(node.GetOutputPortByName("m_MaterialTileClasses"), Is.Not.Null);
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

        [Test]
        public void ClassificationPassNode_HidesBufferInputs_AndExposesOverrideOptions()
        {
            var node = new AutoRegisteredClassificationPassNode();

            Assert.That(node.HasOverrideOption("m_StandardMaterialIndices"), Is.True);
            Assert.That(node.HasOverrideOption("m_FabricMaterialIndices"), Is.True);
            Assert.That(node.HasOverrideOption("m_ClearCoatMaterialIndices"), Is.True);
            Assert.That(node.HasOverrideOption("m_MaterialTileClasses"), Is.True);
            Assert.That(node.HasOverrideOption("m_MaterialClassCounts"), Is.True);
            Assert.That(node.HasOverrideOption("m_StandardIndirectArgs"), Is.True);
            Assert.That(node.HasOverrideOption("m_FabricIndirectArgs"), Is.True);
            Assert.That(node.HasOverrideOption("m_ClearCoatIndirectArgs"), Is.True);

            Assert.That(node.GetInputPortByName("m_StandardMaterialIndices_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_FabricMaterialIndices_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_ClearCoatMaterialIndices_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_MaterialTileClasses_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_MaterialClassCounts_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_StandardIndirectArgs_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_FabricIndirectArgs_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_ClearCoatIndirectArgs_In"), Is.Null);
        }
    }
}
