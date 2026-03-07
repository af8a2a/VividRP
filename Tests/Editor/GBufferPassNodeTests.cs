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
            protected override string RegisteredPassTypeName => typeof(GBufferPass).AssemblyQualifiedName;
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
            Assert.That(node.GetOutputPortByName("m_GBufferDepth"), Is.Not.Null);
        }
    }
}
