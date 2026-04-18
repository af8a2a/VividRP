using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class HDRISkyRenderer : ISkyRenderer
    {
        private enum AmbientProbeRebuildReason
        {
            None,
            MissingTexture,
            ResolutionChanged,
            ParametersChanged
        }
        
        private const string HDRISkyShaderName = "Hidden/VividRP/HDRISky";

        private static readonly int SkyCubemapId = Shader.PropertyToID("_SkyCubemap");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int SkyParamId = Shader.PropertyToID("_SkyParam");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly ProfilingSampler s_AmbientProbeMissingTextureSampler = new("HDRISkyRenderer.RebuildAmbientProbe (MissingTexture)");
        private static readonly ProfilingSampler s_AmbientProbeResolutionChangedSampler = new("HDRISkyRenderer.RebuildAmbientProbe (ResolutionChanged)");
        private static readonly ProfilingSampler s_AmbientProbeParametersChangedSampler = new("HDRISkyRenderer.RebuildAmbientProbe (ParametersChanged)");

        private Material m_Material;
        private RenderTexture m_AmbientProbeCubemap;
        private int m_AmbientProbeBakingPass = -1;
        private int m_AmbientProbeSkyHash;
        private RenderGraphTexture m_ColorTarget;
        private RenderGraphTexture m_DepthTexture;
        private Matrix4x4 m_PixelCoordToViewDirMatrix;
        private Texture m_RenderCubemap;
        private Color m_RenderTint = Color.white;
        private float m_RenderIntensityMultiplier = 1.0f;
        private float m_RenderRotation;
        private Rect m_RenderViewport;
        private bool m_ShouldRenderSky;

        public SkyType Type => SkyType.HDRI;

        public void Build(VividRPCoreResources resources)
        {
            var shader = resources?.HDRISkyShader;
            shader ??= Shader.Find(HDRISkyShaderName);

            if (shader == null)
                return;

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_AmbientProbeBakingPass = m_Material.FindPass("HDRISkyBaking");
        }

        public bool IsActive()
        {
            return GetSkyCubemap() != null;
        }

        public int GetSkyHash(in SkyRendererContext context)
        {
            var sky = VividVolumeManagerUtility.GetHDRISkyVolume();
            var cubemap = GetSkyCubemap();
            var intensityMultiplier = ResolveSkyIntensityMultiplier(sky);
            return HashCode.Combine(
                cubemap != null ? cubemap.GetEntityId() : EntityId.None,
                Color.white,
                intensityMultiplier,
                sky?.rotation.value ?? 0.0f,
                16);
        }

        public void UpdateFrameResources(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)
        {
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, int skyHash, bool forceRebuild)
        {
            if (skyData == null)
                return;

            var sky = VividVolumeManagerUtility.GetHDRISkyVolume();
            var cubemap = GetSkyCubemap();
            if (cubemap == null)
            {
                skyData.Reset();
                return;
            }

            var intensityMultiplier = ResolveSkyIntensityMultiplier(sky);
            var rotation = sky?.rotation.value ?? 0.0f;
            skyData.activeSkyType = SkyType.HDRI;
            skyData.specularCubemap = cubemap;
            skyData.tint = Color.white;
            skyData.exposure = intensityMultiplier;
            skyData.rotation = rotation;
            var generatedCubemapResolution = 16;
            var ambientProbeRebuildReason = ResolveAmbientProbeRebuildReason(skyHash, generatedCubemapResolution);
            if (forceRebuild && ambientProbeRebuildReason == AmbientProbeRebuildReason.None)
                ambientProbeRebuildReason = AmbientProbeRebuildReason.ParametersChanged;
            if (ambientProbeRebuildReason != AmbientProbeRebuildReason.None && CanBakeAmbientProbe() && cmd != null)
            {
                EnsureAmbientProbeCubemap(generatedCubemapResolution);
                using (new ProfilingScope(cmd, GetAmbientProbeRebuildSampler(ambientProbeRebuildReason)))
                {
                    if (RebuildAmbientProbeCubemap(cmd, cubemap, skyData.tint, skyData.exposure, skyData.rotation))
                        m_AmbientProbeSkyHash = skyHash;
                }
            }

            var useBakedAmbientProbe = CanBakeAmbientProbe()
                && m_AmbientProbeCubemap != null
                && m_AmbientProbeSkyHash == skyHash;

            skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : cubemap;
            skyData.ambientProbeTint = useBakedAmbientProbe ? Color.white : skyData.tint;
            skyData.ambientProbeExposure = useBakedAmbientProbe ? 1.0f : skyData.exposure;
            skyData.ambientProbeRotation = useBakedAmbientProbe ? 0.0f : skyData.rotation;
            skyData.skyHash = skyHash;
            skyData.ambientProbeHash = skyHash;
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
            m_PixelCoordToViewDirMatrix = context.cameraData?.GetPixelCoordToViewDirWSMatrix() ?? Matrix4x4.identity;
            m_RenderViewport = ResolveRenderViewport(context.cameraData, colorTarget);
            m_ShouldRenderSky = m_Material != null
                                && skyData != null
                                && skyData.activeSkyType == SkyType.HDRI
                                && skyData.specularCubemap != null;

            if (!m_ShouldRenderSky)
            {
                m_RenderCubemap = null;
                return;
            }

            m_RenderCubemap = skyData.specularCubemap;
            m_RenderTint = skyData.tint;
            m_RenderIntensityMultiplier = skyData.exposure;
            m_RenderRotation = skyData.rotation;
        }

        public void RenderSky(CommandBuffer cmd)
        {
            if (!m_ShouldRenderSky
                || cmd == null
                || m_ColorTarget == null
                || m_DepthTexture == null
                || !m_ColorTarget.innerHandle.IsValid()
                || !m_DepthTexture.innerHandle.IsValid())
            {
                return;
            }

            cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);
            cmd.SetViewport(m_RenderViewport);

            var properties = new MaterialPropertyBlock();
            GetSkyParameters(m_RenderIntensityMultiplier, m_RenderRotation, out var intensity, out var phi);
            properties.SetTexture(SkyCubemapId, m_RenderCubemap);
            properties.SetColor(SkyTintId, m_RenderTint);
            properties.SetVector(SkyParamId, new Vector4(intensity, 0.0f, Mathf.Cos(phi), Mathf.Sin(phi)));
            properties.SetMatrix(PixelCoordToViewDirWSId, m_PixelCoordToViewDirMatrix);

            CoreUtils.DrawFullScreen(cmd, m_Material, properties, 0);
        }

        public void Dispose()
        {
            if (m_AmbientProbeCubemap != null)
            {
                m_AmbientProbeCubemap.Release();
                CoreUtils.Destroy(m_AmbientProbeCubemap);
                m_AmbientProbeCubemap = null;
            }

            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_AmbientProbeBakingPass = -1;
            m_AmbientProbeSkyHash = 0;
            m_ColorTarget = null;
            m_DepthTexture = null;
            m_RenderCubemap = null;
            m_RenderTint = Color.white;
            m_RenderIntensityMultiplier = 1.0f;
            m_RenderRotation = 0.0f;
            m_RenderViewport = default;
            m_ShouldRenderSky = false;
        }

        private static Cubemap GetSkyCubemap()
        {
            return VividVolumeManagerUtility.GetHDRISkyVolume()?.GetSkyCubemapOrDefault()
                   ?? HDRISkyVolume.GetDefaultSkyCubemap();
        }

        private bool CanBakeAmbientProbe()
        {
            return m_Material != null && m_AmbientProbeBakingPass >= 0;
        }

        private AmbientProbeRebuildReason ResolveAmbientProbeRebuildReason(int skyHash, int resolution)
        {
            if (m_AmbientProbeCubemap == null || !m_AmbientProbeCubemap.IsCreated())
                return AmbientProbeRebuildReason.MissingTexture;

            if (!IsCubemapValid(m_AmbientProbeCubemap, resolution))
                return AmbientProbeRebuildReason.ResolutionChanged;

            return m_AmbientProbeSkyHash != skyHash
                ? AmbientProbeRebuildReason.ParametersChanged
                : AmbientProbeRebuildReason.None;
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
                name = "VividHDRISkyAmbientProbe",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Cube,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_AmbientProbeCubemap.Create();
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

        private bool RebuildAmbientProbeCubemap(CommandBuffer cmd, Cubemap cubemap, Color tint, float intensityMultiplier, float rotation)
        {
            if (cmd == null
                || cubemap == null
                || m_AmbientProbeCubemap == null
                || !CanBakeAmbientProbe())
            {
                return false;
            }

            var properties = new MaterialPropertyBlock();
            properties.SetTexture(SkyCubemapId, cubemap);
            properties.SetColor(SkyTintId, tint);

            GetSkyParameters(intensityMultiplier, rotation, out var intensity, out var phi);
            properties.SetVector(SkyParamId, new Vector4(intensity, 0.0f, Mathf.Cos(phi), Mathf.Sin(phi)));

            SkyCubemapBakingUtility.RenderSkyToCubemap(
                cmd,
                m_AmbientProbeCubemap,
                m_Material,
                properties,
                m_AmbientProbeBakingPass);
            return true;
        }

        private static ProfilingSampler GetAmbientProbeRebuildSampler(AmbientProbeRebuildReason reason)
        {
            return reason switch
            {
                AmbientProbeRebuildReason.ResolutionChanged => s_AmbientProbeResolutionChangedSampler,
                AmbientProbeRebuildReason.ParametersChanged => s_AmbientProbeParametersChangedSampler,
                _ => s_AmbientProbeMissingTextureSampler,
            };
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

        private static float ResolveSkyIntensityMultiplier(HDRISkyVolume sky)
        {
            return sky != null ? sky.GetIntensityFromSettings() : 1.0f;
        }

        private static void GetSkyParameters(float intensityMultiplier, float rotation, out float intensity, out float phi)
        {
            intensity = Mathf.Max(intensityMultiplier, 0.0f);
            phi = -Mathf.Deg2Rad * rotation;
        }
    }
}
