using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class OverlayDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredOverlayDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(OverlayDebugPass);

            internal bool TryGetOverlayAmount(out float value)
            {
                return TryGetFloatParameterValue("m_OverlayAmount", out value);
            }

            internal bool TryGetArraySlice(out float value)
            {
                return TryGetFloatParameterValue("m_ArraySlice", out value);
            }

            internal bool TryGetExposure(out float value)
            {
                return TryGetFloatParameterValue("m_Exposure", out value);
            }

            internal bool TryGetOpacity(out float value)
            {
                return TryGetFloatParameterValue("m_Opacity", out value);
            }

            internal bool TryGetVisualizationMode(out OverlayDebugVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_VisualizationMode", out value);
            }

            internal bool TryGetDepthMode(out OverlayDebugDepthMode value)
            {
                return TryGetEnumParameterValue("m_DepthMode", out value);
            }

            internal bool TryGetDepthMipLevel(out float value)
            {
                return TryGetFloatParameterValue("m_DepthMipLevel", out value);
            }

            internal bool TryGetDepthRemapEnabled(out float value)
            {
                return TryGetFloatParameterValue("m_DepthRemapEnabled", out value);
            }

            internal bool TryGetDepthRemapMin(out float value)
            {
                return TryGetFloatParameterValue("m_DepthRemapMin", out value);
            }

            internal bool TryGetDepthRemapMax(out float value)
            {
                return TryGetFloatParameterValue("m_DepthRemapMax", out value);
            }
        }

        [Test]
        public void OverlayDebugPassNode_DefinesInputsOutputAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredOverlayDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DebugTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetOverlayAmount(out var overlayAmount), Is.True);
                Assert.That(node.TryGetArraySlice(out var arraySlice), Is.True);
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(node.TryGetOpacity(out var opacity), Is.True);
                Assert.That(node.TryGetVisualizationMode(out var visualizationMode), Is.True);
                Assert.That(node.TryGetDepthMode(out var depthMode), Is.True);
                Assert.That(node.TryGetDepthMipLevel(out var depthMipLevel), Is.True);
                Assert.That(node.TryGetDepthRemapEnabled(out var depthRemapEnabled), Is.True);
                Assert.That(node.TryGetDepthRemapMin(out var depthRemapMin), Is.True);
                Assert.That(node.TryGetDepthRemapMax(out var depthRemapMax), Is.True);
                Assert.That(overlayAmount, Is.EqualTo(0f));
                Assert.That(arraySlice, Is.EqualTo(0f));
                Assert.That(exposure, Is.EqualTo(0f));
                Assert.That(opacity, Is.EqualTo(1f));
                Assert.That(visualizationMode, Is.EqualTo(OverlayDebugVisualizationMode.Auto));
                Assert.That(depthMode, Is.EqualTo(OverlayDebugDepthMode.Raw));
                Assert.That(depthMipLevel, Is.EqualTo(0f));
                Assert.That(depthRemapEnabled, Is.EqualTo(0f));
                Assert.That(depthRemapMin, Is.EqualTo(0f));
                Assert.That(depthRemapMax, Is.EqualTo(1f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenOverlayDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredOverlayDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_OverlayAmount",
                    "m_ArraySlice",
                    "m_Exposure",
                    "m_Opacity",
                    "m_DepthMipLevel",
                    "m_DepthRemapEnabled",
                    "m_DepthRemapMin",
                    "m_DepthRemapMax",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(2));
                Assert.That(result.Passes[0].EnumParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_VisualizationMode",
                    "m_DepthMode",
                }));
                Assert.That(result.Passes[0].EnumParameters.Single(parameter => parameter.FieldName == "m_VisualizationMode").Value, Is.EqualTo((int)OverlayDebugVisualizationMode.Auto));
                Assert.That(result.Passes[0].EnumParameters.Single(parameter => parameter.FieldName == "m_DepthMode").Value, Is.EqualTo((int)OverlayDebugDepthMode.Raw));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
