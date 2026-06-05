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
    public partial class VividLightData : ContextItem
    {
        // Covers the default Grid volume and the current default Onion outer radius before sphere-vs-box rejection.
        private const float DefaultReGIRCollectionBoxHalfExtent = 80.0f;
        private static readonly bool s_CanReadVisibleReflectionProbeEntityId =
            UnsafeUtility.SizeOf<VisibleReflectionProbe>() == UnsafeUtility.SizeOf<VisibleReflectionProbeEntityIdLayout>();

        [StructLayout(LayoutKind.Sequential)]
        private struct VisibleReflectionProbeEntityIdLayout
        {
            public Bounds bounds;
            public Matrix4x4 localToWorldMatrix;
            public Vector4 hdrData;
            public Vector3 center;
            public float blendDistance;
            public int importance;
            public int boxProjection;
            public EntityId entityId;
            public EntityId textureId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DirectionalLightData
        {
            public Vector3 directionWS;
            public float shadowStrength;
            public Vector3 color;
            public uint renderingLayerMask;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public float volumetricFadeDistance;
            public uint affectVolumetric;

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
            public Vector3 rightWS;
            public float angleOffset;
            public Vector3 upWS;
            public float shapeRadiusSquared;
            public Vector2 projectorSize;
            public float rangeAttenuationScale;
            public float rangeAttenuationBias;
            public float shadowStrength;
            public uint renderingLayerMask;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public float volumetricFadeDistance;
            public uint affectVolumetric;
            public Vector2 padding;

            internal static int Stride => Marshal.SizeOf<PunctualLightData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AreaLightData
        {
            public Vector3 positionWS;
            public float rangeAttenuationScale;
            public Vector3 color;
            public uint lightType;
            public Vector3 forwardWS;
            public float rangeAttenuationBias;
            public Vector3 rightWS;
            public float width;
            public Vector3 upWS;
            public float height;
            public uint renderingLayerMask;
            public float range;
            public float cosBarnDoorAngle;
            public float barnDoorLength;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public float volumetricFadeDistance;
            public uint affectVolumetric;

            internal static int Stride => Marshal.SizeOf<AreaLightData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReflectionProbeData
        {
            public Vector3 positionWS;
            public float blendDistance;
            public Vector3 extents;
            public uint isBoxProjection;
            public Vector3 rightWS;
            public float importance;
            public Vector3 upWS;
            public float weight;
            public Vector3 forwardWS;
            public float padding0;
            public Vector3 capturePositionWS;
            public float padding1;
            public Vector4 hdrData;
            public Vector4 atlasScaleOffset;
            public Vector4 atlasIndexAndSlice;
            public Vector3 blendDistancePositive;
            public float multiplier;
            public Vector3 blendDistanceNegative;
            public uint isProjectionInfinite;
            public Vector3 boxSideFadePositive;
            public float rangeCompressionFactor;
            public Vector3 boxSideFadeNegative;
            public float padding2;
            public Vector3 proxyPositionWS;
            public float padding3;
            public Vector3 proxyExtents;
            public float padding4;

            internal static int Stride => Marshal.SizeOf<ReflectionProbeData>();
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
            public Vector3 rightWS;
            public float projectorWidth;
            public Vector3 upWS;
            public float projectorHeight;
            public Vector3 cullingCenterWS;
            public float cullingRadius;
            public uint affectVolumetric;

            internal static int Stride => Marshal.SizeOf<PunctualLightCullData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SFiniteLightBound
        {
            public Vector4 boxAxisX;   // xyz = axis, w = scaleXY
            public Vector4 boxAxisY;   // xyz = axis, w = radius
            public Vector3 boxAxisZ;
            public Vector3 center;

            internal static int Stride => Marshal.SizeOf<SFiniteLightBound>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LightVolumeData
        {
            public Vector3 lightPos;
            public uint lightVolume;
            public Vector3 lightAxisX;
            public uint lightCategory;
            public Vector3 lightAxisY;
            public float radiusSq;
            public Vector3 lightAxisZ;
            public float cotan;
            public Vector3 boxInnerDist;
            public uint featureFlags;
            public Vector3 boxInvRange;
            public int affectVolumetric;

            internal static int Stride => Marshal.SizeOf<LightVolumeData>();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DecalClusterData
        {
            public Matrix4x4 worldToDecal;
            public Vector4 baseColor;
            public uint baseColorTextureIndex;
            public uint normalTextureIndex;
            public uint metallicTextureIndex;
            public uint roughnessTextureIndex;
            public float blendDistance;
            public float metallic;
            public float roughness;
            public float padding;

            internal static int Stride => Marshal.SizeOf<DecalClusterData>();
        }

        private const uint HdrpLightCategoryPunctual = 0u;
        private const uint HdrpLightCategoryArea = 1u;
        private const uint HdrpLightCategoryEnv = 2u;
        private const uint HdrpLightCategoryDecal = 3u;
        private const uint HdrpLightFeatureFlagsPunctual = 4096u;
        private const uint HdrpLightFeatureFlagsArea = 8192u;
        private const uint HdrpLightFeatureFlagsEnv = 32768u;
        private const uint HdrpLightFeatureFlagsDecal = 524288u;
        private const uint HdrpLightVolumeTypeCone = 0u;
        private const uint HdrpLightVolumeTypeSphere = 1u;
        private const uint HdrpLightVolumeTypeBox = 2u;
        private const float HdrpBoxCullingExtentThreshold = 0.01f;
        private const uint VividPunctualLightTypePoint = 0u;
        private const uint VividPunctualLightTypeSpot = 1u;
        private const uint VividPunctualLightTypeProjectorBox = 2u;

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
        private struct AreaLightCandidate
        {
            public AreaLightData lightData;
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
            public float3 rightVS;
            public float projectorWidth;
            public float3 upVS;
            public float projectorHeight;
        }

        private struct PunctualLightClusteredCullBuildContext : IDisposable
        {
            public NativeArray<PunctualLightCullData> punctualLightCullData;
            public NativeArray<SFiniteLightBound> punctualLightBounds;
            public NativeArray<LightVolumeData> punctualLightVolumeData;

            public PunctualLightClusteredCullBuildContext(int punctualLightCount)
            {
                punctualLightCullData = new NativeArray<PunctualLightCullData>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                punctualLightBounds = new NativeArray<SFiniteLightBound>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                punctualLightVolumeData = new NativeArray<LightVolumeData>(
                    punctualLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (punctualLightVolumeData.IsCreated)
                    punctualLightVolumeData.Dispose();

                if (punctualLightBounds.IsCreated)
                    punctualLightBounds.Dispose();

                if (punctualLightCullData.IsCreated)
                    punctualLightCullData.Dispose();
            }
        }

        private struct AreaLightClusteredCullBuildContext : IDisposable
        {
            public NativeArray<AreaLightData> areaLightData;
            public NativeArray<SFiniteLightBound> areaLightBounds;
            public NativeArray<LightVolumeData> areaLightVolumeData;

            public AreaLightClusteredCullBuildContext(int areaLightCount)
            {
                areaLightData = new NativeArray<AreaLightData>(
                    areaLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                areaLightBounds = new NativeArray<SFiniteLightBound>(
                    areaLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                areaLightVolumeData = new NativeArray<LightVolumeData>(
                    areaLightCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (areaLightVolumeData.IsCreated)
                    areaLightVolumeData.Dispose();

                if (areaLightBounds.IsCreated)
                    areaLightBounds.Dispose();

                if (areaLightData.IsCreated)
                    areaLightData.Dispose();
            }
        }


        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes;
        public DirectionalLightData[] directionalLights = Array.Empty<DirectionalLightData>();
        public PunctualLightData[] punctualLights = Array.Empty<PunctualLightData>();
        public AreaLightData[] areaLights = Array.Empty<AreaLightData>();
        public ReflectionProbeData[] reflectionProbes = Array.Empty<ReflectionProbeData>();
        public PunctualLightCullData[] punctualLightCullData = Array.Empty<PunctualLightCullData>();
        public SFiniteLightBound[] punctualLightBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] punctualLightVolumeData = Array.Empty<LightVolumeData>();
        public SFiniteLightBound[] areaLightBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] areaLightVolumeData = Array.Empty<LightVolumeData>();
        public SFiniteLightBound[] reflectionProbeBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] reflectionProbeVolumeData = Array.Empty<LightVolumeData>();
        public VividReGIRLightData[] reGIRLights = Array.Empty<VividReGIRLightData>();
        public DecalClusterData[] decalClusterData = Array.Empty<DecalClusterData>();
        public SFiniteLightBound[] decalBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] decalVolumeData = Array.Empty<LightVolumeData>();
        public int decalCount;
        public int mainLightIndex;
        public EntityId mainLightEntityId;
        public int directionalLightCount;
        public int punctualLightCount;
        public int areaLightCount;
        public int reflectionProbeCount;
        public int reGIRLightCount;
        public int mainDirectionalLightIndex;
        public EntityId mainDirectionalLightEntityId;

        // LightGrid/ReGIR candidate builds run on Burst jobs scheduled in Update and completed by LightGrid/ReGIR Prepare.
        // The fields below are owned by VividLightData while the job is in flight; do not access from outside CompleteLightGridPrepare.
        private NativeList<VisibleLightRenderDataRecord> m_LightGridVisibleLightRecords;
        private NativeList<DirectionalLightCandidate> m_LightGridDirectionalCandidates;
        private NativeList<PunctualLightCandidate> m_LightGridPunctualCandidates;
        private NativeList<AreaLightCandidate> m_LightGridAreaCandidates;
        private NativeList<SFiniteLightBound> m_LightGridPunctualLightBounds;
        private NativeList<LightVolumeData> m_LightGridPunctualLightVolumeData;
        private NativeList<SFiniteLightBound> m_LightGridAreaLightBounds;
        private NativeList<LightVolumeData> m_LightGridAreaLightVolumeData;
        private NativeList<VividLightRenderData> m_ReGIRSceneLightSourceRecords;
        private NativeList<VividLightRenderData> m_ReGIRSceneLightRecords;
        private NativeList<VividReGIRLightData> m_LightGridReGIRLights;
        private JobHandle m_LightGridJobHandle;
        private bool m_LightGridJobScheduled;
        private bool m_LightGridClusteredCullDataPrepared;

        public bool hasVisibleLights => visibleLights.IsCreated && visibleLights.Length > 0;

        public bool hasVisibleReflectionProbes => visibleReflectionProbes.IsCreated && visibleReflectionProbes.Length > 0;

        public bool hasMainLight => IsValidLightIndex(mainLightIndex);

        public bool hasDirectionalLights => directionalLightCount > 0;

        public bool hasPunctualLights => punctualLightCount > 0;

        public bool hasAreaLights => areaLightCount > 0;

        public bool hasReflectionProbes => reflectionProbeCount > 0;

        public bool hasMainDirectionalLight => IsValidDirectionalLightIndex(mainDirectionalLightIndex);

        public int visibleLightCount => hasVisibleLights ? visibleLights.Length : 0;

        public int additionalLightsCount => hasVisibleLights ? visibleLights.Length - (hasMainLight ? 1 : 0) : 0;

        public int visibleReflectionProbeCount => hasVisibleReflectionProbes ? visibleReflectionProbes.Length : 0;

        public int additionalDirectionalLightsCount => hasDirectionalLights ? directionalLightCount - (hasMainDirectionalLight ? 1 : 0) : 0;

        public VisibleLight mainVisibleLight => hasMainLight ? visibleLights[mainLightIndex] : default;

        public Light mainLight => hasMainLight ? visibleLights[mainLightIndex].light : null;

        public DirectionalLightData mainDirectionalLight => hasMainDirectionalLight ? directionalLights[mainDirectionalLightIndex] : default;

        internal void Update(CullingResults cullingResults, Matrix4x4 worldToViewMatrix)
        {
            // Defensive: previous frame's LightGridPass should have drained this, but bail-outs (camera early-return,
            // pipeline swap) can leave the handle live. Drain before mutating any state read by the job.
            using (RenderPassProfilingUtility.InitializeContextLightDataDrainMarker.Auto())
            {
                DrainLightGridPrepare();
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataInputsMarker.Auto())
            {
                visibleLights = cullingResults.visibleLights;
                visibleReflectionProbes = cullingResults.visibleReflectionProbes;
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataVisibleLightsMarker.Auto())
            {
                UpdateVisibleLightData(visibleLights, RenderSettings.sun, worldToViewMatrix);
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbesMarker.Auto())
            {
                UpdateVisibleReflectionProbeData(visibleReflectionProbes, ResolveCameraPositionWS(worldToViewMatrix));
            }
        }

        internal void Update(CullingResults cullingResults)
        {
            Update(cullingResults, Matrix4x4.identity);
        }

        // Called from LightGridPass.Prepare just before LightGrid reads punctualLightCount/areaLightCount/punctualLights/areaLights.
        internal void CompleteLightGridPrepare()
        {
            CompleteLightCollectionPrepare();
        }

        // Called from ReGIRGridBuildPass.Prepare before reading reGIRLightCount/reGIRLights.
        internal void CompleteReGIRPrepare()
        {
            CompleteLightCollectionPrepare();
        }

        private void CompleteLightCollectionPrepare()
        {
            if (!m_LightGridJobScheduled)
                return;

            m_LightGridJobHandle.Complete();
            m_LightGridJobHandle = default;
            m_LightGridJobScheduled = false;

            ApplyPunctualLightCandidates(m_LightGridPunctualCandidates);
            ApplyAreaLightCandidates(m_LightGridAreaCandidates);
            ApplyReGIRLightCandidates(m_LightGridReGIRLights);
            ApplyPunctualLightClusteredCullData(
                m_LightGridPunctualLightBounds,
                m_LightGridPunctualLightVolumeData);
            ApplyAreaLightClusteredCullData(
                m_LightGridAreaLightBounds,
                m_LightGridAreaLightVolumeData);
            m_LightGridClusteredCullDataPrepared = true;

            ClearLightGridBuffers();
        }

        private void DrainLightGridPrepare()
        {
            if (m_LightGridJobScheduled)
            {
                m_LightGridJobHandle.Complete();
                m_LightGridJobHandle = default;
                m_LightGridJobScheduled = false;
            }

            m_LightGridClusteredCullDataPrepared = false;
            ClearLightGridBuffers();
        }

        internal void ReleaseLightGridNativeResources()
        {
            if (m_LightGridJobScheduled)
            {
                m_LightGridJobHandle.Complete();
                m_LightGridJobHandle = default;
                m_LightGridJobScheduled = false;
            }

            m_LightGridClusteredCullDataPrepared = false;
            DisposeLightGridBuffers();
        }

        private void ClearLightGridBuffers()
        {
            ClearLightGridBuffer(ref m_LightGridVisibleLightRecords);
            ClearLightGridBuffer(ref m_LightGridDirectionalCandidates);
            ClearLightGridBuffer(ref m_LightGridPunctualCandidates);
            ClearLightGridBuffer(ref m_LightGridAreaCandidates);
            ClearLightGridBuffer(ref m_LightGridPunctualLightBounds);
            ClearLightGridBuffer(ref m_LightGridPunctualLightVolumeData);
            ClearLightGridBuffer(ref m_LightGridAreaLightBounds);
            ClearLightGridBuffer(ref m_LightGridAreaLightVolumeData);
            ClearLightGridBuffer(ref m_ReGIRSceneLightSourceRecords);
            ClearLightGridBuffer(ref m_ReGIRSceneLightRecords);
            ClearLightGridBuffer(ref m_LightGridReGIRLights);
        }

        private void DisposeLightGridBuffers()
        {
            DisposeLightGridBuffer(ref m_LightGridVisibleLightRecords);
            DisposeLightGridBuffer(ref m_LightGridDirectionalCandidates);
            DisposeLightGridBuffer(ref m_LightGridPunctualCandidates);
            DisposeLightGridBuffer(ref m_LightGridAreaCandidates);
            DisposeLightGridBuffer(ref m_LightGridPunctualLightBounds);
            DisposeLightGridBuffer(ref m_LightGridPunctualLightVolumeData);
            DisposeLightGridBuffer(ref m_LightGridAreaLightBounds);
            DisposeLightGridBuffer(ref m_LightGridAreaLightVolumeData);
            DisposeLightGridBuffer(ref m_ReGIRSceneLightSourceRecords);
            DisposeLightGridBuffer(ref m_ReGIRSceneLightRecords);
            DisposeLightGridBuffer(ref m_LightGridReGIRLights);
        }

        internal void UpdateFiniteLightClusteredCullData(Matrix4x4 worldToViewMatrix)
        {
            if (m_LightGridJobScheduled)
                CompleteLightGridPrepare();

            if (!m_LightGridClusteredCullDataPrepared)
            {
                BuildPunctualLightClusteredCullingData(worldToViewMatrix);
                BuildAreaLightClusteredCullingData(worldToViewMatrix);
            }

            BuildReflectionProbeClusteredCullingData(worldToViewMatrix);
            BuildDecalClusteredCullingData(worldToViewMatrix);
        }

        internal void UpdateDecalClusteredCullData(Matrix4x4 worldToViewMatrix)
        {
            BuildDecalClusteredCullingData(worldToViewMatrix);
        }

        internal void UpdatePunctualLightClusteredCullData(Matrix4x4 worldToViewMatrix)
        {
            BuildPunctualLightClusteredCullingData(worldToViewMatrix);
        }

        internal void UpdateAreaLightClusteredCullData(Matrix4x4 worldToViewMatrix)
        {
            BuildAreaLightClusteredCullingData(worldToViewMatrix);
        }

        internal void UpdateReflectionProbeClusteredCullData(Matrix4x4 worldToViewMatrix)
        {
            BuildReflectionProbeClusteredCullingData(worldToViewMatrix);
        }

        private void BuildPunctualLightClusteredCullingData(Matrix4x4 worldToViewMatrix)
        {
            EnsurePunctualLightCapacity(punctualLightCount);

            if (punctualLightCount <= 0)
                return;

            using var buildContext = new PunctualLightClusteredCullBuildContext(punctualLightCount);
            NativeArray<PunctualLightCullData>.Copy(punctualLightCullData, buildContext.punctualLightCullData, punctualLightCount);
            RunPunctualLightClusteredCullBuildJob(worldToViewMatrix, buildContext);
            ApplyPunctualLightClusteredCullBuildContext(buildContext);
        }

        private static void RunPunctualLightClusteredCullBuildJob(
            Matrix4x4 worldToViewMatrix,
            in PunctualLightClusteredCullBuildContext buildContext)
        {
            var buildClusteredCullDataJob = new BuildPunctualLightClusteredCullDataJob
            {
                punctualLightCullData = buildContext.punctualLightCullData,
                punctualLightBounds = buildContext.punctualLightBounds,
                punctualLightVolumeData = buildContext.punctualLightVolumeData,
                worldToViewMatrix = worldToViewMatrix,
            };

            buildClusteredCullDataJob.Schedule(buildContext.punctualLightCullData.Length, 32).Complete();
        }

        private void ApplyPunctualLightClusteredCullBuildContext(in PunctualLightClusteredCullBuildContext buildContext)
        {
            for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
            {
                punctualLightBounds[lightIndex] = buildContext.punctualLightBounds[lightIndex];
                punctualLightVolumeData[lightIndex] = buildContext.punctualLightVolumeData[lightIndex];
            }
        }

        private void BuildAreaLightClusteredCullingData(Matrix4x4 worldToViewMatrix)
        {
            EnsureAreaLightCapacity(areaLightCount);

            if (areaLightCount <= 0)
                return;

            using var buildContext = new AreaLightClusteredCullBuildContext(areaLightCount);
            NativeArray<AreaLightData>.Copy(areaLights, buildContext.areaLightData, areaLightCount);
            RunAreaLightClusteredCullBuildJob(worldToViewMatrix, buildContext);
            ApplyAreaLightClusteredCullBuildContext(buildContext);
        }

        private static void RunAreaLightClusteredCullBuildJob(
            Matrix4x4 worldToViewMatrix,
            in AreaLightClusteredCullBuildContext buildContext)
        {
            var buildClusteredCullDataJob = new BuildAreaLightClusteredCullDataJob
            {
                areaLightData = buildContext.areaLightData,
                areaLightBounds = buildContext.areaLightBounds,
                areaLightVolumeData = buildContext.areaLightVolumeData,
                worldToViewMatrix = worldToViewMatrix,
            };

            buildClusteredCullDataJob.Schedule(buildContext.areaLightData.Length, 32).Complete();
        }

        private void ApplyAreaLightClusteredCullBuildContext(in AreaLightClusteredCullBuildContext buildContext)
        {
            for (var lightIndex = 0; lightIndex < areaLightCount; lightIndex++)
            {
                areaLightBounds[lightIndex] = buildContext.areaLightBounds[lightIndex];
                areaLightVolumeData[lightIndex] = buildContext.areaLightVolumeData[lightIndex];
            }
        }

        private void BuildReflectionProbeClusteredCullingData(Matrix4x4 worldToViewMatrix)
        {
            EnsureReflectionProbeCapacity(reflectionProbeCount);

            if (reflectionProbeCount <= 0)
                return;

            var viewMatrix = (float4x4)worldToViewMatrix;

            for (var probeIndex = 0; probeIndex < reflectionProbeCount; probeIndex++)
            {
                BuildReflectionProbeVolumeDataAndBound(
                    reflectionProbes[probeIndex],
                    viewMatrix,
                    out var lightVolumeData,
                    out var lightBound);
                reflectionProbeVolumeData[probeIndex] = lightVolumeData;
                reflectionProbeBounds[probeIndex] = lightBound;
            }
        }

        private void BuildDecalClusteredCullingData(Matrix4x4 worldToViewMatrix)
        {
            EnsureDecalCapacity(decalCount);

            if (decalCount <= 0)
                return;

            var viewMatrix = (float4x4)worldToViewMatrix;

            for (int i = 0; i < decalCount; i++)
            {
                DecalClusterData decal = decalClusterData[i];
                Matrix4x4 decalToWorld = decal.worldToDecal.inverse;

                float3 positionWS = new float3(decalToWorld.m03, decalToWorld.m13, decalToWorld.m23);
                float3 axisXWS = math.normalize(new float3(decalToWorld.m00, decalToWorld.m10, decalToWorld.m20));
                float3 axisYWS = math.normalize(new float3(decalToWorld.m01, decalToWorld.m11, decalToWorld.m21));
                float3 axisZWS = math.normalize(new float3(decalToWorld.m02, decalToWorld.m12, decalToWorld.m22));
                float3 scaleWS = new float3(
                    math.length(new float3(decalToWorld.m00, decalToWorld.m10, decalToWorld.m20)),
                    math.length(new float3(decalToWorld.m01, decalToWorld.m11, decalToWorld.m21)),
                    math.length(new float3(decalToWorld.m02, decalToWorld.m12, decalToWorld.m22)));
                float3 halfExtents = scaleWS * 0.5f;

                float3 positionVS = math.mul(viewMatrix, new float4(positionWS, 1.0f)).xyz;
                positionVS.z = -positionVS.z;
                float3 axisXVS = TransformWorldVectorToPositiveViewSpace(viewMatrix, new Vector3(axisXWS.x, axisXWS.y, axisXWS.z));
                float3 axisYVS = TransformWorldVectorToPositiveViewSpace(viewMatrix, new Vector3(axisYWS.x, axisYWS.y, axisYWS.z));
                float3 axisZVS = TransformWorldVectorToPositiveViewSpace(viewMatrix, new Vector3(axisZWS.x, axisZWS.y, axisZWS.z));

                float radius = math.length(halfExtents);

                decalBounds[i] = new SFiniteLightBound
                {
                    center = new Vector3(positionVS.x, positionVS.y, positionVS.z),
                    boxAxisX = new Vector4(axisXVS.x * halfExtents.x, axisXVS.y * halfExtents.x, axisXVS.z * halfExtents.x, 1.0f),
                    boxAxisY = new Vector4(axisYVS.x * halfExtents.y, axisYVS.y * halfExtents.y, axisYVS.z * halfExtents.y, radius),
                    boxAxisZ = new Vector3(axisZVS.x * halfExtents.z, axisZVS.y * halfExtents.z, axisZVS.z * halfExtents.z),
                };

                decalVolumeData[i] = new LightVolumeData
                {
                    lightPos = new Vector3(positionVS.x, positionVS.y, positionVS.z),
                    lightVolume = HdrpLightVolumeTypeBox,
                    lightAxisX = new Vector3(axisXVS.x, axisXVS.y, axisXVS.z),
                    lightCategory = HdrpLightCategoryDecal,
                    lightAxisY = new Vector3(axisYVS.x, axisYVS.y, axisYVS.z),
                    radiusSq = radius * radius,
                    lightAxisZ = new Vector3(axisZVS.x, axisZVS.y, axisZVS.z),
                    cotan = 0.0f,
                    boxInnerDist = new Vector3(halfExtents.x, halfExtents.y, halfExtents.z),
                    featureFlags = HdrpLightFeatureFlagsDecal,
                    boxInvRange = new Vector3(1.0f / math.max(halfExtents.x, 1e-5f), 1.0f / math.max(halfExtents.y, 1e-5f), 1.0f / math.max(halfExtents.z, 1e-5f)),
                    affectVolumetric = 0,
                };
            }
        }

        private void EnsureDecalCapacity(int requiredCapacity)
        {
            if (requiredCapacity > decalClusterData.Length)
                decalClusterData = new DecalClusterData[requiredCapacity];

            if (requiredCapacity > decalBounds.Length)
                decalBounds = new SFiniteLightBound[requiredCapacity];

            if (requiredCapacity > decalVolumeData.Length)
                decalVolumeData = new LightVolumeData[requiredCapacity];
        }

        public override void Reset()
        {
            DrainLightGridPrepare();

            visibleLights = default;
            visibleReflectionProbes = default;
            m_LightGridClusteredCullDataPrepared = false;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            directionalLightCount = 0;
            punctualLightCount = 0;
            areaLightCount = 0;
            reflectionProbeCount = 0;
            reGIRLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
            areaLights = Array.Empty<AreaLightData>();
            reflectionProbes = Array.Empty<ReflectionProbeData>();
            reGIRLights = Array.Empty<VividReGIRLightData>();
            punctualLightBounds = Array.Empty<SFiniteLightBound>();
            punctualLightVolumeData = Array.Empty<LightVolumeData>();
            areaLightBounds = Array.Empty<SFiniteLightBound>();
            areaLightVolumeData = Array.Empty<LightVolumeData>();
            reflectionProbeBounds = Array.Empty<SFiniteLightBound>();
            reflectionProbeVolumeData = Array.Empty<LightVolumeData>();
            decalClusterData = Array.Empty<DecalClusterData>();
            decalBounds = Array.Empty<SFiniteLightBound>();
            decalVolumeData = Array.Empty<LightVolumeData>();
            decalCount = 0;
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

            if (requiredCapacity > punctualLightBounds.Length)
                punctualLightBounds = new SFiniteLightBound[requiredCapacity];

            if (requiredCapacity > punctualLightVolumeData.Length)
                punctualLightVolumeData = new LightVolumeData[requiredCapacity];
        }

        private void EnsureAreaLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity > areaLights.Length)
                areaLights = new AreaLightData[requiredCapacity];

            if (requiredCapacity > areaLightBounds.Length)
                areaLightBounds = new SFiniteLightBound[requiredCapacity];

            if (requiredCapacity > areaLightVolumeData.Length)
                areaLightVolumeData = new LightVolumeData[requiredCapacity];
        }

        private void EnsureReflectionProbeCapacity(int requiredCapacity)
        {
            if (requiredCapacity > reflectionProbes.Length)
                reflectionProbes = new ReflectionProbeData[requiredCapacity];

            if (requiredCapacity > reflectionProbeBounds.Length)
                reflectionProbeBounds = new SFiniteLightBound[requiredCapacity];

            if (requiredCapacity > reflectionProbeVolumeData.Length)
                reflectionProbeVolumeData = new LightVolumeData[requiredCapacity];
        }

        private void EnsureReGIRLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity > reGIRLights.Length)
                reGIRLights = new VividReGIRLightData[requiredCapacity];
        }


        private void UpdateVisibleLightData(
            NativeArray<VisibleLight> visibleLights,
            Light sunLight,
            Matrix4x4 worldToViewMatrix)
        {
            directionalLightCount = 0;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
            punctualLightCount = 0;
            areaLightCount = 0;
            reGIRLightCount = 0;
            m_LightGridClusteredCullDataPrepared = false;

            using (RenderPassProfilingUtility.InitializeContextLightDataSceneLightCompleteMarker.Auto())
            using (RenderPassProfilingUtility.InitializeContextSceneLightCompleteMarker.Auto())
            {
                VividLightRenderDatabase.instance.CompleteSceneLightPrepare();
            }

            var sceneLightData = VividLightRenderDatabase.instance.sceneLightData;
            var visibleLightCount = visibleLights.IsCreated ? visibleLights.Length : 0;
            var sceneLightCount = sceneLightData.Count;
            var lightCapacity = Mathf.Max(Mathf.Max(visibleLightCount, sceneLightCount), 1);
            using (RenderPassProfilingUtility.InitializeContextLightDataEnsureBuffersMarker.Auto())
            {
                EnsureLightGridBufferCapacity(lightCapacity);
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataCollectVisibleMarker.Auto())
            {
                if (visibleLightCount > 0)
                    CollectVisibleLightRenderDataRecords(visibleLights, m_LightGridVisibleLightRecords);
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataCollectSceneMarker.Auto())
            {
                CollectReGIRSceneLightSourceRecords(sceneLightData, m_ReGIRSceneLightSourceRecords);
            }

            if (m_LightGridVisibleLightRecords.Length == 0 && m_ReGIRSceneLightSourceRecords.Length == 0)
            {
                ClearLightGridBuffers();
                return;
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataDirectionalMarker.Auto())
            {
                CollectDirectionalLightCandidatesAndApply(m_LightGridVisibleLightRecords, sunLight);
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataScheduleMarker.Auto())
            {
                var collectReGIRJobHandle = default(JobHandle);
                if (m_ReGIRSceneLightSourceRecords.Length > 0)
                {
                    var collectReGIRSceneLightJob = new CollectReGIRSceneLightRenderDataRecordsJob
                    {
                        sceneLightRenderDataRecords = m_ReGIRSceneLightSourceRecords.AsArray(),
                        reGIRSceneLightRenderDataRecords = m_ReGIRSceneLightRecords,
                        collectionBoxCenterWS = ResolveCameraPositionWS(worldToViewMatrix),
                        collectionBoxHalfExtents = new Vector3(
                            DefaultReGIRCollectionBoxHalfExtent,
                            DefaultReGIRCollectionBoxHalfExtent,
                            DefaultReGIRCollectionBoxHalfExtent),
                    };
                    collectReGIRJobHandle = collectReGIRSceneLightJob.Schedule();
                }

                var buildLightGridJob = new BuildLightGridLightCandidatesJob
                {
                    visibleLightRenderDataRecords = m_LightGridVisibleLightRecords.AsArray(),
                    reGIRSceneLightRenderDataRecords = m_ReGIRSceneLightRecords.AsDeferredJobArray(),
                    punctualLights = m_LightGridPunctualCandidates,
                    areaLights = m_LightGridAreaCandidates,
                    reGIRLights = m_LightGridReGIRLights,
                    punctualLightBounds = m_LightGridPunctualLightBounds,
                    punctualLightVolumeData = m_LightGridPunctualLightVolumeData,
                    areaLightBounds = m_LightGridAreaLightBounds,
                    areaLightVolumeData = m_LightGridAreaLightVolumeData,
                    worldToViewMatrix = worldToViewMatrix,
                };
                m_LightGridJobHandle = buildLightGridJob.Schedule(collectReGIRJobHandle);
                m_LightGridJobScheduled = true;
                JobHandle.ScheduleBatchedJobs();
            }
        }

        private void UpdateVisibleReflectionProbeData(
            NativeArray<VisibleReflectionProbe> visibleReflectionProbes,
            Vector3 cameraPositionWS)
        {
            reflectionProbeCount = 0;
            m_LightGridClusteredCullDataPrepared = false;

            if (!visibleReflectionProbes.IsCreated || visibleReflectionProbes.Length == 0)
                return;

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeEnsureCapacityMarker.Auto())
            {
                EnsureReflectionProbeCapacity(visibleReflectionProbes.Length);
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeBuildMarker.Auto())
            {
                for (var probeIndex = 0; probeIndex < visibleReflectionProbes.Length; probeIndex++)
                {
                    if (!TryCreateReflectionProbeData(visibleReflectionProbes[probeIndex], cameraPositionWS, out var reflectionProbeData))
                        continue;

                    using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeStoreMarker.Auto())
                    {
                        reflectionProbes[reflectionProbeCount] = reflectionProbeData;
                        reflectionProbeCount++;
                    }
                }
            }
        }

        internal void UpdateReflectionProbeAtlasData(
            CommandBuffer cmd,
            VividReflectionProbeTextureCache reflectionProbeTextureCache)
        {
            if (reflectionProbeTextureCache == null || cmd == null || reflectionProbeCount <= 0)
                return;

            var compactProbeIndex = 0;
            if (!visibleReflectionProbes.IsCreated || visibleReflectionProbes.Length == 0)
                return;

            for (var visibleProbeIndex = 0; visibleProbeIndex < visibleReflectionProbes.Length; visibleProbeIndex++)
            {
                var visibleReflectionProbe = visibleReflectionProbes[visibleProbeIndex];
                if (!IsReflectionProbeSpatiallyValid(visibleReflectionProbe))
                    continue;

                if (compactProbeIndex >= reflectionProbeCount)
                    break;

                var reflectionProbeData = reflectionProbes[compactProbeIndex];
                var scaleOffset = Vector4.zero;
                var fetchIndex = -1;
                var texture = visibleReflectionProbe.texture;

                if (texture != null && texture.dimension == TextureDimension.Cube)
                    scaleOffset = reflectionProbeTextureCache.FetchCubeReflectionProbe(
                        cmd,
                        texture,
                        visibleReflectionProbe.hdrData,
                        out fetchIndex);

                reflectionProbeData.atlasScaleOffset = scaleOffset;
                reflectionProbeData.atlasIndexAndSlice = new Vector4(
                    Mathf.Max(fetchIndex, -1),
                    fetchIndex >= 0 ? 0.0f : -1.0f,
                    0.0f,
                    0.0f);
                reflectionProbes[compactProbeIndex] = reflectionProbeData;
                compactProbeIndex++;
            }
        }

        private void EnsureLightGridBufferCapacity(int lightCapacity)
        {
            // VisibleLightRenderDataRecord must be built on the main thread (touches managed Light + database).
            // It is consumed by both the synchronous directional pass below and the deferred LightGrid Burst job.
            EnsureLightGridBufferCapacity(ref m_LightGridVisibleLightRecords, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridDirectionalCandidates, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridPunctualCandidates, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridAreaCandidates, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridReGIRLights, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_ReGIRSceneLightSourceRecords, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_ReGIRSceneLightRecords, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridPunctualLightBounds, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridPunctualLightVolumeData, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridAreaLightBounds, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridAreaLightVolumeData, lightCapacity);
        }

        private static void EnsureLightGridBufferCapacity<T>(
            ref NativeList<T> list,
            int requiredCapacity) where T : unmanaged
        {
            requiredCapacity = Mathf.Max(requiredCapacity, 1);

            if (!list.IsCreated)
            {
                list = new NativeList<T>(requiredCapacity, Allocator.Persistent);
                return;
            }

            if (list.Capacity < requiredCapacity)
                list.Capacity = requiredCapacity;

            list.Clear();
        }

        private static void ClearLightGridBuffer<T>(ref NativeList<T> list)
            where T : unmanaged
        {
            if (list.IsCreated)
                list.Clear();
        }

        private static void DisposeLightGridBuffer<T>(ref NativeList<T> list)
            where T : unmanaged
        {
            if (!list.IsCreated)
                return;

            list.Dispose();
            list = default;
        }

        private void CollectDirectionalLightCandidatesAndApply(
            NativeList<VisibleLightRenderDataRecord> visibleLightRenderDataRecords,
            Light sunLight)
        {
            for (var lightIndex = 0; lightIndex < visibleLightRenderDataRecords.Length; lightIndex++)
            {
                var record = visibleLightRenderDataRecords[lightIndex];
                if (record.lightRenderData.lightType != LightType.Directional)
                    continue;

                m_LightGridDirectionalCandidates.AddNoResize(
                    CreateDirectionalLightCandidate(record.visibleLightIndex, record.lightRenderData));
            }

            ApplyDirectionalLightCandidates(m_LightGridDirectionalCandidates, sunLight);
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
            var hasSunLightEntityId = !AreEntityIdsEqual(sunLightEntityId, EntityId.None);
            var brightestDirectionalIntensity = float.NegativeInfinity;
            var brightestVisibleLightIndex = -1;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;

            for (var directionalIndex = 0; directionalIndex < directionalLightCount; directionalIndex++)
            {
                var candidate = directionalCandidates[directionalIndex];
                directionalLights[directionalIndex] = candidate.lightData;

                if (hasSunLightEntityId && AreEntityIdsEqual(candidate.lightEntityId, sunLightEntityId))
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

        private void ApplyAreaLightCandidates(NativeList<AreaLightCandidate> areaCandidates)
        {
            EnsureAreaLightCapacity(areaCandidates.Length);

            areaLightCount = areaCandidates.Length;

            for (var areaIndex = 0; areaIndex < areaLightCount; areaIndex++)
                areaLights[areaIndex] = areaCandidates[areaIndex].lightData;
        }

        private void ApplyReGIRLightCandidates(NativeList<VividReGIRLightData> reGIRLightCandidates)
        {
            EnsureReGIRLightCapacity(reGIRLightCandidates.Length);

            reGIRLightCount = reGIRLightCandidates.Length;

            for (var lightIndex = 0; lightIndex < reGIRLightCount; lightIndex++)
                reGIRLights[lightIndex] = reGIRLightCandidates[lightIndex];
        }

        private void ApplyPunctualLightClusteredCullData(
            NativeList<SFiniteLightBound> lightBounds,
            NativeList<LightVolumeData> lightVolumeData)
        {
            for (var lightIndex = 0; lightIndex < punctualLightCount; lightIndex++)
            {
                punctualLightBounds[lightIndex] = lightBounds[lightIndex];
                punctualLightVolumeData[lightIndex] = lightVolumeData[lightIndex];
            }
        }

        private void ApplyAreaLightClusteredCullData(
            NativeList<SFiniteLightBound> lightBounds,
            NativeList<LightVolumeData> lightVolumeData)
        {
            for (var lightIndex = 0; lightIndex < areaLightCount; lightIndex++)
            {
                areaLightBounds[lightIndex] = lightBounds[lightIndex];
                areaLightVolumeData[lightIndex] = lightVolumeData[lightIndex];
            }
        }

        private static Vector3 ResolveCameraPositionWS(Matrix4x4 worldToViewMatrix)
        {
            return worldToViewMatrix.inverse.MultiplyPoint3x4(Vector3.zero);
        }

        private static bool AreEntityIdsEqual(EntityId lhs, EntityId rhs)
        {
            return EntityId.ToULong(lhs) == EntityId.ToULong(rhs);
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

        private static void CollectReGIRSceneLightSourceRecords(
            IReadOnlyList<VividLightRenderData> sceneLightData,
            NativeList<VividLightRenderData> reGIRSceneLightSourceRecords)
        {
            for (var lightIndex = 0; lightIndex < sceneLightData.Count; lightIndex++)
                reGIRSceneLightSourceRecords.AddNoResize(sceneLightData[lightIndex]);
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
                rightWS = NormalizeDirection(new Vector3(localToWorld.m00, localToWorld.m10, localToWorld.m20), Vector3.right),
                upWS = NormalizeDirection(new Vector3(localToWorld.m01, localToWorld.m11, localToWorld.m21), Vector3.up),
                areaSize = Vector2.zero,
                shapeRadius = 0.0f,
                intensity = GetLightIntensity(finalColor),
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                shadowStrength = 0.0f,
                spotAngle = visibleLight.spotAngle,
                innerSpotAngle = visibleLight.innerSpotAngle,
                rangeAttenuationScale = range > 0.0f ? 1.0f / Mathf.Max(range * range, 1e-6f) : 0.0f,
                rangeAttenuationBias = 1.0f,
                renderingLayerMask = 0u,
                shadowRenderingLayerMask = 0u,
                volumetricDimmer = VividAdditionalLightData.DefaultVolumetricDimmer,
                volumetricFadeDistance = VividAdditionalLightData.DefaultVolumetricFadeDistance,
                volumetricShadowDimmer = VividAdditionalLightData.DefaultVolumetricShadowDimmer,
                flags = VividLightRenderDataFlags.Enabled
                    | VividLightRenderDataFlags.ActiveInHierarchy
                    | VividLightRenderDataFlags.AffectVolumetric,
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

        private static AreaLightCandidate CreateAreaLightCandidate(VividLightRenderData trackedLightData)
        {
            return new AreaLightCandidate
            {
                lightData = CreateAreaLightData(trackedLightData),
            };
        }

        private static bool TryCreateReflectionProbeData(
            VisibleReflectionProbe visibleReflectionProbe,
            Vector3 cameraPositionWS,
            out ReflectionProbeData reflectionProbeData)
        {
            reflectionProbeData = default;

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeBuildSpatialMarker.Auto())
            {
                if (!IsReflectionProbeSpatiallyValid(visibleReflectionProbe))
                    return false;
            }

            Bounds bounds;
            Vector3 extents;
            Matrix4x4 localToWorld;
            Vector3 axisScale;
            Vector3 positionWS;
            Vector3 capturePositionWS;
            Vector3 proxyPositionWS;
            Vector3 proxyExtents;
            Vector3 blendDistancePositive;
            Vector3 blendDistanceNegative;
            Vector3 boxSideFadePositive;
            Vector3 boxSideFadeNegative;
            Vector4 hdrData;
            float visibleBlendDistance;
            bool isBoxProjection;
            bool isProjectionInfinite;
            float multiplier;
            float weight;
            int importance;
            float rangeCompressionFactor;

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeBuildBaseDataMarker.Auto())
            {
                bounds = visibleReflectionProbe.bounds;
                extents = bounds.extents;
                localToWorld = visibleReflectionProbe.localToWorldMatrix;
                axisScale = GetLocalAxisScale(localToWorld);
                positionWS = bounds.center;
                capturePositionWS = new Vector3(localToWorld.m03, localToWorld.m13, localToWorld.m23);
                proxyPositionWS = positionWS;
                proxyExtents = extents;
                visibleBlendDistance = Mathf.Max(visibleReflectionProbe.blendDistance, 0.0f);
                blendDistancePositive = Vector3.one * visibleBlendDistance;
                blendDistanceNegative = blendDistancePositive;
                boxSideFadePositive = Vector3.one;
                boxSideFadeNegative = Vector3.one;
                isBoxProjection = visibleReflectionProbe.isBoxProjection;
                isProjectionInfinite = !isBoxProjection;
                multiplier = 1.0f;
                weight = 1.0f;
                importance = visibleReflectionProbe.importance;
                rangeCompressionFactor = 1.0f;
                hdrData = visibleReflectionProbe.hdrData;
            }

            VividAdditionalReflectionData additionalData;
            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeAdditionalDataMarker.Auto())
            {
                additionalData = GetAdditionalReflectionData(visibleReflectionProbe);
            }
            var hasAdditionalData = additionalData != null && additionalData.isActiveAndEnabled;

            if (hasAdditionalData)
            {
                using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeApplyAdditionalDataMarker.Auto())
                {
                    using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeApplyAdditionalDataSyncMarker.Auto())
                    {
                        additionalData.SyncReflectionProbeIfDirty();
                    }

                    using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbeApplyAdditionalDataValuesMarker.Auto())
                    {
                        positionWS = localToWorld.MultiplyPoint(additionalData.influenceBoxOffset);
                        extents = ScaleVector(additionalData.influenceBoxSize * 0.5f, axisScale);
                        blendDistancePositive = ScaleVector(additionalData.boxBlendDistancePositive, axisScale);
                        blendDistanceNegative = ScaleVector(additionalData.boxBlendDistanceNegative, axisScale);
                        boxSideFadePositive = additionalData.boxSideFadePositive;
                        boxSideFadeNegative = additionalData.boxSideFadeNegative;
                        isProjectionInfinite = additionalData.isProjectionInfinite;
                        isBoxProjection = !isProjectionInfinite;
                        multiplier = additionalData.multiplier;
                        capturePositionWS = localToWorld.MultiplyPoint(additionalData.capturePositionOffset);
                        weight = ComputeWeightedLinearFadeDistance(
                            new Vector3(localToWorld.m03, localToWorld.m13, localToWorld.m23),
                            cameraPositionWS,
                            additionalData.weight,
                            additionalData.fadeDistance);
                        importance = additionalData.importance;
                        rangeCompressionFactor = additionalData.rangeCompressionFactor;
                        proxyPositionWS = localToWorld.MultiplyPoint(additionalData.GetProxyBoxOffset());
                        proxyExtents = ScaleVector(additionalData.GetProxyBoxSize() * 0.5f, axisScale);
                    }
                }
            }

            using (RenderPassProfilingUtility.InitializeContextLightDataReflectionProbePackResultMarker.Auto())
            {
                reflectionProbeData = new ReflectionProbeData
                {
                    positionWS = positionWS,
                    blendDistance = visibleBlendDistance,
                    extents = extents,
                    isBoxProjection = isBoxProjection ? 1u : 0u,
                    rightWS = NormalizeDirection(new Vector3(localToWorld.m00, localToWorld.m10, localToWorld.m20), Vector3.right),
                    importance = importance,
                    upWS = NormalizeDirection(new Vector3(localToWorld.m01, localToWorld.m11, localToWorld.m21), Vector3.up),
                    weight = weight,
                    forwardWS = NormalizeDirection(new Vector3(localToWorld.m02, localToWorld.m12, localToWorld.m22), Vector3.forward),
                    padding0 = 0.0f,
                    capturePositionWS = capturePositionWS,
                    padding1 = 0.0f,
                    hdrData = hdrData,
                    atlasScaleOffset = Vector4.zero,
                    atlasIndexAndSlice = new Vector4(-1.0f, -1.0f, 0.0f, 0.0f),
                    blendDistancePositive = blendDistancePositive,
                    multiplier = multiplier,
                    blendDistanceNegative = blendDistanceNegative,
                    isProjectionInfinite = isProjectionInfinite ? 1u : 0u,
                    boxSideFadePositive = boxSideFadePositive,
                    rangeCompressionFactor = rangeCompressionFactor,
                    boxSideFadeNegative = boxSideFadeNegative,
                    padding2 = 0.0f,
                    proxyPositionWS = proxyPositionWS,
                    padding3 = 0.0f,
                    proxyExtents = proxyExtents,
                    padding4 = 0.0f,
                };
            }
            return true;
        }

        private static VividAdditionalReflectionData GetAdditionalReflectionData(VisibleReflectionProbe visibleReflectionProbe)
        {
            if (!VividAdditionalReflectionData.hasRegisteredData)
                return null;

            if (s_CanReadVisibleReflectionProbeEntityId)
            {
                ref var probeLayout = ref UnsafeUtility.As<VisibleReflectionProbe, VisibleReflectionProbeEntityIdLayout>(ref visibleReflectionProbe);
                return VividAdditionalReflectionData.TryGetAdditionalData(probeLayout.entityId, out var entityIdAdditionalData)
                    ? entityIdAdditionalData
                    : null;
            }

            var reflectionProbe = visibleReflectionProbe.reflectionProbe;
            if (reflectionProbe == null)
                return null;

            return VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe, out var additionalData)
                ? additionalData
                : null;
        }

        private static Vector3 GetLocalAxisScale(Matrix4x4 localToWorld)
        {
            return new Vector3(
                new Vector3(localToWorld.m00, localToWorld.m10, localToWorld.m20).magnitude,
                new Vector3(localToWorld.m01, localToWorld.m11, localToWorld.m21).magnitude,
                new Vector3(localToWorld.m02, localToWorld.m12, localToWorld.m22).magnitude);
        }

        private static Vector3 ScaleVector(Vector3 value, Vector3 scale)
        {
            return new Vector3(
                Mathf.Abs(value.x * scale.x),
                Mathf.Abs(value.y * scale.y),
                Mathf.Abs(value.z * scale.z));
        }

        private static float ComputeWeightedLinearFadeDistance(
            Vector3 positionWS,
            Vector3 cameraPositionWS,
            float weight,
            float fadeDistance)
        {
            return Mathf.Clamp01(weight) * ComputeLinearDistanceFade(Vector3.Distance(positionWS, cameraPositionWS), fadeDistance);
        }

        private static float ComputeLinearDistanceFade(float distanceToCamera, float fadeDistance)
        {
            if (fadeDistance <= 0.0001f)
                return distanceToCamera <= 0.0001f ? 1.0f : 0.0f;

            var fadeStart = 0.9f * fadeDistance;
            var fadeRange = Mathf.Max(fadeDistance - fadeStart, 0.0001f);
            return 1.0f - Mathf.Clamp01((distanceToCamera - fadeStart) / fadeRange);
        }

        private static bool IsReflectionProbeSpatiallyValid(VisibleReflectionProbe visibleReflectionProbe)
        {
            var extents = visibleReflectionProbe.bounds.extents;
            return extents.x > 0.0f && extents.y > 0.0f && extents.z > 0.0f;
        }

        private static bool IsPunctualLightSupported(VividLightRenderData trackedLightData)
        {
            return trackedLightData.lightType switch
            {
                LightType.Point => trackedLightData.range > 0.0f,
                LightType.Spot => trackedLightData.range > 0.0f,
                LightType.Box => trackedLightData.range > 0.0f
                    && trackedLightData.areaSize.x > 0.0f
                    && trackedLightData.areaSize.y > 0.0f,
                _ => false,
            };
        }

        private static bool IsAreaLightSupported(VividLightRenderData trackedLightData)
        {
            return trackedLightData.lightType switch
            {
                LightType.Rectangle => trackedLightData.range > 0.0f
                    && trackedLightData.areaSize.x > 0.0f
                    && trackedLightData.areaSize.y > 0.0f,
                LightType.Tube => trackedLightData.range > 0.0f
                    && trackedLightData.areaSize.x > 0.0f,
                _ => false,
            };
        }

        private static bool IsReGIRLightSupported(VividLightRenderData trackedLightData)
        {
            return IsReGIRPunctualLightSupported(trackedLightData)
                   || IsAreaLightSupported(trackedLightData);
        }

        private static bool IsReGIRPunctualLightSupported(VividLightRenderData trackedLightData)
        {
            return (trackedLightData.lightType == LightType.Point || trackedLightData.lightType == LightType.Spot)
                   && trackedLightData.range > 0.0f;
        }

        private static bool IsLightEnabledAndActive(VividLightRenderData trackedLightData)
        {
            const VividLightRenderDataFlags requiredFlags =
                VividLightRenderDataFlags.Enabled | VividLightRenderDataFlags.ActiveInHierarchy;

            return (trackedLightData.flags & requiredFlags) == requiredFlags;
        }

        private static bool IntersectsReGIRCollectionBox(
            VividLightRenderData trackedLightData,
            Vector3 collectionBoxCenterWS,
            Vector3 collectionBoxHalfExtents)
        {
            var cullingCenterWS = trackedLightData.positionWS;
            var cullingRadius = ResolveReGIRCollectionRadius(trackedLightData);
            if (cullingRadius <= 0.0f)
                return false;

            if (trackedLightData.lightType == LightType.Spot)
            {
                var punctualLightData = CreatePunctualLightData(trackedLightData);
                var punctualCullData = CreatePunctualLightCullData(punctualLightData);
                cullingCenterWS = punctualCullData.cullingCenterWS;
                cullingRadius = punctualCullData.cullingRadius;
            }

            return SphereIntersectsAabb(cullingCenterWS, cullingRadius, collectionBoxCenterWS, collectionBoxHalfExtents);
        }

        private static float ResolveReGIRCollectionRadius(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.0f);
            return trackedLightData.lightType switch
            {
                LightType.Rectangle => range + 0.5f * new Vector2(
                    Mathf.Max(trackedLightData.areaSize.x, 0.0f),
                    Mathf.Max(trackedLightData.areaSize.y, 0.0f)).magnitude,
                LightType.Tube => range + 0.5f * Mathf.Max(trackedLightData.areaSize.x, 0.0f),
                _ => range + Mathf.Max(trackedLightData.shapeRadius, 0.0f),
            };
        }

        private static bool SphereIntersectsAabb(
            Vector3 sphereCenter,
            float sphereRadius,
            Vector3 boxCenter,
            Vector3 boxHalfExtents)
        {
            var delta = sphereCenter - boxCenter;
            var outsideX = Mathf.Max(Mathf.Abs(delta.x) - boxHalfExtents.x, 0.0f);
            var outsideY = Mathf.Max(Mathf.Abs(delta.y) - boxHalfExtents.y, 0.0f);
            var outsideZ = Mathf.Max(Mathf.Abs(delta.z) - boxHalfExtents.z, 0.0f);
            var outsideDistanceSq = outsideX * outsideX + outsideY * outsideY + outsideZ * outsideZ;

            return outsideDistanceSq <= sphereRadius * sphereRadius;
        }

        private static uint GetPunctualLightType(LightType lightType)
        {
            return lightType switch
            {
                LightType.Spot => VividPunctualLightTypeSpot,
                LightType.Box => VividPunctualLightTypeProjectorBox,
                _ => VividPunctualLightTypePoint,
            };
        }

        private static uint GetAreaLightType(LightType lightType)
        {
            return lightType == LightType.Tube ? 0u : 1u;
        }

        private static uint GetReGIRLightType(LightType lightType)
        {
            return lightType switch
            {
                LightType.Spot => VividReGIRLightData.TypeSpot,
                LightType.Tube => VividReGIRLightData.TypeTube,
                LightType.Rectangle => VividReGIRLightData.TypeRectangle,
                _ => VividReGIRLightData.TypePoint,
            };
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

            if (source.lightType != VividPunctualLightTypeSpot)
                return;

            cosOuterAngle = Mathf.Clamp01(-source.angleOffset / Mathf.Max(source.angleScale, 1e-6f));
            var tanOuter = Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(cosOuterAngle * cosOuterAngle, 1e-6f) - 1.0f, 0.0f));
            radiusAtRange = source.range * tanOuter;
        }

        private static void GetPunctualLightCullingSphere(PunctualLightData source, out Vector3 cullingCenterWS, out float cullingRadius)
        {
            cullingCenterWS = source.positionWS;
            cullingRadius = source.range;

            if (source.lightType == VividPunctualLightTypeProjectorBox)
            {
                GetPunctualLightCullingShapeData(source, out var projectorDirectionWS, out _, out _);
                var extents = new Vector3(
                    0.5f * Mathf.Max(source.projectorSize.x, 0.0f),
                    0.5f * Mathf.Max(source.projectorSize.y, 0.0f),
                    0.5f * Mathf.Max(source.range, 0.0f));
                cullingCenterWS = source.positionWS + projectorDirectionWS * extents.z;
                cullingRadius = extents.magnitude;
                return;
            }

            if (source.lightType != VividPunctualLightTypeSpot)
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

            var innerHalfAngleDegrees = Mathf.Clamp(innerSpotAngle * 0.5f, 0.0f, 89.0f);
            var outerHalfAngleDegrees = GetSpotOuterHalfAngleDegrees(innerHalfAngleDegrees, outerSpotAngle);
            var innerHalfAngle = innerHalfAngleDegrees * Mathf.Deg2Rad;
            var outerHalfAngle = outerHalfAngleDegrees * Mathf.Deg2Rad;
            var cosInner = Mathf.Cos(innerHalfAngle);
            var cosOuter = Mathf.Cos(outerHalfAngle);
            var angleRange = Mathf.Max(cosInner - cosOuter, 0.001f);

            angleScale = 1.0f / angleRange;
            angleOffset = -cosOuter * angleScale;
        }

        private static Vector2 GetProjectorBoxSize(VividLightRenderData trackedLightData)
        {
            if (trackedLightData.lightType != LightType.Box)
                return Vector2.zero;

            return new Vector2(
                Mathf.Max(trackedLightData.areaSize.x, 0.0f),
                Mathf.Max(trackedLightData.areaSize.y, 0.0f));
        }

        private static void GetPunctualLightAxisScales(
            LightType lightType,
            float innerSpotAngle,
            float outerSpotAngle,
            Vector2 projectorSize,
            out float rightAxisScale,
            out float upAxisScale)
        {
            if (lightType == LightType.Box)
            {
                rightAxisScale = 2.0f / Mathf.Max(projectorSize.x, 0.001f);
                upAxisScale = 2.0f / Mathf.Max(projectorSize.y, 0.001f);
                return;
            }

            rightAxisScale = GetSpotConeAxisScale(lightType, innerSpotAngle, outerSpotAngle);
            upAxisScale = rightAxisScale;
        }

        private static float GetSpotConeAxisScale(LightType lightType, float innerSpotAngle, float outerSpotAngle)
        {
            if (lightType != LightType.Spot)
                return 1.0f;

            var innerHalfAngleDegrees = Mathf.Clamp(innerSpotAngle * 0.5f, 0.0f, 89.0f);
            var outerHalfAngle = GetSpotOuterHalfAngleDegrees(innerHalfAngleDegrees, outerSpotAngle) * Mathf.Deg2Rad;
            var cosOuter = Mathf.Clamp01(Mathf.Cos(outerHalfAngle));
            var sinOuter = Mathf.Sqrt(Mathf.Max(1.0f - cosOuter * cosOuter, 1e-6f));
            return cosOuter / sinOuter;
        }

        private static float GetSpotOuterHalfAngleDegrees(float innerHalfAngleDegrees, float outerSpotAngle)
        {
            var minOuterHalfAngle = Mathf.Min(innerHalfAngleDegrees + 0.001f, 89.0f);
            return Mathf.Clamp(outerSpotAngle * 0.5f, minOuterHalfAngle, 89.0f);
        }

        private static float GetLightIntensity(Color finalColor)
        {
            return Mathf.Max(finalColor.r, finalColor.g, finalColor.b);
        }

        private static float GetLightIntensity(Vector3 finalColor)
        {
            return math.max(finalColor.x, math.max(finalColor.y, finalColor.z));
        }

        private static PunctualLightViewSpaceCullDataRecord BuildPunctualLightViewSpaceCullDataRecord(
            PunctualLightCullData source,
            float4x4 worldToViewMatrix)
        {
            var positionVS = BoundProxyClusterProjectionUtility.TransformWorldToPositiveViewSpace(worldToViewMatrix, source.positionWS);
            var directionVS = NormalizeDirection(TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.directionWS), new float3(0.0f, 0.0f, 1.0f));
            var rightVS = NormalizeDirection(TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.rightWS), new float3(1.0f, 0.0f, 0.0f));
            var upVS = NormalizeDirection(TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.upWS), new float3(0.0f, 1.0f, 0.0f));
            var cullingCenterVS = BoundProxyClusterProjectionUtility.TransformWorldToPositiveViewSpace(worldToViewMatrix, source.cullingCenterWS);

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
                rightVS = rightVS,
                projectorWidth = source.projectorWidth,
                upVS = upVS,
                projectorHeight = source.projectorHeight,
            };
        }

        private static void BuildPunctualLightVolumeDataAndBound(
            PunctualLightCullData source,
            PunctualLightViewSpaceCullDataRecord viewSpaceCullData,
            out LightVolumeData lightVolumeData,
            out SFiniteLightBound lightBound)
        {
            lightVolumeData = default;
            lightBound = default;

            var range = math.max(viewSpaceCullData.range, 1e-4f);

            lightVolumeData.lightCategory = HdrpLightCategoryPunctual;
            lightVolumeData.affectVolumetric = source.affectVolumetric != 0u ? 1 : 0;
            lightVolumeData.featureFlags = HdrpLightFeatureFlagsPunctual;
            lightVolumeData.radiusSq = range * range;

            if (source.lightType == VividPunctualLightTypeProjectorBox)
            {
                var axisX = NormalizeDirection(viewSpaceCullData.rightVS, new float3(1.0f, 0.0f, 0.0f));
                var axisY = NormalizeDirection(viewSpaceCullData.upVS, new float3(0.0f, 1.0f, 0.0f));
                var axisZ = NormalizeDirection(viewSpaceCullData.directionVS, new float3(0.0f, 0.0f, 1.0f));
                var extents = new float3(
                    0.5f * math.max(viewSpaceCullData.projectorWidth, 1e-4f),
                    0.5f * math.max(viewSpaceCullData.projectorHeight, 1e-4f),
                    0.5f * range);
                var center = viewSpaceCullData.positionVS + axisZ * extents.z;
                var radius = math.length(extents);

                lightBound.center = new Vector3(center.x, center.y, center.z);
                lightBound.boxAxisX = new Vector4(axisX.x * extents.x, axisX.y * extents.x, axisX.z * extents.x, 1.0f);
                lightBound.boxAxisY = new Vector4(axisY.x * extents.y, axisY.y * extents.y, axisY.z * extents.y, radius);
                lightBound.boxAxisZ = new Vector3(axisZ.x * extents.z, axisZ.y * extents.z, axisZ.z * extents.z);

                lightVolumeData.lightVolume = HdrpLightVolumeTypeBox;
                lightVolumeData.lightAxisX = new Vector3(axisX.x, axisX.y, axisX.z);
                lightVolumeData.lightAxisY = new Vector3(axisY.x, axisY.y, axisY.z);
                lightVolumeData.lightAxisZ = new Vector3(axisZ.x, axisZ.y, axisZ.z);
                lightVolumeData.lightPos = lightBound.center;
                lightVolumeData.boxInvRange = new Vector3(
                    1.0f / math.max(extents.x, 1e-4f),
                    1.0f / math.max(extents.y, 1e-4f),
                    1.0f / math.max(extents.z, 1e-4f));
                return;
            }

            if (source.lightType == VividPunctualLightTypeSpot)
            {
                CreatePerpendicularBasis(viewSpaceCullData.directionVS, out var axisX, out var axisY);
                var axisZ = NormalizeDirection(viewSpaceCullData.directionVS, new float3(0.0f, 0.0f, 1.0f));
                var cosOuterAngle = math.clamp(viewSpaceCullData.cosOuterAngle, 0.0f, 0.999999f);
                var sinOuterAngle = math.sqrt(math.max(1.0f - cosOuterAngle * cosOuterAngle, 0.0f));
                const float floatMax = 3.402823466e+38f;
                var tanOuterAngle = cosOuterAngle > 0.0f ? sinOuterAngle / cosOuterAngle : floatMax;
                var cotan = sinOuterAngle > 0.0f ? cosOuterAngle / sinOuterAngle : floatMax;
                var halfRange = 0.5f * range;
                var squeezeScale = tanOuterAngle;
                var rangeVector = axisZ * halfRange;
                var sphereAxisX = sinOuterAngle * range;
                var sphereAxisY = (cosOuterAngle - 0.5f) * range;
                var radius = math.sqrt(sphereAxisY * sphereAxisY + sphereAxisX * sphereAxisX);

                lightBound.center = new Vector3(
                    viewSpaceCullData.positionVS.x + rangeVector.x,
                    viewSpaceCullData.positionVS.y + rangeVector.y,
                    viewSpaceCullData.positionVS.z + rangeVector.z);
                lightBound.boxAxisX = new Vector4(axisX.x * squeezeScale * range, axisX.y * squeezeScale * range, axisX.z * squeezeScale * range, 0.01f);
                lightBound.boxAxisY = new Vector4(axisY.x * squeezeScale * range, axisY.y * squeezeScale * range, axisY.z * squeezeScale * range, math.max(radius, halfRange));
                lightBound.boxAxisZ = new Vector3(rangeVector.x, rangeVector.y, rangeVector.z);

                lightVolumeData.lightVolume = HdrpLightVolumeTypeCone;
                lightVolumeData.lightAxisX = new Vector3(axisX.x, axisX.y, axisX.z);
                lightVolumeData.lightAxisY = new Vector3(axisY.x, axisY.y, axisY.z);
                lightVolumeData.lightAxisZ = new Vector3(axisZ.x, axisZ.y, axisZ.z);
                lightVolumeData.lightPos = new Vector3(viewSpaceCullData.positionVS.x, viewSpaceCullData.positionVS.y, viewSpaceCullData.positionVS.z);
                lightVolumeData.cotan = cotan;
                return;
            }

            var pointAxisX = new Vector3(1.0f, 0.0f, 0.0f);
            var pointAxisY = new Vector3(0.0f, 1.0f, 0.0f);
            var pointAxisZ = new Vector3(0.0f, 0.0f, 1.0f);
            var pointCenter = new Vector3(viewSpaceCullData.positionVS.x, viewSpaceCullData.positionVS.y, viewSpaceCullData.positionVS.z);

            lightBound.center = pointCenter;
            lightBound.boxAxisX = new Vector4(pointAxisX.x * range, pointAxisX.y * range, pointAxisX.z * range, 1.0f);
            lightBound.boxAxisY = new Vector4(pointAxisY.x * range, pointAxisY.y * range, pointAxisY.z * range, range);
            lightBound.boxAxisZ = pointAxisZ * range;

            lightVolumeData.lightVolume = HdrpLightVolumeTypeSphere;
            lightVolumeData.lightAxisX = pointAxisX;
            lightVolumeData.lightAxisY = pointAxisY;
            lightVolumeData.lightAxisZ = pointAxisZ;
            lightVolumeData.lightPos = pointCenter;
        }

        private static void BuildAreaLightVolumeDataAndBound(
            AreaLightData source,
            float4x4 worldToViewMatrix,
            out LightVolumeData lightVolumeData,
            out SFiniteLightBound lightBound)
        {
            lightVolumeData = default;
            lightBound = default;

            var positionVS = BoundProxyClusterProjectionUtility.TransformWorldToPositiveViewSpace(
                worldToViewMatrix,
                source.positionWS);
            var axisXVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.rightWS),
                new float3(1.0f, 0.0f, 0.0f));
            var axisYVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.upWS),
                new float3(0.0f, 1.0f, 0.0f));
            var axisZVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.forwardWS),
                new float3(0.0f, 0.0f, 1.0f));
            var range = math.max(source.range, 1e-4f);

            lightVolumeData.lightCategory = HdrpLightCategoryArea;
            lightVolumeData.lightVolume = HdrpLightVolumeTypeBox;
            lightVolumeData.affectVolumetric = source.affectVolumetric != 0u && source.volumetricDimmer > 0.0f ? 1 : 0;
            lightVolumeData.featureFlags = HdrpLightFeatureFlagsArea;
            lightVolumeData.lightAxisX = new Vector3(axisXVS.x, axisXVS.y, axisXVS.z);
            lightVolumeData.lightAxisY = new Vector3(axisYVS.x, axisYVS.y, axisYVS.z);
            lightVolumeData.lightAxisZ = new Vector3(axisZVS.x, axisZVS.y, axisZVS.z);

            if (source.lightType == 0u)
            {
                var dimensions = new float3(
                    math.max(source.width, 0.0f) + 2.0f * range,
                    2.0f * range,
                    2.0f * range);
                var extents = 0.5f * dimensions;

                lightBound.center = new Vector3(positionVS.x, positionVS.y, positionVS.z);
                lightBound.boxAxisX = new Vector4(axisXVS.x * extents.x, axisXVS.y * extents.x, axisXVS.z * extents.x, 1.0f);
                lightBound.boxAxisY = new Vector4(axisYVS.x * extents.y, axisYVS.y * extents.y, axisYVS.z * extents.y, extents.x);
                lightBound.boxAxisZ = new Vector3(axisZVS.x * extents.z, axisZVS.y * extents.z, axisZVS.z * extents.z);

                lightVolumeData.lightPos = lightBound.center;
                lightVolumeData.boxInvRange = new Vector3(
                    1.0f / math.max(extents.x, 1e-4f),
                    1.0f / math.max(extents.y, 1e-4f),
                    1.0f / math.max(extents.z, 1e-4f));
                return;
            }

            GetRectangleAreaLightInfluenceBounds(source, positionVS, axisZVS, range, out var rectangleExtents, out var centerVS, out var radius);

            lightBound.center = new Vector3(centerVS.x, centerVS.y, centerVS.z);
            lightBound.boxAxisX = new Vector4(axisXVS.x * rectangleExtents.x, axisXVS.y * rectangleExtents.x, axisXVS.z * rectangleExtents.x, 1.0f);
            lightBound.boxAxisY = new Vector4(axisYVS.x * rectangleExtents.y, axisYVS.y * rectangleExtents.y, axisYVS.z * rectangleExtents.y, radius);
            lightBound.boxAxisZ = new Vector3(axisZVS.x * rectangleExtents.z, axisZVS.y * rectangleExtents.z, axisZVS.z * rectangleExtents.z);

            lightVolumeData.lightPos = lightBound.center;
            lightVolumeData.boxInvRange = new Vector3(
                1.0f / math.max(rectangleExtents.x, 1e-4f),
                1.0f / math.max(rectangleExtents.y, 1e-4f),
                1.0f / math.max(rectangleExtents.z, 1e-4f));
        }

        private static void BuildReflectionProbeVolumeDataAndBound(
            ReflectionProbeData source,
            float4x4 worldToViewMatrix,
            out LightVolumeData lightVolumeData,
            out SFiniteLightBound lightBound)
        {
            lightVolumeData = default;
            lightBound = default;

            var positionVS = BoundProxyClusterProjectionUtility.TransformWorldToPositiveViewSpace(
                worldToViewMatrix,
                source.positionWS);
            var axisXVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.rightWS),
                new float3(1.0f, 0.0f, 0.0f));
            var axisYVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.upWS),
                new float3(0.0f, 1.0f, 0.0f));
            var axisZVS = NormalizeDirection(
                TransformWorldVectorToPositiveViewSpace(worldToViewMatrix, source.forwardWS),
                new float3(0.0f, 0.0f, 1.0f));
            var extents = new float3(
                math.max(source.extents.x, 1e-4f),
                math.max(source.extents.y, 1e-4f),
                math.max(source.extents.z, 1e-4f));
            var radius = math.length(extents);
            var cullingThreshold = new Vector3(
                HdrpBoxCullingExtentThreshold,
                HdrpBoxCullingExtentThreshold,
                HdrpBoxCullingExtentThreshold);

            lightBound.center = new Vector3(positionVS.x, positionVS.y, positionVS.z);
            lightBound.boxAxisX = new Vector4(axisXVS.x * extents.x, axisXVS.y * extents.x, axisXVS.z * extents.x, 1.0f);
            lightBound.boxAxisY = new Vector4(axisYVS.x * extents.y, axisYVS.y * extents.y, axisYVS.z * extents.y, radius);
            lightBound.boxAxisZ = new Vector3(axisZVS.x * extents.z, axisZVS.y * extents.z, axisZVS.z * extents.z);

            lightVolumeData.lightPos = lightBound.center;
            lightVolumeData.lightVolume = HdrpLightVolumeTypeBox;
            lightVolumeData.lightAxisX = new Vector3(axisXVS.x, axisXVS.y, axisXVS.z);
            lightVolumeData.lightCategory = HdrpLightCategoryEnv;
            lightVolumeData.lightAxisY = new Vector3(axisYVS.x, axisYVS.y, axisYVS.z);
            lightVolumeData.radiusSq = radius * radius;
            lightVolumeData.lightAxisZ = new Vector3(axisZVS.x, axisZVS.y, axisZVS.z);
            lightVolumeData.cotan = 0.0f;
            lightVolumeData.boxInnerDist = new Vector3(
                math.max(extents.x - HdrpBoxCullingExtentThreshold, 0.0f),
                math.max(extents.y - HdrpBoxCullingExtentThreshold, 0.0f),
                math.max(extents.z - HdrpBoxCullingExtentThreshold, 0.0f));
            lightVolumeData.featureFlags = HdrpLightFeatureFlagsEnv;
            lightVolumeData.boxInvRange = new Vector3(
                1.0f / cullingThreshold.x,
                1.0f / cullingThreshold.y,
                1.0f / cullingThreshold.z);
            lightVolumeData.affectVolumetric = 0;
        }

        // Matches HDRP's rectangle clustered bounds: barn door is a shading-time crop of the source
        // and does not shrink the conservative one-sided influence volume uploaded to LightGrid.
        private static void GetRectangleAreaLightInfluenceBounds(
            AreaLightData source,
            float3 positionVS,
            float3 axisZVS,
            float range,
            out float3 rectangleExtents,
            out float3 centerVS,
            out float radius)
        {
            var rectangleDimensions = new float3(
                math.max(source.width, 0.0f) + 2.0f * range,
                math.max(source.height, 0.0f) + 2.0f * range,
                range);
            rectangleExtents = 0.5f * rectangleDimensions;
            centerVS = positionVS + rectangleExtents.z * axisZVS;

            var diagonalRadius = range + 0.5f * math.sqrt(source.width * source.width + source.height * source.height);
            radius = math.sqrt(diagonalRadius * diagonalRadius + rectangleExtents.z * rectangleExtents.z);
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

        private static void CreatePerpendicularBasis(float3 forward, out float3 axisX, out float3 axisY)
        {
            forward = NormalizeDirection(forward, new float3(0.0f, 0.0f, 1.0f));
            var tangent = math.abs(forward.y) < 0.999f
                ? new float3(0.0f, 1.0f, 0.0f)
                : new float3(1.0f, 0.0f, 0.0f);
            axisX = NormalizeDirection(math.cross(tangent, forward), new float3(1.0f, 0.0f, 0.0f));
            axisY = NormalizeDirection(math.cross(forward, axisX), new float3(0.0f, 1.0f, 0.0f));
        }
    }
}
