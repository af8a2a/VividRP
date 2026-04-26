using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass
{
    partial class AutoExposurePass
    {
        private static readonly uint[] s_EmptyHdrpHistogramData = new uint[HdrpAutoExposureHistogramBucketCount];

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
        private readonly int[] m_HdrpVariants = new int[4];


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
                : 0u;

            if (previousExposureBuffer == null)
                return false;

            if (!m_ExposureData.hasValidHistory)
            {
                cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpResetKernel, HdrpOutputTextureId, previousExposureTexture);
                cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpResetKernel, 1, 1, 1);
            }

            cmd.SetBufferData(m_AutoExposureHistogramBuffer, s_EmptyHdrpHistogramData);

            BindHDRPHistogramGenerationParameters(cmd);
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
                CoreUtils.DivRoundUp(Mathf.Max(1, m_AutoExposureWidth / 2), HdrpHistogramThreadGroupSizeX),
                CoreUtils.DivRoundUp(Mathf.Max(1, m_AutoExposureHeight / 2), HdrpHistogramThreadGroupSizeY),
                1);

            BindHDRPHistogramReductionParameters(cmd, evaluateMode);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel,
                HdrpPreviousExposureTextureId, previousExposureTexture);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, HdrpExposureCurveTextureId, curveTexture);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, AutoExposureCurrentBufferId, m_ExposureData.currentExposureBuffer);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, HdrpOutputTextureId, currentExposureTexture);
            cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpHistogramReduceKernel, 1, 1, 1);
            cmd.SetGlobalTexture(HdrpPreviousExposureTextureId, currentExposureTexture);
            return true;
        }

        private bool ExecuteHDRPAutoExposure(CommandBuffer cmd)
        {
            // if (cmd == null
            //     || m_AutoExposureCompute == null
            //     || m_ExposureData?.currentExposureBuffer == null
            //     || m_ExposureData.previousExposureTexture == null
            //     || m_ExposureData.currentExposureTexture == null
            //     || source?.innerHandle.IsValid() != true)
            // {
            //     return false;
            // }
            //
            // EnsureHdrpScratchTextures();
            // if (m_HDRPPrePassTexture == null || m_HDRPReductionTexture == null)
            //     return false;
            //
            // var meterMask = m_AutoExposureSettings.meterMask != null
            //     ? m_AutoExposureSettings.meterMask
            //     : Texture2D.whiteTexture;
            // var curveTexture = ResolveHDRPExposureCurveTexture();
            // var evaluateMode = AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode)
            //     ? 2u
            //     : 1u;
            // var previousExposureTexture = m_ExposureData.previousExposureTexture;
            // var currentExposureTexture = m_ExposureData.currentExposureTexture;
            //
            // if (!m_ExposureData.hasValidHistory)
            // {
            //     BindHDRPAutoExposureParameters(cmd, m_HdrpResetKernel, 0u);
            //     cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpResetKernel, HdrpOutputTextureId,
            //         previousExposureTexture);
            //     cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpResetKernel, 1, 1, 1);
            // }
            //
            // BindHDRPAutoExposureParameters(cmd, m_HdrpPrePassKernel, 0u);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpSourceTextureId,
            //     source.innerHandle);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpPreviousExposureTextureId,
            //     previousExposureTexture);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureWeightMaskId, meterMask);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpExposureCurveTextureId,
            //     curveTexture);
            // cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpPrePassKernel, AutoExposureCurrentBufferId,
            //     m_ExposureData.currentExposureBuffer);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpPrePassKernel, HdrpOutputTextureId,
            //     m_HDRPPrePassTexture);
            // cmd.DispatchCompute(
            //     m_AutoExposureCompute,
            //     m_HdrpPrePassKernel,
            //     HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
            //     HdrpAutoExposurePrePassSize / HdrpAutoExposureThreadGroupSize,
            //     1);
            //
            // BindHDRPAutoExposureParameters(cmd, m_HdrpReductionKernel, 0u);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId,
            //     m_HDRPPrePassTexture);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId,
            //     previousExposureTexture);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId,
            //     meterMask);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId,
            //     curveTexture);
            // cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId,
            //     m_ExposureData.currentExposureBuffer);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId,
            //     m_HDRPReductionTexture);
            // cmd.DispatchCompute(
            //     m_AutoExposureCompute,
            //     m_HdrpReductionKernel,
            //     HdrpAutoExposureReductionSize,
            //     HdrpAutoExposureReductionSize,
            //     1);
            //
            // BindHDRPAutoExposureParameters(cmd, m_HdrpReductionKernel, evaluateMode);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpReductionInputTextureId,
            //     m_HDRPReductionTexture);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpPreviousExposureTextureId,
            //     previousExposureTexture);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureWeightMaskId,
            //     meterMask);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpExposureCurveTextureId,
            //     curveTexture);
            // cmd.SetComputeBufferParam(m_AutoExposureCompute, m_HdrpReductionKernel, AutoExposureCurrentBufferId,
            //     m_ExposureData.currentExposureBuffer);
            // cmd.SetComputeTextureParam(m_AutoExposureCompute, m_HdrpReductionKernel, HdrpOutputTextureId,
            //     currentExposureTexture);
            // cmd.DispatchCompute(m_AutoExposureCompute, m_HdrpReductionKernel, 1, 1, 1);
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

            ResolveHDRPAutoExposureContext(
                out var compensationStops,
                out var minExposureEV100,
                out var maxExposureEV100,
                out var curveMinEV100,
                out var curveMaxEV100,
                out var histogramScaleBias,
                out var meteringMode,
                out var adaptationMode);

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
                    ColorUtils.lensImperfectionExposureScale,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramRangeParamsId,
                new Vector4(
                    histogramScaleBias.x,
                    histogramScaleBias.y,
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
                    m_AutoExposureSettings.exposureSpeedDown,
                    m_AutoExposureSettings.exposureSpeedUp,
                    0f,
                    0f));
            SetHDRPVariants(cmd, meteringMode, adaptationMode, (int)evaluateMode);
        }

        private void BindHDRPHistogramGenerationParameters(CommandBuffer cmd)
        {
            if (cmd == null || m_AutoExposureCompute == null)
                return;

            ResolveHDRPAutoExposureContext(
                out _,
                out _,
                out _,
                out _,
                out _,
                out var histogramScaleBias,
                out var meteringMode,
                out var adaptationMode);

            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpExposureParams2Id,
                new Vector4(
                    0f,
                    0f,
                    ColorUtils.lensImperfectionExposureScale,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramRangeParamsId,
                new Vector4(
                    histogramScaleBias.x,
                    histogramScaleBias.y,
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
            SetHDRPVariants(cmd, meteringMode, adaptationMode, 0);
        }

        private void BindHDRPHistogramReductionParameters(CommandBuffer cmd, uint evaluateMode)
        {
            if (cmd == null || m_AutoExposureCompute == null)
                return;

            ResolveHDRPAutoExposureContext(
                out var compensationStops,
                out var minExposureEV100,
                out var maxExposureEV100,
                out var curveMinEV100,
                out var curveMaxEV100,
                out var histogramScaleBias,
                out var meteringMode,
                out var adaptationMode);

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
                    ColorUtils.lensImperfectionExposureScale,
                    m_AutoExposureSettings.targetMidGray));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                HdrpHistogramRangeParamsId,
                new Vector4(
                    histogramScaleBias.x,
                    histogramScaleBias.y,
                    m_AutoExposureSettings.exposureLowPercent,
                    m_AutoExposureSettings.exposureHighPercent));
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
                    m_AutoExposureSettings.exposureSpeedDown,
                    m_AutoExposureSettings.exposureSpeedUp,
                    0f,
                    0f));
            SetHDRPVariants(cmd, meteringMode, adaptationMode, (int)evaluateMode);
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
                    ColorUtils.lensImperfectionExposureScale,
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
            SetHDRPVariants(cmd, 0, 0, 0);
        }

        private void SetHDRPVariants(CommandBuffer cmd, int meteringMode, int adaptationMode, int evaluateMode)
        {
            m_HdrpVariants[0] = 1;
            m_HdrpVariants[1] = meteringMode;
            m_HdrpVariants[2] = adaptationMode;
            m_HdrpVariants[3] = evaluateMode;
            cmd.SetComputeIntParams(m_AutoExposureCompute, HdrpVariantsId, m_HdrpVariants);
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

        private int ResolveHDRPMeteringMode()
        {
            switch (m_AutoExposureSettings.meteringMode)
            {
                case AutoExposureMeteringMode.Spot:
                    return 1;
                case AutoExposureMeteringMode.CenterWeighted:
                    return 2;
                case AutoExposureMeteringMode.MaskWeighted:
                    return m_AutoExposureSettings.meterMask != null ? 3 : 0;
                default:
                    return 0;
            }
        }

        private void ResolveHDRPAutoExposureContext(
            out float compensationStops,
            out float minExposureEV100,
            out float maxExposureEV100,
            out float curveMinEV100,
            out float curveMaxEV100,
            out Vector2 histogramScaleBias,
            out int meteringMode,
            out int adaptationMode)
        {
            compensationStops = Mathf.Log(Mathf.Max(m_AutoExposureSettings.exposureCompensationSettings, 1e-6f), 2f);
            minExposureEV100 = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(
                m_AutoExposureSettings.minAverageLuminance);
            maxExposureEV100 = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(
                m_AutoExposureSettings.maxAverageLuminance);

            var usesCurveRemapping = AutoExposureExposureModeUtility.UsesCurveRemapping(m_AutoExposureSettings.exposureMode);
            curveMinEV100 = usesCurveRemapping ? m_AutoExposureSettings.curveMapMinEV100 : 0f;
            curveMaxEV100 = usesCurveRemapping
                ? Mathf.Max(m_AutoExposureSettings.curveMapMaxEV100, curveMinEV100 + 1e-4f)
                : 0f;
            histogramScaleBias = ResolveHDRPHistogramScaleBias(minExposureEV100, maxExposureEV100);
            meteringMode = ResolveHDRPMeteringMode();
            adaptationMode = m_AutoExposureSettings.adaptationMode == AutoExposureAdaptationMode.Progressive
                             && m_AutoExposureSettings.forceTarget <= 0.5f
                ? 1
                : 0;
        }

        private static Vector2 ResolveHDRPHistogramScaleBias(float minExposureEV100, float maxExposureEV100)
        {
            var resolvedMaxExposureEV100 = Mathf.Max(maxExposureEV100, minExposureEV100 + 1e-4f);
            var exposureRange = Mathf.Max(resolvedMaxExposureEV100 - minExposureEV100, 1e-4f);
            var histogramScale = 1f / exposureRange;
            var histogramBias = -minExposureEV100 * histogramScale;
            return new Vector2(histogramScale, histogramBias);
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
