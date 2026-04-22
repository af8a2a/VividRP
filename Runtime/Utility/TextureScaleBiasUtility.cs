using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class TextureScaleBiasUtility
    {
        internal static Vector2 GetScale(RTHandle handle)
        {
            if (handle == null || !handle.useScaling)
                return Vector2.one;

            return new Vector2(
                handle.rtHandleProperties.rtHandleScale.x,
                handle.rtHandleProperties.rtHandleScale.y);
        }

        internal static Vector4 GetScaleBias(RTHandle handle)
        {
            Vector2 scale = GetScale(handle);
            return new Vector4(scale.x, scale.y, 0f, 0f);
        }

        internal static Vector4 GetScaleBias(
            Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            bool yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }

        internal static Vector4 GetScaleBias(
            RTHandle handle,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            return GetScaleBias(GetScale(handle), sourceTextureUVOrigin, destinationTextureUVOrigin);
        }
    }
}
