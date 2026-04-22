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
                GetScaleBias(
                    sourceHandle,
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
                GetScaleBias(
                    sourceHandle,
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

        private static Vector4 GetScaleBias(
            RTHandle handle,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            var scale = Vector2.one;
            if (handle == null || !handle.useScaling)
            {
                var yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
                return yFlip
                    ? new Vector4(1f, -1f, 0f, 1f)
                    : new Vector4(1f, 1f, 0f, 0f);
            }

            scale.x = handle.rtHandleProperties.rtHandleScale.x;
            scale.y = handle.rtHandleProperties.rtHandleScale.y;

            var shouldFlipY = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return shouldFlipY
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
