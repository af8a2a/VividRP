using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SliderDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredSliderDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(SliderDebugPass);

            internal bool TryGetSliderValue(out float value)
            {
                return TryGetFloatParameterValue("m_Slider", out value);
            }
        }

        [Test]
        public void SliderDebugPassNode_DefinesTwoInputsOneOutputAndSliderOption()
        {
            var node = new AutoRegisteredSliderDebugPassNode();

            Assert.That(node.GetInputPortByName("m_LeftTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_RightTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
            Assert.That(node.TryGetSliderValue(out var sliderValue), Is.True);
            Assert.That(sliderValue, Is.EqualTo(50f));
        }

        [Test]
        public void Compile_IncludesSliderFloatParameter_WhenSliderDebugPassNodeIsPresent()
        {
            var graph = new RenderGraphEditorGraph();
            var node = new AutoRegisteredSliderDebugPassNode();

            graph.AddNode(node);

            var result = RenderGraphCompiler.Compile(graph);

            Assert.That(result.Passes, Has.Count.EqualTo(1));
            Assert.That(result.Passes[0].FloatParameters, Has.Count.EqualTo(1));
            Assert.That(result.Passes[0].FloatParameters[0].FieldName, Is.EqualTo("m_Slider"));
            Assert.That(result.Passes[0].FloatParameters[0].Value, Is.EqualTo(50f));
        }
    }
}
