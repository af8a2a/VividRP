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
        public void DrawObjectPassNode_DefinesRenderListInputWriteOnlyColorAttachmentPortsAndDepthReadWritePorts()
        {
            var node = new AutoRegisteredDrawObjectPassNode();

            var renderListInput = node.GetInputPortByName("m_RenderList");
            var colorInput = node.GetInputPortByName("m_ColorTarget_In");
            var colorReadWriteOutput = node.GetOutputPortByName("m_ColorTarget_Out");
            var colorPlainOutput = node.GetOutputPortByName("m_ColorTarget");
            var depthInput = node.GetInputPortByName("m_DepthTarget_In");
            var depthOutput = node.GetOutputPortByName("m_DepthTarget_Out");

            Assert.That(renderListInput, Is.Not.Null);
            Assert.That(colorInput, Is.Not.Null);
            Assert.That(colorReadWriteOutput, Is.Not.Null);
            Assert.That(colorPlainOutput, Is.Null);
            Assert.That(depthInput, Is.Not.Null);
            Assert.That(depthOutput, Is.Not.Null);
        }

        [Test]
        public void DerivedDrawObjectPassNode_DefinesInheritedWriteOnlyColorAttachmentPorts()
        {
            var node = new AutoRegisteredDerivedDrawObjectPassNode();

            Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Null);
            Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
        }
    }
}
