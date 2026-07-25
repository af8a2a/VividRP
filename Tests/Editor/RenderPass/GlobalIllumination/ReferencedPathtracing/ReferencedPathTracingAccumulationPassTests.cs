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

        [Test]
        public void LightSignature_IsOrderIndependentAndTracksRectangleBarnDoorChanges()
        {
            var firstLight = new VividReGIRLightData
            {
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 10.0f,
                color = new Vector3(100.0f, 80.0f, 60.0f),
                lightType = VividReGIRLightData.TypePoint,
                shapeRadius = 0.1f
            };
            var secondLight = new VividReGIRLightData
            {
                positionWS = new Vector3(-2.0f, 4.0f, 1.0f),
                range = 8.0f,
                color = new Vector3(40.0f, 50.0f, 60.0f),
                lightType = VividReGIRLightData.TypeSpot,
                directionWS = Vector3.down,
                angleScale = 2.0f,
                angleOffset = -1.0f
            };
            var areaLight = new VividReGIRLightData
            {
                positionWS = new Vector3(0.0f, 5.0f, 0.0f),
                range = 12.0f,
                color = new Vector3(15.0f, 12.0f, 10.0f),
                lightType = VividReGIRLightData.TypeRectangle,
                directionWS = Vector3.down,
                rightWS = Vector3.right,
                upWS = Vector3.forward,
                areaSize = new Vector2(2.0f, 1.0f),
                cosBarnDoorAngle = Mathf.Cos(45.0f * Mathf.Deg2Rad),
                barnDoorLength = 0.35f
            };
            var lightData = new VividLightData
            {
                reGIRLights = new[] { firstLight, secondLight, areaLight },
                reGIRLightCount = 3
            };

            ReferencedPathTracingLightSignatureUtility.Resolve(
                lightData,
                out _,
                out _,
                out var originalSignature);

            lightData.reGIRLights = new[] { areaLight, secondLight, firstLight };
            ReferencedPathTracingLightSignatureUtility.Resolve(
                lightData,
                out _,
                out _,
                out var reorderedSignature);

            areaLight.barnDoorLength += 0.1f;
            lightData.reGIRLights = new[] { secondLight, firstLight, areaLight };
            ReferencedPathTracingLightSignatureUtility.Resolve(
                lightData,
                out _,
                out _,
                out var changedSignature);

            Assert.That(reorderedSignature, Is.EqualTo(originalSignature));
            Assert.That(changedSignature, Is.Not.EqualTo(originalSignature));
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
