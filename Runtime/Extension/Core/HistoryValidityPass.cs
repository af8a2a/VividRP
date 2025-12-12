using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class HistoryValidityPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();




            var temporalFilter = cameraData.denoiseSystem.temporalDenoiser;

            cameraData.denoiseSystem.historyValidity = temporalFilter.HistoryValidity(renderGraph, cameraData,
                 resourceData.gBuffer[2] , resourceData.motionVectorColor, resourceData.cameraDepthTexture);
        }
    }
}