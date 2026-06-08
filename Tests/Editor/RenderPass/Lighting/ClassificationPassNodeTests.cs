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
            Assert.That(node.GetOutputPortByName("m_MaterialTileFeatureFlags"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MaterialFeatureTileList"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MaterialFeatureIndirectArgs"), Is.Not.Null);
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

            Assert.That(node.HasOverrideOption("m_MaterialTileFeatureFlags"), Is.True);
            Assert.That(node.HasOverrideOption("m_MaterialFeatureTileList"), Is.True);
            Assert.That(node.HasOverrideOption("m_MaterialFeatureIndirectArgs"), Is.True);

            Assert.That(node.GetInputPortByName("m_MaterialTileFeatureFlags_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_MaterialFeatureTileList_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_MaterialFeatureIndirectArgs_In"), Is.Null);
        }
    }
}
