namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Lighting")]
    public class VividLightingFeature : ScriptableRendererFeature
    {
        private CharacterLightingPass _pass;
        private VividDeferredLighting _DeferredLights;
        private ReferencedPathTracingPass _referencedPathTracing;

        public override void Create()
        {
            _pass = new CharacterLightingPass();
            _DeferredLights = new VividDeferredLighting();
            _referencedPathTracing = new ReferencedPathTracingPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var pathTracing =
                VolumeManager.instance.stack.GetComponent<GlobalIllumination>().technique.value is GlobalIlluminationTechnique.ReferencedPathTracing;
            if (pathTracing)
            {
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