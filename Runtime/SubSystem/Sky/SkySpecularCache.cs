using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : System.IDisposable
    {
        private const string PrefilterKernelName = "SkySpecularPrefilter";

        private static readonly int SkySpecularSourceCubemapId = Shader.PropertyToID("_SkySpecularSourceCubemap");
        private static readonly int SkySpecularMipOutputId = Shader.PropertyToID("_SkySpecularMipOutput");
        private static readonly int SkySpecularMipParamsId = Shader.PropertyToID("_SkySpecularMipParams");

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

        internal void Update(CommandBuffer cmd, Texture source, int skyHash)
        {
            if (ReferenceEquals(m_CachedSource, source) && skyHash == m_CachedSkyHash && (m_FilteredCubemapHandle != null || m_CachedSourceCubemapHandle != null))
                return;

            if (source == null)
            {
                ReleaseCachedSourceCubemapHandle();
                ReleaseFilteredCubemapResources();
                m_CachedSkyHash = 0;
                return;
            }

            if (!ReferenceEquals(m_CachedSource, source))
            {
                ReleaseCachedSourceCubemapHandle(false);
                ReleaseFilteredCubemapResources();
                m_CachedSource = source;
            }

            if (CanPrefilter(source) && cmd != null)
            {
                EnsurePrefilterResources(source);

                if (m_FilteredCubemap != null && m_FilteredCubemapFaces != null)
                {
                    RebuildPrefilteredCubemap(cmd, source);
                    EnsureFilteredCubemapHandle();
                }
            }
            else
            {
                ReleaseFilteredCubemapResources();
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

        private void EnsurePrefilterResources(Texture source)
        {
            var faceSize = Mathf.Max(1, source.width);
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

        private void RebuildPrefilteredCubemap(CommandBuffer cmd, Texture source)
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
                    new Vector4(mip, maxMip, 0.0f, 0.0f));
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
    }
}
