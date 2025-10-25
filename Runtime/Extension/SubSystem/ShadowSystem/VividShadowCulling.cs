using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    internal class VividShadowCulling
    {
        static readonly ProfilingSampler ShadowCulling = new ProfilingSampler(nameof(ShadowCulling));


        public static void CullShadowCasters(ref ScriptableRenderContext context,
            ref CullingResults cullResults)
        {
            ShadowCastersCullingInfos cullingInfos = default;

            
            // ComputeCullingSplits()
            context.CullShadowCasters(cullResults, cullingInfos);
        }




        public static unsafe void ComputeCullingSplits()
        {
            
        }
    }
}