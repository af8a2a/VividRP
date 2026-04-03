using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class HDRISkyRenderer : ISkyRenderer
    {
        private readonly SkyAmbientProbeConvolution m_AmbientProbeConvolution;

        public SkyType Type => SkyType.HDRI;

        public HDRISkyRenderer(SkyAmbientProbeConvolution ambientProbeConvolution)
        {
            m_AmbientProbeConvolution = ambientProbeConvolution ?? throw new ArgumentNullException(nameof(ambientProbeConvolution));
        }

        public void Build(VividRPCoreResources resources)
        {
        }

        public bool IsActive()
        {
            return GetSkyCubemap() != null;
        }

        public int GetSkyHash(in SkyRendererContext context)
        {
            var sky = VividVolumeManagerUtility.GetHDRISkyVolume();
            var cubemap = GetSkyCubemap();
            return HashCode.Combine(
                cubemap != null ? cubemap.GetEntityId() : EntityId.None,
                sky?.tint.value ?? Color.white,
                sky?.exposure.value ?? 0.0f,
                sky?.rotation.value ?? 0.0f);
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
            skyData.skyHash = skyHash;
            skyData.hasDiffuseSH = false;
            skyData.diffuseSH = default;

            m_AmbientProbeConvolution.RequestUpdate(
                cmd,
                cubemap,
                skyData.tint,
                skyData.exposure,
                skyData.rotation,
                skyHash);
        }

        public void Dispose()
        {
        }

        private static Cubemap GetSkyCubemap()
        {
            return VividVolumeManagerUtility.GetHDRISkyVolume()?.GetSkyCubemapOrDefault()
                   ?? HDRISkyVolume.GetDefaultSkyCubemap();
        }
    }
}
