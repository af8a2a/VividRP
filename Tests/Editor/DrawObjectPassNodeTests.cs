using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DrawObjectPassNodeTests
    {
        private sealed class DerivedDrawObjectPass : DrawObjectPass
        {
        }

        [Serializable]
        private sealed class AutoRegisteredDrawObjectPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DrawObjectPass);
        }

        [Serializable]
        private sealed class AutoRegisteredDerivedDrawObjectPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DerivedDrawObjectPass);
        }

        [Test]
        public void DrawObjectPassNode_DefinesRenderListInputPort()
        {
            var node = new AutoRegisteredDrawObjectPassNode();

            var renderListInput = node.GetInputPortByName("m_RenderList");
            var colorOutput = node.GetOutputPortByName("m_ColorTarget");
            var depthOutput = node.GetOutputPortByName("m_DepthTarget");

            Assert.That(renderListInput, Is.Not.Null);
            Assert.That(colorOutput, Is.Not.Null);
            Assert.That(depthOutput, Is.Not.Null);
        }

        [Test]
        public void DerivedDrawObjectPassNode_DefinesInheritedPorts()
        {
            var node = new AutoRegisteredDerivedDrawObjectPassNode();

            Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_DepthTarget"), Is.Not.Null);
        }
    }
}
