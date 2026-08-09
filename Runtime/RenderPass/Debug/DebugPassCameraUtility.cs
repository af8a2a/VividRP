using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal static class DebugPassCameraUtility
    {
        internal static bool ShouldSkipExecution(VividCameraData cameraData)
        {
            return ShouldSkipExecution(cameraData?.camera);
        }

        internal static bool ShouldSkipExecution(Camera camera)
        {
            return camera != null && ShouldSkipExecution(camera.cameraType);
        }

        internal static bool ShouldSkipExecution(CameraType cameraType)
        {
            return cameraType == CameraType.Preview || cameraType == CameraType.Reflection;
        }

        internal static bool TryPassThrough(
            RasterPassContext context,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture outputTexture)
        {
            if (sourceTexture == null
                || outputTexture == null
                || sourceTexture.innerHandle.IsValid() != true
                || outputTexture.innerHandle.IsValid() != true)
            {
                return false;
            }

            RTHandle sourceHandle = sourceTexture.innerHandle;
            Blitter.BlitTexture(
                context.cmd,
                sourceHandle,
                sourceHandle.GetScaleBias(
                    context.GetTextureUVOrigin(sourceTexture.innerHandle),
                    context.GetTextureUVOrigin(outputTexture.innerHandle)),
                0f,
                UseBilinearSampling(sourceTexture));
            return true;
        }

        internal static bool TryPassThrough(
            UnsafePassContext context,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture outputTexture)
        {
            if (sourceTexture == null
                || outputTexture == null
                || sourceTexture.innerHandle.IsValid() != true
                || outputTexture.innerHandle.IsValid() != true)
            {
                return false;
            }

            RTHandle sourceHandle = sourceTexture.innerHandle;
            var cmd = context.GetNativeCommandBuffer();
            cmd.SetRenderTarget(outputTexture.innerHandle);
            Blitter.BlitTexture(
                cmd,
                sourceHandle,
                sourceHandle.GetScaleBias(
                    context.GetTextureUVOrigin(sourceTexture.innerHandle),
                    context.GetTextureUVOrigin(outputTexture.innerHandle)),
                0f,
                UseBilinearSampling(sourceTexture));
            return true;
        }

        private static bool UseBilinearSampling(RenderGraphTexture sourceTexture)
        {
            return sourceTexture?.desc?.FilterMode != FilterMode.Point;
        }

    }
}
