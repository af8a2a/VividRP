namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Fluid")]
    public class FluidSimulation : ScriptableRendererFeature
    {
        NSFluidSimulation nsFluidSimulation = new NSFluidSimulation();

        public override void Create()
        {
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(nsFluidSimulation);
        }
    }
}