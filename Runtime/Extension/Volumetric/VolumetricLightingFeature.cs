using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("VolumetricLightingFeature")]

    public class VolumetricLightingFeature: ScriptableRendererFeature
    {
        private VolumetricLightPass _pass;
        public override void Create()
        {
            _pass = new VolumetricLightPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pass);
        }
    }
}