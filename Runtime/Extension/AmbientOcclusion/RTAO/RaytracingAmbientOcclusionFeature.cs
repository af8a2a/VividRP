namespace UnityEngine.Rendering.Universal
{
    public class RaytracingAmbientOcclusionFeature:ScriptableRendererFeature
    {
        RaytracingAmbientOcclusionPass pass;
        public override void Create()
        {
            pass = new RaytracingAmbientOcclusionPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            pass.Setup();
            renderer.EnqueuePass(pass);
        }
    }
}