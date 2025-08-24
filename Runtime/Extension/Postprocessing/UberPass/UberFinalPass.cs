using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class UberFinalPass : ScriptableRenderPass
    {
        Material material;

        public UberFinalPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        // Precomputed shader ids to same some CPU cycles (mostly affects mobile)
        static class ShaderConstants
        {
            public static readonly int _Grain_Texture = Shader.PropertyToID("_Grain_Texture");
            public static readonly int _Grain_Params = Shader.PropertyToID("_Grain_Params");
            public static readonly int _Grain_TilingParams = Shader.PropertyToID("_Grain_TilingParams");

            public static readonly int _BlueNoise_Texture = Shader.PropertyToID("_BlueNoise_Texture");
            public static readonly int _Dithering_Params = Shader.PropertyToID("_Dithering_Params");

            public static readonly int _SourceSize = Shader.PropertyToID("_SourceSize");
        }


        #region Film Grain

        void SetupGrain(UniversalCameraData cameraData, Material material)
        {
            var filmGrain = VolumeManager.instance.stack.GetComponent<FilmGrain>();

            if (filmGrain.IsActive())
            {
                material.EnableKeyword(ShaderKeywordStrings.FilmGrain);

                var cameraPixelWidth = cameraData.pixelWidth;
                var cameraPixelHeight = cameraData.pixelHeight;

                var runtimeTexture = GraphicsSettings.GetRenderPipelineSettings<PostProcessData.TextureResources>();
                var texture = filmGrain.texture.value;

                if (filmGrain.type.value != FilmGrainLookup.Custom)
                    texture = runtimeTexture.filmGrainTex[(int)filmGrain.type.value];

#if LWRP_DEBUG_STATIC_POSTFX
            float rndOffsetX = 0f;
            float rndOffsetY = 0f;
#else
                var oldState = Random.state;
                Random.InitState(Time.frameCount);
                float rndOffsetX = Random.value;
                float rndOffsetY = Random.value;
                Random.state = oldState;
#endif

                var tilingParams = texture == null
                    ? Vector4.zero
                    : new Vector4(cameraPixelWidth / (float)texture.width, cameraPixelHeight / (float)texture.height, rndOffsetX, rndOffsetY);

                material.SetTexture(ShaderConstants._Grain_Texture, texture);
                material.SetVector(ShaderConstants._Grain_Params, new Vector2(filmGrain.intensity.value * 4f, filmGrain.response.value));
                material.SetVector(ShaderConstants._Grain_TilingParams, tilingParams);
            }
        }

        #endregion


        #region 8-bit Dithering

        private int m_DitheringTextureIndex;


        int SetupDithering(UniversalCameraData cameraData, Material material)
        {
            material.EnableKeyword(ShaderKeywordStrings.Dithering);

            var runtimeTexture = GraphicsSettings.GetRenderPipelineSettings<PostProcessData.TextureResources>();

            var blueNoise = runtimeTexture.blueNoise16LTex;

            if (blueNoise == null || blueNoise.Length == 0)
                return 0; // Safe guard

            var index = m_DitheringTextureIndex;
#if LWRP_DEBUG_STATIC_POSTFX // Used by QA for automated testing
            index = 0;
            float rndOffsetX = 0f;
            float rndOffsetY = 0f;
#else
            if (++index >= blueNoise.Length)
                index = 0;

            var oldState = Random.state;
            Random.InitState(Time.frameCount);
            float rndOffsetX = Random.value;
            float rndOffsetY = Random.value;
            Random.state = oldState;
#endif

            // Ideally we would be sending a texture array once and an index to the slice to use
            // on every frame but these aren't supported on all Universal targets
            var noiseTex = blueNoise[index];

            material.SetTexture(ShaderConstants._BlueNoise_Texture, noiseTex);
            material.SetVector(ShaderConstants._Dithering_Params, new Vector4(
                cameraData.pixelWidth / (float)noiseTex.width,
                cameraData.pixelHeight / (float)noiseTex.height,
                rndOffsetX,
                rndOffsetY
            ));

            return index;
        }

        #endregion


        #region LinearToSRGBConversion

        private bool m_EnableColorEncodingIfNeeded;

        bool RequireSRGBConversionBlitToBackBuffer(bool requireSrgbConversion)
        {
            return requireSrgbConversion && m_EnableColorEncodingIfNeeded;
        }

        void SetupLinearToSRGBConversion(UniversalCameraData cameraData, Material material)
        {
            if (RequireSRGBConversionBlitToBackBuffer(cameraData.requireSrgbConversion))
                material.EnableKeyword(ShaderKeywordStrings.LinearToSRGBConversion);
        }

        #endregion


        #region HDR

        bool RequireHDROutput(UniversalCameraData cameraData)
        {
            // If capturing, don't convert to HDR.
            // If not last in the stack, don't convert to HDR.
            return cameraData.isHDROutputActive && cameraData.captureActions == null;
        }


        void SetupHDROutput(HDROutputUtils.HDRDisplayInformation hdrDisplayInformation, ColorGamut hdrDisplayColorGamut, Material material,
            HDROutputUtils.Operation hdrOperations, bool rendersOverlayUI)
        {
            float minNits = hdrDisplayInformation.minToneMapLuminance;
            float maxNits = hdrDisplayInformation.maxToneMapLuminance;
            float paperWhite = hdrDisplayInformation.paperWhiteNits;

            var tonemapping = VolumeManager.instance.stack.GetComponent<VividToneMapping>();

            if (!tonemapping.detectPaperWhite.value)
            {
                paperWhite = tonemapping.paperWhite.value;
            }

            if (!tonemapping.detectBrightnessLimits.value)
            {
                minNits = tonemapping.minNits.value;
                maxNits = tonemapping.maxNits.value;
            }

            var hdrOutputLuminanceParams = new Vector4(minNits, maxNits, paperWhite, 1f / paperWhite);


            material.SetVector(ShaderPropertyId.hdrOutputLuminanceParams, hdrOutputLuminanceParams);

            HDROutputUtils.ConfigureHDROutput(material, hdrDisplayColorGamut, hdrOperations);
            CoreUtils.SetKeyword(material, ShaderKeywordStrings.HDROverlay, rendersOverlayUI);
        }


        void SetupHDR(UniversalCameraData cameraData, Material material, bool enableColorEncodingIfNeeded = true)
        {
            var requireHDROutput = RequireHDROutput(cameraData);
            if (requireHDROutput)
            {
                // If there is a final post process pass, it's always the final pass so do color encoding
                var hdrOperations = enableColorEncodingIfNeeded ? HDROutputUtils.Operation.ColorEncoding : HDROutputUtils.Operation.None;
                // If the color space conversion wasn't applied by the uber pass, do it here
                if (!cameraData.postProcessEnabled)
                    hdrOperations |= HDROutputUtils.Operation.ColorConversion;

                SetupHDROutput(cameraData.hdrDisplayInformation, cameraData.hdrDisplayColorGamut, material, hdrOperations,
                    cameraData.rendersOverlayUI);
            }
        }

        #endregion

        #region FXAA

        void SetupFXAA(UniversalCameraData cameraData, Material material)
        {
            var isFxaaEnabled = (cameraData.antialiasing == AntialiasingMode.FastApproximateAntialiasing);

            if (isFxaaEnabled)
            {
                material.EnableKeyword(ShaderKeywordStrings.Fxaa);
            }
        }

        #endregion


        #region FSR1

        void SetupFSR1(UniversalCameraData cameraData, Material material, HDROutputUtils.Operation hdrOperations)
        {
            bool isFsrEnabled = ((cameraData.imageScalingMode == ImageScalingMode.Upscaling) && (cameraData.upscalingFilter == ImageUpscalingFilter.FSR));

            if (isFsrEnabled)
            {
                material.EnableKeyword(hdrOperations.HasFlag(HDROutputUtils.Operation.ColorEncoding)
                    ? ShaderKeywordStrings.Gamma20AndHDRInput
                    : ShaderKeywordStrings.Gamma20);
            }
        }

        #endregion

        #region AlphaOutput

        void SetupAlphaOutput(UniversalCameraData cameraData, Material material)
        {
            if (cameraData.isAlphaOutputEnabled)
            {
                material.EnableKeyword(ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT);
            }
        }

        #endregion

        private class PostProcessingFinalBlitPassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle sourceTexture;
            internal Material material;
            internal UniversalCameraData cameraData;
            internal FinalBlitSettings settings;
        }

        /// <summary>
        /// Final blit settings.
        /// </summary>
        public struct FinalBlitSettings
        {
            /// <summary>Is FXAA enabled</summary>
            public bool isFxaaEnabled;

            /// <summary>Is FSR Enabled.</summary>
            public bool isFsrEnabled;

            /// <summary>Is TAA sharpening enabled.</summary>
            public bool isTaaSharpeningEnabled;

            /// <summary>True if final blit requires HDR output.</summary>
            public bool requireHDROutput;

            /// <summary>True if final blit needs to resolve to debug screen.</summary>
            public bool resolveToDebugScreen;

            /// <summary>True if final blit needs to output alpha channel.</summary>
            public bool isAlphaOutputEnabled;

            /// <summary>HDR Operations</summary>
            public HDROutputUtils.Operation hdrOperations;

            /// <summary>
            /// Create FinalBlitSettings
            /// </summary>
            /// <returns>New FinalBlitSettings</returns>
            public static FinalBlitSettings Create()
            {
                FinalBlitSettings s = new FinalBlitSettings();
                s.isFxaaEnabled = false;
                s.isFsrEnabled = false;
                s.isTaaSharpeningEnabled = false;
                s.requireHDROutput = false;
                s.resolveToDebugScreen = false;
                s.isAlphaOutputEnabled = false;

                s.hdrOperations = HDROutputUtils.Operation.None;

                return s;
            }
        };


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            var cameraColor = resourceData.cameraColor;
            Render(renderGraph, frameData, cameraColor);
        }

        public void Render(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.finalPost);
            }


            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            
            SetupGrain(cameraData, material);
            m_DitheringTextureIndex= SetupDithering(cameraData, material);


            SetupLinearToSRGBConversion(cameraData, material);
            
            FinalBlitSettings settings = FinalBlitSettings.Create();
            settings.hdrOperations = HDROutputUtils.Operation.None;
            settings.requireHDROutput = RequireHDROutput(cameraData);
            if (settings.requireHDROutput)
            {
                // If there is a final post process pass, it's always the final pass so do color encoding
                settings.hdrOperations = m_EnableColorEncodingIfNeeded ? HDROutputUtils.Operation.ColorEncoding : HDROutputUtils.Operation.None;
                // If the color space conversion wasn't applied by the uber pass, do it here
                if (!cameraData.postProcessEnabled)
                    settings.hdrOperations |= HDROutputUtils.Operation.ColorConversion;

                SetupHDROutput(cameraData.hdrDisplayInformation, cameraData.hdrDisplayColorGamut, material, settings.hdrOperations,
                    cameraData.rendersOverlayUI);
            }

            DebugHandler debugHandler = GetActiveDebugHandler(cameraData);
            bool resolveToDebugScreen = debugHandler != null && debugHandler.WriteToDebugScreenTexture(cameraData.resolveFinalTarget);
            debugHandler?.UpdateShaderGlobalPropertiesForFinalValidationPass(renderGraph, cameraData, !resolveToDebugScreen);


            m_EnableColorEncodingIfNeeded = debugHandler == null || !debugHandler.HDRDebugViewIsActive(cameraData.resolveFinalTarget);

            settings.resolveToDebugScreen = resolveToDebugScreen;
            settings.isAlphaOutputEnabled = cameraData.isAlphaOutputEnabled;
            settings.isFxaaEnabled = (cameraData.antialiasing == AntialiasingMode.FastApproximateAntialiasing);
            settings.isFsrEnabled = ((cameraData.imageScalingMode == ImageScalingMode.Upscaling) && (cameraData.upscalingFilter == ImageUpscalingFilter.FSR));
            // Reuse RCAS pass as an optional standalone post sharpening pass for TAA.
            // This avoids the cost of EASU and is available for other upscaling options.
            // If FSR is enabled then FSR settings override the TAA settings and we perform RCAS only once.
            // If STP is enabled, then TAA sharpening has already been performed inside STP.
            settings.isTaaSharpeningEnabled = (cameraData.IsTemporalAAEnabled() && cameraData.taaSettings.contrastAdaptiveSharpening > 0.0f) &&
                                              !settings.isFsrEnabled && !cameraData.IsSTPEnabled();

            if (settings.isFxaaEnabled)
            {
                // In unscaled renders, FXAA can be safely performed in the FinalPost shader
                material.EnableKeyword(ShaderKeywordStrings.Fxaa);
            }

            var overlayUITexture = resourceData.overlayUITexture;
            using (var builder = renderGraph.AddRasterRenderPass<PostProcessingFinalBlitPassData>("Vivid Final", out var passData))
            {
                builder.AllowGlobalStateModification(true);
                passData.destinationTexture = resourceData.backBufferColor;
                builder.SetRenderAttachment(passData.destinationTexture, 0, AccessFlags.Write);
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                passData.cameraData = cameraData;
                passData.material = material;

                if (RequireHDROutput(cameraData) && cameraData.rendersOverlayUI)
                {
                    builder.UseTexture(overlayUITexture, AccessFlags.Read);
                }

#if ENABLE_VR && ENABLE_XR_MODULE
                if (cameraData.xr.enabled)
                {
                    // This is a screen-space pass, make sure foveated rendering is disabled for non-uniform renders
                    bool passSupportsFoveation = !XRSystem.foveatedRenderingCaps.HasFlag(FoveatedRenderingCaps.NonUniformRaster);
                    builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                }
#endif

                builder.SetRenderFunc(static (PostProcessingFinalBlitPassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.material;
                    var isFsrEnabled = data.settings.isFsrEnabled;
                    var isRcasEnabled = data.settings.isTaaSharpeningEnabled;
                    var requireHDROutput = data.settings.requireHDROutput;
                    var resolveToDebugScreen = data.settings.resolveToDebugScreen;
                    var isAlphaOutputEnabled = data.settings.isAlphaOutputEnabled;
                    RTHandle sourceTextureHdl = data.sourceTexture;
                    RTHandle destinationTextureHdl = data.destinationTexture;

                    PostProcessUtils.SetSourceSize(cmd, data.sourceTexture);


                    if (isFsrEnabled)
                    {
                        // RCAS
                        // Use the override value if it's available, otherwise use the default.
                        float sharpness = data.cameraData.fsrOverrideSharpness ? data.cameraData.fsrSharpness : FSRUtils.kDefaultSharpnessLinear;

                        // Set up the parameters for the RCAS pass unless the sharpness value indicates that it wont have any effect.
                        if (data.cameraData.fsrSharpness > 0.0f)
                        {
                            // RCAS is performed during the final post blit, but we set up the parameters here for better logical grouping.
                            material.EnableKeyword(requireHDROutput ? ShaderKeywordStrings.EasuRcasAndHDRInput : ShaderKeywordStrings.Rcas);
                            FSRUtils.SetRcasConstantsLinear(cmd, sharpness);
                        }
                    }
                    else if (isRcasEnabled) // RCAS only
                    {
                        // Reuse RCAS as a standalone sharpening filter for TAA.
                        // If FSR is enabled then it overrides the sharpening/TAA setting and we skip it.
                        material.EnableKeyword(ShaderKeywordStrings.Rcas);
                        FSRUtils.SetRcasConstantsLinear(cmd, data.cameraData.taaSettings.contrastAdaptiveSharpening);
                    }

                    if (isAlphaOutputEnabled)
                        CoreUtils.SetKeyword(material, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, isAlphaOutputEnabled);

                    bool isRenderToBackBufferTarget = !data.cameraData.isSceneViewCamera;
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (data.cameraData.xr.enabled)
                        isRenderToBackBufferTarget = destinationTextureHdl == data.cameraData.xr.renderTarget;
#endif
                    // HDR debug views force-renders to DebugScreenTexture.
                    isRenderToBackBufferTarget &= !resolveToDebugScreen;

                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;

                    // We y-flip if
                    // 1) we are blitting from render texture to back buffer(UV starts at bottom) and
                    // 2) renderTexture starts UV at top
                    bool yflip = isRenderToBackBufferTarget && data.cameraData.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
                    Vector4 scaleBias = yflip
                        ? new Vector4(viewportScale.x, -viewportScale.y, 0, viewportScale.y)
                        : new Vector4(viewportScale.x, viewportScale.y, 0, 0);

                    cmd.SetViewport(data.cameraData.pixelRect);
                    Blitter.BlitTexture(cmd, sourceTextureHdl, scaleBias, material, 0);
                });

            }

            resourceData.activeColorID = UniversalResourceData.ActiveID.BackBuffer;
            resourceData.activeDepthID = UniversalResourceData.ActiveID.BackBuffer;

        }
    }
}