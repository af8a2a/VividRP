using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>
    {
        private const string DefaultShaderName = "VividRP/Material/StandardLit";

        public RenderGraphData RenderGraphAsset;

        [SerializeField]
        private bool m_EnableAsyncCompute = true;

        [SerializeField]
        private bool m_EnableGPUDriven;

        [SerializeField]
        private bool m_EnableGPUDrivenDebugOverlay;

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

        public bool EnableGPUDrivenDebugOverlay
        {
            get => m_EnableGPUDrivenDebugOverlay;
            set => m_EnableGPUDrivenDebugOverlay = value;
        }

        public override Shader defaultShader => Shader.Find(DefaultShaderName);

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            VividRenderPipelineGlobalSettings.Ensure();
#endif
            return new VividRenderPipeline(this);
        }
    }
}
