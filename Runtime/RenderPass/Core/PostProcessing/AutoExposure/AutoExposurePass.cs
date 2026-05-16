using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass
{
    public sealed partial class AutoExposurePass : UnsafePass, IPostProcessSourceOverridePass
    {
        private const int UnrealAutoExposureHistogramBucketCount = 64;
        private const int HdrpAutoExposureHistogramBucketCount = 128;
        private const int AutoExposureHistogramThreadGroupSizeX = 8;
        private const int AutoExposureHistogramThreadGroupSizeY = 8;
        private const int HdrpAutoExposurePrePassSize = 1024;
        private const int HdrpAutoExposureReductionSize = 32;
        private const int HdrpHistogramThreadGroupSizeX = 16;
        private const int HdrpHistogramThreadGroupSizeY = 8;
        private const string ClearHistogramKernelName = "ClearHistogram";
        private const string BuildHistogramKernelName = "BuildHistogram";
        private const string ResolveExposureKernelName = "ResolveExposure";
        private const string HdrpFixedExposureKernelName = "KFixedExposure";
        private const string HdrpManualCameraExposureKernelName = "KManualCameraExposure";
        private const string HdrpHistogramClearKernelName = "KHistogramClear";
        private const string HdrpHistogramGenKernelName = "KHistogramGen";
        private const string HdrpHistogramReduceKernelName = "KHistogramReduce";
        private const string HdrpPrePassKernelName = "KPrePass";
        private const string HdrpReductionKernelName = "KReduction";
        private const string HdrpResetKernelName = "KReset";

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
        private static readonly int HdrpHistogramRangeParamsId = Shader.PropertyToID("_HistogramRangeParams");
        private static readonly int HdrpProceduralMaskParamsId = Shader.PropertyToID("_ProceduralMaskParams");
        private static readonly int HdrpProceduralMaskParams2Id = Shader.PropertyToID("_ProceduralMaskParams2");
        private static readonly int HdrpHistogramExposureParamsId = Shader.PropertyToID("_HistogramExposureParams");
        private static readonly int HdrpAdaptationParamsId = Shader.PropertyToID("_AdaptationParams");
        private static readonly int HdrpVariantsId = Shader.PropertyToID("_Variants");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        private AutoExposureImplementationPath m_AutoExposureImplementation;
        private AutoExposureSettingsData m_AutoExposureSettings;
        private VividExposureData m_ExposureData;
        private Camera m_Camera;
        private bool m_PostProcessingAllowed;
        private bool m_EnableExposure;
        private bool m_EnableAutoExposure;
        private int m_AutoExposureWidth;
        private int m_AutoExposureHeight;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public AutoExposurePass()
        {
            profilingSampler = new ProfilingSampler(nameof(AutoExposurePass));
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetSourceTexture()
        {
            return source;
        }

        internal void SetSourceTexture(RenderGraphTexture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));

            if (ReferenceEquals(source, sourceTexture))
                return;

            if (!m_HasSourceTextureOverride)
                m_OriginalSource = source;

            source = sourceTexture;
            m_HasSourceTextureOverride = true;
            m_IsPassResourceLayoutDirty = true;
        }

        internal void RestoreSourceTexture()
        {
            if (!m_HasSourceTextureOverride)
                return;

            if (!ReferenceEquals(source, m_OriginalSource) && m_OriginalSource != null)
            {
                source = m_OriginalSource;
                m_IsPassResourceLayoutDirty = true;
            }

            m_OriginalSource = null;
            m_HasSourceTextureOverride = false;
        }

        RenderGraphTexture IPostProcessSourceOverridePass.GetSourceTexture() => GetSourceTexture();

        void IPostProcessSourceOverridePass.SetSourceTexture(RenderGraphTexture sourceTexture) => SetSourceTexture(sourceTexture);

        void IPostProcessSourceOverridePass.RestoreSourceTexture() => RestoreSourceTexture();

        public override void Create()
        {
            RefreshAutoExposureImplementation();
            EnsureAutoExposureHistogramBuffer();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Camera = cameraData?.camera;
            m_PostProcessingAllowed = m_Camera != null && CoreUtils.ArePostProcessesEnabled(m_Camera);
            m_ExposureData = frameData.Get<VividExposureData>();
            m_AutoExposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();

            var viewport = ResolveViewport(cameraData);
            m_AutoExposureWidth = ResolveAutoExposureDimension(
                viewport.width,
                cameraData != null ? cameraData.actualWidth : 0,
                cameraData != null ? cameraData.pixelWidth : 0,
                Screen.width);
            m_AutoExposureHeight = ResolveAutoExposureDimension(
                viewport.height,
                cameraData != null ? cameraData.actualHeight : 0,
                cameraData != null ? cameraData.pixelHeight : 0,
                Screen.height);

            RefreshAutoExposureImplementation();
            EnsureAutoExposureHistogramBuffer();

            m_EnableAutoExposure = m_PostProcessingAllowed
                && m_ExposureData != null
                && m_ExposureData.autoExposureEnabled
                && SupportsActiveAutoExposureExecutionPath();
            m_EnableExposure = m_PostProcessingAllowed
                && m_ExposureData != null
                && m_ExposureData.exposureEnabled;
        }


        public override void Record(UnsafePassContext context)
        {
            var cmd =CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var exposureBuffer = m_ExposureData?.frameExposureBuffer;
            var exposureUpdated = false;

            if (m_EnableAutoExposure)
            {
                if (ExecuteAutoExposure(cmd))
                {
                    exposureBuffer = m_ExposureData?.currentExposureBuffer ?? exposureBuffer;
                    exposureUpdated = exposureBuffer != null;
                }
            }
            else if (m_EnableExposure
                     && m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                     && m_AutoExposureSettings.mode == AutoExposureMode.Manual)
            {
                if (ExecuteHDRPManualExposure(cmd))
                {
                    exposureBuffer = m_ExposureData?.currentExposureBuffer ?? exposureBuffer;
                    exposureUpdated = exposureBuffer != null;
                }
            }

            if (m_ExposureData != null)
            {
                m_ExposureData.frameExposureBuffer = exposureBuffer;
                m_ExposureData.histogramBuffer = m_EnableAutoExposure && UsesHistogramBufferAutoExposureExecution()
                    ? m_AutoExposureHistogramBuffer
                    : null;
            }

#if UNITY_EDITOR
            var preExposureBuffer = m_EnableExposure
                ? VividAutoExposureSystem.ResolvePreExposureBuffer(m_ExposureData)
                : null;
            AutoExposureStatsReadbackBridge.Request(
                cmd,
                m_Camera,
                m_AutoExposureSettings,
                m_EnableExposure,
                m_EnableAutoExposure,
                m_ExposureData != null && m_ExposureData.hasValidHistory,
                m_EnableExposure ? exposureBuffer : null,
                preExposureBuffer,
                m_EnableAutoExposure && UsesHistogramBufferAutoExposureExecution()
                    ? m_AutoExposureHistogramBuffer
                    : null);
#endif

            if (exposureUpdated)
                VividAutoExposureSystem.CommitFrame(m_Camera);
        }

        public override void Dispose()
        {
            m_AutoExposureHistogramBuffer?.Dispose();
            m_AutoExposureHistogramBuffer = null;
            ReleaseHDRPScratchTexture(ref m_HDRPPrePassTexture);
            ReleaseHDRPScratchTexture(ref m_HDRPReductionTexture);
            m_AutoExposureCompute = null;
            m_HistogramAutoExposureCompute = null;
            m_AutoExposureImplementation = AutoExposureImplementationPath.Unreal;
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
            m_ResolveExposureKernel = -1;
            m_HdrpFixedExposureKernel = -1;
            m_HdrpManualCameraExposureKernel = -1;
            m_HdrpHistogramClearKernel = -1;
            m_HdrpHistogramGenKernel = -1;
            m_HdrpHistogramReduceKernel = -1;
            m_HdrpPrePassKernel = -1;
            m_HdrpReductionKernel = -1;
            m_HdrpResetKernel = -1;
        }

        private bool ExecuteAutoExposure(CommandBuffer cmd)
        {
            if (UsesHDRPHistogramAutoExposureExecution())
                return ExecuteHDRPHistogramAutoExposure(cmd);

            if (UsesUnrealAutoExposureExecution())
                return ExecuteUnrealAutoExposure(cmd);

            return ExecuteHDRPAutoExposure(cmd);
        }



        private void BindAutoExposureParameters(CommandBuffer cmd, ComputeShader computeShader, int kernel)
        {
            if (cmd == null || kernel < 0 || computeShader == null)
                return;

            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureParams0Id,
                new Vector4(
                    m_AutoExposureSettings.exposureLowPercent,
                    m_AutoExposureSettings.exposureHighPercent,
                    m_AutoExposureSettings.minAverageLuminance,
                    m_AutoExposureSettings.maxAverageLuminance));
            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureParams1Id,
                new Vector4(
                    m_AutoExposureSettings.exposureSpeedUp,
                    m_AutoExposureSettings.exposureSpeedDown,
                    m_AutoExposureSettings.exposureCompensationSettings,
                    m_AutoExposureSettings.deltaTime));
            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureParams2Id,
                new Vector4(
                    m_AutoExposureSettings.histogramScale,
                    m_AutoExposureSettings.histogramBias,
                    m_AutoExposureSettings.luminanceMin,
                    m_AutoExposureSettings.forceTarget));
            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureParams3Id,
                new Vector4(
                    m_AutoExposureSettings.exponentialUpM,
                    m_AutoExposureSettings.exponentialDownM,
                    m_AutoExposureSettings.startDistance,
                    0f));
            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureCurveParamsId,
                new Vector4(
                    m_AutoExposureSettings.exposureCompensationCurveMinEV100,
                    m_AutoExposureSettings.exposureCompensationCurveInvRange,
                    m_AutoExposureSettings.exposureCompensationCurveEnabled ? 1f : 0f,
                    0f));
            cmd.SetComputeVectorParam(
                computeShader,
                AutoExposureScreenSizeId,
                new Vector4(
                    m_AutoExposureWidth,
                    m_AutoExposureHeight,
                    1f / Mathf.Max(1, m_AutoExposureWidth),
                    1f / Mathf.Max(1, m_AutoExposureHeight)));
            cmd.SetComputeTextureParam(
                computeShader,
                kernel,
                AutoExposureCompensationCurveId,
                m_AutoExposureSettings.exposureCompensationCurveTexture != null
                    ? m_AutoExposureSettings.exposureCompensationCurveTexture
                    : Texture2D.blackTexture);
        }


        private bool UsesUnrealAutoExposureExecution()
        {
            return m_AutoExposureImplementation != AutoExposureImplementationPath.HDRP;
        }

        private bool UsesHDRPHistogramAutoExposureExecution()
        {
            return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                && AutoExposureExposureModeUtility.UsesHistogramSettings(m_AutoExposureSettings.exposureMode);
        }

        private bool UsesHistogramBufferAutoExposureExecution()
        {
            return UsesUnrealAutoExposureExecution()
                || UsesHDRPHistogramAutoExposureExecution();
        }

        private bool SupportsUnrealAutoExposurePath()
        {
            return m_HistogramAutoExposureCompute != null
                && m_ClearHistogramKernel >= 0
                && m_BuildHistogramKernel >= 0
                && m_ResolveExposureKernel >= 0;
        }

        private bool SupportsHdrpHistogramAutoExposurePath()
        {
            return m_AutoExposureCompute != null
                && m_HdrpHistogramGenKernel >= 0
                && m_HdrpHistogramReduceKernel >= 0
                && m_HdrpResetKernel >= 0;
        }

        private bool SupportsActiveAutoExposureExecutionPath()
        {
            if (UsesHDRPHistogramAutoExposureExecution())
                return SupportsHdrpHistogramAutoExposurePath();

            return SupportsSelectedAutoExposureImplementation();
        }

        private bool SupportsSelectedAutoExposureImplementation()
        {
            return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? AutoExposureExposureModeUtility.UsesHistogramSettings(m_AutoExposureSettings.exposureMode)
                    ? SupportsHdrpHistogramAutoExposurePath()
                    : m_HdrpPrePassKernel >= 0 && m_HdrpReductionKernel >= 0 && m_HdrpResetKernel >= 0
                : SupportsUnrealAutoExposurePath();
        }

        private void RefreshAutoExposureImplementation()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var implementation = m_ExposureData != null
                ? m_ExposureData.implementation
                : AutoExposureImplementationUtility.ResolveImplementation(VividRenderPipelineAsset.GetActiveAsset());
            var computeShader = AutoExposureImplementationUtility.ResolveComputeShader(resources, implementation);
            var histogramComputeShader = resources?.AutoExposureCompute;

            if (m_AutoExposureImplementation == implementation
                && m_AutoExposureCompute == computeShader
                && m_HistogramAutoExposureCompute == histogramComputeShader)
            {
                return;
            }

            m_AutoExposureImplementation = implementation;
            m_AutoExposureCompute = computeShader;
            m_HistogramAutoExposureCompute = histogramComputeShader;
            ResolveAutoExposureKernels();
        }

        private void ResolveAutoExposureKernels()
        {
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
            m_ResolveExposureKernel = -1;
            m_HdrpFixedExposureKernel = -1;
            m_HdrpManualCameraExposureKernel = -1;
            m_HdrpHistogramClearKernel = -1;
            m_HdrpHistogramGenKernel = -1;
            m_HdrpHistogramReduceKernel = -1;
            m_HdrpPrePassKernel = -1;
            m_HdrpReductionKernel = -1;
            m_HdrpResetKernel = -1;

            if (AutoExposureImplementationUtility.SupportsUnrealDispatch(m_HistogramAutoExposureCompute))
            {
                m_ClearHistogramKernel = m_HistogramAutoExposureCompute.FindKernel(ClearHistogramKernelName);
                m_BuildHistogramKernel = m_HistogramAutoExposureCompute.FindKernel(BuildHistogramKernelName);
                m_ResolveExposureKernel = m_HistogramAutoExposureCompute.FindKernel(ResolveExposureKernelName);
            }
            else if (m_AutoExposureImplementation != AutoExposureImplementationPath.HDRP)
            {
                Debug.LogWarning("[VividRP] Unreal auto exposure is selected, but the compute shader is missing the required histogram kernels.");
            }

            if (m_AutoExposureImplementation != AutoExposureImplementationPath.HDRP)
                return;

            if (m_AutoExposureCompute == null)
            {
                Debug.LogWarning("[VividRP] HDRP auto exposure is selected, but the HDRP compute shader is missing.");
                return;
            }

            if (m_AutoExposureCompute.HasKernel(HdrpFixedExposureKernelName))
                m_HdrpFixedExposureKernel = m_AutoExposureCompute.FindKernel(HdrpFixedExposureKernelName);

            if (m_AutoExposureCompute.HasKernel(HdrpManualCameraExposureKernelName))
                m_HdrpManualCameraExposureKernel = m_AutoExposureCompute.FindKernel(HdrpManualCameraExposureKernelName);

            if (!m_AutoExposureCompute.HasKernel(HdrpResetKernelName))
            {
                Debug.LogWarning("[VividRP] HDRP auto exposure is selected, but the HDRP compute shader is missing the required kernels.");
                return;
            }

            m_HdrpResetKernel = m_AutoExposureCompute.FindKernel(HdrpResetKernelName);

            if (AutoExposureImplementationUtility.SupportsHdrpPrePassDispatch(m_AutoExposureCompute))
            {
                m_HdrpPrePassKernel = m_AutoExposureCompute.FindKernel(HdrpPrePassKernelName);
                m_HdrpReductionKernel = m_AutoExposureCompute.FindKernel(HdrpReductionKernelName);
            }

            if (AutoExposureImplementationUtility.SupportsHdrpHistogramDispatch(m_AutoExposureCompute))
            {
                m_HdrpHistogramClearKernel = m_AutoExposureCompute.FindKernel(HdrpHistogramClearKernelName);
                m_HdrpHistogramGenKernel = m_AutoExposureCompute.FindKernel(HdrpHistogramGenKernelName);
                m_HdrpHistogramReduceKernel = m_AutoExposureCompute.FindKernel(HdrpHistogramReduceKernelName);
            }
        }

        private void EnsureAutoExposureHistogramBuffer()
        {
            var histogramBucketCount = ResolveActiveAutoExposureHistogramBucketCount();
            if (m_AutoExposureHistogramBuffer != null
                && m_AutoExposureHistogramBuffer.count == histogramBucketCount
                && m_AutoExposureHistogramBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_AutoExposureHistogramBuffer?.Dispose();
            m_AutoExposureHistogramBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                histogramBucketCount,
                sizeof(uint));
            m_AutoExposureHistogramBuffer.name = "VividRP Auto Exposure Histogram";
        }

        private int ResolveActiveAutoExposureHistogramBucketCount()
        {
            return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? HdrpAutoExposureHistogramBucketCount
                : UnrealAutoExposureHistogramBucketCount;
        }

        private void EnsureHdrpScratchTextures()
        {
            EnsureHDRPScratchTexture(
                ref m_HDRPPrePassTexture,
                HdrpAutoExposurePrePassSize,
                HdrpAutoExposurePrePassSize,
                "VividRP HDRP Auto Exposure PrePass");
            EnsureHDRPScratchTexture(
                ref m_HDRPReductionTexture,
                HdrpAutoExposureReductionSize,
                HdrpAutoExposureReductionSize,
                "VividRP HDRP Auto Exposure Reduction");
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

        private static Rect ResolveViewport(VividCameraData cameraData)
        {
            if (cameraData == null)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            if (cameraData.pixelRect.width > 0f && cameraData.pixelRect.height > 0f)
                return cameraData.pixelRect;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0 || height <= 0)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            return new Rect(0f, 0f, width, height);
        }
    }
}
