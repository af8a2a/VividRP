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


            var temporalFilter = DenoiseSystem.GetTemporalFilter();

            if (deferred)
            {
                temporalFilter.HistoryValidity(renderGraph, cameraData,
                    resourceData.gBuffer[2],
                    resourceData.motionVectorColor,
                    resourceData.cameraDepthTexture);
            }
            else
            {
                temporalFilter.HistoryValidity(renderGraph, cameraData,
                    resourceData.cameraNormalsTexture,
                    resourceData.motionVectorColor,
                    resourceData.cameraDepthTexture);
            }
        }
    }
}