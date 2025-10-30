namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("GPUDriven")]
    public class GPUDrivenFeature : ScriptableRendererFeature
    {
        private VisibilityBufferPass visibilityBufferPass;

        public override void Create()
        {
            visibilityBufferPass = new VisibilityBufferPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(visibilityBufferPass);
        }
    }
}