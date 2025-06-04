using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class BackgroundLightScatterPass : ScriptableRenderPass
    {
        private Material _material;

        private Material scatterMaterial => _material ??= new Material(Shader.Find("PostProcessing/BackgroundLighting"));


        class PassData
        {
            internal int iterations;

            internal Material material;

            internal TextureHandle sourceTexture;
            internal TextureHandle OutputTexture;


            internal TextureHandle preFilterTexture;
            internal TextureHandle preFilterBlurTexture;

            internal TextureHandle[] MipUpTexture;
            internal TextureHandle[] MipDownTexture;
        }


        class ShaderID
        {
            public static readonly int _Params = Shader.PropertyToID("_Params");
            public static readonly int _ColorTint = Shader.PropertyToID("_ColorTint");
            public static readonly int _BlurScaler = Shader.PropertyToID("_BlurScaler");
            public static readonly int _BlurCompositeWeight = Shader.PropertyToID("_BlurCompositeWeight");
            public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");

            public static int[] _MipUp = new int[16];
            public static int[] _MipDown = new int[16];
        }

        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            CoreUtils.SetKeyword(cmd, "_USE_RGBM", true);
            // preFilter
            Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.preFilterTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 0);

            // preFilterBlur
            cmd.SetGlobalVector(ShaderID._BlurScaler, new Vector4(1, 0, 0, 0));
            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 2);
            cmd.SetGlobalVector(ShaderID._BlurScaler, new Vector4(0, 1, 0, 0));
            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 2);

            TextureHandle last = data.preFilterTexture;

            for (var level = 0; level < data.iterations; level++)
            {
                Blitter.BlitCameraTexture(cmd, last, data.MipDownTexture[level], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                    data.material,
                    1);
                last = data.MipDownTexture[level];
            }

            // blur mips
            for (var level = 0; level < data.iterations; level++)
            {
                cmd.SetGlobalVector(ShaderID._BlurScaler, new Vector4(1, 0, 0, 0));
                Blitter.BlitCameraTexture(cmd, data.MipDownTexture[level], data.MipUpTexture[level], RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store, data.material, 3 + level);

                cmd.SetGlobalVector(ShaderID._BlurScaler, new Vector4(0, 1, 0, 0));
                Blitter.BlitCameraTexture(cmd, data.MipUpTexture[level], data.MipDownTexture[level], RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store, data.material, 3 + level);
            }

            // upsample once
            for (var level = 0; level < data.iterations; level++)
            {
                data.material.SetTexture(ShaderID._MipDown[level], data.MipDownTexture[level]);
            }

            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 6);


            data.material.SetTexture(ShaderID._Bloom_Texture, data.preFilterBlurTexture);

            Blitter.BlitCameraTexture(cmd,
                data.sourceTexture,
                data.OutputTexture,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store,
                data.material,
                7);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("Background Light Scatter", out var data))
            {
                var setting = VolumeManager.instance.stack.GetComponent<BackgroundLightScatter>();
                if (setting == null || !setting.IsActive())
                {
                    return;
                }

                var bloomParams = new Vector4(setting.threshold.value, setting.lumRangeScale.value, setting.preFilterScale.value, setting.intensity.value);

                scatterMaterial.SetVector(ShaderID._Params, bloomParams);
                scatterMaterial.SetVector(ShaderID._BlurCompositeWeight, setting.blurCompositeWeight.value);
                scatterMaterial.SetColor(ShaderID._ColorTint, setting.tint.value);

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                var scale = 0.25f;

                var scaledWidth = (int)(cameraData.pixelWidth * scale);
                var scaleHeight = (int)(cameraData.pixelHeight * scale);


                data.material = scatterMaterial;

                
                data.preFilterTexture = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                {
                    format = desc.format,
                    filterMode = FilterMode.Bilinear,
                    name = "Pre-Filter",
                });
                data.preFilterBlurTexture = renderGraph.CreateTexture(new TextureDesc(scaledWidth, scaleHeight)
                {
                    format = desc.format,
                    filterMode = FilterMode.Bilinear,
                    name = "Pre-Filter Blur",
                });

                data.OutputTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    format = desc.format,
                    filterMode = FilterMode.Bilinear,
                    name = "Bloom Output",
                });


                int iterations = 3;

                data.MipDownTexture = new TextureHandle[4];
                data.MipUpTexture = new TextureHandle[4];
                // Create mip pyramid textures
                {
                    data.MipDownTexture[0] = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                    {
                        format = desc.format,
                        filterMode = FilterMode.Bilinear,
                        name = "_BloomMipDown0"
                    });
                    data.MipUpTexture[0] = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                    {
                        width = (int)(desc.width * scale),
                        height = (int)(desc.height * scale),

                        format = desc.format,
                        filterMode = FilterMode.Bilinear,
                        name = "_BloomMipUp0"
                    });
                    ShaderID._MipUp[0] = Shader.PropertyToID("_MipUp0");
                    ShaderID._MipDown[0] = Shader.PropertyToID("_MipDown0");

                    for (int i = 1; i < iterations; i++)
                    {
                        scale /= 2f;
                        scaledWidth = (int)(cameraData.pixelWidth * scale);
                        scaleHeight = (int)(cameraData.pixelHeight * scale);

                        ref TextureHandle mipDown = ref data.MipDownTexture[i];
                        ref TextureHandle mipUp = ref data.MipUpTexture[i];


                        // NOTE: Reuse RTHandle names for TextureHandles
                        mipDown = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                        {
                            format = desc.format,
                            filterMode = FilterMode.Bilinear,
                            name = $"_BloomMipDown{i}"
                        });
                        ;
                        mipUp = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                        {
                            width = (int)(desc.width * scale),
                            height = (int)(desc.height * scale),
                            format = desc.format,
                            filterMode = FilterMode.Bilinear,
                            name = $"_BloomMipup{i}"
                        });
                        ShaderID._MipUp[i] = Shader.PropertyToID("_MipUp" + i);
                        ShaderID._MipDown[i] = Shader.PropertyToID("_MipDown" + i);
                    }
                }
                data.iterations = iterations;
                data.sourceTexture = resourceData.activeColorTexture;

                builder.AllowPassCulling(false);
                builder.UseTexture(data.sourceTexture);
                builder.UseTexture(data.preFilterBlurTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.OutputTexture, AccessFlags.ReadWrite);

                builder.SetRenderFunc<PassData>(ExecutePass);

                resourceData.cameraColor = data.OutputTexture;
            }
        }
    }
}