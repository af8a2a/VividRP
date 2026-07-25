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
            var environmentTexture = resources.Textures.Single(
                resource => resource.Name == "PathTracingEnvironment");
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
            Assert.That(environmentTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                environmentTexture.Texture.desc.Dimension,
                Is.EqualTo(TextureDimension.Cube));
            Assert.That(
                environmentTexture.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(environmentTexture.Texture.desc.UseMipMap, Is.True);
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
                Assert.That(node.GetInputPortByName("m_EnvironmentTexture"), Is.Not.Null);
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
        public void EnvironmentState_ResolvesHdriVisibilityLightingAndSamplingIndependently()
        {
            var cubemap = new Cubemap(4, TextureFormat.RGBAHalf, true);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                settings.environmentLighting.value = true;
                settings.environmentCameraVisible.value = false;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = new Color(0.5f, 0.75f, 1.0f, 0.25f),
                    exposure = 2.0f,
                    rotation = 45.0f,
                    skyHash = 1234
                };

                var state = ReferencedPathTracingEnvironmentState.Resolve(skyData, settings);

                Assert.That(state.hasHdri, Is.True);
                Assert.That(state.lightingEnabled, Is.True);
                Assert.That(state.cameraVisible, Is.False);
                Assert.That(state.importanceSamplingEnabled, Is.True);
                Assert.That(
                    state.samplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling));
                Assert.That(
                    state.debugMode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentDebugMode.Combined));
                Assert.That(state.tint, Is.EqualTo(new Color(0.5f, 0.75f, 1.0f, 1.0f)));
                Assert.That(state.intensityMultiplier, Is.EqualTo(2.0f));
                Assert.That(state.rotation, Is.EqualTo(45.0f));
                Assert.That(state.skyHash, Is.EqualTo(1234));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void EnvironmentState_DisablesUnsupportedSkyAndSanitizesInvalidValues()
        {
            var cubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.PhysicallyBased,
                    specularCubemap = cubemap,
                    tint = new Color(float.NaN, -1.0f, float.PositiveInfinity, 1.0f),
                    exposure = float.PositiveInfinity,
                    rotation = float.NaN,
                    skyHash = 99
                };

                var state = ReferencedPathTracingEnvironmentState.Resolve(skyData, settings);

                Assert.That(state.hasHdri, Is.False);
                Assert.That(state.lightingEnabled, Is.False);
                Assert.That(state.cameraVisible, Is.False);
                Assert.That(state.importanceSamplingEnabled, Is.False);
                Assert.That(state.tint, Is.EqualTo(Color.white));
                Assert.That(state.intensityMultiplier, Is.Zero);
                Assert.That(state.rotation, Is.Zero);
                Assert.That(state.skyHash, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void EnvironmentState_SignatureTracksSkyAndPathTracingSettings()
        {
            var cubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var replacementCubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = Color.white,
                    exposure = 1.0f,
                    rotation = 0.0f,
                    skyHash = 42
                };

                var original = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.rotation = 30.0f;
                var rotated = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.rotation = 0.0f;
                settings.environmentCameraVisible.value = false;
                var hidden = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentCameraVisible.value = true;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly;
                var bsdfOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
                settings.environmentDebugMode.value =
                    ReferencedPathTracingEnvironmentDebugMode.IndirectMissOnly;
                var indirectMissOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentDebugMode.value =
                    ReferencedPathTracingEnvironmentDebugMode.Combined;
                skyData.specularCubemap = replacementCubemap;
                var replacement = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);

                Assert.That(rotated.signature, Is.Not.EqualTo(original.signature));
                Assert.That(hidden.signature, Is.Not.EqualTo(original.signature));
                Assert.That(bsdfOnly.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    indirectMissOnly.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(replacement.signature, Is.Not.EqualTo(original.signature));
                Assert.That(bsdfOnly.importanceSamplingEnabled, Is.False);
                Assert.That(bsdfOnly.lightingEnabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(replacementCubemap);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void CameraBackgroundState_TracksSkyModeAndSceneLinearClearColor()
        {
            var cameraObject = new GameObject("Reference PT Camera Background Test");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = new Color(0.5f, 0.25f, 0.75f, 0.4f);
                var cameraData = new VividCameraData
                {
                    camera = camera
                };

                var sky = ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);
                camera.clearFlags = CameraClearFlags.SolidColor;
                var solidColor =
                    ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);
                camera.backgroundColor = new Color(0.25f, 0.5f, 0.75f, 0.8f);
                var changedColor =
                    ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

                Assert.That(sky.skyRequested, Is.True);
                Assert.That(
                    sky.clearColor.r,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.5f)).Within(1e-6f));
                Assert.That(
                    sky.clearColor.g,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.25f)).Within(1e-6f));
                Assert.That(
                    sky.clearColor.b,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.75f)).Within(1e-6f));
                Assert.That(sky.clearColor.a, Is.EqualTo(0.4f).Within(1e-6f));
                Assert.That(solidColor.skyRequested, Is.False);
                Assert.That(solidColor.signature, Is.Not.EqualTo(sky.signature));
                Assert.That(changedColor.signature, Is.Not.EqualTo(solidColor.signature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
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
