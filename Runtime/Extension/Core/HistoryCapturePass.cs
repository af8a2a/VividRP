using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Core
{
    public class HistoryCapturePass : ScriptableRenderPass
    {
        static RTHandle HistoryCaptureBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                enableRandomWrite: true,  useDynamicScale: true,
                name: string.Format("{0}_CameraCaptureBuffer{1}", viewName, frameIndex));
        }

        internal bool ReAllocatedHistoryDepthTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT,
            out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryDepth);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryDepth);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryDepth,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R32_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryDepth);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryDepth);
            return vaild;
        }

        internal bool ReAllocatedHistoryColorIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT,
            out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryColor);

            bool vaild = true;
            if (curTexture == null)
            {
                vaild = false;
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryColor);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryColor,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryColor);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceGlobalIlluminationHistoryColor);
            return vaild;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var historyCaptureData = frameData.GetOrCreate<HistoryCaptureData>();

            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            bool vaild = true;
            vaild &= ReAllocatedHistoryColorIfNeeded(camHistoryRTSystem, out var currColorTexture, out var prevColorTexture);
            vaild &= ReAllocatedHistoryDepthTextureIfNeeded(camHistoryRTSystem, out var currDepthTexture, out var prevDepthTexture);

            historyCaptureData.PrevDepthTexture = renderGraph.ImportTexture(prevDepthTexture);
            historyCaptureData.CurrDepthTexture = renderGraph.ImportTexture(currDepthTexture);
            historyCaptureData.PrevColorTexture = renderGraph.ImportTexture(prevColorTexture);
            historyCaptureData.CurrColorTexture = renderGraph.ImportTexture(currColorTexture);


            MipGenerator.Instance.CopyColor(renderGraph, frameData, resourceData.activeColorTexture, historyCaptureData.CurrColorTexture);
            MipGenerator.Instance.CopyColor(renderGraph, frameData, resourceData.activeDepthTexture, historyCaptureData.CurrDepthTexture);
            if (!vaild)
            {
                MipGenerator.Instance.CopyColor(renderGraph, frameData, historyCaptureData.CurrColorTexture, historyCaptureData.PrevColorTexture);
                MipGenerator.Instance.CopyColor(renderGraph, frameData, historyCaptureData.CurrDepthTexture, historyCaptureData.PrevDepthTexture);
            }
        }
    }
}