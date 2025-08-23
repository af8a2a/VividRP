using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class StopNaNPass
    {
        Material material;

        private class StopNaNsPassData
        {
            internal TextureHandle stopNaNTarget;
            internal TextureHandle sourceTexture;
            internal Material stopNaN;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.stopNaNShader);
            }


            var cameraData = frameData.Get<UniversalCameraData>();

            if (!cameraData.isStopNaNEnabled)
            {
                return source;
            }

            var stopNaNTarget = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                format = renderGraph.GetTextureDesc(source).format,
                enableRandomWrite = true,
                name = "StopNaN",
            });

            

            using (var builder = renderGraph.AddRasterRenderPass<StopNaNsPassData>("Stop NaN", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_StopNaNs)))
            {
                passData.stopNaNTarget = stopNaNTarget;
                builder.SetRenderAttachment(stopNaNTarget, 0);
                passData.sourceTexture = source;
                builder.UseTexture(passData.sourceTexture, AccessFlags.Read);
                passData.stopNaN = material;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (StopNaNsPassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;

                    Blitter.BlitTexture(cmd, data.sourceTexture, Vector2.one, data.stopNaN, 0);
                });
            }
            return stopNaNTarget;
        }
    }
}