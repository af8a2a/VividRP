using System;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class TemporalAAPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredTemporalAAPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(TemporalAAPass);
        }

        [Test]
        public void TemporalAAPassNode_DefinesColorInputAndTAAOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredTemporalAAPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_ColorInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectors"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void TemporalAAPassNode_HidesHistoryPorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredTemporalAAPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_HistoryColorPrevious"), Is.Null);
                Assert.That(node.GetInputPortByName("m_HistoryColorCurrent_In"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void TemporalAAPassNode_DoesNotExposeAsyncComputeOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredTemporalAAPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.HasAsyncComputeOption(), Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }

    public class CMAA2PassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredCMAA2PassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(CMAA2Pass);
        }

        [Test]
        public void CMAA2PassNode_DefinesColorInputAndOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredCMAA2PassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_ColorInput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void CMAA2PassNode_HidesWorkingResources()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredCMAA2PassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_CmaaEdgesTexture"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaDeferredBlendItemListHeadsTexture"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaShapeCandidatesBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaDeferredBlendItemListBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaDeferredBlendLocationListBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaControlBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_CmaaExecuteIndirectBuffer"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void CMAA2PassNode_DoesNotExposeAsyncComputeOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredCMAA2PassNode();
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
