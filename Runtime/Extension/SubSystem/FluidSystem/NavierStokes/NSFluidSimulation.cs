using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// only for SRP injection
    /// </summary>
    public class NSFluidSimulation : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var planes = NSFluidPlaneManager.instance.GetFluidPlanes();

            foreach (var plane in planes)
            {
                var nsResult = NSFluidEvaluator.instance.EvaluateNavierStokesFluid(renderGraph, plane);
                var nsPlane = renderGraph.ImportTexture(plane.nsFluidTexture);
                MipGenerator.instance.CopyColor(renderGraph, frameData, nsResult, nsPlane);
                plane.ApplyFluid();
            }
        }
    }
}