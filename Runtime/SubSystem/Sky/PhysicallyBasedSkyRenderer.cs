using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    internal sealed class PhysicallyBasedSkyRenderer : ISkyRenderer
    {
        private const float ObserverHeight = 2.0f;
        private const string SkyCubemapKernelName = "SkyCubemap";

        private static readonly int SkyViewLutId = Shader.PropertyToID("_SkyViewLUT");
        private static readonly int SkyUseLutId = Shader.PropertyToID("_SkyUseLUT");
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
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");
        private static readonly int SkyCubemapOutputId = Shader.PropertyToID("_SkyCubemapOutput");

        internal const float SunAngularDiameterDegrees = 0.53f;
        internal const float SunIlluminanceScale = 20.0f;

        private ComputeShader m_AtmosphereLutCompute;
        private Material m_SkyMaterial;
        private int m_SkyCubemapKernel = -1;
        private int m_AmbientProbeBakingPass = -1;
        private RenderTexture m_RuntimeSkyCubemap;
        private RenderTexture m_RuntimeSkyCubemapFaces;
        private RenderTexture m_AmbientProbeCubemap;
        private int m_RuntimeSkyHash;
        private int m_AmbientProbeSkyHash;

        public SkyType Type => SkyType.PhysicallyBased;

        public void Build(VividRPCoreResources resources)
        {
            m_AtmosphereLutCompute = resources?.AtmosphereLUTCompute;
            m_SkyCubemapKernel = m_AtmosphereLutCompute != null
                ? m_AtmosphereLutCompute.FindKernel(SkyCubemapKernelName)
                : -1;

            var shader = resources?.PhysicallyBasedSkyShader;
            shader ??= Shader.Find(PhysicallyBasedSkyPass.PhysicallyBasedSkyShaderName);
            if (shader != null)
            {
                m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_AmbientProbeBakingPass = m_SkyMaterial.FindPass("PhysicallyBasedSkyBaking");
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

            return HashCode.Combine(
                volume.GetHashCode(),
                SkySettingsVolume.GetGeneratedCubemapResolution(skySettings),
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
            var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(VividVolumeManagerUtility.GetSkySettingsVolume());
            if (NeedsRuntimeCubemapRebuild(hash, generatedCubemapResolution) && CanRebuildRuntimeCubemap())
            {
                EnsureRuntimeCubemap(generatedCubemapResolution);
                RebuildRuntimeCubemap(volume, context, cmd);
                m_RuntimeSkyHash = hash;
            }

            if (NeedsAmbientProbeCubemapRebuild(hash, generatedCubemapResolution) && CanBakeAmbientProbe())
            {
                EnsureAmbientProbeCubemap(generatedCubemapResolution);
                if (RebuildAmbientProbeCubemap(volume, context, cmd))
                    m_AmbientProbeSkyHash = hash;
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
            if (m_RuntimeSkyCubemapFaces != null)
            {
                m_RuntimeSkyCubemapFaces.Release();
                CoreUtils.Destroy(m_RuntimeSkyCubemapFaces);
                m_RuntimeSkyCubemapFaces = null;
            }

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

            m_AtmosphereLutCompute = null;
            m_SkyCubemapKernel = -1;
            m_AmbientProbeBakingPass = -1;
            m_RuntimeSkyHash = 0;
            m_AmbientProbeSkyHash = 0;
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
            return m_AtmosphereLutCompute != null
                && m_SkyCubemapKernel >= 0
                && SystemInfo.supportsComputeShaders;
        }

        private bool CanBakeAmbientProbe()
        {
            return m_SkyMaterial != null && m_AmbientProbeBakingPass >= 0;
        }

        private bool NeedsRuntimeCubemapRebuild(int hash, int resolution)
        {
            return !IsCubemapValid(m_RuntimeSkyCubemap, resolution)
                || !IsFaceArrayValid(m_RuntimeSkyCubemapFaces, resolution)
                || m_RuntimeSkyHash != hash;
        }

        private bool NeedsAmbientProbeCubemapRebuild(int hash, int resolution)
        {
            return !IsCubemapValid(m_AmbientProbeCubemap, resolution) || m_AmbientProbeSkyHash != hash;
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

            if (IsFaceArrayValid(m_RuntimeSkyCubemapFaces, resolution))
                return;

            if (m_RuntimeSkyCubemapFaces != null)
            {
                m_RuntimeSkyCubemapFaces.Release();
                CoreUtils.Destroy(m_RuntimeSkyCubemapFaces);
                m_RuntimeSkyCubemapFaces = null;
            }

            m_RuntimeSkyCubemapFaces = new RenderTexture(resolution, resolution, 0)
            {
                name = "VividPhysicallyBasedSkyFaces",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            m_RuntimeSkyCubemapFaces.Create();
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

        private void RebuildRuntimeCubemap(PhysicallyBasedSkyVolume volume, in SkyRendererContext context, CommandBuffer cmd)
        {
            if (cmd == null
                || m_RuntimeSkyCubemap == null
                || m_RuntimeSkyCubemapFaces == null
                || !PhysicallyBasedSkyShaderParameterBuilder.TryBuild(volume, context, out var parameters))
            {
                return;
            }

            BindCommonParameters(cmd, parameters);
            cmd.SetComputeTextureParam(m_AtmosphereLutCompute, m_SkyCubemapKernel, SkyCubemapOutputId, m_RuntimeSkyCubemapFaces);
            var cubemapResolution = m_RuntimeSkyCubemap.width;
            cmd.DispatchCompute(
                m_AtmosphereLutCompute,
                m_SkyCubemapKernel,
                CoreUtils.DivRoundUp(cubemapResolution, 8),
                CoreUtils.DivRoundUp(cubemapResolution, 8),
                6);

            for (var face = 0; face < 6; face++)
                cmd.CopyTexture(m_RuntimeSkyCubemapFaces, face, 0, m_RuntimeSkyCubemap, face, 0);

            cmd.GenerateMips(m_RuntimeSkyCubemap);
        }

        private bool RebuildAmbientProbeCubemap(PhysicallyBasedSkyVolume volume, in SkyRendererContext context, CommandBuffer cmd)
        {
            if (cmd == null
                || m_AmbientProbeCubemap == null
                || !CanBakeAmbientProbe()
                || !PhysicallyBasedSkyShaderParameterBuilder.TryBuildForAmbientProbe(volume, context, out var parameters))
            {
                return false;
            }

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(SkyUseLutId, 0.0f);
            properties.SetTexture(SkyViewLutId, Texture2D.blackTexture);
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

            SkyCubemapBakingUtility.RenderSkyToCubemap(
                cmd,
                m_AmbientProbeCubemap,
                m_SkyMaterial,
                properties,
                m_AmbientProbeBakingPass);
            return true;
        }

        private void BindCommonParameters(CommandBuffer cmd, in PhysicallyBasedSkyShaderParameters parameters)
        {
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyCameraPositionPsId, parameters.skyCameraPositionPS);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkySunDirectionId, parameters.skySunDirection);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkySunColorId, parameters.skySunColor);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyPlanetParamsId, parameters.skyPlanetParams);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyAirScatteringId, parameters.skyAirScattering);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyAirExtinctionId, parameters.skyAirExtinction);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyAerosolScatteringId, parameters.skyAerosolScattering);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyAerosolExtinctionId, parameters.skyAerosolExtinction);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyOzoneExtinctionId, parameters.skyOzoneExtinction);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyOzoneParamsId, parameters.skyOzoneParams);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyGroundTintId, parameters.skyGroundTint);
            cmd.SetComputeVectorParam(m_AtmosphereLutCompute, SkyFogParamsId, parameters.skyFogParams);
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

        private static bool IsFaceArrayValid(RenderTexture texture, int resolution)
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
    }
}
