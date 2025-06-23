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
                enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_CameraCaptureBuffer{1}", viewName, frameIndex));
        }

        internal bool ReAllocatedHistoryDepthTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Depth);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.Depth);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Depth,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R32_SFloat, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Depth);
            return vaild;
        }

        internal bool ReAllocatedHistoryColorTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Color);

            bool vaild = true;
            if (curTexture == null)
            {
                vaild = false;
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.Color);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Color,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R16G16B16A16_SFloat, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Color);
            return vaild;
        }
        
        
        

        internal bool ReAllocatedHistoryNormalTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Normal);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.Normal);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Normal,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R8G8B8A8_UNorm, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Normal);
            return vaild;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            var historyCaptureData = frameData.GetOrCreate<HistoryCaptureData>();
            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);


            var deferred = frameData.Get<UniversalRenderingData>().renderingMode is RenderingMode.Deferred;


            bool vaild = true;
            vaild &= ReAllocatedHistoryColorTextureIfNeeded(camHistoryRTSystem, out var historyColor);
            vaild &= ReAllocatedHistoryDepthTextureIfNeeded(camHistoryRTSystem, out var historyDepth);
            vaild &= ReAllocatedHistoryNormalTextureIfNeeded(camHistoryRTSystem, out var historyNormal);
            historyCaptureData.HistoryColorTexture = renderGraph.ImportTexture(historyColor);
            historyCaptureData.HistoryDepthTexture = renderGraph.ImportTexture(historyDepth);
            historyCaptureData.HisotryNormalTexture = renderGraph.ImportTexture(historyNormal);


            MipGenerator.Instance.CopyColor(renderGraph, frameData, resourceData.activeColorTexture, historyCaptureData.HistoryColorTexture);
            MipGenerator.Instance.CopyColor(renderGraph, frameData, resourceData.activeDepthTexture, historyCaptureData.HistoryDepthTexture);
            MipGenerator.Instance.CopyColor(renderGraph, frameData, deferred ? resourceData.gBuffer[2] : resourceData.cameraNormalsTexture,
                historyCaptureData.HisotryNormalTexture);

        }
    }
}