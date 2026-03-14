using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class MotionVectorPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredMotionVectorPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(MotionVectorPass).AssemblyQualifiedName;
        }

        [Test]
        public void MotionVectorPassNode_DefinesDepthInputAndMotionOutputs()
        {
            var node = new AutoRegisteredMotionVectorPassNode();

            Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_CameraDepthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MotionVectorTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MotionVectorDepthTexture"), Is.Not.Null);
        }

        [Test]
        public void MotionVectorPassNode_DoesNotExposeAsyncComputeOption()
        {
            var node = new AutoRegisteredMotionVectorPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.False);
        }
    }
}
