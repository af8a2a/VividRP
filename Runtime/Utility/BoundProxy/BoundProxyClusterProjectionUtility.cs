using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    public static class BoundProxyClusterProjectionUtility
    {
        private static readonly int[] s_BoxEdgeCornerIndices =
        {
            0, 1,
            0, 2,
            0, 4,
            1, 3,
            1, 5,
            2, 3,
            2, 6,
            3, 7,
            4, 5,
            4, 6,
            5, 7,
            6, 7
        };

        internal readonly struct JobParameters
        {
            public readonly float4x4 worldToViewMatrix;
            public readonly int screenWidth;
            public readonly int screenHeight;
            public readonly int tileSize;
            public readonly int tileCountX;
            public readonly int tileCountY;
            public readonly int bigTileSize;
            public readonly int bigTileCountX;
            public readonly int bigTileCountY;
            public readonly int sliceCount;
            public readonly float nearClip;
            public readonly float farClip;
            public readonly float logDepthScale;
            public readonly float linearDepthScale;
            public readonly float tanHalfFovX;
            public readonly float tanHalfFovY;
            public readonly float orthoHalfWidth;
            public readonly float orthoHalfHeight;
            public readonly int isOrthographic;

            public JobParameters(in BoundProxyClusterProjectionParameters parameters)
            {
                worldToViewMatrix = ToFloat4x4(parameters.worldToViewMatrix);
                screenWidth = parameters.screenWidth;
                screenHeight = parameters.screenHeight;
                tileSize = parameters.tileSize;
                tileCountX = parameters.tileCountX;
                tileCountY = parameters.tileCountY;
                bigTileSize = parameters.bigTileSize;
                bigTileCountX = parameters.bigTileCountX;
                bigTileCountY = parameters.bigTileCountY;
                sliceCount = parameters.sliceCount;
                nearClip = parameters.nearClip;
                farClip = parameters.farClip;
                logDepthScale = parameters.logDepthScale;
                linearDepthScale = parameters.linearDepthScale;
                tanHalfFovX = parameters.tanHalfFovX;
                tanHalfFovY = parameters.tanHalfFovY;
                orthoHalfWidth = parameters.orthoHalfWidth;
                orthoHalfHeight = parameters.orthoHalfHeight;
                isOrthographic = parameters.isOrthographic;
            }
        }

        public static BoundProxyClusterProjectionParameters CreateParameters(
            Camera camera,
            int screenWidth,
            int screenHeight,
            int tileSize,
            int sliceCount,
            int bigTileSize = 0)
        {
            screenWidth = Mathf.Max(screenWidth, 1);
            screenHeight = Mathf.Max(screenHeight, 1);
            tileSize = Mathf.Max(tileSize, 1);
            sliceCount = Mathf.Max(sliceCount, 1);

            float nearClip = camera != null ? camera.nearClipPlane : 0.1f;
            float farClip = camera != null ? camera.farClipPlane : 1000.0f;
            float aspect = screenHeight > 0 ? screenWidth / (float)screenHeight : 1.0f;
            nearClip = Mathf.Max(nearClip, 0.01f);
            farClip = Mathf.Max(farClip, nearClip + 0.01f);
            float logDepthScale = sliceCount / Mathf.Max(Mathf.Log(farClip / nearClip, 2.0f), 0.0001f);
            float linearDepthScale = sliceCount / Mathf.Max(farClip - nearClip, 0.0001f);
            int isOrthographic = camera != null && camera.orthographic ? 1 : 0;
            float tanHalfFovX;
            float tanHalfFovY;
            float orthoHalfWidth;
            float orthoHalfHeight;

            if (isOrthographic != 0)
            {
                orthoHalfHeight = Mathf.Max(camera != null ? camera.orthographicSize : 5.0f, 0.01f);
                orthoHalfWidth = orthoHalfHeight * aspect;
                tanHalfFovX = 0.0f;
                tanHalfFovY = 0.0f;
            }
            else
            {
                float halfVerticalFov = Mathf.Deg2Rad * (camera != null ? camera.fieldOfView : 60.0f) * 0.5f;
                tanHalfFovY = Mathf.Max(Mathf.Tan(halfVerticalFov), 0.0001f);
                tanHalfFovX = tanHalfFovY * aspect;
                orthoHalfWidth = 0.0f;
                orthoHalfHeight = 0.0f;
            }

            int tileCountX = Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)tileSize));
            int tileCountY = Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)tileSize));
            bigTileSize = Mathf.Max(bigTileSize > 0 ? bigTileSize : tileSize, tileSize);
            int bigTileCountX = Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)bigTileSize));
            int bigTileCountY = Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)bigTileSize));

            return new BoundProxyClusterProjectionParameters(
                camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity,
                screenWidth,
                screenHeight,
                tileSize,
                tileCountX,
                tileCountY,
                bigTileSize,
                bigTileCountX,
                bigTileCountY,
                sliceCount,
                nearClip,
                farClip,
                logDepthScale,
                linearDepthScale,
                tanHalfFovX,
                tanHalfFovY,
                orthoHalfWidth,
                orthoHalfHeight,
                isOrthographic);
        }

        public static ClusteredProxyScreenBounds CreateScreenBounds(
            in BoundProxyWorldData worldData,
            in BoundProxyClusterProjectionParameters parameters)
        {
            return CreateScreenBounds(worldData, new JobParameters(parameters));
        }

        internal static ClusteredProxyScreenBounds CreateScreenBounds(
            in BoundProxyWorldData worldData,
            in JobParameters parameters)
        {
            if (worldData.shape == BoundProxyShapeType.Sphere)
            {
                return CreateSphereScreenBounds(worldData.worldCenter, worldData.sphereRadius, parameters);
            }

            CalculateBoxViewSpaceAabb(worldData, parameters.worldToViewMatrix, out float3 viewSpaceMin, out float3 viewSpaceMax);
            return CreateScreenBoundsFromViewSpaceAabb(viewSpaceMin, viewSpaceMax, parameters);
        }

        internal static ClusteredProxyScreenBounds CreateSphereScreenBounds(
            Vector3 worldCenter,
            float radius,
            in JobParameters parameters)
        {
            float sanitizedRadius = math.max(radius, 0.0f);
            float3 centerVS = TransformWorldToPositiveViewSpace(parameters.worldToViewMatrix, worldCenter);
            float3 radiusVector = new float3(sanitizedRadius, sanitizedRadius, sanitizedRadius);
            return CreateScreenBoundsFromViewSpaceAabb(centerVS - radiusVector, centerVS + radiusVector, parameters);
        }

        internal static ClusteredProxyScreenBounds CreateScreenBoundsFromViewSpaceAabb(
            float3 viewSpaceAabbMin,
            float3 viewSpaceAabbMax,
            in JobParameters parameters)
        {
            ClusteredProxyScreenBounds bounds = default;
            bounds.viewSpaceAabbMin = new Vector3(viewSpaceAabbMin.x, viewSpaceAabbMin.y, viewSpaceAabbMin.z);
            bounds.viewSpaceAabbMax = new Vector3(viewSpaceAabbMax.x, viewSpaceAabbMax.y, viewSpaceAabbMax.z);

            if (!TryGetSliceRange(viewSpaceAabbMin.z, viewSpaceAabbMax.z, parameters, out int sliceMin, out int sliceMax))
            {
                return bounds;
            }

            if (!TryGetClipSpaceRect(viewSpaceAabbMin, viewSpaceAabbMax, parameters, out float2 clipSpaceAabbMin, out float2 clipSpaceAabbMax))
            {
                return bounds;
            }

            if (!TryConvertClipRectToTileRange(
                    clipSpaceAabbMin,
                    clipSpaceAabbMax,
                    parameters,
                    out int tileMinX,
                    out int tileMaxX,
                    out int tileMinY,
                    out int tileMaxY))
            {
                return bounds;
            }

            ExpandRange(ref sliceMin, ref sliceMax, 1, parameters.sliceCount);
            ExpandRange(ref tileMinX, ref tileMaxX, 1, parameters.tileCountX);
            ExpandRange(ref tileMinY, ref tileMaxY, 1, parameters.tileCountY);

            if (!TryConvertTileRangeToBigTileRange(
                    tileMinX,
                    tileMaxX,
                    tileMinY,
                    tileMaxY,
                    parameters,
                    out int bigTileMinX,
                    out int bigTileMaxX,
                    out int bigTileMinY,
                    out int bigTileMaxY))
            {
                return bounds;
            }

            bounds.clipSpaceAabbMin = new Vector2(clipSpaceAabbMin.x, clipSpaceAabbMin.y);
            bounds.clipSpaceAabbMax = new Vector2(clipSpaceAabbMax.x, clipSpaceAabbMax.y);
            bounds.sliceMin = sliceMin;
            bounds.sliceMax = sliceMax;
            bounds.tileMinX = tileMinX;
            bounds.tileMaxX = tileMaxX;
            bounds.tileMinY = tileMinY;
            bounds.tileMaxY = tileMaxY;
            bounds.bigTileMinX = bigTileMinX;
            bounds.bigTileMaxX = bigTileMaxX;
            bounds.bigTileMinY = bigTileMinY;
            bounds.bigTileMaxY = bigTileMaxY;
            bounds.isValid = 1u;
            return bounds;
        }

        internal static ClusteredProxyScreenBounds CreateScreenBoundsFromViewSpaceCorners(
            float3[] viewSpaceCorners,
            int cornerCount,
            in JobParameters parameters)
        {
            ClusteredProxyScreenBounds bounds = default;

            if (viewSpaceCorners == null || cornerCount <= 0)
                return bounds;

            cornerCount = math.min(cornerCount, viewSpaceCorners.Length);

            float3 viewSpaceMin = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            float3 viewSpaceMax = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
            {
                float3 corner = viewSpaceCorners[cornerIndex];
                viewSpaceMin = math.min(viewSpaceMin, corner);
                viewSpaceMax = math.max(viewSpaceMax, corner);
            }

            bounds.viewSpaceAabbMin = new Vector3(viewSpaceMin.x, viewSpaceMin.y, viewSpaceMin.z);
            bounds.viewSpaceAabbMax = new Vector3(viewSpaceMax.x, viewSpaceMax.y, viewSpaceMax.z);

            if (!TryGetSliceRange(viewSpaceMin.z, viewSpaceMax.z, parameters, out int sliceMin, out int sliceMax))
                return bounds;

            if (!TryGetClipSpaceRectFromViewSpaceCorners(
                    viewSpaceCorners,
                    cornerCount,
                    parameters,
                    out float2 clipSpaceAabbMin,
                    out float2 clipSpaceAabbMax))
            {
                return bounds;
            }

            if (!TryConvertClipRectToTileRange(
                    clipSpaceAabbMin,
                    clipSpaceAabbMax,
                    parameters,
                    out int tileMinX,
                    out int tileMaxX,
                    out int tileMinY,
                    out int tileMaxY))
            {
                return bounds;
            }

            ExpandRange(ref sliceMin, ref sliceMax, 1, parameters.sliceCount);
            ExpandRange(ref tileMinX, ref tileMaxX, 1, parameters.tileCountX);
            ExpandRange(ref tileMinY, ref tileMaxY, 1, parameters.tileCountY);

            if (!TryConvertTileRangeToBigTileRange(
                    tileMinX,
                    tileMaxX,
                    tileMinY,
                    tileMaxY,
                    parameters,
                    out int bigTileMinX,
                    out int bigTileMaxX,
                    out int bigTileMinY,
                    out int bigTileMaxY))
            {
                return bounds;
            }

            bounds.clipSpaceAabbMin = new Vector2(clipSpaceAabbMin.x, clipSpaceAabbMin.y);
            bounds.clipSpaceAabbMax = new Vector2(clipSpaceAabbMax.x, clipSpaceAabbMax.y);
            bounds.sliceMin = sliceMin;
            bounds.sliceMax = sliceMax;
            bounds.tileMinX = tileMinX;
            bounds.tileMaxX = tileMaxX;
            bounds.tileMinY = tileMinY;
            bounds.tileMaxY = tileMaxY;
            bounds.bigTileMinX = bigTileMinX;
            bounds.bigTileMaxX = bigTileMaxX;
            bounds.bigTileMinY = bigTileMinY;
            bounds.bigTileMaxY = bigTileMaxY;
            bounds.isValid = 1u;
            return bounds;
        }

        internal static float3 TransformWorldToPositiveViewSpace(float4x4 worldToViewMatrix, Vector3 worldPosition)
        {
            float4 viewPosition = math.mul(
                worldToViewMatrix,
                new float4(worldPosition.x, worldPosition.y, worldPosition.z, 1.0f));
            return new float3(viewPosition.x, viewPosition.y, -viewPosition.z);
        }

        private static void CalculateBoxViewSpaceAabb(
            in BoundProxyWorldData worldData,
            float4x4 worldToViewMatrix,
            out float3 viewSpaceMin,
            out float3 viewSpaceMax)
        {
            Vector3 halfExtents = worldData.boxSize * 0.5f;
            Vector3 axisX = worldData.worldRotation * Vector3.right;
            Vector3 axisY = worldData.worldRotation * Vector3.up;
            Vector3 axisZ = worldData.worldRotation * Vector3.forward;

            viewSpaceMin = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            viewSpaceMax = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 cornerOffset =
                    axisX * (((cornerIndex & 1) == 0 ? -1.0f : 1.0f) * halfExtents.x)
                    + axisY * (((cornerIndex & 2) == 0 ? -1.0f : 1.0f) * halfExtents.y)
                    + axisZ * (((cornerIndex & 4) == 0 ? -1.0f : 1.0f) * halfExtents.z);
                float3 cornerVS = TransformWorldToPositiveViewSpace(worldToViewMatrix, worldData.worldCenter + cornerOffset);
                viewSpaceMin = math.min(viewSpaceMin, cornerVS);
                viewSpaceMax = math.max(viewSpaceMax, cornerVS);
            }
        }

        private static bool TryGetSliceRange(
            float depthMin,
            float depthMax,
            in JobParameters parameters,
            out int sliceMin,
            out int sliceMax)
        {
            sliceMin = 0;
            sliceMax = 0;

            if (depthMax < parameters.nearClip || depthMin > parameters.farClip)
            {
                return false;
            }

            depthMin = math.max(depthMin, parameters.nearClip);
            depthMax = math.min(depthMax, parameters.farClip);
            sliceMin = GetSliceIndex(depthMin, parameters);
            sliceMax = GetSliceIndex(depthMax, parameters);
            return sliceMax >= sliceMin;
        }

        private static bool TryGetClipSpaceRect(
            float3 viewSpaceAabbMin,
            float3 viewSpaceAabbMax,
            in JobParameters parameters,
            out float2 clipSpaceAabbMin,
            out float2 clipSpaceAabbMax)
        {
            clipSpaceAabbMin = default;
            clipSpaceAabbMax = default;

            if (parameters.isOrthographic != 0)
            {
                float orthoHalfWidth = math.max(parameters.orthoHalfWidth, 1e-6f);
                float orthoHalfHeight = math.max(parameters.orthoHalfHeight, 1e-6f);
                clipSpaceAabbMin = new float2(
                    viewSpaceAabbMin.x / orthoHalfWidth,
                    viewSpaceAabbMin.y / orthoHalfHeight);
                clipSpaceAabbMax = new float2(
                    viewSpaceAabbMax.x / orthoHalfWidth,
                    viewSpaceAabbMax.y / orthoHalfHeight);
                return true;
            }

            float clippedNearZ = math.max(viewSpaceAabbMin.z, parameters.nearClip);
            float clippedFarZ = math.min(viewSpaceAabbMax.z, parameters.farClip);
            if (clippedFarZ < parameters.nearClip || clippedNearZ > clippedFarZ)
            {
                return false;
            }

            float3 clippedAabbMin = new float3(viewSpaceAabbMin.x, viewSpaceAabbMin.y, clippedNearZ);
            float3 clippedAabbMax = new float3(viewSpaceAabbMax.x, viewSpaceAabbMax.y, clippedFarZ);
            float2 projectedMin = new float2(float.PositiveInfinity, float.PositiveInfinity);
            float2 projectedMax = new float2(float.NegativeInfinity, float.NegativeInfinity);

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                float3 corner = new float3(
                    (cornerIndex & 1) == 0 ? clippedAabbMin.x : clippedAabbMax.x,
                    (cornerIndex & 2) == 0 ? clippedAabbMin.y : clippedAabbMax.y,
                    (cornerIndex & 4) == 0 ? clippedAabbMin.z : clippedAabbMax.z);
                float2 clipSpacePoint = ProjectViewSpacePointToClipSpace(corner, parameters);
                projectedMin = math.min(projectedMin, clipSpacePoint);
                projectedMax = math.max(projectedMax, clipSpacePoint);
            }

            if (!math.all(math.isfinite(projectedMin)) || !math.all(math.isfinite(projectedMax)))
            {
                return false;
            }

            clipSpaceAabbMin = projectedMin - 1e-4f;
            clipSpaceAabbMax = projectedMax + 1e-4f;
            return true;
        }

        private static float2 ProjectViewSpacePointToClipSpace(float3 viewSpacePoint, in JobParameters parameters)
        {
            if (parameters.isOrthographic != 0)
            {
                float orthoHalfWidth = math.max(parameters.orthoHalfWidth, 1e-6f);
                float orthoHalfHeight = math.max(parameters.orthoHalfHeight, 1e-6f);
                return new float2(
                    viewSpacePoint.x / orthoHalfWidth,
                    viewSpacePoint.y / orthoHalfHeight);
            }

            float projectedHalfWidth = math.max(viewSpacePoint.z * parameters.tanHalfFovX, 1e-6f);
            float projectedHalfHeight = math.max(viewSpacePoint.z * parameters.tanHalfFovY, 1e-6f);
            return new float2(
                viewSpacePoint.x / projectedHalfWidth,
                viewSpacePoint.y / projectedHalfHeight);
        }

        private static bool TryGetClipSpaceRectFromViewSpaceCorners(
            float3[] viewSpaceCorners,
            int cornerCount,
            in JobParameters parameters,
            out float2 clipSpaceAabbMin,
            out float2 clipSpaceAabbMax)
        {
            clipSpaceAabbMin = default;
            clipSpaceAabbMax = default;

            if (viewSpaceCorners == null || cornerCount <= 0)
                return false;

            float2 projectedMin = new float2(float.PositiveInfinity, float.PositiveInfinity);
            float2 projectedMax = new float2(float.NegativeInfinity, float.NegativeInfinity);
            bool hasProjectedPoint = false;

            for (int cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
            {
                float3 corner = viewSpaceCorners[cornerIndex];
                if (corner.z < parameters.nearClip || corner.z > parameters.farClip)
                    continue;

                AccumulateProjectedPoint(corner, parameters, ref projectedMin, ref projectedMax, ref hasProjectedPoint);
            }

            int edgeCount = math.min(s_BoxEdgeCornerIndices.Length / 2, 12);
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                int cornerIndexA = s_BoxEdgeCornerIndices[edgeIndex * 2];
                int cornerIndexB = s_BoxEdgeCornerIndices[edgeIndex * 2 + 1];
                if (cornerIndexA >= cornerCount || cornerIndexB >= cornerCount)
                    continue;

                float3 cornerA = viewSpaceCorners[cornerIndexA];
                float3 cornerB = viewSpaceCorners[cornerIndexB];
                AccumulateClippedEdgeIntersections(
                    cornerA,
                    cornerB,
                    parameters,
                    ref projectedMin,
                    ref projectedMax,
                    ref hasProjectedPoint);
            }

            if (!hasProjectedPoint
                || !math.all(math.isfinite(projectedMin))
                || !math.all(math.isfinite(projectedMax)))
            {
                return false;
            }

            clipSpaceAabbMin = projectedMin - 1e-4f;
            clipSpaceAabbMax = projectedMax + 1e-4f;
            return true;
        }

        private static int GetSliceIndex(float depth, in JobParameters parameters)
        {
            depth = math.clamp(depth, parameters.nearClip, parameters.farClip);

            if (parameters.isOrthographic != 0)
            {
                int linearSlice = (int)math.floor((depth - parameters.nearClip) * parameters.linearDepthScale);
                return math.clamp(linearSlice, 0, parameters.sliceCount - 1);
            }

            float logarithmicDepth = math.log2(math.max(depth / math.max(parameters.nearClip, 1e-6f), 1.0f));
            int logarithmicSlice = (int)math.floor(logarithmicDepth * parameters.logDepthScale);
            return math.clamp(logarithmicSlice, 0, parameters.sliceCount - 1);
        }

        private static void AccumulateClippedEdgeIntersections(
            float3 cornerA,
            float3 cornerB,
            in JobParameters parameters,
            ref float2 projectedMin,
            ref float2 projectedMax,
            ref bool hasProjectedPoint)
        {
            AccumulateEdgePlaneIntersection(
                cornerA,
                cornerB,
                parameters.nearClip,
                parameters,
                ref projectedMin,
                ref projectedMax,
                ref hasProjectedPoint);
            AccumulateEdgePlaneIntersection(
                cornerA,
                cornerB,
                parameters.farClip,
                parameters,
                ref projectedMin,
                ref projectedMax,
                ref hasProjectedPoint);
        }

        private static void AccumulateEdgePlaneIntersection(
            float3 cornerA,
            float3 cornerB,
            float planeDepth,
            in JobParameters parameters,
            ref float2 projectedMin,
            ref float2 projectedMax,
            ref bool hasProjectedPoint)
        {
            float depthA = cornerA.z - planeDepth;
            float depthB = cornerB.z - planeDepth;

            if ((depthA < 0.0f && depthB < 0.0f) || (depthA > 0.0f && depthB > 0.0f))
                return;

            float denominator = cornerB.z - cornerA.z;
            if (math.abs(denominator) <= 1e-6f)
                return;

            float t = (planeDepth - cornerA.z) / denominator;
            if (t < 0.0f || t > 1.0f)
                return;

            float3 intersection = math.lerp(cornerA, cornerB, t);
            intersection.z = planeDepth;
            AccumulateProjectedPoint(intersection, parameters, ref projectedMin, ref projectedMax, ref hasProjectedPoint);
        }

        private static void AccumulateProjectedPoint(
            float3 viewSpacePoint,
            in JobParameters parameters,
            ref float2 projectedMin,
            ref float2 projectedMax,
            ref bool hasProjectedPoint)
        {
            float2 clipSpacePoint = ProjectViewSpacePointToClipSpace(viewSpacePoint, parameters);
            projectedMin = math.min(projectedMin, clipSpacePoint);
            projectedMax = math.max(projectedMax, clipSpacePoint);
            hasProjectedPoint = true;
        }

        private static bool TryConvertClipRectToTileRange(
            float2 clipSpaceAabbMin,
            float2 clipSpaceAabbMax,
            in JobParameters parameters,
            out int tileMinX,
            out int tileMaxX,
            out int tileMinY,
            out int tileMaxY)
        {
            return TryConvertClipRectToCellRange(
                clipSpaceAabbMin,
                clipSpaceAabbMax,
                parameters.screenWidth,
                parameters.screenHeight,
                parameters.tileSize,
                parameters.tileCountX,
                parameters.tileCountY,
                out tileMinX,
                out tileMaxX,
                out tileMinY,
                out tileMaxY);
        }

        private static bool TryConvertTileRangeToBigTileRange(
            int tileMinX,
            int tileMaxX,
            int tileMinY,
            int tileMaxY,
            in JobParameters parameters,
            out int bigTileMinX,
            out int bigTileMaxX,
            out int bigTileMinY,
            out int bigTileMaxY)
        {
            bigTileMinX = 0;
            bigTileMaxX = 0;
            bigTileMinY = 0;
            bigTileMaxY = 0;

            if (tileMinX > tileMaxX || tileMinY > tileMaxY)
            {
                return false;
            }

            int minPixelX = tileMinX * parameters.tileSize;
            int maxPixelX = math.max((tileMaxX + 1) * parameters.tileSize - 1, minPixelX);
            int minPixelY = tileMinY * parameters.tileSize;
            int maxPixelY = math.max((tileMaxY + 1) * parameters.tileSize - 1, minPixelY);

            bigTileMinX = math.clamp(minPixelX / parameters.bigTileSize, 0, parameters.bigTileCountX - 1);
            bigTileMaxX = math.clamp(maxPixelX / parameters.bigTileSize, 0, parameters.bigTileCountX - 1);
            bigTileMinY = math.clamp(minPixelY / parameters.bigTileSize, 0, parameters.bigTileCountY - 1);
            bigTileMaxY = math.clamp(maxPixelY / parameters.bigTileSize, 0, parameters.bigTileCountY - 1);
            return true;
        }

        private static bool TryConvertClipRectToCellRange(
            float2 clipSpaceAabbMin,
            float2 clipSpaceAabbMax,
            int screenWidth,
            int screenHeight,
            int cellSize,
            int cellCountX,
            int cellCountY,
            out int cellMinX,
            out int cellMaxX,
            out int cellMinY,
            out int cellMaxY)
        {
            cellMinX = 0;
            cellMaxX = 0;
            cellMinY = 0;
            cellMaxY = 0;

            float screenMinX = GetScreenXFromClipSpace(clipSpaceAabbMin.x, screenWidth);
            float screenMaxX = GetScreenXFromClipSpace(clipSpaceAabbMax.x, screenWidth);
            float screenMinY = GetScreenYFromClipSpace(clipSpaceAabbMax.y, screenHeight);
            float screenMaxY = GetScreenYFromClipSpace(clipSpaceAabbMin.y, screenHeight);
            float rectMinX = math.min(screenMinX, screenMaxX);
            float rectMaxX = math.max(screenMinX, screenMaxX);
            float rectMinY = math.min(screenMinY, screenMaxY);
            float rectMaxY = math.max(screenMinY, screenMaxY);
            float maxPixelX = (float)math.max(screenWidth - 1, 0);
            float maxPixelY = (float)math.max(screenHeight - 1, 0);

            if (rectMaxX < 0.0f
                || rectMinX > maxPixelX
                || rectMaxY < 0.0f
                || rectMinY > maxPixelY)
            {
                return false;
            }

            float clampedMinX = math.clamp(rectMinX, 0.0f, maxPixelX);
            float clampedMaxX = math.clamp(rectMaxX, 0.0f, maxPixelX);
            float clampedMinY = math.clamp(rectMinY, 0.0f, maxPixelY);
            float clampedMaxY = math.clamp(rectMaxY, 0.0f, maxPixelY);
            cellMinX = math.clamp((int)math.floor(clampedMinX / cellSize), 0, cellCountX - 1);
            cellMaxX = math.clamp((int)math.floor(clampedMaxX / cellSize), 0, cellCountX - 1);
            cellMinY = math.clamp((int)math.floor(clampedMinY / cellSize), 0, cellCountY - 1);
            cellMaxY = math.clamp((int)math.floor(clampedMaxY / cellSize), 0, cellCountY - 1);
            return true;
        }

        private static void ExpandRange(ref int minValue, ref int maxValue, int padding, int rangeCount)
        {
            if (rangeCount <= 0)
            {
                minValue = 0;
                maxValue = 0;
                return;
            }

            minValue = math.clamp(minValue - padding, 0, rangeCount - 1);
            maxValue = math.clamp(maxValue + padding, 0, rangeCount - 1);
        }

        private static float GetScreenXFromClipSpace(float clipSpaceX, int screenWidth)
        {
            return (clipSpaceX * 0.5f + 0.5f) * screenWidth;
        }

        private static float GetScreenYFromClipSpace(float clipSpaceY, int screenHeight)
        {
            return (1.0f - clipSpaceY) * 0.5f * screenHeight;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 source)
        {
            return new float4x4(
                new float4(source.m00, source.m10, source.m20, source.m30),
                new float4(source.m01, source.m11, source.m21, source.m31),
                new float4(source.m02, source.m12, source.m22, source.m32),
                new float4(source.m03, source.m13, source.m23, source.m33));
        }
    }
}
