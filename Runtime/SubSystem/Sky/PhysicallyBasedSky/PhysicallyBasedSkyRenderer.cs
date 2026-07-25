using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class PhysicallyBasedSkyRenderer : ISkyRenderer
    {
        private enum SkyRebuildReason
        {
            None,
            MissingTexture,
            ResolutionChanged,
            QualityChanged,
            ParametersChanged,
            FrameRefresh
        }

        private const string PhysicallyBasedSkyShaderName = "Hidden/VividRP/PhysicallyBasedSky";

        private static readonly int SkyViewLutId = Shader.PropertyToID("_SkyViewLUT");
        private static readonly int SkyUseLutId = Shader.PropertyToID("_SkyUseLUT");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int PreExposureBufferId = VividAutoExposureSystem.PreExposureBufferId;
        private static readonly int CelestialBodyDatasId = Shader.PropertyToID("_CelestialBodyDatas");
        private static readonly int MultiScatteringLutId = Shader.PropertyToID("_MultiScatteringLUT");
        private static readonly int MultiScatteringLutRwId = Shader.PropertyToID("_MultiScatteringLUT_RW");
        private static readonly int GroundIrradianceTableId = Shader.PropertyToID("_GroundIrradianceTable");
        private static readonly int AirSingleScatteringTableId = Shader.PropertyToID("_AirSingleScatteringTable");
        private static readonly int AerosolSingleScatteringTableId = Shader.PropertyToID("_AerosolSingleScatteringTable");
        private static readonly int MultipleScatteringTableId = Shader.PropertyToID("_MultipleScatteringTable");
        private static readonly int GroundIrradianceTextureId = Shader.PropertyToID("_GroundIrradianceTexture");
        private static readonly int AirSingleScatteringTextureId = Shader.PropertyToID("_AirSingleScatteringTexture");
        private static readonly int AerosolSingleScatteringTextureId = Shader.PropertyToID("_AerosolSingleScatteringTexture");
        private static readonly int MultipleScatteringTextureId = Shader.PropertyToID("_MultipleScatteringTexture");
        private static readonly ProfilingSampler s_RuntimeCubemapMissingTextureSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (MissingTexture)");
        private static readonly ProfilingSampler s_RuntimeCubemapResolutionChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ResolutionChanged)");
        private static readonly ProfilingSampler s_RuntimeCubemapQualityChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (QualityChanged)");
        private static readonly ProfilingSampler s_RuntimeCubemapParametersChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ParametersChanged)");
        private static readonly ProfilingSampler s_AmbientProbeMissingTextureSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (MissingTexture)");
        private static readonly ProfilingSampler s_AmbientProbeResolutionChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ResolutionChanged)");
        private static readonly ProfilingSampler s_AmbientProbeParametersChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ParametersChanged)");
        private static readonly ProfilingSampler s_AmbientProbeFrameRefreshSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (FrameRefresh)");

        internal const float SunAngularDiameterDegrees = 0.53f;
        internal const float SunIlluminanceScale = 20.0f;
        private const bool RefreshAmbientProbeCubemapEveryFrame = true;
        private const int GroundIrradianceTableSize = 256;
        private const int InScatteredRadianceTableSizeX = 128;
        private const int InScatteredRadianceTableSizeY = 32;
        private const int InScatteredRadianceTableSizeZ = 16;
        private const int InScatteredRadianceTableSizeW = 64;

        private Material m_SkyMaterial;
        private int m_SkyBakingPass = -1;
        private ComputeShader m_GroundIrradiancePrecomputationCompute;
        private ComputeShader m_InScatteredRadiancePrecomputationCompute;
        private ComputeShader m_AtmosphereLutCompute;
        private int m_GroundIrradiancePrecomputationKernel = -1;
        private int m_InScatteredRadiancePrecomputationKernel = -1;
        private int m_MultiScatteringKernel = -1;
        private RenderTexture m_RuntimeSkyCubemap;
        private RenderTexture m_AmbientProbeCubemap;
        private RenderTexture m_GroundIrradianceTable;
        private RenderTexture m_AirSingleScatteringTable;
        private RenderTexture m_AerosolSingleScatteringTable;
        private RenderTexture m_MultipleScatteringTable;
        private RenderTexture m_MultiScatteringLut;
        private int m_RuntimeSkyHash;
        private int m_RuntimeSkyViewSampleCount;
        private int m_AmbientProbeSkyHash;
        private int m_LocalSkyPrecomputationHash;
        private bool m_HasLocalSkyPrecomputation;
        private bool m_LocalSkyPrecomputationRebuiltThisFrame;
        private bool m_SkyViewLutRebuiltForBakingThisFrame;
        private bool m_RuntimeCubemapNeedsDeferredBakingResourceRefresh;
        private RenderGraphTexture m_ColorTarget;
        private RenderGraphTexture m_DepthTexture;
        private RenderGraphTexture m_DirectionalShadowTexture;
        private RenderGraphTexture m_CSMShadowAtlas;
        private bool m_HasConnectedCSMShadowAtlas;
        private Rect m_RenderViewport;
        private bool m_ShouldRenderSky;
        private PhysicallyBasedSkyVolume m_RenderVolume;
        private SkyRendererContext m_RenderContext;
        private PhysicallyBasedSkyShaderParameters m_RenderParameters;
        private PhysicallyBasedSkyMaterialParameters m_RenderMaterialParameters;
        private int m_RenderSkyViewLutHash;
        private int m_LastRenderedSkyViewLutHash;
        private bool m_HasRenderedSkyViewLut;
        private bool m_HasRenderMaterialParameters;
        private bool m_UseLocalSkyPrecomputationForRender;
        private bool m_RuntimeSkyTextureBakeLogged;
        private readonly MaterialPropertyBlock m_RenderPropertyBlock = new();
        private readonly MaterialPropertyBlock m_SkyBakingPropertyBlock = new();
        private readonly PhysicallyBasedSkyAtmosphereLutCache m_AtmosphereLutCache = new();
        private readonly PhysicallyBasedSkyCelestialBodyBuffer m_CelestialBodyBuffer = new();

        public SkyType Type => SkyType.PhysicallyBased;

        public void Build(VividRPCoreResources resources)
        {
            var shader = resources.PhysicallyBasedSkyShader;
            m_AtmosphereLutCompute = resources?.AtmosphereLUTCompute;
            m_GroundIrradiancePrecomputationCompute = resources?.GroundIrradiancePrecomputationCompute;
            m_InScatteredRadiancePrecomputationCompute = resources?.InScatteredRadiancePrecomputationCompute;
            m_AtmosphereLutCache.Build(resources);
            if (shader)
            {
                m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_SkyBakingPass = m_SkyMaterial.FindPass("PhysicallyBasedSkyBaking");
            }

            if (m_AtmosphereLutCompute && m_AtmosphereLutCompute.HasKernel("MultiScatteringLUT"))
                m_MultiScatteringKernel = m_AtmosphereLutCompute.FindKernel("MultiScatteringLUT");
            if (m_GroundIrradiancePrecomputationCompute && m_GroundIrradiancePrecomputationCompute.HasKernel("main"))
                m_GroundIrradiancePrecomputationKernel = m_GroundIrradiancePrecomputationCompute.FindKernel("main");
            if (m_InScatteredRadiancePrecomputationCompute && m_InScatteredRadiancePrecomputationCompute.HasKernel("main"))
                m_InScatteredRadiancePrecomputationKernel = m_InScatteredRadiancePrecomputationCompute.FindKernel("main");
        }

        public bool IsActive()
        {
            return VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume()?.IsActive() ?? false;
        }

        public int GetSkyHash(in SkyRendererContext context)
        {
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            if (volume == null)
                return 0;

            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            var planet = ResolvePlanet(context, volume, skySettings);
            var includeSunInBaking = SkySettingsVolume.GetIncludeSunInBaking(skySettings);
            var intensityMultiplier = volume.GetIntensityMultiplier();

            var hash = 17;
            hash = AppendHash(hash, volume.GetHashCode());
            hash = AppendHash(hash, generatedCubemapViewSampleCount);
            hash = AppendHash(hash, planet.ComputeHashCode());
            hash = AppendHash(hash, includeSunInBaking);
            hash = AppendHash(hash, intensityMultiplier);
            hash = AppendHash(hash, ResolveCameraPosition(context, volume.planetRadius.value));
            hash = AppendHash(hash, PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialBodyHash(context));
            return hash;
        }

        public void UpdateFrameResources(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)
        {
            m_LocalSkyPrecomputationRebuiltThisFrame = false;
            m_SkyViewLutRebuiltForBakingThisFrame = false;
            m_AtmosphereLutCache.Update(context, cmd);
            ApplyAtmosphereLutHandle(skyData);
            UpdateLocalSkyPrecomputation(context, skyData, cmd);
            RebuildCachedRuntimeCubemapIfNeeded(context, skyData, cmd);
            RefreshCachedAmbientProbeCubemap(context, skyData, cmd);
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, int skyHash, bool forceRebuild)
        {
            if (skyData == null)
                return;

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (!volume || !volume.IsActive())
            {
                skyData.Reset();
                return;
            }

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var skyTextureResolution = SkySettingsVolume.GetSkyTextureResolution(skySettings);
            var ambientProbeCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(skySettings);
            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            var intensityMultiplier = volume.GetIntensityMultiplier();
            var runtimeCubemapRebuildReason = ResolveRuntimeCubemapRebuildReason(
                skyHash,
                skyTextureResolution,
                generatedCubemapViewSampleCount);
            if (forceRebuild && runtimeCubemapRebuildReason == SkyRebuildReason.None)
                runtimeCubemapRebuildReason = SkyRebuildReason.ParametersChanged;
            var canRebuildRuntimeCubemap = CanRebuildRuntimeCubemap();
            var hasCommandBuffer = cmd != null;
            var runtimeCubemapBakeAttempted = false;
            var runtimeCubemapBakeSucceeded = false;
            if (runtimeCubemapRebuildReason != SkyRebuildReason.None
                && canRebuildRuntimeCubemap
                && hasCommandBuffer)
            {
                runtimeCubemapBakeAttempted = true;
                EnsureRuntimeCubemap(skyTextureResolution);
                using (new ProfilingScope(cmd, GetRuntimeCubemapRebuildSampler(runtimeCubemapRebuildReason)))
                {
                    runtimeCubemapBakeSucceeded = RebuildRuntimeCubemap(
                        volume,
                        context,
                        cmd,
                        generatedCubemapViewSampleCount);
                    if (runtimeCubemapBakeSucceeded)
                    {
                        m_RuntimeSkyHash = skyHash;
                        m_RuntimeSkyViewSampleCount = generatedCubemapViewSampleCount;
                        skyData.specularCubemapDirty = true;
                    }
                }
            }
            LogRuntimeSkyTextureBakeOnce(
                runtimeCubemapRebuildReason,
                runtimeCubemapBakeAttempted,
                runtimeCubemapBakeSucceeded,
                canRebuildRuntimeCubemap,
                hasCommandBuffer,
                skyTextureResolution,
                generatedCubemapViewSampleCount,
                skyHash,
                forceRebuild);

            var hash = ComputeAmbientProbeHash(volume, context, skySettings, generatedCubemapViewSampleCount, intensityMultiplier);

            var ambientProbeRebuildReason = ResolveAmbientProbeCubemapRebuildReason(hash, ambientProbeCubemapResolution);
            if (forceRebuild && ambientProbeRebuildReason == SkyRebuildReason.None)
                ambientProbeRebuildReason = SkyRebuildReason.ParametersChanged;
            RefreshAmbientProbeCubemap(
                volume,
                context,
                cmd,
                hash,
                ambientProbeCubemapResolution,
                generatedCubemapViewSampleCount,
                ambientProbeRebuildReason,
                RefreshAmbientProbeCubemapEveryFrame);

            var useBakedAmbientProbe = CanBakeAmbientProbe()
                                       && m_AmbientProbeCubemap != null
                                       && m_AmbientProbeSkyHash == hash;

            skyData.activeSkyType = SkyType.PhysicallyBased;
            skyData.specularCubemap = m_RuntimeSkyCubemap;
            skyData.skyContentHash = m_RuntimeSkyHash;
            skyData.tint = Color.white;
            skyData.exposure = 1.0f;
            skyData.rotation = 0.0f;
            skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : m_RuntimeSkyCubemap;
            skyData.ambientProbeTint = Color.white;
            skyData.ambientProbeExposure = 1.0f;
            skyData.ambientProbeRotation = 0.0f;
            skyData.ambientProbeHash = hash;
            ApplyAtmosphereLutHandle(skyData);
        }

        public void PrepareSkyRendering(
            in SkyRendererContext context,
            VividSkyData skyData,
            RenderGraphTexture colorTarget,
            RenderGraphTexture depthTexture,
            RenderGraphTexture skyViewLut,
            RenderGraphTexture directionalShadowTexture,
            RenderGraphTexture csmShadowAtlas,
            bool hasConnectedCSMShadowAtlas)
        {
            m_ColorTarget = colorTarget;
            m_DepthTexture = depthTexture;
            m_DirectionalShadowTexture = directionalShadowTexture;
            m_CSMShadowAtlas = csmShadowAtlas;
            m_HasConnectedCSMShadowAtlas = hasConnectedCSMShadowAtlas;
            m_RenderContext = context;
            m_RenderVolume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            m_RenderViewport = ResolveRenderViewport(context.cameraData, colorTarget);
            m_RenderSkyViewLutHash = 0;
            m_HasRenderMaterialParameters = false;
            m_UseLocalSkyPrecomputationForRender = false;
            m_ShouldRenderSky = m_SkyMaterial != null
                                && skyData != null
                                && skyData.activeSkyType == SkyType.PhysicallyBased
                                && PhysicallyBasedSkyShaderParameterBuilder.TryBuild(
                                    context.cameraData,
                                    skyData,
                                    context.lightData,
                                    out m_RenderParameters);

            if (!m_ShouldRenderSky)
            {
                m_RenderVolume = null;
                return;
            }

            m_HasRenderMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(
                m_RenderVolume,
                context,
                out m_RenderMaterialParameters);
            m_CelestialBodyBuffer.Update(context);
            if (m_HasRenderMaterialParameters)
            {
                SyncRenderMaterialParametersWithCelestialBodyBuffer();
                m_RenderSkyViewLutHash = PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(m_RenderParameters, m_RenderMaterialParameters, m_RenderContext);
            }

            m_UseLocalSkyPrecomputationForRender = m_HasRenderMaterialParameters
                                                   && HasMatchingLocalSkyPrecomputation(
                                                       m_RenderParameters,
                                                       m_RenderMaterialParameters);
            ImportSkyViewLutForPass(skyViewLut);
        }

        public void RenderSky(UnsafePassContext context)
        {
            if (!m_ShouldRenderSky
                || m_SkyMaterial == null
                || m_ColorTarget == null
                || m_DepthTexture == null
                || !m_ColorTarget.innerHandle.IsValid()
                || !m_DepthTexture.innerHandle.IsValid())
            {
                return;
            }

            var cmd = context.GetNativeCommandBuffer();
            RefreshSkyViewLutForRender(cmd);
            m_AtmosphereLutCache.RenderAtmosphericScattering(
                cmd,
                m_HasConnectedCSMShadowAtlas ? m_CSMShadowAtlas : null,
                context.GetOrCreate<VividShadowData>());
            cmd.SetViewport(m_RenderViewport);

            m_SkyMaterial.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);

            var skyViewTexture = ResolveSkyViewTexture();
            if (skyViewTexture != null)
            {
                m_LastRenderedSkyViewLutHash = m_RenderSkyViewLutHash;
                m_HasRenderedSkyViewLut = true;
            }

            var directionalShadowTexture = TextureResolveUtility.ResolveTexture(m_DirectionalShadowTexture) ?? Shader.GetGlobalTexture(DirectionalShadowTextureId);
            var properties = m_RenderPropertyBlock;
            properties.Clear();
            properties.SetMatrix(PixelCoordToViewDirWSId, m_RenderParameters.pixelCoordToViewDirWS);
            properties.SetTexture(SkyViewLutId, skyViewTexture ?? Texture2D.blackTexture);
            properties.SetFloat(SkyUseLutId, skyViewTexture != null ? 1.0f : 0.0f);
            properties.SetTexture(DirectionalShadowTextureId, directionalShadowTexture ?? Texture2D.whiteTexture);
            var preExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(m_RenderContext.exposureData);
            if (preExposureBuffer != null)
                properties.SetBuffer(PreExposureBufferId, preExposureBuffer);
            if (m_HasRenderMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, m_RenderMaterialParameters, m_RenderVolume);

            CoreUtils.SetKeyword(m_SkyMaterial, "LOCAL_SKY", m_UseLocalSkyPrecomputationForRender);
            if (m_UseLocalSkyPrecomputationForRender)
                ApplyLocalSkyPrecomputationTextures(properties);

            CoreUtils.DrawFullScreen(cmd, m_SkyMaterial, properties, 0);
        }

        public void Dispose()
        {
            if (m_RuntimeSkyCubemap != null)
            {
                m_RuntimeSkyCubemap.Release();
                CoreUtils.Destroy(m_RuntimeSkyCubemap);
                m_RuntimeSkyCubemap = null;
            }

            if (m_AmbientProbeCubemap != null)
            {
                m_AmbientProbeCubemap.Release();
                CoreUtils.Destroy(m_AmbientProbeCubemap);
                m_AmbientProbeCubemap = null;
            }

            ReleaseLocalSkyPrecomputationResources();

            if (m_SkyMaterial != null)
            {
                CoreUtils.Destroy(m_SkyMaterial);
                m_SkyMaterial = null;
            }

            m_SkyBakingPass = -1;
            m_GroundIrradiancePrecomputationCompute = null;
            m_InScatteredRadiancePrecomputationCompute = null;
            m_AtmosphereLutCompute = null;
            m_GroundIrradiancePrecomputationKernel = -1;
            m_InScatteredRadiancePrecomputationKernel = -1;
            m_MultiScatteringKernel = -1;
            m_RuntimeSkyHash = 0;
            m_RuntimeSkyViewSampleCount = 0;
            m_AmbientProbeSkyHash = 0;
            m_LocalSkyPrecomputationHash = 0;
            m_HasLocalSkyPrecomputation = false;
            m_LocalSkyPrecomputationRebuiltThisFrame = false;
            m_SkyViewLutRebuiltForBakingThisFrame = false;
            m_RuntimeCubemapNeedsDeferredBakingResourceRefresh = false;
            m_ColorTarget = null;
            m_DepthTexture = null;
            m_DirectionalShadowTexture = null;
            m_CSMShadowAtlas = null;
            m_HasConnectedCSMShadowAtlas = false;
            m_RenderViewport = default;
            m_ShouldRenderSky = false;
            m_RenderVolume = null;
            m_RenderContext = default;
            m_RenderParameters = default;
            m_RenderMaterialParameters = default;
            m_RenderSkyViewLutHash = 0;
            m_LastRenderedSkyViewLutHash = 0;
            m_HasRenderedSkyViewLut = false;
            m_HasRenderMaterialParameters = false;
            m_UseLocalSkyPrecomputationForRender = false;
            m_RuntimeSkyTextureBakeLogged = false;
            m_AtmosphereLutCache.Dispose();
            m_CelestialBodyBuffer.Dispose();
        }

        private void UpdateLocalSkyPrecomputation(
            in SkyRendererContext context,
            VividSkyData skyData,
            CommandBuffer cmd)
        {
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null
                || !volume.IsActive()
                || skyData == null
                || skyData.activeSkyType != SkyType.PhysicallyBased
                || !PhysicallyBasedSkyShaderParameterBuilder.TryBuild(
                    context.cameraData,
                    skyData,
                    context.lightData,
                    out var skyParameters)
                || !PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(
                    volume,
                    context,
                    out var materialParameters)
                || !UsesWorldSpacePrecomputation(materialParameters))
            {
                return;
            }

            TryPrepareLocalSkyPrecomputation(volume, context, cmd, skyParameters, materialParameters);
        }

        private void RefreshCachedAmbientProbeCubemap(
            in SkyRendererContext context,
            VividSkyData skyData,
            CommandBuffer cmd)
        {
            if (!RefreshAmbientProbeCubemapEveryFrame
                || cmd == null
                || skyData == null
                || skyData.activeSkyType != SkyType.PhysicallyBased)
            {
                return;
            }

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsActive())
                return;

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var ambientProbeCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(skySettings);
            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            var intensityMultiplier = volume.GetIntensityMultiplier();
            var hash = ComputeAmbientProbeHash(volume, context, skySettings, generatedCubemapViewSampleCount, intensityMultiplier);
            if (skyData.ambientProbeHash != hash)
                return;

            var ambientProbeRebuildReason = ResolveAmbientProbeCubemapRebuildReason(hash, ambientProbeCubemapResolution);
            if (RefreshAmbientProbeCubemap(
                    volume,
                    context,
                    cmd,
                    hash,
                    ambientProbeCubemapResolution,
                    generatedCubemapViewSampleCount,
                    ambientProbeRebuildReason,
                    true))
            {
                skyData.ambientProbeCubemap = m_AmbientProbeCubemap;
                skyData.ambientProbeTint = Color.white;
                skyData.ambientProbeExposure = 1.0f;
                skyData.ambientProbeRotation = 0.0f;
                skyData.ambientProbeHash = hash;
            }
        }

        private void RebuildCachedRuntimeCubemapIfNeeded(
            in SkyRendererContext context,
            VividSkyData skyData,
            CommandBuffer cmd)
        {
            if (cmd == null
                || skyData == null
                || skyData.activeSkyType != SkyType.PhysicallyBased)
            {
                return;
            }

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsActive())
                return;

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var skyHash = GetSkyHash(context);
            var skyTextureResolution = SkySettingsVolume.GetSkyTextureResolution(skySettings);
            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            if (skyHash != skyData.skyHash)
            {
                return;
            }

            var runtimeCubemapRebuildReason = ResolveRuntimeCubemapRebuildReason(
                skyHash,
                skyTextureResolution,
                generatedCubemapViewSampleCount);
            if (runtimeCubemapRebuildReason == SkyRebuildReason.None
                && m_RuntimeCubemapNeedsDeferredBakingResourceRefresh)
            {
                runtimeCubemapRebuildReason = SkyRebuildReason.ParametersChanged;
            }

            if (runtimeCubemapRebuildReason == SkyRebuildReason.None || !CanRebuildRuntimeCubemap())
                return;

            EnsureRuntimeCubemap(skyTextureResolution);
            using (new ProfilingScope(cmd, GetRuntimeCubemapRebuildSampler(runtimeCubemapRebuildReason)))
            {
                if (RebuildRuntimeCubemap(
                        volume,
                        context,
                        cmd,
                        generatedCubemapViewSampleCount))
                {
                    m_RuntimeSkyHash = skyHash;
                    m_RuntimeSkyViewSampleCount = generatedCubemapViewSampleCount;
                    skyData.specularCubemap = m_RuntimeSkyCubemap;
                    skyData.skyContentHash = m_RuntimeSkyHash;
                    skyData.specularCubemapDirty = true;
                }
            }
        }

        private bool HasMatchingLocalSkyPrecomputation(
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            return m_HasLocalSkyPrecomputation
                   && !m_LocalSkyPrecomputationRebuiltThisFrame
                   && m_LocalSkyPrecomputationHash == ComputeLocalSkyPrecomputationHash(skyParameters, materialParameters)
                   && HasLocalSkyPrecomputationTextures();
        }

        internal static Vector3 ResolveSunDirection(in SkyRendererContext context)
        {
            if (context.lightData != null
                && context.lightData.hasMainDirectionalLight
                && HasPositiveColor(context.lightData.mainDirectionalLight.color))
            {
                return context.lightData.mainDirectionalLight.directionWS.normalized;
            }

            if (TryResolveFallbackSunLight(out var sunLight))
                return (-sunLight.transform.forward).normalized;

            return Vector3.up;
        }

        internal static Color ResolveSunColor(in SkyRendererContext context)
        {
            if (context.lightData != null && context.lightData.hasMainDirectionalLight)
            {
                var color = context.lightData.mainDirectionalLight.color;
                if (HasPositiveColor(color))
                    return new Color(color.x, color.y, color.z, 1.0f);
            }

            if (TryResolveFallbackSunLight(out var sunLight))
                return VividLightRenderDatabase.EvaluateLightColor(sunLight);

            return Color.white;
        }

        internal static bool TryResolveFallbackSunLight(out Light sunLight)
        {
            sunLight = null;

            if (IsUsableSkyDirectionalLight(RenderSettings.sun))
            {
                sunLight = RenderSettings.sun;
                return true;
            }

            var sceneLights = UnityEngine.Object.FindObjectsByType<Light>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var brightestIntensity = 0.0f;
            for (var lightIndex = 0; lightIndex < sceneLights.Length; lightIndex++)
            {
                var candidate = sceneLights[lightIndex];
                if (!IsUsableSkyDirectionalLight(candidate))
                    continue;

                var candidateColor = VividLightRenderDatabase.EvaluateLightColor(candidate);
                var candidateIntensity = Mathf.Max(candidateColor.r, candidateColor.g, candidateColor.b);
                if (candidateIntensity <= brightestIntensity)
                    continue;

                brightestIntensity = candidateIntensity;
                sunLight = candidate;
            }

            return sunLight != null;
        }

        private static bool IsUsableSkyDirectionalLight(Light light)
        {
            if (!light
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.type != LightType.Directional)
            {
                return false;
            }

            if (light.TryGetComponent(out VividAdditionalLightData additionalData)
                && !additionalData.interactsWithSky)
            {
                return false;
            }

            var color = VividLightRenderDatabase.EvaluateLightColor(light);
            return color.maxColorComponent > 0.0f;
        }

        private static bool HasPositiveColor(Vector3 color)
        {
            return Mathf.Max(color.x, color.y, color.z) > 0.0f;
        }

        internal static Vector3 ResolveCameraPosition(in SkyRendererContext context, float planetRadius)
        {
            var worldCameraPosition = ResolveWorldCameraPosition(context);
            var planet = SkyPlanet.Resolve(planetRadius, VividVolumeManagerUtility.GetSkySettingsVolume(), worldCameraPosition);
            return planet.GetCameraPositionPS(worldCameraPosition);
        }

        private bool CanRebuildRuntimeCubemap()
        {
            return CanBakeSky();
        }

        private bool CanBakeAmbientProbe()
        {
            return CanBakeSky();
        }

        private bool CanBakeSky()
        {
            return m_SkyMaterial != null && m_SkyBakingPass >= 0;
        }

        private void LogRuntimeSkyTextureBakeOnce(
            SkyRebuildReason rebuildReason,
            bool attempted,
            bool succeeded,
            bool canRebuild,
            bool hasCommandBuffer,
            int resolution,
            int viewSampleCount,
            int skyHash,
            bool forceRebuild)
        {
            if (m_RuntimeSkyTextureBakeLogged)
                return;

            m_RuntimeSkyTextureBakeLogged = true;
            Debug.Log(
                $"[VividRP][SkyTextureBake] attempted={attempted}, succeeded={succeeded}, " +
                $"reason={rebuildReason}, canRebuild={canRebuild}, hasCommandBuffer={hasCommandBuffer}, " +
                $"resolution={resolution}, viewSampleCount={viewSampleCount}, skyHash={skyHash}, forceRebuild={forceRebuild}, " +
                $"material={(m_SkyMaterial != null)}, pass={m_SkyBakingPass}, " +
                $"runtimeCubemapCreated={(m_RuntimeSkyCubemap != null && m_RuntimeSkyCubemap.IsCreated())}, " +
                $"runtimeSkyHash={m_RuntimeSkyHash}, runtimeViewSampleCount={m_RuntimeSkyViewSampleCount}");
        }

        private SkyRebuildReason ResolveRuntimeCubemapRebuildReason(
            int hash,
            int resolution,
            int viewSampleCount)
        {
            if (m_RuntimeSkyCubemap == null
                || !m_RuntimeSkyCubemap.IsCreated())
            {
                return SkyRebuildReason.MissingTexture;
            }

            if (!IsCubemapValid(m_RuntimeSkyCubemap, resolution))
                return SkyRebuildReason.ResolutionChanged;

            if (m_RuntimeSkyViewSampleCount != viewSampleCount)
                return SkyRebuildReason.QualityChanged;

            return m_RuntimeSkyHash != hash
                ? SkyRebuildReason.ParametersChanged
                : SkyRebuildReason.None;
        }

        private SkyRebuildReason ResolveAmbientProbeCubemapRebuildReason(int hash, int resolution)
        {
            if (m_AmbientProbeCubemap == null || !m_AmbientProbeCubemap.IsCreated())
                return SkyRebuildReason.MissingTexture;

            if (!IsCubemapValid(m_AmbientProbeCubemap, resolution))
                return SkyRebuildReason.ResolutionChanged;

            return m_AmbientProbeSkyHash != hash
                ? SkyRebuildReason.ParametersChanged
                : SkyRebuildReason.None;
        }

        private void EnsureRuntimeCubemap(int resolution)
        {
            if (!IsCubemapValid(m_RuntimeSkyCubemap, resolution))
            {
                if (m_RuntimeSkyCubemap)
                {
                    m_RuntimeSkyCubemap.Release();
                    CoreUtils.Destroy(m_RuntimeSkyCubemap);
                    m_RuntimeSkyCubemap = null;
                }

                m_RuntimeSkyCubemap = new RenderTexture(resolution, resolution, 0)
                {
                    name = "VividPhysicallyBasedSky",
                    hideFlags = HideFlags.HideAndDontSave,
                    dimension = TextureDimension.Cube,
                    volumeDepth = 6,
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                m_RuntimeSkyCubemap.Create();
            }
        }

        private void EnsureAmbientProbeCubemap(int resolution)
        {
            if (IsCubemapValid(m_AmbientProbeCubemap, resolution))
                return;

            if (m_AmbientProbeCubemap != null)
            {
                m_AmbientProbeCubemap.Release();
                CoreUtils.Destroy(m_AmbientProbeCubemap);
                m_AmbientProbeCubemap = null;
            }

            m_AmbientProbeCubemap = new RenderTexture(resolution, resolution, 0)
            {
                name = "VividPhysicallyBasedSkyAmbientProbe",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Cube,
                volumeDepth = 6,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_AmbientProbeCubemap.Create();
        }

        private bool RebuildRuntimeCubemap(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            int runtimeCubemapViewSampleCount)
        {
            if (cmd == null
                || !m_RuntimeSkyCubemap
                || !TryBuildSkyBakingProperties(volume, context, cmd, runtimeCubemapViewSampleCount, out var properties))
            {
                return false;
            }

            SkyCubemapBakingUtility.RenderSkyToCubemap(
                cmd,
                m_RuntimeSkyCubemap,
                m_SkyMaterial,
                properties,
                m_SkyBakingPass);
            SkyCubemapBakingUtility.GenerateCubemapMipmaps(cmd, m_RuntimeSkyCubemap);
            m_RuntimeCubemapNeedsDeferredBakingResourceRefresh =
                m_LocalSkyPrecomputationRebuiltThisFrame || m_SkyViewLutRebuiltForBakingThisFrame;
            return true;
        }

        private bool RebuildAmbientProbeCubemap(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            int viewSampleCount)
        {
            if (cmd == null
                || !m_AmbientProbeCubemap
                || !TryBuildSkyBakingProperties(volume, context, cmd, viewSampleCount, out var properties))
            {
                return false;
            }

            SkyCubemapBakingUtility.RenderSkyToCubemap(
                cmd,
                m_AmbientProbeCubemap,
                m_SkyMaterial,
                properties,
                m_SkyBakingPass);
            SkyCubemapBakingUtility.GenerateCubemapMipmaps(cmd, m_AmbientProbeCubemap);
            return true;
        }

        private bool RefreshAmbientProbeCubemap(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            int hash,
            int resolution,
            int viewSampleCount,
            SkyRebuildReason rebuildReason,
            bool forceDraw)
        {
            if (cmd == null || !CanBakeAmbientProbe())
            {
                return false;
            }

            if (forceDraw && rebuildReason == SkyRebuildReason.None)
                rebuildReason = SkyRebuildReason.FrameRefresh;
            if (rebuildReason == SkyRebuildReason.None)
                return false;

            EnsureAmbientProbeCubemap(resolution);
            using (new ProfilingScope(cmd, GetAmbientProbeRebuildSampler(rebuildReason)))
            {
                if (!RebuildAmbientProbeCubemap(volume, context, cmd, viewSampleCount))
                    return false;
            }

            m_AmbientProbeSkyHash = hash;
            return true;
        }

        private bool TryBuildSkyBakingProperties(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            int viewSampleCount,
            out MaterialPropertyBlock properties)
        {
            properties = null;
            if (!CanBakeSky()
                || !PhysicallyBasedSkyShaderParameterBuilder.TryBuildForSkyBaking(volume, context, out var parameters))
            {
                return false;
            }

            var hasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(volume, context, out var materialParameters);
            m_CelestialBodyBuffer.Update(context);
            m_SkyMaterial.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);
            if (hasMaterialParameters)
                SyncSkyBakingMaterialParametersWithCelestialBodyBuffer(ref materialParameters);

            var skyViewLutHash = hasMaterialParameters
                ? PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(parameters, materialParameters, context)
                : 0;
            var useSkyViewLut = TryResolveSkyBakingSkyViewLut(context, cmd, skyViewLutHash, hasMaterialParameters, out var skyViewLut);
            var includeSunInBaking = SkySettingsVolume.GetIncludeSunInBaking(VividVolumeManagerUtility.GetSkySettingsVolume());

            properties = m_SkyBakingPropertyBlock;
            properties.Clear();
            properties.SetFloat(SkyUseLutId, useSkyViewLut ? 1.0f : 0.0f);
            properties.SetTexture(SkyViewLutId, useSkyViewLut ? skyViewLut : Texture2D.blackTexture);
            properties.SetTexture(DirectionalShadowTextureId, Texture2D.whiteTexture);
            var useLocalSkyPrecomputation = false;
            if (hasMaterialParameters && UsesWorldSpacePrecomputation(materialParameters))
            {
                TryPrepareLocalSkyPrecomputation(volume, context, cmd, parameters, materialParameters);
                useLocalSkyPrecomputation = HasMatchingLocalSkyPrecomputation(parameters, materialParameters);
            }
            CoreUtils.SetKeyword(m_SkyMaterial, "LOCAL_SKY", useLocalSkyPrecomputation);
            if (useLocalSkyPrecomputation)
                ApplyLocalSkyPrecomputationTextures(properties);

            if (hasMaterialParameters)
            {
                materialParameters.renderSunDisk = includeSunInBaking && volume.renderSunDisk.value ? 1 : 0;
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, materialParameters, volume);
            }

            return true;
        }

        private bool TryResolveSkyBakingSkyViewLut(
            in SkyRendererContext context,
            CommandBuffer cmd,
            int skyViewLutHash,
            bool hasMaterialParameters,
            out Texture skyViewLut)
        {
            if (!hasMaterialParameters)
            {
                skyViewLut = null;
                return false;
            }

            if (m_AtmosphereLutCache.TryGetSkyViewLut(skyViewLutHash, out skyViewLut))
            {
                if (!m_AtmosphereLutCache.SkyViewLutRebuiltThisFrame)
                    return true;

                m_SkyViewLutRebuiltForBakingThisFrame = true;
                skyViewLut = null;
                return false;
            }

            if (cmd == null)
                return false;

            m_AtmosphereLutCache.Update(context, cmd, forceSkyViewRebuild: true);
            if (m_AtmosphereLutCache.TryGetSkyViewLut(skyViewLutHash, out skyViewLut)
                && !m_AtmosphereLutCache.SkyViewLutRebuiltThisFrame)
            {
                return true;
            }

            if (m_AtmosphereLutCache.SkyViewLutRebuiltThisFrame)
                m_SkyViewLutRebuiltForBakingThisFrame = true;

            skyViewLut = null;
            return false;
        }

        private bool TryPrepareLocalSkyPrecomputation(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            if (volume == null
                || cmd == null)
            {
                return false;
            }

            var localSkyPrecomputationHash = ComputeLocalSkyPrecomputationHash(skyParameters, materialParameters);
            if (m_HasLocalSkyPrecomputation
                && m_LocalSkyPrecomputationHash == localSkyPrecomputationHash
                && HasLocalSkyPrecomputationTextures())
            {
                return true;
            }

            if (!EnsureLocalSkyPrecomputation(volume, context, cmd, skyParameters, materialParameters))
                return false;

            m_LocalSkyPrecomputationHash = localSkyPrecomputationHash;
            m_HasLocalSkyPrecomputation = true;
            m_LocalSkyPrecomputationRebuiltThisFrame = true;
            return true;
        }

        private void ApplyLocalSkyPrecomputationTextures(MaterialPropertyBlock properties)
        {
            if (properties == null)
                return;

            properties.SetTexture(GroundIrradianceTextureId, m_GroundIrradianceTable);
            properties.SetTexture(AirSingleScatteringTextureId, m_AirSingleScatteringTable);
            properties.SetTexture(AerosolSingleScatteringTextureId, m_AerosolSingleScatteringTable);
            properties.SetTexture(MultipleScatteringTextureId, m_MultipleScatteringTable);
        }

        private bool EnsureLocalSkyPrecomputation(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            if (cmd == null
                || m_AtmosphereLutCompute == null
                || m_GroundIrradiancePrecomputationCompute == null
                || m_InScatteredRadiancePrecomputationCompute == null
                || m_MultiScatteringKernel < 0
                || m_GroundIrradiancePrecomputationKernel < 0
                || m_InScatteredRadiancePrecomputationKernel < 0)
            {
                return false;
            }

            EnsureLocalSkyPrecomputationResources();
            PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_AtmosphereLutCompute, skyParameters, materialParameters);
            PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_GroundIrradiancePrecomputationCompute, skyParameters, materialParameters);
            PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_InScatteredRadiancePrecomputationCompute, skyParameters, materialParameters);

            cmd.SetComputeTextureParam(m_AtmosphereLutCompute, m_MultiScatteringKernel, MultiScatteringLutRwId, m_MultiScatteringLut);
            cmd.DispatchCompute(
                m_AtmosphereLutCompute,
                m_MultiScatteringKernel,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringHeight,
                1);

            cmd.SetComputeTextureParam(m_InScatteredRadiancePrecomputationCompute, m_InScatteredRadiancePrecomputationKernel, MultiScatteringLutId, m_MultiScatteringLut);
            cmd.SetComputeTextureParam(m_InScatteredRadiancePrecomputationCompute, m_InScatteredRadiancePrecomputationKernel, AirSingleScatteringTableId,
                m_AirSingleScatteringTable);
            cmd.SetComputeTextureParam(m_InScatteredRadiancePrecomputationCompute, m_InScatteredRadiancePrecomputationKernel, AerosolSingleScatteringTableId,
                m_AerosolSingleScatteringTable);
            cmd.SetComputeTextureParam(m_InScatteredRadiancePrecomputationCompute, m_InScatteredRadiancePrecomputationKernel, MultipleScatteringTableId, m_MultipleScatteringTable);
            cmd.DispatchCompute(
                m_InScatteredRadiancePrecomputationCompute,
                m_InScatteredRadiancePrecomputationKernel,
                InScatteredRadianceTableSizeX / 4,
                InScatteredRadianceTableSizeY / 4,
                InScatteredRadianceTableSizeZ / 4 * InScatteredRadianceTableSizeW);

            cmd.SetComputeTextureParam(m_GroundIrradiancePrecomputationCompute, m_GroundIrradiancePrecomputationKernel, GroundIrradianceTableId, m_GroundIrradianceTable);
            cmd.SetComputeTextureParam(m_GroundIrradiancePrecomputationCompute, m_GroundIrradiancePrecomputationKernel, AirSingleScatteringTextureId, m_AirSingleScatteringTable);
            cmd.SetComputeTextureParam(m_GroundIrradiancePrecomputationCompute, m_GroundIrradiancePrecomputationKernel, AerosolSingleScatteringTextureId,
                m_AerosolSingleScatteringTable);
            cmd.SetComputeTextureParam(m_GroundIrradiancePrecomputationCompute, m_GroundIrradiancePrecomputationKernel, MultipleScatteringTextureId, m_MultipleScatteringTable);
            cmd.DispatchCompute(m_GroundIrradiancePrecomputationCompute, m_GroundIrradiancePrecomputationKernel, GroundIrradianceTableSize / 64, 1, 1);
            return true;
        }

        private bool HasLocalSkyPrecomputationTextures()
        {
            return IsTextureCreated(m_GroundIrradianceTable)
                   && IsTextureCreated(m_AirSingleScatteringTable)
                   && IsTextureCreated(m_AerosolSingleScatteringTable)
                   && IsTextureCreated(m_MultipleScatteringTable)
                   && IsTextureCreated(m_MultiScatteringLut);
        }

        private void EnsureLocalSkyPrecomputationResources()
        {
            Ensure2DRenderTexture(
                ref m_MultiScatteringLut,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringHeight,
                "VividPbrSky_MultiScatteringLUT");
            Ensure2DRenderTexture(ref m_GroundIrradianceTable, GroundIrradianceTableSize, 1, "VividPbrSky_GroundIrradiance");
            Ensure3DRenderTexture(ref m_AirSingleScatteringTable, InScatteredRadianceTableSizeX, InScatteredRadianceTableSizeY,
                InScatteredRadianceTableSizeZ * InScatteredRadianceTableSizeW, "VividPbrSky_AirSingleScattering");
            Ensure3DRenderTexture(ref m_AerosolSingleScatteringTable, InScatteredRadianceTableSizeX, InScatteredRadianceTableSizeY,
                InScatteredRadianceTableSizeZ * InScatteredRadianceTableSizeW, "VividPbrSky_AerosolSingleScattering");
            Ensure3DRenderTexture(ref m_MultipleScatteringTable, InScatteredRadianceTableSizeX, InScatteredRadianceTableSizeY,
                InScatteredRadianceTableSizeZ * InScatteredRadianceTableSizeW, "VividPbrSky_MultipleScattering");
        }

        private void ReleaseLocalSkyPrecomputationResources()
        {
            ReleaseRenderTexture(ref m_GroundIrradianceTable);
            ReleaseRenderTexture(ref m_AirSingleScatteringTable);
            ReleaseRenderTexture(ref m_AerosolSingleScatteringTable);
            ReleaseRenderTexture(ref m_MultipleScatteringTable);
            ReleaseRenderTexture(ref m_MultiScatteringLut);
            m_HasLocalSkyPrecomputation = false;
            m_LocalSkyPrecomputationRebuiltThisFrame = false;
            m_SkyViewLutRebuiltForBakingThisFrame = false;
            m_RuntimeCubemapNeedsDeferredBakingResourceRefresh = false;
            m_LocalSkyPrecomputationHash = 0;
        }

        private static int ComputeLocalSkyPrecomputationHash(
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, skyParameters.skySunDirection);
                hash = AppendHash(hash, skyParameters.skySunColor);
                hash = AppendHash(hash, materialParameters.planetCenterRadius);
                hash = AppendHash(hash, materialParameters.planetUpAltitude);
                hash = AppendHash(hash, materialParameters.airSeaLevelExtinction);
                hash = AppendHash(hash, materialParameters.airSeaLevelScattering);
                hash = AppendHash(hash, materialParameters.aerosolSeaLevelScattering);
                hash = AppendHash(hash, materialParameters.ozoneSeaLevelExtinction);
                hash = AppendHash(hash, materialParameters.groundAlbedoPlanetRadius);
                hash = AppendHash(hash, materialParameters.horizonTint);
                hash = AppendHash(hash, materialParameters.zenithTint);
                hash = AppendHash(hash, materialParameters.atmosphericRadius);
                hash = AppendHash(hash, materialParameters.aerosolAnisotropy);
                hash = AppendHash(hash, materialParameters.aerosolPhasePartConstant);
                hash = AppendHash(hash, materialParameters.aerosolSeaLevelExtinction);
                hash = AppendHash(hash, materialParameters.airDensityFalloff);
                hash = AppendHash(hash, materialParameters.airScaleHeight);
                hash = AppendHash(hash, materialParameters.aerosolDensityFalloff);
                hash = AppendHash(hash, materialParameters.aerosolScaleHeight);
                hash = AppendHash(hash, materialParameters.ozoneScaleOffset);
                hash = AppendHash(hash, materialParameters.ozoneLayerStart);
                hash = AppendHash(hash, materialParameters.ozoneLayerEnd);
                hash = AppendHash(hash, materialParameters.intensityMultiplier);
                hash = AppendHash(hash, materialParameters.colorSaturation);
                hash = AppendHash(hash, materialParameters.alphaSaturation);
                hash = AppendHash(hash, materialParameters.alphaMultiplier);
                hash = AppendHash(hash, materialParameters.horizonZenithShiftPower);
                hash = AppendHash(hash, materialParameters.horizonZenithShiftScale);
                hash = AppendHash(hash, materialParameters.celestialLightCount);
                hash = AppendHash(hash, materialParameters.celestialBodyCount);
                hash = AppendHash(hash, materialParameters.atmosphericDepth);
                hash = AppendHash(hash, materialParameters.rcpAtmosphericDepth);
                hash = AppendHash(hash, materialParameters.celestialLightExposure);
                hash = AppendHash(hash, materialParameters.volumetricCloudsBottomAltitude);
                hash = AppendHash(hash, materialParameters.renderSunDisk);
                hash = AppendHash(hash, materialParameters.renderingSpace);
                return hash;
            }
        }

        private static SkyPlanet ResolvePlanet(
            in SkyRendererContext context,
            PhysicallyBasedSkyVolume volume,
            SkySettingsVolume skySettings)
        {
            return SkyPlanet.Resolve(volume, skySettings, ResolveWorldCameraPosition(context));
        }

        private static int ComputeAmbientProbeHash(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            SkySettingsVolume skySettings,
            int generatedCubemapViewSampleCount,
            float intensityMultiplier)
        {
            var hash = 17;
            hash = AppendHash(hash, volume.GetHashCode());
            hash = AppendHash(hash, generatedCubemapViewSampleCount);
            hash = AppendHash(hash, ResolvePlanet(context, volume, skySettings).ComputeHashCode());
            hash = AppendHash(hash, SkySettingsVolume.GetIncludeSunInBaking(skySettings));
            hash = AppendHash(hash, intensityMultiplier);
            hash = AppendHash(hash, PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialBodyHash(context));
            return hash;
        }

        private static Vector3 ResolveWorldCameraPosition(in SkyRendererContext context)
        {
            var camera = context.cameraData?.camera;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        private static bool UsesWorldSpacePrecomputation(in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            return materialParameters.renderingSpace != 0;
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

        private static int AppendHash(int hash, bool value)
        {
            return AppendHash(hash, value ? 1 : 0);
        }

        private static int AppendHash(int hash, Vector3 value)
        {
            unchecked
            {
                hash = AppendHash(hash, value.x);
                hash = AppendHash(hash, value.y);
                hash = AppendHash(hash, value.z);
                return hash;
            }
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

        private static void Ensure2DRenderTexture(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null
                && texture.IsCreated()
                && texture.dimension == TextureDimension.Tex2D
                && texture.width == width
                && texture.height == height)
            {
                return;
            }

            ReleaseRenderTexture(ref texture);
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
        }

        private static void Ensure3DRenderTexture(ref RenderTexture texture, int width, int height, int depth, string name)
        {
            if (texture != null
                && texture.IsCreated()
                && texture.dimension == TextureDimension.Tex3D
                && texture.width == width
                && texture.height == height
                && texture.volumeDepth == depth)
            {
                return;
            }

            ReleaseRenderTexture(ref texture);
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
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }

        private static bool IsTextureCreated(RenderTexture texture)
        {
            return texture != null && texture.IsCreated();
        }

        private static bool IsCubemapValid(RenderTexture texture, int resolution)
        {
            return texture != null
                   && texture.IsCreated()
                   && texture.dimension == TextureDimension.Cube
                   && texture.width == resolution
                   && texture.height == resolution
                   && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static ProfilingSampler GetRuntimeCubemapRebuildSampler(SkyRebuildReason reason)
        {
            return reason switch
            {
                SkyRebuildReason.ResolutionChanged => s_RuntimeCubemapResolutionChangedSampler,
                SkyRebuildReason.QualityChanged => s_RuntimeCubemapQualityChangedSampler,
                SkyRebuildReason.ParametersChanged => s_RuntimeCubemapParametersChangedSampler,
                _ => s_RuntimeCubemapMissingTextureSampler,
            };
        }

        private static ProfilingSampler GetAmbientProbeRebuildSampler(SkyRebuildReason reason)
        {
            return reason switch
            {
                SkyRebuildReason.ResolutionChanged => s_AmbientProbeResolutionChangedSampler,
                SkyRebuildReason.ParametersChanged => s_AmbientProbeParametersChangedSampler,
                SkyRebuildReason.FrameRefresh => s_AmbientProbeFrameRefreshSampler,
                _ => s_AmbientProbeMissingTextureSampler,
            };
        }

        private Texture ResolveSkyViewTexture()
        {
            return TryResolveSkyViewTexture(out var skyViewTexture)
                ? skyViewTexture
                : null;
        }

        private void RefreshSkyViewLutForRender(CommandBuffer cmd)
        {
            if (!m_HasRenderMaterialParameters || cmd == null)
                return;

            if (m_HasRenderedSkyViewLut
                && m_LastRenderedSkyViewLutHash == m_RenderSkyViewLutHash
                && !m_AtmosphereLutCache.SkyViewLutRebuiltThisFrame)
            {
                return;
            }

            m_AtmosphereLutCache.Update(m_RenderContext, cmd, forceSkyViewRebuild: true);
        }

        private bool TryResolveSkyViewTexture(out Texture skyViewTexture)
        {
            if (!m_HasRenderMaterialParameters)
            {
                skyViewTexture = null;
                return false;
            }

            var skyViewHash = m_RenderSkyViewLutHash;
            return m_AtmosphereLutCache.TryGetSkyViewLut(skyViewHash, out skyViewTexture);
        }

        private void ApplyAtmosphereLutHandle(VividSkyData skyData)
        {
            if (skyData == null)
                return;

            var atmosphericScatteringHandle = m_AtmosphereLutCache.AtmosphericScatteringHandle;
            skyData.atmosphericScatteringLutHandle =
                atmosphericScatteringHandle != null
                && atmosphericScatteringHandle.rt != null
                && atmosphericScatteringHandle.rt.IsCreated()
                    ? atmosphericScatteringHandle
                    : null;
        }

        private void ImportSkyViewLutForPass(RenderGraphTexture skyViewLut)
        {
            if (skyViewLut == null
                || !PassRecorder.IsPassTextureImportActive
                || !TryResolveSkyViewTexture(out _))
                return;

            var handle = m_AtmosphereLutCache.SkyViewHandle;
            if (handle == null || handle.rt == null || !handle.rt.IsCreated())
                return;

            PassRecorder.ImportTexture(skyViewLut, handle);
        }

        private void SyncRenderMaterialParametersWithCelestialBodyBuffer()
        {
            m_RenderMaterialParameters.celestialLightCount = m_CelestialBodyBuffer.CelestialLightCount;
            m_RenderMaterialParameters.celestialBodyCount = m_CelestialBodyBuffer.CelestialBodyCount;
            m_RenderMaterialParameters.celestialLightExposure = Mathf.Max(m_CelestialBodyBuffer.CelestialLightExposure, 1.0f);
        }

        private void SyncSkyBakingMaterialParametersWithCelestialBodyBuffer(ref PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            materialParameters.celestialLightCount = m_CelestialBodyBuffer.CelestialLightCount;
            materialParameters.celestialBodyCount = m_CelestialBodyBuffer.CelestialBodyCount;
            materialParameters.celestialLightExposure = Mathf.Max(m_CelestialBodyBuffer.CelestialLightExposure, 1.0f);
        }

        private static Rect ResolveRenderViewport(VividCameraData cameraData, RenderGraphTexture colorTarget)
        {
            var width = colorTarget?.desc?.Width ?? 0;
            var height = colorTarget?.desc?.Height ?? 0;

            if (width <= 0)
                width = cameraData?.actualWidth > 0 ? cameraData.actualWidth : cameraData?.pixelWidth ?? 0;

            if (height <= 0)
                height = cameraData?.actualHeight > 0 ? cameraData.actualHeight : cameraData?.pixelHeight ?? 0;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            return new Rect(0.0f, 0.0f, width, height);
        }
    }

    internal readonly struct PhysicallyBasedSkyAtmosphericAttenuationContext
    {
        internal PhysicallyBasedSkyAtmosphericAttenuationContext(
            float airScaleHeight,
            float aerosolScaleHeight,
            Vector3 airExtinctionCoefficient,
            float aerosolExtinctionCoefficient,
            float ozoneMinimumAltitude,
            float ozoneLayerWidth,
            Vector3 ozoneExtinctionCoefficient,
            Vector3 planetCenterWS,
            float planetRadius,
            Vector3 cameraPositionWS)
        {
            this.airScaleHeight = airScaleHeight;
            this.aerosolScaleHeight = aerosolScaleHeight;
            this.airExtinctionCoefficient = airExtinctionCoefficient;
            this.aerosolExtinctionCoefficient = aerosolExtinctionCoefficient;
            this.ozoneMinimumAltitude = ozoneMinimumAltitude;
            this.ozoneLayerWidth = ozoneLayerWidth;
            this.ozoneExtinctionCoefficient = ozoneExtinctionCoefficient;
            this.planetCenterWS = planetCenterWS;
            this.planetRadius = planetRadius;
            this.cameraPositionWS = cameraPositionWS;
        }

        internal float airScaleHeight { get; }

        internal float aerosolScaleHeight { get; }

        internal Vector3 airExtinctionCoefficient { get; }

        internal float aerosolExtinctionCoefficient { get; }

        internal float ozoneMinimumAltitude { get; }

        internal float ozoneLayerWidth { get; }

        internal Vector3 ozoneExtinctionCoefficient { get; }

        internal Vector3 planetCenterWS { get; }

        internal float planetRadius { get; }

        internal Vector3 cameraPositionWS { get; }
    }

    internal static class PhysicallyBasedSkyAtmosphericAttenuation
    {
        internal static bool TryCreate(
            Camera camera,
            out PhysicallyBasedSkyAtmosphericAttenuationContext context)
        {
            return TryCreate(
                VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume(),
                VividVolumeManagerUtility.GetSkySettingsVolume(),
                camera != null ? camera.transform.position : Vector3.zero,
                out context);
        }

        internal static bool TryCreate(
            PhysicallyBasedSkyVolume volume,
            SkySettingsVolume skySettings,
            Vector3 cameraPositionWS,
            out PhysicallyBasedSkyAtmosphericAttenuationContext context)
        {
            context = default;

            if (volume == null || !volume.IsActive())
                return false;

            var planet = SkyPlanet.Resolve(volume, skySettings, cameraPositionWS);
            context = new PhysicallyBasedSkyAtmosphericAttenuationContext(
                Mathf.Max(volume.GetAirScaleHeight(), 1.0f),
                Mathf.Max(volume.GetAerosolScaleHeight(), 1.0f),
                volume.GetAirExtinctionCoefficient(),
                volume.GetAerosolExtinctionCoefficient(),
                Mathf.Max(volume.GetOzoneLayerMinimumAltitude(), 0.0f),
                Mathf.Max(volume.GetOzoneLayerWidth(), 1.0f),
                volume.GetOzoneExtinctionCoefficient(),
                planet.center,
                planet.radius,
                cameraPositionWS);
            return true;
        }

        internal static Vector3 Evaluate(
            in PhysicallyBasedSkyAtmosphericAttenuationContext context,
            Vector3 lightDirectionWS)
        {
            if (lightDirectionWS.sqrMagnitude <= 1e-6f)
                return Vector3.one;

            var directionToLight = lightDirectionWS.normalized;
            var planetToCamera = context.cameraPositionWS - context.planetCenterWS;
            var radialDistance = planetToCamera.magnitude;
            if (radialDistance <= 1e-6f)
                radialDistance = Mathf.Max(context.planetRadius, 1.0f);

            var cosHoriz = ComputeCosineOfHorizonAngle(radialDistance, context.planetRadius);
            var cosTheta = Vector3.Dot(planetToCamera, directionToLight) * Rcp(radialDistance);
            if (cosTheta <= cosHoriz)
                return Vector3.zero;

            var opticalDepth = ComputeAtmosphericOpticalDepth(
                context.airScaleHeight,
                context.aerosolScaleHeight,
                context.airExtinctionCoefficient,
                context.aerosolExtinctionCoefficient,
                context.ozoneMinimumAltitude,
                context.ozoneLayerWidth,
                context.ozoneExtinctionCoefficient,
                context.planetRadius,
                radialDistance,
                cosTheta,
                true);

            return new Vector3(
                Mathf.Exp(-opticalDepth.x),
                Mathf.Exp(-opticalDepth.y),
                Mathf.Exp(-opticalDepth.z));
        }

        private static float Rcp(float value)
        {
            return 1.0f / value;
        }

        private static float Rsqrt(float value)
        {
            return Rcp(Mathf.Sqrt(value));
        }

        private static float Saturate(float value)
        {
            return Mathf.Clamp01(value);
        }

        private static float ComputeCosineOfHorizonAngle(float radialDistance, float planetRadius)
        {
            var sinHorizon = planetRadius * Rcp(radialDistance);
            return -Mathf.Sqrt(Saturate(1.0f - sinHorizon * sinHorizon));
        }

        private static float ChapmanUpperApprox(float z, float cosTheta)
        {
            var c = cosTheta;
            var n = 0.761643f * ((1.0f + 2.0f * z) - (c * c * z));
            var d = c * z + Mathf.Sqrt(z * (1.47721f + 0.273828f * (c * c * z)));
            return 0.5f * c + n * Rcp(d);
        }

        private static float ChapmanHorizontal(float z)
        {
            var r = Rsqrt(z);
            var s = z * r;
            return 0.626657f * (r + 2.0f * s);
        }

        private static float OzoneDensity(float height, Vector2 ozoneScaleOffset)
        {
            return Mathf.Clamp01(1.0f - Mathf.Abs(height * ozoneScaleOffset.x + ozoneScaleOffset.y));
        }

        private static Vector2 IntersectSphere(float sphereRadius, float cosChi, float radialDistance)
        {
            var reciprocalRadialDistance = Rcp(radialDistance);
            var d = sphereRadius * reciprocalRadialDistance;
            d = d * d - Saturate(1.0f - cosChi * cosChi);
            if (d < 0.0f)
                return new Vector2(d, d);

            var sqrtD = Mathf.Sqrt(d);
            return radialDistance * new Vector2(-cosChi - sqrtD, -cosChi + sqrtD);
        }

        private static float ComputeOzoneOpticalDepth(
            float planetRadius,
            float radialDistance,
            float cosTheta,
            float ozoneMinimumAltitude,
            float ozoneLayerWidth)
        {
            var ozoneOpticalDepth = 0.0f;
            var innerIntersection = IntersectSphere(
                planetRadius + ozoneMinimumAltitude,
                cosTheta,
                radialDistance);
            var outerIntersection = IntersectSphere(
                planetRadius + ozoneMinimumAltitude + ozoneLayerWidth,
                cosTheta,
                radialDistance);

            float tEntry;
            float tEntrySecond;
            float tExit;
            float tExitSecond;

            if (innerIntersection.x < 0.0f && innerIntersection.y >= 0.0f)
            {
                tEntry = innerIntersection.y;
                tExitSecond = outerIntersection.y;
                tEntrySecond = tExit = (tExitSecond - tEntry) * 0.5f;
            }
            else
            {
                tEntry = Mathf.Max(outerIntersection.x, 0.0f);
                tExit = innerIntersection.x >= 0.0f ? innerIntersection.x : outerIntersection.y;

                if (innerIntersection.x >= 0.0f)
                {
                    tEntrySecond = innerIntersection.y;
                    tExitSecond = outerIntersection.y;
                }
                else
                {
                    tExitSecond = tExit;
                    tEntrySecond = tExit = (tExitSecond - tEntry) * 0.5f;
                }
            }

            const float sampleCount = 2.0f;
            var reciprocalSampleCount = Rcp(sampleCount);
            var dt = Mathf.Max(tExit - tEntry, 0.0f) * reciprocalSampleCount;
            var dtSecond = Mathf.Max(tExitSecond - tEntrySecond, 0.0f) * reciprocalSampleCount;
            var ozoneScaleOffset = new Vector2(
                2.0f / ozoneLayerWidth,
                -2.0f * ozoneMinimumAltitude / ozoneLayerWidth - 1.0f);

            for (var sampleIndex = 0; sampleIndex < 2; sampleIndex++)
            {
                var sampleT = Mathf.Lerp(tEntry, tExit, (sampleIndex + 0.5f) * reciprocalSampleCount);
                var secondSampleT = Mathf.Lerp(tEntrySecond, tExitSecond, (sampleIndex + 0.5f) * reciprocalSampleCount);
                var height = Mathf.Sqrt(radialDistance * radialDistance + sampleT * (2.0f * radialDistance * cosTheta + sampleT)) - planetRadius;
                var secondHeight = Mathf.Sqrt(radialDistance * radialDistance + secondSampleT * (2.0f * radialDistance * cosTheta + secondSampleT)) - planetRadius;

                ozoneOpticalDepth += OzoneDensity(height, ozoneScaleOffset) * dt;
                ozoneOpticalDepth += OzoneDensity(secondHeight, ozoneScaleOffset) * dtSecond;
            }

            return ozoneOpticalDepth * 0.6f;
        }

        private static Vector3 ComputeAtmosphericOpticalDepth(
            float airScaleHeight,
            float aerosolScaleHeight,
            Vector3 airExtinctionCoefficient,
            float aerosolExtinctionCoefficient,
            float ozoneMinimumAltitude,
            float ozoneLayerWidth,
            Vector3 ozoneExtinctionCoefficient,
            float planetRadius,
            float radialDistance,
            float cosTheta,
            bool alwaysAboveHorizon)
        {
            var scaleHeights = new Vector2(airScaleHeight, aerosolScaleHeight);
            var reciprocalScaleHeights = new Vector2(Rcp(scaleHeights.x), Rcp(scaleHeights.y));
            var z = new Vector2(
                radialDistance * reciprocalScaleHeights.x,
                radialDistance * reciprocalScaleHeights.y);
            var zAtSeaLevel = new Vector2(
                planetRadius * reciprocalScaleHeights.x,
                planetRadius * reciprocalScaleHeights.y);
            var cosHoriz = ComputeCosineOfHorizonAngle(radialDistance, planetRadius);
            var sinTheta = Mathf.Sqrt(Saturate(1.0f - cosTheta * cosTheta));

            var chapman = new Vector2(
                ChapmanUpperApprox(z.x, Mathf.Abs(cosTheta)) * Mathf.Exp(zAtSeaLevel.x - z.x),
                ChapmanUpperApprox(z.y, Mathf.Abs(cosTheta)) * Mathf.Exp(zAtSeaLevel.y - z.y));

            if (!alwaysAboveHorizon && cosTheta < cosHoriz)
            {
                var sinGamma = (radialDistance / planetRadius) * sinTheta;
                var cosGamma = Mathf.Sqrt(Saturate(1.0f - sinGamma * sinGamma));
                var chapmanAtHorizon = new Vector2(
                    ChapmanUpperApprox(zAtSeaLevel.x, cosGamma),
                    ChapmanUpperApprox(zAtSeaLevel.y, cosGamma));
                chapman -= chapmanAtHorizon;
            }
            else if (cosTheta < 0.0f)
            {
                var z0 = z * sinTheta;
                var expTerm = new Vector2(
                    Mathf.Exp(zAtSeaLevel.x - z0.x),
                    Mathf.Exp(zAtSeaLevel.y - z0.y));
                var horizontal = new Vector2(
                    2.0f * ChapmanHorizontal(z0.x),
                    2.0f * ChapmanHorizontal(z0.y));
                chapman = Vector2.Scale(horizontal, expTerm) - chapman;
            }

            var opticalDepth = Vector2.Scale(chapman, scaleHeights);
            var ozoneOpticalDepth = alwaysAboveHorizon
                ? ComputeOzoneOpticalDepth(
                    planetRadius,
                    radialDistance,
                    cosTheta,
                    ozoneMinimumAltitude,
                    ozoneLayerWidth)
                : 0.0f;

            return new Vector3(
                opticalDepth.x * airExtinctionCoefficient.x + opticalDepth.y * aerosolExtinctionCoefficient + ozoneOpticalDepth * ozoneExtinctionCoefficient.x,
                opticalDepth.x * airExtinctionCoefficient.y + opticalDepth.y * aerosolExtinctionCoefficient + ozoneOpticalDepth * ozoneExtinctionCoefficient.y,
                opticalDepth.x * airExtinctionCoefficient.z + opticalDepth.y * aerosolExtinctionCoefficient + ozoneOpticalDepth * ozoneExtinctionCoefficient.z);
        }
    }
}
