using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("HBAO")]
    public class HBAOFeature : ScriptableRendererFeature
    {
        private HBAOPass pass;

        public override void Create()
        {
            pass = new HBAOPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            pass.Setup();

            renderer.EnqueuePass(pass);
        }
    }
}