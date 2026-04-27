using UnityEngine;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal struct DecalData
    {
        public Matrix4x4 worldToDecal;
        public Texture2D baseColorTexture;
        public Texture2D normalTexture;
        public Texture2D metallicTexture;
        public Texture2D roughnessTexture;
        public Color baseColor;
        public float blendDistance;
        public float metallic;
        public float roughness;
    }
}
