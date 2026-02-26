using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderPipeline
{
    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>
    {
        public RenderGraphAsset RenderGraphAsset;

        
        
        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
            return new VividRenderPipeline(this);
        }
    }
}
