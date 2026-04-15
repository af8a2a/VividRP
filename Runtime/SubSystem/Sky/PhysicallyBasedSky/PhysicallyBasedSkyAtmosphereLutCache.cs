using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class PhysicallyBasedSkyAtmosphereLutCache : IDisposable
    {
        private enum SkyLutRebuildReason
        {
            None,
            MissingTexture,
            ParametersChanged
        }

        internal const int MultiScatteringWidth = 32;
        internal const int MultiScatteringHeight = 32;
        internal const int SkyViewWidth = 256;
        internal const int SkyViewHeight = 144;
        internal const int AtmosphericScatteringWidth = 32;
        internal const int AtmosphericScatteringHeight = 32;
        internal const int AtmosphericScatteringDepth = 64;

        private const string ComputePathWarning = "[VividRP] Atmosphere LUT compute shader is missing. Re-sync PipelineResources after Unity imports SkyLUTGenerator.compute.";
        private const string MultiScatteringKernelName = "MultiScatteringLUT";
        private const string SkyViewKernelName = "SkyViewLUT";
        private const string AtmosphericScatteringCameraKernelName = "AtmosphericScatteringLUTCamera";
        private const string AtmosphericScatteringBlurKernelName = "AtmosphericScatteringBlur";
        private const string MultiScatteringTextureName = "VividSkyMultiScatteringLUT";
        private const string SkyViewTextureName = "VividSkySkyViewLUT";
        private const string AtmosphericScatteringTextureName = "VividSkyAtmosphericScatteringLUT";

        private static readonly ProfilingSampler s_MultiScatteringMissingTextureSampler = new("PhysicallyBasedSkyAtmosphereLutCache.RebuildMultiScattering (MissingTexture)");
        private static readonly ProfilingSampler s_MultiScatteringParametersChangedSampler = new("PhysicallyBasedSkyAtmosphereLutCache.RebuildMultiScattering (ParametersChanged)");
        private static readonly ProfilingSampler s_SkyViewMissingTextureSampler = new("PhysicallyBasedSkyAtmosphereLutCache.RebuildSkyView (MissingTexture)");
        private static readonly ProfilingSampler s_SkyViewParametersChangedSampler = new("PhysicallyBasedSkyAtmosphereLutCache.RebuildSkyView (ParametersChanged)");
        private static readonly ProfilingSampler s_AtmosphericScatteringSampler = new("PhysicallyBasedSkyAtmosphereLutCache.RenderAtmosphericScatteringLUT");

        private static readonly int MultiScatteringLutId = Shader.PropertyToID("_MultiScatteringLUT");
        private static readonly int MultiScatteringLutRwId = Shader.PropertyToID("_MultiScatteringLUT_RW");
        private static readonly int SkyViewLutRwId = Shader.PropertyToID("_SkyViewLUT_RW");
        private static readonly int AtmosphericScatteringLutRwId = Shader.PropertyToID("_AtmosphericScatteringLUT_RW");
        private static readonly int CelestialBodyDatasId = Shader.PropertyToID("_CelestialBodyDatas");

        private ComputeShader m_ComputeShader;
        private int m_MultiScatteringKernel = -1;
        private int m_SkyViewKernel = -1;
        private int m_AtmosphericScatteringCameraKernel = -1;
        private int m_AtmosphericScatteringBlurKernel = -1;
        private bool m_IsActive;
        private bool m_HasMaterialParameters;
        private bool m_ShouldRebuildMultiScattering;
        private bool m_ShouldRebuildSkyView;
        private bool m_ShouldRenderAtmosphericScattering;
        private SkyLutRebuildReason m_MultiScatteringRebuildReason;
        private SkyLutRebuildReason m_SkyViewRebuildReason;
        private bool m_MultiScatteringCacheRecreated;
        private bool m_SkyViewCacheRecreated;
        private bool m_AtmosphericScatteringCacheRecreated;
        private int m_CachedMultiScatteringHash;
        private int m_CachedSkyViewHash;
        private int m_NextMultiScatteringHash;
        private int m_NextSkyViewHash;
        private RenderTexture m_CachedMultiScatteringTexture;
        private RTHandle m_CachedMultiScatteringHandle;
        private RenderTexture m_CachedSkyViewTexture;
        private RTHandle m_CachedSkyViewHandle;
        private RenderTexture m_CachedAtmosphericScatteringTexture;
        private RTHandle m_CachedAtmosphericScatteringHandle;
        private PhysicallyBasedSkyShaderParameters m_Parameters;
        private PhysicallyBasedSkyMaterialParameters m_MaterialParameters;
        private readonly PhysicallyBasedSkyCelestialBodyBuffer m_CelestialBodyBuffer = new();

        internal RTHandle MultiScatteringHandle => m_CachedMultiScatteringHandle;

        internal RTHandle SkyViewHandle => m_CachedSkyViewHandle;

        internal RTHandle AtmosphericScatteringHandle => m_CachedAtmosphericScatteringHandle;

        internal void Build(VividRPCoreResources resources)
        {
            m_ComputeShader = resources?.AtmosphereLUTCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning(ComputePathWarning);
                return;
            }

            m_MultiScatteringKernel = FindKernel(MultiScatteringKernelName);
            m_SkyViewKernel = FindKernel(SkyViewKernelName);
            m_AtmosphericScatteringCameraKernel = FindKernel(AtmosphericScatteringCameraKernelName);
            m_AtmosphericScatteringBlurKernel = FindKernel(AtmosphericScatteringBlurKernelName);
        }

        internal void Update(in SkyRendererContext context, CommandBuffer cmd)
        {
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();

            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuildForCamera(volume, context, out m_Parameters);
            m_HasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(volume, context, out m_MaterialParameters);
            m_CelestialBodyBuffer.Update(context);
            if (m_HasMaterialParameters)
            {
                m_MaterialParameters.celestialLightCount = m_CelestialBodyBuffer.CelestialLightCount;
                m_MaterialParameters.celestialBodyCount = m_CelestialBodyBuffer.CelestialBodyCount;
                m_MaterialParameters.celestialLightExposure = Mathf.Max(m_CelestialBodyBuffer.CelestialLightExposure, 1.0f);
            }

            ResetFrameState();

            if (!m_IsActive
                || !m_HasMaterialParameters
                || cmd == null
                || m_ComputeShader == null
                || m_MultiScatteringKernel < 0
                || m_SkyViewKernel < 0
                || m_AtmosphericScatteringCameraKernel < 0)
            {
                return;
            }

            EnsureCachedLutResources();

            m_NextMultiScatteringHash = ComputeMultiScatteringHash(m_MaterialParameters);
            m_NextSkyViewHash = ComputeSkyViewHash(
                m_Parameters,
                m_MaterialParameters,
                m_NextMultiScatteringHash,
                m_CelestialBodyBuffer.CelestialLightHash);

            m_MultiScatteringRebuildReason = ResolveRebuildReason(
                m_MultiScatteringCacheRecreated,
                m_CachedMultiScatteringHash,
                m_NextMultiScatteringHash);
            m_SkyViewRebuildReason = ResolveRebuildReason(
                m_SkyViewCacheRecreated,
                m_CachedSkyViewHash,
                m_NextSkyViewHash);

            m_ShouldRebuildMultiScattering = m_MultiScatteringRebuildReason != SkyLutRebuildReason.None;
            m_ShouldRebuildSkyView = m_ShouldRebuildMultiScattering || m_SkyViewRebuildReason != SkyLutRebuildReason.None;
            m_ShouldRenderAtmosphericScattering = m_CachedAtmosphericScatteringTexture != null
                                                 && m_CachedAtmosphericScatteringTexture.IsCreated();

            if (m_ShouldRebuildMultiScattering)
            {
                using (new ProfilingScope(cmd, GetMultiScatteringRebuildSampler(m_MultiScatteringRebuildReason)))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_MultiScatteringKernel,
                        MultiScatteringLutRwId,
                        m_CachedMultiScatteringTexture);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_MultiScatteringKernel,
                        MultiScatteringWidth,
                        MultiScatteringHeight,
                        1);
                }

                m_CachedMultiScatteringHash = m_NextMultiScatteringHash;
            }

            if (m_ShouldRebuildSkyView)
            {
                using (new ProfilingScope(cmd, GetSkyViewRebuildSampler(m_SkyViewRebuildReason)))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_SkyViewKernel,
                        MultiScatteringLutId,
                        m_CachedMultiScatteringTexture);
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_SkyViewKernel,
                        SkyViewLutRwId,
                        m_CachedSkyViewTexture);
                    cmd.SetComputeBufferParam(
                        m_ComputeShader,
                        m_SkyViewKernel,
                        CelestialBodyDatasId,
                        m_CelestialBodyBuffer.Buffer);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_SkyViewKernel,
                        CoreUtils.DivRoundUp(SkyViewWidth, 8),
                        CoreUtils.DivRoundUp(SkyViewHeight, 8),
                        1);
                }

                m_CachedSkyViewHash = m_NextSkyViewHash;
            }

            if (m_ShouldRenderAtmosphericScattering)
            {
                using (new ProfilingScope(cmd, s_AtmosphericScatteringSampler))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_AtmosphericScatteringCameraKernel,
                        MultiScatteringLutId,
                        m_CachedMultiScatteringTexture);
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_AtmosphericScatteringCameraKernel,
                        AtmosphericScatteringLutRwId,
                        m_CachedAtmosphericScatteringTexture);
                    cmd.SetComputeBufferParam(
                        m_ComputeShader,
                        m_AtmosphericScatteringCameraKernel,
                        CelestialBodyDatasId,
                        m_CelestialBodyBuffer.Buffer);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_AtmosphericScatteringCameraKernel,
                        AtmosphericScatteringWidth,
                        AtmosphericScatteringHeight,
                        1);

                    if (m_AtmosphericScatteringBlurKernel >= 0)
                    {
                        cmd.SetComputeTextureParam(
                            m_ComputeShader,
                            m_AtmosphericScatteringBlurKernel,
                            AtmosphericScatteringLutRwId,
                            m_CachedAtmosphericScatteringTexture);
                        cmd.DispatchCompute(
                            m_ComputeShader,
                            m_AtmosphericScatteringBlurKernel,
                            1,
                            1,
                            AtmosphericScatteringDepth);
                    }
                }
            }
        }

        internal bool TryGetSkyViewLut(int skyViewHash, out Texture skyViewTexture)
        {
            if (m_CachedSkyViewTexture != null
                && m_CachedSkyViewTexture.IsCreated()
                && m_CachedSkyViewHash == skyViewHash)
            {
                skyViewTexture = m_CachedSkyViewTexture;
                return true;
            }

            skyViewTexture = null;
            return false;
        }

        internal static int ComputeSkyViewLutHash(
            PhysicallyBasedSkyShaderParameters skyParameters,
            PhysicallyBasedSkyMaterialParameters materialParameters,
            in SkyRendererContext context)
        {
            var multiScatteringHash = ComputeMultiScatteringHash(materialParameters);
            var celestialLightHash = PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialLightHash(context);
            return ComputeSkyViewHash(skyParameters, materialParameters, multiScatteringHash, celestialLightHash);
        }

        public void Dispose()
        {
            m_ComputeShader = null;
            m_MultiScatteringKernel = -1;
            m_SkyViewKernel = -1;
            m_AtmosphericScatteringCameraKernel = -1;
            m_AtmosphericScatteringBlurKernel = -1;
            m_CelestialBodyBuffer.Dispose();
            ReleaseCachedLutResources();
        }

        private int FindKernel(string kernelName)
        {
            return m_ComputeShader != null && m_ComputeShader.HasKernel(kernelName)
                ? m_ComputeShader.FindKernel(kernelName)
                : -1;
        }

        private void BindCommonParameters(CommandBuffer cmd)
        {
            PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_ComputeShader, m_Parameters, m_MaterialParameters);
        }

        private void ResetFrameState()
        {
            m_ShouldRebuildMultiScattering = false;
            m_ShouldRebuildSkyView = false;
            m_ShouldRenderAtmosphericScattering = false;
            m_MultiScatteringRebuildReason = SkyLutRebuildReason.None;
            m_SkyViewRebuildReason = SkyLutRebuildReason.None;
            m_MultiScatteringCacheRecreated = false;
            m_SkyViewCacheRecreated = false;
            m_AtmosphericScatteringCacheRecreated = false;
            m_NextMultiScatteringHash = 0;
            m_NextSkyViewHash = 0;
        }

        private void EnsureCachedLutResources()
        {
            m_MultiScatteringCacheRecreated = Ensure2DLutResource(
                ref m_CachedMultiScatteringTexture,
                ref m_CachedMultiScatteringHandle,
                MultiScatteringWidth,
                MultiScatteringHeight,
                MultiScatteringTextureName);
            m_SkyViewCacheRecreated = Ensure2DLutResource(
                ref m_CachedSkyViewTexture,
                ref m_CachedSkyViewHandle,
                SkyViewWidth,
                SkyViewHeight,
                SkyViewTextureName);
            m_AtmosphericScatteringCacheRecreated = Ensure3DLutResource(
                ref m_CachedAtmosphericScatteringTexture,
                ref m_CachedAtmosphericScatteringHandle,
                AtmosphericScatteringWidth,
                AtmosphericScatteringHeight,
                AtmosphericScatteringDepth,
                AtmosphericScatteringTextureName);
        }

        private bool Ensure2DLutResource(
            ref RenderTexture texture,
            ref RTHandle handle,
            int width,
            int height,
            string name)
        {
            if (Is2DLutResourceValid(texture, handle, width, height))
                return false;

            ReleaseLutResource(ref texture, ref handle);

            texture = new RenderTexture(width, height, 0)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex2D,
                volumeDepth = 1,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            handle = RTHandles.Alloc(texture);
            return true;
        }

        private bool Ensure3DLutResource(
            ref RenderTexture texture,
            ref RTHandle handle,
            int width,
            int height,
            int depth,
            string name)
        {
            if (Is3DLutResourceValid(texture, handle, width, height, depth))
                return false;

            ReleaseLutResource(ref texture, ref handle);

            texture = new RenderTexture(width, height, 0)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex3D,
                volumeDepth = depth,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            handle = RTHandles.Alloc(texture);
            return true;
        }

        private void ReleaseCachedLutResources()
        {
            ReleaseLutResource(ref m_CachedMultiScatteringTexture, ref m_CachedMultiScatteringHandle);
            ReleaseLutResource(ref m_CachedSkyViewTexture, ref m_CachedSkyViewHandle);
            ReleaseLutResource(ref m_CachedAtmosphericScatteringTexture, ref m_CachedAtmosphericScatteringHandle);
            m_CachedMultiScatteringHash = 0;
            m_CachedSkyViewHash = 0;
        }

        private static void ReleaseLutResource(ref RenderTexture texture, ref RTHandle handle)
        {
            if (handle != null)
            {
                handle.Release();
                handle = null;
            }

            if (texture == null)
                return;

            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }

        private static bool Is2DLutResourceValid(RenderTexture texture, RTHandle handle, int width, int height)
        {
            return texture != null
                && handle != null
                && texture.IsCreated()
                && texture.width == width
                && texture.height == height
                && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat
                && texture.enableRandomWrite
                && texture.dimension == TextureDimension.Tex2D;
        }

        private static bool Is3DLutResourceValid(RenderTexture texture, RTHandle handle, int width, int height, int depth)
        {
            return texture != null
                && handle != null
                && texture.IsCreated()
                && texture.width == width
                && texture.height == height
                && texture.volumeDepth == depth
                && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat
                && texture.enableRandomWrite
                && texture.dimension == TextureDimension.Tex3D;
        }

        private static SkyLutRebuildReason ResolveRebuildReason(bool cacheRecreated, int cachedHash, int nextHash)
        {
            if (cacheRecreated)
                return SkyLutRebuildReason.MissingTexture;

            return cachedHash != nextHash
                ? SkyLutRebuildReason.ParametersChanged
                : SkyLutRebuildReason.None;
        }

        private static int ComputeMultiScatteringHash(PhysicallyBasedSkyMaterialParameters parameters)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, parameters.planetCenterRadius.w);
                hash = AppendHash(hash, parameters.atmosphericRadius);
                hash = AppendHash(hash, parameters.airSeaLevelExtinction);
                hash = AppendHash(hash, parameters.airSeaLevelScattering);
                hash = AppendHash(hash, parameters.aerosolSeaLevelScattering);
                hash = AppendHash(hash, parameters.ozoneSeaLevelExtinction);
                hash = AppendHash(hash, parameters.groundAlbedoPlanetRadius);
                hash = AppendHash(hash, parameters.aerosolSeaLevelExtinction);
                hash = AppendHash(hash, parameters.airDensityFalloff);
                hash = AppendHash(hash, parameters.airScaleHeight);
                hash = AppendHash(hash, parameters.aerosolDensityFalloff);
                hash = AppendHash(hash, parameters.aerosolScaleHeight);
                hash = AppendHash(hash, parameters.ozoneScaleOffset);
                hash = AppendHash(hash, parameters.ozoneLayerStart);
                hash = AppendHash(hash, parameters.ozoneLayerEnd);
                hash = AppendHash(hash, parameters.atmosphericDepth);
                hash = AppendHash(hash, parameters.rcpAtmosphericDepth);
                return hash;
            }
        }

        private static int ComputeSkyViewHash(
            PhysicallyBasedSkyShaderParameters skyParameters,
            PhysicallyBasedSkyMaterialParameters materialParameters,
            int multiScatteringHash,
            int celestialLightHash)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, multiScatteringHash);
                hash = AppendHash(hash, celestialLightHash);
                hash = AppendHash(hash, skyParameters.skySunDirection);
                hash = AppendHash(hash, skyParameters.skySunColor);
                hash = AppendHash(hash, materialParameters.celestialLightExposure);
                return hash;
            }
        }

        private static int AppendHash(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }

        private static int AppendHash(int hash, float value)
        {
            return AppendHash(hash, value.GetHashCode());
        }

        private static int AppendHash(int hash, Vector4 value)
        {
            unchecked
            {
                hash = AppendHash(hash, value.x);
                hash = AppendHash(hash, value.y);
                hash = AppendHash(hash, value.z);
                hash = AppendHash(hash, value.w);
                return hash;
            }
        }

        private static ProfilingSampler GetMultiScatteringRebuildSampler(SkyLutRebuildReason reason)
        {
            return reason == SkyLutRebuildReason.MissingTexture
                ? s_MultiScatteringMissingTextureSampler
                : s_MultiScatteringParametersChangedSampler;
        }

        private static ProfilingSampler GetSkyViewRebuildSampler(SkyLutRebuildReason reason)
        {
            return reason == SkyLutRebuildReason.MissingTexture
                ? s_SkyViewMissingTextureSampler
                : s_SkyViewParametersChangedSampler;
        }
    }
}
