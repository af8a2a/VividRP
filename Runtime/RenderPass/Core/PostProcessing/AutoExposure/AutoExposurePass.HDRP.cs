using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass
{
    partial class AutoExposurePass
    {
        private int m_HdrpFixedExposureKernel = -1;
        private int m_HdrpManualCameraExposureKernel = -1;
        private int m_HdrpHistogramClearKernel = -1;
        private int m_HdrpHistogramGenKernel = -1;
        private int m_HdrpHistogramReduceKernel = -1;
        private int m_HdrpPrePassKernel = -1;
        private int m_HdrpReductionKernel = -1;
        private int m_HdrpResetKernel = -1;
        private GraphicsBuffer m_AutoExposureHistogramBuffer;
        private RenderTexture m_HDRPPrePassTexture;
        private RenderTexture m_HDRPReductionTexture;


        private bool ExecuteHDRPHistogramAutoExposure(CommandBuffer cmd)
        {
            if (cmd == null
                || m_AutoExposureCompute == null
                || m_AutoExposureHistogramBuffer == null
                || m_ExposureData?.defaultExposureBuffer == null
                || m_ExposureData.currentExposureBuffer == null
                || m_ExposureData.previousExposureTexture == null
                || m_ExposureData.currentExposureTexture == null
                || source?.innerHandle.IsValid() != true)
            {
                return false;
            }

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var curveTexture = ResolveHDRPExposureCurveTexture();
            var previousExposureBuffer = m_ExposureData.hasValidHistory
                ? m_ExposureData.previousExposureBuffer
                : m_ExposureData.defaultExposureBuffer;
            var previousExposureTexture = m_ExposureData.previousExposureTexture;
            var currentExposureTexture = m_ExposureData.currentExposureTexture;
            var evaluateMode = AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode)
                ? 2u
                : 1u;

            if (previousExposureBuffer == null)
                return false;

            if (!m_ExposureData.hasValidHistory)
            {
                BindHDRPAutoExposureParameters(cmd, m_HdrpResetKernel, 0u);
                cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpResetKernel, HdrpOutputTextureId, previousExposureTexture);
                cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpResetKernel, 1, 1, 1);
            }

            BindHDRPAutoExposureParameters(cmd, m_HdrpHistogramClearKernel, 0u);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramClearKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpHistogramClearKernel, 1, 1, 1);

            BindHDRPAutoExposureParameters(cmd, m_HdrpHistogramGenKernel, 0u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramGenKernel, HdrpSourceTextureId,
                source.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramGenKernel, HdrpPreviousExposureTextureId,
                previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramGenKernel, HdrpExposureWeightMaskId,
                meterMask);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramGenKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_HdrpHistogramGenKernel,
                CoreUtils.DivRoundUp(m_AutoExposureWidth, HdrpAutoExposureThreadGroupSize),
                CoreUtils.DivRoundUp(m_AutoExposureHeight, HdrpAutoExposureThreadGroupSize),
                1);

            BindHDRPAutoExposureParameters(cmd, m_HdrpHistogramReduceKernel, evaluateMode);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel,
                HdrpPreviousExposureTextureId, previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, HdrpExposureCurveTextureId, curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, HdrpOutputTextureId, currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, 1, 1, 1);
            return true;
        }

        private bool ExecuteHDRPAutoExposure(CommandBuffer cmd)
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
            if (m_HDRPPrePassTexture == null || m_HDRPReductionTexture == null)
                return false;

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var curveTexture = ResolveHDRPExposureCurveTexture();
            var evaluateMode = AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode)
                ? 2u
                : 1u;
            var previousExposureTexture = m_ExposureData.previousExposureTexture;
            var currentExposureTexture = m_ExposureData.currentExposureTexture;

            if (!m_ExposureData.hasValidHistory)
            {
                BindHDRPAutoExposureParameters(cmd, m_HdrpResetKernel, 0u);
                cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpResetKernel, HdrpOutputTextureId,
                    previousExposureTexture);
                cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpResetKernel, 1, 1, 1);
            }

            BindHDRPAutoExposureParameters(cmd, m_HdrpPrePassKernel, 0u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpSourceTextureId,
                source.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpPreviousExposureTextureId,
                previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureWeightMaskId, meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureCurveTextureId,
                curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpPrePassKernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpOutputTextureId,
                m_HDRPPrePassTexture);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_HdrpPrePassKernel,
                HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
                HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
                1);

            BindHDRPAutoExposureParameters(cmd, m_HdrpReductionKernel, 0u);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId,
                m_HDRPPrePassTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId,
                previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId,
                meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId,
                curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId,
                m_HDRPReductionTexture);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_HdrpReductionKernel,
                HdrpAutoExposureReductionSize,
                HdrpAutoExposureReductionSize,
                1);

            BindHDRPAutoExposureParameters(cmd, m_HdrpReductionKernel, evaluateMode);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId,
                m_HDRPReductionTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId,
                previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId,
                meterMask);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId,
                curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId,
                currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpReductionKernel, 1, 1, 1);
            return true;
        }

        private bool ExecuteHDRPManualExposure(CommandBuffer cmd)
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

            BindHDRPManualExposureParameters(cmd, kernel);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, kernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, kernel, HdrpOutputTextureId,
                m_ExposureData.currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, kernel, 1, 1, 1);
            return true;
        }


        private void BindHDRPAutoExposureParameters(CommandBuffer cmd, int kernel, uint evaluateMode)
        {
            if (cmd == null || kernel < 0 || m_AutoExposureCompute == null)
                return;

            var compensationStops =
                Mathf.Log(Mathf.Max(m_AutoExposureSettings.exposureCompensationSettings, 1e-6f), 2f);
            var minExposureEV100 =
                AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings
                    .minAverageLuminance);
            var maxExposureEV100 =
                AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings
                    .maxAverageLuminance);
            var usesCurveRemapping =
                AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode);
            var curveMinEV100 = usesCurveRemapping
                ? m_AutoExposureSettings.curveMapMinEV100
                : 0f;
            var curveMaxEV100 = usesCurveRemapping
                ? Mathf.Max(m_AutoExposureSettings.curveMapMaxEV100, curveMinEV100 + 1e-4f)
                : 0f;
            var meteringMode = ResolveHDRPMeteringMode();
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
                    curveMinEV100,
                    curveMaxEV100,
                    1f,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramRangeParamsId,
                new Vector4(
                    m_AutoExposureSettings.histogramScale,
                    m_AutoExposureSettings.histogramBias,
                    m_AutoExposureSettings.exposureLowPercent,
                    m_AutoExposureSettings.exposureHighPercent));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureScreenSizeId,
                new Vector4(
                    m_AutoExposureWidth,
                    m_AutoExposureHeight,
                    1f / Mathf.Max(1, m_AutoExposureWidth),
                    1f / Mathf.Max(1, m_AutoExposureHeight)));
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

        private void BindHDRPManualExposureParameters(CommandBuffer cmd, int kernel)
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
                    m_AutoExposureSettings.targetMidGray));
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

        private Texture ResolveHDRPExposureCurveTexture()
        {
            if (AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode))
            {
                if (m_AutoExposureSettings.curveMapTexture != null)
                    return m_AutoExposureSettings.curveMapTexture;

                return AutoExposureCurveMapUtility.Resolve(
                    null,
                    AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings
                        .minAverageLuminance),
                    AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(m_AutoExposureSettings
                        .maxAverageLuminance)).texture;
            }

            return m_AutoExposureSettings.exposureCompensationCurveTexture != null
                ? m_AutoExposureSettings.exposureCompensationCurveTexture
                : Texture2D.blackTexture;
        }

        private float ResolveHDRPMeteringMode()
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

        private static void EnsureHDRPScratchTexture(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null
                && texture.IsCreated()
                && texture.width == width
                && texture.height == height
                && texture.enableRandomWrite)
            {
                return;
            }

            ReleaseHDRPScratchTexture(ref texture);

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

        private static void ReleaseHDRPScratchTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }
    }
}