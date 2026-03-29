using System;
using UnityEngine;

namespace VividRP.Runtime
{
    internal sealed class HDRISkyRenderer : ISkyRenderer
    {
        public SkyType Type => SkyType.HDRI;

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
                sky?.exposure.value ?? 1.0f,
                sky?.rotation.value ?? 0.0f);
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData)
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

            skyData.activeSkyType = SkyType.HDRI;
            skyData.specularCubemap = cubemap;
            skyData.tint = sky?.tint.value ?? Color.white;
            skyData.exposure = sky?.exposure.value ?? 1.0f;
            skyData.rotation = sky?.rotation.value ?? 0.0f;
            skyData.hasDiffuseSH = SkyDiffuseSHUtility.TryProjectCubemapToSH(
                cubemap,
                skyData.tint,
                skyData.exposure,
                skyData.rotation,
                out skyData.diffuseSH);
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
