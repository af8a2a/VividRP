using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class URPBloomPass 
    {
        #region Private Field

        readonly Material m_BloomMaterial;
        public readonly Material[] bloomUpsample;

        BloomMaterialParams m_BloomParamsPrev = new BloomMaterialParams();
        const int k_MaxPyramidSize = 16;


        private class BloomPassData
        {
            internal int mipCount;

            internal Material material;
            internal Material[] upsampleMaterials;

            internal TextureHandle sourceTexture;

            internal TextureHandle[] bloomMipUp;
            internal TextureHandle[] bloomMipDown;
        }

        private struct BloomMaterialParams
        {
            internal Vector4 parameters;
            internal bool highQualityFiltering;
            internal bool enableAlphaOutput;

            internal bool Equals(ref BloomMaterialParams other)
            {
                return parameters == other.parameters &&
                       highQualityFiltering == other.highQualityFiltering &&
                       enableAlphaOutput == other.enableAlphaOutput;
            }
        }

        #endregion


        public URPBloomPass()
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<BloomRuntimeShader>();
            m_BloomMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.URPBloomShader);

            bloomUpsample = new Material[k_MaxPyramidSize];
            for (uint i = 0; i < k_MaxPyramidSize; ++i)
                bloomUpsample[i] = RenderingUtilsExt.Load(runtimeShader.URPBloomShader);
        }


        #region ShaderID

        static class ShaderConstants
        {
            public static int _Params = Shader.PropertyToID("_Params");
            public static readonly int _SourceTexLowMip = Shader.PropertyToID("_SourceTexLowMip");
        }

        #endregion

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            // Start at half-res
            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();

            using (var builder = renderGraph.AddUnsafePass<BloomPassData>("Blit Bloom Mipmaps", out var passData, ProfilingSampler.Get(URPProfileId.Bloom)))
            {
                int downres = 1;
                switch (bloom.downscale.value)
                {
                    case BloomDownscaleMode.Half:
                        downres = 1;
                        break;
                    case BloomDownscaleMode.Quarter:
                        downres = 2;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var cameraData = frameData.Get<UniversalCameraData>();

                //We should set the limit the downres result to ensure we dont turn 1x1 textures, which should technically be valid
                //into 0x0 textures which will be invalid
                int tw = Mathf.Max(1, cameraData.actualWidth >> downres);
                int th = Mathf.Max(1, cameraData.actualHeight >> downres);

                // Determine the iteration count
                int maxSize = Mathf.Max(tw, th);
                int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
                int mipCount = Mathf.Clamp(iterations, 1, bloom.maxIterations.value);

                // Setup
                using (new ProfilingScope(ProfilingSampler.Get(URPProfileId.RG_BloomSetup)))
                {
                    // Pre-filtering parameters
                    float clamp = bloom.clamp.value;
                    float threshold = Mathf.GammaToLinearSpace(bloom.threshold.value);
                    float thresholdKnee = threshold * 0.5f; // Hardcoded soft knee

                    // Material setup
                    float scatter = Mathf.Lerp(0.05f, 0.95f, bloom.scatter.value);

                    BloomMaterialParams bloomParams = new BloomMaterialParams();
                    bloomParams.parameters = new Vector4(scatter, clamp, threshold, thresholdKnee);
                    bloomParams.highQualityFiltering = bloom.highQualityFiltering.value;
                    bloomParams.enableAlphaOutput = cameraData.isAlphaOutputEnabled;

                    // Setting keywords can be somewhat expensive on low-end platforms.
                    // Previous params are cached to avoid setting the same keywords every frame.
                    var material = m_BloomMaterial;
                    bool bloomParamsDirty = !m_BloomParamsPrev.Equals(ref bloomParams);
                    bool isParamsPropertySet = material.HasProperty(ShaderConstants._Params);
                    if (bloomParamsDirty || !isParamsPropertySet)
                    {
                        material.SetVector(ShaderConstants._Params, bloomParams.parameters);
                        CoreUtils.SetKeyword(material, ShaderKeywordStrings.BloomHQ, bloomParams.highQualityFiltering);
                        CoreUtils.SetKeyword(material, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, bloomParams.enableAlphaOutput);

                        // These materials are duplicate just to allow different bloom blits to use different textures.
                        for (uint i = 0; i < k_MaxPyramidSize; ++i)
                        {
                            var materialPyramid = bloomUpsample[i];
                            materialPyramid.SetVector(ShaderConstants._Params, bloomParams.parameters);
                            CoreUtils.SetKeyword(materialPyramid, ShaderKeywordStrings.BloomHQ, bloomParams.highQualityFiltering);
                            CoreUtils.SetKeyword(materialPyramid, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, bloomParams.enableAlphaOutput);
                        }

                        m_BloomParamsPrev = bloomParams;
                    }

                    passData.bloomMipDown = new TextureHandle[4];
                    passData.bloomMipUp = new TextureHandle[4];


                    // Create bloom mip pyramid textures
                    {
                        var format = RenderingUtilsExt.PickPostProcessingFormat();
                        passData.bloomMipDown[0] = renderGraph.CreateTexture(new TextureDesc(tw, th)
                        {
                            format = format,
                            name = "_BloomMipDown0",
                            filterMode = FilterMode.Bilinear,
                            clearBuffer = false,
                        });
                        passData.bloomMipUp[0] = renderGraph.CreateTexture(new TextureDesc(tw, th)
                        {
                            format = format,
                            name = "_BloomMipUp0",
                            filterMode = FilterMode.Bilinear,
                            clearBuffer = false,
                        });

                        for (int i = 1; i < mipCount; i++)
                        {
                            tw = Mathf.Max(1, tw >> 1);
                            th = Mathf.Max(1, th >> 1);
                            ref TextureHandle mipDown = ref passData.bloomMipDown[i];
                            ref TextureHandle mipUp = ref passData.bloomMipUp[i];

                            mipDown = renderGraph.CreateTexture(new TextureDesc(tw, th)
                            {
                                format = format,
                                name = $"_BloomMipDown{i}",
                                filterMode = FilterMode.Bilinear,
                                clearBuffer = false,
                            });

                            mipUp = renderGraph.CreateTexture(new TextureDesc(tw, th)
                            {
                                format = format,
                                name = $"_BloomMipup{i}",
                                filterMode = FilterMode.Bilinear,
                                clearBuffer = false,
                            });
                        }
                    }
                }


                passData.mipCount = mipCount;
                passData.material = m_BloomMaterial;
                passData.upsampleMaterials = bloomUpsample;
                passData.sourceTexture = source;

                // TODO RENDERGRAPH: properly setup dependencies between passes
                builder.AllowPassCulling(false);

                builder.UseTexture(source, AccessFlags.Read);
                for (int i = 0; i < mipCount; i++)
                {
                    builder.UseTexture(passData.bloomMipUp[i], AccessFlags.ReadWrite);
                    builder.UseTexture(passData.bloomMipDown[i], AccessFlags.ReadWrite);
                }

                builder.SetRenderFunc(static (BloomPassData data, UnsafeGraphContext context) =>
                {
                    // TODO: can't call BlitTexture with unsafe command buffer
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var material = data.material;
                    int mipCount = data.mipCount;

                    var loadAction = RenderBufferLoadAction.DontCare; // Blit - always write all pixels
                    var storeAction = RenderBufferStoreAction.Store; // Blit - always read by then next Blit

                    // Prefilter
                    using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.RG_BloomPrefilter)))
                    {
                        Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.bloomMipDown[0], loadAction, storeAction, material, 0);
                    }

                    // Downsample - gaussian pyramid
                    // Classic two pass gaussian blur - use mipUp as a temporary target
                    //   First pass does 2x downsampling + 9-tap gaussian
                    //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
                    using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.RG_BloomDownsample)))
                    {
                        TextureHandle lastDown = data.bloomMipDown[0];
                        for (int i = 1; i < mipCount; i++)
                        {
                            TextureHandle mipDown = data.bloomMipDown[i];
                            TextureHandle mipUp = data.bloomMipUp[i];

                            Blitter.BlitCameraTexture(cmd, lastDown, mipUp, loadAction, storeAction, material, 1);
                            Blitter.BlitCameraTexture(cmd, mipUp, mipDown, loadAction, storeAction, material, 2);

                            lastDown = mipDown;
                        }
                    }

                    using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.RG_BloomUpsample)))
                    {
                        // Upsample (bilinear by default, HQ filtering does bicubic instead
                        for (int i = mipCount - 2; i >= 0; i--)
                        {
                            TextureHandle lowMip = (i == mipCount - 2) ? data.bloomMipDown[i + 1] : data.bloomMipUp[i + 1];
                            TextureHandle highMip = data.bloomMipDown[i];
                            TextureHandle dst = data.bloomMipUp[i];

                            // We need a separate material for each upsample pass because setting the low texture mip source
                            // gets overriden by the time the render func is executed.
                            // Material is a reference, so all the blits would share the same material state in the cmdbuf.
                            // NOTE: another option would be to use cmd.SetGlobalTexture().
                            var upMaterial = data.upsampleMaterials[i];
                            upMaterial.SetTexture(ShaderConstants._SourceTexLowMip, lowMip);

                            Blitter.BlitCameraTexture(cmd, highMip, dst, loadAction, storeAction, upMaterial, 3);
                        }
                    }
                });

                return passData.bloomMipUp[0];

            }
        }
    }
}