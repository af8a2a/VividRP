using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class CorePostProcessingFeature : ScriptableRendererFeature
    {
        CorePostProcessPass _corePostProcessPass = new CorePostProcessPass();

        public override void Create()
        {
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            
            renderer.EnqueuePass(_corePostProcessPass);
        }
    }
}