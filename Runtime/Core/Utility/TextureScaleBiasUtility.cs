using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class TextureScaleBiasUtility
    {
        internal static Vector2 GetScale(this RTHandle handle)
        {
            if (handle == null || !handle.useScaling)
                return Vector2.one;

            return new Vector2(
                handle.rtHandleProperties.rtHandleScale.x,
                handle.rtHandleProperties.rtHandleScale.y);
        }

        internal static Vector2 GetScale(this TextureHandle handle)
        {
            RTHandle rtHandle = handle;
            return rtHandle.GetScale();
        }

        internal static Vector2 GetScale(this TextureHandle? handle)
        {
            return handle.HasValue ? handle.Value.GetScale() : Vector2.one;
        }

        internal static Vector4 GetScaleBias(this RTHandle handle)
        {
            return handle.GetScale().GetScaleBias();
        }

        internal static Vector4 GetScaleBias(this Vector2 scale)
        {
            return new Vector4(scale.x, scale.y, 0f, 0f);
        }

        internal static Vector4 GetScaleBias(this TextureHandle handle)
        {
            return handle.GetScale().GetScaleBias();
        }

        internal static Vector4 GetScaleBias(this TextureHandle? handle)
        {
            return handle.GetScale().GetScaleBias();
        }

        internal static Vector4 GetScaleBias(
            this Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            bool yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }

        internal static Vector4 GetScaleBias(
            this RTHandle handle,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            return GetScaleBias(GetScale(handle), sourceTextureUVOrigin, destinationTextureUVOrigin);
        }

        internal static Vector4 GetScaleBias(
            this TextureHandle handle,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            return handle.GetScale().GetScaleBias(sourceTextureUVOrigin, destinationTextureUVOrigin);
        }

        internal static Vector4 GetScaleBias(
            this TextureHandle? handle,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            return handle.GetScale().GetScaleBias(sourceTextureUVOrigin, destinationTextureUVOrigin);
        }
    }
}
