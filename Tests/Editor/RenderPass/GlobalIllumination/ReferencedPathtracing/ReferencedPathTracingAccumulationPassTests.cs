using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingAccumulationPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredAccumulationPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingAccumulationPass);
        }

        [Test]
        public void Pass_PreservesHistoryWhenOutputHasNoSameFrameConsumer()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingAccumulationPass)),
                Is.True);
        }

        [Test]
        public void Initialize_DefinesFp32SampleHistoryAndResolvedResources()
        {
            IRenderPass renderPass = new ReferencedPathTracingAccumulationPass();

            var resources = renderPass.Initialize();

            Assert.That(
                resources.Textures.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "PathTracingSampleRadiance",
                    "PathTracingAccumulationPrevious",
                    "PathTracingAccumulationCurrent",
                    "PathTracingResolvedColor"
                }));
            Assert.That(
                resources.Textures.All(resource =>
                    resource.Texture.desc.ColorFormat == GraphicsFormat.R32G32B32A32_SFloat),
                Is.True);
            Assert.That(
                resources.Textures.Single(resource => resource.Name == "PathTracingSampleRadiance").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource => resource.Name == "PathTracingAccumulationPrevious").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource => resource.Name == "PathTracingAccumulationCurrent").Access,
                Is.EqualTo(AccessFlags.WriteAll));
            Assert.That(
                resources.Textures.Single(resource => resource.Name == "PathTracingResolvedColor").Access,
                Is.EqualTo(AccessFlags.WriteAll));
        }

        [Test]
        public void Prepare_ResizesHistoryAndResolvedTargetsToCameraDimensions()
        {
            var cameraObject = new GameObject("ReferencedPathTracingAccumulationPassTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                var pass = new ReferencedPathTracingAccumulationPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;
                frameData.GetOrCreate<VividTemporalData>().isFirstFrame = true;

                pass.Prepare(frameData);

                var historyCurrent = GetField<RenderGraphTexture>(pass, "m_AccumulationCurrent");
                var resolvedColor = GetField<RenderGraphTexture>(pass, "m_ResolvedColor");
                Assert.That(historyCurrent.desc.Width, Is.EqualTo(640));
                Assert.That(historyCurrent.desc.Height, Is.EqualTo(360));
                Assert.That(historyCurrent.desc.EnableRandomWrite, Is.True);
                Assert.That(resolvedColor.desc.Width, Is.EqualTo(640));
                Assert.That(resolvedColor.desc.Height, Is.EqualTo(360));
                Assert.That(GetField<bool>(pass, "m_UseHistory"), Is.False);
                Assert.That(GetField<float>(pass, "m_InverseSampleCount"), Is.EqualTo(1.0f));

                pass.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RenderGraphNode_DefinesSampleInputAndResolvedOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredAccumulationPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SampleRadiance"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_AccumulationPrevious"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_AccumulationCurrent"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ResolvedColor"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static T GetField<T>(ReferencedPathTracingAccumulationPass pass, string fieldName)
        {
            var field = typeof(ReferencedPathTracingAccumulationPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
