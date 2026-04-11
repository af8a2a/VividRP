using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : System.IDisposable
    {
        private RTHandle m_CachedCubemapHandle;
        private Cubemap m_CachedSource;
        private Cubemap m_FallbackCubemap;
        private RTHandle m_FallbackCubemapHandle;
        private int m_CachedSkyHash;

        internal bool IsValid =>
            m_CachedSource != null
            || m_CachedCubemapHandle != null
            || m_FallbackCubemap != null
            || m_FallbackCubemapHandle != null;

        internal RTHandle Cubemap
        {
            get
            {
                EnsureCachedCubemapHandle();
                if (m_CachedCubemapHandle != null)
                    return m_CachedCubemapHandle;

                EnsureFallbackCubemapHandle();
                return m_FallbackCubemapHandle;
            }
        }

        internal int SkyHash => m_CachedSkyHash;

        internal void Update(Cubemap source, int skyHash)
        {
            if (source == null)
            {
                ReleaseCachedCubemapHandle();
                m_CachedSkyHash = 0;
                return;
            }

            if (m_CachedSource != source)
            {
                ReleaseCachedCubemapHandle();
                m_CachedSource = source;
            }

            m_CachedSkyHash = skyHash;
        }

        public void Dispose()
        {
            ReleaseCachedCubemapHandle();

            if (m_FallbackCubemapHandle != null)
            {
                m_FallbackCubemapHandle.Release();
                m_FallbackCubemapHandle = null;
            }

            if (m_FallbackCubemap != null)
            {
                CoreUtils.Destroy(m_FallbackCubemap);
                m_FallbackCubemap = null;
            }

            m_CachedSkyHash = 0;
        }

        private void EnsureFallbackCubemapHandle()
        {
            if (m_FallbackCubemapHandle != null)
                return;

            m_FallbackCubemap = CreateFallbackSkyIBLCubemap();
            m_FallbackCubemapHandle = RTHandles.Alloc(m_FallbackCubemap);
        }

        private void EnsureCachedCubemapHandle()
        {
            if (m_CachedCubemapHandle != null || m_CachedSource == null)
                return;

            m_CachedCubemapHandle = RTHandles.Alloc(m_CachedSource);
        }

        private void ReleaseCachedCubemapHandle()
        {
            if (m_CachedCubemapHandle != null)
            {
                m_CachedCubemapHandle.Release();
                m_CachedCubemapHandle = null;
            }

            m_CachedSource = null;
        }

        private static Cubemap CreateFallbackSkyIBLCubemap()
        {
            var cubemap = new Cubemap(1, TextureFormat.RGBA32, false);
            var colors = new[] { Color.black };
            cubemap.SetPixels(colors, CubemapFace.PositiveX);
            cubemap.SetPixels(colors, CubemapFace.NegativeX);
            cubemap.SetPixels(colors, CubemapFace.PositiveY);
            cubemap.SetPixels(colors, CubemapFace.NegativeY);
            cubemap.SetPixels(colors, CubemapFace.PositiveZ);
            cubemap.SetPixels(colors, CubemapFace.NegativeZ);
            cubemap.Apply(false, true);
            cubemap.name = "FallbackSkyIBLCubemap";
            cubemap.hideFlags = HideFlags.HideAndDontSave;
            return cubemap;
        }
    }
}
