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
        private int m_CachedContentHash;
        private int m_CachedResolution;
        private bool m_ConvolutionAttemptedForCachedState;
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

        internal RTHandle SourceCubemap
        {
            get
            {
                EnsureCachedSourceCubemapHandle();
                if (m_CachedSourceCubemapHandle != null)
                    return m_CachedSourceCubemapHandle;

                return FallbackCubemap;
            }
        }

        internal RTHandle FallbackCubemap
        {
            get
            {
                EnsureFallbackCubemapHandle();
                return m_FallbackCubemapHandle;
            }
        }

        internal int Resolution
        {
            get
            {
                if (m_ConvolvedCubemap != null)
                    return m_ConvolvedCubemap.width;

                return m_CachedSource != null
                    ? Mathf.Max(1, m_CachedSource.width)
                    : 1;
            }
        }

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

        internal void Update(
            CommandBuffer cmd,
            Texture source,
            int contentHash,
            int resolution,
            bool forceRebuild = false)
        {
            var targetResolution = ResolveTargetResolution(source, resolution);
            var cachedStateMatches =
                ReferenceEquals(m_CachedSource, source)
                && contentHash == m_CachedContentHash
                && targetResolution == m_CachedResolution;
            var hasReusableCache =
                m_ConvolvedCubemapHandle != null
                || (m_CachedSourceCubemapHandle != null
                    && (cmd == null
                        || !m_GgxConvolution.IsSupported
                        || !IsConvolvableCubemap(source)
                        || m_ConvolutionAttemptedForCachedState));
            if (!forceRebuild
                && cachedStateMatches
                && hasReusableCache)
                return;

            if (source == null)
            {
                ReleaseConvolvedCubemap();
                ReleaseCachedSourceCubemapHandle();
                m_CachedContentHash = 0;
                m_CachedResolution = 0;
                m_ConvolutionAttemptedForCachedState = false;
                return;
            }

            if (!cachedStateMatches)
                m_ConvolutionAttemptedForCachedState = false;

            if (!ReferenceEquals(m_CachedSource, source))
            {
                ReleaseConvolvedCubemap(false);
                ReleaseCachedSourceCubemapHandle(false);
                m_CachedSource = source;
            }

            m_CachedContentHash = contentHash;
            m_CachedResolution = targetResolution;
            if (cmd != null
                && m_GgxConvolution.IsSupported
                && IsConvolvableCubemap(source))
            {
                m_ConvolutionAttemptedForCachedState = true;
            }

            if (TryConvolveCubemap(cmd, source, targetResolution))
                return;

            EnsureCachedSourceCubemapHandle();
        }

        internal void Update(Texture source, int contentHash, int resolution)
        {
            Update(null, source, contentHash, resolution);
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

            m_CachedContentHash = 0;
            m_CachedResolution = 0;
            m_ConvolutionAttemptedForCachedState = false;
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

        private bool TryConvolveCubemap(
            CommandBuffer cmd,
            Texture source,
            int targetResolution)
        {
            if (!m_GgxConvolution.IsSupported || cmd == null || !IsConvolvableCubemap(source))
            {
                ReleaseConvolvedCubemap(false);
                return false;
            }

            EnsureConvolvedCubemap(source, targetResolution);
            if (m_ConvolvedCubemap == null || !m_GgxConvolution.Convolve(cmd, source, m_ConvolvedCubemap))
            {
                ReleaseConvolvedCubemap(false);
                return false;
            }

            EnsureConvolvedCubemapHandle();
            return true;
        }

        private void EnsureConvolvedCubemap(Texture source, int targetResolution)
        {
            if (IsConvolvedCubemapValid(targetResolution))
                return;

            ReleaseConvolvedCubemap(false);

            m_ConvolvedCubemap = new RenderTexture(targetResolution, targetResolution, 0)
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

        private bool IsConvolvedCubemapValid(int targetResolution)
        {
            return m_ConvolvedCubemap != null
                && m_ConvolvedCubemap.IsCreated()
                && m_ConvolvedCubemap.dimension == TextureDimension.Cube
                && m_ConvolvedCubemap.width == targetResolution
                && m_ConvolvedCubemap.height == targetResolution
                && m_ConvolvedCubemap.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static int ResolveTargetResolution(Texture source, int requestedResolution)
        {
            if (source == null)
                return 0;

            var sourceResolution = Mathf.Max(1, Mathf.Min(source.width, source.height));
            if (requestedResolution <= 0)
                return sourceResolution;

            return Mathf.Clamp(requestedResolution, 1, sourceResolution);
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
