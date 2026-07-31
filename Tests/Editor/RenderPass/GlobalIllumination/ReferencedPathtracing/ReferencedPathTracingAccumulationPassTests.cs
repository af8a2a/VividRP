using System;
using System.IO;
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
        public void AccumulationShader_DrawsProgressAndPreservesConvergedHistory()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingAccumulation.compute"));

            Assert.That(source, Does.Contain("void AddConvergenceCue("));
            Assert.That(
                source,
                Does.Contain("targetSampleCount <= 1 || sampleCount >= targetSampleCount"));
            Assert.That(source, Does.Contain("uint barHeight = max(4u"));
            Assert.That(
                source,
                Does.Contain("preserveConvergedHistory"));
            Assert.That(
                source,
                Does.Contain("sceneLinearResult = _ReferencedPathTracingHistoryRadiance[pixelCoord]"));
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
        public void ReferenceLightListSignature_IsOrderIndependentAndTracksBarnDoorChanges()
        {
            var firstLight = CreateReferenceLight(
                1,
                LightType.Point,
                new Vector3(100.0f, 80.0f, 60.0f));
            var secondLight = CreateReferenceLight(
                2,
                LightType.Spot,
                new Vector3(40.0f, 50.0f, 60.0f));
            var areaLight = CreateReferenceLight(
                3,
                LightType.Rectangle,
                new Vector3(15.0f, 12.0f, 10.0f));
            areaLight.areaSize = new Vector2(2.0f, 1.0f);
            areaLight.barnDoorAngle = 45.0f;
            areaLight.barnDoorLength = 0.35f;

            var original = ReferencedPathTracingLightListBuilder.Build(
                new[] { firstLight, secondLight, areaLight });
            var reordered = ReferencedPathTracingLightListBuilder.Build(
                new[] { areaLight, secondLight, firstLight });
            areaLight.barnDoorLength += 0.1f;
            var changed = ReferencedPathTracingLightListBuilder.Build(
                new[] { secondLight, firstLight, areaLight });

            Assert.That(
                reordered.parameters.signatureLow,
                Is.EqualTo(original.parameters.signatureLow));
            Assert.That(
                reordered.parameters.signatureHigh,
                Is.EqualTo(original.parameters.signatureHigh));
            Assert.That(
                (changed.parameters.signatureHigh,
                    changed.parameters.signatureLow),
                Is.Not.EqualTo(
                    (original.parameters.signatureHigh,
                        original.parameters.signatureLow)));
        }

        [Test]
        public void LightSignature_TracksDirectionalAngularDiameterAndShadowStrength()
        {
            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = Vector3.up,
                        color = new Vector3(100000.0f, 90000.0f, 80000.0f),
                        angularDiameter = 1.25f * Mathf.Deg2Rad,
                        shadowStrength = 0.4f,
                    }
                },
                directionalLightCount = 1,
                mainDirectionalLightIndex = 0,
            };

            ReferencedPathTracingLightSignatureUtility.Resolve(
                lightData,
                out _,
                out _,
                out var angularDiameter,
                out var shadowStrength,
                out _);

            Assert.That(
                angularDiameter,
                Is.EqualTo(1.25f * Mathf.Deg2Rad).Within(0.000001f));
            Assert.That(shadowStrength, Is.EqualTo(0.4f).Within(0.000001f));
        }

        private static T GetField<T>(ReferencedPathTracingAccumulationPass pass, string fieldName)
        {
            var field = typeof(ReferencedPathTracingAccumulationPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(ReferencedPathTracingAccumulationPass).Assembly);

            Assert.That(packageInfo, Is.Not.Null);
            return Path.Combine(
                packageInfo.resolvedPath,
                Path.Combine(relativeParts));
        }

        private static VividLightRenderData CreateReferenceLight(
            ulong stableId,
            LightType lightType,
            Vector3 color)
        {
            return new VividLightRenderData
            {
                lightEntityId = EntityId.FromULong(stableId),
                lightType = lightType,
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 10.0f,
                forwardWS = Vector3.forward,
                rightWS = Vector3.right,
                upWS = Vector3.up,
                areaSize = Vector2.one,
                shapeRadius = 0.1f,
                color = color,
                shadowStrength = 1.0f,
                spotAngle = 60.0f,
                innerSpotAngle = 30.0f,
                rangeAttenuationScale = 0.01f,
                rangeAttenuationBias = 1.0f,
                flags = VividLightRenderDataFlags.Enabled
                    | VividLightRenderDataFlags.ActiveInHierarchy
                    | VividLightRenderDataFlags.CastShadows,
            };
        }
    }
}
