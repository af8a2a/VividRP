using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class SkyPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            //
            // SkySystem.instance.UpdateCurrentSky();
            // SkySystem.instance.UpdateEnvironment(renderGraph, frameData, lightData, false, false, false, SkyAmbientMode.Dynamic);
            
            Render(renderGraph, frameData, resourceData.activeColorTexture, resourceData.activeDepthTexture);
        }

        public SkyPass()
        {
            //now overlay URP Skybox
            //not ready to disable URP Skybox...
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        private class ScrTrianglePassData
        {
            internal TextureHandle colorTarget;
            internal TextureHandle depthTarget;
        }

        
        /// <summary>
        /// Use a screen triangle to render sky.
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="frameData"></param>
        /// <param name="colorTarget"></param>
        /// <param name="depthTarget"></param>
        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle colorTarget, TextureHandle depthTarget)
        {
            using (var builder = renderGraph.AddUnsafePass<ScrTrianglePassData>("Render Sky", out var passData, base.profilingSampler))
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                passData.colorTarget = colorTarget;
                passData.depthTarget = depthTarget;

                builder.UseTexture(colorTarget, AccessFlags.Write);
                builder.UseTexture(depthTarget, AccessFlags.Write);
                
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ScrTrianglePassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmd.SetRenderTarget(passData.colorTarget, passData.depthTarget);
                    SkySystem.instance.RenderSky(cmd, cameraData, lightData);
                });
            }
        }

    }
}