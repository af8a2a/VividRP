using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class OcclusionOutlineFeature : ScriptableRendererFeature
    {
        class CustomRenderPass : ScriptableRenderPass
        {
            public CustomRenderPass()
            {
                profilingSampler = new ProfilingSampler(nameof(OcclusionOutlineFeature));
            }

            class PassData
            {
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                using (var builder =
                       renderGraph.AddRasterRenderPass<PassData>("Occlusion Outline", out var passData, profilingSampler))
                {
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                    {
                        var cmd = rgContext.cmd;
                        OcclusionOutlineDrawSystem.Instance.Render(cmd);
                    });
                }
            }
        }

        CustomRenderPass m_ScriptablePass;

        /// <inheritdoc/>
        public override void Create()
        {
            m_ScriptablePass = new CustomRenderPass();

            // Configures where the render pass should be injected.
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
}