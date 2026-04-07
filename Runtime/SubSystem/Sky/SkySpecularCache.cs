using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : System.IDisposable
    {
        private enum SpecularPrefilterRebuildReason
        {
            None,
            MissingTexture,
            SourceChanged,
            SkyChanged,
            ResolutionChanged,
            QualityChanged
        }

        private const string PrefilterKernelName = "SkySpecularPrefilter";

        private static readonly int SkySpecularSourceCubemapId = Shader.PropertyToID("_SkySpecularSourceCubemap");
        private static readonly int SkySpecularMipOutputId = Shader.PropertyToID("_SkySpecularMipOutput");
        private static readonly int SkySpecularMipParamsId = Shader.PropertyToID("_SkySpecularMipParams");
        private static readonly ProfilingSampler s_PrefilterMissingTextureSampler = new("SkySpecularCache.RebuildPrefilter (MissingTexture)");
        private static readonly ProfilingSampler s_PrefilterSourceChangedSampler = new("SkySpecularCache.RebuildPrefilter (SourceChanged)");
        private static readonly ProfilingSampler s_PrefilterSkyChangedSampler = new("SkySpecularCache.RebuildPrefilter (SkyChanged)");
        private static readonly ProfilingSampler s_PrefilterResolutionChangedSampler = new("SkySpecularCache.RebuildPrefilter (ResolutionChanged)");
        private static readonly ProfilingSampler s_PrefilterQualityChangedSampler = new("SkySpecularCache.RebuildPrefilter (QualityChanged)");

        private ComputeShader m_ConvolutionCompute;
        private int m_PrefilterKernel = -1;

        private RTHandle m_FilteredCubemapHandle;
        private RenderTexture m_FilteredCubemap;
        private RenderTexture m_FilteredCubemapFaces;

        private RTHandle m_CachedSourceCubemapHandle;
        private Texture m_CachedSource;
        private Cubemap m_FallbackCubemap;
        private RTHandle m_FallbackCubemapHandle;
        private int m_CachedSkyHash;
        private int m_CachedResolution;
        private int m_CachedMaxSampleCount;

        internal bool IsValid =>
            m_CachedSource != null
            || m_CachedSourceCubemapHandle != null
            || m_FilteredCubemapHandle != null
            || m_FilteredCubemap != null
            || m_FilteredCubemapFaces != null
            || m_FallbackCubemap != null
            || m_FallbackCubemapHandle != null;

        internal RTHandle Cubemap
        {
            get
            {
                if (m_FilteredCubemapHandle != null)
                    return m_FilteredCubemapHandle;

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
                if (m_FilteredCubemap != null)
                    return Mathf.Max(0, m_FilteredCubemap.mipmapCount - 1);

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
            m_ConvolutionCompute = resources?.SkyAmbientProbeConvolutionCompute;
            m_PrefilterKernel = m_ConvolutionCompute != null && m_ConvolutionCompute.HasKernel(PrefilterKernelName)
                ? m_ConvolutionCompute.FindKernel(PrefilterKernelName)
                : -1;
        }

        internal void Update(CommandBuffer cmd, Texture source, int skyHash, int requestedResolution, int requestedMaxSampleCount)
        {
            var resolvedResolution = ResolvePrefilterResolution(source, requestedResolution);
            var resolvedMaxSampleCount = ResolvePrefilterMaxSampleCount(requestedMaxSampleCount);
            var canPrefilter = CanPrefilter(source) && cmd != null;
            var hasUsableCachedCubemap = canPrefilter
                ? m_FilteredCubemapHandle != null
                : m_FilteredCubemapHandle != null || m_CachedSourceCubemapHandle != null;

            if (ReferenceEquals(m_CachedSource, source)
                && skyHash == m_CachedSkyHash
                && resolvedResolution == m_CachedResolution
                && resolvedMaxSampleCount == m_CachedMaxSampleCount
                && hasUsableCachedCubemap)
                return;

            if (source == null)
            {
                ReleaseCachedSourceCubemapHandle();
                ReleaseFilteredCubemapResources();
                m_CachedSkyHash = 0;
                m_CachedResolution = 0;
                m_CachedMaxSampleCount = 0;
                return;
            }

            var rebuildReason = canPrefilter
                ? ResolvePrefilterRebuildReason(source, skyHash, resolvedResolution, resolvedMaxSampleCount)
                : SpecularPrefilterRebuildReason.None;

            if (!ReferenceEquals(m_CachedSource, source))
            {
                ReleaseCachedSourceCubemapHandle(false);
                ReleaseFilteredCubemapResources();
                m_CachedSource = source;
            }

            if (canPrefilter)
            {
                EnsurePrefilterResources(source, resolvedResolution);

                if (m_FilteredCubemap != null && m_FilteredCubemapFaces != null)
                {
                    using (new ProfilingScope(cmd, GetPrefilterRebuildSampler(rebuildReason)))
                    {
                        RebuildPrefilteredCubemap(cmd, source, resolvedMaxSampleCount);
                    }
                    EnsureFilteredCubemapHandle();
                }
            }
            else
            {
                ReleaseFilteredCubemapResources();
            }

            m_CachedSkyHash = skyHash;
            m_CachedResolution = resolvedResolution;
            m_CachedMaxSampleCount = resolvedMaxSampleCount;
        }

        internal void Update(Texture source, int skyHash, int requestedResolution, int requestedMaxSampleCount)
        {
            Update(null, source, skyHash, requestedResolution, requestedMaxSampleCount);
        }

        public void Dispose()
        {
            ReleaseCachedSourceCubemapHandle();
            ReleaseFilteredCubemapResources();

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
            m_CachedResolution = 0;
            m_CachedMaxSampleCount = 0;
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

        private void EnsureFilteredCubemapHandle()
        {
            if (m_FilteredCubemapHandle != null || m_FilteredCubemap == null)
                return;

            m_FilteredCubemapHandle = RTHandles.Alloc(m_FilteredCubemap);
        }

        private void EnsurePrefilterResources(Texture source, int requestedResolution)
        {
            var faceSize = ResolvePrefilterResolution(source, requestedResolution);
            if (m_FilteredCubemap != null
                && m_FilteredCubemap.width == faceSize
                && m_FilteredCubemap.height == faceSize
                && m_FilteredCubemap.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat
                && m_FilteredCubemapFaces != null
                && m_FilteredCubemapFaces.width == faceSize
                && m_FilteredCubemapFaces.height == faceSize
                && m_FilteredCubemapFaces.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat)
            {
                return;
            }

            ReleaseFilteredCubemapResources();

            m_FilteredCubemap = new RenderTexture(faceSize, faceSize, 0)
            {
                name = "VividSkySpecularPrefiltered",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Cube,
                volumeDepth = 6,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FilteredCubemap.Create();

            m_FilteredCubemapFaces = new RenderTexture(faceSize, faceSize, 0)
            {
                name = "VividSkySpecularPrefilteredFaces",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FilteredCubemapFaces.Create();
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

        private void ReleaseFilteredCubemapResources()
        {
            if (m_FilteredCubemapHandle != null)
            {
                m_FilteredCubemapHandle.Release();
                m_FilteredCubemapHandle = null;
            }

            if (m_FilteredCubemap != null)
            {
                m_FilteredCubemap.Release();
                CoreUtils.Destroy(m_FilteredCubemap);
                m_FilteredCubemap = null;
            }

            if (m_FilteredCubemapFaces != null)
            {
                m_FilteredCubemapFaces.Release();
                CoreUtils.Destroy(m_FilteredCubemapFaces);
                m_FilteredCubemapFaces = null;
            }
        }

        private bool CanPrefilter(Texture source)
        {
            return source != null
                && source.dimension == TextureDimension.Cube
                && m_ConvolutionCompute != null
                && m_PrefilterKernel >= 0
                && SystemInfo.supportsComputeShaders;
        }

        private void RebuildPrefilteredCubemap(CommandBuffer cmd, Texture source, int maxSampleCount)
        {
            if (cmd == null || source == null || m_FilteredCubemap == null || m_FilteredCubemapFaces == null)
                return;

            var mipCount = Mathf.Max(1, m_FilteredCubemap.mipmapCount);
            cmd.SetComputeTextureParam(m_ConvolutionCompute, m_PrefilterKernel, SkySpecularSourceCubemapId, source);

            var maxMip = Mathf.Max(mipCount - 1, 1);
            for (var mip = 0; mip < mipCount; mip++)
            {
                var mipSize = Mathf.Max(1, m_FilteredCubemap.width >> mip);
                cmd.SetComputeVectorParam(
                    m_ConvolutionCompute,
                    SkySpecularMipParamsId,
                    new Vector4(mip, maxMip, maxSampleCount, 0.0f));
                cmd.SetComputeTextureParam(
                    m_ConvolutionCompute,
                    m_PrefilterKernel,
                    SkySpecularMipOutputId,
                    m_FilteredCubemapFaces,
                    mip);
                cmd.DispatchCompute(
                    m_ConvolutionCompute,
                    m_PrefilterKernel,
                    CoreUtils.DivRoundUp(mipSize, 8),
                    CoreUtils.DivRoundUp(mipSize, 8),
                    6);

                for (var face = 0; face < 6; face++)
                    cmd.CopyTexture(m_FilteredCubemapFaces, face, mip, m_FilteredCubemap, face, mip);
            }
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

        private static int ResolvePrefilterResolution(Texture source, int requestedResolution)
        {
            if (source == null)
                return 0;

            var sourceResolution = Mathf.Max(1, source.width);
            if (requestedResolution <= 0)
                return sourceResolution;

            return Mathf.Max(1, Mathf.Min(requestedResolution, sourceResolution));
        }

        private static int ResolvePrefilterMaxSampleCount(int requestedMaxSampleCount)
        {
            return Mathf.Max(0, requestedMaxSampleCount);
        }

        private SpecularPrefilterRebuildReason ResolvePrefilterRebuildReason(
            Texture source,
            int skyHash,
            int resolvedResolution,
            int resolvedMaxSampleCount)
        {
            if (!HasValidFilteredResources(resolvedResolution))
                return SpecularPrefilterRebuildReason.MissingTexture;

            if (!ReferenceEquals(m_CachedSource, source))
                return SpecularPrefilterRebuildReason.SourceChanged;

            if (m_CachedResolution != resolvedResolution)
                return SpecularPrefilterRebuildReason.ResolutionChanged;

            if (m_CachedMaxSampleCount != resolvedMaxSampleCount)
                return SpecularPrefilterRebuildReason.QualityChanged;

            return m_CachedSkyHash != skyHash
                ? SpecularPrefilterRebuildReason.SkyChanged
                : SpecularPrefilterRebuildReason.None;
        }

        private bool HasValidFilteredResources(int resolution)
        {
            return m_FilteredCubemapHandle != null
                && IsFilteredCubemapValid(m_FilteredCubemap, resolution)
                && IsFilteredFaceArrayValid(m_FilteredCubemapFaces, resolution);
        }

        private static bool IsFilteredCubemapValid(RenderTexture texture, int resolution)
        {
            return texture != null
                && texture.IsCreated()
                && texture.dimension == TextureDimension.Cube
                && texture.width == resolution
                && texture.height == resolution
                && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static bool IsFilteredFaceArrayValid(RenderTexture texture, int resolution)
        {
            return texture != null
                && texture.IsCreated()
                && texture.dimension == TextureDimension.Tex2DArray
                && texture.width == resolution
                && texture.height == resolution
                && texture.volumeDepth == 6
                && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat
                && texture.enableRandomWrite;
        }

        private static ProfilingSampler GetPrefilterRebuildSampler(SpecularPrefilterRebuildReason reason)
        {
            return reason switch
            {
                SpecularPrefilterRebuildReason.SourceChanged => s_PrefilterSourceChangedSampler,
                SpecularPrefilterRebuildReason.SkyChanged => s_PrefilterSkyChangedSampler,
                SpecularPrefilterRebuildReason.ResolutionChanged => s_PrefilterResolutionChangedSampler,
                SpecularPrefilterRebuildReason.QualityChanged => s_PrefilterQualityChangedSampler,
                _ => s_PrefilterMissingTextureSampler,
            };
        }
    }
}
