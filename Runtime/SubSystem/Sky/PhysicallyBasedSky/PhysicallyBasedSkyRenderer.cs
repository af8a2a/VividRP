using System;
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
            ParametersChanged
        }

        private const float ObserverHeight = 2.0f;
        private const string PhysicallyBasedSkyShaderName = "Hidden/VividRP/PhysicallyBasedSky";

        private static readonly int SkyViewLutId = Shader.PropertyToID("_SkyViewLUT");
        private static readonly int SkyUseLutId = Shader.PropertyToID("_SkyUseLUT");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
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

        internal const float SunAngularDiameterDegrees = 0.53f;
        internal const float SunIlluminanceScale = 20.0f;
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
        private RenderGraphTexture m_ColorTarget;
        private RenderGraphTexture m_DepthTexture;
        private RenderGraphTexture m_SkyViewLut;
        private RenderGraphTexture m_DirectionalShadowTexture;
        private Rect m_RenderViewport;
        private bool m_ShouldRenderSky;
        private PhysicallyBasedSkyVolume m_RenderVolume;
        private SkyRendererContext m_RenderContext;
        private PhysicallyBasedSkyShaderParameters m_RenderParameters;
        private PhysicallyBasedSkyMaterialParameters m_RenderMaterialParameters;
        private bool m_HasRenderMaterialParameters;
        private readonly PhysicallyBasedSkyAtmosphereLutCache m_AtmosphereLutCache = new();
        private readonly PhysicallyBasedSkyCelestialBodyBuffer m_CelestialBodyBuffer = new();

        public SkyType Type => SkyType.PhysicallyBased;

        public void Build(VividRPCoreResources resources)
        {
            var shader = resources?.PhysicallyBasedSkyShader;
            shader ??= Shader.Find(PhysicallyBasedSkyShaderName);
            m_AtmosphereLutCompute = resources?.AtmosphereLUTCompute;
            m_GroundIrradiancePrecomputationCompute = resources?.GroundIrradiancePrecomputationCompute;
            m_InScatteredRadiancePrecomputationCompute = resources?.InScatteredRadiancePrecomputationCompute;
            m_AtmosphereLutCache.Build(resources);
            if (shader != null)
            {
                m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_SkyBakingPass = m_SkyMaterial.FindPass("PhysicallyBasedSkyBaking");
            }

            if (m_AtmosphereLutCompute != null && m_AtmosphereLutCompute.HasKernel("MultiScatteringLUT"))
                m_MultiScatteringKernel = m_AtmosphereLutCompute.FindKernel("MultiScatteringLUT");
            if (m_GroundIrradiancePrecomputationCompute != null && m_GroundIrradiancePrecomputationCompute.HasKernel("main"))
                m_GroundIrradiancePrecomputationKernel = m_GroundIrradiancePrecomputationCompute.FindKernel("main");
            if (m_InScatteredRadiancePrecomputationCompute != null && m_InScatteredRadiancePrecomputationCompute.HasKernel("main"))
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
            return HashCode.Combine(
                volume.GetHashCode(),
                generatedCubemapViewSampleCount,
                ResolveCameraPosition(context, volume.planetRadius.value),
                PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialBodyHash(context));
        }

        public void UpdateFrameResources(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)
        {
            m_AtmosphereLutCache.Update(context, cmd);
            ApplyAtmosphereLutHandle(skyData);
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, int skyHash, bool forceRebuild)
        {
            if (skyData == null)
                return;

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsActive())
            {
                skyData.Reset();
                return;
            }

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(skySettings);
            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            var runtimeCubemapRebuildReason = ResolveRuntimeCubemapRebuildReason(
                skyHash,
                generatedCubemapResolution,
                generatedCubemapViewSampleCount);
            if (forceRebuild && runtimeCubemapRebuildReason == SkyRebuildReason.None)
                runtimeCubemapRebuildReason = SkyRebuildReason.ParametersChanged;
            if (runtimeCubemapRebuildReason != SkyRebuildReason.None && CanRebuildRuntimeCubemap() && cmd != null)
            {
                EnsureRuntimeCubemap(generatedCubemapResolution);
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
                    }
                }
            }

            var hash = HashCode.Combine(
                volume.GetHashCode(),
                generatedCubemapViewSampleCount,
                PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialBodyHash(context));

            var ambientProbeRebuildReason = ResolveAmbientProbeCubemapRebuildReason(hash, generatedCubemapResolution);
            if (forceRebuild && ambientProbeRebuildReason == SkyRebuildReason.None)
                ambientProbeRebuildReason = SkyRebuildReason.ParametersChanged;
            if (ambientProbeRebuildReason != SkyRebuildReason.None && CanBakeAmbientProbe() && cmd != null)
            {
                EnsureAmbientProbeCubemap(generatedCubemapResolution);
                using (new ProfilingScope(cmd, GetAmbientProbeRebuildSampler(ambientProbeRebuildReason)))
                {
                    if (TryCopyRuntimeCubemapToAmbientProbe(
                            cmd,
                            skyHash,
                            generatedCubemapResolution,
                            generatedCubemapViewSampleCount)
                        || RebuildAmbientProbeCubemap(volume, context, cmd, generatedCubemapViewSampleCount))
                    {
                        m_AmbientProbeSkyHash = hash;
                    }
                }
            }

            var useBakedAmbientProbe = CanBakeAmbientProbe()
                                       && m_AmbientProbeCubemap != null
                                       && m_AmbientProbeSkyHash == hash;

            skyData.activeSkyType = SkyType.PhysicallyBased;
            skyData.specularCubemap = m_RuntimeSkyCubemap;
            skyData.tint = Color.white;
            skyData.exposure = 0.0f;
            skyData.rotation = 0.0f;
            skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : m_RuntimeSkyCubemap;
            skyData.ambientProbeTint = Color.white;
            skyData.ambientProbeExposure = 0.0f;
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
            RenderGraphTexture directionalShadowTexture)
        {
            m_ColorTarget = colorTarget;
            m_DepthTexture = depthTexture;
            m_SkyViewLut = skyViewLut;
            m_DirectionalShadowTexture = directionalShadowTexture;
            m_RenderContext = context;
            m_RenderVolume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            m_RenderViewport = ResolveRenderViewport(context.cameraData, colorTarget);
            ImportSkyViewLutForPass(skyViewLut);
            m_HasRenderMaterialParameters = false;
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
        }

        public void RenderSky(CommandBuffer cmd)
        {
            if (!m_ShouldRenderSky
                || cmd == null
                || m_SkyMaterial == null
                || m_ColorTarget == null
                || m_DepthTexture == null
                || !m_ColorTarget.innerHandle.IsValid()
                || !m_DepthTexture.innerHandle.IsValid())
            {
                return;
            }

            cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);
            cmd.SetViewport(m_RenderViewport);

            m_SkyMaterial.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);

            var skyViewTexture = ResolveSkyViewTexture();
            var directionalShadowTexture = ResolveTexture(m_DirectionalShadowTexture) ?? Shader.GetGlobalTexture(DirectionalShadowTextureId);
            var properties = new MaterialPropertyBlock();
            properties.SetMatrix(PixelCoordToViewDirWSId, m_RenderParameters.pixelCoordToViewDirWS);
            properties.SetTexture(SkyViewLutId, skyViewTexture ?? Texture2D.blackTexture);
            properties.SetFloat(SkyUseLutId, skyViewTexture != null ? 1.0f : 0.0f);
            properties.SetTexture(DirectionalShadowTextureId, directionalShadowTexture ?? Texture2D.whiteTexture);
            if (m_HasRenderMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, m_RenderMaterialParameters, m_RenderVolume);

            var useLocalSkyPrecomputation = m_HasRenderMaterialParameters
                                            && TryPrepareLocalSkyPrecomputation(
                                                m_RenderVolume,
                                                m_RenderContext,
                                                cmd,
                                                m_RenderParameters,
                                                m_RenderMaterialParameters);
            CoreUtils.SetKeyword(m_SkyMaterial, "LOCAL_SKY", useLocalSkyPrecomputation);
            if (useLocalSkyPrecomputation)
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
            m_ColorTarget = null;
            m_DepthTexture = null;
            m_SkyViewLut = null;
            m_DirectionalShadowTexture = null;
            m_RenderViewport = default;
            m_ShouldRenderSky = false;
            m_RenderVolume = null;
            m_RenderContext = default;
            m_RenderParameters = default;
            m_RenderMaterialParameters = default;
            m_HasRenderMaterialParameters = false;
            m_AtmosphereLutCache.Dispose();
            m_CelestialBodyBuffer.Dispose();
        }

        internal static Vector3 ResolveSunDirection(in SkyRendererContext context)
        {
            if (context.lightData != null && context.lightData.hasMainDirectionalLight)
                return context.lightData.mainDirectionalLight.directionWS.normalized;

            if (RenderSettings.sun != null)
                return (-RenderSettings.sun.transform.forward).normalized;

            return Vector3.up;
        }

        internal static Color ResolveSunColor(in SkyRendererContext context)
        {
            if (context.lightData != null && context.lightData.hasMainDirectionalLight)
            {
                var color = context.lightData.mainDirectionalLight.color;
                return new Color(color.x, color.y, color.z, 1.0f);
            }

            if (RenderSettings.sun != null)
                return RenderSettings.sun.color.linear * Mathf.Max(RenderSettings.sun.intensity, 0.0f);

            return Color.white;
        }

        internal static Vector3 ResolveCameraPosition(in SkyRendererContext context, float planetRadius)
        {
            var camera = context.cameraData?.camera;
            if (camera == null)
                return new Vector3(0.0f, planetRadius + ObserverHeight, 0.0f);

            var worldPosition = camera.transform.position;
            return new Vector3(
                worldPosition.x,
                Mathf.Max(worldPosition.y + planetRadius, planetRadius + 0.1f),
                worldPosition.z);
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
                if (m_RuntimeSkyCubemap != null)
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
                || m_RuntimeSkyCubemap == null
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
            return true;
        }

        private bool RebuildAmbientProbeCubemap(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            CommandBuffer cmd,
            int viewSampleCount)
        {
            if (cmd == null
                || m_AmbientProbeCubemap == null
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
            return true;
        }

        private bool TryCopyRuntimeCubemapToAmbientProbe(
            CommandBuffer cmd,
            int skyHash,
            int resolution,
            int viewSampleCount)
        {
            if (cmd == null
                || !IsCubemapValid(m_RuntimeSkyCubemap, resolution)
                || !IsCubemapValid(m_AmbientProbeCubemap, resolution)
                || m_RuntimeSkyHash != skyHash
                || m_RuntimeSkyViewSampleCount != viewSampleCount)
            {
                return false;
            }

            cmd.CopyTexture(m_RuntimeSkyCubemap, m_AmbientProbeCubemap);
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
            var skyViewLutHash = hasMaterialParameters
                ? PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(parameters, materialParameters, context)
                : 0;
            var useSkyViewLut = m_AtmosphereLutCache.TryGetSkyViewLut(skyViewLutHash, out var skyViewLut) && hasMaterialParameters;

            properties = new MaterialPropertyBlock();
            properties.SetFloat(SkyUseLutId, useSkyViewLut ? 1.0f : 0.0f);
            properties.SetTexture(SkyViewLutId, useSkyViewLut ? skyViewLut : Texture2D.blackTexture);
            properties.SetTexture(DirectionalShadowTextureId, Texture2D.whiteTexture);
            m_CelestialBodyBuffer.Update(context);
            m_SkyMaterial.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);
            var useLocalSkyPrecomputation = hasMaterialParameters
                                            && TryPrepareLocalSkyPrecomputation(volume, context, cmd, parameters, materialParameters);
            CoreUtils.SetKeyword(m_SkyMaterial, "LOCAL_SKY", useLocalSkyPrecomputation);
            if (useLocalSkyPrecomputation)
                ApplyLocalSkyPrecomputationTextures(properties);

            if (hasMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, materialParameters, volume);

            return true;
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
                _ => s_AmbientProbeMissingTextureSampler,
            };
        }

        private Texture ResolveSkyViewTexture()
        {
            var skyViewTexture = ResolveTexture(m_SkyViewLut);
            if (skyViewTexture != null || !m_HasRenderMaterialParameters)
                return skyViewTexture;

            var skyViewHash = PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(m_RenderParameters, m_RenderMaterialParameters, m_RenderContext);
            return m_AtmosphereLutCache.TryGetSkyViewLut(skyViewHash, out skyViewTexture)
                ? skyViewTexture
                : null;
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
            if (skyViewLut == null || !PassRecorder.IsPassTextureImportActive)
                return;

            var handle = m_AtmosphereLutCache.SkyViewHandle;
            if (handle == null || handle.rt == null || !handle.rt.IsCreated())
                return;

            PassRecorder.ImportTexture(skyViewLut, handle);
        }

        private static Texture ResolveTexture(RenderGraphTexture texture)
        {
            if (texture == null)
                return null;

            RTHandle handle = texture.innerHandle;
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
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
}
