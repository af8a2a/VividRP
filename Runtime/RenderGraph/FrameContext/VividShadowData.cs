using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividShadowData : ContextItem
    {
        public const int MaxCascadeCount = 4;

        public bool isCSMActive;
        public int cascadeCount;
        public float maxShadowDistance;
        public int atlasResolution;
        public int cascadeResolution;
        public float depthBias;
        public float normalBias;

        public readonly Matrix4x4[] viewMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] projMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] viewProjMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Vector4[] cascadeSpheres = new Vector4[MaxCascadeCount];
        public readonly Vector4[] cascadeAtlasScaleOffsets = new Vector4[MaxCascadeCount];

        public override void Reset()
        {
            isCSMActive = false;
            cascadeCount = 0;
            maxShadowDistance = 0f;
            atlasResolution = 0;
            cascadeResolution = 0;
            depthBias = 0f;
            normalBias = 0f;

            for (int i = 0; i < MaxCascadeCount; i++)
            {
                viewMatrices[i] = Matrix4x4.identity;
                projMatrices[i] = Matrix4x4.identity;
                viewProjMatrices[i] = Matrix4x4.identity;
                cascadeSpheres[i] = Vector4.zero;
                cascadeAtlasScaleOffsets[i] = Vector4.zero;
            }
        }

        /// <summary>
        /// Computes the atlas scale and offset for each cascade in a 2x2 grid layout.
        /// </summary>
        public void ComputeAtlasLayout()
        {
            // 2x2 grid: each cascade occupies a quarter of the atlas
            // Scale is 0.5 for both x and y
            // Offsets: C0=(0,0), C1=(0.5,0), C2=(0,0.5), C3=(0.5,0.5)
            float scale = 0.5f;
            for (int i = 0; i < MaxCascadeCount; i++)
            {
                if (i < cascadeCount)
                {
                    float offsetX = (i % 2) * scale;
                    float offsetY = (i / 2) * scale;
                    cascadeAtlasScaleOffsets[i] = new Vector4(scale, scale, offsetX, offsetY);
                }
                else
                {
                    cascadeAtlasScaleOffsets[i] = Vector4.zero;
                }
            }
        }
    }
}
