using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;
using VividRP.Runtime.Plugin;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingDebugSettings
    {
        internal ReferencedPathTracingDebugSettings(
            ReferencedPathTracingTransportDebugMode transportMode,
            ReferencedPathTracingEnvironmentDebugMode environmentMode)
        {
            this.transportMode = transportMode;
            this.environmentMode = environmentMode;
        }

        internal ReferencedPathTracingTransportDebugMode transportMode
        {
            get;
        }

        internal ReferencedPathTracingEnvironmentDebugMode environmentMode
        {
            get;
        }

        internal static ReferencedPathTracingDebugSettings Resolve(
            VividRenderingDebugSettingsData data)
        {
            return data != null
                ? new ReferencedPathTracingDebugSettings(
                    data.referencedPathTracingTransportDebugMode,
                    data.referencedPathTracingEnvironmentDebugMode)
                : new ReferencedPathTracingDebugSettings(
                    ReferencedPathTracingTransportDebugMode.Combined,
                    ReferencedPathTracingEnvironmentDebugMode.Combined);
        }
    }

    /// <summary>
    /// OpenPBR reference path-tracing prototype for StandardLit. It traces an iterative multi-bounce
    /// path and samples one canonical next-event candidate from the stable Reference Light List
    /// plus the active HDRI environment at every hit. Environment NEE, finite directional lights,
    /// and BSDF paths are combined with power-heuristic MIS and delta-aware gates. Reference
    /// Atmosphere mode inserts hero-channel delta tracking, physical phase scattering, a finite
    /// solar-disk MIS pair, a Lambertian virtual planet ground, and an optional PT-only procedural
    /// cloud shell before surface or miss evaluation without consuming raster sky data.
    /// The resolved sample stores scene-linear radiance and camera-background opacity. Denoising
    /// AOV alpha channels continue to use primary-hit validity.
    /// </summary>
    public sealed class ReferencedPathTracingPass
        : UnsafePass,
          IAllowGlobalStateModificationPass,
          IBlueNoiseConsumerPass
    {
        private static readonly ReferencedPathTracingLightListStorageBlock[]
            s_EmptyReferenceLightListStorage =
                ReferencedPathTracingLightListBuilder.Build(null).storageBlocks;

        internal const string MaterialShaderPassName = "ReferencedPathtracingDXR";
        internal const string RayGenerationShaderName = "RayGenReferencedPathtracing";
        internal const string ShaderExecutionReorderingKeywordName =
            "VIVID_REFERENCE_PT_SER";
        internal const string IndexedBndKeywordName =
            "VIVID_REFERENCE_PT_INDEXED_BND";
        internal const uint ShaderExecutionReorderingUavSlot = 31;
        internal const float DlssInfiniteHitDistance = 65504.0f;

        private const string AccelerationStructureName = "_AccelerationStructure";
        private const int NvidiaShaderExtensionStructStride = 256;

        private static readonly int PathTracingRadianceId =
            Shader.PropertyToID("_ReferencedPathTracingRadiance");
        private static readonly int PathTracingAlbedoId =
            Shader.PropertyToID("_ReferencedPathTracingAlbedo");
        private static readonly int PathTracingNormalId =
            Shader.PropertyToID("_ReferencedPathTracingNormal");
        private static readonly int DebugTextureId =
            Shader.PropertyToID("_ReferencedPathTracingDebugTexture");
        private static readonly int NvidiaShaderExtensionBufferId =
            Shader.PropertyToID("g_NvidiaExt");
        private static readonly int DiffuseRadianceHitDistanceId =
            Shader.PropertyToID("_ReferencedDiffuseRadianceHitDistance");
        private static readonly int SpecularRadianceHitDistanceId =
            Shader.PropertyToID("_ReferencedSpecularRadianceHitDistance");
        private static readonly int DirectLightingId =
            Shader.PropertyToID("_ReferencedPathTracingDirectLighting");
        private static readonly int EmissionId = Shader.PropertyToID("_ReferencedPathTracingEmission");
        private static readonly int EnvironmentDirectDiffuseId =
            Shader.PropertyToID("_ReferencedPathTracingEnvironmentDirectDiffuse");
        private static readonly int EnvironmentDirectSpecularId =
            Shader.PropertyToID("_ReferencedPathTracingEnvironmentDirectSpecular");
        private static readonly int DiffuseRayDirectionHitDistanceId =
            Shader.PropertyToID("_ReferencedDiffuseRayDirectionHitDistance");
        private static readonly int SpecularRayDirectionHitDistanceId =
            Shader.PropertyToID("_ReferencedSpecularRayDirectionHitDistance");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int WorldToViewId = Shader.PropertyToID("_ReferencedWorldToView");
        private static readonly int CameraRightWSId =
            Shader.PropertyToID("_ReferencedCameraRightWS");
        private static readonly int CameraUpWSId =
            Shader.PropertyToID("_ReferencedCameraUpWS");
        private static readonly int CameraForwardWSId =
            Shader.PropertyToID("_ReferencedCameraForwardWS");
        private static readonly int PhysicalCameraParametersId =
            Shader.PropertyToID("_ReferencedPhysicalCameraParameters");
        private static readonly int RayMinDistanceId = Shader.PropertyToID("_RayMinDistance");
        private static readonly int RayMaxDistanceId = Shader.PropertyToID("_RayMaxDistance");
        private static readonly int MaxBounceCountId = Shader.PropertyToID("_ReferencedMaxBounceCount");
        private static readonly int RussianRouletteStartBounceId =
            Shader.PropertyToID("_ReferencedRussianRouletteStartBounce");
        private static readonly int FrameIndexId = Shader.PropertyToID("_ReferencedFrameIndex");
        private static readonly int SeedId = Shader.PropertyToID("_ReferencedSeed");
        private static readonly int PathSamplingModeId =
            Shader.PropertyToID("_ReferencedPathSamplingMode");
        private static readonly int ReblurHitDistanceParametersId =
            Shader.PropertyToID("_ReferencedReblurHitDistanceParameters");
        private static readonly int ReblurCheckerboardModeId =
            Shader.PropertyToID("_ReferencedReblurCheckerboardMode");
        private static readonly int ReferenceLightListId =
            Shader.PropertyToID("_ReferencedLightList");
        private static readonly int ReferenceLightListParametersId =
            Shader.PropertyToID("_ReferencedLightListParameters");
        private static readonly int LocalLightNeeEnabledId =
            Shader.PropertyToID("_ReferencedLocalLightNeeEnabled");
        private static readonly int ShadingPointLightSelectionEnabledId =
            Shader.PropertyToID(
                "_ReferencedShadingPointLightSelectionEnabled");
        private static readonly int GlobalLightProposalProbabilityId =
            Shader.PropertyToID(
                "_ReferencedGlobalLightProposalProbability");
        private static readonly int LightSpatialIndexEnabledId =
            Shader.PropertyToID("_ReferencedLightSpatialIndexEnabled");
        private static readonly int EnvironmentTextureId =
            Shader.PropertyToID("_ReferencedEnvironmentTexture");
        private static readonly int EnvironmentBackgroundTextureId =
            Shader.PropertyToID("_ReferencedEnvironmentBackgroundTexture");
        private static readonly int EnvironmentImportanceDistributionId =
            Shader.PropertyToID("_ReferencedEnvironmentImportanceDistribution");
        private static readonly int EnvironmentTintId =
            Shader.PropertyToID("_ReferencedEnvironmentTint");
        private static readonly int EnvironmentParametersId =
            Shader.PropertyToID("_ReferencedEnvironmentParameters");
        private static readonly int EnvironmentModeId =
            Shader.PropertyToID("_ReferencedEnvironmentMode");
        private static readonly int EnvironmentLightingEnabledId =
            Shader.PropertyToID("_ReferencedEnvironmentLightingEnabled");
        private static readonly int EnvironmentCameraVisibleId =
            Shader.PropertyToID("_ReferencedEnvironmentCameraVisible");
        private static readonly int EnvironmentImportanceSamplingEnabledId =
            Shader.PropertyToID("_ReferencedEnvironmentImportanceSamplingEnabled");
        private static readonly int EnvironmentNeeEnabledId =
            Shader.PropertyToID("_ReferencedEnvironmentNeeEnabled");
        private static readonly int EnvironmentSamplingModeId =
            Shader.PropertyToID("_ReferencedEnvironmentSamplingMode");
        private static readonly int TransportEstimatorModeId =
            Shader.PropertyToID("_ReferencedTransportEstimatorMode");
        private static readonly int TransportDebugModeId =
            Shader.PropertyToID("_ReferencedTransportDebugMode");
        private static readonly int EnvironmentDebugModeId =
            Shader.PropertyToID("_ReferencedEnvironmentDebugMode");
        private static readonly int CameraClearColorId =
            Shader.PropertyToID("_ReferencedCameraClearColor");
        private static readonly int CameraSkyEnabledId =
            Shader.PropertyToID("_ReferencedCameraSkyEnabled");
        private static readonly int AtmosphereFlagsId =
            Shader.PropertyToID("_ReferencedAtmosphereFlags");
        private static readonly int AtmospherePlanetCenterBottomRadiusId =
            Shader.PropertyToID(
                "_ReferencedAtmospherePlanetCenterBottomRadius");
        private static readonly int AtmosphereTopRadiusMieAnisotropyId =
            Shader.PropertyToID(
                "_ReferencedAtmosphereTopRadiusMieAnisotropy");
        private static readonly int AtmosphereGroundAlbedoId =
            Shader.PropertyToID("_ReferencedAtmosphereGroundAlbedo");
        private static readonly int AtmosphereRayleighScatteringId =
            Shader.PropertyToID("_ReferencedAtmosphereRayleighScattering");
        private static readonly int AtmosphereRayleighExtinctionId =
            Shader.PropertyToID("_ReferencedAtmosphereRayleighExtinction");
        private static readonly int AtmosphereMieScatteringId =
            Shader.PropertyToID("_ReferencedAtmosphereMieScattering");
        private static readonly int AtmosphereMieExtinctionId =
            Shader.PropertyToID("_ReferencedAtmosphereMieExtinction");
        private static readonly int AtmosphereOzoneExtinctionId =
            Shader.PropertyToID("_ReferencedAtmosphereOzoneExtinction");
        private static readonly int AtmosphereOzoneLayerId =
            Shader.PropertyToID("_ReferencedAtmosphereOzoneLayer");
        private static readonly int AtmosphereSunDirectionId =
            Shader.PropertyToID("_ReferencedAtmosphereSunDirection");
        private static readonly int AtmosphereSunIlluminanceId =
            Shader.PropertyToID("_ReferencedAtmosphereSunIlluminance");
        private static readonly int AtmosphereHasSunId =
            Shader.PropertyToID("_ReferencedAtmosphereHasSun");
        private static readonly int CloudLayerParametersId =
            Shader.PropertyToID("_ReferencedCloudLayerParameters");
        private static readonly int CloudMaterialParametersId =
            Shader.PropertyToID("_ReferencedCloudMaterialParameters");
        private static readonly int CloudNoiseParametersId =
            Shader.PropertyToID("_ReferencedCloudNoiseParameters");
        private static readonly int GlobalFogEnabledId =
            Shader.PropertyToID("_ReferencedGlobalFogEnabled");
        private static readonly int GlobalFogScatteringExtinctionId =
            Shader.PropertyToID(
                "_ReferencedGlobalFogScatteringExtinction");
        private static readonly int GlobalFogHeightAnisotropyId =
            Shader.PropertyToID(
                "_ReferencedGlobalFogHeightAnisotropy");
        private static readonly int GlobalFogLightingId =
            Shader.PropertyToID("_ReferencedGlobalFogLighting");
        private static readonly int LocalFogCountId =
            Shader.PropertyToID("_ReferencedLocalFogCount");
        private static readonly int LocalFogListId =
            Shader.PropertyToID("_ReferencedLocalFogList");
        private static readonly int[] LocalFogMaskTextureIds =
            CreateLocalFogMaskTextureIds();
        private static readonly VividLocalVolumetricFogEngineData[]
            s_EmptyLocalFogStorage =
            {
                default
            };

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(Name = "ReferenceLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReferenceLightList;

        [RenderGraphResource(
            Name = "ReferenceLightListParameters",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReferenceLightListParameters;

        [RenderGraphResource(Name = "PathTracingEnvironment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_EnvironmentTexture;

        [RenderGraphResource(
            Name = "PathTracingEnvironmentBackground",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_EnvironmentBackgroundTexture;

        [RenderGraphResource(
            Name = "EnvironmentImportanceDistribution",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_EnvironmentImportanceDistribution;

        [RenderGraphResource(
            Name = "PathTracingRadiance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        [FormerlySerializedAs("m_WorldPositionTexture")]
        private RenderGraphTexture m_PathTracingRadiance;

        [RenderGraphResource(
            Name = "PathTracingAlbedo",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_PathTracingAlbedo;

        [RenderGraphResource(
            Name = "PathTracingNormal",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_PathTracingNormal;

        [RenderGraphResource(
            Name = "DebugTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DebugTexture;

        [RenderGraphResource(
            Name = "DiffuseRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseRadianceHitDistance;

        [RenderGraphResource(
            Name = "SpecularRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularRadianceHitDistance;

        [RenderGraphResource(
            Name = "PathTracingDirectLighting",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DirectLighting;

        [RenderGraphResource(
            Name = "PathTracingEmission",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Emission;

        [RenderGraphResource(
            Name = "EnvironmentDirectDiffuse",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_EnvironmentDirectDiffuse;

        [RenderGraphResource(
            Name = "EnvironmentDirectSpecular",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_EnvironmentDirectSpecular;

        [RenderGraphResource(
            Name = "DiffuseRayDirectionHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseRayDirectionHitDistance;

        [RenderGraphResource(
            Name = "SpecularRayDirectionHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularRayDirectionHitDistance;

        private RayTracingShader m_RayTracingShader;
        private GraphicsBuffer m_NvidiaShaderExtensionBuffer;
        private GraphicsBuffer m_LocalFogBuffer;
        private LocalKeyword m_ShaderExecutionReorderingKeyword;
        private bool m_ShaderExecutionReorderingKeywordAvailable;
        private LocalKeyword m_IndexedBndKeyword;
        private bool m_IndexedBndKeywordAvailable;
        private bool m_SupportsRayTracing;
        private bool m_ShaderExecutionReorderingAvailable;
        private bool m_UseShaderExecutionReordering;
        private bool m_ShaderExecutionReorderingWarningIssued;
        private string m_ShaderExecutionReorderingFailureReason;
        private bool m_ShouldSkipExecution;
        private bool m_ShouldRenderSample;
        private int m_Width = 1;
        private int m_Height = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;
        private Matrix4x4 m_WorldToView = Matrix4x4.identity;
        private Vector4 m_CameraRightWS =
            new(1.0f, 0.0f, 0.0f, 0.0f);
        private Vector4 m_CameraUpWS =
            new(0.0f, 1.0f, 0.0f, 0.0f);
        private Vector4 m_CameraForwardWS =
            new(0.0f, 0.0f, 1.0f, 0.0f);
        private ReferencedPathTracingPhysicalCameraState
            m_PhysicalCameraState =
                ReferencedPathTracingPhysicalCameraState.Disabled;
        private float m_RayMinDistance = 0.01f;
        private float m_RayMaxDistance = 1000.0f;
        private bool m_HasFiniteDirectionalLight;
        private Vector4 m_ReblurHitDistanceParameters =
            ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
        private ReferencedPathTracingReblurCheckerboardMode m_ReblurCheckerboardMode =
            ReferencedPathTracingReblurCheckerboardMode.Off;
        private ReferencedPathTracingEnvironmentState m_EnvironmentState;
        private ReferencedPathTracingAtmosphereState m_AtmosphereState;
        private ReferencedPathTracingGlobalFogState m_GlobalFogState;
        private ReferencedPathTracingLocalFogState m_LocalFogState;
        private ReferencedPathTracingCameraBackgroundState m_CameraBackgroundState;
        private ReferencedPathTracingIntegratorState m_IntegratorState;
        private ReferencedPathTracingSamplingMode m_ResolvedPathSamplingMode =
            ReferencedPathTracingSamplingMode.IndexedBnd;
        private ReferencedPathTracingDebugSettings m_DebugSettings;
        private bool m_SamplingFallbackWarningIssued;
        private readonly RenderGraphBuffer m_DefaultReferenceLightList;
        private readonly RenderGraphBuffer m_DefaultReferenceLightListParameters;
        private bool m_DefaultReferenceLightListInitialized;
        private readonly RenderGraphBuffer m_DefaultEnvironmentImportanceDistribution;
        private bool m_DefaultEnvironmentImportanceDistributionInitialized;
        private int m_FrameIndex;
        private int m_Seed;

        public ReferencedPathTracingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_DefaultReferenceLightList = RenderGraphBuffer.CreateStructured(
                "ReferenceLightListFallback",
                1,
                ReferencedPathTracingLightRecord.Stride);
            m_DefaultReferenceLightListParameters =
                RenderGraphBuffer.CreateStructured(
                    "ReferenceLightListParametersFallback",
                    Mathf.Max(
                        s_EmptyReferenceLightListStorage.Length,
                        1),
                    ReferencedPathTracingLightListStorageBlock.Stride);
            m_ReferenceLightList = m_DefaultReferenceLightList;
            m_ReferenceLightListParameters =
                m_DefaultReferenceLightListParameters;
            m_EnvironmentTexture = CreateEnvironmentTexture("PathTracingEnvironment");
            m_EnvironmentBackgroundTexture =
                CreateEnvironmentTexture("PathTracingEnvironmentBackground");
            m_DefaultEnvironmentImportanceDistribution =
                RenderGraphBuffer.CreateStructured(
                    "EnvironmentImportanceDistributionFallback",
                    ReferencedPathTracingEnvironmentImportanceLayout.ElementCount,
                    ReferencedPathTracingEnvironmentImportanceLayout.ElementStride);
            m_EnvironmentImportanceDistribution =
                m_DefaultEnvironmentImportanceDistribution;
            m_PathTracingRadiance = RenderGraphTexture.CreateOutput(
                "PathTracingRadiance",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_PathTracingAlbedo = RenderGraphTexture.CreateOutput(
                "PathTracingAlbedo",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_PathTracingNormal = RenderGraphTexture.CreateOutput(
                "PathTracingNormal",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DebugTexture = RenderGraphTexture.CreateOutput(
                "ReferencedPathTracingDebug",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DiffuseRadianceHitDistance = RenderGraphTexture.CreateOutput(
                "DiffuseRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularRadianceHitDistance = RenderGraphTexture.CreateOutput(
                "SpecularRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DirectLighting = RenderGraphTexture.CreateOutput(
                "PathTracingDirectLighting",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Emission = RenderGraphTexture.CreateOutput(
                "PathTracingEmission",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_EnvironmentDirectDiffuse = RenderGraphTexture.CreateOutput(
                "EnvironmentDirectDiffuse",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_EnvironmentDirectSpecular = RenderGraphTexture.CreateOutput(
                "EnvironmentDirectSpecular",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DiffuseRayDirectionHitDistance = RenderGraphTexture.CreateOutput(
                "DiffuseRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularRayDirectionHitDistance = RenderGraphTexture.CreateOutput(
                "SpecularRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            ConfigureOutputs(1, 1);
        }

        public override void Create()
        {
            SkyManager.Initialize();
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            if (m_SupportsRayTracing)
            {
                m_ShaderExecutionReorderingAvailable =
                    NvApiSer.TryInitializeShaderExecutionReordering(
                        ShaderExecutionReorderingUavSlot,
                        out m_ShaderExecutionReorderingFailureReason);
            }
            else
            {
                m_ShaderExecutionReorderingAvailable = false;
                m_ShaderExecutionReorderingFailureReason =
                    "ray tracing is unavailable on the active graphics device";
            }

            m_RayTracingShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathtracingRayTracing;

            if (m_RayTracingShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the ray-tracing shader resource for {nameof(ReferencedPathTracingPass)}.");
                return;
            }

            RefreshShaderExecutionReorderingKeyword();
            RefreshIndexedBndKeyword();
            PrepareShaderExecutionReorderingBuffer();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);

            ConfigureOutputs(m_Width, m_Height);
            PrepareReferenceLightListFallback();
            PrepareEnvironmentImportanceDistributionFallback();
            PrepareDirectionalDenoiserState();
            PrepareEnvironment(frameData, cameraData);
            m_GlobalFogState =
                ReferencedPathTracingGlobalFogState.Resolve();
            m_LocalFogState =
                ReferencedPathTracingLocalFogState.Resolve(
                    camera,
                    m_GlobalFogState.enabled);
            PrepareLocalFogBuffer();
            m_IntegratorState = ReferencedPathTracingIntegratorState.Resolve();
            RefreshIndexedBndKeyword();
            ResolvePathSamplingMode();
            m_DebugSettings = ReferencedPathTracingDebugSettings.Resolve(
                VividRenderingDebugDisplaySettings.Data);
            // A RayTracingShader reimport invalidates LocalKeyword handles while
            // keeping the serialized shader reference alive. Recreate the handle
            // before querying it so editor hot reload cannot dereference stale
            // native keyword state.
            RefreshShaderExecutionReorderingKeyword();
            PrepareShaderExecutionReorderingState();
            var reblurSettings = ReferencedPathTracingReblurSettingsResolver.Resolve();
            m_ReblurHitDistanceParameters = reblurSettings.hitDistanceParameters;
            m_ReblurCheckerboardMode = reblurSettings.enabled
                ? reblurSettings.checkerboardMode
                : ReferencedPathTracingReblurCheckerboardMode.Off;
            if (m_IntegratorState.deterministicSampling)
            {
                // Canonical samples must not inherit a denoiser checkerboard pattern.
                m_ReblurCheckerboardMode =
                    ReferencedPathTracingReblurCheckerboardMode.Off;
            }
            var pathTracingData =
                frameData.GetOrCreate<VividReferencedPathTracingData>();

            m_ShouldSkipExecution = camera == null || camera.orthographic;
            if (m_ShouldSkipExecution)
            {
                pathTracingData.Reset();
                m_ShouldRenderSample = false;
                m_CameraPositionWS = Vector4.zero;
                m_PixelCoordToViewDirWS = Matrix4x4.identity;
                m_WorldToView = Matrix4x4.identity;
                m_CameraRightWS =
                    new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                m_CameraUpWS =
                    new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
                m_CameraForwardWS =
                    new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
                m_PhysicalCameraState =
                    ReferencedPathTracingPhysicalCameraState.Disabled;
                m_RayMinDistance = 0.01f;
                m_RayMaxDistance = 1000.0f;
                m_FrameIndex = 0;
                m_Seed = m_IntegratorState.deterministicSampling
                    ? m_IntegratorState.fixedSeed
                    : 0;
                return;
            }

            var cameraPosition = camera.transform.position;
            m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_WorldToView = cameraData.GetViewMatrix();
            m_CameraRightWS = camera.transform.right;
            m_CameraUpWS = camera.transform.up;
            m_CameraForwardWS = camera.transform.forward;
            m_PhysicalCameraState =
                ReferencedPathTracingPhysicalCameraState.Resolve(
                    camera,
                    DepthOfFieldSettingsResolver
                        .ResolveForReferencePathTracing());
            m_RayMinDistance = Mathf.Max(camera.nearClipPlane, 0.0001f);
            m_RayMaxDistance = Mathf.Max(camera.farClipPlane, m_RayMinDistance + 0.0001f);
            if (m_PhysicalCameraState.enabled)
            {
                // REBLUR assumes a stable pinhole primary surface and cannot
                // interpret stochastic aperture visibility or checkerboarding.
                m_ReblurCheckerboardMode =
                    ReferencedPathTracingReblurCheckerboardMode.Off;
            }
            PrepareSampleSequence(
                frameData,
                cameraData,
                pathTracingData);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution
                || !m_ShouldRenderSample
                || !m_SupportsRayTracing
                || m_RayTracingShader == null
                || m_SceneAccelerationStructure == null
                || !HaveValidOutputs())
            {
                return;
            }

            var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (accelerationStructure == null)
                return;

            var cmd = context.GetNativeCommandBuffer();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (m_ShaderExecutionReorderingKeywordAvailable)
                {
                    cmd.SetKeyword(
                        m_RayTracingShader,
                        m_ShaderExecutionReorderingKeyword,
                        m_UseShaderExecutionReordering);
                }

                if (m_IndexedBndKeywordAvailable)
                {
                    cmd.SetKeyword(
                        m_RayTracingShader,
                        m_IndexedBndKeyword,
                        m_ResolvedPathSamplingMode
                            == ReferencedPathTracingSamplingMode.IndexedBnd);
                }

                if (m_UseShaderExecutionReordering)
                {
                    // Unity validates every declared ray-tracing resource before dispatch.
                    // NVAPI consumes this UAV as its instruction channel, but it must still
                    // be backed by a counter-capable structured buffer.
                    cmd.SetBufferCounterValue(
                        m_NvidiaShaderExtensionBuffer,
                        0);
                    cmd.SetRayTracingBufferParam(
                        m_RayTracingShader,
                        NvidiaShaderExtensionBufferId,
                        m_NvidiaShaderExtensionBuffer);
                }

                cmd.SetRayTracingShaderPass(m_RayTracingShader, MaterialShaderPassName);
                cmd.SetRayTracingAccelerationStructure(
                    m_RayTracingShader,
                    AccelerationStructureName,
                    accelerationStructure);
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    PathTracingRadianceId,
                    m_PathTracingRadiance.innerHandle);
                BindOutput(cmd, PathTracingAlbedoId, m_PathTracingAlbedo);
                BindOutput(cmd, PathTracingNormalId, m_PathTracingNormal);
                BindOutput(cmd, DebugTextureId, m_DebugTexture);
                BindOutput(cmd, DiffuseRadianceHitDistanceId, m_DiffuseRadianceHitDistance);
                BindOutput(cmd, SpecularRadianceHitDistanceId, m_SpecularRadianceHitDistance);
                BindOutput(cmd, DirectLightingId, m_DirectLighting);
                BindOutput(cmd, EmissionId, m_Emission);
                BindOutput(
                    cmd,
                    EnvironmentDirectDiffuseId,
                    m_EnvironmentDirectDiffuse);
                BindOutput(
                    cmd,
                    EnvironmentDirectSpecularId,
                    m_EnvironmentDirectSpecular);
                BindOutput(cmd, DiffuseRayDirectionHitDistanceId, m_DiffuseRayDirectionHitDistance);
                BindOutput(cmd, SpecularRayDirectionHitDistanceId, m_SpecularRayDirectionHitDistance);
                cmd.SetRayTracingVectorParam(m_RayTracingShader, CameraPositionId, m_CameraPositionWS);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    PixelCoordToViewDirWSId,
                    m_PixelCoordToViewDirWS);
                cmd.SetRayTracingMatrixParam(m_RayTracingShader, WorldToViewId, m_WorldToView);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    CameraRightWSId,
                    m_CameraRightWS);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    CameraUpWSId,
                    m_CameraUpWS);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    CameraForwardWSId,
                    m_CameraForwardWS);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    PhysicalCameraParametersId,
                    m_PhysicalCameraState.shaderParameters);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMinDistanceId, m_RayMinDistance);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMaxDistanceId, m_RayMaxDistance);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    MaxBounceCountId,
                    m_IntegratorState.maxBounceCount);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    RussianRouletteStartBounceId,
                    m_IntegratorState.russianRouletteStartBounce);
                cmd.SetRayTracingIntParam(m_RayTracingShader, FrameIndexId, m_FrameIndex);
                cmd.SetRayTracingIntParam(m_RayTracingShader, SeedId, m_Seed);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    PathSamplingModeId,
                    (int)m_ResolvedPathSamplingMode);
                if (m_ResolvedPathSamplingMode
                    == ReferencedPathTracingSamplingMode.IndexedBnd)
                {
                    BlueNoise.Instance?.Bind(cmd, m_RayTracingShader);
                }
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    ReblurHitDistanceParametersId,
                    m_ReblurHitDistanceParameters);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    ReblurCheckerboardModeId,
                    (int)m_ReblurCheckerboardMode);
                BindReferenceLightList(cmd);
                BindEnvironment(cmd);
                // Keep the serialized setting as a local-light NEE gate. Reference PT no
                // longer consumes the camera-space ReGIR grid or reservoir resources.
                var localLightNeeEnabled = m_IntegratorState.enableReGIR;
                cmd.SetGlobalInt(
                    LocalLightNeeEnabledId,
                    localLightNeeEnabled ? 1 : 0);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    LocalLightNeeEnabledId,
                    localLightNeeEnabled ? 1 : 0);
                var shadingPointLightSelectionEnabled =
                    m_IntegratorState.shadingPointLightSelection;
                cmd.SetGlobalInt(
                    ShadingPointLightSelectionEnabledId,
                    shadingPointLightSelectionEnabled ? 1 : 0);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    ShadingPointLightSelectionEnabledId,
                    shadingPointLightSelectionEnabled ? 1 : 0);
                cmd.SetGlobalFloat(
                    GlobalLightProposalProbabilityId,
                    m_IntegratorState.globalLightProposalProbability);
                cmd.SetRayTracingFloatParam(
                    m_RayTracingShader,
                    GlobalLightProposalProbabilityId,
                    m_IntegratorState.globalLightProposalProbability);
                var lightSpatialIndexEnabled =
                    m_IntegratorState.lightSpatialIndex;
                cmd.SetGlobalInt(
                    LightSpatialIndexEnabledId,
                    lightSpatialIndexEnabled ? 1 : 0);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    LightSpatialIndexEnabledId,
                    lightSpatialIndexEnabled ? 1 : 0);
                cmd.DispatchRays(
                    m_RayTracingShader,
                    RayGenerationShaderName,
                    (uint)m_Width,
                    (uint)m_Height,
                    1,
                    null);
            }
        }

        public override void Dispose()
        {
            m_NvidiaShaderExtensionBuffer?.Dispose();
            m_NvidiaShaderExtensionBuffer = null;
            m_LocalFogBuffer?.Dispose();
            m_LocalFogBuffer = null;
            m_RayTracingShader = null;
            m_ShaderExecutionReorderingKeyword = default;
            m_ShaderExecutionReorderingKeywordAvailable = false;
            m_IndexedBndKeyword = default;
            m_IndexedBndKeywordAvailable = false;
            m_SupportsRayTracing = false;
            m_ShaderExecutionReorderingAvailable = false;
            m_UseShaderExecutionReordering = false;
            m_ShaderExecutionReorderingWarningIssued = false;
            m_ShaderExecutionReorderingFailureReason = null;
            m_ShouldSkipExecution = false;
            m_ShouldRenderSample = false;
            m_Width = 1;
            m_Height = 1;
            m_CameraPositionWS = Vector4.zero;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
            m_WorldToView = Matrix4x4.identity;
            m_CameraRightWS =
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            m_CameraUpWS =
                new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_CameraForwardWS =
                new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            m_PhysicalCameraState =
                ReferencedPathTracingPhysicalCameraState.Disabled;
            m_RayMinDistance = 0.01f;
            m_RayMaxDistance = 1000.0f;
            m_HasFiniteDirectionalLight = false;
            m_ReblurHitDistanceParameters =
                ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
            m_ReblurCheckerboardMode = ReferencedPathTracingReblurCheckerboardMode.Off;
            m_EnvironmentState = default;
            m_AtmosphereState = default;
            m_GlobalFogState =
                ReferencedPathTracingGlobalFogState.Disabled;
            m_LocalFogState =
                ReferencedPathTracingLocalFogState.Disabled;
            m_CameraBackgroundState = default;
            m_IntegratorState = default;
            m_ResolvedPathSamplingMode =
                ReferencedPathTracingSamplingMode.IndexedBnd;
            m_DebugSettings = default;
            m_SamplingFallbackWarningIssued = false;
            m_DefaultReferenceLightList?.ClearImportedBuffer();
            m_DefaultReferenceLightListParameters?.ClearImportedBuffer();
            m_DefaultReferenceLightListInitialized = false;
            m_DefaultEnvironmentImportanceDistribution?.ClearImportedBuffer();
            m_DefaultEnvironmentImportanceDistributionInitialized = false;
            m_FrameIndex = 0;
            m_Seed = 0;
            ReferencedPathTracingSampleSequence.Dispose();
        }

        private void PrepareShaderExecutionReorderingState()
        {
            var requested =
                m_IntegratorState.enableShaderExecutionReordering;
            var keywordAvailable =
                m_ShaderExecutionReorderingKeywordAvailable;
            m_UseShaderExecutionReordering =
                requested
                && m_ShaderExecutionReorderingAvailable
                && keywordAvailable
                && m_NvidiaShaderExtensionBuffer != null;

            if (!requested
                || m_UseShaderExecutionReordering
                || m_ShaderExecutionReorderingWarningIssued)
            {
                return;
            }

            var failureReason = keywordAvailable
                ? m_ShaderExecutionReorderingFailureReason
                : "the SER shader variant is unavailable";
            Debug.LogWarning(
                $"[VividRP] Reference Path Tracing requested NVIDIA Shader Execution " +
                $"Reordering, but {failureReason ?? "initialization failed"}. " +
                "The standard TraceRay variant will be used.");
            m_ShaderExecutionReorderingWarningIssued = true;
        }

        private void RefreshShaderExecutionReorderingKeyword()
        {
            m_ShaderExecutionReorderingKeyword = default;
            m_ShaderExecutionReorderingKeywordAvailable = false;
            if (m_RayTracingShader == null)
                return;

            try
            {
                var keyword = new LocalKeyword(
                    m_RayTracingShader,
                    ShaderExecutionReorderingKeywordName);
                if (!keyword.isValid)
                    return;

                m_ShaderExecutionReorderingKeyword = keyword;
                m_ShaderExecutionReorderingKeywordAvailable = true;
            }
            catch (System.Exception exception)
            {
                m_ShaderExecutionReorderingFailureReason =
                    $"the SER shader keyword could not be refreshed ({exception.Message})";
            }
        }

        private void PrepareShaderExecutionReorderingBuffer()
        {
            m_NvidiaShaderExtensionBuffer?.Dispose();
            m_NvidiaShaderExtensionBuffer = null;
            if (!m_ShaderExecutionReorderingAvailable)
                return;

            try
            {
                m_NvidiaShaderExtensionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Counter,
                    1,
                    NvidiaShaderExtensionStructStride)
                {
                    name = "VividRP NVIDIA Shader Extension"
                };
                m_NvidiaShaderExtensionBuffer.SetCounterValue(0);
            }
            catch (System.Exception exception)
            {
                m_ShaderExecutionReorderingAvailable = false;
                m_ShaderExecutionReorderingFailureReason =
                    $"the NVIDIA shader-extension UAV could not be created ({exception.Message})";
                m_NvidiaShaderExtensionBuffer?.Dispose();
                m_NvidiaShaderExtensionBuffer = null;
            }
        }

        private void ConfigureOutputs(int width, int height)
        {
            ConfigureOutput(
                m_PathTracingRadiance,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "PathTracingRadiance");
            ConfigureOutput(
                m_PathTracingAlbedo,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "PathTracingAlbedo");
            ConfigureOutput(
                m_PathTracingNormal,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "PathTracingNormal");
            ConfigureOutput(
                m_DebugTexture,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "ReferencedPathTracingDebug");
            ConfigureOutput(
                m_DiffuseRadianceHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "DiffuseRadianceHitDistance");
            ConfigureOutput(
                m_SpecularRadianceHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "SpecularRadianceHitDistance");
            ConfigureOutput(
                m_DirectLighting,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "PathTracingDirectLighting");
            ConfigureOutput(
                m_Emission,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "PathTracingEmission");
            ConfigureOutput(
                m_EnvironmentDirectDiffuse,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "EnvironmentDirectDiffuse");
            ConfigureOutput(
                m_EnvironmentDirectSpecular,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "EnvironmentDirectSpecular");
            ConfigureDlssRayGuideOutput(
                m_DiffuseRayDirectionHitDistance,
                width,
                height,
                "DiffuseRayDirectionHitDistance");
            ConfigureDlssRayGuideOutput(
                m_SpecularRayDirectionHitDistance,
                width,
                height,
                "SpecularRayDirectionHitDistance");
        }

        private static void ConfigureDlssRayGuideOutput(
            RenderGraphTexture texture,
            int width,
            int height,
            string name)
        {
            ConfigureOutput(
                texture,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                name);
            if (texture?.desc != null)
            {
                texture.desc.ClearColor =
                    new Color(0.0f, 0.0f, 0.0f, DlssInfiniteHitDistance);
            }
        }

        private static void ConfigureOutput(
            RenderGraphTexture texture,
            int width,
            int height,
            GraphicsFormat format,
            string name)
        {
            if (texture?.desc == null)
                return;

            texture.Resize(width, height);
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
            texture.desc.Name = name;
        }

        private bool HaveValidOutputs()
        {
            return IsValid(m_PathTracingRadiance)
                && IsValid(m_PathTracingAlbedo)
                && IsValid(m_PathTracingNormal)
                && IsValid(m_DebugTexture)
                && IsValid(m_DiffuseRadianceHitDistance)
                && IsValid(m_SpecularRadianceHitDistance)
                && IsValid(m_DirectLighting)
                && IsValid(m_Emission)
                && IsValid(m_EnvironmentDirectDiffuse)
                && IsValid(m_EnvironmentDirectSpecular)
                && IsValid(m_DiffuseRayDirectionHitDistance)
                && IsValid(m_SpecularRayDirectionHitDistance);
        }

        private static bool IsValid(RenderGraphTexture texture)
        {
            return texture?.innerHandle.IsValid() == true;
        }

        private void BindOutput(CommandBuffer cmd, int propertyId, RenderGraphTexture texture)
        {
            cmd.SetRayTracingTextureParam(m_RayTracingShader, propertyId, texture.innerHandle);
        }

        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
        }

        private static RenderGraphTexture CreateEnvironmentTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = name
                }
            };
        }

        private void PrepareEnvironment(
            ContextContainer frameData,
            VividCameraData cameraData)
        {
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var settings =
                VividVolumeManagerUtility
                    .GetReferencedPathTracingSettingsVolume();
            m_EnvironmentTexture.ClearImportedHandle();
            m_EnvironmentBackgroundTexture.ClearImportedHandle();
            m_EnvironmentState =
                ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
            m_AtmosphereState =
                ReferencedPathTracingAtmosphereState.Resolve(
                    frameData,
                    settings);
            m_CameraBackgroundState =
                ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

            // HDRI and Reference Atmosphere are mutually exclusive. Reference Atmosphere A0
            // captures only a physical parameter snapshot, so the cubemap ports deliberately
            // receive the black fallback and cannot leak raster sky radiance into the path.
            SkyManager.ImportSpecularCubemap(
                m_EnvironmentTexture,
                m_EnvironmentState.hasHdri ? skyData : null);
            SkyManager.ImportSkySourceCubemap(
                m_EnvironmentBackgroundTexture,
                m_EnvironmentState.hasHdri ? skyData : null);
        }

        private void PrepareSampleSequence(
            ContextContainer frameData,
            VividCameraData cameraData,
            VividReferencedPathTracingData pathTracingData)
        {
            var renderFrameIndex = cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
            var effectiveIntegratorSignature =
                m_IntegratorState.ResolveEffectiveSignature(
                    m_ResolvedPathSamplingMode);
            var frameSignature =
                ReferencedPathTracingFrameSignatureUtility.Compute(
                    frameData,
                    cameraData,
                    m_Width,
                    m_Height,
                    effectiveIntegratorSignature,
                    m_EnvironmentState,
                    m_AtmosphereState,
                    m_GlobalFogState,
                    m_LocalFogState,
                    m_CameraBackgroundState,
                    m_PhysicalCameraState);
            var temporalData = frameData.Contains<VividTemporalData>()
                ? frameData.Get<VividTemporalData>()
                : null;
            var sampleIndex =
                ReferencedPathTracingSampleSequence.Resolve(
                    cameraData.camera,
                    renderFrameIndex,
                    frameSignature,
                    temporalData == null || temporalData.isFirstFrame,
                    (uint)m_IntegratorState.targetSampleCount);
            m_ShouldRenderSample = sampleIndex
                < (uint)m_IntegratorState.targetSampleCount;

            m_FrameIndex = unchecked((int)sampleIndex);
            m_Seed = m_IntegratorState.deterministicSampling
                ? m_IntegratorState.fixedSeed
                : 0;
            pathTracingData.isValid = true;
            pathTracingData.deterministicSampling =
                m_IntegratorState.deterministicSampling;
            pathTracingData.sampleIndex = sampleIndex;
            pathTracingData.pathSamplingMode =
                m_ResolvedPathSamplingMode;
            pathTracingData.samplingContractVersion =
                ReferencedPathTracingSamplingContract.Version;
            pathTracingData.frameSignature = frameSignature;
            pathTracingData.integratorSignature =
                effectiveIntegratorSignature;
            pathTracingData.targetSampleCount =
                m_IntegratorState.targetSampleCount;
            pathTracingData.accumulatedSampleCount = Math.Min(
                (ulong)sampleIndex,
                (ulong)m_IntegratorState.targetSampleCount);
            pathTracingData.shouldRenderSample = m_ShouldRenderSample;
            pathTracingData.isConverged = !m_ShouldRenderSample;
            pathTracingData.mainLightInDenoiserSignals =
                m_HasFiniteDirectionalLight;
            pathTracingData.physicalCameraDofEnabled =
                m_PhysicalCameraState.enabled;
        }

        private void ResolvePathSamplingMode()
        {
            m_ResolvedPathSamplingMode = m_IntegratorState.pathSamplingMode;
            if (m_ResolvedPathSamplingMode
                != ReferencedPathTracingSamplingMode.IndexedBnd)
                return;
            if (m_IndexedBndKeywordAvailable
                && BlueNoise.Instance?.SupportsBnd256 == true)
                return;

            m_ResolvedPathSamplingMode =
                ReferencedPathTracingSamplingMode.IndexedHash;
            if (m_SamplingFallbackWarningIssued)
                return;

            Debug.LogWarning(
                "[VividRP] Reference Path Tracing could not bind the 256-SPP " +
                "Owen-Sobol blue-noise set. Falling back to the fixed-dimension " +
                "indexed hash sampler; canonical BND captures will be rejected.");
            m_SamplingFallbackWarningIssued = true;
        }

        private void RefreshIndexedBndKeyword()
        {
            m_IndexedBndKeyword = default;
            m_IndexedBndKeywordAvailable = false;
            if (m_RayTracingShader == null)
                return;

            try
            {
                var keyword = new LocalKeyword(
                    m_RayTracingShader,
                    IndexedBndKeywordName);
                if (!keyword.isValid)
                    return;

                m_IndexedBndKeyword = keyword;
                m_IndexedBndKeywordAvailable = true;
            }
            catch (System.Exception)
            {
                // ResolvePathSamplingMode selects the resource-free indexed
                // hash variant when the keyword cannot be refreshed.
            }
        }

        private void BindEnvironment(CommandBuffer cmd)
        {
            var hasEnvironmentBinding =
                m_EnvironmentTexture?.innerHandle.IsValid() == true;
            if (hasEnvironmentBinding)
            {
                cmd.SetGlobalTexture(EnvironmentTextureId, m_EnvironmentTexture.innerHandle);
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    EnvironmentTextureId,
                    m_EnvironmentTexture.innerHandle);
            }

            var hasBackgroundBinding =
                m_EnvironmentBackgroundTexture?.innerHandle.IsValid() == true;
            if (hasBackgroundBinding)
            {
                cmd.SetGlobalTexture(
                    EnvironmentBackgroundTextureId,
                    m_EnvironmentBackgroundTexture.innerHandle);
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    EnvironmentBackgroundTextureId,
                    m_EnvironmentBackgroundTexture.innerHandle);
            }

            if (m_EnvironmentImportanceDistribution?.innerHandle.IsValid() == true)
            {
                cmd.SetGlobalBuffer(
                    EnvironmentImportanceDistributionId,
                    m_EnvironmentImportanceDistribution.innerHandle);
                cmd.SetRayTracingBufferParam(
                    m_RayTracingShader,
                    EnvironmentImportanceDistributionId,
                    m_EnvironmentImportanceDistribution.innerHandle);
            }

            var tint = m_EnvironmentState.tint;
            var environmentTint = new Vector4(tint.r, tint.g, tint.b, 1.0f);
            var environmentParameters = new Vector4(
                m_EnvironmentState.intensityMultiplier,
                m_EnvironmentState.rotation,
                m_EnvironmentState.maxMipLevel,
                hasEnvironmentBinding && m_EnvironmentState.hasHdri ? 1.0f : 0.0f);
            var environmentMode = (int)m_EnvironmentState.mode;
            var lightingEnabled =
                hasEnvironmentBinding && m_EnvironmentState.lightingEnabled ? 1 : 0;
            var cameraVisible =
                hasBackgroundBinding && m_EnvironmentState.cameraVisible ? 1 : 0;
            var importanceSamplingEnabled =
                hasEnvironmentBinding && m_EnvironmentState.importanceSamplingEnabled ? 1 : 0;
            var neeEnabled =
                hasEnvironmentBinding && m_EnvironmentState.neeEnabled ? 1 : 0;
            var samplingMode = (int)m_EnvironmentState.samplingMode;
            var estimatorMode = (int)m_IntegratorState.estimatorMode;
            var transportDebugMode = (int)m_DebugSettings.transportMode;
            var debugMode = (int)m_DebugSettings.environmentMode;
            var clearColor = m_CameraBackgroundState.clearColor;
            var cameraClearColor = new Vector4(
                clearColor.r,
                clearColor.g,
                clearColor.b,
                clearColor.a);
            var cameraSkyEnabled = m_CameraBackgroundState.skyRequested ? 1 : 0;

            cmd.SetGlobalVector(EnvironmentTintId, environmentTint);
            cmd.SetGlobalVector(EnvironmentParametersId, environmentParameters);
            cmd.SetGlobalInt(EnvironmentModeId, environmentMode);
            cmd.SetGlobalInt(EnvironmentLightingEnabledId, lightingEnabled);
            cmd.SetGlobalInt(EnvironmentCameraVisibleId, cameraVisible);
            cmd.SetGlobalInt(
                EnvironmentImportanceSamplingEnabledId,
                importanceSamplingEnabled);
            cmd.SetGlobalInt(EnvironmentNeeEnabledId, neeEnabled);
            cmd.SetGlobalInt(EnvironmentSamplingModeId, samplingMode);
            cmd.SetGlobalInt(TransportEstimatorModeId, estimatorMode);
            cmd.SetGlobalInt(TransportDebugModeId, transportDebugMode);
            cmd.SetGlobalInt(EnvironmentDebugModeId, debugMode);
            cmd.SetGlobalVector(CameraClearColorId, cameraClearColor);
            cmd.SetGlobalInt(CameraSkyEnabledId, cameraSkyEnabled);

            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                EnvironmentTintId,
                environmentTint);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                EnvironmentParametersId,
                environmentParameters);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentModeId,
                environmentMode);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentLightingEnabledId,
                lightingEnabled);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentCameraVisibleId,
                cameraVisible);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentImportanceSamplingEnabledId,
                importanceSamplingEnabled);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentNeeEnabledId,
                neeEnabled);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentSamplingModeId,
                samplingMode);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                TransportEstimatorModeId,
                estimatorMode);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                TransportDebugModeId,
                transportDebugMode);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                EnvironmentDebugModeId,
                debugMode);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                CameraClearColorId,
                cameraClearColor);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                CameraSkyEnabledId,
                cameraSkyEnabled);
            BindAtmosphereContract(cmd);
            BindGlobalFogContract(cmd);
            BindLocalFogContract(cmd);
        }

        private void BindLocalFogContract(CommandBuffer cmd)
        {
            var count = m_LocalFogState.count;
            cmd.SetGlobalInt(LocalFogCountId, count);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                LocalFogCountId,
                count);

            var fallbackMask =
                VividLocalVolumetricFogManager.defaultMaskTexture;
            var maskTextures = m_LocalFogState.maskTextures;
            for (var index = 0;
                index
                    < ReferencedPathTracingLocalFogState
                        .MaximumMaskTextureSlotCount;
                index++)
            {
                var maskTexture =
                    maskTextures != null
                        && index < maskTextures.Length
                        ? maskTextures[index]
                        : fallbackMask;
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    LocalFogMaskTextureIds[index],
                    maskTexture != null
                        ? maskTexture
                        : fallbackMask);
            }

            if (m_LocalFogBuffer == null)
                return;

            cmd.SetGlobalBuffer(
                LocalFogListId,
                m_LocalFogBuffer);
            cmd.SetRayTracingBufferParam(
                m_RayTracingShader,
                LocalFogListId,
                m_LocalFogBuffer);
        }

        private static int[] CreateLocalFogMaskTextureIds()
        {
            var textureIds =
                new int[
                    ReferencedPathTracingLocalFogState
                        .MaximumMaskTextureSlotCount];
            for (var index = 0; index < textureIds.Length; index++)
            {
                textureIds[index] = Shader.PropertyToID(
                    $"_ReferencedLocalFogMask{index}");
            }

            return textureIds;
        }

        private void BindGlobalFogContract(CommandBuffer cmd)
        {
            var scatteringAlbedo =
                m_GlobalFogState.scatteringAlbedo;
            var scatteringExtinction = new Vector4(
                scatteringAlbedo.x,
                scatteringAlbedo.y,
                scatteringAlbedo.z,
                m_GlobalFogState.extinction);
            var heightAnisotropy = new Vector4(
                m_GlobalFogState.baseHeight,
                m_GlobalFogState.reciprocalScaleHeight,
                m_GlobalFogState.maxDistance,
                m_GlobalFogState.anisotropy);
            var lighting = new Vector4(
                m_GlobalFogState.globalLightProbeDimmer,
                m_GlobalFogState.directionalLightsOnly
                    ? 1.0f
                    : 0.0f,
                0.0f,
                0.0f);
            var enabled =
                m_GlobalFogState.enabled ? 1 : 0;

            cmd.SetGlobalInt(GlobalFogEnabledId, enabled);
            cmd.SetGlobalVector(
                GlobalFogScatteringExtinctionId,
                scatteringExtinction);
            cmd.SetGlobalVector(
                GlobalFogHeightAnisotropyId,
                heightAnisotropy);
            cmd.SetGlobalVector(
                GlobalFogLightingId,
                lighting);

            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                GlobalFogEnabledId,
                enabled);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                GlobalFogScatteringExtinctionId,
                scatteringExtinction);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                GlobalFogHeightAnisotropyId,
                heightAnisotropy);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                GlobalFogLightingId,
                lighting);
        }

        private void PrepareLocalFogBuffer()
        {
            var requiredCount =
                Mathf.Max(m_LocalFogState.count, 1);
            var requiredStride =
                VividLocalVolumetricFogEngineData.Stride;
            if (m_LocalFogBuffer == null
                || !m_LocalFogBuffer.IsValid()
                || m_LocalFogBuffer.count != requiredCount
                || m_LocalFogBuffer.stride != requiredStride)
            {
                m_LocalFogBuffer?.Dispose();
                m_LocalFogBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    requiredCount,
                    requiredStride)
                {
                    name = "Referenced Path Tracing Local Fog List"
                };
            }

            m_LocalFogBuffer.SetData(
                m_LocalFogState.count > 0
                    ? m_LocalFogState.records
                    : s_EmptyLocalFogStorage);
        }

        private void BindAtmosphereContract(CommandBuffer cmd)
        {
            var parameters = m_AtmosphereState.parameters;
            var planetCenterBottomRadius = new Vector4(
                parameters.planetCenter.x,
                parameters.planetCenter.y,
                parameters.planetCenter.z,
                parameters.bottomRadius);
            var topRadiusMieAnisotropy = new Vector4(
                parameters.topRadius,
                parameters.mieAnisotropy,
                parameters.intensityMultiplier,
                0.0f);
            var groundAlbedo = new Vector4(
                parameters.groundAlbedo.x,
                parameters.groundAlbedo.y,
                parameters.groundAlbedo.z,
                0.0f);
            var rayleighScattering = new Vector4(
                parameters.rayleighScattering.x,
                parameters.rayleighScattering.y,
                parameters.rayleighScattering.z,
                parameters.rayleighScaleHeight);
            var rayleighExtinction = new Vector4(
                parameters.rayleighExtinction.x,
                parameters.rayleighExtinction.y,
                parameters.rayleighExtinction.z,
                0.0f);
            var mieScattering = new Vector4(
                parameters.mieScattering.x,
                parameters.mieScattering.y,
                parameters.mieScattering.z,
                parameters.mieScaleHeight);
            var mieExtinction = new Vector4(
                parameters.mieExtinction.x,
                parameters.mieExtinction.y,
                parameters.mieExtinction.z,
                0.0f);
            var ozoneExtinction = new Vector4(
                parameters.ozoneExtinction.x,
                parameters.ozoneExtinction.y,
                parameters.ozoneExtinction.z,
                0.0f);
            var ozoneLayer = new Vector4(
                parameters.ozoneLayerStart,
                parameters.ozoneLayerWidth,
                0.0f,
                0.0f);
            var sunDirection = new Vector4(
                m_AtmosphereState.sunDirection.x,
                m_AtmosphereState.sunDirection.y,
                m_AtmosphereState.sunDirection.z,
                0.5f * m_AtmosphereState.sunAngularDiameter);
            var sunIlluminance = new Vector4(
                m_AtmosphereState.sunIlluminance.x,
                m_AtmosphereState.sunIlluminance.y,
                m_AtmosphereState.sunIlluminance.z,
                m_AtmosphereState.sunShadowStrength);
            var clouds = m_AtmosphereState.cloudParameters;
            var cloudLayerParameters = new Vector4(
                clouds.bottomRadius,
                clouds.topRadius,
                clouds.coverage,
                clouds.extinction);
            var cloudMaterialParameters = new Vector4(
                clouds.scatteringAlbedo.x,
                clouds.scatteringAlbedo.y,
                clouds.scatteringAlbedo.z,
                clouds.anisotropy);
            var cloudNoiseParameters = new Vector4(
                clouds.noiseScale,
                clouds.noiseSeed,
                (int)clouds.multipleScatteringMode,
                clouds.multipleScatteringStrength);
            var flags = (int)m_AtmosphereState.flags;
            var hasSun = m_AtmosphereState.hasSun ? 1 : 0;

            cmd.SetGlobalInt(AtmosphereFlagsId, flags);
            cmd.SetGlobalVector(
                AtmospherePlanetCenterBottomRadiusId,
                planetCenterBottomRadius);
            cmd.SetGlobalVector(
                AtmosphereTopRadiusMieAnisotropyId,
                topRadiusMieAnisotropy);
            cmd.SetGlobalVector(AtmosphereGroundAlbedoId, groundAlbedo);
            cmd.SetGlobalVector(
                AtmosphereRayleighScatteringId,
                rayleighScattering);
            cmd.SetGlobalVector(
                AtmosphereRayleighExtinctionId,
                rayleighExtinction);
            cmd.SetGlobalVector(AtmosphereMieScatteringId, mieScattering);
            cmd.SetGlobalVector(AtmosphereMieExtinctionId, mieExtinction);
            cmd.SetGlobalVector(
                AtmosphereOzoneExtinctionId,
                ozoneExtinction);
            cmd.SetGlobalVector(AtmosphereOzoneLayerId, ozoneLayer);
            cmd.SetGlobalVector(AtmosphereSunDirectionId, sunDirection);
            cmd.SetGlobalVector(
                AtmosphereSunIlluminanceId,
                sunIlluminance);
            cmd.SetGlobalInt(AtmosphereHasSunId, hasSun);
            cmd.SetGlobalVector(
                CloudLayerParametersId,
                cloudLayerParameters);
            cmd.SetGlobalVector(
                CloudMaterialParametersId,
                cloudMaterialParameters);
            cmd.SetGlobalVector(
                CloudNoiseParametersId,
                cloudNoiseParameters);

            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                AtmosphereFlagsId,
                flags);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmospherePlanetCenterBottomRadiusId,
                planetCenterBottomRadius);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereTopRadiusMieAnisotropyId,
                topRadiusMieAnisotropy);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereGroundAlbedoId,
                groundAlbedo);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereRayleighScatteringId,
                rayleighScattering);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereRayleighExtinctionId,
                rayleighExtinction);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereMieScatteringId,
                mieScattering);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereMieExtinctionId,
                mieExtinction);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereOzoneExtinctionId,
                ozoneExtinction);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereOzoneLayerId,
                ozoneLayer);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereSunDirectionId,
                sunDirection);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                AtmosphereSunIlluminanceId,
                sunIlluminance);
            cmd.SetRayTracingIntParam(
                m_RayTracingShader,
                AtmosphereHasSunId,
                hasSun);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                CloudLayerParametersId,
                cloudLayerParameters);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                CloudMaterialParametersId,
                cloudMaterialParameters);
            cmd.SetRayTracingVectorParam(
                m_RayTracingShader,
                CloudNoiseParametersId,
                cloudNoiseParameters);
        }

        private void PrepareEnvironmentImportanceDistributionFallback()
        {
            if (!ReferenceEquals(
                    m_EnvironmentImportanceDistribution,
                    m_DefaultEnvironmentImportanceDistribution))
            {
                return;
            }

            m_DefaultEnvironmentImportanceDistribution.EnsureImportedBuffer();
            if (m_DefaultEnvironmentImportanceDistributionInitialized)
                return;

            m_DefaultEnvironmentImportanceDistribution.SetData(
                new float[ReferencedPathTracingEnvironmentImportanceLayout.ElementCount]);
            m_DefaultEnvironmentImportanceDistributionInitialized = true;
        }

        private void PrepareReferenceLightListFallback()
        {
            if (!ReferenceEquals(
                    m_ReferenceLightList,
                    m_DefaultReferenceLightList)
                && !ReferenceEquals(
                    m_ReferenceLightListParameters,
                    m_DefaultReferenceLightListParameters))
            {
                return;
            }

            m_DefaultReferenceLightList.EnsureImportedBuffer();
            m_DefaultReferenceLightListParameters.desc.Count =
                Mathf.Max(s_EmptyReferenceLightListStorage.Length, 1);
            m_DefaultReferenceLightListParameters.desc.Stride =
                ReferencedPathTracingLightListStorageBlock.Stride;
            m_DefaultReferenceLightListParameters.desc.Target =
                GraphicsBuffer.Target.Structured;
            m_DefaultReferenceLightListParameters.EnsureImportedBuffer();
            if (m_DefaultReferenceLightListInitialized)
                return;

            m_DefaultReferenceLightList.SetData(
                new ReferencedPathTracingLightRecord[1]);
            m_DefaultReferenceLightListParameters.SetData(
                s_EmptyReferenceLightListStorage);
            m_DefaultReferenceLightListInitialized = true;
        }

        private void BindReferenceLightList(CommandBuffer cmd)
        {
            if (m_ReferenceLightList?.innerHandle.IsValid() != true
                || m_ReferenceLightListParameters?.innerHandle.IsValid()
                    != true)
            {
                return;
            }

            cmd.SetGlobalBuffer(
                ReferenceLightListId,
                m_ReferenceLightList.innerHandle);
            cmd.SetGlobalBuffer(
                ReferenceLightListParametersId,
                m_ReferenceLightListParameters.innerHandle);
            cmd.SetRayTracingBufferParam(
                m_RayTracingShader,
                ReferenceLightListId,
                m_ReferenceLightList.innerHandle);
            cmd.SetRayTracingBufferParam(
                m_RayTracingShader,
                ReferenceLightListParametersId,
                m_ReferenceLightListParameters.innerHandle);
        }

        private void PrepareDirectionalDenoiserState()
        {
            m_HasFiniteDirectionalLight = false;
            var lightDatabase = VividLightRenderDatabase.instance;
            lightDatabase.CompleteSceneLightPrepare();
            var buildResult = ReferencedPathTracingLightListBuilder.Build(
                lightDatabase.sceneLightData);
            for (var lightIndex = 0;
                 lightIndex < buildResult.records.Length;
                 lightIndex++)
            {
                var light = buildResult.records[lightIndex];
                if (light.lightType
                        == (uint)ReferencedPathTracingLightType.Directional
                    && light.selectionWeight > 0.0f
                    && ReferencedPathTracingLightSignatureUtility
                        .HasFiniteMainLightSolidAngle(
                            light.angularDiameter))
                {
                    m_HasFiniteDirectionalLight = true;
                    return;
                }
            }
        }

    }
}
