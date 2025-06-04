using Features.Shadow.ShadowCommon;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow
{
    public static class ShadowUtilsExt
    {
        internal static void RenderShadowSliceNoOffset(RasterCommandBuffer cmd,
            ref ShadowSliceData shadowSliceData, ref RendererList shadowRendererList,
            Matrix4x4 proj, Matrix4x4 view)
        {
            cmd.SetGlobalDepthBias(1.0f,
                2.5f); // these values match HDRP defaults (see https://github.com/Unity-Technologies/Graphics/blob/9544b8ed2f98c62803d285096c91b44e9d8cbc47/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAtlas.cs#L197 )

            cmd.SetViewProjectionMatrices(view, proj);
            if (shadowRendererList.isValid)
                cmd.DrawRendererList(shadowRendererList);

            cmd.DisableScissorRect();
            cmd.SetGlobalDepthBias(0.0f, 0.0f); // Restore previous depth bias values
        }


        internal static void SetSoftShadowFilterShaderKeywords(RasterCommandBuffer cmd, Shadows shadows)
        {
            if (shadows.shadowAlgo.value is ShadowCommon.ShadowFilter.PCSS)
            {
                CoreUtils.SetKeyword(cmd, "PCSS", true);
            }
        }
    }
}