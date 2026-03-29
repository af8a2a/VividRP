using System;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
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
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredMotionVectorPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_FallbackRenderList"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_CameraDepthTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectorDepthTexture_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_MotionVectorTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_MotionVectorDepthTexture_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void MotionVectorPassNode_DoesNotExposeAsyncComputeOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredMotionVectorPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.HasAsyncComputeOption(), Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
