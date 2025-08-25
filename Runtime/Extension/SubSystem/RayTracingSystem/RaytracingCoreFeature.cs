namespace UnityEngine.Rendering.Universal
{
    public class RaytracingCoreFeature : ScriptableRendererFeature
    {
        RaytracingCorePass raytracingCorePass;
        public override void Create()
        {
            raytracingCorePass = new RaytracingCorePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            raytracingCorePass.Setup();
            renderer.EnqueuePass(raytracingCorePass);
        }
    }
}