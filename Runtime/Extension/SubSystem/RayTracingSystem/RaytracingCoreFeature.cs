namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Raytracing Core")]
    public class RaytracingCoreFeature : ScriptableRendererFeature
    {
        RaytracingCorePass raytracingCorePass;
        public override void Create()
        {
            raytracingCorePass = new RaytracingCorePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(raytracingCorePass);
        }
    }
}