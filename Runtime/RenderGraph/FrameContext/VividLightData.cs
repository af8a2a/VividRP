using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        public struct PunctualLightViewSpaceCullData
        {
            public Vector3 positionVS;
            public float range;
            public Vector3 directionVS;
            public float cosOuterAngle;
            public Vector3 cullingCenterVS;
            public float cullingRadius;
            public uint lightType;
            public float radiusAtRange;

            internal static int Stride => Marshal.SizeOf<PunctualLightViewSpaceCullData>();
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

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightCoarseRange
        {
            public int startIndex;
            public int lightCount;

            internal static int Stride => Marshal.SizeOf<PunctualLightCoarseRange>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightCoarseRecord
        {
            public int lightIndex;
            public int tileMinX;
            public int tileMaxX;
            public int tileMinY;
            public int tileMaxY;

            internal static int Stride => Marshal.SizeOf<PunctualLightCoarseRecord>();
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

        internal readonly struct PunctualLightClusteredLightListParameters
        {
            public readonly PunctualLightScreenSpaceBoundsParameters screenSpaceBoundsParameters;
            public readonly int punctualLightCount;
            public readonly int clusterCount;
            public readonly int lightIndexCapacity;
            public readonly int bigTileSize;
            public readonly int bigTileCountX;
            public readonly int bigTileCountY;
            public readonly int bigTileCount;
            public readonly int bigTileLightIndexCapacity;

            public int screenWidth => screenSpaceBoundsParameters.screenWidth;

            public int screenHeight => screenSpaceBoundsParameters.screenHeight;

            public int tileSize => screenSpaceBoundsParameters.tileSize;

            public int tileCountX => screenSpaceBoundsParameters.tileCountX;

            public int tileCountY => screenSpaceBoundsParameters.tileCountY;

            public int sliceCount => screenSpaceBoundsParameters.sliceCount;

            public PunctualLightClusteredLightListParameters(
                PunctualLightScreenSpaceBoundsParameters screenSpaceBoundsParameters,
                int punctualLightCount,
                int clusterCount,
                int lightIndexCapacity,
                int bigTileSize,
                int bigTileCountX,
                int bigTileCountY,
                int bigTileCount,
                int bigTileLightIndexCapacity)
            {
                this.screenSpaceBoundsParameters = screenSpaceBoundsParameters;
                this.punctualLightCount = punctualLightCount;
                this.clusterCount = clusterCount;
                this.lightIndexCapacity = lightIndexCapacity;
                this.bigTileSize = bigTileSize;
                this.bigTileCountX = bigTileCountX;
                this.bigTileCountY = bigTileCountY;
                this.bigTileCount = bigTileCount;
                this.bigTileLightIndexCapacity = bigTileLightIndexCapacity;
            }
        }

        internal readonly struct PunctualLightClusteredLightListBuildResult
        {
            public readonly int punctualLightCount;
            public readonly int coarseRangeCount;
            public readonly int coarseRecordCount;

            public PunctualLightClusteredLightListBuildResult(
                int punctualLightCount,
                int coarseRangeCount,
                int coarseRecordCount)
            {
                this.punctualLightCount = punctualLightCount;
                this.coarseRangeCount = coarseRangeCount;
                this.coarseRecordCount = coarseRecordCount;
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
        private struct PunctualLightViewSpaceCullDataRecord
        {
            public float3 positionVS;
            public float range;
            public float3 directionVS;
            public float cosOuterAngle;
            public float3 cullingCenterVS;
            public float cullingRadius;
            public uint lightType;
            public float radiusAtRange;
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

        private struct PunctualLightClusteredCullBuildContext : IDisposable
        {
            public NativeArray<PunctualLightCullData> punctualLightCullData;
            public NativeArray<PunctualLightViewSpaceCullDataRecord> punctualLightViewSpaceCullData;
            public NativeArray<PunctualLightScreenSpaceBoundsRecord> punctualLightScreenSpaceBounds;

            public PunctualLightClusteredCullBuildContext(int punctualLightCount)
            {
                punctualLightCullData = new NativeArray<PunctualLightCullData>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                punctualLightViewSpaceCullData = new NativeArray<PunctualLightViewSpaceCullDataRecord>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                punctualLightScreenSpaceBounds = new NativeArray<PunctualLightScreenSpaceBoundsRecord>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (punctualLightCullData.IsCreated)
                    punctualLightCullData.Dispose();

                if (punctualLightViewSpaceCullData.IsCreated)
                    punctualLightViewSpaceCullData.Dispose();

                if (punctualLightScreenSpaceBounds.IsCreated)
                    punctualLightScreenSpaceBounds.Dispose();
            }
        }

        private struct PunctualLightCoarseBuildContext : IDisposable
        {
            public NativeArray<int> sliceLightCounts;
            public NativeArray<int> sliceStartOffsets;
            public NativeArray<PunctualLightCoarseRecord> punctualLightCoarseRecords;

            public PunctualLightCoarseBuildContext(int sliceCount)
            {
                sliceLightCounts = new NativeArray<int>(
                    sliceCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                sliceStartOffsets = new NativeArray<int>(
                    sliceCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                punctualLightCoarseRecords = default;
            }

            public void AllocateRecords(int recordCount)
            {
                if (punctualLightCoarseRecords.IsCreated)
                    punctualLightCoarseRecords.Dispose();

                if (recordCount <= 0)
                {
                    punctualLightCoarseRecords = default;
                    return;
                }

                punctualLightCoarseRecords = new NativeArray<PunctualLightCoarseRecord>(
                    recordCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (punctualLightCoarseRecords.IsCreated)
                    punctualLightCoarseRecords.Dispose();

                if (sliceLightCounts.IsCreated)
                    sliceLightCounts.Dispose();

                if (sliceStartOffsets.IsCreated)
                    sliceStartOffsets.Dispose();
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
        private struct BuildPunctualLightClusteredCullDataJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<PunctualLightCullData> punctualLightCullData;

            [WriteOnly]
            public NativeArray<PunctualLightViewSpaceCullDataRecord> punctualLightViewSpaceCullData;

            [WriteOnly]
            public NativeArray<PunctualLightScreenSpaceBoundsRecord> punctualLightScreenSpaceBounds;

            public PunctualLightScreenSpaceBoundsJobParameters parameters;

            public void Execute(int index)
            {
                var viewSpaceCullData = BuildPunctualLightViewSpaceCullDataRecord(
                    punctualLightCullData[index],
                    parameters.worldToViewMatrix);
                punctualLightViewSpaceCullData[index] = viewSpaceCullData;
                punctualLightScreenSpaceBounds[index] = BuildPunctualLightScreenSpaceBoundsRecord(
                    viewSpaceCullData,
                    parameters);
            }
        }

        [BurstCompile]
        private struct CountPunctualLightCoarseRangesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<PunctualLightScreenSpaceBoundsRecord> punctualLightScreenSpaceBounds;

            [WriteOnly]
            public NativeArray<int> sliceLightCounts;

            public void Execute(int sliceIndex)
            {
                var lightCount = 0;

                for (var lightIndex = 0; lightIndex < punctualLightScreenSpaceBounds.Length; lightIndex++)
                {
                    var screenSpaceBounds = punctualLightScreenSpaceBounds[lightIndex];
                    if (screenSpaceBounds.isValid == 0u
                        || sliceIndex < screenSpaceBounds.sliceMin
                        || sliceIndex > screenSpaceBounds.sliceMax)
                    {
                        continue;
                    }

                    lightCount++;
                }

                sliceLightCounts[sliceIndex] = lightCount;
            }
        }

        [BurstCompile]
        private struct BuildPunctualLightCoarseRecordsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<PunctualLightScreenSpaceBoundsRecord> punctualLightScreenSpaceBounds;

            [ReadOnly]
            public NativeArray<int> sliceStartOffsets;

            [WriteOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<PunctualLightCoarseRecord> punctualLightCoarseRecords;

            public void Execute(int sliceIndex)
            {
                var recordIndex = sliceStartOffsets[sliceIndex];

                for (var lightIndex = 0; lightIndex < punctualLightScreenSpaceBounds.Length; lightIndex++)
                {
                    var screenSpaceBounds = punctualLightScreenSpaceBounds[lightIndex];
                    if (screenSpaceBounds.isValid == 0u
                        || sliceIndex < screenSpaceBounds.sliceMin
                        || sliceIndex > screenSpaceBounds.sliceMax)
                    {
                        continue;
                    }

                    punctualLightCoarseRecords[recordIndex++] = new PunctualLightCoarseRecord
                    {
                        lightIndex = lightIndex,
                        tileMinX = screenSpaceBounds.tileMinX,
                        tileMaxX = screenSpaceBounds.tileMaxX,
                        tileMinY = screenSpaceBounds.tileMinY,
                        tileMaxY = screenSpaceBounds.tileMaxY,
                    };
                }
            }
        }

        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes;
        public DirectionalLightData[] directionalLights = Array.Empty<DirectionalLightData>();
        public PunctualLightData[] punctualLights = Array.Empty<PunctualLightData>();
        public PunctualLightCullData[] punctualLightCullData = Array.Empty<PunctualLightCullData>();
        public PunctualLightViewSpaceCullData[] punctualLightViewSpaceCullData = Array.Empty<PunctualLightViewSpaceCullData>();
        public PunctualLightScreenSpaceBounds[] punctualLightScreenSpaceBounds = Array.Empty<PunctualLightScreenSpaceBounds>();
        public PunctualLightCoarseRange[] punctualLightCoarseRanges = Array.Empty<PunctualLightCoarseRange>();
        public PunctualLightCoarseRecord[] punctualLightCoarseRecords = Array.Empty<PunctualLightCoarseRecord>();
        public int mainLightIndex;
        public EntityId mainLightEntityId;
        public int directionalLightCount;
        public int punctualLightCount;
        public int punctualLightCoarseRangeCount;
        public int punctualLightCoarseRecordCount;
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
            punctualLightCoarseRangeCount = 0;
            punctualLightCoarseRecordCount = 0;

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
            UpdatePunctualLightClusteredCullData(parameters);
        }

        internal void UpdatePunctualLightClusteredLightListData(in PunctualLightScreenSpaceBoundsParameters parameters)
        {
            BuildPunctualLightClusteredCullingData(parameters, buildCoarseCullingData: true);
        }

        internal PunctualLightClusteredLightListBuildResult UpdatePunctualLightClusteredLightListData(
            in PunctualLightClusteredLightListParameters parameters)
        {
            UpdatePunctualLightClusteredLightListData(parameters.screenSpaceBoundsParameters);
            return new PunctualLightClusteredLightListBuildResult(
                punctualLightCount,
                punctualLightCoarseRangeCount,
                punctualLightCoarseRecordCount);
        }

        internal void UpdatePunctualLightClusteredCullData(in PunctualLightScreenSpaceBoundsParameters parameters)
        {
            BuildPunctualLightClusteredCullingData(parameters, buildCoarseCullingData: false);
        }

        private void BuildPunctualLightClusteredCullingData(
            in PunctualLightScreenSpaceBoundsParameters parameters,
            bool buildCoarseCullingData)
        {
            EnsurePunctualLightCapacity(punctualLightCount);
            punctualLightCoarseRangeCount = 0;
            punctualLightCoarseRecordCount = 0;

            if (punctualLightCount <= 0)
                return;

            using var buildContext = new PunctualLightClusteredCullBuildContext(punctualLightCount);
            NativeArray<PunctualLightCullData>.Copy(punctualLightCullData, buildContext.punctualLightCullData, punctualLightCount);
            RunPunctualLightClusteredCullBuildJob(parameters, buildContext);
            ApplyPunctualLightClusteredCullBuildContext(buildContext);

            if (buildCoarseCullingData)
                BuildPunctualLightCoarseCullingData(buildContext.punctualLightScreenSpaceBounds, parameters.sliceCount);
        }

        internal void UpdatePunctualLightCoarseCullingData(int sliceCount)
        {
            var nativePunctualLightScreenSpaceBounds = new NativeArray<PunctualLightScreenSpaceBoundsRecord>(
                punctualLightCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
                {
                    nativePunctualLightScreenSpaceBounds[lightIndex] = ConvertPunctualLightScreenSpaceBoundsRecord(
                        punctualLightScreenSpaceBounds[lightIndex]);
                }

                BuildPunctualLightCoarseCullingData(nativePunctualLightScreenSpaceBounds, sliceCount);
            }
            finally
            {
                nativePunctualLightScreenSpaceBounds.Dispose();
            }
        }

        private void BuildPunctualLightCoarseCullingData(
            NativeArray<PunctualLightScreenSpaceBoundsRecord> nativePunctualLightScreenSpaceBounds,
            int sliceCount)
        {
            sliceCount = Mathf.Max(sliceCount, 1);
            EnsurePunctualLightCoarseCapacity(sliceCount, 0);

            punctualLightCoarseRangeCount = sliceCount;
            punctualLightCoarseRecordCount = 0;

            for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
                punctualLightCoarseRanges[sliceIndex] = default;

            if (nativePunctualLightScreenSpaceBounds.Length <= 0)
                return;

            var buildContext = new PunctualLightCoarseBuildContext(sliceCount);

            try
            {
                RunPunctualLightCoarseRangeCountJob(nativePunctualLightScreenSpaceBounds, buildContext.sliceLightCounts, sliceCount);

                var startIndex = 0;
                for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
                {
                    var lightCount = buildContext.sliceLightCounts[sliceIndex];
                    punctualLightCoarseRanges[sliceIndex] = new PunctualLightCoarseRange
                    {
                        startIndex = startIndex,
                        lightCount = lightCount,
                    };
                    buildContext.sliceStartOffsets[sliceIndex] = startIndex;
                    punctualLightCoarseRecordCount += lightCount;
                    startIndex += lightCount;
                }

                if (punctualLightCoarseRecordCount <= 0)
                    return;

                EnsurePunctualLightCoarseCapacity(sliceCount, punctualLightCoarseRecordCount);
                buildContext.AllocateRecords(punctualLightCoarseRecordCount);
                RunPunctualLightCoarseRecordBuildJob(
                    nativePunctualLightScreenSpaceBounds,
                    buildContext.sliceStartOffsets,
                    buildContext.punctualLightCoarseRecords,
                    sliceCount);
                NativeArray<PunctualLightCoarseRecord>.Copy(
                    buildContext.punctualLightCoarseRecords,
                    punctualLightCoarseRecords,
                    punctualLightCoarseRecordCount);
            }
            finally
            {
                buildContext.Dispose();
            }
        }

        private static void RunPunctualLightClusteredCullBuildJob(
            in PunctualLightScreenSpaceBoundsParameters parameters,
            in PunctualLightClusteredCullBuildContext buildContext)
        {
            var buildClusteredCullDataJob = new BuildPunctualLightClusteredCullDataJob
            {
                punctualLightCullData = buildContext.punctualLightCullData,
                punctualLightViewSpaceCullData = buildContext.punctualLightViewSpaceCullData,
                punctualLightScreenSpaceBounds = buildContext.punctualLightScreenSpaceBounds,
                parameters = new PunctualLightScreenSpaceBoundsJobParameters(parameters),
            };

            buildClusteredCullDataJob.Schedule(buildContext.punctualLightCullData.Length, 32).Complete();
        }

        private void ApplyPunctualLightClusteredCullBuildContext(in PunctualLightClusteredCullBuildContext buildContext)
        {
            for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
            {
                punctualLightViewSpaceCullData[lightIndex] = ConvertPunctualLightViewSpaceCullData(
                    buildContext.punctualLightViewSpaceCullData[lightIndex]);
                punctualLightScreenSpaceBounds[lightIndex] = ConvertPunctualLightScreenSpaceBounds(
                    buildContext.punctualLightScreenSpaceBounds[lightIndex]);
            }
        }

        private static void RunPunctualLightCoarseRangeCountJob(
            NativeArray<PunctualLightScreenSpaceBoundsRecord> nativePunctualLightScreenSpaceBounds,
            NativeArray<int> nativeSliceLightCounts,
            int sliceCount)
        {
            var countCoarseRangesJob = new CountPunctualLightCoarseRangesJob
            {
                punctualLightScreenSpaceBounds = nativePunctualLightScreenSpaceBounds,
                sliceLightCounts = nativeSliceLightCounts,
            };

            countCoarseRangesJob.Schedule(sliceCount, 1).Complete();
        }

        private static void RunPunctualLightCoarseRecordBuildJob(
            NativeArray<PunctualLightScreenSpaceBoundsRecord> nativePunctualLightScreenSpaceBounds,
            NativeArray<int> nativeSliceStartOffsets,
            NativeArray<PunctualLightCoarseRecord> nativePunctualLightCoarseRecords,
            int sliceCount)
        {
            var buildCoarseRecordsJob = new BuildPunctualLightCoarseRecordsJob
            {
                punctualLightScreenSpaceBounds = nativePunctualLightScreenSpaceBounds,
                sliceStartOffsets = nativeSliceStartOffsets,
                punctualLightCoarseRecords = nativePunctualLightCoarseRecords,
            };

            buildCoarseRecordsJob.Schedule(sliceCount, 1).Complete();
        }

        public override void Reset()
        {
            visibleLights = default;
            visibleReflectionProbes = default;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            directionalLightCount = 0;
            punctualLightCount = 0;
            punctualLightCoarseRangeCount = 0;
            punctualLightCoarseRecordCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
            punctualLightViewSpaceCullData = Array.Empty<PunctualLightViewSpaceCullData>();
            punctualLightScreenSpaceBounds = Array.Empty<PunctualLightScreenSpaceBounds>();
            punctualLightCoarseRanges = Array.Empty<PunctualLightCoarseRange>();
            punctualLightCoarseRecords = Array.Empty<PunctualLightCoarseRecord>();
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

        internal static PunctualLightClusteredLightListParameters CreatePunctualLightClusteredLightListParameters(
            Camera camera,
            int screenWidth,
            int screenHeight,
            int tileSize,
            int bigTileSize,
            int sliceCount,
            int punctualLightCount,
            int maxLightsPerCluster)
        {
            var screenSpaceBoundsParameters = CreatePunctualLightScreenSpaceBoundsParameters(
                camera,
                screenWidth,
                screenHeight,
                tileSize,
                sliceCount);
            var clusterCount = Mathf.Max(
                1,
                screenSpaceBoundsParameters.tileCountX
                * screenSpaceBoundsParameters.tileCountY
                * screenSpaceBoundsParameters.sliceCount);
            var perClusterLightCapacity = punctualLightCount > 0
                ? Mathf.Min(punctualLightCount, Mathf.Max(maxLightsPerCluster, 1))
                : 1;
            var lightIndexCapacity = 1;

            if (punctualLightCount > 0)
            {
                var rawLightIndexCapacity = (long)clusterCount * perClusterLightCapacity;
                lightIndexCapacity = Mathf.Max(1, (int)Math.Min(rawLightIndexCapacity, int.MaxValue));
            }

            bigTileSize = Mathf.Max(bigTileSize, tileSize);
            var bigTileCountX = Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)bigTileSize));
            var bigTileCountY = Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)bigTileSize));
            var bigTileCount = Mathf.Max(1, bigTileCountX * bigTileCountY);
            var bigTileLightIndexCapacity = 1;

            if (punctualLightCount > 0)
            {
                var rawBigTileLightIndexCapacity = (long)bigTileCount * punctualLightCount;
                bigTileLightIndexCapacity = Mathf.Max(1, (int)Math.Min(rawBigTileLightIndexCapacity, int.MaxValue));
            }

            return new PunctualLightClusteredLightListParameters(
                screenSpaceBoundsParameters,
                punctualLightCount,
                clusterCount,
                lightIndexCapacity,
                bigTileSize,
                bigTileCountX,
                bigTileCountY,
                bigTileCount,
                bigTileLightIndexCapacity);
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

            if (requiredCapacity > punctualLightViewSpaceCullData.Length)
                punctualLightViewSpaceCullData = new PunctualLightViewSpaceCullData[requiredCapacity];

            if (requiredCapacity > punctualLightScreenSpaceBounds.Length)
                punctualLightScreenSpaceBounds = new PunctualLightScreenSpaceBounds[requiredCapacity];
        }

        private void EnsurePunctualLightCoarseCapacity(int requiredRangeCapacity, int requiredRecordCapacity)
        {
            if (requiredRangeCapacity > punctualLightCoarseRanges.Length)
                punctualLightCoarseRanges = new PunctualLightCoarseRange[requiredRangeCapacity];

            if (requiredRecordCapacity > punctualLightCoarseRecords.Length)
                punctualLightCoarseRecords = new PunctualLightCoarseRecord[requiredRecordCapacity];
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
            var jobParameters = new PunctualLightScreenSpaceBoundsJobParameters(parameters);
            return ConvertPunctualLightScreenSpaceBounds(
                BuildPunctualLightScreenSpaceBoundsRecord(
                    BuildPunctualLightViewSpaceCullDataRecord(source, jobParameters.worldToViewMatrix),
                    jobParameters));
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
            {
                punctualLightCount = 0;
                punctualLightCoarseRangeCount = 0;
                punctualLightCoarseRecordCount = 0;
            }

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
            punctualLightCoarseRangeCount = 0;
            punctualLightCoarseRecordCount = 0;

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

        private static PunctualLightViewSpaceCullData ConvertPunctualLightViewSpaceCullData(PunctualLightViewSpaceCullDataRecord source)
        {
            return new PunctualLightViewSpaceCullData
            {
                positionVS = new Vector3(source.positionVS.x, source.positionVS.y, source.positionVS.z),
                range = source.range,
                directionVS = new Vector3(source.directionVS.x, source.directionVS.y, source.directionVS.z),
                cosOuterAngle = source.cosOuterAngle,
                cullingCenterVS = new Vector3(source.cullingCenterVS.x, source.cullingCenterVS.y, source.cullingCenterVS.z),
                cullingRadius = source.cullingRadius,
                lightType = source.lightType,
                radiusAtRange = source.radiusAtRange,
            };
        }

        private static PunctualLightScreenSpaceBoundsRecord ConvertPunctualLightScreenSpaceBoundsRecord(PunctualLightScreenSpaceBounds source)
        {
            return new PunctualLightScreenSpaceBoundsRecord
            {
                viewSpaceAabbMin = new float3(source.viewSpaceAabbMin.x, source.viewSpaceAabbMin.y, source.viewSpaceAabbMin.z),
                viewSpaceAabbMax = new float3(source.viewSpaceAabbMax.x, source.viewSpaceAabbMax.y, source.viewSpaceAabbMax.z),
                clipSpaceAabbMin = new float2(source.clipSpaceAabbMin.x, source.clipSpaceAabbMin.y),
                clipSpaceAabbMax = new float2(source.clipSpaceAabbMax.x, source.clipSpaceAabbMax.y),
                sliceMin = source.sliceMin,
                sliceMax = source.sliceMax,
                tileMinX = source.tileMinX,
                tileMaxX = source.tileMaxX,
                tileMinY = source.tileMinY,
                tileMaxY = source.tileMaxY,
                isValid = source.isValid,
            };
        }

        private static PunctualLightViewSpaceCullDataRecord BuildPunctualLightViewSpaceCullDataRecord(
            PunctualLightCullData source,
            float4x4 worldToViewMatrix)
        {
            var positionVS = TransformWorldToPositiveViewSpace(worldToViewMatrix, source.positionWS);
            var directionVS = NormalizeDirection(TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.directionWS), new float3(0.0f, 0.0f, 1.0f));
            var cullingCenterVS = TransformWorldToPositiveViewSpace(worldToViewMatrix, source.cullingCenterWS);

            return new PunctualLightViewSpaceCullDataRecord
            {
                positionVS = positionVS,
                range = source.range,
                directionVS = directionVS,
                cosOuterAngle = source.cosOuterAngle,
                cullingCenterVS = cullingCenterVS,
                cullingRadius = source.cullingRadius,
                lightType = source.lightType,
                radiusAtRange = source.radiusAtRange,
            };
        }

        private static PunctualLightScreenSpaceBoundsRecord BuildPunctualLightScreenSpaceBoundsRecord(
            PunctualLightViewSpaceCullDataRecord source,
            in PunctualLightScreenSpaceBoundsJobParameters parameters)
        {
            var radius = math.max(source.cullingRadius, 0.0f);
            var radiusVector = new float3(radius, radius, radius);
            var viewSpaceAabbMin = source.cullingCenterVS - radiusVector;
            var viewSpaceAabbMax = source.cullingCenterVS + radiusVector;
            var bounds = new PunctualLightScreenSpaceBoundsRecord
            {
                viewSpaceAabbMin = viewSpaceAabbMin,
                viewSpaceAabbMax = viewSpaceAabbMax,
            };

            if (radius <= 0.0f)
                return bounds;

            if (!TryGetPunctualLightSliceRange(viewSpaceAabbMin.z, viewSpaceAabbMax.z, parameters, out var sliceMin, out var sliceMax))
                return bounds;

            if (!TryGetPunctualLightClipSpaceRect(source.cullingCenterVS, radius, parameters, out var clipSpaceAabbMin, out var clipSpaceAabbMax))
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

        private static float3 TransformWorldVectorToPositiveViewSpace(float4x4 worldToViewMatrix, Vector3 worldDirection)
        {
            var viewDirection = math.mul(
                worldToViewMatrix,
                new float4(worldDirection.x, worldDirection.y, worldDirection.z, 0.0f));
            return new float3(viewDirection.x, viewDirection.y, -viewDirection.z);
        }

        private static float3 NormalizeDirection(float3 direction, float3 fallback)
        {
            var lengthSq = math.lengthsq(direction);
            if (lengthSq <= 1e-6f)
                return fallback;

            return direction * math.rsqrt(lengthSq);
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
