using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : System.IDisposable
    {
        private RTHandle m_CachedSourceCubemapHandle;
        private Texture m_CachedSource;
        private Cubemap m_FallbackCubemap;
        private RTHandle m_FallbackCubemapHandle;
        private int m_CachedSkyHash;

        internal bool IsValid =>
            m_CachedSource != null
            || m_CachedSourceCubemapHandle != null
            || m_FallbackCubemap != null
            || m_FallbackCubemapHandle != null;

        internal RTHandle Cubemap
        {
            get
            {
                EnsureCachedSourceCubemapHandle();
                if (m_CachedSourceCubemapHandle != null)
                    return m_CachedSourceCubemapHandle;

                EnsureFallbackCubemapHandle();
                return m_FallbackCubemapHandle;
            }
        }

        internal int SkyHash => m_CachedSkyHash;
        internal int MaxMipLevel
        {
            get
            {
                if (m_CachedSource != null)
                    return Mathf.Max(0, m_CachedSource.mipmapCount - 1);

                if (m_FallbackCubemap != null)
                    return Mathf.Max(0, m_FallbackCubemap.mipmapCount - 1);

                return 0;
            }
        }

        internal bool HasSource(Texture source)
        {
            return ReferenceEquals(m_CachedSource, source);
        }

        internal void Build(VividRPCoreResources resources)
        {
        }

        internal void Update(CommandBuffer cmd, Texture source, int skyHash)
        {
            if (ReferenceEquals(m_CachedSource, source)
                && skyHash == m_CachedSkyHash
                && m_CachedSourceCubemapHandle != null)
                return;

            if (source == null)
            {
                ReleaseCachedSourceCubemapHandle();
                m_CachedSkyHash = 0;
                return;
            }

            if (!ReferenceEquals(m_CachedSource, source))
            {
                ReleaseCachedSourceCubemapHandle(false);
                m_CachedSource = source;
            }

            m_CachedSkyHash = skyHash;
        }

        internal void Update(Texture source, int skyHash)
        {
            Update(null, source, skyHash);
        }

        public void Dispose()
        {
            ReleaseCachedSourceCubemapHandle();

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

        private void EnsureCachedSourceCubemapHandle()
        {
            if (m_CachedSourceCubemapHandle != null || m_CachedSource == null)
                return;

            m_CachedSourceCubemapHandle = RTHandles.Alloc(m_CachedSource);
        }

        private void ReleaseCachedSourceCubemapHandle(bool clearSource = true)
        {
            if (m_CachedSourceCubemapHandle != null)
            {
                m_CachedSourceCubemapHandle.Release();
                m_CachedSourceCubemapHandle = null;
            }

            if (clearSource)
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
