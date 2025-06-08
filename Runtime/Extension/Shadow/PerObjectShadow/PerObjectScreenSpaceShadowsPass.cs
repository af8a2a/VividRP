using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.PerObjectShadow
{
    internal class PerObjectScreenSpaceShadowsPass : ScriptableRenderPass
    {
        private static class PerObjectShadowProjectorConstant
        {
            public static int _PerObjectWorldToShadow;
            public static int _PerObjectUVScaleOffset;
            public static int _PerObjectShadowParams;
            public static int _PerObjectShadowScaledScreenParams;
            public static int _PerObjectScreenSpaceShadowmapTexture;
        }
        // Profiling tag
        private static string m_ProfilerTag = "PerObjectScreenSpaceShadows";
        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        // Public Variables

        // Private Variables
        private RTHandle m_ScreenSpaceShadowTexture;
        private ObjectShadowDrawSystem m_DrawSystem;
        private ShaderTagId m_ShaderTagId;
        private Shadows m_volumeSettings;

        // Constants


        // Statics


        internal PerObjectScreenSpaceShadowsPass(ObjectShadowDrawSystem drawSystem)
        {
            m_DrawSystem = drawSystem;
            m_ShaderTagId = new ShaderTagId(PerObjectShadowShaderPassNames.PerObjectScreenSpaceShadow);

            PerObjectShadowProjectorConstant._PerObjectWorldToShadow = Shader.PropertyToID("_PerObjectWorldToShadow");
            PerObjectShadowProjectorConstant._PerObjectUVScaleOffset = Shader.PropertyToID("_PerObjectUVScaleOffset");
            PerObjectShadowProjectorConstant._PerObjectShadowParams = Shader.PropertyToID("_PerObjectShadowParams");
            PerObjectShadowProjectorConstant._PerObjectShadowScaledScreenParams = Shader.PropertyToID("_PerObjectShadowScaledScreenParams");
            PerObjectShadowProjectorConstant._PerObjectScreenSpaceShadowmapTexture = Shader.PropertyToID("_PerObjectScreenSpaceShadowmapTexture");
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_ScreenSpaceShadowTexture?.Release();
        }

        internal bool Setup(Shadows volumeSettings)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);

            m_volumeSettings = volumeSettings;

            return true;
        }


        /// <summary>
        /// Clear Keyword.
        /// </summary>
        /// <param name="cmd"></param>
        public void ClearRenderingState(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd,"_PEROBJECT_SCREEN_SPACE_SHADOW",false);

            // cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, false);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd,"_PEROBJECT_SCREEN_SPACE_SHADOW",false);

            // cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, false);
        }
        private class PassData
        {
            internal RendererListHandle rendererListHandle;
            internal ObjectShadowDrawSystem drawSystem;
            internal Vector2 rtSize;
            internal Vector4 perObjectShadowParams;
            internal int historyFramCount;
        }

        private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph)
        {
            // Access the relevant frame data from the Universal Render Pipeline
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.opaque;
            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange);
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagId, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Update keywords and other shader params
            int historyFramCount = 0;
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem != null)
                historyFramCount = historyRTSystem.historyFrameCount;

            var desc = cameraData.cameraTargetDescriptor;
            int downSampleScale = 0;
            desc.width = desc.width >> downSampleScale;
            desc.height = desc.height >> downSampleScale;
            desc.useMipMap = false;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;

            // Create RenderGraphTexture, will clear before render this pass.
            var screenSpaceShadowMapTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, 
                "_PerObjectScreenSpaceShadowmapTexture", true, Color.white, FilterMode.Point, TextureWrapMode.Clamp);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PerObject ScreenSpace Shadows", out var passData, m_ProfilingSampler))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                int shadowLightIndex = lightData.mainLightIndex;
                if (shadowLightIndex == -1)
                    return;

                Light shadowLight = lightData.visibleLights[shadowLightIndex].light;
                if (shadowLight.shadows == LightShadows.None)
                    return;

                // Params
                float softShadowQuality = ShadowUtils.SoftShadowQualityToShaderProperty(shadowLight, true);
                float shadowStrength = 1.0f;
                passData.perObjectShadowParams = new Vector4(softShadowQuality, shadowStrength, downSampleScale, m_volumeSettings.perObjectShadowPenumbra.value * 0.25f);
                passData.drawSystem = m_DrawSystem;
                passData.rtSize = new Vector2(desc.width, desc.height);

                InitRendererLists(frameData, ref passData, renderGraph);

                // Shader keyword changes are considered as global state modifications
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                if (frameData.Contains<PerObjectShadowCasterPass.PerObjectShadowMapRefData>())
                {
                    var shadowMapRefData = frameData.Get<PerObjectShadowCasterPass.PerObjectShadowMapRefData>();
                    builder.UseTexture(shadowMapRefData.perObjectShadowMap);
                }

                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture);

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(screenSpaceShadowMapTexture, 0, AccessFlags.Write);
                if (screenSpaceShadowMapTexture.IsValid())
                    builder.SetGlobalTextureAfterPass(screenSpaceShadowMapTexture, PerObjectShadowProjectorConstant._PerObjectScreenSpaceShadowmapTexture);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Draw System
                    data.drawSystem?.Execute(context.cmd, data.rtSize);

                    context.cmd.SetGlobalInt("_CamHistoryFrameCount", historyFramCount);
                    context.cmd.SetGlobalVector(PerObjectShadowProjectorConstant._PerObjectShadowParams, data.perObjectShadowParams);
                    // context.cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, true);
                    CoreUtils.SetKeyword(context.cmd, "_PEROBJECT_SCREEN_SPACE_SHADOW", true);
                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }

        }


    }

}