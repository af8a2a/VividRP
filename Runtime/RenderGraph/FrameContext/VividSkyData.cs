using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividSkyData : ContextItem
    {
        public SkyType activeSkyType;
        public Texture specularCubemap;
        public Texture ambientProbeCubemap;
        internal RTHandle atmosphericScatteringLutHandle;
        public Color tint;
        public Color ambientProbeTint;
        public float exposure;
        public float ambientProbeExposure;
        public float rotation;
        public float ambientProbeRotation;
        public int skyHash;
        public int skyContentHash;
        public int ambientProbeHash;
        internal bool specularCubemapDirty;

        public override void Reset()
        {
            activeSkyType = SkyType.None;
            specularCubemap = null;
            ambientProbeCubemap = null;
            atmosphericScatteringLutHandle = null;
            tint = Color.white;
            ambientProbeTint = Color.white;
            exposure = 1.0f;
            ambientProbeExposure = 1.0f;
            rotation = 0.0f;
            ambientProbeRotation = 0.0f;
            skyHash = 0;
            skyContentHash = 0;
            ambientProbeHash = 0;
            specularCubemapDirty = false;
        }

        internal void CopyFrom(VividSkyData other)
        {
            if (other == null)
            {
                Reset();
                return;
            }

            activeSkyType = other.activeSkyType;
            specularCubemap = other.specularCubemap;
            ambientProbeCubemap = other.ambientProbeCubemap;
            atmosphericScatteringLutHandle = other.atmosphericScatteringLutHandle;
            tint = other.tint;
            ambientProbeTint = other.ambientProbeTint;
            exposure = other.exposure;
            ambientProbeExposure = other.ambientProbeExposure;
            rotation = other.rotation;
            ambientProbeRotation = other.ambientProbeRotation;
            skyHash = other.skyHash;
            skyContentHash = other.skyContentHash;
            ambientProbeHash = other.ambientProbeHash;
            specularCubemapDirty = other.specularCubemapDirty;
        }
    }
}
