using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividSkyData : ContextItem
    {
        public SkyType activeSkyType;
        public bool hasDiffuseSH;
        public SphericalHarmonicsL2 diffuseSH;
        public Texture specularCubemap;
        public Color tint;
        public float exposure;
        public float rotation;
        public int skyHash;

        public override void Reset()
        {
            activeSkyType = SkyType.None;
            hasDiffuseSH = false;
            diffuseSH = default;
            specularCubemap = null;
            tint = Color.white;
            exposure = 0.0f;
            rotation = 0.0f;
            skyHash = 0;
        }

        internal void CopyFrom(VividSkyData other)
        {
            if (other == null)
            {
                Reset();
                return;
            }

            activeSkyType = other.activeSkyType;
            hasDiffuseSH = other.hasDiffuseSH;
            diffuseSH = other.diffuseSH;
            specularCubemap = other.specularCubemap;
            tint = other.tint;
            exposure = other.exposure;
            rotation = other.rotation;
            skyHash = other.skyHash;
        }
    }
}
