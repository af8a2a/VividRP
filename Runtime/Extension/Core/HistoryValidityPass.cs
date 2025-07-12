using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class HistoryValidityPass : ScriptableRenderPass
    {
        public void Setup(bool deferred)
        {
            if (deferred)
            {
                ConfigureInput(ScriptableRenderPassInput.Normal);
            }
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();


            var deferred = renderingData.renderingMode is RenderingMode.Deferred;


            var temporalFilter = cameraData.denoiseSystem.temporalDenoiser;

            cameraData.denoiseSystem.historyValidity = temporalFilter.HistoryValidity(renderGraph, cameraData,
                deferred ? resourceData.gBuffer[2] : resourceData.cameraNormalsTexture, resourceData.motionVectorColor, resourceData.cameraDepthTexture);
        }
    }
}