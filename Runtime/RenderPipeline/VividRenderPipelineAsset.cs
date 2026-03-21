using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>
    {
        public RenderGraphData RenderGraphAsset;

        [SerializeField]
        private bool m_EnableAsyncCompute = true;

        [SerializeField]
        private bool m_EnableGPUDriven;

        [SerializeField]
        private bool m_EnableSRPBatcher = true;

        public bool EnableAsyncCompute
        {
            get => m_EnableAsyncCompute;
            set => m_EnableAsyncCompute = value;
        }

        public bool EnableSRPBatcher
        {
            get => m_EnableSRPBatcher;
            set => m_EnableSRPBatcher = value;
        }

        public bool EnableGPUDriven
        {
            get => m_EnableGPUDriven;
            set => m_EnableGPUDriven = value;
        }

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            VividRenderPipelineGlobalSettings.Ensure();
#endif
            return new VividRenderPipeline(this);
        }
    }
}
