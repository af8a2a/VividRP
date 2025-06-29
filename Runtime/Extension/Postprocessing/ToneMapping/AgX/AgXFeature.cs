namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("AgX Tonemapping")]
    public class AgXFeature : ScriptableRendererFeature
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        private AgXPass _pass;

        public override void Create()
        {
            _pass = new AgXPass()
            {
                renderPassEvent = renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            _pass.Setup();
            renderer.EnqueuePass(_pass);
        }
    }
}