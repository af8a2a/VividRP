using System.Collections.Generic;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class CharacterLightingPass : ScriptableRenderPass
    {
        // Public Variables

        // Private Variables
        private FilteringSettings m_FilteringSettings;
        private RenderStateBlock m_RenderStateBlock;

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>
        {
            new ShaderTagId("CharacterForward"),
            new ShaderTagId("CharacterTransparent"),
            new ShaderTagId("CharacterOutline")
        };
        // Constants

        // Statics

        public CharacterLightingPass()
        {
            base.profilingSampler = new ProfilingSampler(nameof(CharacterLightingPass));
            base.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, -1);

            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        internal class PassData
        {
            internal TextureHandle albedoHdl;
            internal TextureHandle depthHdl;

            // Lighting Texture
            internal TextureHandle SSShadowsTexture;

            // Sky Ambient
            internal BufferHandle ambientProbe;

            // Sky Reflect
            internal TextureHandle reflectProbe;

            internal UniversalCameraData cameraData;

            internal uint batchLayerMask;
            internal RendererListHandle rendererListBase;
            internal RendererListHandle rendererListTrans;
            internal RendererListHandle rendererListOutline;
            
            // PreIntergratedFGD
            internal TextureHandle FGD_GGXAndDisneyDiffuse;

        }

        internal void InitRendererLists(
            RenderGraph renderGraph,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            ref PassData passData)
        {
            ref Camera camera = ref cameraData.camera;
            var sortFlags = cameraData.defaultOpaqueSortFlags;
            if (cameraData.renderer.useDepthPriming && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
                sortFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue | SortingCriteria.OptimizeStateChanges;

            var filterSettings = m_FilteringSettings;
            filterSettings.batchLayerMask = passData.batchLayerMask;
            filterSettings.layerMask = -1;
#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif
            DrawingSettings drawSettingsBase = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList[0], renderingData, cameraData, lightData, sortFlags);
            DrawingSettings drawSettingsTrans = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList[1], renderingData, cameraData, lightData, sortFlags);
            DrawingSettings drawSettingsOutline = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList[2], renderingData, cameraData, lightData, sortFlags);

            RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettingsBase, filterSettings,
                m_RenderStateBlock, ref passData.rendererListBase);
            RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettingsTrans, filterSettings,
                m_RenderStateBlock, ref passData.rendererListTrans);
            RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettingsOutline, filterSettings,
                m_RenderStateBlock, ref passData.rendererListOutline);
        }


        static void ExecutePass(RasterCommandBuffer cmd, PassData data, bool yFlip)
        {
            // scaleBias.x = flipSign
            // scaleBias.y = scale
            // scaleBias.z = bias
            // scaleBias.w = unused
            float flipSign = yFlip ? -1.0f : 1.0f;
            Vector4 scaleBias = (flipSign < 0.0f)
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);
            cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, 0);

            cmd.SetGlobalTexture(ShaderConstants._ScreenSpaceShadowmapTexture, data.SSShadowsTexture);

            cmd.SetGlobalBuffer("_AmbientProbeData", data.ambientProbe);
            cmd.SetGlobalTexture("_SkyTexture", data.reflectProbe);


            cmd.DrawRendererList(data.rendererListBase);
            cmd.DrawRendererList(data.rendererListTrans);
            cmd.DrawRendererList(data.rendererListOutline);
        }
        

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Character Lighting", out var passData, base.profilingSampler))
            {
                // Access resources
                
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                passData.cameraData = cameraData;
                passData.batchLayerMask = uint.MaxValue;

                passData.SSShadowsTexture = resourceData.screenSpaceShadowsTexture;

                // Sky Environment
                passData.ambientProbe = resourceData.skyAmbientProbe;
                passData.reflectProbe = resourceData.skyReflectionProbe;
                passData.FGD_GGXAndDisneyDiffuse =
                    PreIntegratedFGD.instance.ImportToRenderGraph(renderGraph, PreIntegratedFGD.FGDIndex.FGD_GGXAndDisneyDiffuse);

                TextureHandle colorTarget = resourceData.activeColorTexture;

                TextureHandle depthTarget = resourceData.activeDepthTexture;
                InitRendererLists(renderGraph, renderingData, cameraData, lightData, ref passData);

                TextureHandle mainShadowsTexture = resourceData.directionalShadowsTexture;
                TextureHandle additionalShadowsTexture = resourceData.additionalShadowsTexture;


                builder.UseBuffer(passData.ambientProbe);
                builder.UseTexture(passData.reflectProbe);
                builder.UseTexture(passData.FGD_GGXAndDisneyDiffuse, AccessFlags.Read);

                if (colorTarget.IsValid())
                {
                    passData.albedoHdl = colorTarget;
                    builder.SetRenderAttachment(colorTarget, 0, AccessFlags.Write);
                }

                if (depthTarget.IsValid())
                {
                    passData.depthHdl = depthTarget;
                    builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Write);
                }

                if (mainShadowsTexture.IsValid())
                    builder.UseTexture(mainShadowsTexture, AccessFlags.Read);
                if (additionalShadowsTexture.IsValid())
                    builder.UseTexture(additionalShadowsTexture, AccessFlags.Read);
                if (passData.SSShadowsTexture.IsValid())
                    builder.UseTexture(passData.SSShadowsTexture, AccessFlags.Read);

                builder.UseRendererList(passData.rendererListBase);
                builder.UseRendererList(passData.rendererListTrans);
                builder.UseRendererList(passData.rendererListOutline);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    bool yFlip = data.cameraData.IsRenderTargetProjectionMatrixFlipped(data.albedoHdl, data.depthHdl);
                
                    PreIntegratedFGD.instance.Bind(context.cmd,PreIntegratedFGD.FGDIndex.FGD_GGXAndDisneyDiffuse,data.FGD_GGXAndDisneyDiffuse);
                    ExecutePass(context.cmd, data, yFlip);
                });
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        static class ShaderConstants
        {
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
        }
    }
}