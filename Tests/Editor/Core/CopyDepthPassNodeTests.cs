using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class CopyDepthPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredCopyDepthPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(CopyDepthPass);
        }

        [Test]
        public void CopyDepthPassNode_DefinesDepthInputAndTextureOutputPorts()
        {
            var node = new AutoRegisteredCopyDepthPassNode();

            Assert.That(node.GetInputPortByName("m_DepthAttachment"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_DepthTexture"), Is.Not.Null);
        }

        [Test]
        public void CopyDepthPassNode_ExposesAsyncComputeOption()
        {
            var node = new AutoRegisteredCopyDepthPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.True);
        }
    }
}
