using Features.Shadow.ShadowCommon;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.UberScreenSpaceShadow
{
    internal class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        internal ScreenSpaceShadowsPostPass()
        {
            profilingSampler = new ProfilingSampler("Set Screen Space Shadow Keywords");
            shadows = VolumeManager.instance.stack.GetComponent<Shadows>();
        }

        private bool ShadowScatterEnable = true;
        private Shadows shadows;
        
        internal static class ShaderIDs
        {
            public static int _ShadowScatterEnable = Shader.PropertyToID("_ShadowScatterEnable");
            public static int _DirLightShadowScatterPenumbraOnly = Shader.PropertyToID("_DirLightShadowScatterPenumbraOnly");
            public static int _DirShadowRampTexture = Shader.PropertyToID("_DirShadowRampTexture");

        }
    
        

        private static void ExecutePass(RasterCommandBuffer cmd,PassData passData)
        {
            UniversalShadowData shadowData = passData.shadowData;
            
            int cascadesCount = shadowData.mainLightShadowCascadesCount;
            bool mainLightShadows = shadowData.supportsMainLightShadows;
            bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
            bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

            // Before transparent object pass, force to disable screen space shadow of main light
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowScreen, false);

            // then enable main light shadows with or without cascades
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, receiveShadowsNoCascade);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, receiveShadowsCascades);

            cmd.SetGlobalFloat(ShaderIDs._ShadowScatterEnable, passData.pass.ShadowScatterEnable ? 1 : 0);
            cmd.SetGlobalFloat(ShaderIDs._DirLightShadowScatterPenumbraOnly, 1);
            Shader.SetGlobalTexture(ShaderIDs._DirShadowRampTexture, passData.pass.shadows.shadowRampTex.value);   
            
        }


        internal class PassData
        {
            internal ScreenSpaceShadowsPostPass pass;
            internal UniversalShadowData shadowData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                TextureHandle color = resourceData.activeColorTexture;
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                passData.shadowData = frameData.Get<UniversalShadowData>();
                passData.pass = this;

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => { ExecutePass(rgContext.cmd, data); });
            }
        }
    }
}