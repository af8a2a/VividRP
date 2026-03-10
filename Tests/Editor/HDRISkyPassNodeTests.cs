using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class HDRISkyPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredHDRISkyPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(HDRISkyPass).AssemblyQualifiedName;
        }

        [Test]
        public void HDRISkyPassNode_DefinesDepthInputAndColorOutputPorts()
        {
            var node = new AutoRegisteredHDRISkyPassNode();

            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Not.Null);
        }
    }
}
