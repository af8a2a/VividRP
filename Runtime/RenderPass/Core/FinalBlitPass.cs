using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class FinalBlitPass : UnsafePass
    {
        private const int AutoExposureHistogramBucketCount = 64;
        private const int AutoExposureHistogramThreadGroupSizeX = 8;
        private const int AutoExposureHistogramThreadGroupSizeY = 8;
        private const int AutoExposureVectorStride = sizeof(float) * 4;
        private const string ClearHistogramKernelName = "ClearHistogram";
        private const string BuildHistogramKernelName = "BuildHistogram";
        private const string ResolveExposureKernelName = "ResolveExposure";

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
        private static readonly int AutoExposureParams0Id = Shader.PropertyToID("_AutoExposureParams0");
        private static readonly int AutoExposureParams1Id = Shader.PropertyToID("_AutoExposureParams1");
        private static readonly int AutoExposureParams2Id = Shader.PropertyToID("_AutoExposureParams2");
        private static readonly int AutoExposureParams3Id = Shader.PropertyToID("_AutoExposureParams3");
        private static readonly int AutoExposureScreenSizeId = Shader.PropertyToID("_AutoExposureScreenSize");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(Name = "ColorGradingTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture colorGradingLut = new();

        private Material m_Material;
        private ComputeShader m_AutoExposureCompute;
        private readonly AutoExposureHistorySystem m_AutoExposureHistorySystem = new();
        private ColorGradingSettingsData m_ColorGradingSettings;
        private AutoExposureSettingsData m_AutoExposureSettings;
        private FilmGrainSettingsData m_FilmGrainSettings;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private TextureUVOrigin m_CameraBackBufferTextureUVOrigin;
        private bool m_ShouldSetViewport;
        private bool m_PostProcessingAllowed;
        private bool m_EnableAutoExposure;
        private int m_AutoExposureWidth;
        private int m_AutoExposureHeight;
        private int m_ClearHistogramKernel = -1;
        private int m_BuildHistogramKernel = -1;
        private int m_ResolveExposureKernel = -1;
        private Rect m_Viewport;
        private int m_FrameCount;
        private GraphicsBuffer m_AutoExposureHistogramBuffer;
        private GraphicsBuffer m_DefaultAutoExposureBuffer;
        private AutoExposureHistoryState m_AutoExposureHistoryState;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
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
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            m_AutoExposureSettings = m_PostProcessingAllowed
                ? AutoExposureSettingsResolver.Resolve(temporalData != null && temporalData.isFirstFrame)
                : AutoExposureSettingsData.CreateDefault();
            m_FilmGrainSettings = m_PostProcessingAllowed
                ? FilmGrainSettingsResolver.Resolve()
                : FilmGrainSettingsData.CreateDefault();

            m_FrameCount = Time.frameCount;
            m_AutoExposureWidth = ResolveAutoExposureDimension(m_Viewport.width, cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_AutoExposureHeight = ResolveAutoExposureDimension(m_Viewport.height, cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_AutoExposureHistoryState = null;
            m_AutoExposureHistorySystem.PurgeDestroyedCameras();

            m_EnableAutoExposure = m_PostProcessingAllowed
                && m_AutoExposureSettings.enabled
                && camera != null
                && m_AutoExposureCompute != null
                && m_ClearHistogramKernel >= 0
                && m_BuildHistogramKernel >= 0
                && m_ResolveExposureKernel >= 0;

            if (m_EnableAutoExposure)
            {
                m_AutoExposureHistoryState = m_AutoExposureHistorySystem.GetOrCreateBase(camera);
                EnsureAutoExposureHistoryState(m_AutoExposureHistoryState);
            }
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            m_Material = CoreUtils.CreateEngineMaterial(resources.FinalBlitShader);
            m_AutoExposureCompute = resources.AutoExposureCompute;
            if (m_AutoExposureCompute != null)
            {
                m_ClearHistogramKernel = m_AutoExposureCompute.FindKernel(ClearHistogramKernelName);
                m_BuildHistogramKernel = m_AutoExposureCompute.FindKernel(BuildHistogramKernelName);
                m_ResolveExposureKernel = m_AutoExposureCompute.FindKernel(ResolveExposureKernelName);
            }

            EnsureAutoExposureHistogramBuffer();
            EnsureDefaultAutoExposureBuffer();
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

            var autoExposureBuffer = m_DefaultAutoExposureBuffer;
            var autoExposureComputed = false;
            if (m_EnableAutoExposure
                && m_AutoExposureHistoryState != null
                && ExecuteAutoExposure(cmd))
            {
                autoExposureBuffer = m_AutoExposureHistoryState.currentExposureBuffer;
                autoExposureComputed = autoExposureBuffer != null;
            }

            if (autoExposureBuffer != null)
                m_Material.SetBuffer(AutoExposureBufferId, autoExposureBuffer);

            m_Material.SetVector(
                AutoExposureMaterialParamsId,
                new Vector4(autoExposureComputed ? 1f : 0f, 1f, 0f, 0f));

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

            if (autoExposureComputed && m_AutoExposureHistoryState != null)
            {
                m_AutoExposureHistoryState.SwapBuffers();
                m_AutoExposureHistoryState.hasValidHistory = true;
            }
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

            m_DefaultAutoExposureBuffer?.Dispose();
            m_DefaultAutoExposureBuffer = null;

            m_AutoExposureHistorySystem.Dispose();
            m_AutoExposureCompute = null;
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
            m_ResolveExposureKernel = -1;
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
            if (cmd == null
                || m_AutoExposureCompute == null
                || m_AutoExposureHistogramBuffer == null
                || m_DefaultAutoExposureBuffer == null
                || m_AutoExposureHistoryState?.currentExposureBuffer == null)
            {
                return false;
            }

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var previousExposureBuffer = m_AutoExposureHistoryState.hasValidHistory
                ? m_AutoExposureHistoryState.previousExposureBuffer
                : m_DefaultAutoExposureBuffer;

            if (previousExposureBuffer == null || source?.innerHandle.IsValid() != true)
                return false;

            BindAutoExposureParameters(cmd, m_ClearHistogramKernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ClearHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(m_AutoExposureCompute, m_ClearHistogramKernel, 1, 1, 1);

            BindAutoExposureParameters(cmd, m_BuildHistogramKernel);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureInputTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureMeterMaskId, meterMask);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_BuildHistogramKernel,
                CoreUtils.DivRoundUp(m_AutoExposureWidth, AutoExposureHistogramThreadGroupSizeX),
                CoreUtils.DivRoundUp(m_AutoExposureHeight, AutoExposureHistogramThreadGroupSizeY),
                1);

            BindAutoExposureParameters(cmd, m_ResolveExposureKernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ResolveExposureKernel, AutoExposureCurrentBufferId, m_AutoExposureHistoryState.currentExposureBuffer);
            cmd.DispatchCompute(m_AutoExposureCompute, m_ResolveExposureKernel, 1, 1, 1);
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
                    m_AutoExposureSettings.exposureCompensation,
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
                AutoExposureScreenSizeId,
                new Vector4(
                    m_AutoExposureWidth,
                    m_AutoExposureHeight,
                    1f / Mathf.Max(1, m_AutoExposureWidth),
                    1f / Mathf.Max(1, m_AutoExposureHeight)));
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

        private void EnsureDefaultAutoExposureBuffer()
        {
            if (m_DefaultAutoExposureBuffer != null
                && m_DefaultAutoExposureBuffer.count == 1
                && m_DefaultAutoExposureBuffer.stride == AutoExposureVectorStride)
            {
                return;
            }

            m_DefaultAutoExposureBuffer?.Dispose();
            m_DefaultAutoExposureBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                AutoExposureVectorStride);
            m_DefaultAutoExposureBuffer.name = "VividRP Auto Exposure Default";
            m_DefaultAutoExposureBuffer.SetData(new[] { new Vector4(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f) });
        }

        private static void EnsureAutoExposureHistoryState(AutoExposureHistoryState state)
        {
            if (state == null)
                return;

            EnsureAutoExposureBuffer(ref state.previousExposureBuffer, "VividRP Auto Exposure Previous");
            EnsureAutoExposureBuffer(ref state.currentExposureBuffer, "VividRP Auto Exposure Current");
        }

        private static void EnsureAutoExposureBuffer(ref GraphicsBuffer buffer, string name)
        {
            if (buffer != null && buffer.count == 1 && buffer.stride == AutoExposureVectorStride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, AutoExposureVectorStride);
            buffer.name = name;
            buffer.SetData(new[] { new Vector4(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f) });
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

        private sealed class AutoExposureHistoryState : CameraRelativeState
        {
            public GraphicsBuffer previousExposureBuffer;
            public GraphicsBuffer currentExposureBuffer;
            public bool hasValidHistory;

            public void SwapBuffers()
            {
                (previousExposureBuffer, currentExposureBuffer) = (currentExposureBuffer, previousExposureBuffer);
            }

            public override void Dispose()
            {
                previousExposureBuffer?.Dispose();
                previousExposureBuffer = null;

                currentExposureBuffer?.Dispose();
                currentExposureBuffer = null;
                hasValidHistory = false;
            }
        }

        private sealed class AutoExposureHistorySystem : CameraRelativeSystem<AutoExposureHistoryState>
        {
        }
    }
}
