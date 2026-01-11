using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

#if DLSS_PLUGIN_INTEGRATE
using DLSS;
#endif

namespace UnityEngine.Rendering.Universal
{
    public class DLSSWarpPass
    {
        private DLSSPass _dlssPass;

        #region DLSSMask

        static readonly int _StencilMask = Shader.PropertyToID("_StencilMask");
        static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
        static readonly int _StencilCmp = Shader.PropertyToID("_StencilCmp");


        Material m_UpscalerBiasColorMaskMaterial;

        class UpscalerColorMaskPassData
        {
            public Material colorMaskMaterial;
            public int destWidth;
            public int destHeight;
        }

        static ProfilingSampler UpscalerColorMaskProfiler = new ProfilingSampler("UpscalerColorMask");

        TextureHandle RenderDLSSMask(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<UpscalerColorMaskPassData>("Upscaler Color Mask", out var passData,
                       UpscalerColorMaskProfiler))
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                var output = renderGraph.CreateTexture(
                    new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
                    {
                        format = GraphicsFormat.R8G8B8A8_UNorm,
                        clearBuffer = true,
                        clearColor = Color.black,
                        name = "Upscaler Color Mask"
                    });

                var inputDepth = resourceData.activeDepthTexture;
                builder.SetRenderAttachment(output, 0);
                builder.SetRenderAttachmentDepth(inputDepth, AccessFlags.Read);


                if (!m_UpscalerBiasColorMaskMaterial)
                {
                    var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                    m_UpscalerBiasColorMaskMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.DLSSBiasColorMaskPS);
                }

                passData.colorMaskMaterial = m_UpscalerBiasColorMaskMaterial;
                passData.destWidth = cameraData.actualWidth;
                passData.destHeight = cameraData.actualHeight;

                builder.SetRenderFunc((UpscalerColorMaskPassData data, UnsafeGraphContext ctx) =>
                {
                    Rect targetViewport = new Rect(0.0f, 0.0f, data.destWidth, data.destHeight);
                    data.colorMaskMaterial.SetInt(_StencilMask, (int)StencilUsage.ExcludeFromTUAndAA);
                    data.colorMaskMaterial.SetInt(_StencilRef, (int)StencilUsage.ExcludeFromTUAndAA);
                    ctx.cmd.SetViewport(targetViewport);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.colorMaskMaterial, 0, MeshTopology.Triangles, 3, 1, null);
                });
                return output;
            }
        }

        #endregion


        #region DLSSUpscale

        static ProfilingSampler DLSSProfiler = new ProfilingSampler("DLSSUpscale");

        TextureHandle DoDLSSPass(
            RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle source, TextureHandle depthBuffer, TextureHandle motionVectors, TextureHandle biasColorMask)
        {
            
            using (var builder = renderGraph.AddUnsafePass<DLSSData>("Deep Learning Super Sampling", out var passData,
                       DLSSProfiler))
            {
                builder.AllowPassCulling(false);
                cameraData.RequestGpuExposureValue(cameraData.GetExposureTexture());
                passData.parameters = new DLSSPass.Parameters();
                passData.parameters.resetHistory = cameraData.resetHistory;
                // Must check this with nvidia. After trying many things this gives the least amount of ghosting.
                // For now we clamp the exposure to a reasonable value.
                passData.parameters.preExposure = Mathf.Clamp(cameraData.GpuExposureValue(), 0.20f, 2.0f);
                passData.parameters.cameraData = cameraData;
                var viewHandles = new UpscalerResources.ViewResourceHandles();
                viewHandles.source = source;
                builder.UseTexture(viewHandles.source, AccessFlags.Read);
                viewHandles.output = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    name = "DLSS destination",
                    format = cameraData.cameraTargetDescriptor.graphicsFormat,
                    enableRandomWrite = true,
                });
                builder.UseTexture(viewHandles.output, AccessFlags.Write);
                viewHandles.depth = depthBuffer;
                builder.UseTexture(viewHandles.depth, AccessFlags.Read);
                viewHandles.motionVectors = motionVectors;
                builder.UseTexture(viewHandles.motionVectors, AccessFlags.Read);
                // Note: exposure texture input
                // We skip providing exposureTexture since HDRP pre-applies exposure in GBuffer pass / light accumulation buffer,
                // and doesn't use it later on in tonemapping. DLSS docs mention this texture is needed if used in tonemapping later on.
                // Given we also provide an option to inject DLSS post-tonemapping, we can safely skip providing this input as
                // it usually exacerbates ghosting within the HDRP use context.

                if (biasColorMask.IsValid())
                {
                    viewHandles.biasColorMask = biasColorMask;
                    builder.UseTexture(viewHandles.biasColorMask, AccessFlags.Read);
                }
                else
                    viewHandles.biasColorMask = TextureHandle.nullHandle;

                passData.resourceHandles = UpscalerResources.CreateCameraResources(cameraData, renderGraph, builder, viewHandles);

                passData.pass = _dlssPass;

                builder.SetRenderFunc((DLSSData data, UnsafeGraphContext ctx) =>
                {
                    data.pass.Render(data.parameters, UpscalerResources.GetCameraResources(data.resourceHandles),
                        CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd));
                });
                return viewHandles.output;
            }

        }


        class DLSSData
        {
            public DLSSPass.Parameters parameters;
            public UpscalerResources.CameraResourcesHandles resourceHandles;
            public DLSSPass pass;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (_dlssPass is null)
            {
                _dlssPass = DLSSPass.Create();
            }

            TextureHandle colorBiasMask = RenderDLSSMask(renderGraph, frameData);
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var depthBuffer = resourceData.activeDepthTexture;
            var motionVectors = resourceData.motionVectorColor;
            _dlssPass.BeginFrame(cameraData);    
            source = DoDLSSPass(renderGraph, cameraData, source, depthBuffer, motionVectors, colorBiasMask);
            return source;

        }

        #endregion
    }
}