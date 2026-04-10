using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

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

        private static readonly int SkyViewLutId = Shader.PropertyToID("_SkyViewLUT");
        private static readonly int SkyUseLutId = Shader.PropertyToID("_SkyUseLUT");
        private static readonly int SkyBakingViewSampleCountId = Shader.PropertyToID("_SkyBakingViewSampleCount");
        private static readonly int SkyCameraPositionPsId = Shader.PropertyToID("_SkyCameraPositionPS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int SkyPlanetParamsId = Shader.PropertyToID("_SkyPlanetParams");
        private static readonly int SkyAirScatteringId = Shader.PropertyToID("_SkyAirScattering");
        private static readonly int SkyAirExtinctionId = Shader.PropertyToID("_SkyAirExtinction");
        private static readonly int SkyAerosolScatteringId = Shader.PropertyToID("_SkyAerosolScattering");
        private static readonly int SkyAerosolExtinctionId = Shader.PropertyToID("_SkyAerosolExtinction");
        private static readonly int SkyOzoneExtinctionId = Shader.PropertyToID("_SkyOzoneExtinction");
        private static readonly int SkyOzoneParamsId = Shader.PropertyToID("_SkyOzoneParams");
        private static readonly int SkyGroundTintId = Shader.PropertyToID("_SkyGroundTint");
        private static readonly ProfilingSampler s_RuntimeCubemapMissingTextureSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (MissingTexture)");
        private static readonly ProfilingSampler s_RuntimeCubemapResolutionChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ResolutionChanged)");
        private static readonly ProfilingSampler s_RuntimeCubemapQualityChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (QualityChanged)");
        private static readonly ProfilingSampler s_RuntimeCubemapParametersChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ParametersChanged)");
        private static readonly ProfilingSampler s_AmbientProbeMissingTextureSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (MissingTexture)");
        private static readonly ProfilingSampler s_AmbientProbeResolutionChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ResolutionChanged)");
        private static readonly ProfilingSampler s_AmbientProbeParametersChangedSampler = new("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ParametersChanged)");

        internal const float SunAngularDiameterDegrees = 0.53f;
        internal const float SunIlluminanceScale = 20.0f;

        private Material m_SkyMaterial;
        private int m_SkyBakingPass = -1;
        private RenderTexture m_RuntimeSkyCubemap;
        private RenderTexture m_AmbientProbeCubemap;
        private int m_RuntimeSkyHash;
        private int m_RuntimeSkyViewSampleCount;
        private int m_AmbientProbeSkyHash;
        private bool m_HasPendingSkyViewLutRebake;
        private int m_PendingSkyViewLutHash;

        public SkyType Type => SkyType.PhysicallyBased;

        public void Build(VividRPCoreResources resources)
        {
            var shader = resources?.PhysicallyBasedSkyShader;
            shader ??= Shader.Find(PhysicallyBasedSkyPass.PhysicallyBasedSkyShaderName);
            if (shader != null)
            {
                m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_SkyBakingPass = m_SkyMaterial.FindPass("PhysicallyBasedSkyBaking");
            }
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
                SkySettingsVolume.GetGeneratedCubemapResolution(skySettings),
                generatedCubemapViewSampleCount,
                ResolveCameraPosition(context, volume.planetRadius.value),
                ResolveSunDirection(context),
                ResolveSunColor(context));
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)
        {
            if (skyData == null)
                return;

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsActive())
            {
                skyData.Reset();
                return;
            }

            var hash = GetSkyHash(context);
            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(skySettings);
            var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);
            var runtimeCubemapRebuildReason = ResolveRuntimeCubemapRebuildReason(
                hash,
                generatedCubemapResolution,
                generatedCubemapViewSampleCount);
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
                        m_RuntimeSkyHash = hash;
                        m_RuntimeSkyViewSampleCount = generatedCubemapViewSampleCount;
                    }
                }
            }

            var ambientProbeRebuildReason = ResolveAmbientProbeCubemapRebuildReason(hash, generatedCubemapResolution);
            if (ambientProbeRebuildReason != SkyRebuildReason.None && CanBakeAmbientProbe() && cmd != null)
            {
                EnsureAmbientProbeCubemap(generatedCubemapResolution);
                using (new ProfilingScope(cmd, GetAmbientProbeRebuildSampler(ambientProbeRebuildReason)))
                {
                    if (RebuildAmbientProbeCubemap(volume, context, cmd, generatedCubemapViewSampleCount))
                        m_AmbientProbeSkyHash = hash;
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

            if (m_SkyMaterial != null)
            {
                CoreUtils.Destroy(m_SkyMaterial);
                m_SkyMaterial = null;
            }

            m_SkyBakingPass = -1;
            m_RuntimeSkyHash = 0;
            m_RuntimeSkyViewSampleCount = 0;
            m_AmbientProbeSkyHash = 0;
            m_HasPendingSkyViewLutRebake = false;
            m_PendingSkyViewLutHash = 0;
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
                || !TryBuildSkyBakingProperties(volume, context, runtimeCubemapViewSampleCount, out var properties))
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
                || !TryBuildSkyBakingProperties(volume, context, viewSampleCount, out var properties))
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

        private bool TryBuildSkyBakingProperties(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
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
                ? AtmosphereLUTPass.ComputeSkyViewLutHash(parameters, materialParameters)
                : 0;
            var useSkyViewLut = AtmosphereLUTPass.TryGetCachedSkyViewLut(skyViewLutHash, out var skyViewLut) &&
                                hasMaterialParameters;
            if (useSkyViewLut)
            {
                m_HasPendingSkyViewLutRebake = false;
                m_PendingSkyViewLutHash = 0;
            }
            else if (hasMaterialParameters
                     && CanUseSkyViewLut()
                     && (!m_HasPendingSkyViewLutRebake || m_PendingSkyViewLutHash != skyViewLutHash))
            {
                SkyManager.RequestUpdate();
                m_HasPendingSkyViewLutRebake = true;
                m_PendingSkyViewLutHash = skyViewLutHash;
            }

            properties = new MaterialPropertyBlock();
            properties.SetFloat(SkyUseLutId, useSkyViewLut ? 1.0f : 0.0f);
            properties.SetTexture(SkyViewLutId, useSkyViewLut ? skyViewLut : Texture2D.blackTexture);
            properties.SetInt(SkyBakingViewSampleCountId, Mathf.Max(viewSampleCount, 1));
            properties.SetVector(SkyCameraPositionPsId, parameters.skyCameraPositionPS);
            properties.SetVector(SkySunDirectionId, parameters.skySunDirection);
            properties.SetVector(SkySunColorId, parameters.skySunColor);
            properties.SetVector(SkyPlanetParamsId, parameters.skyPlanetParams);
            properties.SetVector(SkyAirScatteringId, parameters.skyAirScattering);
            properties.SetVector(SkyAirExtinctionId, parameters.skyAirExtinction);
            properties.SetVector(SkyAerosolScatteringId, parameters.skyAerosolScattering);
            properties.SetVector(SkyAerosolExtinctionId, parameters.skyAerosolExtinction);
            properties.SetVector(SkyOzoneExtinctionId, parameters.skyOzoneExtinction);
            properties.SetVector(SkyOzoneParamsId, parameters.skyOzoneParams);
            properties.SetVector(SkyGroundTintId, parameters.skyGroundTint);
            if (hasMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, materialParameters, volume);

            return true;
        }

        private static bool CanUseSkyViewLut()
        {
            return PipelineResourceManager.Get<VividRPCoreResources>()?.AtmosphereLUTCompute != null;
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
    }
}
