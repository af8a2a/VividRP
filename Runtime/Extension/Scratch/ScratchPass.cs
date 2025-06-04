using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ScratchPass : ScriptableRenderPass
    {
        public ScratchPass(RenderPassEvent evt)
        {
            profilingSampler = new ProfilingSampler(nameof(ScratchPass));
            renderPassEvent = evt;
        }

        class PassData
        {
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            using (var builder =
                   renderGraph.AddUnsafePass<PassData>("ScratchPass", out var passData, profilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                {

                    var cmd =CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd) ;
                    UIScratchEffectSystem.instance.RenderMask(cmd);
                });
            }
        }
    }
}