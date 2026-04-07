using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    internal sealed class HDRISkyRenderer : ISkyRenderer
    {
        private const string HDRISkyShaderName = "Hidden/VividRP/HDRISky";

        private static readonly int SkyCubemapId = Shader.PropertyToID("_SkyCubemap");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int SkyParamId = Shader.PropertyToID("_SkyParam");

        private Material m_Material;
        private RenderTexture m_AmbientProbeCubemap;
        private int m_AmbientProbeBakingPass = -1;
        private int m_AmbientProbeSkyHash;

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
            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var cubemap = GetSkyCubemap();
            return HashCode.Combine(
                cubemap != null ? cubemap.GetEntityId() : EntityId.None,
                sky?.tint.value ?? Color.white,
                sky?.exposure.value ?? 0.0f,
                sky?.rotation.value ?? 0.0f,
                SkySettingsVolume.GetGeneratedCubemapResolution(skySettings));
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)
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

            var skyHash = GetSkyHash(context);
            skyData.activeSkyType = SkyType.HDRI;
            skyData.specularCubemap = cubemap;
            skyData.tint = sky?.tint.value ?? Color.white;
            skyData.exposure = sky?.exposure.value ?? 0.0f;
            skyData.rotation = sky?.rotation.value ?? 0.0f;
            var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(VividVolumeManagerUtility.GetSkySettingsVolume());
            if (NeedsAmbientProbeRebuild(skyHash, generatedCubemapResolution) && CanBakeAmbientProbe())
            {
                EnsureAmbientProbeCubemap(generatedCubemapResolution);
                if (RebuildAmbientProbeCubemap(cmd, cubemap, skyData.tint, skyData.exposure, skyData.rotation))
                    m_AmbientProbeSkyHash = skyHash;
            }

            var useBakedAmbientProbe = CanBakeAmbientProbe()
                && m_AmbientProbeCubemap != null
                && m_AmbientProbeSkyHash == skyHash;

            skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : cubemap;
            skyData.ambientProbeTint = useBakedAmbientProbe ? Color.white : skyData.tint;
            skyData.ambientProbeExposure = useBakedAmbientProbe ? 0.0f : skyData.exposure;
            skyData.ambientProbeRotation = useBakedAmbientProbe ? 0.0f : skyData.rotation;
            skyData.skyHash = skyHash;
            skyData.ambientProbeHash = skyHash;
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

        private bool NeedsAmbientProbeRebuild(int skyHash, int resolution)
        {
            return !IsCubemapValid(m_AmbientProbeCubemap, resolution) || m_AmbientProbeSkyHash != skyHash;
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
                volumeDepth = 6,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
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
                && texture.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        }

        private bool RebuildAmbientProbeCubemap(CommandBuffer cmd, Cubemap cubemap, Color tint, float exposure, float rotation)
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
            properties.SetVector(SkyParamId, HDRISkyPass.BuildSkyParam(exposure, rotation));

            SkyCubemapBakingUtility.RenderSkyToCubemap(
                cmd,
                m_AmbientProbeCubemap,
                m_Material,
                properties,
                m_AmbientProbeBakingPass);
            return true;
        }
    }
}
