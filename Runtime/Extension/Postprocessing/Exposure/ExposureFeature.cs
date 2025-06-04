using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ExposureFeature : ScriptableRendererFeature
    {
        ExposurePass exposurePass;

        public override void Create()
        {
            exposurePass = new ExposurePass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }
        

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(exposurePass);
        }
    }
}