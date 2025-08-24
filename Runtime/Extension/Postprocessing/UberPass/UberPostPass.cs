using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public static partial class ShaderKeywordStrings
    {
        /// <summary> Keyword used for ACES Tonemapping. </summary>
        public const string TonemapGranTurismo = "_TONEMAP_GT";

        public const string TonemapAgx = "_TONEMAP_AGX";
        public const string TonemapAgxApprox = "_TONEMAP_AGX_APPROX";
    }

    // Precomputed shader ids to same some CPU cycles (mostly affects mobile)
    public static partial class ShaderConstants
    {
        public static readonly int _TempTarget = Shader.PropertyToID("_TempTarget");
        public static readonly int _TempTarget2 = Shader.PropertyToID("_TempTarget2");

        public static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
        public static readonly int _StencilMask = Shader.PropertyToID("_StencilMask");

        public static readonly int _FullCoCTexture = Shader.PropertyToID("_FullCoCTexture");
        public static readonly int _HalfCoCTexture = Shader.PropertyToID("_HalfCoCTexture");
        public static readonly int _DofTexture = Shader.PropertyToID("_DofTexture");
        public static readonly int _CoCParams = Shader.PropertyToID("_CoCParams");
        public static readonly int _BokehKernel = Shader.PropertyToID("_BokehKernel");
        public static readonly int _BokehConstants = Shader.PropertyToID("_BokehConstants");
        public static readonly int _PongTexture = Shader.PropertyToID("_PongTexture");
        public static readonly int _PingTexture = Shader.PropertyToID("_PingTexture");

        public static readonly int _Metrics = Shader.PropertyToID("_Metrics");
        public static readonly int _AreaTexture = Shader.PropertyToID("_AreaTexture");
        public static readonly int _SearchTexture = Shader.PropertyToID("_SearchTexture");
        public static readonly int _EdgeTexture = Shader.PropertyToID("_EdgeTexture");
        public static readonly int _BlendTexture = Shader.PropertyToID("_BlendTexture");

        public static readonly int _ColorTexture = Shader.PropertyToID("_ColorTexture");
        public static readonly int _Params = Shader.PropertyToID("_Params");
        public static readonly int _SourceTexLowMip = Shader.PropertyToID("_SourceTexLowMip");
        public static readonly int _Bloom_Params = Shader.PropertyToID("_Bloom_Params");
        public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");
        public static readonly int _LensDirt_Texture = Shader.PropertyToID("_LensDirt_Texture");
        public static readonly int _LensDirt_Params = Shader.PropertyToID("_LensDirt_Params");
        public static readonly int _LensDirt_Intensity = Shader.PropertyToID("_LensDirt_Intensity");
        public static readonly int _Distortion_Params1 = Shader.PropertyToID("_Distortion_Params1");
        public static readonly int _Distortion_Params2 = Shader.PropertyToID("_Distortion_Params2");
        public static readonly int _Chroma_Params = Shader.PropertyToID("_Chroma_Params");
        public static readonly int _Vignette_Params1 = Shader.PropertyToID("_Vignette_Params1");
        public static readonly int _Vignette_Params2 = Shader.PropertyToID("_Vignette_Params2");
        public static readonly int _Vignette_ParamsXR = Shader.PropertyToID("_Vignette_ParamsXR");


        public static readonly int _Lut_Params = Shader.PropertyToID("_Lut_Params");
        public static readonly int _UserLut_Params = Shader.PropertyToID("_UserLut_Params");
        public static readonly int _InternalLut = Shader.PropertyToID("_InternalLut");
        public static readonly int _UserLut = Shader.PropertyToID("_UserLut");
        public static readonly int _GTToneMap_Params0 = Shader.PropertyToID("_GTToneMap_Params0");
        public static readonly int _GTToneMap_Params1 = Shader.PropertyToID("_GTToneMap_Params1");


        public static readonly int _DownSampleScaleFactor = Shader.PropertyToID("_DownSampleScaleFactor");

        public static readonly int _FlareOcclusionRemapTex = Shader.PropertyToID("_FlareOcclusionRemapTex");
        public static readonly int _FlareOcclusionTex = Shader.PropertyToID("_FlareOcclusionTex");
        public static readonly int _FlareOcclusionIndex = Shader.PropertyToID("_FlareOcclusionIndex");
        public static readonly int _FlareTex = Shader.PropertyToID("_FlareTex");
        public static readonly int _FlareColorValue = Shader.PropertyToID("_FlareColorValue");
        public static readonly int _FlareData0 = Shader.PropertyToID("_FlareData0");
        public static readonly int _FlareData1 = Shader.PropertyToID("_FlareData1");
        public static readonly int _FlareData2 = Shader.PropertyToID("_FlareData2");
        public static readonly int _FlareData3 = Shader.PropertyToID("_FlareData3");
        public static readonly int _FlareData4 = Shader.PropertyToID("_FlareData4");
        public static readonly int _FlareData5 = Shader.PropertyToID("_FlareData5");

        public static readonly int _FullscreenProjMat = Shader.PropertyToID("_FullscreenProjMat");

        public static int[] _BloomMipUp;
        public static int[] _BloomMipDown;
    }


    public class UberPostPass
    {
        Material material;
        RTHandle m_UserLut;

        #region ColorGrading

        private class ColorGradingPassData
        {
            internal TextureHandle lutTexture;
            internal TextureHandle userLutTexture;


            internal Vector4 lutParams;
            internal Vector4 userLutParams;
            internal Vector4 hdrOutputLuminanceParams;

            internal Vector4 gtToneMapParams0;
            internal Vector4 gtToneMapParams1;


            internal Material material;
            internal VividTonemappingMode toneMappingMode;
            internal bool isHdrGrading;
        }

        TextureHandle TryGetCachedUserLutTextureHandle(RenderGraph renderGraph)
        {
            var colorLookup = VolumeManager.instance.stack.GetComponent<ColorLookup>();

            if (colorLookup.texture.value == null)
            {
                if (m_UserLut != null)
                {
                    m_UserLut.Release();
                    m_UserLut = null;
                }
            }
            else
            {
                if (m_UserLut == null || m_UserLut.externalTexture != colorLookup.texture.value)
                {
                    m_UserLut?.Release();
                    m_UserLut = RTHandles.Alloc(colorLookup.texture.value);
                }
            }

            return m_UserLut != null ? renderGraph.ImportTexture(m_UserLut) : TextureHandle.nullHandle;
        }


        static void GetHDROutputLuminanceParameters(HDROutputUtils.HDRDisplayInformation hdrDisplayInformation, ColorGamut hdrDisplayColorGamut,
            VividToneMapping tonemapping, out Vector4 hdrOutputParameters)
        {
            float minNits = hdrDisplayInformation.minToneMapLuminance;
            float maxNits = hdrDisplayInformation.maxToneMapLuminance;
            float paperWhite = hdrDisplayInformation.paperWhiteNits;

            if (!tonemapping.detectPaperWhite.value)
            {
                paperWhite = tonemapping.paperWhite.value;
            }

            if (!tonemapping.detectBrightnessLimits.value)
            {
                minNits = tonemapping.minNits.value;
                maxNits = tonemapping.maxNits.value;
            }

            hdrOutputParameters = new Vector4(minNits, maxNits, paperWhite, 1f / paperWhite);
        }

        void SetupColorGrading(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ColorGradingPassData>("Setup ColorGrading", out var passData))
            {
                var postProcessingData = frameData.Get<UniversalPostProcessingData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var colorLookup = VolumeManager.instance.stack.GetComponent<ColorLookup>();
                var colorAdjustments = VolumeManager.instance.stack.GetComponent<ColorAdjustments>();
                var tonemapping = VolumeManager.instance.stack.GetComponent<VividToneMapping>();


                TextureHandle internalColorLut = resourceData.internalColorLut;


                bool hdrGrading = postProcessingData.gradingMode == ColorGradingMode.HighDynamicRange;
                int lutHeight = postProcessingData.lutSize;
                int lutWidth = lutHeight * lutHeight;

                // Source material setup
                float postExposureLinear = Mathf.Pow(2f, colorAdjustments.postExposure.value);
                Vector4 lutParams = new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f, postExposureLinear);

                TextureHandle userLutTexture = TryGetCachedUserLutTextureHandle(renderGraph);

                // var Tonemapping = VolumeManager.instance.stack.GetComponent<Tonemapping>();

                Vector4 userLutParams = !colorLookup.IsActive()
                    ? Vector4.zero
                    : new Vector4(1f / colorLookup.texture.value.width,
                        1f / colorLookup.texture.value.height,
                        colorLookup.texture.value.height - 1f,
                        colorLookup.contribution.value);


#if ENABLE_VR && ENABLE_XR_MODULE
                if (cameraData.xr.enabled)
                {
                    bool passSupportsFoveation = cameraData.xrUniversal.canFoveateIntermediatePasses || resourceData.isActiveTargetBackBuffer;
                    // This is a screen-space pass, make sure foveated rendering is disabled for non-uniform renders
                    passSupportsFoveation &= !XRSystem.foveatedRenderingCaps.HasFlag(FoveatedRenderingCaps.NonUniformRaster);
                    builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                }
#endif


                if (cameraData.isHDROutputActive)
                {
                    GetHDROutputLuminanceParameters(cameraData.hdrDisplayInformation, cameraData.hdrDisplayColorGamut, tonemapping,
                        out passData.hdrOutputLuminanceParams);
                }


                if (tonemapping.mode.value is VividTonemappingMode.GranTurismo)
                {
                    passData.gtToneMapParams0 = new Vector4(tonemapping.maxBrightness.value, tonemapping.contrast.value,
                        tonemapping.linearSectionStart.value, tonemapping.linearSectionLength.value);
                    passData.gtToneMapParams1 = new Vector4(tonemapping.blackPow.value, tonemapping.blackMin.value, 0.0f,
                        0.0f);
                }

                passData.lutTexture = internalColorLut;
                builder.UseTexture(passData.lutTexture, AccessFlags.Read);
                passData.lutParams = lutParams;
                if (userLutTexture.IsValid())
                {
                    passData.userLutTexture = userLutTexture;
                    builder.UseTexture(userLutTexture, AccessFlags.Read);
                }


                passData.userLutParams = userLutParams;
                passData.material = material;
                passData.toneMappingMode = tonemapping.mode.value;
                passData.isHdrGrading = hdrGrading;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<ColorGradingPassData>((data, context) =>
                {
                    var material = data.material;

                    material.SetTexture(ShaderConstants._InternalLut, data.lutTexture);
                    material.SetVector(ShaderConstants._Lut_Params, data.lutParams);
                    material.SetTexture(ShaderConstants._UserLut, data.userLutTexture);
                    material.SetVector(ShaderConstants._UserLut_Params, data.userLutParams);
                    material.SetVector(ShaderConstants._UserLut_Params, data.userLutParams);
                    material.SetVector(ShaderConstants._GTToneMap_Params0, data.gtToneMapParams0);
                    material.SetVector(ShaderConstants._GTToneMap_Params1, data.gtToneMapParams1);


                    if (data.isHdrGrading)
                    {
                        material.EnableKeyword(ShaderKeywordStrings.HDRGrading);
                    }
                    else
                    {
                        switch (data.toneMappingMode)
                        {
                            case VividTonemappingMode.Neutral: material.EnableKeyword(ShaderKeywordStrings.TonemapNeutral); break;
                            case VividTonemappingMode.ACES: material.EnableKeyword(ShaderKeywordStrings.TonemapACES); break;
                            case VividTonemappingMode.GranTurismo: material.EnableKeyword(ShaderKeywordStrings.TonemapGranTurismo); break;
                            case VividTonemappingMode.AgX: material.EnableKeyword(ShaderKeywordStrings.TonemapAgx); break;
                            case VividTonemappingMode.AgxApprox: material.EnableKeyword(ShaderKeywordStrings.TonemapAgxApprox); break;
                            default: break; // None
                        }
                    }
                });
            }
        }

        #endregion


        #region BloomApply

        private class UberSetupBloomPassData
        {
            internal Vector4 bloomParams;
            internal Vector4 dirtScaleOffset;
            internal float dirtIntensity;
            internal Texture dirtTexture;
            internal bool highQualityFilteringValue;
            internal TextureHandle bloomTexture;

            internal Material uberMaterial;
        }

        public void UberPostSetupBloomPass(RenderGraph renderGraph, ContextContainer frameData)
        {
            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();

            using (var builder = renderGraph.AddRasterRenderPass<UberSetupBloomPassData>("Setup Bloom Post Processing", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_UberPostSetupBloomPass)))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                // Setup bloom on uber
                var tint = bloom.tint.value.linear;
                var luma = ColorUtils.Luminance(tint);
                tint = luma > 0f ? tint * (1f / luma) : Color.white;
                var bloomParams = new Vector4(bloom.intensity.value, tint.r, tint.g, tint.b);

                // Setup lens dirtiness on uber
                // Keep the aspect ratio correct & center the dirt texture, we don't want it to be
                // stretched or squashed
                var dirtTexture = bloom.dirtTexture.value == null ? Texture2D.blackTexture : bloom.dirtTexture.value;
                float dirtRatio = dirtTexture.width / (float)dirtTexture.height;
                float screenRatio = cameraData.aspectRatio;
                var dirtScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                float dirtIntensity = bloom.dirtIntensity.value;

                if (dirtRatio > screenRatio)
                {
                    dirtScaleOffset.x = screenRatio / dirtRatio;
                    dirtScaleOffset.z = (1f - dirtScaleOffset.x) * 0.5f;
                }
                else if (screenRatio > dirtRatio)
                {
                    dirtScaleOffset.y = dirtRatio / screenRatio;
                    dirtScaleOffset.w = (1f - dirtScaleOffset.y) * 0.5f;
                }

                passData.bloomParams = bloomParams;
                passData.dirtScaleOffset = dirtScaleOffset;
                passData.dirtIntensity = dirtIntensity;
                passData.dirtTexture = dirtTexture;
                passData.highQualityFilteringValue = bloom.highQualityFiltering.value;

                passData.bloomTexture = resourceData.bloomTexture;
                builder.UseTexture(passData.bloomTexture, AccessFlags.Read);
                passData.uberMaterial = material;

                // TODO RENDERGRAPH: properly setup dependencies between passes
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (UberSetupBloomPassData data, RasterGraphContext context) =>
                {
                    var uberMaterial = data.uberMaterial;
                    uberMaterial.SetVector(ShaderConstants._Bloom_Params, data.bloomParams);
                    uberMaterial.SetVector(ShaderConstants._LensDirt_Params, data.dirtScaleOffset);
                    uberMaterial.SetFloat(ShaderConstants._LensDirt_Intensity, data.dirtIntensity);
                    uberMaterial.SetTexture(ShaderConstants._LensDirt_Texture, data.dirtTexture);

                    // Keyword setup - a bit convoluted as we're trying to save some variants in Uber...
                    if (data.highQualityFilteringValue)
                        uberMaterial.EnableKeyword(data.dirtIntensity > 0f ? ShaderKeywordStrings.BloomHQDirt : ShaderKeywordStrings.BloomHQ);
                    else
                        uberMaterial.EnableKeyword(data.dirtIntensity > 0f ? ShaderKeywordStrings.BloomLQDirt : ShaderKeywordStrings.BloomLQ);

                    uberMaterial.SetTexture(ShaderConstants._Bloom_Texture, data.bloomTexture);
                });
            }
        }

        #endregion


        #region LensDistortion

        void SetupLensDistortion(Material material, bool isSceneView)
        {
            LensDistortion lensDistortion = VolumeManager.instance.stack.GetComponent<LensDistortion>();

            float amount = 1.6f * Mathf.Max(Mathf.Abs(lensDistortion.intensity.value * 100f), 1f);
            float theta = Mathf.Deg2Rad * Mathf.Min(160f, amount);
            float sigma = 2f * Mathf.Tan(theta * 0.5f);
            var center = lensDistortion.center.value * 2f - Vector2.one;
            var p1 = new Vector4(
                center.x,
                center.y,
                Mathf.Max(lensDistortion.xMultiplier.value, 1e-4f),
                Mathf.Max(lensDistortion.yMultiplier.value, 1e-4f)
            );
            var p2 = new Vector4(
                lensDistortion.intensity.value >= 0f ? theta : 1f / theta,
                sigma,
                1f / lensDistortion.scale.value,
                lensDistortion.intensity.value * 100f
            );

            material.SetVector(ShaderConstants._Distortion_Params1, p1);
            material.SetVector(ShaderConstants._Distortion_Params2, p2);

            if (lensDistortion.IsActive() && !isSceneView)
                material.EnableKeyword(ShaderKeywordStrings.Distortion);
        }

        #endregion

        #region ChromaticAberration

        void SetupChromaticAberration(Material material)
        {
            var chromaticAberration = VolumeManager.instance.stack.GetComponent<ChromaticAberration>();
            material.SetFloat(ShaderConstants._Chroma_Params, chromaticAberration.intensity.value * 0.05f);

            if (chromaticAberration.IsActive())
                material.EnableKeyword(ShaderKeywordStrings.ChromaticAberration);
        }

        #endregion

        #region Vignette

        void SetupVignette(Material material, UniversalCameraData cameraData, XRPass xrPass = null)
        {
            var m_Vignette = VolumeManager.instance.stack.GetComponent<Vignette>();
            var color = m_Vignette.color.value;
            var center = m_Vignette.center.value;
            var aspectRatio = cameraData.aspectRatio;


#if ENABLE_VR && ENABLE_XR_MODULE
            if (xrPass != null && xrPass.enabled)
            {
                if (xrPass.singlePassEnabled)
                    material.SetVector(ShaderConstants._Vignette_ParamsXR, xrPass.ApplyXRViewCenterOffset(center));
                else
                    // In multi-pass mode we need to modify the eye center with the values from .xy of the corrected
                    // center since the version of the shader that is not single-pass will use the value in _Vignette_Params2
                    center = xrPass.ApplyXRViewCenterOffset(center);
            }
#endif

            var v1 = new Vector4(
                color.r, color.g, color.b,
                m_Vignette.rounded.value ? aspectRatio : 1f
            );
            var v2 = new Vector4(
                center.x, center.y,
                m_Vignette.intensity.value * 3f,
                m_Vignette.smoothness.value * 5f
            );

            material.SetVector(ShaderConstants._Vignette_Params1, v1);
            material.SetVector(ShaderConstants._Vignette_Params2, v2);
        }

        #endregion


        private class UberPostPassData
        {
            internal TextureHandle destTexture;
            internal TextureHandle sourceTexture;
            internal TextureHandle userLutTexture;
            internal Vector4 userLutParams;

            internal Material material;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.uberPost);
            }
            var cameraData = frameData.Get<UniversalCameraData>();

            material.enabledKeywords = null;


            UberPostSetupBloomPass(renderGraph, frameData);
            SetupColorGrading(renderGraph, frameData);
            SetupLensDistortion(material, cameraData.isSceneViewCamera);
            SetupVignette(material, cameraData);
            SetupChromaticAberration(material);

            var destTexture = renderGraph.ImportTexture(cameraData.urpRenderer.nextRenderGraphCameraColorHandle);
            using (var builder = renderGraph.AddRasterRenderPass<UberPostPassData>("Vivid UberPost", out var passData))
            {
                passData.destTexture = destTexture;
                builder.SetRenderAttachment(destTexture, 0);
                passData.sourceTexture = source;
                builder.UseTexture(passData.sourceTexture, AccessFlags.Read);
                passData.material = material;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (UberPostPassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;

                    Blitter.BlitTexture(cmd, data.sourceTexture, Vector2.one, data.material, 0);
                });
            }


            return destTexture;
        }
    }
}