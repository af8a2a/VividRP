using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// OpenPBR reference path-tracing prototype for StandardLit. It traces an iterative multi-bounce
    /// path and performs next-event estimation against the main directional light, the active
    /// HDRI environment, plus ReGIR point, spot, rectangle, and tube-light reservoirs at every hit.
    /// Environment NEE and BSDF misses are combined with power-heuristic MIS and delta-aware gates.
    /// The resolved sample stores scene-linear radiance and camera-background opacity. Denoising
    /// AOV alpha channels continue to use primary-hit validity.
    /// </summary>
    public sealed class ReferencedPathTracingPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const string MaterialShaderPassName = "ReferencedPathtracingDXR";
        internal const string RayGenerationShaderName = "RayGenReferencedPathtracing";

        private const string AccelerationStructureName = "_AccelerationStructure";
        private const int MaxBounceCount = 4;
        private const int RussianRouletteStartBounce = 3;

        private static readonly int WorldPositionTextureId = Shader.PropertyToID("_WorldPositionTexture");
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
        private static readonly int RayMinDistanceId = Shader.PropertyToID("_RayMinDistance");
        private static readonly int RayMaxDistanceId = Shader.PropertyToID("_RayMaxDistance");
        private static readonly int MainLightDirectionWSId = Shader.PropertyToID("_ReferencedMainLightDirectionWS");
        private static readonly int MainLightColorId = Shader.PropertyToID("_ReferencedMainLightColor");
        private static readonly int MaxBounceCountId = Shader.PropertyToID("_ReferencedMaxBounceCount");
        private static readonly int RussianRouletteStartBounceId =
            Shader.PropertyToID("_ReferencedRussianRouletteStartBounce");
        private static readonly int FrameIndexId = Shader.PropertyToID("_ReferencedFrameIndex");
        private static readonly int ReblurHitDistanceParametersId =
            Shader.PropertyToID("_ReferencedReblurHitDistanceParameters");
        private static readonly int ReblurCheckerboardModeId =
            Shader.PropertyToID("_ReferencedReblurCheckerboardMode");
        private static readonly int ReGIRLightsId = Shader.PropertyToID("_ReGIRLights");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");
        private static readonly int ReGIRLightPdfTextureId = Shader.PropertyToID("_ReGIRLightPdfTexture");
        private static readonly int ReGIREnabledId = Shader.PropertyToID("_ReferencedReGIREnabled");
        private static readonly int EnvironmentTextureId =
            Shader.PropertyToID("_ReferencedEnvironmentTexture");
        private static readonly int EnvironmentImportanceDistributionId =
            Shader.PropertyToID("_ReferencedEnvironmentImportanceDistribution");
        private static readonly int EnvironmentTintId =
            Shader.PropertyToID("_ReferencedEnvironmentTint");
        private static readonly int EnvironmentParametersId =
            Shader.PropertyToID("_ReferencedEnvironmentParameters");
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
        private static readonly int EnvironmentEstimatorModeId =
            Shader.PropertyToID("_ReferencedEnvironmentEstimatorMode");
        private static readonly int EnvironmentDebugModeId =
            Shader.PropertyToID("_ReferencedEnvironmentDebugMode");
        private static readonly int CameraClearColorId =
            Shader.PropertyToID("_ReferencedCameraClearColor");
        private static readonly int CameraSkyEnabledId =
            Shader.PropertyToID("_ReferencedCameraSkyEnabled");

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(Name = "ReGIRLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRLightBuffer;

        [RenderGraphResource(Name = "ReGIRParameters", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRParameterBuffer;

        [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRReservoirBuffer;

        [RenderGraphResource(Name = "ReGIRLightPdfTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ReGIRLightPdfTexture;

        [RenderGraphResource(Name = "PathTracingEnvironment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_EnvironmentTexture;

        [RenderGraphResource(
            Name = "EnvironmentImportanceDistribution",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_EnvironmentImportanceDistribution;

        [RenderGraphResource(
            Name = "WorldPosition",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_WorldPositionTexture;

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
        private bool m_SupportsRayTracing;
        private bool m_ShouldSkipExecution;
        private int m_Width = 1;
        private int m_Height = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;
        private Matrix4x4 m_WorldToView = Matrix4x4.identity;
        private float m_RayMinDistance = 0.01f;
        private float m_RayMaxDistance = 1000.0f;
        private Vector4 m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
        private Vector4 m_MainLightColor = Vector4.zero;
        private Vector4 m_ReblurHitDistanceParameters =
            ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
        private ReferencedPathTracingReblurCheckerboardMode m_ReblurCheckerboardMode =
            ReferencedPathTracingReblurCheckerboardMode.Off;
        private ReferencedPathTracingEnvironmentState m_EnvironmentState;
        private ReferencedPathTracingCameraBackgroundState m_CameraBackgroundState;
        private readonly RenderGraphBuffer m_DefaultEnvironmentImportanceDistribution;
        private bool m_DefaultEnvironmentImportanceDistributionInitialized;
        private int m_FrameIndex;

        public ReferencedPathTracingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_ReGIRLightBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRLights",
                VividReGIRLightData.Stride);
            m_ReGIRParameterBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRParameters",
                VividReGIRParameters.Stride);
            m_ReGIRReservoirBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRReservoirs",
                VividReGIRReservoir.Stride);
            m_ReGIRLightPdfTexture = RenderGraphTexture.CreateInput(
                "ReGIRLightPdfTexture",
                GraphicsFormat.R32_SFloat);
            m_EnvironmentTexture = CreateEnvironmentTexture("PathTracingEnvironment");
            m_DefaultEnvironmentImportanceDistribution =
                RenderGraphBuffer.CreateStructured(
                    "EnvironmentImportanceDistributionFallback",
                    ReferencedPathTracingEnvironmentImportanceLayout.ElementCount,
                    ReferencedPathTracingEnvironmentImportanceLayout.ElementStride);
            m_EnvironmentImportanceDistribution =
                m_DefaultEnvironmentImportanceDistribution;
            m_WorldPositionTexture = RenderGraphTexture.CreateOutput(
                "WorldPosition",
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
            m_RayTracingShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathtracingRayTracing;

            if (m_RayTracingShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the ray-tracing shader resource for {nameof(ReferencedPathTracingPass)}.");
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);

            ConfigureOutputs(m_Width, m_Height);
            PrepareEnvironmentImportanceDistributionFallback();
            PrepareMainDirectionalLight(frameData.GetOrCreate<VividLightData>());
            PrepareEnvironment(frameData.GetOrCreate<VividSkyData>(), cameraData);
            var reblurSettings = ReferencedPathTracingReblurSettingsResolver.Resolve();
            m_ReblurHitDistanceParameters = reblurSettings.hitDistanceParameters;
            m_ReblurCheckerboardMode = reblurSettings.enabled
                ? reblurSettings.checkerboardMode
                : ReferencedPathTracingReblurCheckerboardMode.Off;
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;

            var camera = cameraData?.camera;
            m_ShouldSkipExecution = camera == null || camera.orthographic;
            if (m_ShouldSkipExecution)
            {
                m_CameraPositionWS = Vector4.zero;
                m_PixelCoordToViewDirWS = Matrix4x4.identity;
                m_WorldToView = Matrix4x4.identity;
                m_RayMinDistance = 0.01f;
                m_RayMaxDistance = 1000.0f;
                return;
            }

            var cameraPosition = camera.transform.position;
            m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_WorldToView = cameraData.GetViewMatrix();
            m_RayMinDistance = Mathf.Max(camera.nearClipPlane, 0.0001f);
            m_RayMaxDistance = Mathf.Max(camera.farClipPlane, m_RayMinDistance + 0.0001f);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution
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
                cmd.SetRayTracingShaderPass(m_RayTracingShader, MaterialShaderPassName);
                cmd.SetRayTracingAccelerationStructure(
                    m_RayTracingShader,
                    AccelerationStructureName,
                    accelerationStructure);
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    WorldPositionTextureId,
                    m_WorldPositionTexture.innerHandle);
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
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMinDistanceId, m_RayMinDistance);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMaxDistanceId, m_RayMaxDistance);
                cmd.SetGlobalVector(MainLightDirectionWSId, m_MainLightDirectionWS);
                cmd.SetGlobalVector(MainLightColorId, m_MainLightColor);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    MainLightDirectionWSId,
                    m_MainLightDirectionWS);
                cmd.SetRayTracingVectorParam(m_RayTracingShader, MainLightColorId, m_MainLightColor);
                cmd.SetRayTracingIntParam(m_RayTracingShader, MaxBounceCountId, MaxBounceCount);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    RussianRouletteStartBounceId,
                    RussianRouletteStartBounce);
                cmd.SetRayTracingIntParam(m_RayTracingShader, FrameIndexId, m_FrameIndex);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    ReblurHitDistanceParametersId,
                    m_ReblurHitDistanceParameters);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    ReblurCheckerboardModeId,
                    (int)m_ReblurCheckerboardMode);
                BindEnvironment(cmd);
                var hasValidReGIRResources = HasValidReGIRResources();
                cmd.SetGlobalInt(ReGIREnabledId, hasValidReGIRResources ? 1 : 0);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    ReGIREnabledId,
                    hasValidReGIRResources ? 1 : 0);
                if (hasValidReGIRResources)
                    BindReGIRGlobals(cmd);
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
            m_RayTracingShader = null;
            m_SupportsRayTracing = false;
            m_ShouldSkipExecution = false;
            m_Width = 1;
            m_Height = 1;
            m_CameraPositionWS = Vector4.zero;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
            m_WorldToView = Matrix4x4.identity;
            m_RayMinDistance = 0.01f;
            m_RayMaxDistance = 1000.0f;
            m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_MainLightColor = Vector4.zero;
            m_ReblurHitDistanceParameters =
                ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
            m_ReblurCheckerboardMode = ReferencedPathTracingReblurCheckerboardMode.Off;
            m_EnvironmentState = default;
            m_CameraBackgroundState = default;
            m_DefaultEnvironmentImportanceDistribution?.ClearImportedBuffer();
            m_DefaultEnvironmentImportanceDistributionInitialized = false;
            m_FrameIndex = 0;
        }

        private void ConfigureOutputs(int width, int height)
        {
            ConfigureOutput(
                m_WorldPositionTexture,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "WorldPosition");
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
            ConfigureOutput(
                m_DiffuseRayDirectionHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "DiffuseRayDirectionHitDistance");
            ConfigureOutput(
                m_SpecularRayDirectionHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "SpecularRayDirectionHitDistance");
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
            return IsValid(m_WorldPositionTexture)
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

        private void PrepareEnvironment(VividSkyData skyData, VividCameraData cameraData)
        {
            m_EnvironmentTexture.ClearImportedHandle();
            m_EnvironmentState = ReferencedPathTracingEnvironmentState.Resolve(skyData);
            m_CameraBackgroundState =
                ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

            // Reference Path Tracing V1 only accepts HDRI Sky. Passing null deliberately imports
            // SkyManager's black cubemap for disabled, missing, or unsupported sky types.
            SkyManager.ImportSpecularCubemap(
                m_EnvironmentTexture,
                m_EnvironmentState.hasHdri ? skyData : null);
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
            var lightingEnabled =
                hasEnvironmentBinding && m_EnvironmentState.lightingEnabled ? 1 : 0;
            var cameraVisible =
                hasEnvironmentBinding && m_EnvironmentState.cameraVisible ? 1 : 0;
            var importanceSamplingEnabled =
                hasEnvironmentBinding && m_EnvironmentState.importanceSamplingEnabled ? 1 : 0;
            var neeEnabled =
                hasEnvironmentBinding && m_EnvironmentState.neeEnabled ? 1 : 0;
            var samplingMode = (int)m_EnvironmentState.samplingMode;
            var estimatorMode = (int)m_EnvironmentState.estimatorMode;
            var debugMode = (int)m_EnvironmentState.debugMode;
            var clearColor = m_CameraBackgroundState.clearColor;
            var cameraClearColor = new Vector4(
                clearColor.r,
                clearColor.g,
                clearColor.b,
                clearColor.a);
            var cameraSkyEnabled = m_CameraBackgroundState.skyRequested ? 1 : 0;

            cmd.SetGlobalVector(EnvironmentTintId, environmentTint);
            cmd.SetGlobalVector(EnvironmentParametersId, environmentParameters);
            cmd.SetGlobalInt(EnvironmentLightingEnabledId, lightingEnabled);
            cmd.SetGlobalInt(EnvironmentCameraVisibleId, cameraVisible);
            cmd.SetGlobalInt(
                EnvironmentImportanceSamplingEnabledId,
                importanceSamplingEnabled);
            cmd.SetGlobalInt(EnvironmentNeeEnabledId, neeEnabled);
            cmd.SetGlobalInt(EnvironmentSamplingModeId, samplingMode);
            cmd.SetGlobalInt(EnvironmentEstimatorModeId, estimatorMode);
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
                EnvironmentEstimatorModeId,
                estimatorMode);
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

        private void PrepareMainDirectionalLight(VividLightData lightData)
        {
            m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_MainLightColor = Vector4.zero;

            if (lightData == null)
                return;

            lightData.CompleteLightGridPrepare();
            if (!lightData.hasMainDirectionalLight)
                return;

            var mainLight = lightData.mainDirectionalLight;
            var directionWS = mainLight.directionWS;
            if (directionWS.sqrMagnitude <= 1e-8f)
                return;

            directionWS.Normalize();
            m_MainLightDirectionWS = new Vector4(directionWS.x, directionWS.y, directionWS.z, 0.0f);
            // DirectionalLightData.color is RGB illuminance in lux. Preserve that physical scale:
            // OpenPBR eval already returns BSDF * NdotL, so raygen can multiply it directly by
            // illuminance to obtain outgoing scene-linear radiance without another cosine or PI.
            m_MainLightColor = new Vector4(
                Mathf.Max(mainLight.color.x, 0.0f),
                Mathf.Max(mainLight.color.y, 0.0f),
                Mathf.Max(mainLight.color.z, 0.0f),
                1.0f);
        }

        private bool HasValidReGIRResources()
        {
            return m_ReGIRLightBuffer?.innerHandle.IsValid() == true
                && m_ReGIRParameterBuffer?.innerHandle.IsValid() == true
                && m_ReGIRReservoirBuffer?.innerHandle.IsValid() == true
                && m_ReGIRLightPdfTexture?.innerHandle.IsValid() == true;
        }

        private void BindReGIRGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalBuffer(ReGIRLightsId, m_ReGIRLightBuffer.innerHandle);
            cmd.SetGlobalBuffer(ReGIRParametersId, m_ReGIRParameterBuffer.innerHandle);
            cmd.SetGlobalBuffer(ReGIRReservoirsId, m_ReGIRReservoirBuffer.innerHandle);
            cmd.SetGlobalTexture(ReGIRLightPdfTextureId, m_ReGIRLightPdfTexture.innerHandle);
        }
    }
}
