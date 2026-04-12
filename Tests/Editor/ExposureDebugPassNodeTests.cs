using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ExposureDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredExposureDebugPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(ExposureDebugPass).AssemblyQualifiedName;

            internal bool TryGetDebugExposure(out float value)
            {
                return TryGetFloatParameterValue("m_DebugExposure", out value);
            }

            internal bool TryGetMode(out ExposureDebugMode value)
            {
                return TryGetEnumParameterValue("m_Mode", out value);
            }
        }

        [Test]
        public void ExposureDebugPassNode_DefinesInputOutputAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredExposureDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetDebugExposure(out var debugExposure), Is.True);
                Assert.That(node.TryGetMode(out var mode), Is.True);
                Assert.That(debugExposure, Is.EqualTo(0f));
                Assert.That(mode, Is.EqualTo(ExposureDebugMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenExposureDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredExposureDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_DebugExposure",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_Mode"));
                Assert.That(result.Passes[0].EnumParameters.Single().Value, Is.EqualTo((int)ExposureDebugMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
