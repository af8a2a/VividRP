using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RTASInstanceDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredRTASInstanceDebugPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RTASInstanceDebugPass).AssemblyQualifiedName;

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }

            internal bool TryGetVisualizationMode(out RTASInstanceDebugVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_VisualizationMode", out value);
            }
        }

        [Serializable]
        private sealed class AutoRegisteredRTASBuildPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RTASBuildPass).AssemblyQualifiedName;
        }

        [Serializable]
        private sealed class AutoRegisteredFinalBlitPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(FinalBlitPass).AssemblyQualifiedName;
        }

        [Test]
        public void RTASInstanceDebugPassNode_DefinesAccelerationStructureInput_OutputTextureOutput_AndOverrideOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredRTASInstanceDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SceneAccelerationStructure"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_OutputTexture_In"), Is.Null);
                Assert.That(node.HasOverrideOption("m_OutputTexture"), Is.True);
                Assert.That(node.TryGetVisualizationMode(out var mode), Is.True);
                Assert.That(mode, Is.EqualTo(RTASInstanceDebugVisualizationMode.InstanceIndex));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesVisualizationModeEnumParameter_WhenRTASInstanceDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredRTASInstanceDebugPassNode();

                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters[0].FieldName, Is.EqualTo("m_VisualizationMode"));
                Assert.That(
                    result.Passes[0].EnumParameters[0].Value,
                    Is.EqualTo((int)RTASInstanceDebugVisualizationMode.InstanceIndex));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersBuildDebugAndBlitPasses_WhenRTASAndOutputAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var buildNode = new AutoRegisteredRTASBuildPassNode();
                var debugNode = new AutoRegisteredRTASInstanceDebugPassNode();
                var blitNode = new AutoRegisteredFinalBlitPassNode();

                RenderGraphTestUtility.AddTestNode(graph, blitNode);
                RenderGraphTestUtility.AddTestNode(graph, debugNode);
                RenderGraphTestUtility.AddTestNode(graph, buildNode);

                graph.Connect(
                    buildNode.GetOutputPortByName("m_SceneAccelerationStructure"),
                    debugNode.GetInputPortByName("m_SceneAccelerationStructure"));
                graph.Connect(
                    debugNode.GetOutputPortByName("m_OutputTexture"),
                    blitNode.GetInputPortByName("source"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(RTASBuildPass),
                    nameof(RTASInstanceDebugPass),
                    nameof(FinalBlitPass),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
