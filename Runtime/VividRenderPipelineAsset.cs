using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime
{
    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>
    {
        public RenderGraphAsset RenderGraphAsset;

        protected override RenderPipeline CreatePipeline()
        {
            return new VividRenderPipeline(this);
        }
    }
}
