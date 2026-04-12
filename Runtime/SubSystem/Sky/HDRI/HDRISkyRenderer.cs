using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

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
        private static readonly ProfilingSampler s_AmbientProbeMissingTextureSampler = new("HDRISkyRenderer.RebuildAmbientProbe (MissingTexture)");
        private static readonly ProfilingSampler s_AmbientProbeResolutionChangedSampler = new("HDRISkyRenderer.RebuildAmbientProbe (ResolutionChanged)");
        private static readonly ProfilingSampler s_AmbientProbeParametersChangedSampler = new("HDRISkyRenderer.RebuildAmbientProbe (ParametersChanged)");

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

        public void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, bool forceRebuild = false)
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
            
            float intensity, phi;
            HDRISkyPass.GetParameters(out intensity, out phi);
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
    }
}
