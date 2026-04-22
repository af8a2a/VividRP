using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DirectionalRayTracedShadowPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredRTASBuildPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(RTASBuildPass);
        }

        [Serializable]
        private sealed class AutoRegisteredDirectionalRayTracedShadowPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DirectionalRayTracedShadowPass);
        }

        [Serializable]
        private class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DeferredLightingPass);
        }

        [Serializable]
        private sealed class AutoRegisteredDeferredLightingPassShadowOverrideNode : AutoRegisteredDeferredLightingPassNode
        {
            protected override bool GetPassOwnedResourceOverrideEnabled(
                System.Reflection.FieldInfo field,
                RenderGraphResource attr)
            {
                return (field != null && field.Name == "m_DirectionalShadowTexture")
                       || base.GetPassOwnedResourceOverrideEnabled(field, attr);
            }
        }

        [Test]
        public void DirectionalRayTracedShadowPassNode_DefinesExpectedPorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDirectionalRayTracedShadowPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer1"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DirectionalShadowTexture_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DirectionalShadowTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersBuildShadowAndDeferredLightingPasses_WhenDirectionalShadowIsConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var buildNode = new AutoRegisteredRTASBuildPassNode();
                var shadowNode = new AutoRegisteredDirectionalRayTracedShadowPassNode();
                var deferredNode = new AutoRegisteredDeferredLightingPassShadowOverrideNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, shadowNode);
                RenderGraphTestUtility.AddTestNode(graph, buildNode);

                graph.Connect(
                    buildNode.GetOutputPortByName("m_SceneAccelerationStructure"),
                    shadowNode.GetInputPortByName("m_SceneAccelerationStructure"));
                graph.Connect(
                    shadowNode.GetOutputPortByName("m_DirectionalShadowTexture"),
                    deferredNode.GetInputPortByName("m_DirectionalShadowTexture"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(RTASBuildPass),
                    nameof(DirectionalRayTracedShadowPass),
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
