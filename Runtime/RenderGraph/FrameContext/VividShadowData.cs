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
        public readonly float[] cascadeWorldTexelSizes = new float[MaxCascadeCount];
        public readonly float[] cascadeBorders = new float[MaxCascadeCount];

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
                cascadeWorldTexelSizes[i] = 0f;
                cascadeBorders[i] = 0f;
            }
        }

        /// <summary>
        /// Computes the atlas scale and offset for each cascade in a 2x2 grid layout.
        /// </summary>
        public void ComputeAtlasLayout()
        {
            float scale = atlasResolution > 0 ? (float)cascadeResolution / atlasResolution : 0f;
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
