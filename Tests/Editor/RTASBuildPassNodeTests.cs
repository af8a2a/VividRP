using System;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RTASBuildPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredRTASBuildPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RTASBuildPass).AssemblyQualifiedName;

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Serializable]
        private sealed class AutoRegisteredRayTracingConsumerPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RayTracingConsumerPass).AssemblyQualifiedName;
        }

        [Test]
        public void RTASBuildPassNode_DefinesAccelerationStructureOutput_AndOverrideOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredRTASBuildPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetOutputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure_In"), Is.Null);
                Assert.That(node.HasOverrideOption("m_SceneAccelerationStructure"), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void RayTracingConsumerPassNode_DefinesAccelerationStructureInput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredRayTracingConsumerPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SceneAccelerationStructure"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private sealed class RayTracingConsumerPass : ComputePass
        {
            [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
            private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();

            public override void Create()
            {
            }

            public override void Prepare(ContextContainer frameData)
            {
            }

            public override void Record(ComputeGraphContext context)
            {
            }

            public override void Dispose()
            {
            }
        }
    }
}
