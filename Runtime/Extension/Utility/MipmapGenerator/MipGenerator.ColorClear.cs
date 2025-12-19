using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class MipGenerator
    {
        public void Clear(ComputeCommandBuffer cmd, TextureHandle source, int width, int height)
        {
            cmd.SetComputeTextureParam(m_ColorClearShader, 0, "_Texture", source);

            var tx = CoreUtils.DivRoundUp(width, 8);
            var ty = CoreUtils.DivRoundUp(height, 8);

            cmd.DispatchCompute(m_ColorClearShader, 0, tx, ty, 1);
        }
    }
}