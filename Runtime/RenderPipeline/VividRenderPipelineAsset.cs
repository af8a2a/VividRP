using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>
    {
        public RenderGraphData RenderGraphAsset;

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            VividRenderPipelineGlobalSettings.Ensure();
#endif
            return new VividRenderPipeline(this);
        }
    }
}
