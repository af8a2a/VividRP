using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class MobileBloomFeature:ScriptableRendererFeature
    {
        private MobileBloomPass pass;
        public override void Create()
        {
            pass = new MobileBloomPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }
    }
}