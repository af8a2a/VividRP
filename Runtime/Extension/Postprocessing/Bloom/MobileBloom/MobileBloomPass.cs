using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class MobileBloomPass 
    {
        Material _material;

        Material bloomMaterial
        {
            get { return _material ??= new Material(Shader.Find("PostProcessing/MobileBloom")); }
        }

        class PassData
        {
            internal int iterations;

            internal Material material;

            internal TextureHandle sourceTexture;
            internal TextureHandle OutputTexture;


            internal TextureHandle preFilterTexture;
            internal TextureHandle preFilterBlurTexture;

            internal TextureHandle[] bloomMipUpTexture;
            internal TextureHandle[] bloomMipDownTexture;
        }




        class ShaderID
        {
            public static readonly int _Bloom_Custom_Params = Shader.PropertyToID("_Bloom_Custom_Params");
            public static readonly int _Bloom_Custom_ColorTint = Shader.PropertyToID("_Bloom_Custom_ColorTint");
            public static readonly int _Bloom_Custom_BlurScaler = Shader.PropertyToID("_Bloom_Custom_BlurScaler");
            public static readonly int _Bloom_Custom_BlurCompositeWeight = Shader.PropertyToID("_Bloom_Custom_BlurCompositeWeight");
            public static int[] _BloomMipUp = new int[16];
            public static int[] _BloomMipDown = new int[16];
            public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");
        }


        static void ExecuteBloomPass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            CoreUtils.SetKeyword(cmd, "_USE_RGBM", true);
            // preFilter
            Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.preFilterTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 0);

            // preFilterBlur
            cmd.SetGlobalVector(ShaderID._Bloom_Custom_BlurScaler, new Vector4(1, 0, 0, 0));
            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 2);
            cmd.SetGlobalVector(ShaderID._Bloom_Custom_BlurScaler, new Vector4(0, 1, 0, 0));
            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 2);

            TextureHandle last = data.preFilterTexture;

            for (var level = 0; level < data.iterations; level++)
            {
                Blitter.BlitCameraTexture(cmd, last, data.bloomMipDownTexture[level], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                    data.material,
                    1);
                last = data.bloomMipDownTexture[level];
            }

            // blur mips
            for (var level = 0; level < data.iterations; level++)
            {
                cmd.SetGlobalVector(ShaderID._Bloom_Custom_BlurScaler, new Vector4(1, 0, 0, 0));
                Blitter.BlitCameraTexture(cmd, data.bloomMipDownTexture[level], data.bloomMipUpTexture[level], RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store, data.material, 3 + level);

                cmd.SetGlobalVector(ShaderID._Bloom_Custom_BlurScaler, new Vector4(0, 1, 0, 0));
                Blitter.BlitCameraTexture(cmd, data.bloomMipUpTexture[level], data.bloomMipDownTexture[level], RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store, data.material, 3 + level);
            }

            // upsample once
            for (var level = 0; level < data.iterations; level++)
            {
                data.material.SetTexture(ShaderID._BloomMipDown[level], data.bloomMipDownTexture[level]);
            }

            Blitter.BlitCameraTexture(cmd, data.preFilterTexture, data.preFilterBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                data.material, 6);

        
        }


        public  TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            var setting = VolumeManager.instance.stack.GetComponent<MobileBloom>();
            if (!setting.enable.value)
            {
                return source;
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>("Mobile Bloom", out var data))
            {
                var bloomParams = new Vector4(setting.threshold.value, setting.lumRangeScale.value, setting.preFilterScale.value, setting.intensity.value);

                bloomMaterial.SetVector(ShaderID._Bloom_Custom_Params, bloomParams);
                bloomMaterial.SetVector(ShaderID._Bloom_Custom_BlurCompositeWeight, setting.blurCompositeWeight.value);
                bloomMaterial.SetColor(ShaderID._Bloom_Custom_ColorTint, setting.tint.value);

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                var scale = 0.25f;

                var scaledWidth = (int)(cameraData.pixelWidth * scale);
                var scaleHeight = (int)(cameraData.pixelHeight * scale);
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

                data.bloomMipDownTexture = new TextureHandle[4];
                data.bloomMipUpTexture = new TextureHandle[4];
                // Create bloom mip pyramid textures
                {
                    data.bloomMipDownTexture[0] = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                    {
                        format = desc.format,
                        filterMode = FilterMode.Bilinear,
                        name = "_BloomMipDown0"
                    });
                    data.bloomMipUpTexture[0] = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                    {
                        width = (int)(desc.width * scale),
                        height = (int)(desc.height * scale),

                        format = desc.format,
                        filterMode = FilterMode.Bilinear,
                        name = "_BloomMipUp0"
                    });
                    ShaderID._BloomMipUp[0] = Shader.PropertyToID("_BloomMipUp0");
                    ShaderID._BloomMipDown[0] = Shader.PropertyToID("_BloomMipDown0");

                    for (int i = 1; i < iterations; i++)
                    {
                        scale /= 2f;
                        scaledWidth = (int)(cameraData.pixelWidth * scale);
                        scaleHeight = (int)(cameraData.pixelHeight * scale);

                        ref TextureHandle mipDown = ref data.bloomMipDownTexture[i];
                        ref TextureHandle mipUp = ref data.bloomMipUpTexture[i];


                        // NOTE: Reuse RTHandle names for TextureHandles
                        mipDown = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                        {
                            format = desc.format,
                            filterMode = FilterMode.Bilinear,
                            name = $"_BloomMipDown{i}"
                        });
                        
                        mipUp = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaleHeight)
                        {
                            width = (int)(desc.width * scale),
                            height = (int)(desc.height * scale),
                            format = desc.format,
                            filterMode = FilterMode.Bilinear,
                            name = $"_BloomMipup{i}"
                        });
                        ShaderID._BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
                        ShaderID._BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);
                    }
                }


                data.iterations = iterations;
                data.material = bloomMaterial;
                data.sourceTexture = resourceData.activeColorTexture;

                builder.AllowPassCulling(false);
                builder.UseTexture(data.sourceTexture);
                builder.UseTexture(data.preFilterBlurTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => ExecuteBloomPass(passData, context));
                resourceData.bloomTexture = data.preFilterBlurTexture;
                return source;
            }
        }
    }
}