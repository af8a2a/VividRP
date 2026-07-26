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
        public void CapturePass_ConsumesRawFp32AccumulationAndIsNeverCulled()
        {
            IRenderPass renderPass = new ReferencedPathTracingCapturePass();

            var resources = renderPass.Initialize();
            var rawAccumulation = resources.Textures.Single();

            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingCapturePass)),
                Is.True);
            Assert.That(
                rawAccumulation.Name,
                Is.EqualTo("PathTracingAccumulationRaw"));
            Assert.That(rawAccumulation.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                rawAccumulation.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
        }

        [Test]
        public void Initialize_RegistersReferenceLightReGIRInputsAndReblurOutputs()
        {
            IRenderPass renderPass = new ReferencedPathTracingPass();

            var resources = renderPass.Initialize();
            var accelerationStructure = resources.AccelerationStructures.Single();
            var referenceLightList = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightList");
            var referenceLightListParameters = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightListParameters");
            var reGIRLights = resources.Buffers.Single(resource => resource.Name == "ReGIRLights");
            var reGIRParameters = resources.Buffers.Single(
                resource => resource.Name == "ReGIRParameters");
            var reGIRReservoirs = resources.Buffers.Single(
                resource => resource.Name == "ReGIRReservoirs");
            var environmentImportanceDistribution = resources.Buffers.Single(
                resource => resource.Name == "EnvironmentImportanceDistribution");
            var worldPosition = resources.Textures.Single(resource => resource.Name == "WorldPosition");
            var lightPdfTexture = resources.Textures.Single(resource => resource.Name == "ReGIRLightPdfTexture");
            var environmentTexture = resources.Textures.Single(
                resource => resource.Name == "PathTracingEnvironment");
            var environmentBackgroundTexture = resources.Textures.Single(
                resource => resource.Name == "PathTracingEnvironmentBackground");
            var diffuse = resources.Textures.Single(
                resource => resource.Name == "DiffuseRadianceHitDistance");
            var specular = resources.Textures.Single(
                resource => resource.Name == "SpecularRadianceHitDistance");
            var directLighting = resources.Textures.Single(
                resource => resource.Name == "PathTracingDirectLighting");
            var emission = resources.Textures.Single(resource => resource.Name == "PathTracingEmission");
            var environmentDirectDiffuse = resources.Textures.Single(
                resource => resource.Name == "EnvironmentDirectDiffuse");
            var environmentDirectSpecular = resources.Textures.Single(
                resource => resource.Name == "EnvironmentDirectSpecular");

            Assert.That(accelerationStructure.Name, Is.EqualTo("SceneRTAS"));
            Assert.That(accelerationStructure.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Buffers.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "ReferenceLightList",
                    "ReferenceLightListParameters",
                    "ReGIRLights",
                    "ReGIRParameters",
                    "ReGIRReservoirs",
                    "EnvironmentImportanceDistribution"
                }));
            Assert.That(resources.Buffers.All(resource => resource.Access == AccessFlags.Read), Is.True);
            Assert.That(
                referenceLightList.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightRecord.Stride));
            Assert.That(
                referenceLightListParameters.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightListParameters.Stride));
            Assert.That(reGIRLights.Buffer.desc.Stride, Is.EqualTo(VividReGIRLightData.Stride));
            Assert.That(reGIRParameters.Buffer.desc.Stride, Is.EqualTo(VividReGIRParameters.Stride));
            Assert.That(reGIRReservoirs.Buffer.desc.Stride, Is.EqualTo(VividReGIRReservoir.Stride));
            Assert.That(
                environmentImportanceDistribution.Buffer.desc.Count,
                Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.ElementCount));
            Assert.That(
                environmentImportanceDistribution.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.ElementStride));
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
            Assert.That(environmentBackgroundTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                environmentBackgroundTexture.Texture.desc.Dimension,
                Is.EqualTo(TextureDimension.Cube));
            Assert.That(
                environmentBackgroundTexture.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(environmentBackgroundTexture.Texture.desc.UseMipMap, Is.True);
            Assert.That(worldPosition.Name, Is.EqualTo("WorldPosition"));
            Assert.That(worldPosition.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(worldPosition.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(worldPosition.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(worldPosition.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(diffuse.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(specular.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(directLighting.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(emission.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(environmentDirectDiffuse.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(environmentDirectSpecular.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(diffuse.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(specular.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(diffuse.Texture.desc.ClearBuffer, Is.True);
            Assert.That(specular.Texture.desc.ClearBuffer, Is.True);
            Assert.That(
                directLighting.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(emission.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(
                environmentDirectDiffuse.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(
                environmentDirectSpecular.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(environmentDirectDiffuse.Texture.desc.ClearBuffer, Is.True);
            Assert.That(environmentDirectSpecular.Texture.desc.ClearBuffer, Is.True);
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
            var environmentDirectDiffuse = GetField<RenderGraphTexture>(
                pass,
                "m_EnvironmentDirectDiffuse");
            var environmentDirectSpecular = GetField<RenderGraphTexture>(
                pass,
                "m_EnvironmentDirectSpecular");
            Assert.That(output.desc.Width, Is.EqualTo(960));
            Assert.That(output.desc.Height, Is.EqualTo(540));
            Assert.That(output.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(output.desc.EnableRandomWrite, Is.True);
            Assert.That(environmentDirectDiffuse.desc.Width, Is.EqualTo(960));
            Assert.That(environmentDirectDiffuse.desc.Height, Is.EqualTo(540));
            Assert.That(environmentDirectSpecular.desc.Width, Is.EqualTo(960));
            Assert.That(environmentDirectSpecular.desc.Height, Is.EqualTo(540));
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
                    angularDiameter = 1.25f * Mathf.Deg2Rad,
                    shadowStrength = 0.4f,
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
            Assert.That(
                GetField<float>(pass, "m_MainLightAngularDiameter"),
                Is.EqualTo(1.25f * Mathf.Deg2Rad).Within(0.000001f));
            Assert.That(
                GetField<float>(pass, "m_MainLightShadowStrength"),
                Is.EqualTo(0.4f).Within(0.000001f));
        }

        [Test]
        public void DirectionalLightSampling_UsesUniformSolidAngleAndLightBsdfMis()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var closestHitSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var reblurResolveSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NRD",
                "REBLUR",
                "REBLUR_DiffuseSpecular_Resolve.compute"));

            Assert.That(commonSource, Does.Contain("float mainLightLightPdf;"));
            Assert.That(commonSource, Does.Contain("float mainLightBsdfPdf;"));
            Assert.That(commonSource, Does.Contain("uint mainLightIsDelta;"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingGetMainDirectionalLightSolidAnglePdf"));
            Assert.That(
                commonSource,
                Does.Contain("lightPdf = rcp(solidAngle);"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateMainDirectionalLightPdf"));
            Assert.That(
                closestHitSource,
                Does.Contain("ReferencedPathtracingSampleMainDirectionalLight"));
            Assert.That(
                closestHitSource,
                Does.Contain("openpbr_pdf(preparedBsdf, mainLightDirectionWS)"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("payload.mainLightDirectionWS"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetMainLightEstimatorWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetMainBsdfEstimatorWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "mainLightIlluminance * mainLightPdfForBsdfSample"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("payload.nextThroughputWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "TraceReferencedPathtracingMainLightVisibility"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "primaryDenoiserMainLightDiffuseRadiance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "primaryDenoiserMainLightSpecularRadiance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "payload.mainLightIsDelta == 0u"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "CombineReferencedPathtracingDenoiserHitDistance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "kReferencedPathtracingShadowMaxDistance"));
            Assert.That(
                reblurResolveSource,
                Does.Contain("_ReblurMainLightInSignals != 0"));
            Assert.That(
                reblurResolveSource,
                Does.Contain("+ unfilteredDirectLighting"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain("normalize(_ReferencedMainLightDirectionWS.xyz)"));
        }

        [Test]
        public void RenderGraphNode_DefinesReferenceLightReGIRInputsAndReblurOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReferencedPathtracingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_ReferenceLightList"),
                    Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_ReferenceLightListParameters"),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRParameterBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRReservoirBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightPdfTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_EnvironmentTexture"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_EnvironmentBackgroundTexture"),
                    Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_EnvironmentImportanceDistribution"),
                    Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_WorldPositionTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DirectLighting"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_Emission"), Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_EnvironmentDirectDiffuse"),
                    Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_EnvironmentDirectSpecular"),
                    Is.Not.Null);
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
                Assert.That(state.neeEnabled, Is.True);
                Assert.That(
                    state.samplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling));
                Assert.That(
                    state.estimatorMode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(
                    state.debugMode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentDebugMode.Combined));
                Assert.That(state.tint, Is.EqualTo(new Color(0.5f, 0.75f, 1.0f, 1.0f)));
                Assert.That(state.intensityMultiplier, Is.EqualTo(2.0f));
                Assert.That(state.rotation, Is.EqualTo(45.0f));
                Assert.That(state.skyHash, Is.EqualTo(1234));
                Assert.That(state.contentHash, Is.Not.Zero);
                Assert.That(state.backgroundResolution, Is.EqualTo(4));
                Assert.That(state.lightingResolution, Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void EnvironmentMetadata_CapturesRawLightingContractWithoutDisplayExposure()
        {
            var cubemap = new Cubemap(8, TextureFormat.RGBAHalf, true)
            {
                name = "Metadata HDRI"
            };
            var settings =
                ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.UniformSphere;
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = Color.white,
                    exposure = 3.0f,
                    rotation = 75.0f,
                    skyHash = 91,
                    skyContentHash = 17
                };

                var metadata = ReferencedPathTracingEnvironmentMetadata.Capture(
                    skyData,
                    settings);

                Assert.That(
                    metadata.contractVersion,
                    Is.EqualTo(ReferencedPathTracingEnvironmentMetadata.ContractVersion));
                Assert.That(metadata.assetName, Is.EqualTo("Metadata HDRI"));
                Assert.That(metadata.skyHash, Is.EqualTo(91));
                Assert.That(metadata.contentHash, Is.EqualTo(17));
                Assert.That(metadata.backgroundResolution, Is.EqualTo(8));
                Assert.That(metadata.lightingResolution, Is.EqualTo(8));
                Assert.That(metadata.lightingEnabled, Is.True);
                Assert.That(metadata.cameraVisible, Is.True);
                Assert.That(metadata.rotation, Is.EqualTo(75.0f));
                Assert.That(metadata.physicalIntensityMultiplier, Is.EqualTo(3.0f));
                Assert.That(
                    metadata.samplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.UniformSphere));
                Assert.That(
                    metadata.pdfVersion,
                    Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.Version));
                Assert.That(metadata.rawRadianceIsPreExposed, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void DeterministicSampleSequence_AdvancesOncePerFrameAndResetsOnSignature()
        {
            var gameObject =
                new GameObject("ReferencedPathTracingSampleSequenceTests.Camera");
            var camera = gameObject.AddComponent<Camera>();

            try
            {
                var first = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    100,
                    11ul,
                    true);
                var duplicatePrepare = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    100,
                    11ul,
                    false);
                var nextFrame = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    101,
                    11ul,
                    false);
                var changedScene = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    102,
                    12ul,
                    false);
                ReferencedPathTracingSampleSequence.RequestReset(camera);
                var requestedReset = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    103,
                    12ul,
                    false);

                Assert.That(first, Is.Zero);
                Assert.That(duplicatePrepare, Is.Zero);
                Assert.That(nextFrame, Is.EqualTo(1u));
                Assert.That(changedScene, Is.Zero);
                Assert.That(requestedReset, Is.Zero);
            }
            finally
            {
                ReferencedPathTracingSampleSequence.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void V1FreezeGate_RequiresEveryCanonicalCaseAndPassedGpuEvidence()
        {
            var captures = ReferencedPathTracingV1Corpus.Cases
                .Select(CreateValidFrozenCapture)
                .ToArray();

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out var failure),
                Is.True,
                failure);

            captures[0].validation.status =
                ReferencedPathTracingValidationStatus.NotRun;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("GPU validation evidence"));
        }

        [Test]
        public void V1FreezeGate_RejectsReGIRPreExposureAndWrongCameraVisibility()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases.Single(
                item => item.id == "hdri-camera-hidden-lighting");
            var capture = CreateValidFrozenCapture(corpusCase);

            capture.usesReGIR = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.usesReGIR = false;
            capture.rawRadianceIsPreExposed = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.rawRadianceIsPreExposed = false;
            capture.environment.cameraVisible = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);
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
                Assert.That(state.neeEnabled, Is.False);
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
                skyData.skyContentHash = original.contentHash + 1;
                var contentChanged = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.skyContentHash = original.contentHash;
                skyData.skyHash = 43;
                var nonContentSkyStateChanged =
                    ReferencedPathTracingEnvironmentState.Resolve(
                        skyData,
                        settings);
                skyData.skyHash = 42;
                skyData.rotation = 30.0f;
                var rotated = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.rotation = 0.0f;
                skyData.tint = new Color(0.5f, 1.0f, 1.0f, 1.0f);
                var tinted = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.tint = Color.white;
                skyData.exposure = 2.0f;
                var intensified = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.exposure = 1.0f;
                settings.environmentCameraVisible.value = false;
                var hidden = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentCameraVisible.value = true;
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly;
                var lightOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly;
                var estimatorBsdfOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly;
                var bsdfOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.UniformSphere;
                var uniformSphere = ReferencedPathTracingEnvironmentState.Resolve(
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
                Assert.That(
                    contentChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    contentChanged.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    nonContentSkyStateChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    nonContentSkyStateChanged.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(
                    rotated.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    tinted.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    intensified.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(hidden.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    hidden.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(lightOnly.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    lightOnly.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(
                    estimatorBsdfOnly.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    estimatorBsdfOnly.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(bsdfOnly.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    bsdfOnly.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    indirectMissOnly.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    indirectMissOnly.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(replacement.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    replacement.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(bsdfOnly.importanceSamplingEnabled, Is.False);
                Assert.That(bsdfOnly.neeEnabled, Is.False);
                Assert.That(bsdfOnly.lightingEnabled, Is.True);
                Assert.That(lightOnly.neeEnabled, Is.True);
                Assert.That(estimatorBsdfOnly.neeEnabled, Is.False);
                Assert.That(uniformSphere.importanceSamplingEnabled, Is.False);
                Assert.That(uniformSphere.neeEnabled, Is.True);
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

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(ReferencedPathTracingPass).Assembly);

            Assert.That(packageInfo, Is.Not.Null);
            return Path.Combine(packageInfo.resolvedPath, Path.Combine(relativeParts));
        }

        private static ReferencedPathTracingCaptureMetadata
            CreateValidFrozenCapture(
                ReferencedPathTracingV1CorpusCase corpusCase)
        {
            return new ReferencedPathTracingCaptureMetadata
            {
                freezeContractVersion =
                    ReferencedPathTracingV1FreezeGate.ContractVersion,
                corpusVersion = ReferencedPathTracingV1Corpus.Version,
                integratorVersion =
                    ReferencedPathTracingIntegratorState.Version,
                corpusCaseId = corpusCase.id,
                width = corpusCase.width,
                height = corpusCase.height,
                targetSampleCount = corpusCase.targetSampleCount,
                accumulatedSampleCount =
                    (ulong)corpusCase.targetSampleCount,
                deterministicSampling = true,
                fixedSeed = corpusCase.fixedSeed,
                maxBounceCount = corpusCase.maxBounceCount,
                russianRouletteStartBounce =
                    corpusCase.russianRouletteStartBounce,
                integratorSignature = 1ul,
                usesReGIR = false,
                usesDenoiser = false,
                usesRasterGI = false,
                rawRadianceIsPreExposed = false,
                hasMainDirectionalLight = false,
                localLightCount = 0,
                unsupportedMaterialCount = 0,
                standardLitOnly = true,
                imageOriginBottomLeft = true,
                environment =
                    new ReferencedPathTracingEnvironmentMetadata
                    {
                        contractVersion =
                            ReferencedPathTracingEnvironmentMetadata.ContractVersion,
                        contentHash = 1,
                        backgroundResolution = 1024,
                        lightingResolution = 256,
                        lightingEnabled = true,
                        cameraVisible =
                            corpusCase.id
                            != "hdri-camera-hidden-lighting",
                        samplingMode = corpusCase.samplingMode,
                        estimatorMode = corpusCase.estimatorMode,
                        physicalIntensityMultiplier = 1.0f,
                        pdfVersion =
                            ReferencedPathTracingEnvironmentImportanceLayout.Version,
                        rawRadianceIsPreExposed = false
                    },
                validation = new ReferencedPathTracingValidationEvidence
                {
                    status = ReferencedPathTracingValidationStatus.Passed,
                    graphicsApi = "Direct3D12",
                    deviceName = "Canonical Test Device",
                    referenceImageSha256 = new string('a', 64),
                    finitePixelFraction = 1.0f,
                    negativeRadianceFraction = 0.0f,
                    relativeMeanError = 0.01f
                }
            };
        }
    }
}
