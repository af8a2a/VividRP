using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VisibilityBufferDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredVisibilityBufferDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferDebugPass);

            internal bool TryGetVisualizationMode(out VisibilityBufferDebugVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_VisualizationMode", out value);
            }

            internal bool TryGetExposure(out float value)
            {
                return TryGetFloatParameterValue("m_Exposure", out value);
            }

            internal bool TryGetAttributeComparisonMode(
                out VisibilityBufferAttributeComparisonMode value)
            {
                return TryGetEnumParameterValue(
                    "m_AttributeComparisonMode",
                    out value);
            }
        }

        [Test]
        public void VisibilityBufferDebugPassNode_DefinesInputOutputAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVisibilityBufferDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_VisibilityBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Attributes0"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetVisualizationMode(out var visualizationMode), Is.True);
                Assert.That(
                    node.TryGetAttributeComparisonMode(
                        out var attributeComparisonMode),
                    Is.True);
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(visualizationMode, Is.EqualTo(VisibilityBufferDebugVisualizationMode.Cluster));
                Assert.That(
                    attributeComparisonMode,
                    Is.EqualTo(VisibilityBufferAttributeComparisonMode.Disabled));
                Assert.That(exposure, Is.EqualTo(0f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenVisibilityBufferDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVisibilityBufferDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_Exposure",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(2));
                Assert.That(
                    result.Passes[0].EnumParameters.Select(parameter => parameter.FieldName),
                    Is.EquivalentTo(new[]
                    {
                        "m_VisualizationMode",
                        "m_AttributeComparisonMode",
                    }));
                Assert.That(
                    result.Passes[0].EnumParameters.Single(
                        parameter => parameter.FieldName == "m_VisualizationMode").Value,
                    Is.EqualTo((int)VisibilityBufferDebugVisualizationMode.Cluster));
                Assert.That(
                    result.Passes[0].EnumParameters.Single(
                        parameter => parameter.FieldName == "m_AttributeComparisonMode").Value,
                    Is.EqualTo((int)VisibilityBufferAttributeComparisonMode.Disabled));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
