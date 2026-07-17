using System;
using NUnit.Framework;
using VividRP.Runtime;
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
        public void DrawObjectPassNode_UsesEmbeddedRenderListDescriptorByDefault()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                var renderListInput = node.GetInputPortByName("m_RenderList");
                var descriptorOption = node.GetNodeOptionByName(
                    RenderGraphPassRenderListDescParameterUtility.GetOptionName("m_RenderListDesc"));

                Assert.That(renderListInput, Is.Null);
                Assert.That(descriptorOption, Is.Not.Null);
                Assert.That(descriptorOption.TryGetValue<RenderGraphRenderListDesc>(out var descriptor), Is.True);
                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Opaque));
                Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Null);
                Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DrawObjectPassNode_DefinesRenderListInput_WhenOverrideIsEnabled()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                var overrideOption = node.GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName("m_RenderList"));
                Assert.That(overrideOption, Is.Not.Null);
                Assert.That(overrideOption.TrySetValue(true), Is.True);
                node.DefineNode();

                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DerivedDrawObjectPassNode_InheritsEmbeddedRenderListAndAttachmentLayout()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDerivedDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Null);
                Assert.That(
                    node.GetNodeOptionByName(RenderGraphPassRenderListDescParameterUtility.GetOptionName("m_RenderListDesc")),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Null);
                Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
