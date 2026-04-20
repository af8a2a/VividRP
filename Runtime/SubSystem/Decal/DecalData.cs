using UnityEngine;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal struct DecalData
    {
        public Matrix4x4 worldToDecal;
        public Texture2D baseColorTexture;
        public Texture2D normalTexture;
        public Color baseColor;
        public float blendDistance;
    }
}
