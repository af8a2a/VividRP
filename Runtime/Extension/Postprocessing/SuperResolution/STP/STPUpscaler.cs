using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class STPUpscaler
    {
        private const string _UpscaledColorTargetName = "_UpscaledColorTarget";

        
        internal static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor desc, int width, int height, GraphicsFormat format, GraphicsFormat depthStencilFormat = GraphicsFormat.None)
        {
            desc.depthStencilFormat = depthStencilFormat;
            desc.msaaSamples = 1;
            desc.width = width;
            desc.height = height;
            desc.graphicsFormat = format;
            return desc;
        }

        
       public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var runtimeTexture = GraphicsSettings.GetRenderPipelineSettings<PostProcessData.TextureResources>();
            TextureHandle cameraDepth = resourceData.cameraDepthTexture;
            TextureHandle motionVectors = resourceData.motionVectorColor;

            Debug.Assert(motionVectors.IsValid(), "MotionVectors are invalid. STP requires a motion vector texture.");

            
            
            
            var destination = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
            {
                format = cameraData.cameraTargetDescriptor.graphicsFormat,
                enableRandomWrite = true,
                name = _UpscaledColorTargetName,
                clearBuffer = false,
                filterMode = FilterMode.Bilinear,
            });


            int frameIndex = Time.frameCount;
            var noiseTexture = runtimeTexture.blueNoise16LTex[frameIndex & (runtimeTexture.blueNoise16LTex.Length - 1)];

            StpUtils.Execute(renderGraph, resourceData, cameraData, source, cameraDepth, motionVectors, destination, noiseTexture);
            return destination;
        }
    }
}