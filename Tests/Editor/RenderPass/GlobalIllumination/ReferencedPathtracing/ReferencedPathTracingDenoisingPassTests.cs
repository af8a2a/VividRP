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
    public sealed class ReferencedPathTracingDenoisingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredDenoisingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingDenoisingPass);
        }

        private sealed class TestBackend : IReferencedPathTracingDenoiserBackend
        {
            public int InvalidationCount { get; private set; }
            public bool IsSupported => true;

            public void Invalidate()
            {
                InvalidationCount++;
            }

            public bool Process(
                CommandBuffer commandBuffer,
                RenderTexture source,
                RenderTexture destination,
                int width,
                int height)
            {
                return false;
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void Pass_PreservesAsynchronousDenoisingWhenOutputHasNoSameFrameConsumer()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingDenoisingPass)),
                Is.True);
        }

        [Test]
        public void Initialize_DefinesFp32InputOutputAndInactiveBypass()
        {
            IRenderPass renderPass = new ReferencedPathTracingDenoisingPass();

            var resources = renderPass.Initialize();

            Assert.That(
                resources.Textures.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "PathTracingAccumulatedColor",
                    "PathTracingDenoisedColor"
                }));
            Assert.That(
                resources.Textures.All(resource =>
                    resource.Texture.desc.ColorFormat == GraphicsFormat.R32G32B32A32_SFloat),
                Is.True);
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingAccumulatedColor").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingDenoisedColor").Access,
                Is.EqualTo(AccessFlags.WriteAll));
            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_AccumulatedColor"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_DenoisedColor"));
        }

        [Test]
        public void Prepare_ResizesOutputAndInvalidatesBackendWhenCameraSignatureChanges()
        {
            var cameraObject = new GameObject("ReferencedPathTracingDenoisingPassTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var backend = new TestBackend();

            try
            {
                var pass = new ReferencedPathTracingDenoisingPass(() => backend);
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;

                pass.Prepare(frameData);

                var denoisedColor = GetField<RenderGraphTexture>(pass, "m_DenoisedColor");
                Assert.That(denoisedColor.desc.Width, Is.EqualTo(640));
                Assert.That(denoisedColor.desc.Height, Is.EqualTo(360));
                Assert.That(denoisedColor.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                camera.transform.position = Vector3.right;
                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(2));

                pass.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RenderGraphNode_DefinesAccumulatedInputAndDenoisedOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDenoisingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_AccumulatedColor"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DenoisedColor"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static T GetField<T>(ReferencedPathTracingDenoisingPass pass, string fieldName)
        {
            var field = typeof(ReferencedPathTracingDenoisingPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
