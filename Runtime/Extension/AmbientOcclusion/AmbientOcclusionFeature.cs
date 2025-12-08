namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Ambient Occlusion")]
    public class AmbientOcclusionFeature : ScriptableRendererFeature
    {
        XeGTAOPass pass;

        public override void Create()
        {
            pass = new XeGTAOPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }
    }
}