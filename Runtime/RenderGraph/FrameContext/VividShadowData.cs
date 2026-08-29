using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime
{
    public class VividShadowData : ContextItem
    {
        public const int MaxCascadeCount = 4;
        private const int FrustumPlaneCount = 6;
        private const float CascadeBlendCullingFactor = 0.6f;
        private const float MinCascadeRange = 0.001f;
        private const float MinCascadeRadius = 0.001f;

        public bool isCSMActive;
        public int cascadeCount;
        public float maxShadowDistance;
        public int cascadeResolution;
        public float normalBias;

        internal int mainLightVisibleIndex = -1;
        internal bool hasUnityShadowCasters;
        internal float slopeScaleDepthBias;
        internal Vector4 shadowCasterState;

        public readonly Matrix4x4[] viewMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] projMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Matrix4x4[] viewProjMatrices = new Matrix4x4[MaxCascadeCount];
        public readonly Vector4[] cascadeSpheres = new Vector4[MaxCascadeCount];
        public readonly float[] cascadeWorldTexelSizes = new float[MaxCascadeCount];
        public readonly float[] cascadeBorders = new float[MaxCascadeCount];
        internal readonly ShadowSplitData[] splitData = new ShadowSplitData[MaxCascadeCount];

        private readonly Vector3[] m_FrustumCorners = new Vector3[8];
        private readonly Plane[] m_CullingPlanes = new Plane[FrustumPlaneCount];

        public override void Reset()
        {
            isCSMActive = false;
            cascadeCount = 0;
            maxShadowDistance = 0f;
            cascadeResolution = 0;
            normalBias = 0f;
            mainLightVisibleIndex = -1;
            hasUnityShadowCasters = false;
            slopeScaleDepthBias = 0f;
            shadowCasterState = Vector4.zero;

            for (int i = 0; i < MaxCascadeCount; i++)
            {
                viewMatrices[i] = Matrix4x4.identity;
                projMatrices[i] = Matrix4x4.identity;
                viewProjMatrices[i] = Matrix4x4.identity;
                cascadeSpheres[i] = Vector4.zero;
                cascadeWorldTexelSizes[i] = 0f;
                cascadeBorders[i] = 0f;
                splitData[i] = default;
            }
        }

        internal void Update(
            CullingResults cullingResults,
            VividLightData lightData,
            VividCameraData cameraData)
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
            cascadeResolution = Mathf.Max(1, additionalLightData.resolvedShadowMapResolution);
            maxShadowDistance = csmSettings.maxShadowDistance.value;
            normalBias = Mathf.Max(0.0f, additionalLightData.normalBias);
            slopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);
            shadowCasterState = BuildShadowCasterState(lightData.mainVisibleLight);
            Bounds unityShadowCasterBounds = default;
            hasUnityShadowCasters = mainLightVisibleIndex >= 0
                && mainLightVisibleIndex < cullingResults.visibleLights.Length
                && cullingResults.GetShadowCasterBounds(
                    mainLightVisibleIndex,
                    out unityShadowCasterBounds);

            Vector3 splitRatios = csmSettings.GetCascadeSplitRatios();
            Vector4 borderRatios = csmSettings.GetCascadeBorderRatios();
            Bounds primitiveShadowCasterBounds = default;
            bool hasPrimitiveShadowCasterBounds = PassRecorder.HasCascadedShadowCasterPass
                && VividGPUDrivenSystem.TryGetPrimitiveShadowCasterBounds(
                    cameraData?.camera,
                    out primitiveShadowCasterBounds);
            if (!TryCombineShadowCasterBounds(
                    hasUnityShadowCasters,
                    unityShadowCasterBounds,
                    hasPrimitiveShadowCasterBounds,
                    primitiveShadowCasterBounds,
                    out Bounds shadowCasterBounds))
            {
                Reset();
                return;
            }

            for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
            {
                if (!TryGetCascadeDepthRange(
                        cameraData,
                        maxShadowDistance,
                        cascadeIndex,
                        cascadeCount,
                        splitRatios,
                        out float cascadeNearDistance,
                        out float cascadeFarDistance)
                    || !TryBuildCascadeMatrices(
                        cameraData,
                        light,
                        shadowCasterBounds,
                        cascadeNearDistance,
                        cascadeFarDistance,
                        cascadeResolution,
                        QualitySettings.shadowNearPlaneOffset,
                        out viewMatrices[cascadeIndex],
                        out projMatrices[cascadeIndex],
                        out splitData[cascadeIndex]))
                {
                    Reset();
                    return;
                }

                // Match HDRP/Unity's directional cascade overlap. Higher values cull more
                // casters, which causes blend regions to lose moving occluders.
                splitData[cascadeIndex].shadowCascadeBlendCullingFactor = CascadeBlendCullingFactor;

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

            isCSMActive = true;
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

        internal bool TryBuildCascadeMatrices(
            VividCameraData cameraData,
            Light light,
            Bounds shadowCasterBounds,
            float cascadeNearDistance,
            float cascadeFarDistance,
            int shadowResolution,
            float nearPlaneOffset,
            out Matrix4x4 viewMatrix,
            out Matrix4x4 projectionMatrix,
            out ShadowSplitData cascadeSplitData)
        {
            viewMatrix = Matrix4x4.identity;
            projectionMatrix = Matrix4x4.identity;
            cascadeSplitData = default;
            if (cameraData?.camera == null
                || light == null
                || light.type != LightType.Directional
                || shadowResolution <= 0
                || !float.IsFinite(cascadeNearDistance)
                || !float.IsFinite(cascadeFarDistance)
                || cascadeNearDistance <= 0.0f
                || cascadeFarDistance <= cascadeNearDistance
                || !IsFinite(shadowCasterBounds.min)
                || !IsFinite(shadowCasterBounds.max))
            {
                return false;
            }

            if (!TryBuildFrustumSliceCorners(
                    cameraData,
                    cascadeNearDistance,
                    cascadeFarDistance))
            {
                return false;
            }

            Vector3 cascadeCenter = Vector3.zero;
            for (int cornerIndex = 0; cornerIndex < m_FrustumCorners.Length; cornerIndex++)
                cascadeCenter += m_FrustumCorners[cornerIndex];
            cascadeCenter /= m_FrustumCorners.Length;

            float cascadeRadiusSquared = 0.0f;
            for (int cornerIndex = 0; cornerIndex < m_FrustumCorners.Length; cornerIndex++)
            {
                cascadeRadiusSquared = Mathf.Max(
                    cascadeRadiusSquared,
                    (m_FrustumCorners[cornerIndex] - cascadeCenter).sqrMagnitude);
            }

            float cascadeRadius = Mathf.Max(Mathf.Sqrt(cascadeRadiusSquared), MinCascadeRadius);
            cascadeRadius += Mathf.Max(MinCascadeRadius, cascadeRadius * 1e-5f);
            float projectionRadius = cascadeRadius;
            if (shadowResolution > 2)
                projectionRadius *= shadowResolution / (float) (shadowResolution - 2);

            if (!TryNormalize(light.transform.forward, out Vector3 lightForward))
                return false;

            float minimumForwardDistance = float.PositiveInfinity;
            float maximumForwardDistance = float.NegativeInfinity;
            for (int cornerIndex = 0; cornerIndex < m_FrustumCorners.Length; cornerIndex++)
            {
                float forwardDistance = Vector3.Dot(
                    lightForward,
                    m_FrustumCorners[cornerIndex]);
                minimumForwardDistance = Mathf.Min(minimumForwardDistance, forwardDistance);
                maximumForwardDistance = Mathf.Max(maximumForwardDistance, forwardDistance);
            }

            Vector3 casterExtents = shadowCasterBounds.extents;
            float casterCenterDistance = Vector3.Dot(lightForward, shadowCasterBounds.center);
            float casterExtentDistance = Vector3.Dot(
                new Vector3(
                    Mathf.Abs(lightForward.x),
                    Mathf.Abs(lightForward.y),
                    Mathf.Abs(lightForward.z)),
                casterExtents);
            minimumForwardDistance = Mathf.Min(
                minimumForwardDistance,
                casterCenterDistance - casterExtentDistance);
            maximumForwardDistance = Mathf.Max(
                maximumForwardDistance,
                casterCenterDistance + casterExtentDistance);
            if (!float.IsFinite(minimumForwardDistance)
                || !float.IsFinite(maximumForwardDistance)
                || maximumForwardDistance < minimumForwardDistance)
            {
                return false;
            }

            float worldTexelSize = 2.0f * projectionRadius / shadowResolution;
            float depthPadding = Mathf.Max(
                float.IsFinite(nearPlaneOffset) ? nearPlaneOffset : 0.0f,
                Mathf.Max(worldTexelSize, MinCascadeRange));
            float viewPositionForwardDistance = minimumForwardDistance - depthPadding;
            Vector3 viewPosition = cascadeCenter
                + lightForward * (
                    viewPositionForwardDistance
                    - Vector3.Dot(lightForward, cascadeCenter));
            float depthRange = Mathf.Max(
                maximumForwardDistance - minimumForwardDistance + 2.0f * depthPadding,
                MinCascadeRange);

            viewMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f))
                * Matrix4x4.TRS(
                    viewPosition,
                    light.transform.rotation,
                    Vector3.one).inverse;
            projectionMatrix = Matrix4x4.Ortho(
                -projectionRadius,
                projectionRadius,
                -projectionRadius,
                projectionRadius,
                0.0f,
                depthRange);
            StabilizeCascadeProjection(
                ref projectionMatrix,
                viewMatrix,
                shadowResolution);
            cascadeSplitData.cullingSphere = new Vector4(
                cascadeCenter.x,
                cascadeCenter.y,
                cascadeCenter.z,
                cascadeRadius);
            cascadeSplitData.cullingNearPlane = 0.0f;
            SetCullingPlanes(
                ref cascadeSplitData,
                projectionMatrix * viewMatrix);
            return IsCascadeDataUsable(viewMatrix, projectionMatrix, cascadeSplitData);
        }

        internal static bool TryCombineShadowCasterBounds(
            bool hasUnityShadowCasterBounds,
            Bounds unityShadowCasterBounds,
            bool hasPrimitiveShadowCasterBounds,
            Bounds primitiveShadowCasterBounds,
            out Bounds combinedShadowCasterBounds)
        {
            combinedShadowCasterBounds = default;
            if (!hasUnityShadowCasterBounds && !hasPrimitiveShadowCasterBounds)
                return false;

            if ((hasUnityShadowCasterBounds && !IsBoundsUsable(unityShadowCasterBounds))
                || (hasPrimitiveShadowCasterBounds
                    && !IsBoundsUsable(primitiveShadowCasterBounds)))
            {
                return false;
            }

            combinedShadowCasterBounds = hasUnityShadowCasterBounds
                ? unityShadowCasterBounds
                : primitiveShadowCasterBounds;
            if (hasUnityShadowCasterBounds && hasPrimitiveShadowCasterBounds)
                combinedShadowCasterBounds.Encapsulate(primitiveShadowCasterBounds);

            return IsBoundsUsable(combinedShadowCasterBounds);
        }

        internal static bool TryGetCascadeDepthRange(
            VividCameraData cameraData,
            float shadowDistance,
            int cascadeIndex,
            int cascadeCount,
            Vector3 splitRatios,
            out float cascadeNearDistance,
            out float cascadeFarDistance)
        {
            cascadeNearDistance = 0.0f;
            cascadeFarDistance = 0.0f;
            Camera camera = cameraData?.camera;
            if (camera == null
                || cascadeIndex < 0
                || cascadeIndex >= cascadeCount
                || cascadeCount <= 0
                || cascadeCount > MaxCascadeCount
                || !float.IsFinite(shadowDistance))
            {
                return false;
            }

            float cameraNear = cameraData.hasCameraFrameProperties
                ? cameraData.nearClipPlane
                : camera.nearClipPlane;
            float cameraFar = cameraData.hasCameraFrameProperties
                ? cameraData.farClipPlane
                : camera.farClipPlane;
            cameraNear = Mathf.Max(cameraNear, MinCascadeRange);
            float shadowFar = Mathf.Min(cameraFar, shadowDistance);
            if (!float.IsFinite(cameraNear)
                || !float.IsFinite(shadowFar)
                || shadowFar <= cameraNear)
            {
                return false;
            }

            float split0 = SanitizeSplitRatio(splitRatios.x, 0.0f);
            float split1 = SanitizeSplitRatio(splitRatios.y, split0);
            float split2 = SanitizeSplitRatio(splitRatios.z, split1);
            float previousRatio = cascadeIndex switch
            {
                0 => 0.0f,
                1 => split0,
                2 => split1,
                _ => split2,
            };
            float currentRatio = cascadeIndex >= cascadeCount - 1
                ? 1.0f
                : cascadeIndex switch
                {
                    0 => split0,
                    1 => split1,
                    _ => split2,
                };
            float shadowRange = shadowFar - cameraNear;
            cascadeNearDistance = cameraNear + previousRatio * shadowRange;
            cascadeFarDistance = cameraNear + currentRatio * shadowRange;
            return cascadeFarDistance - cascadeNearDistance >= MinCascadeRange;
        }

        internal static bool IsCascadeDataUsable(
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            in ShadowSplitData cascadeSplitData)
        {
            Vector4 sphere = cascadeSplitData.cullingSphere;
            float viewDeterminant = viewMatrix.determinant;
            float projectionDeterminant = projectionMatrix.determinant;
            float cullingDeterminant = cascadeSplitData.cullingMatrix.determinant;
            return IsFinite(viewMatrix)
                && IsFinite(projectionMatrix)
                && IsFinite(cascadeSplitData.cullingMatrix)
                && float.IsFinite(viewDeterminant)
                && float.IsFinite(projectionDeterminant)
                && float.IsFinite(cullingDeterminant)
                && viewDeterminant != 0.0f
                && projectionDeterminant != 0.0f
                && cullingDeterminant != 0.0f
                && IsFinite(new Vector3(sphere.x, sphere.y, sphere.z))
                && float.IsFinite(sphere.w)
                && sphere.w > 0.0f;
        }

        private bool TryBuildFrustumSliceCorners(
            VividCameraData cameraData,
            float nearDistance,
            float farDistance)
        {
            Matrix4x4 projectionMatrix = cameraData.nonJitteredProjectionMatrix;
            Matrix4x4 inverseProjectionMatrix = projectionMatrix.inverse;
            Matrix4x4 cameraToWorldMatrix = cameraData.inverseViewMatrix;
            if (!IsFinite(projectionMatrix)
                || !IsFinite(inverseProjectionMatrix)
                || !IsFinite(cameraToWorldMatrix))
            {
                return false;
            }

            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                float clipX = cornerIndex == 0 || cornerIndex == 3 ? -1.0f : 1.0f;
                float clipY = cornerIndex < 2 ? -1.0f : 1.0f;
                if (!TryGetViewSpaceFrustumCorner(
                        inverseProjectionMatrix,
                        clipX,
                        clipY,
                        nearDistance,
                        out Vector3 nearCorner)
                    || !TryGetViewSpaceFrustumCorner(
                        inverseProjectionMatrix,
                        clipX,
                        clipY,
                        farDistance,
                        out Vector3 farCorner))
                {
                    return false;
                }

                m_FrustumCorners[cornerIndex] =
                    cameraToWorldMatrix.MultiplyPoint3x4(nearCorner);
                m_FrustumCorners[cornerIndex + 4] =
                    cameraToWorldMatrix.MultiplyPoint3x4(farCorner);
                if (!IsFinite(m_FrustumCorners[cornerIndex])
                    || !IsFinite(m_FrustumCorners[cornerIndex + 4]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetViewSpaceFrustumCorner(
            Matrix4x4 inverseProjectionMatrix,
            float clipX,
            float clipY,
            float viewDistance,
            out Vector3 viewSpaceCorner)
        {
            viewSpaceCorner = default;
            Vector4 nearCorner = inverseProjectionMatrix
                * new Vector4(clipX, clipY, -1.0f, 1.0f);
            Vector4 farCorner = inverseProjectionMatrix
                * new Vector4(clipX, clipY, 1.0f, 1.0f);
            if (!float.IsFinite(nearCorner.w)
                || !float.IsFinite(farCorner.w)
                || Mathf.Abs(nearCorner.w) <= 1e-6f
                || Mathf.Abs(farCorner.w) <= 1e-6f)
            {
                return false;
            }

            Vector3 nearPoint = new Vector3(
                nearCorner.x / nearCorner.w,
                nearCorner.y / nearCorner.w,
                nearCorner.z / nearCorner.w);
            Vector3 farPoint = new Vector3(
                farCorner.x / farCorner.w,
                farCorner.y / farCorner.w,
                farCorner.z / farCorner.w);
            float depthRange = farPoint.z - nearPoint.z;
            if (!IsFinite(nearPoint)
                || !IsFinite(farPoint)
                || Mathf.Abs(depthRange) <= 1e-6f)
            {
                return false;
            }

            float interpolation = (-viewDistance - nearPoint.z) / depthRange;
            viewSpaceCorner = Vector3.LerpUnclamped(nearPoint, farPoint, interpolation);
            return IsFinite(viewSpaceCorner);
        }

        private void SetCullingPlanes(
            ref ShadowSplitData cascadeSplitData,
            Matrix4x4 viewProjectionMatrix)
        {
            cascadeSplitData.cullingMatrix = viewProjectionMatrix;
            GeometryUtility.CalculateFrustumPlanes(
                viewProjectionMatrix,
                m_CullingPlanes);
            cascadeSplitData.cullingPlaneCount = FrustumPlaneCount;
            for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
                cascadeSplitData.SetCullingPlane(planeIndex, m_CullingPlanes[planeIndex]);
        }

        private static float SanitizeSplitRatio(float ratio, float minimum)
        {
            return float.IsFinite(ratio)
                ? Mathf.Clamp(ratio, minimum, 1.0f)
                : minimum;
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            float magnitudeSquared = value.sqrMagnitude;
            if (!IsFinite(value)
                || !float.IsFinite(magnitudeSquared)
                || magnitudeSquared <= 1e-12f)
            {
                return false;
            }

            normalized = value / Mathf.Sqrt(magnitudeSquared);
            return true;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int elementIndex = 0; elementIndex < 16; elementIndex++)
            {
                if (!float.IsFinite(matrix[elementIndex]))
                    return false;
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }

        private static bool IsBoundsUsable(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return IsFinite(bounds.min)
                && IsFinite(bounds.max)
                && IsFinite(size)
                && size.x >= 0.0f
                && size.y >= 0.0f
                && size.z >= 0.0f;
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
