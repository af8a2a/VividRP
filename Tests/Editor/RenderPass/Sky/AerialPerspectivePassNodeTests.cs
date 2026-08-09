using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class AtmosphericScatteringPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredAtmosphericScatteringPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(AtmosphericScatteringPass);
        }

        [Test]
        public void AtmosphericScatteringPassNode_DefinesExpectedInputAndOutputPorts()
        {
            var node = new AutoRegisteredAtmosphericScatteringPassNode();

            Assert.That(node.GetInputPortByName("m_ColorInput"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_VBufferLighting"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_AtmosphericScatteringLUT"), Is.Null);
            Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
        }
    }
}
