#if VIVID_DEPRECATED
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ExposureFeature : ScriptableRendererFeature
    {
        ExposurePass exposurePass;
        ExposureSetupPass exposureSetupPass;
        public override void Create()
        {
            exposurePass = new ExposurePass();
            exposureSetupPass = new ExposureSetupPass();
        }
        

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(exposureSetupPass);
            renderer.EnqueuePass(exposurePass);
        }
    }
}
#endif