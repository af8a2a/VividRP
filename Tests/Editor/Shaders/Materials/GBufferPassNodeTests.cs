using System;
using System.Linq;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class GBufferPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredGBufferPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(GBufferPass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Serializable]
        private sealed class AutoRegisteredLightGridPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(LightGridPass);
        }

        [Test]
        public void GBufferPassNode_DefinesRenderListAndGBufferPorts()
        {
            var node = new AutoRegisteredGBufferPassNode();

            Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_VirtualTextureRenderList"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DecalDataBuffer"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_LayeredOffsetBuffer"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_LayeredLightListBuffer"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_LogBaseBuffer"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_GBufferDepth_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer0"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer1"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer2"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer3"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBuffer4"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_GBufferDepth_Out"), Is.Not.Null);
        }

        [Test]
        public void GBufferPassNode_HidesColorAttachmentInputs_AndExposesOverrideOptions()
        {
            var node = new AutoRegisteredGBufferPassNode();

            Assert.That(node.HasOverrideOption("m_GBuffer0"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer1"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer2"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer3"), Is.True);
            Assert.That(node.HasOverrideOption("m_GBuffer4"), Is.True);

            Assert.That(node.GetInputPortByName("m_GBuffer0_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer1_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer2_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer3_In"), Is.Null);
            Assert.That(node.GetInputPortByName("m_GBuffer4_In"), Is.Null);
        }

        [Test]
        public void Compile_OrdersLightGridBeforeGBuffer_WhenDecalClusterResourcesAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var gbufferNode = new AutoRegisteredGBufferPassNode();
                var lightGridNode = new AutoRegisteredLightGridPassNode();

                RenderGraphTestUtility.AddTestNode(graph, gbufferNode);
                RenderGraphTestUtility.AddTestNode(graph, lightGridNode);

                graph.Connect(
                    lightGridNode.GetOutputPortByName("m_DecalDataBuffer"),
                    gbufferNode.GetInputPortByName("m_DecalDataBuffer"));
                graph.Connect(
                    lightGridNode.GetOutputPortByName("m_LayeredOffsetBuffer"),
                    gbufferNode.GetInputPortByName("m_LayeredOffsetBuffer"));
                graph.Connect(
                    lightGridNode.GetOutputPortByName("m_LayeredLightListBuffer"),
                    gbufferNode.GetInputPortByName("m_LayeredLightListBuffer"));
                graph.Connect(
                    lightGridNode.GetOutputPortByName("m_LogBaseBuffer"),
                    gbufferNode.GetInputPortByName("m_LogBaseBuffer"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(LightGridPass),
                    nameof(GBufferPass),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
