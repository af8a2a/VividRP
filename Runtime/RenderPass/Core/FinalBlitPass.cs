using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class FinalBlitPass : UnsafePass
    {
        private const int AutoExposureHistogramBucketCount = 64;
        private const int AutoExposureHistogramThreadGroupSizeX = 8;
        private const int AutoExposureHistogramThreadGroupSizeY = 8;
        private const int HdrpAutoExposurePrePassSize = 1024;
        private const int HdrpAutoExposureReductionSize = 32;
        private const int HdrpAutoExposureThreadGroupSize = 8;
        private const string ClearHistogramKernelName = "ClearHistogram";
        private const string BuildHistogramKernelName = "BuildHistogram";
        private const string ResolveExposureKernelName = "ResolveExposure";
        private const string HdrpFixedExposureKernelName = "KFixedExposure";
        private const string HdrpManualCameraExposureKernelName = "KManualCameraExposure";
        private const string HdrpPrePassKernelName = "KPrePass";
        private const string HdrpReductionKernelName = "KReduction";
        private const string HdrpResetKernelName = "KReset";

        private static readonly int ColorGradingLutId = Shader.PropertyToID("_VividColorGradingLut");
        private static readonly int ColorGradingParamsId = Shader.PropertyToID("_VividColorGradingParams");
        private static readonly int AutoExposureBufferId = Shader.PropertyToID("_VividAutoExposureBuffer");
        private static readonly int AutoExposureMaterialParamsId = Shader.PropertyToID("_VividAutoExposureParams");
        private static readonly int FilmGrainTextureId = Shader.PropertyToID("_VividFilmGrainTexture");
        private static readonly int FilmGrainParamsId = Shader.PropertyToID("_VividFilmGrainParams");
        private static readonly int FilmGrainTexParamsId = Shader.PropertyToID("_VividFilmGrainTexParams");
        private static readonly int AutoExposureInputTextureId = Shader.PropertyToID("_InputColor");
        private static readonly int AutoExposureHistogramBufferId = Shader.PropertyToID("_HistogramBuffer");
        private static readonly int AutoExposurePreviousBufferId = Shader.PropertyToID("_PreviousExposureBuffer");
        private static readonly int AutoExposureCurrentBufferId = Shader.PropertyToID("_CurrentExposureBuffer");
        private static readonly int AutoExposureMeterMaskId = Shader.PropertyToID("_AutoExposureMeterMask");
        private static readonly int AutoExposureCompensationCurveId = Shader.PropertyToID("_AutoExposureCompensationCurve");
        private static readonly int AutoExposureParams0Id = Shader.PropertyToID("_AutoExposureParams0");
        private static readonly int AutoExposureParams1Id = Shader.PropertyToID("_AutoExposureParams1");
        private static readonly int AutoExposureParams2Id = Shader.PropertyToID("_AutoExposureParams2");
        private static readonly int AutoExposureParams3Id = Shader.PropertyToID("_AutoExposureParams3");
        private static readonly int AutoExposureCurveParamsId = Shader.PropertyToID("_AutoExposureCurveParams");
        private static readonly int AutoExposureScreenSizeId = Shader.PropertyToID("_AutoExposureScreenSize");
        private static readonly int HdrpSourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int HdrpReductionInputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int HdrpPreviousExposureTextureId = Shader.PropertyToID("_PreviousExposureTexture");
        private static readonly int HdrpOutputTextureId = Shader.PropertyToID("_OutputTexture");
        private static readonly int HdrpExposureWeightMaskId = Shader.PropertyToID("_ExposureWeightMask");
        private static readonly int HdrpExposureCurveTextureId = Shader.PropertyToID("_ExposureCurveTexture");
        private static readonly int HdrpExposureParamsId = Shader.PropertyToID("_ExposureParams");
        private static readonly int HdrpExposureParams2Id = Shader.PropertyToID("_ExposureParams2");
        private static readonly int HdrpProceduralMaskParamsId = Shader.PropertyToID("_ProceduralMaskParams");
        private static readonly int HdrpProceduralMaskParams2Id = Shader.PropertyToID("_ProceduralMaskParams2");
        private static readonly int HdrpHistogramExposureParamsId = Shader.PropertyToID("_HistogramExposureParams");
        private static readonly int HdrpAdaptationParamsId = Shader.PropertyToID("_AdaptationParams");
        private static readonly int HdrpVariantsId = Shader.PropertyToID("_Variants");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(Name = "ColorGradingTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture colorGradingLut = new();

        private Material m_Material;
        private ComputeShader m_AutoExposureCompute;
        private AutoExposureImplementationPath m_AutoExposureImplementation;
        private ColorGradingSettingsData m_ColorGradingSettings;
        private AutoExposureSettingsData m_AutoExposureSettings;
        private FilmGrainSettingsData m_FilmGrainSettings;
        private VividExposureData m_ExposureData;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private TextureUVOrigin m_CameraBackBufferTextureUVOrigin;
        private bool m_ShouldSetViewport;
        private bool m_PostProcessingAllowed;
        private bool m_EnableExposure;
        private bool m_EnableAutoExposure;
        private int m_AutoExposureWidth;
        private int m_AutoExposureHeight;
        private int m_ClearHistogramKernel = -1;
        private int m_BuildHistogramKernel = -1;
        private int m_ResolveExposureKernel = -1;
        private int m_HdrpFixedExposureKernel = -1;
        private int m_HdrpManualCameraExposureKernel = -1;
        private int m_HdrpPrePassKernel = -1;
        private int m_HdrpReductionKernel = -1;
        private int m_HdrpResetKernel = -1;
        private Rect m_Viewport;
        private int m_FrameCount;
        private Camera m_Camera;
        private GraphicsBuffer m_AutoExposureHistogramBuffer;
        private RenderTexture m_HdrpPrePassTexture;
        private RenderTexture m_HdrpReductionTexture;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            m_Camera = camera;
            var hasTargetTexture = camera != null && camera.targetTexture != null;
            var cameraType = camera != null ? camera.cameraType : CameraType.Game;

            m_CameraBackBufferTarget = hasTargetTexture
                ? new RenderTargetIdentifier(camera.targetTexture)
                : BuiltinRenderTextureType.CameraTarget;
            m_CameraBackBufferTextureUVOrigin = GetCameraBackBufferTextureUVOrigin(cameraType, hasTargetTexture);
            m_ShouldSetViewport = ShouldSetViewport(cameraType);

            m_Viewport = GetViewport(cameraData);
            m_PostProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_ColorGradingSettings = m_PostProcessingAllowed
                ? ColorGradingSettingsResolver.Resolve()
                : ColorGradingSettingsData.CreateDefault();
            m_FilmGrainSettings = m_PostProcessingAllowed
                ? FilmGrainSettingsResolver.Resolve()
                : FilmGrainSettingsData.CreateDefault();
            m_ExposureData = frameData.Get<VividExposureData>();
            m_AutoExposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();

            m_FrameCount = Time.frameCount;
            m_AutoExposureWidth = ResolveAutoExposureDimension(m_Viewport.width, cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_AutoExposureHeight = ResolveAutoExposureDimension(m_Viewport.height, cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            RefreshAutoExposureImplementation();

            m_EnableAutoExposure = m_PostProcessingAllowed
                && m_ExposureData != null
                && m_ExposureData.autoExposureEnabled
                && m_AutoExposureCompute != null
                && SupportsSelectedAutoExposureImplementation();
            m_EnableExposure = m_PostProcessingAllowed
                && m_ExposureData != null
                && m_ExposureData.exposureEnabled;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            m_Material = CoreUtils.CreateEngineMaterial(resources.FinalBlitShader);
            RefreshAutoExposureImplementation();

            EnsureAutoExposureHistogramBuffer();
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_Material == null)
                return;

            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            RTHandle sourceHandle = source.innerHandle;
            if (sourceHandle == null)
                return;

            var scale = Vector2.one;

            if (sourceHandle != null && sourceHandle.useScaling)
            {
                scale.x = sourceHandle.rtHandleProperties.rtHandleScale.x;
                scale.y = sourceHandle.rtHandleProperties.rtHandleScale.y;
            }

            var defaultExposureBuffer = m_ExposureData?.defaultExposureBuffer;
            var autoExposureBuffer = defaultExposureBuffer;
            var exposureUpdated = false;

            if (m_EnableExposure)
            {
                autoExposureBuffer = m_AutoExposureSettings.mode == AutoExposureMode.Manual
                    ? m_ExposureData.currentExposureBuffer ?? m_ExposureData.previousExposureBuffer ?? defaultExposureBuffer
                    : m_ExposureData.hasValidHistory
                        ? m_ExposureData.previousExposureBuffer ?? defaultExposureBuffer
                        : defaultExposureBuffer;
            }

            if (m_EnableAutoExposure && ExecuteAutoExposure(cmd))
            {
                autoExposureBuffer = m_ExposureData.currentExposureBuffer;
                exposureUpdated = autoExposureBuffer != null;
            }
            else if (m_EnableExposure && m_AutoExposureSettings.mode == AutoExposureMode.Manual)
            {
                if (m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP)
                    ExecuteHdrpManualExposure(cmd);

                autoExposureBuffer = m_ExposureData.currentExposureBuffer ?? autoExposureBuffer;
                exposureUpdated = autoExposureBuffer != null;
            }

            if (autoExposureBuffer != null)
                m_Material.SetBuffer(AutoExposureBufferId, autoExposureBuffer);

            m_Material.SetVector(
                AutoExposureMaterialParamsId,
                new Vector4(m_EnableExposure ? 1f : 0f, 0f, 0f, 0f));

            var useColorGradingLut = m_PostProcessingAllowed
                && m_ColorGradingSettings.RequiresLut
                && colorGradingLut != null
                && colorGradingLut.innerHandle.IsValid();

            m_Material.SetVector(
                ColorGradingParamsId,
                new Vector4(
                    1f / ColorGradingLutBuilder.LutSize,
                    ColorGradingLutBuilder.LutSize - 1f,
                    useColorGradingLut ? 1f : 0f,
                    m_ColorGradingSettings.postExposureLinear));

            if (useColorGradingLut)
                cmd.SetGlobalTexture(ColorGradingLutId, colorGradingLut.innerHandle);

            // Film Grain
            if (m_FilmGrainSettings.enabled && m_FilmGrainSettings.texture != null)
            {
                m_Material.SetTexture(FilmGrainTextureId, m_FilmGrainSettings.texture);
                m_Material.SetVector(FilmGrainParamsId, new Vector4(
                    m_FilmGrainSettings.intensity,
                    m_FilmGrainSettings.response,
                    0f, 0f));

                var texWidth = (float)m_FilmGrainSettings.texture.width;
                var texHeight = (float)m_FilmGrainSettings.texture.height;
                var screenWidth = m_Viewport.width > 0f ? m_Viewport.width : Screen.width;
                var screenHeight = m_Viewport.height > 0f ? m_Viewport.height : Screen.height;

                // Per-frame random offset to avoid static tiling
                var offsetX = (HashFrame(m_FrameCount, 0) % 1024) / 1024f;
                var offsetY = (HashFrame(m_FrameCount, 1) % 1024) / 1024f;

                m_Material.SetVector(FilmGrainTexParamsId, new Vector4(
                    screenWidth / texWidth,
                    screenHeight / texHeight,
                    offsetX,
                    offsetY));

                CoreUtils.SetKeyword(m_Material, "_FILM_GRAIN", true);
            }
            else
            {
                CoreUtils.SetKeyword(m_Material, "_FILM_GRAIN", false);
            }

            var sourceTextureUVOrigin = context.GetTextureUVOrigin(source.innerHandle);
            var scaleBias = GetFinalBlitScaleBias(scale, sourceTextureUVOrigin, m_CameraBackBufferTextureUVOrigin);

            cmd.SetRenderTarget(m_CameraBackBufferTarget);
            if (m_ShouldSetViewport)
                cmd.SetViewport(m_Viewport);

            Blitter.BlitTexture(unsafeCmd, sourceHandle, scaleBias, m_Material, 0);

#if UNITY_EDITOR
            AutoExposureStatsReadbackBridge.Request(
                unsafeCmd,
                m_Camera,
                m_AutoExposureSettings,
                m_EnableExposure,
                m_EnableAutoExposure,
                m_ExposureData != null && m_ExposureData.hasValidHistory,
                m_EnableExposure ? autoExposureBuffer : null,
                m_EnableAutoExposure && m_AutoExposureImplementation == AutoExposureImplementationPath.Unreal
                    ? m_AutoExposureHistogramBuffer
                    : null);
#endif

            if (exposureUpdated)
                AutoExposureRuntimeManager.CommitFrame(m_Camera);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_AutoExposureHistogramBuffer?.Dispose();
            m_AutoExposureHistogramBuffer = null;
            ReleaseHdrpScratchTexture(ref m_HdrpPrePassTexture);
            ReleaseHdrpScratchTexture(ref m_HdrpReductionTexture);
            m_AutoExposureCompute = null;
            m_AutoExposureImplementation = AutoExposureImplementationPath.Unreal;
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
            m_ResolveExposureKernel = -1;
            m_HdrpFixedExposureKernel = -1;
            m_HdrpManualCameraExposureKernel = -1;
            m_HdrpPrePassKernel = -1;
            m_HdrpReductionKernel = -1;
            m_HdrpResetKernel = -1;
        }

        private static long HashFrame(int frame, int state)
        {
            long hash = frame * 747796405 + 2891336453 + state * 197;
            hash = ((hash >> 16) ^ hash) * 45679;
            hash = ((hash >> 16) ^ hash) * 45679;
            hash = (hash >> 16) ^ hash;
            return hash & 0x7FFFFFFF;
        }

        private static TextureUVOrigin GetCameraBackBufferTextureUVOrigin(CameraType cameraType, bool hasTargetTexture)
        {
            var useActualBackbufferOrientation = cameraType != CameraType.SceneView
                && cameraType != CameraType.Preview
                && !hasTargetTexture;

            if (!useActualBackbufferOrientation)
                return TextureUVOrigin.BottomLeft;

            return SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;
        }

        private static bool ShouldSetViewport(CameraType cameraType)
        {
            return cameraType != CameraType.SceneView;
        }

        private static Rect GetViewport(VividCameraData cameraData)
        {
            if (cameraData.pixelRect.width > 0f && cameraData.pixelRect.height > 0f)
                return cameraData.pixelRect;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0 || height <= 0)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            return new Rect(0f, 0f, width, height);
        }

        private bool ExecuteAutoExposure(UnsafeCommandBuffer cmd)
        {
            return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? ExecuteHdrpAutoExposure(cmd)
                : ExecuteUnrealAutoExposure(cmd);
        }

        private bool ExecuteUnrealAutoExposure(UnsafeCommandBuffer cmd)
        {
            if (cmd == null
                || m_AutoExposureCompute == null
                || m_AutoExposureHistogramBuffer == null
                || m_ExposureData?.defaultExposureBuffer == null
                || m_ExposureData.currentExposureBuffer == null)
            {
                return false;
            }

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var previousExposureBuffer = m_ExposureData.hasValidHistory
                ? m_ExposureData.previousExposureBuffer
                : m_ExposureData.defaultExposureBuffer;

            if (previousExposureBuffer == null || source?.innerHandle.IsValid() != true)
                return false;

            BindAutoExposureParameters(cmd, m_ClearHistogramKernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ClearHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(m_AutoExposureCompute, m_ClearHistogramKernel, 1, 1, 1);

            BindAutoExposureParameters(cmd, m_BuildHistogramKernel);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureInputTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureMeterMaskId, meterMask);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_BuildHistogramKernel,
                CoreUtils.DivRoundUp(m_AutoExposureWidth, AutoExposureHistogramThreadGroupSizeX),
                CoreUtils.DivRoundUp(m_AutoExposureHeight, AutoExposureHistogramThreadGroupSizeY),
                1);

            BindAutoExposureParameters(cmd, m_ResolveExposureKernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.DispatchCompute(m_AutoExposureCompute, m_ResolveExposureKernel, 1, 1, 1);
            return true;
        }

        private bool ExecuteHdrpAutoExposure(UnsafeCommandBuffer cmd)
        {
            if (cmd == null
                || m_AutoExposureCompute == null
                || m_ExposureData?.currentExposureBuffer == null
                || m_ExposureData.previousExposureTexture == null
                || m_ExposureData.currentExposureTexture == null
                || source?.innerHandle.IsValid() != true)
            {
                return false;
            }

            EnsureHdrpScratchTextures();
            if (m_HdrpPrePassTexture == null || m_HdrpReductionTexture == null)
                return false;

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var curveTexture = m_AutoExposureSettings.exposureCompensationCurveTexture != null
                ? m_AutoExposureSettings.exposureCompensationCurveTexture
                : Texture2D.blackTexture;
            var previousExposureTexture = m_ExposureData.previousExposureTexture;
            var currentExposureTexture = m_ExposureData.currentExposureTexture;

            if (!m_ExposureData.hasValidHistory)
            {
                BindHdrpAutoExposureParameters(cmd, m_HdrpResetKernel, 0u);
                cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpResetKernel, HdrpOutputTextureId, previousExposureTexture);
                cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpResetKernel, 1, 1, 1);
            }

            BindHdrpAutoExposureParameters(cmd, m_HdrpPrePassKernel, 0u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpSourceTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpPreviousExposureTextureId, previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureWeightMaskId, meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureCurveTextureId, curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpPrePassKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpOutputTextureId, m_HdrpPrePassTexture);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_HdrpPrePassKernel,
                HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
                HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
                1);

            BindHdrpAutoExposureParameters(cmd, m_HdrpReductionKernel, 0u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId, m_HdrpPrePassTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId, previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId, meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId, curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId, m_HdrpReductionTexture);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_HdrpReductionKernel,
                HdrpAutoExposureReductionSize,
                HdrpAutoExposureReductionSize,
                1);

            BindHdrpAutoExposureParameters(cmd, m_HdrpReductionKernel, 1u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId, m_HdrpReductionTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId, previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId, meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId, curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId, currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpReductionKernel, 1, 1, 1);
            return true;
        }

        private bool ExecuteHdrpManualExposure(UnsafeCommandBuffer cmd)
        {
            if (cmd == null
                || m_AutoExposureCompute == null
                || m_ExposureData?.currentExposureBuffer == null
                || m_ExposureData.currentExposureTexture == null)
            {
                return false;
            }

            var kernel = m_AutoExposureSettings.applyPhysicalCameraExposure
                ? m_HdrpManualCameraExposureKernel
                : m_HdrpFixedExposureKernel;
            if (kernel < 0)
                return false;

            BindHdrpManualExposureParameters(cmd, kernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, kernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, kernel, HdrpOutputTextureId, m_ExposureData.currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, kernel, 1, 1, 1);
            return true;
        }

        private void BindAutoExposureParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            if (cmd == null || kernel < 0 || m_AutoExposureCompute == null)
                return;

            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams0Id,
                new Vector4(
                    m_AutoExposureSettings.exposureLowPercent,
                    m_AutoExposureSettings.exposureHighPercent,
                    m_AutoExposureSettings.minAverageLuminance,
                    m_AutoExposureSettings.maxAverageLuminance));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams1Id,
                new Vector4(
                    m_AutoExposureSettings.exposureSpeedUp,
                    m_AutoExposureSettings.exposureSpeedDown,
                    m_AutoExposureSettings.exposureCompensationSettings,
                    m_AutoExposureSettings.deltaTime));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams2Id,
                new Vector4(
                    m_AutoExposureSettings.histogramScale,
                    m_AutoExposureSettings.histogramBias,
                    m_AutoExposureSettings.luminanceMin,
                    m_AutoExposureSettings.forceTarget));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams3Id,
                new Vector4(
                    m_AutoExposureSettings.exponentialUpM,
                    m_AutoExposureSettings.exponentialDownM,
                    m_AutoExposureSettings.startDistance,
                    0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureCurveParamsId,
                new Vector4(
                    m_AutoExposureSettings.exposureCompensationCurveMinEV100,
                    m_AutoExposureSettings.exposureCompensationCurveInvRange,
                    m_AutoExposureSettings.exposureCompensationCurveEnabled ? 1f : 0f,
                    0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureScreenSizeId,
                new Vector4(
                    m_AutoExposureWidth,
                    m_AutoExposureHeight,
                    1f / Mathf.Max(1, m_AutoExposureWidth),
                    1f / Mathf.Max(1, m_AutoExposureHeight)));
            cmd.SetComputeTextureParam(
                m_AutoExposureCompute,
                kernel,
                AutoExposureCompensationCurveId,
                m_AutoExposureSettings.exposureCompensationCurveTexture != null
                    ? m_AutoExposureSettings.exposureCompensationCurveTexture
                    : Texture2D.blackTexture);
        }

        private void BindHdrpAutoExposureParameters(UnsafeCommandBuffer cmd, int kernel, uint evaluateMode)
        {
            if (cmd == null || kernel < 0 || m_AutoExposureCompute == null)
                return;

            var compensationStops = Mathf.Log(Mathf.Max(m_AutoExposureSettings.exposureCompensationSettings, 1e-6f), 2f);
            var minExposureEV100 = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings.minAverageLuminance);
            var maxExposureEV100 = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings.maxAverageLuminance);
            var meteringMode = ResolveHdrpMeteringMode();
            var variants = new Vector4(
                1f,
                meteringMode,
                m_AutoExposureSettings.adaptationMode == AutoExposureAdaptationMode.Progressive
                    && m_AutoExposureSettings.forceTarget <= 0.5f
                        ? 1f
                        : 0f,
                evaluateMode);

            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpExposureParamsId,
                new Vector4(
                    compensationStops,
                    minExposureEV100,
                    maxExposureEV100,
                    0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpExposureParams2Id,
                new Vector4(
                    0f,
                    0f,
                    1f,
                    18f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpProceduralMaskParamsId,
                Vector4.zero);
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpProceduralMaskParams2Id,
                Vector4.zero);
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramExposureParamsId,
                new Vector4(
                    m_AutoExposureSettings.exposureCompensationCurveMinEV100,
                    m_AutoExposureSettings.exposureCompensationCurveInvRange,
                    m_AutoExposureSettings.exposureCompensationCurveEnabled ? 1f : 0f,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpAdaptationParamsId,
                new Vector4(
                    m_AutoExposureSettings.exposureSpeedUp,
                    m_AutoExposureSettings.exposureSpeedDown,
                    0f,
                    0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpVariantsId,
                variants);
        }

        private void BindHdrpManualExposureParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            if (cmd == null || kernel < 0 || m_AutoExposureCompute == null)
                return;

            var compensationStops = Mathf.Log(Mathf.Max(m_AutoExposureSettings.exposureCompensationAll, 1e-6f), 2f);
            var camera = m_Camera;
            var aperture = camera != null ? Mathf.Max(camera.aperture, 1e-4f) : 1f;
            var shutterSpeed = camera != null ? Mathf.Max(camera.shutterSpeed, 1e-6f) : 1f;
            var iso = camera != null ? Mathf.Max((float)camera.iso, 1f) : 100f;

            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpExposureParamsId,
                new Vector4(
                    compensationStops,
                    m_AutoExposureSettings.applyPhysicalCameraExposure ? aperture : m_AutoExposureSettings.manualEV100,
                    m_AutoExposureSettings.applyPhysicalCameraExposure ? shutterSpeed : 0f,
                    m_AutoExposureSettings.applyPhysicalCameraExposure ? iso : 0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpExposureParams2Id,
                new Vector4(
                    0f,
                    0f,
                    1f,
                    18f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpProceduralMaskParamsId,
                Vector4.zero);
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpProceduralMaskParams2Id,
                Vector4.zero);
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramExposureParamsId,
                new Vector4(
                    m_AutoExposureSettings.exposureCompensationCurveMinEV100,
                    m_AutoExposureSettings.exposureCompensationCurveInvRange,
                    m_AutoExposureSettings.exposureCompensationCurveEnabled ? 1f : 0f,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpAdaptationParamsId,
                Vector4.zero);
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpVariantsId,
                new Vector4(1f, 0f, 0f, 0f));
        }

        private float ResolveHdrpMeteringMode()
        {
            switch (m_AutoExposureSettings.meteringMode)
            {
                case AutoExposureMeteringMode.Spot:
                    return 1f;
                case AutoExposureMeteringMode.CenterWeighted:
                    return 2f;
                case AutoExposureMeteringMode.MaskWeighted:
                    return m_AutoExposureSettings.meterMask != null ? 3f : 0f;
                default:
                    return 0f;
            }
        }

        private bool SupportsSelectedAutoExposureImplementation()
        {
            return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? m_HdrpPrePassKernel >= 0 && m_HdrpReductionKernel >= 0 && m_HdrpResetKernel >= 0
                : m_ClearHistogramKernel >= 0 && m_BuildHistogramKernel >= 0 && m_ResolveExposureKernel >= 0;
        }

        private void RefreshAutoExposureImplementation()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var implementation = m_ExposureData != null
                ? m_ExposureData.implementation
                : AutoExposureImplementationUtility.ResolveImplementation(VividRenderPipelineAsset.GetActiveAsset());
            var computeShader = AutoExposureImplementationUtility.ResolveComputeShader(resources, implementation);

            if (m_AutoExposureImplementation == implementation && m_AutoExposureCompute == computeShader)
                return;

            m_AutoExposureImplementation = implementation;
            m_AutoExposureCompute = computeShader;
            ResolveAutoExposureKernels();
        }

        private void ResolveAutoExposureKernels()
        {
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
            m_ResolveExposureKernel = -1;
            m_HdrpFixedExposureKernel = -1;
            m_HdrpManualCameraExposureKernel = -1;
            m_HdrpPrePassKernel = -1;
            m_HdrpReductionKernel = -1;
            m_HdrpResetKernel = -1;

            if (m_AutoExposureCompute == null)
                return;

            if (m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP)
            {
                if (!AutoExposureImplementationUtility.SupportsHdrpDispatch(m_AutoExposureCompute))
                {
                    Debug.LogWarning("[VividRP] HDRP auto exposure is selected, but the HDRP compute shader is missing the required kernels.");
                    return;
                }

                if (m_AutoExposureCompute.HasKernel(HdrpFixedExposureKernelName))
                    m_HdrpFixedExposureKernel = m_AutoExposureCompute.FindKernel(HdrpFixedExposureKernelName);

                if (m_AutoExposureCompute.HasKernel(HdrpManualCameraExposureKernelName))
                    m_HdrpManualCameraExposureKernel = m_AutoExposureCompute.FindKernel(HdrpManualCameraExposureKernelName);

                m_HdrpPrePassKernel = m_AutoExposureCompute.FindKernel(HdrpPrePassKernelName);
                m_HdrpReductionKernel = m_AutoExposureCompute.FindKernel(HdrpReductionKernelName);
                m_HdrpResetKernel = m_AutoExposureCompute.FindKernel(HdrpResetKernelName);
                return;
            }

            if (!AutoExposureImplementationUtility.SupportsUnrealDispatch(m_AutoExposureCompute))
            {
                Debug.LogWarning("[VividRP] Unreal auto exposure is selected, but the compute shader is missing the required histogram kernels.");
                return;
            }

            m_ClearHistogramKernel = m_AutoExposureCompute.FindKernel(ClearHistogramKernelName);
            m_BuildHistogramKernel = m_AutoExposureCompute.FindKernel(BuildHistogramKernelName);
            m_ResolveExposureKernel = m_AutoExposureCompute.FindKernel(ResolveExposureKernelName);
        }

        private void EnsureAutoExposureHistogramBuffer()
        {
            if (m_AutoExposureHistogramBuffer != null
                && m_AutoExposureHistogramBuffer.count == AutoExposureHistogramBucketCount
                && m_AutoExposureHistogramBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_AutoExposureHistogramBuffer?.Dispose();
            m_AutoExposureHistogramBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                AutoExposureHistogramBucketCount,
                sizeof(uint));
            m_AutoExposureHistogramBuffer.name = "VividRP Auto Exposure Histogram";
        }

        private void EnsureHdrpScratchTextures()
        {
            EnsureHdrpScratchTexture(
                ref m_HdrpPrePassTexture,
                HdrpAutoExposurePrePassSize,
                HdrpAutoExposurePrePassSize,
                "VividRP HDRP Auto Exposure PrePass");
            EnsureHdrpScratchTexture(
                ref m_HdrpReductionTexture,
                HdrpAutoExposureReductionSize,
                HdrpAutoExposureReductionSize,
                "VividRP HDRP Auto Exposure Reduction");
        }

        private static void EnsureHdrpScratchTexture(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null
                && texture.IsCreated()
                && texture.width == width
                && texture.height == height
                && texture.enableRandomWrite)
            {
                return;
            }

            ReleaseHdrpScratchTexture(ref texture);

            texture = new RenderTexture(width, height, 0)
            {
                name = name,
                graphicsFormat = GraphicsFormat.R32G32_SFloat,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
        }

        private static void ReleaseHdrpScratchTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }

        private static int ResolveAutoExposureDimension(float viewportDimension, int preferredDimension, int fallbackDimension, int screenDimension)
        {
            var roundedViewport = Mathf.RoundToInt(viewportDimension);
            if (roundedViewport > 0)
                return roundedViewport;

            if (preferredDimension > 0)
                return preferredDimension;

            if (fallbackDimension > 0)
                return fallbackDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static Vector4 GetFinalBlitScaleBias(
            Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            var yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
