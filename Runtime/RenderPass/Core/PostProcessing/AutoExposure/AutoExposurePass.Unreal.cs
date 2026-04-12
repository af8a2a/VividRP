using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass
{
    partial class AutoExposurePass
    {
        
        private ComputeShader m_AutoExposureCompute;
        private ComputeShader m_HistogramAutoExposureCompute;
        private int m_ClearHistogramKernel = -1;
        private int m_BuildHistogramKernel = -1;
        private int m_ResolveExposureKernel = -1;

        private bool ExecuteUnrealAutoExposure(CommandBuffer cmd)
        {
            var histogramCompute = m_HistogramAutoExposureCompute;
            if (cmd == null
                || histogramCompute == null
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

            BindAutoExposureParameters(cmd, histogramCompute, m_ClearHistogramKernel);
            cmd.SetComputeBufferParam(histogramCompute, m_ClearHistogramKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.DispatchCompute(histogramCompute, m_ClearHistogramKernel, 1, 1, 1);

            BindAutoExposureParameters(cmd, histogramCompute, m_BuildHistogramKernel);
            cmd.SetComputeTextureParam(histogramCompute, m_BuildHistogramKernel, AutoExposureInputTextureId,
                source.innerHandle);
            cmd.SetComputeTextureParam(histogramCompute, m_BuildHistogramKernel, AutoExposureMeterMaskId, meterMask);
            cmd.SetComputeBufferParam(histogramCompute, m_BuildHistogramKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(histogramCompute, m_BuildHistogramKernel, AutoExposurePreviousBufferId,
                previousExposureBuffer);
            cmd.DispatchCompute(
                histogramCompute,
                m_BuildHistogramKernel,
                CoreUtils.DivRoundUp(m_AutoExposureWidth, AutoExposureHistogramThreadGroupSizeX),
                CoreUtils.DivRoundUp(m_AutoExposureHeight, AutoExposureHistogramThreadGroupSizeY),
                1);

            BindAutoExposureParameters(cmd, histogramCompute, m_ResolveExposureKernel);
            cmd.SetComputeBufferParam(histogramCompute, m_ResolveExposureKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(histogramCompute, m_ResolveExposureKernel, AutoExposurePreviousBufferId,
                previousExposureBuffer);
            cmd.SetComputeBufferParam(histogramCompute, m_ResolveExposureKernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.DispatchCompute(histogramCompute, m_ResolveExposureKernel, 1, 1, 1);
            return true;
        }
    }
}