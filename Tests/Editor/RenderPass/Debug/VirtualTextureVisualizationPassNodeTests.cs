using System;
using System.Linq;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureVisualizationPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredVirtualTextureVisualizationPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VirtualTextureVisualizationPass);

            internal bool TryGetOverlayAmount(out float value)
            {
                return TryGetFloatParameterValue("m_OverlayAmount", out value);
            }

            internal bool TryGetOpacity(out float value)
            {
                return TryGetFloatParameterValue("m_Opacity", out value);
            }

            internal bool TryGetDefaultVisualizationMode(out VirtualTextureVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_DefaultVisualizationMode", out value);
            }
        }

        [Test]
        public void VirtualTextureVisualizationPassNode_DefinesExpectedPortsAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureVisualizationPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetOverlayAmount(out float overlayAmount), Is.True);
                Assert.That(node.TryGetOpacity(out float opacity), Is.True);
                Assert.That(node.TryGetDefaultVisualizationMode(out VirtualTextureVisualizationMode visualizationMode), Is.True);
                Assert.That(overlayAmount, Is.EqualTo(0f));
                Assert.That(opacity, Is.EqualTo(1f));
                Assert.That(visualizationMode, Is.EqualTo(VirtualTextureVisualizationMode.PhysicalCache));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenVirtualTextureVisualizationPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureVisualizationPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_OverlayAmount",
                    "m_Opacity",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_DefaultVisualizationMode"));
                Assert.That(
                    result.Passes[0].EnumParameters.Single().Value,
                    Is.EqualTo((int)VirtualTextureVisualizationMode.PhysicalCache));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
