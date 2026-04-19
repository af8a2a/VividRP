using System;
using System.Linq;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureDemoPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredVirtualTextureDemoPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VirtualTextureDemoPass);

            internal bool TryGetDefaultDebugMode(out VirtualTextureDebugMode value)
            {
                return TryGetEnumParameterValue("m_DefaultDebugMode", out value);
            }
        }

        [Test]
        public void VirtualTextureDemoPassNode_DefinesExpectedPortsAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureDemoPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
                Assert.That(node.TryGetDefaultDebugMode(out VirtualTextureDebugMode debugMode), Is.True);
                Assert.That(debugMode, Is.EqualTo(VirtualTextureDebugMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesEnumParameters_WhenVirtualTextureDemoPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureDemoPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters, Is.Empty);
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_DefaultDebugMode"));
                Assert.That(result.Passes[0].EnumParameters.Single().Value, Is.EqualTo((int)VirtualTextureDebugMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
