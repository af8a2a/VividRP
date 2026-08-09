using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividShadowData : ContextItem
    {
        public const int MaxCascadeCount = 4;
        private const int AtlasGridSize = 2;
        private const float CascadeBlendCullingFactor = 0.6f;

        public bool isCSMActive;
        public int cascadeCount;
        public float maxShadowDistance;
        public int atlasResolution;
        public int cascadeResolution;
        public float normalBias;

        internal int mainLightVisibleIndex = -1;
        internal float slopeScaleDepthBias;
        internal Vector4 shadowCasterState;

        public readonly Matrix4x4[] viewMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] projMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] viewProjMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Vector4[] cascadeSpheres = new Vector4[MaxCascadeCount];
        public readonly Vector4[] cascadeAtlasScaleOffsets = new Vector4[MaxCascadeCount];
        public readonly float[] cascadeWorldTexelSizes = new float[MaxCascadeCount];
        public readonly float[] cascadeBorders = new float[MaxCascadeCount];
        internal readonly ShadowSplitData[] splitData = new ShadowSplitData[MaxCascadeCount];

        public override void Reset()
        {
            isCSMActive = false;
            cascadeCount = 0;
            maxShadowDistance = 0f;
            atlasResolution = 0;
            cascadeResolution = 0;
            normalBias = 0f;
            mainLightVisibleIndex = -1;
            slopeScaleDepthBias = 0f;
            shadowCasterState = Vector4.zero;

            for (int i = 0; i < MaxCascadeCount; i++)
            {
                viewMatrices[i] = Matrix4x4.identity;
                projMatrices[i] = Matrix4x4.identity;
                viewProjMatrices[i] = Matrix4x4.identity;
                cascadeSpheres[i] = Vector4.zero;
                cascadeAtlasScaleOffsets[i] = Vector4.zero;
                cascadeWorldTexelSizes[i] = 0f;
                cascadeBorders[i] = 0f;
                splitData[i] = default;
            }
        }

        internal void Update(CullingResults cullingResults, VividLightData lightData)
        {
            // ContextContainer is shared by all cameras. Always clear the previous camera's data,
            // including inactive and failed shadow configurations.
            Reset();

            var csmSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (csmSettings == null || !csmSettings.IsActive())
                return;

            if (!TryResolveVisibleMainDirectionalLight(lightData, out var light, out var additionalLightData)
                || light == null
                || additionalLightData == null
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.shadows == LightShadows.None)
            {
                return;
            }

            mainLightVisibleIndex = lightData.mainLightIndex;
            cascadeCount = Mathf.Clamp(csmSettings.cascadeCount.value, 1, MaxCascadeCount);
            atlasResolution = Mathf.Max(AtlasGridSize, additionalLightData.resolvedShadowAtlasResolution);
            cascadeResolution = Mathf.Max(1, atlasResolution / AtlasGridSize);
            maxShadowDistance = csmSettings.maxShadowDistance.value;
            normalBias = Mathf.Max(0.0f, additionalLightData.normalBias);
            slopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);
            shadowCasterState = BuildShadowCasterState(lightData.mainVisibleLight);

            Vector3 splitRatios = csmSettings.GetCascadeSplitRatios();
            Vector4 borderRatios = csmSettings.GetCascadeBorderRatios();
            for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
            {
                bool success = cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    mainLightVisibleIndex,
                    cascadeIndex,
                    cascadeCount,
                    splitRatios,
                    cascadeResolution,
                    QualitySettings.shadowNearPlaneOffset,
                    out viewMatrices[cascadeIndex],
                    out projMatrices[cascadeIndex],
                    out splitData[cascadeIndex]);

                if (!success)
                {
                    Reset();
                    return;
                }

                // Match HDRP/Unity's directional cascade overlap. Higher values cull more
                // casters, which causes blend regions to lose moving occluders.
                splitData[cascadeIndex].shadowCascadeBlendCullingFactor = CascadeBlendCullingFactor;
                StabilizeCascadeProjection(
                    ref projMatrices[cascadeIndex],
                    viewMatrices[cascadeIndex],
                    cascadeResolution);

                Vector4 sphere = splitData[cascadeIndex].cullingSphere;
                viewProjMatrices[cascadeIndex] = BuildWorldToShadowMatrix(
                    projMatrices[cascadeIndex],
                    viewMatrices[cascadeIndex]);
                cascadeSpheres[cascadeIndex] = new Vector4(
                    sphere.x,
                    sphere.y,
                    sphere.z,
                    sphere.w * sphere.w);
                cascadeWorldTexelSizes[cascadeIndex] = ComputeCascadeWorldTexelSize(
                    projMatrices[cascadeIndex],
                    cascadeResolution);
                cascadeBorders[cascadeIndex] = borderRatios[cascadeIndex];
            }

            ComputeAtlasLayout();
            isCSMActive = true;
        }

        internal void ScheduleShadowCasterCulling(
            ScriptableRenderContext context,
            CullingResults cullingResults)
        {
            if (!isCSMActive
                || mainLightVisibleIndex < 0
                || mainLightVisibleIndex >= cullingResults.visibleLights.Length
                || cascadeCount <= 0)
            {
                return;
            }

            var perLightInfos = new NativeArray<LightShadowCasterCullingInfo>(
                cullingResults.visibleLights.Length,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var splitBuffer = new NativeArray<ShadowSplitData>(
                cascadeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
                    splitBuffer[cascadeIndex] = splitData[cascadeIndex];

                perLightInfos[mainLightVisibleIndex] = new LightShadowCasterCullingInfo
                {
                    splitRange = new RangeInt(0, cascadeCount),
                    projectionType = BatchCullingProjectionType.Orthographic,
                };

                context.CullShadowCasters(
                    cullingResults,
                    new ShadowCastersCullingInfos
                    {
                        perLightInfos = perLightInfos,
                        splitBuffer = splitBuffer,
                    });
            }
            finally
            {
                splitBuffer.Dispose();
                perLightInfos.Dispose();
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

        private static bool TryResolveVisibleMainDirectionalLight(
            VividLightData lightData,
            out Light light,
            out VividAdditionalLightData additionalLightData)
        {
            light = null;
            additionalLightData = null;

            if (lightData == null
                || !lightData.hasMainDirectionalLight
                || !lightData.hasVisibleLights
                || lightData.mainLightIndex < 0
                || lightData.mainLightIndex >= lightData.visibleLights.Length)
            {
                return false;
            }

            light = lightData.visibleLights[lightData.mainLightIndex].light;
            if (light == null
                || light.type != LightType.Directional
                || !light.GetEntityId().Equals(lightData.mainDirectionalLightEntityId))
            {
                light = null;
                return false;
            }

            return light.TryGetComponent(out additionalLightData);
        }

        private static Matrix4x4 BuildWorldToShadowMatrix(Matrix4x4 projMatrix, Matrix4x4 viewMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projMatrix.m20 = -projMatrix.m20;
                projMatrix.m21 = -projMatrix.m21;
                projMatrix.m22 = -projMatrix.m22;
                projMatrix.m23 = -projMatrix.m23;
            }

            var worldToShadow = projMatrix * viewMatrix;
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            return textureScaleAndBias * worldToShadow;
        }

        private static Vector4 BuildShadowCasterState(in VisibleLight shadowLight)
        {
            // Match HDRP's directional shadow path: rely on raster slope-scale depth bias,
            // receiver normal bias, and a tiny fixed compare bias instead of caster vertex offsets.
            return new Vector4(0.0f, 0.0f, (float)shadowLight.lightType, 0.0f);
        }

        private static void StabilizeCascadeProjection(
            ref Matrix4x4 projMatrix,
            Matrix4x4 viewMatrix,
            float cascadeResolution)
        {
            if (cascadeResolution <= 0.0f)
                return;

            Vector4 originClip = projMatrix * viewMatrix * new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            float texelSizeClip = 2.0f / cascadeResolution;
            projMatrix.m03 -= originClip.x % texelSizeClip;
            projMatrix.m13 -= originClip.y % texelSizeClip;
        }

        private static float ComputeCascadeWorldTexelSize(
            Matrix4x4 lightProjectionMatrix,
            float shadowResolution)
        {
            float projectionScale = Mathf.Max(Mathf.Abs(lightProjectionMatrix.m00), 1e-6f);
            float frustumSize = 2.0f / projectionScale;
            float texelSize = frustumSize / Mathf.Max(shadowResolution, 1.0f);
            return texelSize * Mathf.Sqrt(2.0f);
        }
    }
}
