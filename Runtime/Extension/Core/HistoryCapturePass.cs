using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
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
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.PrevNormalRoughness);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.PrevNormalRoughness,
                    HistoryCaptureBufferAllocatorFunction, GraphicsFormat.R8G8B8A8_UNorm, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness);
            return vaild;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            var historyCaptureData = frameData.GetOrCreate<HistoryCaptureData>();
            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);




            bool vaild = true;
            vaild &= ReAllocatedHistoryColorTextureIfNeeded(camHistoryRTSystem, out var historyColor);
            vaild &= ReAllocatedHistoryDepthTextureIfNeeded(camHistoryRTSystem, out var historyDepth);
            vaild &= ReAllocatedHistoryNormalTextureIfNeeded(camHistoryRTSystem, out var historyNormal);
            historyCaptureData.HistoryColorTexture = renderGraph.ImportTexture(historyColor);
            historyCaptureData.HistoryDepthTexture = renderGraph.ImportTexture(historyDepth);
            historyCaptureData.HistoryNormalTexture = renderGraph.ImportTexture(historyNormal);


            MipGenerator.instance.CopyColor(renderGraph, resourceData.activeColorTexture, historyCaptureData.HistoryColorTexture);
            MipGenerator.instance.CopyColor(renderGraph, resourceData.activeDepthTexture, historyCaptureData.HistoryDepthTexture);
            MipGenerator.instance.CopyColor(renderGraph, resourceData.gBuffer[2], historyCaptureData.HistoryNormalTexture);

        }
    }
}