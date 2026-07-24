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
        public void Initialize_RegistersSceneRtasReGIRInputsAndReblurOutputs()
        {
            IRenderPass renderPass = new ReferencedPathTracingPass();

            var resources = renderPass.Initialize();
            var accelerationStructure = resources.AccelerationStructures.Single();
            var reGIRLights = resources.Buffers.Single(resource => resource.Name == "ReGIRLights");
            var reGIRParameters = resources.Buffers.Single(
                resource => resource.Name == "ReGIRParameters");
            var reGIRReservoirs = resources.Buffers.Single(
                resource => resource.Name == "ReGIRReservoirs");
            var worldPosition = resources.Textures.Single(resource => resource.Name == "WorldPosition");
            var lightPdfTexture = resources.Textures.Single(resource => resource.Name == "ReGIRLightPdfTexture");
            var diffuse = resources.Textures.Single(
                resource => resource.Name == "DiffuseRadianceHitDistance");
            var specular = resources.Textures.Single(
                resource => resource.Name == "SpecularRadianceHitDistance");
            var directLighting = resources.Textures.Single(
                resource => resource.Name == "PathTracingDirectLighting");
            var emission = resources.Textures.Single(resource => resource.Name == "PathTracingEmission");

            Assert.That(accelerationStructure.Name, Is.EqualTo("SceneRTAS"));
            Assert.That(accelerationStructure.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Buffers.Select(resource => resource.Name),
                Is.EquivalentTo(new[] { "ReGIRLights", "ReGIRParameters", "ReGIRReservoirs" }));
            Assert.That(resources.Buffers.All(resource => resource.Access == AccessFlags.Read), Is.True);
            Assert.That(reGIRLights.Buffer.desc.Stride, Is.EqualTo(VividReGIRLightData.Stride));
            Assert.That(reGIRParameters.Buffer.desc.Stride, Is.EqualTo(VividReGIRParameters.Stride));
            Assert.That(reGIRReservoirs.Buffer.desc.Stride, Is.EqualTo(VividReGIRReservoir.Stride));
            Assert.That(lightPdfTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(lightPdfTexture.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(worldPosition.Name, Is.EqualTo("WorldPosition"));
            Assert.That(worldPosition.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(worldPosition.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(worldPosition.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(worldPosition.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(diffuse.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(specular.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(directLighting.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(emission.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(diffuse.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(specular.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(diffuse.Texture.desc.ClearBuffer, Is.True);
            Assert.That(specular.Texture.desc.ClearBuffer, Is.True);
            Assert.That(
                directLighting.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(emission.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
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
                cameraData.frameIndex = 37;

                pass.Prepare(frameData);

                Assert.That(GetField<bool>(pass, "m_ShouldSkipExecution"), Is.False);
                Assert.That(GetField<float>(pass, "m_RayMinDistance"), Is.EqualTo(0.25f));
                Assert.That(GetField<float>(pass, "m_RayMaxDistance"), Is.EqualTo(750.0f));
                Assert.That(GetField<int>(pass, "m_FrameIndex"), Is.EqualTo(37));
                Assert.That(GetField<Vector4>(pass, "m_CameraPositionWS"), Is.EqualTo(new Vector4(1.0f, 2.0f, 3.0f, 1.0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Prepare_PreservesMainDirectionalLightPhysicalIlluminance()
        {
            var pass = new ReferencedPathTracingPass();
            var frameData = new ContextContainer();
            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.directionalLights = new[]
            {
                new VividLightData.DirectionalLightData
                {
                    directionWS = new Vector3(0.0f, 2.0f, 0.0f),
                    color = new Vector3(130000.0f, 65000.0f, 32500.0f),
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
                Is.EqualTo(new Vector4(130000.0f, 65000.0f, 32500.0f, 1.0f)));
        }

        [Test]
        public void RenderGraphNode_DefinesSceneRtasReGIRInputsAndReblurOutputs()
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
                Assert.That(node.GetOutputPortByName("m_DiffuseRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DirectLighting"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_Emission"), Is.Not.Null);
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
