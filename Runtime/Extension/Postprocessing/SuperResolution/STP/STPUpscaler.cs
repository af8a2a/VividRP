using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.SuperResolution.STP
{
    public class STPUpscaler
    {
        private const string _UpscaledColorTargetName = "_UpscaledColorTarget";

        TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var runtimeTexture = GraphicsSettings.GetRenderPipelineSettings<PostProcessData.TextureResources>();
            TextureHandle cameraDepth = resourceData.cameraDepthTexture;
            TextureHandle motionVectors = resourceData.motionVectorColor;

            Debug.Assert(motionVectors.IsValid(), "MotionVectors are invalid. STP requires a motion vector texture.");


            var destination = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelWidth)
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