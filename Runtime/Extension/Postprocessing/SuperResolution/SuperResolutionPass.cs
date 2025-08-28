using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace UnityEngine.Rendering.Universal
{
    public class SuperResolutionPass
    {
        private STPUpscaler _stpUpscaler = new STPUpscaler();
        private HDRPTemporalAAPass _taauUpscaler = new HDRPTemporalAAPass();

        private URPTemporalAAPass _urpTemporalAAPass = new URPTemporalAAPass();


        class BlitPassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
        }

        
        static ProfilingSampler _bilinearSampler = new ProfilingSampler("Bilinear Upscaler"); 

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();


            bool useTemporalAA = cameraData.IsTemporalAAEnabled();

            if (cameraData.imageScalingMode is not ImageScalingMode.Upscaling)
            {
                return source;
            }

            // STP is only enabled when TAA is enabled and all of its runtime requirements are met.
            // Using IsSTPRequested() vs IsSTPEnabled() for perf reason here, as we already know TAA status
            TextureHandle dest;
            if (cameraData.upscalingTechnique is UpscalingTechnique.STP)
            {
                dest = _stpUpscaler.Render(renderGraph, frameData, source);
            }
            else if (cameraData.upscalingTechnique is UpscalingTechnique.TAAU)
            {
                dest = _taauUpscaler.Render(renderGraph, frameData, source);
            }
            // else if (useTemporalAA)
            // {
            //     dest = _urpTemporalAAPass.Render(renderGraph, frameData, source);
            // }
            else
            {
                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("Bilinear Upscale", out var passData, _bilinearSampler))
                {
                    dest = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                    {
                        enableRandomWrite = true,
                        format = cameraData.cameraTargetDescriptor.graphicsFormat,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "Bilinear Upscaled"
                    });


                    passData.source = source;
                    passData.destination = dest;
                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0);

                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc<BlitPassData>((data, ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, data.source, Vector2.one, 0, true);
                    });
                }
            }
            
            
            SuperResolutionUtil.UpdateCameraResolution(renderGraph, cameraData, new Vector2Int(cameraData.pixelWidth, cameraData.pixelHeight));
            return dest;
        }
    }
}