using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class TextureResolveUtility
    {
        internal static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }

        internal static Texture ResolveTexture(RenderGraphTexture texture)
        {
            return texture == null ? null : ResolveTexture(texture.innerHandle);
        }
    }
}
