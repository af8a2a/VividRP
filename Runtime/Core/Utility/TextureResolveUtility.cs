using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class TextureResolveUtility
    {
        internal static Texture ResolveTexture(this RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }

        internal static Texture ResolveTexture(this TextureHandle handle)
        {
            RTHandle rtHandle = handle;
            return rtHandle.ResolveTexture();
        }

        internal static Texture ResolveTexture(this TextureHandle? handle)
        {
            return handle.HasValue ? handle.Value.ResolveTexture() : null;
        }

        internal static Texture ResolveTexture(this RenderGraphTexture texture)
        {
            return texture == null ? null : ResolveTexture(texture.innerHandle);
        }
    }
}
