using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class SkyPrePass:ScriptableRenderPass
    {
        public SkyPrePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            SkySystem.instance.UpdateEnvironment(renderGraph, frameData, lightData, false, false, false, SkyAmbientMode.Dynamic);

        }
    }
}