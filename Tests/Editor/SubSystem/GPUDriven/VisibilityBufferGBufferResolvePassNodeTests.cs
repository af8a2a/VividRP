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
                           || field.Name == "m_GBuffer4"
                           || field.Name == "m_LayerAux0"
                           || field.Name == "m_LayerAux1")
                       || base.GetPassOwnedResourceOverrideEnabled(field, attr);
            }
        }

        [Serializable]
        private sealed class AutoRegisteredPreDepthPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(PreDepthPass);
        }

        [Serializable]
        private sealed class AutoRegisteredVisibilityBufferPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferPass);
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
                Assert.That(node.GetInputPortByName("m_Attributes0"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Attributes1"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Barycentrics"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_DualSlabSidecarTileList"),
                    Is.Null);
                Assert.That(
                    node.GetOutputPortByName("m_DualSlabSidecarTileList"),
                    Is.Null);
                Assert.That(
                    node.GetInputPortByName(
                        "m_DualSlabSidecarIndirectDrawArgs"),
                    Is.Null);
                Assert.That(
                    node.GetOutputPortByName(
                        "m_DualSlabSidecarIndirectDrawArgs"),
                    Is.Null);
                Assert.That(
                    node.HasOverrideOption("m_DualSlabSidecarTileList"),
                    Is.False);
                Assert.That(
                    node.HasOverrideOption(
                        "m_DualSlabSidecarIndirectDrawArgs"),
                    Is.False);

                Assert.That(node.HasOverrideOption("m_GBuffer0"), Is.True);
                Assert.That(node.HasOverrideOption("m_GBuffer1"), Is.True);
                Assert.That(node.HasOverrideOption("m_GBuffer2"), Is.True);
                Assert.That(node.HasOverrideOption("m_GBuffer3"), Is.True);
                Assert.That(node.HasOverrideOption("m_GBuffer4"), Is.True);
                Assert.That(node.HasOverrideOption("m_LayerAux0"), Is.True);
                Assert.That(node.HasOverrideOption("m_LayerAux1"), Is.True);

                Assert.That(node.GetInputPortByName("m_GBuffer0_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer1_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer2_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer3_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer4_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_LayerAux0_In"), Is.Null);
                Assert.That(node.GetInputPortByName("m_LayerAux1_In"), Is.Null);

                Assert.That(node.GetOutputPortByName("m_GBuffer0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer1_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer2_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer3_Out"), Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_GBuffer4_Out"),
                    Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_LayerAux0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_LayerAux1_Out"), Is.Not.Null);
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
                Assert.That(
                    node.GetInputPortByName("m_GBuffer4_In"),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_LayerAux0_In"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_LayerAux1_In"), Is.Not.Null);

                Assert.That(node.GetOutputPortByName("m_GBuffer0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer1_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer2_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBuffer3_Out"), Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_GBuffer4_Out"),
                    Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_LayerAux0_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_LayerAux1_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersResolveBeforeDeferredLighting_WhenResolvedGBufferIsConsumed()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var resolveNode = new AutoRegisteredVisibilityBufferGBufferResolvePassNode();
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, resolveNode);

                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer0_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer0"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_LayerAux0_Out"),
                    deferredNode.GetInputPortByName("m_LayerAux0"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_LayerAux1_Out"),
                    deferredNode.GetInputPortByName("m_LayerAux1"));
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
                    nameof(VisibilityBufferGBufferResolvePass),
                    nameof(DeferredLightingPass),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersPreDepthVisibilityResolveAndDeferred_AsOpaqueChain()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var resolveNode = new AutoRegisteredVisibilityBufferGBufferResolvePassNode();
                var preDepthNode = new AutoRegisteredPreDepthPassNode();
                var visibilityNode = new AutoRegisteredVisibilityBufferPassNode();
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, resolveNode);
                RenderGraphTestUtility.AddTestNode(graph, preDepthNode);
                RenderGraphTestUtility.AddTestNode(graph, visibilityNode);

                graph.Connect(
                    preDepthNode.GetOutputPortByName("m_DepthAttachment_Out"),
                    visibilityNode.GetInputPortByName("m_Depth_In"));
                graph.Connect(
                    visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                    resolveNode.GetInputPortByName("m_VisibilityBuffer"));
                graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Attributes0_Out"),
                    resolveNode.GetInputPortByName("m_Attributes0"));
                graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Attributes1_Out"),
                    resolveNode.GetInputPortByName("m_Attributes1"));
                graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Barycentrics_Out"),
                    resolveNode.GetInputPortByName("m_Barycentrics"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_GBuffer0_Out"),
                    deferredNode.GetInputPortByName("m_GBuffer0"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_LayerAux0_Out"),
                    deferredNode.GetInputPortByName("m_LayerAux0"));
                graph.Connect(
                    resolveNode.GetOutputPortByName("m_LayerAux1_Out"),
                    deferredNode.GetInputPortByName("m_LayerAux1"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName).ToArray(), Is.EqualTo(new[]
                {
                    nameof(PreDepthPass),
                    nameof(VisibilityBufferPass),
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
