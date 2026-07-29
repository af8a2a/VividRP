using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass
{
    partial class AutoExposurePass
    {
        private static readonly int AutoExposurePartialHistogramBufferId =
            Shader.PropertyToID("_PartialHistogramBuffer");

        private ComputeShader m_AutoExposureCompute;
        private ComputeShader m_HistogramAutoExposureCompute;
        private GraphicsBuffer m_UnrealPartialHistogramBuffer;
        private int m_BuildHistogramKernel = -1;
        private int m_ResolveExposureKernel = -1;
        private int m_ResolveBasicExposureKernel = -1;

        private bool ExecuteUnrealAutoExposure(CommandBuffer cmd)
        {
            var histogramCompute = m_HistogramAutoExposureCompute;
            if (cmd == null
                || histogramCompute == null
                || m_AutoExposureHistogramBuffer == null
                || m_UnrealPartialHistogramBuffer == null
                || m_ExposureData?.defaultExposureBuffer == null
                || m_ExposureData.currentExposureBuffer == null)
            {
                return false;
            }

            var meterMask = m_AutoExposureSettings.unrealExposureMeteringMask != null
                ? m_AutoExposureSettings.unrealExposureMeteringMask
                : Texture2D.whiteTexture;
            var resolveKernel = m_AutoExposureSettings.mode == AutoExposureMode.Basic
                ? m_ResolveBasicExposureKernel
                : m_ResolveExposureKernel;
            var previousExposureBuffer = m_ExposureData.hasValidHistory
                ? m_ExposureData.previousExposureBuffer
                : m_ExposureData.defaultExposureBuffer;

            if (previousExposureBuffer == null || source?.innerHandle.IsValid() != true)
                return false;

            BindAutoExposureParameters(cmd, histogramCompute, m_BuildHistogramKernel);
            cmd.SetComputeTextureParam(histogramCompute, m_BuildHistogramKernel, AutoExposureInputTextureId,
                source.innerHandle);
            cmd.SetComputeTextureParam(histogramCompute, m_BuildHistogramKernel, AutoExposureMeterMaskId, meterMask);
            cmd.SetComputeBufferParam(histogramCompute, m_BuildHistogramKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(
                histogramCompute,
                m_BuildHistogramKernel,
                AutoExposurePartialHistogramBufferId,
                m_UnrealPartialHistogramBuffer);
            cmd.SetComputeBufferParam(histogramCompute, m_BuildHistogramKernel, AutoExposurePreviousBufferId,
                previousExposureBuffer);
            cmd.DispatchCompute(
                histogramCompute,
                m_BuildHistogramKernel,
                Mathf.Max(1, m_AutoExposureHeight),
                1,
                1);
            InsertUnrealHistogramBuildToResolveFence(cmd);

            BindAutoExposureParameters(cmd, histogramCompute, resolveKernel);
            cmd.SetComputeBufferParam(histogramCompute, resolveKernel, AutoExposureHistogramBufferId,
                m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(
                histogramCompute,
                resolveKernel,
                AutoExposurePartialHistogramBufferId,
                m_UnrealPartialHistogramBuffer);
            cmd.SetComputeBufferParam(histogramCompute, resolveKernel, AutoExposurePreviousBufferId,
                previousExposureBuffer);
            cmd.SetComputeBufferParam(histogramCompute, resolveKernel, AutoExposureCurrentBufferId,
                m_ExposureData.currentExposureBuffer);
            cmd.DispatchCompute(histogramCompute, resolveKernel, 1, 1, 1);
            return true;
        }

        private static void InsertUnrealHistogramBuildToResolveFence(
            CommandBuffer cmd)
        {
            var fence = cmd.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.ComputeProcessing);
            cmd.WaitOnAsyncGraphicsFence(
                fence,
                SynchronisationStageFlags.ComputeProcessing);
        }

        internal static int ResolveUnrealPartialHistogramBufferCount(int height)
        {
            return UnrealAutoExposureHistogramBucketCount * Mathf.Max(1, height);
        }

        private void EnsureUnrealPartialHistogramBuffer()
        {
            if (m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP)
            {
                DisposeUnrealAutoExposureResources();
                return;
            }

            var requiredCount = ResolveUnrealPartialHistogramBufferCount(
                m_AutoExposureHeight);
            if (m_UnrealPartialHistogramBuffer != null
                && m_UnrealPartialHistogramBuffer.count >= requiredCount
                && m_UnrealPartialHistogramBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_UnrealPartialHistogramBuffer?.Dispose();
            m_UnrealPartialHistogramBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredCount,
                sizeof(uint));
            m_UnrealPartialHistogramBuffer.name =
                "VividRP Unreal Auto Exposure Partial Histogram";
        }

        private void DisposeUnrealAutoExposureResources()
        {
            m_UnrealPartialHistogramBuffer?.Dispose();
            m_UnrealPartialHistogramBuffer = null;
        }
    }
}
