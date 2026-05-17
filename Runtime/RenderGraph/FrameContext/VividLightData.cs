using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividLightData : ContextItem
    {
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
            public float rangeAttenuationScale;
            public float rangeAttenuationBias;
            public float shadowStrength;
            public uint renderingLayerMask;
            public float volumetricDimmer;
            public float volumetricShadowDimmer;
            public float volumetricFadeDistance;
            public uint affectVolumetric;

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
        private const uint HdrpLightCategoryDecal = 3u;
        private const uint HdrpLightFeatureFlagsPunctual = 4096u;
        private const uint HdrpLightFeatureFlagsArea = 8192u;
        private const uint HdrpLightFeatureFlagsDecal = 524288u;
        private const uint HdrpLightVolumeTypeCone = 0u;
        private const uint HdrpLightVolumeTypeSphere = 1u;
        private const uint HdrpLightVolumeTypeBox = 2u;

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
        public PunctualLightCullData[] punctualLightCullData = Array.Empty<PunctualLightCullData>();
        public SFiniteLightBound[] punctualLightBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] punctualLightVolumeData = Array.Empty<LightVolumeData>();
        public SFiniteLightBound[] areaLightBounds = Array.Empty<SFiniteLightBound>();
        public LightVolumeData[] areaLightVolumeData = Array.Empty<LightVolumeData>();
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
        public int reGIRLightCount;
        public int mainDirectionalLightIndex;
        public EntityId mainDirectionalLightEntityId;

        // LightGrid candidate build runs on a Burst job scheduled in Update and completed by LightGridPass.Prepare.
        // The fields below are owned by VividLightData while the job is in flight; do not access from outside CompleteLightGridPrepare.
        private NativeList<VisibleLightRenderDataRecord> m_LightGridVisibleLightRecords;
        private NativeList<PunctualLightCandidate> m_LightGridPunctualCandidates;
        private NativeList<AreaLightCandidate> m_LightGridAreaCandidates;
        private NativeList<SFiniteLightBound> m_LightGridPunctualLightBounds;
        private NativeList<LightVolumeData> m_LightGridPunctualLightVolumeData;
        private NativeList<SFiniteLightBound> m_LightGridAreaLightBounds;
        private NativeList<LightVolumeData> m_LightGridAreaLightVolumeData;
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
            DrainLightGridPrepare();

            visibleLights = cullingResults.visibleLights;
            visibleReflectionProbes = cullingResults.visibleReflectionProbes;
            UpdateVisibleLightData(visibleLights, RenderSettings.sun, worldToViewMatrix);
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
            ClearLightGridBuffer(ref m_LightGridPunctualCandidates);
            ClearLightGridBuffer(ref m_LightGridAreaCandidates);
            ClearLightGridBuffer(ref m_LightGridPunctualLightBounds);
            ClearLightGridBuffer(ref m_LightGridPunctualLightVolumeData);
            ClearLightGridBuffer(ref m_LightGridAreaLightBounds);
            ClearLightGridBuffer(ref m_LightGridAreaLightVolumeData);
            ClearLightGridBuffer(ref m_LightGridReGIRLights);
        }

        private void DisposeLightGridBuffers()
        {
            DisposeLightGridBuffer(ref m_LightGridVisibleLightRecords);
            DisposeLightGridBuffer(ref m_LightGridPunctualCandidates);
            DisposeLightGridBuffer(ref m_LightGridAreaCandidates);
            DisposeLightGridBuffer(ref m_LightGridPunctualLightBounds);
            DisposeLightGridBuffer(ref m_LightGridPunctualLightVolumeData);
            DisposeLightGridBuffer(ref m_LightGridAreaLightBounds);
            DisposeLightGridBuffer(ref m_LightGridAreaLightVolumeData);
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
            reGIRLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
            areaLights = Array.Empty<AreaLightData>();
            reGIRLights = Array.Empty<VividReGIRLightData>();
            punctualLightBounds = Array.Empty<SFiniteLightBound>();
            punctualLightVolumeData = Array.Empty<LightVolumeData>();
            areaLightBounds = Array.Empty<SFiniteLightBound>();
            areaLightVolumeData = Array.Empty<LightVolumeData>();
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

            if (!visibleLights.IsCreated || visibleLights.Length == 0)
            {
                ClearLightGridBuffers();
                return;
            }

            var lightCapacity = Mathf.Max(visibleLights.Length, 1);
            EnsureLightGridBufferCapacity(lightCapacity);

            CollectVisibleLightRenderDataRecords(visibleLights, m_LightGridVisibleLightRecords);

            CollectDirectionalLightCandidatesAndApply(m_LightGridVisibleLightRecords, sunLight);

            var buildLightGridJob = new BuildLightGridLightCandidatesJob
            {
                visibleLightRenderDataRecords = m_LightGridVisibleLightRecords.AsArray(),
                punctualLights = m_LightGridPunctualCandidates,
                areaLights = m_LightGridAreaCandidates,
                reGIRLights = m_LightGridReGIRLights,
                punctualLightBounds = m_LightGridPunctualLightBounds,
                punctualLightVolumeData = m_LightGridPunctualLightVolumeData,
                areaLightBounds = m_LightGridAreaLightBounds,
                areaLightVolumeData = m_LightGridAreaLightVolumeData,
                worldToViewMatrix = worldToViewMatrix,
            };
            m_LightGridJobHandle = buildLightGridJob.Schedule();
            m_LightGridJobScheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }

        private void EnsureLightGridBufferCapacity(int lightCapacity)
        {
            // VisibleLightRenderDataRecord must be built on the main thread (touches managed Light + database).
            // It is consumed by both the synchronous directional pass below and the deferred LightGrid Burst job.
            EnsureLightGridBufferCapacity(ref m_LightGridVisibleLightRecords, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridPunctualCandidates, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridAreaCandidates, lightCapacity);
            EnsureLightGridBufferCapacity(ref m_LightGridReGIRLights, lightCapacity);
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
            using var directionalCandidates = new NativeList<DirectionalLightCandidate>(visibleLightRenderDataRecords.Length, Allocator.Temp);

            for (var lightIndex = 0; lightIndex < visibleLightRenderDataRecords.Length; lightIndex++)
            {
                var record = visibleLightRenderDataRecords[lightIndex];
                if (record.lightRenderData.lightType != LightType.Directional)
                    continue;

                directionalCandidates.Add(
                    CreateDirectionalLightCandidate(record.visibleLightIndex, record.lightRenderData));
            }

            ApplyDirectionalLightCandidates(directionalCandidates, sunLight);
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

        private static bool IsPunctualLightSupported(VividLightRenderData trackedLightData)
        {
            return (trackedLightData.lightType == LightType.Point || trackedLightData.lightType == LightType.Spot)
                   && trackedLightData.range > 0.0f;
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

        private static uint GetPunctualLightType(LightType lightType)
        {
            return lightType == LightType.Spot ? 1u : 0u;
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

            if (source.lightType == 1u)
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
