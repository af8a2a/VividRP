namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Lighting")]
    public class VividLightingFeature : ScriptableRendererFeature
    {
        private CharacterLightingPass _pass;
        private VividDeferredLighting _DeferredLights;
        private ReferencedPathTracingPass _referencedPathTracing;
        private RayTracingGBufferPass _referencedPathTracingGBufferPass;

        public override void Create()
        {
            _pass = new CharacterLightingPass();
            _DeferredLights = new VividDeferredLighting();
            _referencedPathTracing = new ReferencedPathTracingPass();
            _referencedPathTracingGBufferPass = new RayTracingGBufferPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var pathTracing =
                VolumeManager.instance.stack.GetComponent<GlobalIllumination>().technique.value is GlobalIlluminationTechnique.ReferencedPathTracing;
            if (pathTracing)
            {
                renderer.EnqueuePass(_referencedPathTracingGBufferPass);
                renderer.EnqueuePass(_referencedPathTracing);
            }
            else
            {
                renderer.EnqueuePass(_pass);
                renderer.EnqueuePass(_DeferredLights);
            }
        }
    }
}