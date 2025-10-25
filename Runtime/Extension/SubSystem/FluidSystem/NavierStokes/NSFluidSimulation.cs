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
                var nsResult = TextureHandle.nullHandle;
                if (plane.useMobileNS)
                {
                    nsResult = NSFluidEvaluator.instance.EvaluateNavierStokesFluidMobile(renderGraph, plane);
                }
                else
                {
                    nsResult = NSFluidEvaluator.instance.EvaluateNavierStokesFluid(renderGraph, plane);
                }

                var nsPlane = renderGraph.ImportTexture(plane.nsFluidTexture);

                var normal = NSFluidEvaluator.instance.GenerateNormalTexture(renderGraph, nsResult);

                MipGenerator.instance.CopyColor(renderGraph,  normal, nsPlane);
                plane.ApplyFluid();
            }
        }
    }
}