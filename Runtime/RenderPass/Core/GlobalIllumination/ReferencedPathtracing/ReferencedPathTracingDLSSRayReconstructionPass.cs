using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Denoises the reference path tracer with DLSS Ray Reconstruction at native resolution.
    /// Auto exposure is supplied through the per-camera FrameContext rather than a RenderGraph
    /// resource so exposure can meter the denoised result without introducing a graph cycle.
    /// </summary>
    public sealed class ReferencedPathTracingDLSSRayReconstructionPass
        : UnsafePass, IRenderGraphSideEffectPass
    {
        internal const string ResolvePreExposureKernelName =
            "ResolveDLSSRayReconstructionPreExposure";

        private static readonly int SceneLinearColorId =
            Shader.PropertyToID("_ReferencedPathTracingDLSSRRSceneLinearColor");
        private static readonly int ResolvedColorId =
            Shader.PropertyToID("_ReferencedPathTracingDLSSRRResolvedColor");
        private static readonly int ScreenSizeId =
            Shader.PropertyToID("_ReferencedPathTracingDLSSRRScreenSize");

        [RenderGraphResource(Name = "PathTracingRadiance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Radiance;

        [RenderGraphResource(Name = "DlssDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Depth;

        [RenderGraphResource(Name = "DlssMotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectors;

        [RenderGraphResource(Name = "DlssNormalRoughness", Access = AccessFlags.Read)]
        private RenderGraphTexture m_NormalRoughness;

        [RenderGraphResource(Name = "DiffuseAlbedo", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DiffuseAlbedo;

        [RenderGraphResource(Name = "SpecularAlbedo", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SpecularAlbedo;

        [RenderGraphResource(Name = "PathTracingEmission", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Emissive;

        [RenderGraphResource(Name = "DiffuseRayDirectionHitDistance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DiffuseRayDirectionHitDistance;

        [RenderGraphResource(Name = "SpecularRayDirectionHitDistance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SpecularRayDirectionHitDistance;

        [RenderGraphResource(
            Name = "DLSSRRResolvedColor",
            Access = AccessFlags.WriteAll,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        [PassBypass(nameof(m_Radiance))]
        private RenderGraphTexture m_ResolvedColor;

        [RenderGraphResource(
            Name = "DLSSRRSceneLinearColor",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SceneLinearColor;

        private int m_Width = 1;
        private int m_Height = 1;

#if DLSS_PLUGIN_INTEGRATE
        private sealed class DLSSCameraState : CameraRelativeState
        {
            public DLSSRRDenoiser denoiser;
            public bool hasSignature;
            public int width;
            public int height;
            public ulong integratorSignature;
            public ulong frameSignature;

            public override void Dispose()
            {
                denoiser?.Dispose();
                denoiser = null;
                hasSignature = false;
                width = 0;
                height = 0;
                integratorSignature = 0;
                frameSignature = 0;
            }
        }

        private readonly CameraRelativeSystem<DLSSCameraState> m_CameraStates = new();
        private DLSSCameraState m_CurrentState;
        private Matrix4x4 m_WorldToView = Matrix4x4.identity;
        private Matrix4x4 m_ViewToClip = Matrix4x4.identity;
        private RenderTexture m_ExposureTexture;
        private GraphicsBuffer m_PreExposureBuffer;
        private ComputeShader m_ResolveCompute;
        private int m_ResolveKernel = -1;
        private bool m_ResetHistory = true;
#endif

        public ReferencedPathTracingDLSSRayReconstructionPass()
        {
            profilingSampler =
                new ProfilingSampler(nameof(ReferencedPathTracingDLSSRayReconstructionPass));

            m_Radiance = RenderGraphTexture.CreateInput(
                "PathTracingRadiance",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Depth = RenderGraphTexture.CreateInput("DlssDepth", GraphicsFormat.R32_SFloat);
            m_MotionVectors = RenderGraphTexture.CreateInput(
                "DlssMotionVectors",
                GraphicsFormat.R16G16_SFloat);
            m_NormalRoughness = RenderGraphTexture.CreateInput(
                "DlssNormalRoughness",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseAlbedo = RenderGraphTexture.CreateInput(
                "DiffuseAlbedo",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_SpecularAlbedo = RenderGraphTexture.CreateInput(
                "SpecularAlbedo",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_Emissive = RenderGraphTexture.CreateInput(
                "PathTracingEmission",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DiffuseRayDirectionHitDistance = RenderGraphTexture.CreateInput(
                "DiffuseRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularRayDirectionHitDistance = RenderGraphTexture.CreateInput(
                "SpecularRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ResolvedColor = RenderGraphTexture.CreateOutput(
                "DLSSRRResolvedColor",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SceneLinearColor = RenderGraphTexture.CreateOutput(
                "DLSSRRSceneLinearColor",
                GraphicsFormat.R16G16B16A16_SFloat);
            ConfigureOutputDescriptor();
            ConfigureSceneLinearDescriptor();
        }

        public override void Create()
        {
#if DLSS_PLUGIN_INTEGRATE
            m_ResolveCompute = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathTracingDLSSRayReconstructionResolveCompute;
            if (m_ResolveCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the compute shader resource for {nameof(ReferencedPathTracingDLSSRayReconstructionPass)}.");
                return;
            }

            m_ResolveKernel =
                m_ResolveCompute.FindKernel(ResolvePreExposureKernelName);
#endif
        }

        public override bool IsActive(ContextContainer frameData)
        {
#if DLSS_PLUGIN_INTEGRATE
            // Keep the pass active when RR is unavailable so its deterministic
            // fallback still converts raw path-traced radiance into VividRP's
            // pre-exposed scene-color domain.
            return m_ResolveCompute != null && m_ResolveKernel >= 0;
#else
            return false;
#endif
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);
            m_ResolvedColor.Resize(m_Width, m_Height);
            m_SceneLinearColor.Resize(m_Width, m_Height);
            ConfigureOutputDescriptor();
            ConfigureSceneLinearDescriptor();

#if DLSS_PLUGIN_INTEGRATE
            m_PreExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(
                frameData.Get<VividExposureData>());
            PrepareCameraState(frameData, cameraData);
            m_CameraStates.PurgeDestroyedCameras();
#endif
        }

        public override void Record(UnsafePassContext context)
        {
#if DLSS_PLUGIN_INTEGRATE
            if (m_ResolveCompute == null
                || m_ResolveKernel < 0
                || m_PreExposureBuffer == null
                || !HaveValidResources())
            {
                return;
            }

            var radiance = (RenderTexture)m_Radiance;
            var depth = (RenderTexture)m_Depth;
            var motionVectors = (RenderTexture)m_MotionVectors;
            var normalRoughness = (RenderTexture)m_NormalRoughness;
            var diffuseAlbedo = (RenderTexture)m_DiffuseAlbedo;
            var specularAlbedo = (RenderTexture)m_SpecularAlbedo;
            var emissive = (RenderTexture)m_Emissive;
            var diffuseRayDirectionHitDistance =
                (RenderTexture)m_DiffuseRayDirectionHitDistance;
            var specularRayDirectionHitDistance =
                (RenderTexture)m_SpecularRayDirectionHitDistance;
            var sceneLinearColor = (RenderTexture)m_SceneLinearColor;
            var resolvedColor = (RenderTexture)m_ResolvedColor;
            if (!HaveMatchingDimensions(
                    resolvedColor,
                    radiance,
                    depth,
                    motionVectors,
                    normalRoughness,
                    diffuseAlbedo,
                    specularAlbedo,
                    emissive,
                    diffuseRayDirectionHitDistance,
                    specularRayDirectionHitDistance,
                    sceneLinearColor))
            {
                return;
            }

            var cmd = context.GetNativeCommandBuffer();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (m_CurrentState?.denoiser != null)
                {
                    var settings = DLSSRRDenoiser.Settings.Default;
                    settings.quality = DLSSQuality.DLAA;
                    settings.resetHistory = m_ResetHistory;

                    // The reference path tracer and all RR radiance guides are
                    // absolute scene-linear HDR. NGX therefore sees unit
                    // pre-exposure and returns the same scene-linear domain.
                    settings.preExposure = 1.0f;
                    settings.exposureScale = 1.0f;
                    settings.frameTimeDeltaMs =
                        ResolveFrameTimeDeltaMilliseconds();
                    settings.autoExposure = false;
                    settings.isHDR = true;
                    settings.exposureTexture = m_ExposureTexture;

                    // The wrapper records a raw-radiance fallback into this
                    // texture while asynchronous feature creation is pending
                    // or after creation/evaluation failure.
                    m_CurrentState.denoiser.Execute(
                        cmd,
                        radiance,
                        sceneLinearColor,
                        depth,
                        motionVectors,
                        diffuseAlbedo,
                        specularAlbedo,
                        normalRoughness,
                        emissive,
                        diffuseRayDirectionHitDistance,
                        specularRayDirectionHitDistance,
                        Vector2.zero,
                        m_WorldToView,
                        m_ViewToClip,
                        settings);
                }
                else
                {
                    cmd.Blit(radiance, sceneLinearColor);
                }

                // DLSS-RR preserves the HDR exposure domain of its input.
                // VividRP's AutoExposure and FinalBlit consume pre-exposed
                // scene color, so restore that pipeline contract exactly once
                // after RR (or its fallback), never inside the NGX inputs.
                cmd.SetComputeTextureParam(
                    m_ResolveCompute,
                    m_ResolveKernel,
                    SceneLinearColorId,
                    sceneLinearColor);
                cmd.SetComputeTextureParam(
                    m_ResolveCompute,
                    m_ResolveKernel,
                    ResolvedColorId,
                    resolvedColor);
                cmd.SetComputeBufferParam(
                    m_ResolveCompute,
                    m_ResolveKernel,
                    VividAutoExposureSystem.PreExposureBufferId,
                    m_PreExposureBuffer);
                cmd.SetComputeVectorParam(
                    m_ResolveCompute,
                    ScreenSizeId,
                    new Vector4(
                        m_Width,
                        m_Height,
                        1.0f / m_Width,
                        1.0f / m_Height));
                cmd.DispatchCompute(
                    m_ResolveCompute,
                    m_ResolveKernel,
                    CoreUtils.DivRoundUp(m_Width, 8),
                    CoreUtils.DivRoundUp(m_Height, 8),
                    1);
            }
#endif
        }

        public override void Dispose()
        {
#if DLSS_PLUGIN_INTEGRATE
            m_CameraStates.Dispose();
            m_CurrentState = null;
            m_WorldToView = Matrix4x4.identity;
            m_ViewToClip = Matrix4x4.identity;
            m_ExposureTexture = null;
            m_PreExposureBuffer = null;
            m_ResolveCompute = null;
            m_ResolveKernel = -1;
            m_ResetHistory = true;
#endif
            m_Width = 1;
            m_Height = 1;
        }

#if DLSS_PLUGIN_INTEGRATE
        private void PrepareCameraState(
            ContextContainer frameData,
            VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
            {
                m_CurrentState = null;
                m_ExposureTexture = null;
                m_ResetHistory = true;
                return;
            }

            var state = m_CameraStates.GetOrCreateBase(camera);
            state.denoiser ??= new DLSSRRDenoiser();

            var pathTracingData =
                frameData.GetOrCreate<VividReferencedPathTracingData>();
            var integratorSignature = pathTracingData.isValid
                ? pathTracingData.integratorSignature
                : 0ul;
            var frameSignature = pathTracingData.isValid
                ? pathTracingData.frameSignature
                : 0ul;
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            var antialiasingData = frameData.Get<VividAntialiasingData>();
            var signatureMatches = state.hasSignature
                && state.width == m_Width
                && state.height == m_Height
                && state.integratorSignature == integratorSignature
                && state.frameSignature == frameSignature;

            m_ResetHistory = !signatureMatches
                || temporalData == null
                || temporalData.isFirstFrame
                || antialiasingData?.resetHistory == true;
            state.hasSignature = true;
            state.width = m_Width;
            state.height = m_Height;
            state.integratorSignature = integratorSignature;
            state.frameSignature = frameSignature;

            m_WorldToView = cameraData.GetViewMatrix();
            m_ViewToClip =
                cameraData.GetGPUProjectionMatrixNoJitter(renderIntoTexture: true);
            m_ExposureTexture =
                frameData.Get<VividExposureData>()?.dlssExposureTexture;

            if (!DLSSExtension.IsRayReconstructionSupported
                || !state.denoiser.Initialize(
                    m_Width,
                    m_Height,
                    m_Width,
                    m_Height,
                    DLSSQuality.DLAA,
                    isHDR: true,
                    autoExposure: false))
            {
                m_CurrentState = null;
                m_ExposureTexture = null;
                return;
            }

            m_CurrentState = state;
        }

        private bool HaveValidResources()
        {
            return m_Radiance?.innerHandle.IsValid() == true
                && m_Depth?.innerHandle.IsValid() == true
                && m_MotionVectors?.innerHandle.IsValid() == true
                && m_NormalRoughness?.innerHandle.IsValid() == true
                && m_DiffuseAlbedo?.innerHandle.IsValid() == true
                && m_SpecularAlbedo?.innerHandle.IsValid() == true
                && m_Emissive?.innerHandle.IsValid() == true
                && m_DiffuseRayDirectionHitDistance?.innerHandle.IsValid() == true
                && m_SpecularRayDirectionHitDistance?.innerHandle.IsValid() == true
                && m_SceneLinearColor?.innerHandle.IsValid() == true
                && m_ResolvedColor?.innerHandle.IsValid() == true;
        }

        private static bool HaveMatchingDimensions(
            RenderTexture reference,
            params RenderTexture[] textures)
        {
            if (reference == null)
                return false;

            for (var index = 0; index < textures.Length; index++)
            {
                var texture = textures[index];
                if (texture == null
                    || texture.width != reference.width
                    || texture.height != reference.height)
                {
                    return false;
                }
            }

            return true;
        }

        private static float ResolveFrameTimeDeltaMilliseconds()
        {
            var milliseconds = Time.unscaledDeltaTime * 1000.0f;
            return milliseconds > 0.0f && !float.IsNaN(milliseconds)
                ? milliseconds
                : 16.67f;
        }
#endif

        private void ConfigureOutputDescriptor()
        {
            var descriptor = m_ResolvedColor.desc;
            descriptor.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Point;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.ClearBuffer = false;
            descriptor.EnableRandomWrite = true;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            descriptor.Name = "DLSSRRResolvedColor";
        }

        private void ConfigureSceneLinearDescriptor()
        {
            var descriptor = m_SceneLinearColor.desc;
            descriptor.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Point;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.ClearBuffer = false;
            descriptor.EnableRandomWrite = true;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            descriptor.Name = "DLSSRRSceneLinearColor";
        }
    }
}
