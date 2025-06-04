using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class BackgroundLightClassifyPass : ScriptableRenderPass
    {
        private List<ShaderTagId> m_ShaderTagIdList = new();

        private FilteringSettings m_filter;

        public BackgroundLightClassifyPass(string[] PassNames)
        {
            RenderQueueRange queue = RenderQueueRange.opaque;
            m_filter = new FilteringSettings(queue);
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            if (PassNames != null && PassNames.Length > 0)
            {
                foreach (var passName in PassNames)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }

            m_ShaderTagIdList.Add(new ShaderTagId("CharacterMask"));
        }


        class DrawMaskPassData
        {
            internal RendererListHandle rendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecuteMaskPass(DrawMaskPassData data, RasterGraphContext context)
        {
            // // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
            context.cmd.ClearRenderTarget(false, true, Color.clear);
            
            context.cmd.DrawRendererList(data.rendererListHandle);
        }


        // Sample utility method that showcases how to create a renderer list via the RenderGraph API
        private void InitRendererLists(ContextContainer frameData, ref DrawMaskPassData passData, RenderGraph renderGraph)
        {
            // Access the relevant frame data from the Universal Render Pipeline
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.opaque;

            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList[0], universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DrawMaskPassData>("BackgroundLightScatter Classify", out var passData))
            {
                var setting = VolumeManager.instance.stack.GetComponent<BackgroundLightScatter>();
                if (setting == null || !setting.IsActive())
                {
                    return;
                }

                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Fill up the passData with the data needed by the pass
                InitRendererLists(frameData, ref passData, renderGraph);

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                desc.format = GraphicsFormat.R16_SFloat;
                desc.name = "Character Mask";
                desc.msaaSamples = MSAASamples.None;
                desc.depthBufferBits = 0;
                var maskTexture = renderGraph.CreateTexture(desc);
                //
                // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                builder.UseRendererList(passData.rendererListHandle);
                
                builder.SetRenderAttachment(maskTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);
                builder.SetGlobalTextureAfterPass(maskTexture, Shader.PropertyToID("_CharacterMaskTexture"));
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<DrawMaskPassData>(ExecuteMaskPass);
            }
        }
    }
}