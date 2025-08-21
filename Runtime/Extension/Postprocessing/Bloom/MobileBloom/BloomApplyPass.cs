using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    
    [PostProcessOrder(PostProcessExecutionOrder.ApplyBloom)]
    public class BloomApplyPass 
    {
        private Material material;
        class PassData
        {

            internal Material material;

            internal TextureHandle sourceTexture;
            internal TextureHandle bloomTexture;
            
        }

        public BloomApplyPass()
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<BloomRuntimeShader>();
            material = CoreUtils.CreateEngineMaterial(runtimeShader.bloomShader);
        }

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Mobile Bloom Apply", out var passData))
            {
                passData.sourceTexture = source;
                passData.bloomTexture = frameData.Get<UniversalResourceData>().bloomTexture;
                builder.SetRenderAttachment(source, 0);
                builder.UseTexture(passData.bloomTexture);
                builder.SetRenderFunc<PassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    
                    Blitter.BlitTexture(cmd, Vector2.one, data.material, 7);
                });
            }

            return source;
        }
    }
}