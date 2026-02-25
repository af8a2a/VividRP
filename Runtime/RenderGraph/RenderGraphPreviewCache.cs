using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderGraph
{
    public static class RenderGraphPreviewCache
    {
        private static readonly Dictionary<string, RenderTexture> s_PreviewTextures = new();

        public static RenderTexture Get(string nodeGuid)
        {
            if (string.IsNullOrEmpty(nodeGuid))
                return null;

            if (!s_PreviewTextures.TryGetValue(nodeGuid, out var texture) || texture == null)
                return null;

            return texture;
        }

        public static RenderTexture GetOrCreate(string nodeGuid, int width, int height)
        {
            if (string.IsNullOrEmpty(nodeGuid))
                return null;

            width = Mathf.Clamp(width, 16, 2048);
            height = Mathf.Clamp(height, 16, 2048);

            if (s_PreviewTextures.TryGetValue(nodeGuid, out var existing) && existing != null)
            {
                if (existing.width == width && existing.height == height)
                    return existing;

                DestroyTexture(existing);
            }

            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = $"VividRP Preview ({nodeGuid})",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();

            s_PreviewTextures[nodeGuid] = texture;
            return texture;
        }

        public static void Clear(string nodeGuid)
        {
            if (string.IsNullOrEmpty(nodeGuid))
                return;

            if (!s_PreviewTextures.TryGetValue(nodeGuid, out var texture))
                return;

            DestroyTexture(texture);
            s_PreviewTextures.Remove(nodeGuid);
        }

        public static void ClearAll()
        {
            foreach (var texture in s_PreviewTextures.Values)
                DestroyTexture(texture);

            s_PreviewTextures.Clear();
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;

            if (texture.IsCreated())
                texture.Release();

            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
        }
    }
}
