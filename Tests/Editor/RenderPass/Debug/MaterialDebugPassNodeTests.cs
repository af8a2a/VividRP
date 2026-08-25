using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredMaterialDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(MaterialDebugPass);

            internal bool TryGetVisualizationMode(out MaterialDebugVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_VisualizationMode", out value);
            }

            internal bool TryGetExposure(out float value)
            {
                return TryGetFloatParameterValue("m_Exposure", out value);
            }
        }

        [Test]
        public void MaterialDebugPassNode_DefinesInputsOutputAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredMaterialDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer0"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer1"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer2"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer3"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GBuffer4"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_VisibilityBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MaterialTileFeatureFlags"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetVisualizationMode(out var visualizationMode), Is.True);
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(visualizationMode, Is.EqualTo(MaterialDebugVisualizationMode.None));
                Assert.That(exposure, Is.EqualTo(0f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenMaterialDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredMaterialDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_Exposure",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_VisualizationMode"));
                Assert.That(
                    result.Passes[0].EnumParameters.Single().Value,
                    Is.EqualTo((int)MaterialDebugVisualizationMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void BuildRegistrations_IncludesMaterialDebugPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(MaterialDebugPass) });

            Assert.That(registrations.Select(registration => registration.PassType), Contains.Item(typeof(MaterialDebugPass)));
        }
    }
}
