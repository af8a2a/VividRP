using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Generates an in-place depth pyramid 
    /// TODO: Mip-mapping depth is problematic for precision at lower mips, generate a packed atlas instead
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass
    {
        private ComputeShader m_Shader;
        private int m_DepthDownsampleKernel;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="computeShader"></param>
        public DepthPyramidPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler("DepthPyramid Prepass");
            renderPassEvent = evt;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            resourceData.cameraDepthPyramidTexture = MipGenerator.RenderMinDepthPyramidHDRP(renderGraph, frameData);
        }

    }
}