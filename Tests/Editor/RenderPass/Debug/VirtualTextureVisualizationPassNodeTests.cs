using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureVisualizationPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredVirtualTextureVisualizationPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VirtualTextureVisualizationPass);
        }

        [Test]
        public void VirtualTextureVisualizationPassNode_DefinesOnlyResourcePorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureVisualizationPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_DoesNotPersistVisualizationParameters_WhenVirtualTextureVisualizationPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureVisualizationPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters, Is.Empty);
                Assert.That(result.Passes[0].EnumParameters, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
