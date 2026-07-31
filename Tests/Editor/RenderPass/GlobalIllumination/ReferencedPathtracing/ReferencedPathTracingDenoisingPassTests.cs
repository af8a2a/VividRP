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
                RenderTexture radiance,
                RenderTexture albedo,
                RenderTexture normal,
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
        public void Initialize_DefinesFp32RadianceFeatureInputsAndInactiveBypass()
        {
            IRenderPass renderPass = new ReferencedPathTracingDenoisingPass();

            var resources = renderPass.Initialize();

            Assert.That(
                resources.Textures.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "PathTracingRadiance",
                    "PathTracingAlbedo",
                    "PathTracingNormal",
                    "PathTracingDenoisedRadiance"
                }));
            Assert.That(
                resources.Textures.All(resource =>
                    resource.Texture.desc.ColorFormat == GraphicsFormat.R32G32B32A32_SFloat),
                Is.True);
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingRadiance").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingAlbedo").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingNormal").Access,
                Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "PathTracingDenoisedRadiance").Access,
                Is.EqualTo(AccessFlags.WriteAll));
            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_Radiance"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_DenoisedRadiance"));
        }

        [Test]
        public void RequestPolicy_RequiresConvergenceAndKeepsCompletedResultStable()
        {
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(true, 31ul, 32),
                Is.False);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(true, 32ul, 32),
                Is.True);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(true, 33ul, 32),
                Is.True);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(false, 32ul, 32),
                Is.False);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(true, 32ul, 0),
                Is.False);

            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .ShouldBeginRequest(false, false),
                Is.True);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .ShouldBeginRequest(true, false),
                Is.False);
            Assert.That(
                ReferencedPathTracingDenoiserRequestPolicy
                    .ShouldBeginRequest(false, true),
                Is.False);
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
                var pathTracingData =
                    frameData.GetOrCreate<VividReferencedPathTracingData>();
                pathTracingData.isValid = true;
                pathTracingData.integratorSignature = 11ul;
                pathTracingData.frameSignature = 22ul;
                pathTracingData.targetSampleCount = 32;
                pathTracingData.accumulatedSampleCount = 1ul;

                pass.Prepare(frameData);

                var denoisedRadiance = GetField<RenderGraphTexture>(
                    pass,
                    "m_DenoisedRadiance");
                Assert.That(denoisedRadiance.desc.Width, Is.EqualTo(640));
                Assert.That(denoisedRadiance.desc.Height, Is.EqualTo(360));
                Assert.That(
                    denoisedRadiance.desc.ColorFormat,
                    Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                camera.transform.position = Vector3.right;
                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(2));

                pathTracingData.frameSignature++;
                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(3));

                pass.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Prepare_GatesDenoisingAndInvalidatesWhenAccumulationRestarts()
        {
            var cameraObject = new GameObject(
                "ReferencedPathTracingDenoisingPassTests.ConvergenceCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var backend = new TestBackend();

            try
            {
                var pass = new ReferencedPathTracingDenoisingPass(() => backend);
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;
                cameraData.frameIndex = 10;
                var pathTracingData =
                    frameData.GetOrCreate<VividReferencedPathTracingData>();
                pathTracingData.isValid = true;
                pathTracingData.integratorSignature = 101ul;
                pathTracingData.frameSignature = 202ul;
                pathTracingData.targetSampleCount = 8;
                pathTracingData.accumulatedSampleCount = 7ul;

                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.False);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                pathTracingData.accumulatedSampleCount = 8ul;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.True);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                pathTracingData.accumulatedSampleCount = 9ul;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.True);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                pathTracingData.accumulatedSampleCount = 1ul;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.False);
                Assert.That(backend.InvalidationCount, Is.EqualTo(2));

                pathTracingData.targetSampleCount = 16;
                pathTracingData.accumulatedSampleCount = 8ul;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.False);
                Assert.That(backend.InvalidationCount, Is.EqualTo(3));

                pathTracingData.accumulatedSampleCount = 16ul;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.True);
                Assert.That(backend.InvalidationCount, Is.EqualTo(3));

                pathTracingData.Reset();
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.False);
                Assert.That(backend.InvalidationCount, Is.EqualTo(4));

                pass.Prepare(frameData);
                Assert.That(backend.InvalidationCount, Is.EqualTo(4));

                pass.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Prepare_DetectsOneSampleAccumulationRestart()
        {
            var cameraObject = new GameObject(
                "ReferencedPathTracingDenoisingPassTests.OneSampleCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var backend = new TestBackend();

            try
            {
                var pass = new ReferencedPathTracingDenoisingPass(() => backend);
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 64;
                cameraData.actualHeight = 64;
                cameraData.frameIndex = 20;
                var pathTracingData =
                    frameData.GetOrCreate<VividReferencedPathTracingData>();
                pathTracingData.isValid = true;
                pathTracingData.integratorSignature = 303ul;
                pathTracingData.frameSignature = 404ul;
                pathTracingData.targetSampleCount = 1;
                pathTracingData.accumulatedSampleCount = 1ul;
                pathTracingData.sampleIndex = 0;

                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.True);
                Assert.That(backend.InvalidationCount, Is.EqualTo(1));

                cameraData.frameIndex++;
                pass.Prepare(frameData);
                Assert.That(GetField<bool>(pass, "m_IsDenoisingReady"), Is.True);
                Assert.That(backend.InvalidationCount, Is.EqualTo(2));

                pass.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RenderGraphNode_DefinesPathTracingAovInputsAndDenoisedOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDenoisingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_Radiance"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Albedo"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Normal"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DenoisedRadiance"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void LegacyDenoisingBindings_ResolveToRadiancePorts()
        {
            var input = RenderGraphPassReflectionUtility.GetInstanceField(
                typeof(ReferencedPathTracingDenoisingPass),
                "m_AccumulatedColor");
            var output = RenderGraphPassReflectionUtility.GetInstanceField(
                typeof(ReferencedPathTracingDenoisingPass),
                "m_DenoisedColor");

            Assert.That(input?.Name, Is.EqualTo("m_Radiance"));
            Assert.That(output?.Name, Is.EqualTo("m_DenoisedRadiance"));
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
