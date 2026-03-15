using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividLightData : ContextItem
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DirectionalLightData
        {
            public Vector3 directionWS;
            public float shadowStrength;
            public Vector3 color;
            public uint renderingLayerMask;

            internal static int Stride => Marshal.SizeOf<DirectionalLightData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightData
        {
            public Vector3 positionWS;
            public float range;
            public Vector3 color;
            public uint lightType;
            public Vector3 directionWS;
            public float angleScale;
            public float angleOffset;
            public float inverseRangeSquared;
            public float shadowStrength;
            public uint renderingLayerMask;

            internal static int Stride => Marshal.SizeOf<PunctualLightData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightCullData
        {
            public Vector3 positionWS;
            public float range;
            public Vector3 directionWS;
            public uint lightType;
            public float cosOuterAngle;
            public float radiusAtRange;
            public Vector3 cullingCenterWS;
            public float cullingRadius;

            internal static int Stride => Marshal.SizeOf<PunctualLightCullData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightScreenSpaceBounds
        {
            public Vector3 viewSpaceAabbMin;
            public Vector3 viewSpaceAabbMax;
            public Vector2 clipSpaceAabbMin;
            public Vector2 clipSpaceAabbMax;
            public int sliceMin;
            public int sliceMax;
            public int tileMinX;
            public int tileMaxX;
            public int tileMinY;
            public int tileMaxY;
            public uint isValid;

            internal static int Stride => Marshal.SizeOf<PunctualLightScreenSpaceBounds>();
        }

        internal readonly struct PunctualLightScreenSpaceBoundsParameters
        {
            public readonly Matrix4x4 worldToViewMatrix;
            public readonly int screenWidth;
            public readonly int screenHeight;
            public readonly int tileSize;
            public readonly int tileCountX;
            public readonly int tileCountY;
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

            public PunctualLightScreenSpaceBoundsParameters(
                Matrix4x4 worldToViewMatrix,
                int screenWidth,
                int screenHeight,
                int tileSize,
                int tileCountX,
                int tileCountY,
                int sliceCount,
                float nearClip,
                float farClip,
                float logDepthScale,
                float linearDepthScale,
                float tanHalfFovX,
                float tanHalfFovY,
                float orthoHalfWidth,
                float orthoHalfHeight,
                int isOrthographic)
            {
                this.worldToViewMatrix = worldToViewMatrix;
                this.screenWidth = screenWidth;
                this.screenHeight = screenHeight;
                this.tileSize = tileSize;
                this.tileCountX = tileCountX;
                this.tileCountY = tileCountY;
                this.sliceCount = sliceCount;
                this.nearClip = nearClip;
                this.farClip = farClip;
                this.logDepthScale = logDepthScale;
                this.linearDepthScale = linearDepthScale;
                this.tanHalfFovX = tanHalfFovX;
                this.tanHalfFovY = tanHalfFovY;
                this.orthoHalfWidth = orthoHalfWidth;
                this.orthoHalfHeight = orthoHalfHeight;
                this.isOrthographic = isOrthographic;
            }
        }

        internal readonly struct VisibleLightDescriptor
        {
            public VisibleLightDescriptor(EntityId lightEntityId, LightType lightType, Color finalColor)
            {
                this.lightEntityId = lightEntityId;
                this.lightType = lightType;
                this.finalColor = finalColor;
            }

            public EntityId lightEntityId { get; }

            public LightType lightType { get; }

            public Color finalColor { get; }
        }

        [Flags]
        private enum VisibleLightCollectionMask
        {
            None = 0,
            Directional = 1 << 0,
            Punctual = 1 << 1,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VisibleLightRenderDataRecord
        {
            public int visibleLightIndex;
            public VividLightRenderData lightRenderData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DirectionalLightCandidate
        {
            public int visibleLightIndex;
            public EntityId lightEntityId;
            public DirectionalLightData lightData;
            public float intensity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PunctualLightCandidate
        {
            public PunctualLightData lightData;
            public PunctualLightCullData lightCullData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PunctualLightScreenSpaceBoundsRecord
        {
            public float3 viewSpaceAabbMin;
            public float3 viewSpaceAabbMax;
            public float2 clipSpaceAabbMin;
            public float2 clipSpaceAabbMax;
            public int sliceMin;
            public int sliceMax;
            public int tileMinX;
            public int tileMaxX;
            public int tileMinY;
            public int tileMaxY;
            public uint isValid;
        }

        private readonly struct PunctualLightScreenSpaceBoundsJobParameters
        {
            public readonly float4x4 worldToViewMatrix;
            public readonly int screenWidth;
            public readonly int screenHeight;
            public readonly int tileSize;
            public readonly int tileCountX;
            public readonly int tileCountY;
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

            public PunctualLightScreenSpaceBoundsJobParameters(in PunctualLightScreenSpaceBoundsParameters parameters)
            {
                worldToViewMatrix = ToFloat4x4(parameters.worldToViewMatrix);
                screenWidth = parameters.screenWidth;
                screenHeight = parameters.screenHeight;
                tileSize = parameters.tileSize;
                tileCountX = parameters.tileCountX;
                tileCountY = parameters.tileCountY;
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

        [BurstCompile]
        private struct BuildVisibleLightCandidatesJob : IJob
        {
            [ReadOnly]
            public NativeArray<VisibleLightRenderDataRecord> visibleLightRenderDataRecords;

            public bool collectDirectionalLights;
            public bool collectPunctualLights;
            public NativeList<DirectionalLightCandidate> directionalLights;
            public NativeList<PunctualLightCandidate> punctualLights;

            public void Execute()
            {
                for (var lightIndex = 0; lightIndex < visibleLightRenderDataRecords.Length; lightIndex++)
                {
                    var visibleLightRenderDataRecord = visibleLightRenderDataRecords[lightIndex];
                    var lightRenderData = visibleLightRenderDataRecord.lightRenderData;

                    if (collectDirectionalLights && lightRenderData.lightType == LightType.Directional)
                    {
                        directionalLights.AddNoResize(
                            CreateDirectionalLightCandidate(
                                visibleLightRenderDataRecord.visibleLightIndex,
                                lightRenderData));
                    }

                    if (collectPunctualLights && IsPunctualLightSupported(lightRenderData))
                    {
                        punctualLights.AddNoResize(
                            CreatePunctualLightCandidate(
                                lightRenderData));
                    }
                }
            }
        }

        [BurstCompile]
        private struct BuildPunctualLightScreenSpaceBoundsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<PunctualLightCullData> punctualLightCullData;

            [WriteOnly]
            public NativeArray<PunctualLightScreenSpaceBoundsRecord> punctualLightScreenSpaceBounds;

            public PunctualLightScreenSpaceBoundsJobParameters parameters;

            public void Execute(int index)
            {
                punctualLightScreenSpaceBounds[index] = BuildPunctualLightScreenSpaceBoundsRecord(
                    punctualLightCullData[index],
                    parameters);
            }
        }

        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes;
        public DirectionalLightData[] directionalLights = Array.Empty<DirectionalLightData>();
        public PunctualLightData[] punctualLights = Array.Empty<PunctualLightData>();
        public PunctualLightCullData[] punctualLightCullData = Array.Empty<PunctualLightCullData>();
        public PunctualLightScreenSpaceBounds[] punctualLightScreenSpaceBounds = Array.Empty<PunctualLightScreenSpaceBounds>();
        public int mainLightIndex;
        public EntityId mainLightEntityId;
        public int directionalLightCount;
        public int punctualLightCount;
        public int mainDirectionalLightIndex;
        public EntityId mainDirectionalLightEntityId;

        public bool hasVisibleLights => visibleLights.IsCreated && visibleLights.Length > 0;

        public bool hasVisibleReflectionProbes => visibleReflectionProbes.IsCreated && visibleReflectionProbes.Length > 0;

        public bool hasMainLight => IsValidLightIndex(mainLightIndex);

        public bool hasDirectionalLights => directionalLightCount > 0;

        public bool hasPunctualLights => punctualLightCount > 0;

        public bool hasMainDirectionalLight => IsValidDirectionalLightIndex(mainDirectionalLightIndex);

        public int visibleLightCount => hasVisibleLights ? visibleLights.Length : 0;

        public int additionalLightsCount => hasVisibleLights ? visibleLights.Length - (hasMainLight ? 1 : 0) : 0;

        public int visibleReflectionProbeCount => hasVisibleReflectionProbes ? visibleReflectionProbes.Length : 0;

        public int additionalDirectionalLightsCount => hasDirectionalLights ? directionalLightCount - (hasMainDirectionalLight ? 1 : 0) : 0;

        public VisibleLight mainVisibleLight => hasMainLight ? visibleLights[mainLightIndex] : default;

        public Light mainLight => hasMainLight ? visibleLights[mainLightIndex].light : null;

        public DirectionalLightData mainDirectionalLight => hasMainDirectionalLight ? directionalLights[mainDirectionalLightIndex] : default;

        internal void Update(CullingResults cullingResults)
        {
            visibleLights = cullingResults.visibleLights;
            visibleReflectionProbes = cullingResults.visibleReflectionProbes;
            UpdateVisibleLightData(visibleLights, RenderSettings.sun, VisibleLightCollectionMask.Directional | VisibleLightCollectionMask.Punctual);
        }

        internal void UpdateDirectionalLights(NativeArray<VisibleLight> visibleLights, Light sunLight)
        {
            UpdateVisibleLightData(visibleLights, sunLight, VisibleLightCollectionMask.Directional);
        }

        internal void UpdateDirectionalLights(IReadOnlyList<Light> lights, Light sunLight)
        {
            EnsureDirectionalLightCapacity(CountDirectionalLights(lights));

            directionalLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;

            if (lights == null || lights.Count == 0)
                return;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                var light = lights[lightIndex];
                if (!IsDirectionalLightSupported(light))
                    continue;

                directionalLights[directionalLightCount] = CreateDirectionalLightData(light);

                var lightEntityId = light.GetEntityId();
                if (!sunLightEntityId.Equals(EntityId.None) && lightEntityId.Equals(sunLightEntityId))
                {
                    mainDirectionalLightIndex = directionalLightCount;
                    mainDirectionalLightEntityId = lightEntityId;
                }

                var lightIntensity = GetLightIntensity(directionalLights[directionalLightCount].color);
                if (lightIntensity > brightestDirectionalIntensity)
                {
                    brightestDirectionalIntensity = lightIntensity;
                    brightestDirectionalIndex = directionalLightCount;
                    brightestDirectionalEntityId = lightEntityId;
                }

                directionalLightCount++;
            }

            if (mainDirectionalLightIndex >= 0)
                return;

            mainDirectionalLightIndex = brightestDirectionalIndex;
            mainDirectionalLightEntityId = brightestDirectionalEntityId;
        }

        internal void UpdatePunctualLights(NativeArray<VisibleLight> visibleLights)
        {
            UpdateVisibleLightData(visibleLights, null, VisibleLightCollectionMask.Punctual);
        }

        internal void UpdatePunctualLights(IReadOnlyList<Light> lights)
        {
            EnsurePunctualLightCapacity(CountPunctualLights(lights));

            punctualLightCount = 0;

            if (lights == null || lights.Count == 0)
                return;

            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                var light = lights[lightIndex];
                if (!IsPunctualLightSupported(light))
                    continue;

                var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light);
                var punctualLightData = CreatePunctualLightData(trackedLightData);
                punctualLights[punctualLightCount] = punctualLightData;
                punctualLightCullData[punctualLightCount] = CreatePunctualLightCullData(punctualLightData);
                punctualLightCount++;
            }
        }

        internal void UpdatePunctualLightScreenSpaceBounds(in PunctualLightScreenSpaceBoundsParameters parameters)
        {
            EnsurePunctualLightCapacity(punctualLightCount);

            if (punctualLightCount <= 0)
                return;

            var nativePunctualLightCullData = new NativeArray<PunctualLightCullData>(
                punctualLightCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var nativePunctualLightScreenSpaceBounds = new NativeArray<PunctualLightScreenSpaceBoundsRecord>(
                punctualLightCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
                    nativePunctualLightCullData[lightIndex] = punctualLightCullData[lightIndex];

                var buildScreenSpaceBoundsJob = new BuildPunctualLightScreenSpaceBoundsJob
                {
                    punctualLightCullData = nativePunctualLightCullData,
                    punctualLightScreenSpaceBounds = nativePunctualLightScreenSpaceBounds,
                    parameters = new PunctualLightScreenSpaceBoundsJobParameters(parameters),
                };

                buildScreenSpaceBoundsJob.Schedule(punctualLightCount, 32).Complete();

                for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
                {
                    punctualLightScreenSpaceBounds[lightIndex] = ConvertPunctualLightScreenSpaceBounds(
                        nativePunctualLightScreenSpaceBounds[lightIndex]);
                }
            }
            finally
            {
                nativePunctualLightCullData.Dispose();
                nativePunctualLightScreenSpaceBounds.Dispose();
            }
        }

        public override void Reset()
        {
            visibleLights = default;
            visibleReflectionProbes = default;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            directionalLightCount = 0;
            punctualLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
            punctualLightScreenSpaceBounds = Array.Empty<PunctualLightScreenSpaceBounds>();
        }

        internal static PunctualLightScreenSpaceBoundsParameters CreatePunctualLightScreenSpaceBoundsParameters(
            Camera camera,
            int screenWidth,
            int screenHeight,
            int tileSize,
            int sliceCount)
        {
            screenWidth = Mathf.Max(screenWidth, 1);
            screenHeight = Mathf.Max(screenHeight, 1);
            tileSize = Mathf.Max(tileSize, 1);
            sliceCount = Mathf.Max(sliceCount, 1);

            var nearClip = camera != null ? camera.nearClipPlane : 0.1f;
            var farClip = camera != null ? camera.farClipPlane : 1000.0f;
            var aspect = screenHeight > 0 ? screenWidth / (float)screenHeight : 1.0f;
            nearClip = Mathf.Max(nearClip, 0.01f);
            farClip = Mathf.Max(farClip, nearClip + 0.01f);
            var logDepthScale = sliceCount / Mathf.Max(Mathf.Log(farClip / nearClip, 2.0f), 0.0001f);
            var linearDepthScale = sliceCount / Mathf.Max(farClip - nearClip, 0.0001f);
            var isOrthographic = camera != null && camera.orthographic ? 1 : 0;
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
                var halfVerticalFov = Mathf.Deg2Rad * (camera != null ? camera.fieldOfView : 60.0f) * 0.5f;
                tanHalfFovY = Mathf.Max(Mathf.Tan(halfVerticalFov), 0.0001f);
                tanHalfFovX = tanHalfFovY * aspect;
                orthoHalfWidth = 0.0f;
                orthoHalfHeight = 0.0f;
            }

            return new PunctualLightScreenSpaceBoundsParameters(
                camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity,
                screenWidth,
                screenHeight,
                tileSize,
                Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)tileSize)),
                Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)tileSize)),
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

        internal static int FindMainLightIndex(NativeArray<VisibleLight> visibleLights, Light sunLight)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return -1;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                if (!sunLightEntityId.Equals(EntityId.None)
                    && visibleLight.light != null
                    && visibleLight.light.GetEntityId().Equals(sunLightEntityId))
                    return lightIndex;

                var lightIntensity = GetLightIntensity(visibleLight.finalColor);
                if (lightIntensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = lightIntensity;
                brightestDirectionalIndex = lightIndex;
            }

            return brightestDirectionalIndex;
        }

        internal static int FindMainLightIndex(IReadOnlyList<VisibleLightDescriptor> visibleLights, EntityId sunLightEntityId)
        {
            if (visibleLights == null || visibleLights.Count == 0)
                return -1;

            var brightestDirectionalIndex = -1;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < visibleLights.Count; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                if (!sunLightEntityId.Equals(EntityId.None) && visibleLight.lightEntityId.Equals(sunLightEntityId))
                    return lightIndex;

                var lightIntensity = GetLightIntensity(visibleLight.finalColor);
                if (lightIntensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = lightIntensity;
                brightestDirectionalIndex = lightIndex;
            }

            return brightestDirectionalIndex;
        }

        private bool IsValidLightIndex(int lightIndex)
        {
            return hasVisibleLights && lightIndex >= 0 && lightIndex < visibleLights.Length;
        }

        private bool IsValidDirectionalLightIndex(int lightIndex)
        {
            return hasDirectionalLights && directionalLights != null && lightIndex >= 0 && lightIndex < directionalLightCount;
        }

        private void EnsureDirectionalLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= directionalLights.Length)
                return;

            directionalLights = new DirectionalLightData[requiredCapacity];
        }

        private void EnsurePunctualLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity > punctualLights.Length)
                punctualLights = new PunctualLightData[requiredCapacity];

            if (requiredCapacity > punctualLightCullData.Length)
                punctualLightCullData = new PunctualLightCullData[requiredCapacity];

            if (requiredCapacity > punctualLightScreenSpaceBounds.Length)
                punctualLightScreenSpaceBounds = new PunctualLightScreenSpaceBounds[requiredCapacity];
        }

        private static int CountDirectionalLights(IReadOnlyList<Light> lights)
        {
            if (lights == null || lights.Count == 0)
                return 0;

            var directionalLightCount = 0;
            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                if (IsDirectionalLightSupported(lights[lightIndex]))
                    directionalLightCount++;
            }

            return directionalLightCount;
        }

        private static int CountPunctualLights(IReadOnlyList<Light> lights)
        {
            if (lights == null || lights.Count == 0)
                return 0;

            var count = 0;
            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                if (IsPunctualLightSupported(lights[lightIndex]))
                    count++;
            }

            return count;
        }

        private static int CountDirectionalLights(NativeArray<VisibleLight> visibleLights)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return 0;

            var directionalLightCount = 0;
            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                if (visibleLights[lightIndex].lightType == LightType.Directional)
                    directionalLightCount++;
            }

            return directionalLightCount;
        }

        private static int CountPunctualLights(NativeArray<VisibleLight> visibleLights)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return 0;

            var count = 0;
            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                if (IsPunctualLightSupported(visibleLights[lightIndex]))
                    count++;
            }

            return count;
        }

        private static DirectionalLightData CreateDirectionalLightData(Light light)
        {
            return CreateDirectionalLightData(VividLightRenderDatabase.instance.UpdateLightData(light));
        }

        private static DirectionalLightData CreateDirectionalLightData(VividLightRenderData trackedLightData)
        {
            return new DirectionalLightData
            {
                directionWS = -trackedLightData.forwardWS,
                shadowStrength = trackedLightData.shadowStrength,
                color = trackedLightData.color,
                renderingLayerMask = trackedLightData.renderingLayerMask,
            };
        }

        private static PunctualLightData CreatePunctualLightData(Light light)
        {
            return CreatePunctualLightData(VividLightRenderDatabase.instance.UpdateLightData(light));
        }

        private static PunctualLightData CreatePunctualLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            GetSpotAngleParameters(trackedLightData.lightType, trackedLightData.innerSpotAngle, trackedLightData.spotAngle, out var angleScale, out var angleOffset);

            return new PunctualLightData
            {
                positionWS = trackedLightData.positionWS,
                range = range,
                color = trackedLightData.color,
                lightType = GetPunctualLightType(trackedLightData.lightType),
                directionWS = trackedLightData.forwardWS,
                angleScale = angleScale,
                angleOffset = angleOffset,
                inverseRangeSquared = trackedLightData.inverseRangeSquared > 0.0f
                    ? trackedLightData.inverseRangeSquared
                    : 1.0f / Mathf.Max(range * range, 1e-6f),
                shadowStrength = trackedLightData.shadowStrength,
                renderingLayerMask = trackedLightData.renderingLayerMask,
            };
        }

        private static PunctualLightCullData CreatePunctualLightCullData(PunctualLightData source)
        {
            GetPunctualLightCullingShapeData(
                source,
                out var directionWS,
                out var cosOuterAngle,
                out var radiusAtRange);
            GetPunctualLightCullingSphere(source, out var cullingCenterWS, out var cullingRadius);

            return new PunctualLightCullData
            {
                positionWS = source.positionWS,
                range = source.range,
                directionWS = directionWS,
                lightType = source.lightType,
                cosOuterAngle = cosOuterAngle,
                radiusAtRange = radiusAtRange,
                cullingCenterWS = cullingCenterWS,
                cullingRadius = cullingRadius,
            };
        }

        private static PunctualLightScreenSpaceBounds CreatePunctualLightScreenSpaceBounds(
            PunctualLightCullData source,
            in PunctualLightScreenSpaceBoundsParameters parameters)
        {
            return ConvertPunctualLightScreenSpaceBounds(
                BuildPunctualLightScreenSpaceBoundsRecord(
                    source,
                    new PunctualLightScreenSpaceBoundsJobParameters(parameters)));
        }

        private void UpdateVisibleLightData(NativeArray<VisibleLight> visibleLights, Light sunLight, VisibleLightCollectionMask collectionMask)
        {
            var collectDirectionalLights = (collectionMask & VisibleLightCollectionMask.Directional) != 0;
            var collectPunctualLights = (collectionMask & VisibleLightCollectionMask.Punctual) != 0;

            if (collectDirectionalLights)
            {
                directionalLightCount = 0;
                mainLightIndex = -1;
                mainLightEntityId = EntityId.None;
                mainDirectionalLightIndex = -1;
                mainDirectionalLightEntityId = EntityId.None;
            }

            if (collectPunctualLights)
                punctualLightCount = 0;

            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return;

            var lightCapacity = Mathf.Max(visibleLights.Length, 1);
            using var visibleLightRenderDataRecords = new NativeList<VisibleLightRenderDataRecord>(lightCapacity, Allocator.TempJob);
            using var directionalCandidates = new NativeList<DirectionalLightCandidate>(lightCapacity, Allocator.TempJob);
            using var punctualCandidates = new NativeList<PunctualLightCandidate>(lightCapacity, Allocator.TempJob);

            CollectVisibleLightRenderDataRecords(visibleLights, visibleLightRenderDataRecords);

            var buildCandidatesJob = new BuildVisibleLightCandidatesJob
            {
                visibleLightRenderDataRecords = visibleLightRenderDataRecords.AsArray(),
                collectDirectionalLights = collectDirectionalLights,
                collectPunctualLights = collectPunctualLights,
                directionalLights = directionalCandidates,
                punctualLights = punctualCandidates,
            };

            buildCandidatesJob.Schedule().Complete();

            if (collectDirectionalLights)
                ApplyDirectionalLightCandidates(directionalCandidates, sunLight);

            if (collectPunctualLights)
                ApplyPunctualLightCandidates(punctualCandidates);
        }

        private void ApplyDirectionalLightCandidates(NativeList<DirectionalLightCandidate> directionalCandidates, Light sunLight)
        {
            EnsureDirectionalLightCapacity(directionalCandidates.Length);

            directionalLightCount = directionalCandidates.Length;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIntensity = float.NegativeInfinity;
            var brightestVisibleLightIndex = -1;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;

            for (var directionalIndex = 0; directionalIndex < directionalLightCount; directionalIndex++)
            {
                var candidate = directionalCandidates[directionalIndex];
                directionalLights[directionalIndex] = candidate.lightData;

                if (!sunLightEntityId.Equals(EntityId.None) && candidate.lightEntityId.Equals(sunLightEntityId))
                {
                    mainLightIndex = candidate.visibleLightIndex;
                    mainLightEntityId = candidate.lightEntityId;
                    mainDirectionalLightIndex = directionalIndex;
                    mainDirectionalLightEntityId = candidate.lightEntityId;
                }

                if (candidate.intensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = candidate.intensity;
                brightestVisibleLightIndex = candidate.visibleLightIndex;
                brightestDirectionalIndex = directionalIndex;
                brightestDirectionalEntityId = candidate.lightEntityId;
            }

            if (mainDirectionalLightIndex >= 0)
                return;

            mainLightIndex = brightestVisibleLightIndex;
            mainLightEntityId = brightestDirectionalEntityId;
            mainDirectionalLightIndex = brightestDirectionalIndex;
            mainDirectionalLightEntityId = brightestDirectionalEntityId;
        }

        private void ApplyPunctualLightCandidates(NativeList<PunctualLightCandidate> punctualCandidates)
        {
            EnsurePunctualLightCapacity(punctualCandidates.Length);

            punctualLightCount = punctualCandidates.Length;

            for (var punctualIndex = 0; punctualIndex < punctualLightCount; punctualIndex++)
            {
                punctualLights[punctualIndex] = punctualCandidates[punctualIndex].lightData;
                punctualLightCullData[punctualIndex] = punctualCandidates[punctualIndex].lightCullData;
            }
        }

        private void CollectVisibleLightRenderDataRecords(
            NativeArray<VisibleLight> visibleLights,
            NativeList<VisibleLightRenderDataRecord> visibleLightRenderDataRecords)
        {
            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                var light = visibleLight.light;

                visibleLightRenderDataRecords.AddNoResize(new VisibleLightRenderDataRecord
                {
                    visibleLightIndex = lightIndex,
                    lightRenderData = GetVisibleLightRenderData(light, visibleLight),
                });
            }
        }

        private static VividLightRenderData GetVisibleLightRenderData(Light light, VisibleLight visibleLight)
        {
            if (light != null)
                return VividLightRenderDatabase.instance.UpdateLightData(light);

            return CreateLightRenderData(visibleLight);
        }

        private static VividLightRenderData CreateLightRenderData(VisibleLight visibleLight)
        {
            var localToWorld = visibleLight.localToWorldMatrix;
            var range = Mathf.Max(visibleLight.range, 0.0f);
            var finalColor = visibleLight.finalColor;

            return new VividLightRenderData
            {
                lightEntityId = EntityId.None,
                lightType = visibleLight.lightType,
                positionWS = new Vector3(localToWorld.m03, localToWorld.m13, localToWorld.m23),
                range = range,
                forwardWS = new Vector3(localToWorld.m02, localToWorld.m12, localToWorld.m22),
                intensity = GetLightIntensity(finalColor),
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                shadowStrength = 0.0f,
                spotAngle = visibleLight.spotAngle,
                innerSpotAngle = visibleLight.innerSpotAngle,
                inverseRangeSquared = range > 0.0f ? 1.0f / Mathf.Max(range * range, 1e-6f) : 0.0f,
                renderingLayerMask = 0u,
                shadowRenderingLayerMask = 0u,
                flags = VividLightRenderDataFlags.Enabled | VividLightRenderDataFlags.ActiveInHierarchy,
            };
        }

        private static DirectionalLightCandidate CreateDirectionalLightCandidate(int visibleLightIndex, VividLightRenderData trackedLightData)
        {
            return new DirectionalLightCandidate
            {
                visibleLightIndex = visibleLightIndex,
                lightEntityId = trackedLightData.lightEntityId,
                lightData = CreateDirectionalLightData(trackedLightData),
                intensity = GetLightIntensity(trackedLightData.color),
            };
        }

        private static PunctualLightCandidate CreatePunctualLightCandidate(VividLightRenderData trackedLightData)
        {
            var punctualLightData = CreatePunctualLightData(trackedLightData);

            return new PunctualLightCandidate
            {
                lightData = punctualLightData,
                lightCullData = CreatePunctualLightCullData(punctualLightData),
            };
        }

        private static bool IsDirectionalLightSupported(Light light)
        {
            return light != null
                   && light.type == LightType.Directional
                   && light.enabled
                   && light.gameObject.activeInHierarchy;
        }

        private static bool IsPunctualLightSupported(Light light)
        {
            return light != null
                   && (light.type == LightType.Point || light.type == LightType.Spot)
                   && light.enabled
                   && light.gameObject.activeInHierarchy
                   && light.range > 0.0f;
        }

        private static bool IsPunctualLightSupported(VisibleLight visibleLight)
        {
            return (visibleLight.lightType == LightType.Point || visibleLight.lightType == LightType.Spot)
                   && visibleLight.range > 0.0f;
        }

        private static bool IsPunctualLightSupported(VividLightRenderData trackedLightData)
        {
            return (trackedLightData.lightType == LightType.Point || trackedLightData.lightType == LightType.Spot)
                   && trackedLightData.range > 0.0f;
        }

        private static uint GetPunctualLightType(LightType lightType)
        {
            return lightType == LightType.Spot ? 1u : 0u;
        }

        private static void GetPunctualLightCullingShapeData(
            PunctualLightData source,
            out Vector3 directionWS,
            out float cosOuterAngle,
            out float radiusAtRange)
        {
            directionWS = NormalizeDirection(source.directionWS, Vector3.forward);
            cosOuterAngle = 1.0f;
            radiusAtRange = 0.0f;

            if (source.lightType != 1u)
                return;

            cosOuterAngle = Mathf.Clamp01(-source.angleOffset / Mathf.Max(source.angleScale, 1e-6f));
            var tanOuter = Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(cosOuterAngle * cosOuterAngle, 1e-6f) - 1.0f, 0.0f));
            radiusAtRange = source.range * tanOuter;
        }

        private static void GetPunctualLightCullingSphere(PunctualLightData source, out Vector3 cullingCenterWS, out float cullingRadius)
        {
            cullingCenterWS = source.positionWS;
            cullingRadius = source.range;

            if (source.lightType != 1u)
                return;

            GetPunctualLightCullingShapeData(source, out var directionWS, out _, out var radiusAtRange);
            var tanOuter = radiusAtRange / Mathf.Max(source.range, 1e-6f);
            float centerDistance;

            if (tanOuter <= 1.0f)
            {
                centerDistance = 0.5f * source.range * (1.0f + tanOuter * tanOuter);
                cullingRadius = centerDistance;
            }
            else
            {
                centerDistance = source.range;
                cullingRadius = source.range * tanOuter;
            }

            cullingCenterWS = source.positionWS + directionWS * centerDistance;
        }

        private static Vector3 NormalizeDirection(Vector3 direction, Vector3 fallback)
        {
            var lengthSq = direction.sqrMagnitude;
            if (lengthSq <= 1e-6f)
                return fallback;

            return direction / Mathf.Sqrt(lengthSq);
        }

        private static void GetSpotAngleParameters(LightType lightType, float innerSpotAngle, float outerSpotAngle, out float angleScale, out float angleOffset)
        {
            if (lightType != LightType.Spot)
            {
                angleScale = 0.0f;
                angleOffset = 1.0f;
                return;
            }

            var innerHalfAngle = Mathf.Clamp(innerSpotAngle * 0.5f, 0.0f, 89.0f) * Mathf.Deg2Rad;
            var outerHalfAngle = Mathf.Clamp(outerSpotAngle * 0.5f, innerSpotAngle * 0.5f + 0.001f, 89.0f) * Mathf.Deg2Rad;
            var cosInner = Mathf.Cos(innerHalfAngle);
            var cosOuter = Mathf.Cos(outerHalfAngle);
            var angleRange = Mathf.Max(cosInner - cosOuter, 0.001f);

            angleScale = 1.0f / angleRange;
            angleOffset = -cosOuter * angleScale;
        }

        private static float GetLightIntensity(Color finalColor)
        {
            return Mathf.Max(finalColor.r, finalColor.g, finalColor.b);
        }

        private static float GetLightIntensity(Vector3 finalColor)
        {
            return math.max(finalColor.x, math.max(finalColor.y, finalColor.z));
        }

        private static PunctualLightScreenSpaceBounds ConvertPunctualLightScreenSpaceBounds(PunctualLightScreenSpaceBoundsRecord source)
        {
            return new PunctualLightScreenSpaceBounds
            {
                viewSpaceAabbMin = new Vector3(source.viewSpaceAabbMin.x, source.viewSpaceAabbMin.y, source.viewSpaceAabbMin.z),
                viewSpaceAabbMax = new Vector3(source.viewSpaceAabbMax.x, source.viewSpaceAabbMax.y, source.viewSpaceAabbMax.z),
                clipSpaceAabbMin = new Vector2(source.clipSpaceAabbMin.x, source.clipSpaceAabbMin.y),
                clipSpaceAabbMax = new Vector2(source.clipSpaceAabbMax.x, source.clipSpaceAabbMax.y),
                sliceMin = source.sliceMin,
                sliceMax = source.sliceMax,
                tileMinX = source.tileMinX,
                tileMaxX = source.tileMaxX,
                tileMinY = source.tileMinY,
                tileMaxY = source.tileMaxY,
                isValid = source.isValid,
            };
        }

        private static PunctualLightScreenSpaceBoundsRecord BuildPunctualLightScreenSpaceBoundsRecord(
            PunctualLightCullData source,
            in PunctualLightScreenSpaceBoundsJobParameters parameters)
        {
            var cullingCenterVS = TransformWorldToPositiveViewSpace(parameters.worldToViewMatrix, source.cullingCenterWS);
            var radius = math.max(source.cullingRadius, 0.0f);
            var radiusVector = new float3(radius, radius, radius);
            var viewSpaceAabbMin = cullingCenterVS - radiusVector;
            var viewSpaceAabbMax = cullingCenterVS + radiusVector;
            var bounds = new PunctualLightScreenSpaceBoundsRecord
            {
                viewSpaceAabbMin = viewSpaceAabbMin,
                viewSpaceAabbMax = viewSpaceAabbMax,
            };

            if (radius <= 0.0f)
                return bounds;

            if (!TryGetPunctualLightSliceRange(viewSpaceAabbMin.z, viewSpaceAabbMax.z, parameters, out var sliceMin, out var sliceMax))
                return bounds;

            if (!TryGetPunctualLightClipSpaceRect(cullingCenterVS, radius, parameters, out var clipSpaceAabbMin, out var clipSpaceAabbMax))
                return bounds;

            if (!TryConvertClipRectToTileRange(clipSpaceAabbMin, clipSpaceAabbMax, parameters, out var tileMinX, out var tileMaxX, out var tileMinY, out var tileMaxY))
                return bounds;

            bounds.clipSpaceAabbMin = clipSpaceAabbMin;
            bounds.clipSpaceAabbMax = clipSpaceAabbMax;
            bounds.sliceMin = sliceMin;
            bounds.sliceMax = sliceMax;
            bounds.tileMinX = tileMinX;
            bounds.tileMaxX = tileMaxX;
            bounds.tileMinY = tileMinY;
            bounds.tileMaxY = tileMaxY;
            bounds.isValid = 1u;
            return bounds;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 source)
        {
            return new float4x4(
                new float4(source.m00, source.m10, source.m20, source.m30),
                new float4(source.m01, source.m11, source.m21, source.m31),
                new float4(source.m02, source.m12, source.m22, source.m32),
                new float4(source.m03, source.m13, source.m23, source.m33));
        }

        private static float3 TransformWorldToPositiveViewSpace(float4x4 worldToViewMatrix, Vector3 worldPosition)
        {
            var viewPosition = math.mul(
                worldToViewMatrix,
                new float4(worldPosition.x, worldPosition.y, worldPosition.z, 1.0f));
            return new float3(viewPosition.x, viewPosition.y, -viewPosition.z);
        }

        private static bool TryGetPunctualLightSliceRange(
            float depthMin,
            float depthMax,
            in PunctualLightScreenSpaceBoundsJobParameters parameters,
            out int sliceMin,
            out int sliceMax)
        {
            sliceMin = 0;
            sliceMax = 0;

            if (depthMax < parameters.nearClip || depthMin > parameters.farClip)
                return false;

            depthMin = math.max(depthMin, parameters.nearClip);
            depthMax = math.min(depthMax, parameters.farClip);
            sliceMin = GetClusterSliceIndex(depthMin, parameters);
            sliceMax = GetClusterSliceIndex(depthMax, parameters);
            return sliceMax >= sliceMin;
        }

        private static bool TryGetPunctualLightClipSpaceRect(
            float3 cullingCenterVS,
            float radius,
            in PunctualLightScreenSpaceBoundsJobParameters parameters,
            out float2 clipSpaceAabbMin,
            out float2 clipSpaceAabbMax)
        {
            clipSpaceAabbMin = default;
            clipSpaceAabbMax = default;

            float minClipX;
            float maxClipX;
            float minClipY;
            float maxClipY;

            if (parameters.isOrthographic != 0)
            {
                var orthoHalfWidth = math.max(parameters.orthoHalfWidth, 1e-6f);
                var orthoHalfHeight = math.max(parameters.orthoHalfHeight, 1e-6f);
                minClipX = (cullingCenterVS.x - radius) / orthoHalfWidth;
                maxClipX = (cullingCenterVS.x + radius) / orthoHalfWidth;
                minClipY = (cullingCenterVS.y - radius) / orthoHalfHeight;
                maxClipY = (cullingCenterVS.y + radius) / orthoHalfHeight;
            }
            else
            {
                var projectionDepth = math.max(cullingCenterVS.z - radius, parameters.nearClip);
                var projectedHalfWidth = math.max(projectionDepth * parameters.tanHalfFovX, 1e-6f);
                var projectedHalfHeight = math.max(projectionDepth * parameters.tanHalfFovY, 1e-6f);
                minClipX = (cullingCenterVS.x - radius) / projectedHalfWidth;
                maxClipX = (cullingCenterVS.x + radius) / projectedHalfWidth;
                minClipY = (cullingCenterVS.y - radius) / projectedHalfHeight;
                maxClipY = (cullingCenterVS.y + radius) / projectedHalfHeight;
            }

            clipSpaceAabbMin = new float2(math.min(minClipX, maxClipX), math.min(minClipY, maxClipY));
            clipSpaceAabbMax = new float2(math.max(minClipX, maxClipX), math.max(minClipY, maxClipY));
            return true;
        }

        private static int GetClusterSliceIndex(float depth, in PunctualLightScreenSpaceBoundsJobParameters parameters)
        {
            depth = math.clamp(depth, parameters.nearClip, parameters.farClip);

            if (parameters.isOrthographic != 0)
            {
                var linearSlice = (int)math.floor((depth - parameters.nearClip) * parameters.linearDepthScale);
                return math.clamp(linearSlice, 0, parameters.sliceCount - 1);
            }

            var logarithmicDepth = math.log2(math.max(depth / math.max(parameters.nearClip, 1e-6f), 1.0f));
            var logarithmicSlice = (int)math.floor(logarithmicDepth * parameters.logDepthScale);
            return math.clamp(logarithmicSlice, 0, parameters.sliceCount - 1);
        }

        private static bool TryConvertClipRectToTileRange(
            float2 clipSpaceAabbMin,
            float2 clipSpaceAabbMax,
            in PunctualLightScreenSpaceBoundsJobParameters parameters,
            out int tileMinX,
            out int tileMaxX,
            out int tileMinY,
            out int tileMaxY)
        {
            tileMinX = 0;
            tileMaxX = 0;
            tileMinY = 0;
            tileMaxY = 0;

            var screenMinX = GetScreenXFromClipSpace(clipSpaceAabbMin.x, parameters.screenWidth);
            var screenMaxX = GetScreenXFromClipSpace(clipSpaceAabbMax.x, parameters.screenWidth);
            var screenMinY = GetScreenYFromClipSpace(clipSpaceAabbMax.y, parameters.screenHeight);
            var screenMaxY = GetScreenYFromClipSpace(clipSpaceAabbMin.y, parameters.screenHeight);
            var rectMinX = math.min(screenMinX, screenMaxX);
            var rectMaxX = math.max(screenMinX, screenMaxX);
            var rectMinY = math.min(screenMinY, screenMaxY);
            var rectMaxY = math.max(screenMinY, screenMaxY);
            var maxPixelX = (float)math.max(parameters.screenWidth - 1, 0);
            var maxPixelY = (float)math.max(parameters.screenHeight - 1, 0);

            if (rectMaxX < 0.0f
                || rectMinX > maxPixelX
                || rectMaxY < 0.0f
                || rectMinY > maxPixelY)
            {
                return false;
            }

            var clampedMinX = math.clamp(rectMinX, 0.0f, maxPixelX);
            var clampedMaxX = math.clamp(rectMaxX, 0.0f, maxPixelX);
            var clampedMinY = math.clamp(rectMinY, 0.0f, maxPixelY);
            var clampedMaxY = math.clamp(rectMaxY, 0.0f, maxPixelY);
            tileMinX = math.clamp((int)math.floor(clampedMinX / parameters.tileSize), 0, parameters.tileCountX - 1);
            tileMaxX = math.clamp((int)math.floor(clampedMaxX / parameters.tileSize), 0, parameters.tileCountX - 1);
            tileMinY = math.clamp((int)math.floor(clampedMinY / parameters.tileSize), 0, parameters.tileCountY - 1);
            tileMaxY = math.clamp((int)math.floor(clampedMaxY / parameters.tileSize), 0, parameters.tileCountY - 1);
            return true;
        }

        private static float GetScreenXFromClipSpace(float clipSpaceX, int screenWidth)
        {
            return (clipSpaceX * 0.5f + 0.5f) * screenWidth;
        }

        private static float GetScreenYFromClipSpace(float clipSpaceY, int screenHeight)
        {
            return (1.0f - clipSpaceY) * 0.5f * screenHeight;
        }
    }
}
