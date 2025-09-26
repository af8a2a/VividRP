namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Lighting")]
    public class VividLightingFeature:ScriptableRendererFeature
    {
        private CharacterLightingPass _pass;
        private  VividDeferredLighting _DeferredLights;

        public override void Create()
        {
            _pass = new CharacterLightingPass();
            _DeferredLights = new VividDeferredLighting();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pass);
            renderer.EnqueuePass(_DeferredLights);
        }

        
    }
}