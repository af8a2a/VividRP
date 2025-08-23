using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
#if false
    [DisallowMultipleRendererFeature]
    public class DepthOfFieldFeature : ScriptableRendererFeature
    {
        DiaphragmDoFPass diaphragmDoFPass;

        public override void Create()
        {
            diaphragmDoFPass = new DiaphragmDoFPass();
            diaphragmDoFPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {


            renderer.EnqueuePass(diaphragmDoFPass);
        }
    }
#endif
}