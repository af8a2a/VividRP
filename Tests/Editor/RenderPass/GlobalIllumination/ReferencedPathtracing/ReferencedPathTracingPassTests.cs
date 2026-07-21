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
    public sealed class ReferencedPathTracingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReferencedPathtracingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingPass);
        }

        [Test]
        public void Pass_AllowsGlobalStateModification()
        {
            Assert.That(
                typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(ReferencedPathTracingPass)),
                Is.True);
        }

        [Test]
        public void Initialize_RegistersSceneRtasReGIRInputsAndWorldPositionOutput()
        {
            IRenderPass renderPass = new ReferencedPathTracingPass();

            var resources = renderPass.Initialize();
            var accelerationStructure = resources.AccelerationStructures.Single();
            var worldPosition = resources.Textures.Single(resource => resource.Name == "WorldPosition");
            var lightPdfTexture = resources.Textures.Single(resource => resource.Name == "ReGIRLightPdfTexture");

            Assert.That(accelerationStructure.Name, Is.EqualTo("SceneRTAS"));
            Assert.That(accelerationStructure.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Buffers.Select(resource => resource.Name),
                Is.EquivalentTo(new[] { "ReGIRLights", "ReGIRParameters", "ReGIRReservoirs" }));
            Assert.That(resources.Buffers.All(resource => resource.Access == AccessFlags.Read), Is.True);
            Assert.That(lightPdfTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(lightPdfTexture.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(worldPosition.Name, Is.EqualTo("WorldPosition"));
            Assert.That(worldPosition.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(worldPosition.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(worldPosition.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(worldPosition.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Prepare_ResizesWorldPositionOutputToCameraDimensions()
        {
            var pass = new ReferencedPathTracingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            var output = GetField<RenderGraphTexture>(pass, "m_WorldPositionTexture");
            Assert.That(output.desc.Width, Is.EqualTo(960));
            Assert.That(output.desc.Height, Is.EqualTo(540));
            Assert.That(output.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(output.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_CachesPerspectiveCameraRayRangeAndPosition()
        {
            var gameObject = new GameObject("ReferencedPathtracingPassTests.Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.25f;
            camera.farClipPlane = 750.0f;
            camera.transform.position = new Vector3(1.0f, 2.0f, 3.0f);

            try
            {
                var pass = new ReferencedPathTracingPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;

                pass.Prepare(frameData);

                Assert.That(GetField<bool>(pass, "m_ShouldSkipExecution"), Is.False);
                Assert.That(GetField<float>(pass, "m_RayMinDistance"), Is.EqualTo(0.25f));
                Assert.That(GetField<float>(pass, "m_RayMaxDistance"), Is.EqualTo(750.0f));
                Assert.That(GetField<Vector4>(pass, "m_CameraPositionWS"), Is.EqualTo(new Vector4(1.0f, 2.0f, 3.0f, 1.0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Prepare_CachesMainDirectionalLightForLambertEvaluation()
        {
            var pass = new ReferencedPathTracingPass();
            var frameData = new ContextContainer();
            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.directionalLights = new[]
            {
                new VividLightData.DirectionalLightData
                {
                    directionWS = new Vector3(0.0f, 2.0f, 0.0f),
                    color = new Vector3(3.0f, 2.0f, 1.0f),
                }
            };
            lightData.directionalLightCount = 1;
            lightData.mainDirectionalLightIndex = 0;

            pass.Prepare(frameData);

            Assert.That(
                GetField<Vector4>(pass, "m_MainLightDirectionWS"),
                Is.EqualTo(new Vector4(0.0f, 1.0f, 0.0f, 0.0f)));
            Assert.That(
                GetField<Vector4>(pass, "m_MainLightColor"),
                Is.EqualTo(new Vector4(3.0f, 2.0f, 1.0f, 1.0f)));
        }

        [Test]
        public void RenderGraphNode_DefinesSceneRtasReGIRInputsAndWorldPositionOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReferencedPathtracingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRParameterBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRReservoirBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightPdfTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_WorldPositionTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void StandardLitShader_DeclaresReferencedPathtracingDxrPass()
        {
            var shader = Shader.Find("VividRP/Material/StandardLit");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                var passIndex = material.FindPass(ReferencedPathTracingPass.MaterialShaderPassName);

                Assert.That(passIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")),
                    Is.EqualTo(new ShaderTagId(ReferencedPathTracingPass.MaterialShaderPassName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static T GetField<T>(ReferencedPathTracingPass pass, string fieldName)
        {
            var field = typeof(ReferencedPathTracingPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
