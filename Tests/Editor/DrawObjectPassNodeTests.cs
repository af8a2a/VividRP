using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DrawObjectPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredDrawObjectPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DrawObjectPass).AssemblyQualifiedName;
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
    }
}
