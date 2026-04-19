using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class GBufferPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredGBufferPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(GBufferPass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Test]
        public void GBufferPassNode_DefinesRenderListAndGBufferPorts()
        {
            var node = new AutoRegisteredGBufferPassNode();

            Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer0"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer1"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer2"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer3"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer4"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBufferDepth"), Is.Not.Null);
        }

        [Test]
        public void GBufferPassNode_HidesColorAttachmentInputs_AndExposesOverrideOptions()
        {
            var node = new AutoRegisteredGBufferPassNode();

            Assert.That(node.HasOverrideOption("m_GBuffer0"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer1"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer2"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer3"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer4"), Is.True);

            Assert.That(node.GetInputPortByName("m_GBuffer0_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer1_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer2_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer3_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer4_In"), Is.Null);
        }
    }
}
