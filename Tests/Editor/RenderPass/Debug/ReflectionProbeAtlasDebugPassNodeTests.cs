using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReflectionProbeAtlasDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredReflectionProbeAtlasDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReflectionProbeAtlasDebugPass);

            internal bool TryGetMode(out ReflectionProbeAtlasDebugMode value)
            {
                return TryGetEnumParameterValue("m_Mode", out value);
            }

            internal bool TryGetArraySlice(out float value)
            {
                return TryGetFloatParameterValue("m_ArraySlice", out value);
            }

            internal bool TryGetMipLevel(out float value)
            {
                return TryGetFloatParameterValue("m_MipLevel", out value);
            }

            internal bool TryGetExposure(out float value)
            {
                return TryGetFloatParameterValue("m_Exposure", out value);
            }
        }

        [Test]
        public void ReflectionProbeAtlasDebugPassNode_DefinesDebugOutputAndInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReflectionProbeAtlasDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetOutputPortByName("m_DebugTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DebugTexture"), Is.Null);
                Assert.That(node.TryGetMode(out var mode), Is.True);
                Assert.That(node.TryGetArraySlice(out var arraySlice), Is.True);
                Assert.That(node.TryGetMipLevel(out var mipLevel), Is.True);
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(mode, Is.EqualTo(ReflectionProbeAtlasDebugMode.None));
                Assert.That(arraySlice, Is.EqualTo(0f));
                Assert.That(mipLevel, Is.EqualTo(0f));
                Assert.That(exposure, Is.EqualTo(0f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesFloatAndEnumParameters_WhenReflectionProbeAtlasDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReflectionProbeAtlasDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName), Is.EquivalentTo(new[]
                {
                    "m_ArraySlice",
                    "m_MipLevel",
                    "m_Exposure",
                }));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_Mode"));
                Assert.That(result.Passes[0].EnumParameters.Single().Value, Is.EqualTo((int)ReflectionProbeAtlasDebugMode.None));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void BuildRegistrations_IncludesReflectionProbeAtlasDebugPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(ReflectionProbeAtlasDebugPass) });

            Assert.That(
                registrations.Select(registration => registration.PassType),
                Contains.Item(typeof(ReflectionProbeAtlasDebugPass)));
        }
    }
}
