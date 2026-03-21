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
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class AutoRegisteredRTASBuildPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RTASBuildPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class AutoRegisteredDirectionalRayTracedShadowPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DirectionalRayTracedShadowPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DeferredLightingPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
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
                graph.AddNode(node);

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

                graph.AddNode(deferredNode);
                graph.AddNode(shadowNode);
                graph.AddNode(buildNode);

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
