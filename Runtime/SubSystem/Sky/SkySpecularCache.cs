using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : System.IDisposable
    {
        private RTHandle m_CachedSourceCubemapHandle;
        private Texture m_CachedSource;
        private RenderTexture m_ConvolvedCubemap;
        private RTHandle m_ConvolvedCubemapHandle;
        private Cubemap m_FallbackCubemap;
        private RTHandle m_FallbackCubemapHandle;
        private int m_CachedSkyHash;
        private readonly SkyCubemapGGXConvolution m_GgxConvolution = new();

        internal bool IsValid =>
            m_CachedSource != null
            || m_CachedSourceCubemapHandle != null
            || m_ConvolvedCubemap != null
            || m_ConvolvedCubemapHandle != null
            || m_FallbackCubemap != null
            || m_FallbackCubemapHandle != null;

        internal RTHandle Cubemap
        {
            get
            {
                EnsureConvolvedCubemapHandle();
                if (m_ConvolvedCubemapHandle != null)
                    return m_ConvolvedCubemapHandle;

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
                if (m_ConvolvedCubemap != null)
                    return m_GgxConvolution.GetConvolutionMipLevel(m_ConvolvedCubemap);

                if (m_CachedSource != null)
                {
                    return m_GgxConvolution.IsSupported
                        ? m_GgxConvolution.GetConvolutionMipLevel(m_CachedSource)
                        : Mathf.Max(0, m_CachedSource.mipmapCount - 1);
                }

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
            m_GgxConvolution.Build(resources);
        }

        internal void Update(CommandBuffer cmd, Texture source, int skyHash)
        {
            if (ReferenceEquals(m_CachedSource, source)
                && skyHash == m_CachedSkyHash
                && (m_ConvolvedCubemapHandle != null || m_CachedSourceCubemapHandle != null))
                return;

            if (source == null)
            {
                ReleaseConvolvedCubemap();
                ReleaseCachedSourceCubemapHandle();
                m_CachedSkyHash = 0;
                return;
            }

            if (!ReferenceEquals(m_CachedSource, source))
            {
                ReleaseConvolvedCubemap(false);
                ReleaseCachedSourceCubemapHandle(false);
                m_CachedSource = source;
            }

            m_CachedSkyHash = skyHash;
            if (TryConvolveCubemap(cmd, source))
                return;

            EnsureCachedSourceCubemapHandle();
        }

        internal void Update(Texture source, int skyHash)
        {
            Update(null, source, skyHash);
        }

        public void Dispose()
        {
            m_GgxConvolution.Dispose();
            ReleaseConvolvedCubemap();
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

        private void EnsureConvolvedCubemapHandle()
        {
            if (m_ConvolvedCubemapHandle != null || m_ConvolvedCubemap == null)
                return;

            m_ConvolvedCubemapHandle = RTHandles.Alloc(m_ConvolvedCubemap);
        }

        private void EnsureCachedSourceCubemapHandle()
        {
            if (m_CachedSourceCubemapHandle != null || m_CachedSource == null)
                return;

            m_CachedSourceCubemapHandle = RTHandles.Alloc(m_CachedSource);
        }

        private bool TryConvolveCubemap(CommandBuffer cmd, Texture source)
        {
            if (!m_GgxConvolution.IsSupported || cmd == null || !IsConvolvableCubemap(source))
            {
                ReleaseConvolvedCubemap(false);
                return false;
            }

            EnsureConvolvedCubemap(source);
            if (m_ConvolvedCubemap == null || !m_GgxConvolution.Convolve(cmd, source, m_ConvolvedCubemap))
            {
                ReleaseConvolvedCubemap(false);
                return false;
            }

            EnsureConvolvedCubemapHandle();
            return true;
        }

        private void EnsureConvolvedCubemap(Texture source)
        {
            if (IsConvolvedCubemapValid(source))
                return;

            ReleaseConvolvedCubemap(false);

            m_ConvolvedCubemap = new RenderTexture(source.width, source.height, 0)
            {
                name = "VividSkySpecularGGX",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Cube,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_ConvolvedCubemap.Create();
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

        private void ReleaseConvolvedCubemap(bool clearSource = true)
        {
            if (m_ConvolvedCubemapHandle != null)
            {
                m_ConvolvedCubemapHandle.Release();
                m_ConvolvedCubemapHandle = null;
            }

            if (m_ConvolvedCubemap != null)
            {
                m_ConvolvedCubemap.Release();
                CoreUtils.Destroy(m_ConvolvedCubemap);
                m_ConvolvedCubemap = null;
            }

            if (clearSource)
                m_CachedSource = null;
        }

        private bool IsConvolvedCubemapValid(Texture source)
        {
            return m_ConvolvedCubemap != null
                && m_ConvolvedCubemap.IsCreated()
                && source != null
                && m_ConvolvedCubemap.dimension == TextureDimension.Cube
                && m_ConvolvedCubemap.width == source.width
                && m_ConvolvedCubemap.height == source.height
                && m_ConvolvedCubemap.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static bool IsConvolvableCubemap(Texture source)
        {
            if (source == null || source.dimension != TextureDimension.Cube || source.width <= 0 || source.height <= 0)
                return false;

            return source is not RenderTexture renderTexture || renderTexture.IsCreated();
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
