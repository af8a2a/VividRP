using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingReblurPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReblurPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingReblurPass);
        }

        [Test]
        public void Pass_PreservesReblurHistoriesWithoutSameFrameConsumer()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingReblurPass)),
                Is.True);
        }

        [Test]
        public void Initialize_DefinesDiffuseSpecularSignalsGuidesAndResolvedOutput()
        {
            IRenderPass renderPass = new ReferencedPathTracingReblurPass();

            var resources = renderPass.Initialize();

            AssertRead(resources, "DiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "SpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "PathTracingEmission", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdViewZ", GraphicsFormat.R32_SFloat);
            AssertRead(resources, "MotionVectors", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(
                resources,
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);

            AssertWrite(resources, "DenoisedDiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "DenoisedSpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "ReblurResolvedColor", GraphicsFormat.R32G32B32A32_SFloat);
        }

        [Test]
        public void SharedConstants_MatchOfficialReblurConstantBufferLayout()
        {
            Assert.That(Marshal.SizeOf<ReblurSharedConstants>(), Is.EqualTo(848));
        }

        [Test]
        public void RenderGraphNode_ExposesExternalSignalsAndKeepsTransientsInternal()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReblurPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DiffuseInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_EmissionInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ViewZInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectorsInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_NormalRoughnessInput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ResolvedColor"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Tiles"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_Tiles"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static void AssertRead(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Read), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
        }

        private static void AssertWrite(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Write), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
            Assert.That(resource.Texture.desc.EnableRandomWrite, Is.True, name);
        }
    }
}
