using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class URPTemporalAAPass
    {
        private Material taaMaterial;

        internal static class ShaderKeywords
        {
            public static readonly string TAA_LOW_PRECISION_SOURCE = "TAA_LOW_PRECISION_SOURCE";
        }

        internal static class ShaderConstants
        {
            public static readonly int _TaaAccumulationTex = Shader.PropertyToID("_TaaAccumulationTex");
            public static readonly int _TaaMotionVectorTex = Shader.PropertyToID("_TaaMotionVectorTex");

            public static readonly int _TaaFilterWeights   = Shader.PropertyToID("_TaaFilterWeights");

            public static readonly int _TaaFrameInfluence     = Shader.PropertyToID("_TaaFrameInfluence");
            public static readonly int _TaaVarianceClampScale = Shader.PropertyToID("_TaaVarianceClampScale");

            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        }

        public class TaaPassData
        {
            internal TextureHandle dstTex;
            internal TextureHandle srcColorTex;
            internal TextureHandle srcDepthTex;
            internal TextureHandle srcMotionVectorTex;
            internal TextureHandle srcTaaAccumTex;

            internal Material material;
            internal int passIndex;

            internal float taaFrameInfluence;
            internal float taaVarianceClampScale;
            internal float[] taaFilterWeights;

            internal bool taaLowPrecisionSource;
            internal bool taaAlphaOutput;
        }

        
        static RTHandle AllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_TemporalAA{1}", viewName, frameIndex));
        }

        internal bool ReAllocatedTemporalAATextureIfNeeded(HistoryFrameRTSystem historyRTSystem,GraphicsFormat format,  out RTHandle currFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.URPTemporalAA);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.URPTemporalAA);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.URPTemporalAA,
                    AllocatorFunction, format, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.URPTemporalAA);
            return vaild;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {


            if (!taaMaterial)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();

                taaMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.temporalAAShader);
            }
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            if (cameraData.antialiasing != AntialiasingMode.TemporalAntiAliasing || cameraData.IsSTPRequested())
            {
                return source;
            }


            TextureHandle cameraDepth = resourceData.cameraDepth;
            TextureHandle motionVectors = resourceData.motionVectorColor;

            if (!motionVectors.IsValid())
            {
                return source;
            }
            
            int multipassId = 0;
#if ENABLE_VR && ENABLE_XR_MODULE
            multipassId = cameraData.xr.multipassId;
#endif

            ref var taa = ref cameraData.taaSettings;

            var format = renderGraph.GetTextureDesc(source).format;

            bool isNewFrame = ReAllocatedTemporalAATextureIfNeeded(cameraData.historyFrameRTSystem, format, out var accumulationTexture);
            float taaInfluence = isNewFrame ? taa.m_FrameInfluence : 1.0f;

            TextureHandle srcAccumulation = renderGraph.ImportTexture(accumulationTexture);

            // On frame rerender or pause, stop all motion using a black motion texture.
            // This is done to avoid blurring the Taa resolve due to motion and Taa history mismatch.
            // The TAA history was updated for the next frame, as we did not know yet that we're going render this frame again.
            // We would need to keep the both the current and previous history (double buffering) in order to resolve
            // either this frame (again) or the next frame correctly, but it would cost more memory.
            TextureHandle activeMotionVectors = isNewFrame ? motionVectors : renderGraph.defaultResources.blackTexture;
            TextureHandle dstColor = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                filterMode = FilterMode.Bilinear,
                clearBuffer = false,
                name = "_TemporalAATarget",
                format = format,
                enableRandomWrite = true
            });
            ;
            using (var builder = renderGraph.AddRasterRenderPass<TaaPassData>("Temporal Anti-aliasing", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_TAA)))
            {


                passData.dstTex = dstColor;
                builder.SetRenderAttachment(passData.dstTex, 0, AccessFlags.Write);
                
                dstColor = passData.dstTex;

                passData.srcColorTex = source;
                builder.UseTexture(passData.srcColorTex, AccessFlags.Read);
                passData.srcDepthTex = cameraDepth;
                builder.UseTexture(passData.srcDepthTex, AccessFlags.Read);
                passData.srcMotionVectorTex = activeMotionVectors;
                builder.UseTexture(activeMotionVectors, AccessFlags.Read);
                passData.srcTaaAccumTex = srcAccumulation;
                builder.UseTexture(srcAccumulation, AccessFlags.Read);

                passData.material = taaMaterial;
                passData.passIndex = (int)taa.quality;

                passData.taaFrameInfluence = taaInfluence;
                passData.taaVarianceClampScale = taa.varianceClampScale;

                if (taa.quality == TemporalAAQuality.VeryHigh)
                    passData.taaFilterWeights = TemporalAA.CalculateFilterWeights(ref taa);
                else
                    passData.taaFilterWeights = null;
                switch (accumulationTexture.rt.graphicsFormat)
                {
                    // Avoid precision issues with YCoCg and low bit color formats.
                    case GraphicsFormat.B10G11R11_UFloatPack32:
                    case GraphicsFormat.R8G8B8A8_UNorm:
                    case GraphicsFormat.B8G8R8A8_UNorm:
                        passData.taaLowPrecisionSource = true;
                        break;
                    default:
                        passData.taaLowPrecisionSource = false;
                        break;
                }

                passData.taaAlphaOutput = cameraData.isAlphaOutputEnabled;

                builder.SetRenderFunc(static (TaaPassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(ShaderConstants._TaaFrameInfluence, data.taaFrameInfluence);
                    data.material.SetFloat(ShaderConstants._TaaVarianceClampScale, data.taaVarianceClampScale);
                    data.material.SetTexture(ShaderConstants._TaaAccumulationTex, data.srcTaaAccumTex);
                    data.material.SetTexture(ShaderConstants._TaaMotionVectorTex, data.srcMotionVectorTex);
                    data.material.SetTexture(ShaderConstants._CameraDepthTexture, data.srcDepthTex);
                    CoreUtils.SetKeyword(data.material, ShaderKeywords.TAA_LOW_PRECISION_SOURCE, data.taaLowPrecisionSource);
                    CoreUtils.SetKeyword(data.material, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, data.taaAlphaOutput);

                    if (data.taaFilterWeights != null)
                        data.material.SetFloatArray(ShaderConstants._TaaFilterWeights, data.taaFilterWeights);

                    Blitter.BlitTexture(context.cmd, data.srcColorTex, Vector2.one, data.material, data.passIndex);
                });
            }

            if (isNewFrame)
            {
                int kHistoryCopyPass = taaMaterial.shader.passCount - 1;
                using (var builder = renderGraph.AddRasterRenderPass<TaaPassData>("Temporal Anti-aliasing Copy History", out var passData,
                           ProfilingSampler.Get(URPProfileId.RG_TAACopyHistory)))
                {
                    passData.dstTex = srcAccumulation;
                    builder.SetRenderAttachment(srcAccumulation, 0, AccessFlags.Write);
                    passData.srcColorTex = dstColor;
                    builder.UseTexture(dstColor, AccessFlags.Read); // Resolved color is the new history

                    passData.material = taaMaterial;
                    passData.passIndex = kHistoryCopyPass;

                    builder.SetRenderFunc((TaaPassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.srcColorTex, Vector2.one, data.material, data.passIndex);
                    });
                }
            }

            return dstColor;
        }
    }
}