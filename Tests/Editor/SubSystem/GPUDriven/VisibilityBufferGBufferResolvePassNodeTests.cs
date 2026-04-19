using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VisibilityBufferGBufferResolvePassNodeTests
    {
        [Serializable]
        private class AutoRegisteredVisibilityBufferGBufferResolvePassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferGBufferResolvePass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Serializable]
        private sealed class AutoRegisteredVisibilityBufferGBufferResolvePassOverrideNode : AutoRegisteredVisibilityBufferGBufferResolvePassNode
        {
            protected override bool GetPassOwnedResourceOverrideEnabled(
                System.Reflection.FieldInfo field,
                RenderGraphResource attr)
            {
                return field != null
                       && (field.Name == "m_GBuffer0"
                           || field.Name == "m_GBuffer1"
                           || field.Name == "m_GBuffer2"
                           || field.Name == "m_GBuffer3"
                           || field.Name == "m_GBuffer4")
                       || base.GetPassOwnedResourceOverrideEnabled(field, attr);
            }
        }

        [Serializable]
        private sealed class AutoRegisteredGBufferPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(GBufferPass);
        }

        [Serializable]
        private sealed class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DeferredLightingPass);
        }

        [Test]
        public void VisibilityBufferGBufferResolvePassNode_ExposesGBufferOutputsAndOverrideOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVisibilityBufferGBufferResolvePassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_VisibilityBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);

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

                Assert.That(node.GetOutputPortByName("m_GBuffer0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer1_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer2_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer3_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer4_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void VisibilityBufferGBufferResolvePassNode_DefinesGBufferInputs_WhenOverridesAreEnabled()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVisibilityBufferGBufferResolvePassOverrideNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_GBuffer0_In"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer1_In"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer2_In"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer3_In"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer4_In"), Is.Not.Null);

                Assert.That(node.GetOutputPortByName("m_GBuffer0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer1_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer2_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer3_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer4_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersGBufferResolveBeforeDeferredLighting_WhenGBufferIsShared()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var gbufferNode = new AutoRegisteredGBufferPassNode();
                var resolveNode = new AutoRegisteredVisibilityBufferGBufferResolvePassOverrideNode();
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, resolveNode);
                RenderGraphTestUtility.AddTestNode(graph, gbufferNode);

                graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer0"),
                    resolveNode.GetInputPortByName("m_GBuffer0_In"));
                graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer1"),
                    resolveNode.GetInputPortByName("m_GBuffer1_In"));
                graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer2"),
                    resolveNode.GetInputPortByName("m_GBuffer2_In"));
                graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer3"),
                    resolveNode.GetInputPortByName("m_GBuffer3_In"));
                graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer4"),
                    resolveNode.GetInputPortByName("m_GBuffer4_In"));

                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer0_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer0"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer1_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer1"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer2_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer2"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer3_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer3"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer4_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer4"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(GBufferPass),
                    nameof(VisibilityBufferGBufferResolvePass),
                    nameof(DeferredLightingPass),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
