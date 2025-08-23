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

            internal Vector4 bloomParams;
            internal TextureHandle sourceTexture;
            internal TextureHandle bloomTexture;
            internal TextureHandle destinationTexture;
        }

        private static int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");
        private static int _Bloom_Custom_Params = Shader.PropertyToID("_Bloom_Custom_Params");

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<BloomRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.bloomShader);
            }


            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Mobile Bloom Apply", out var passData))
            {
                passData.sourceTexture = source;
                passData.bloomTexture = frameData.Get<UniversalResourceData>().bloomTexture;
                passData.material = material;
                var setting = VolumeManager.instance.stack.GetComponent<MobileBloom>();

                var bloomParams = new Vector4(setting.threshold.value, setting.lumRangeScale.value, setting.preFilterScale.value, setting.intensity.value);

                //unity d3d12 doesn't support framebuffer fetch yet ?
                passData.destinationTexture = renderGraph.CreateTexture(new TextureDesc(Vector2.one)
                {
                    format = UniversalRenderPipeline.asset.colorFormat,
                    name = "Mobile Bloom Apply",
                });
                builder.AllowGlobalStateModification(true);

                builder.UseTexture(passData.sourceTexture);
                builder.UseTexture(passData.bloomTexture);

                builder.SetRenderAttachment(passData.destinationTexture, 0);
                builder.SetRenderFunc<PassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;

                    material.SetTexture(_Bloom_Texture, passData.bloomTexture);

                    material.SetVector(_Bloom_Custom_Params, bloomParams);
                    Blitter.BlitTexture(cmd, Vector2.one, data.material, 7);
                });
                return passData.destinationTexture;
            }
        }
    }
}