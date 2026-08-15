using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal readonly struct TerrainVirtualTextureDecalData
    {
        internal TerrainVirtualTextureDecalData(
            DecalProjector projector,
            EntityId entityId,
            Bounds worldBounds,
            Matrix4x4 worldToDecal,
            Color baseColor,
            float normalizedBlendDistance,
            float metallic,
            float roughness,
            int drawOrder,
            VividVirtualTextureAsset virtualTextureAsset,
            uint assetContentVersion)
        {
            Projector = projector;
            EntityId = entityId;
            WorldBounds = worldBounds;
            WorldToDecal = worldToDecal;
            BaseColor = baseColor;
            NormalizedBlendDistance = normalizedBlendDistance;
            Metallic = metallic;
            Roughness = roughness;
            DrawOrder = drawOrder;
            VirtualTextureAsset = virtualTextureAsset;
            AssetContentVersion = assetContentVersion;
        }

        internal DecalProjector Projector { get; }
        internal EntityId EntityId { get; }
        internal Bounds WorldBounds { get; }
        internal Matrix4x4 WorldToDecal { get; }
        internal Color BaseColor { get; }
        internal float NormalizedBlendDistance { get; }
        internal float Metallic { get; }
        internal float Roughness { get; }
        internal int DrawOrder { get; }
        internal VividVirtualTextureAsset VirtualTextureAsset { get; }
        internal uint AssetContentVersion { get; }
    }

    internal readonly struct TerrainVirtualTextureDecalDirtyRegion
    {
        internal TerrainVirtualTextureDecalDirtyRegion(
            EntityId entityId,
            bool hasOldBounds,
            Bounds oldBounds,
            bool hasNewBounds,
            Bounds newBounds)
        {
            EntityId = entityId;
            HasOldBounds = hasOldBounds;
            OldBounds = oldBounds;
            HasNewBounds = hasNewBounds;
            NewBounds = newBounds;
        }

        internal EntityId EntityId { get; }
        internal bool HasOldBounds { get; }
        internal Bounds OldBounds { get; }
        internal bool HasNewBounds { get; }
        internal Bounds NewBounds { get; }

        internal Bounds UnionBounds
        {
            get
            {
                if (!HasOldBounds)
                    return NewBounds;
                if (!HasNewBounds)
                    return OldBounds;

                Bounds union = OldBounds;
                union.Encapsulate(NewBounds);
                return union;
            }
        }
    }

    internal readonly struct TerrainVirtualTextureDecalSnapshot
    {
        internal TerrainVirtualTextureDecalSnapshot(
            uint revision,
            IReadOnlyList<TerrainVirtualTextureDecalData> decals,
            IReadOnlyList<TerrainVirtualTextureDecalDirtyRegion> dirtyRegions)
        {
            Revision = revision;
            Decals = decals;
            DirtyRegions = dirtyRegions;
        }

        internal uint Revision { get; }
        internal IReadOnlyList<TerrainVirtualTextureDecalData> Decals { get; }
        internal IReadOnlyList<TerrainVirtualTextureDecalDirtyRegion> DirtyRegions { get; }
    }

    internal sealed class DecalSystem : VividSubsystem<DecalSystem>
    {
        private const int k_InlineJobDecalThreshold = 64;
        private const int k_PrepareJobBatchSize = 32;
        private const int k_CullJobBatchSize = 64;
        private static readonly Quaternion s_ProjectorToDecalSpaceRotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);

        private readonly List<DecalProjector> m_Projectors = new();
        private readonly List<TerrainVirtualTextureDecalData> m_VirtualTextureDecals = new();
        private readonly List<TerrainVirtualTextureDecalDirtyRegion> m_VirtualTextureDirtyRegions = new();
        private readonly Dictionary<EntityId, TerrainVirtualTextureDecalData> m_PreviousVirtualTextureDecals = new();
        private readonly Dictionary<EntityId, TerrainVirtualTextureDecalData> m_CurrentVirtualTextureDecals = new();

        // Prepared snapshot produced by the PlayerLoop kick (or first SRP update) and consumed by SRP UpdateCore.
        // m_SourceProjectors[i] / m_Prepared[i] / m_CullingInstances[i] correspond to m_Sources[i] for i < m_SourceCount.
        private NativeArray<DecalSourceData> m_Sources;
        private NativeArray<DecalPreparedData> m_Prepared;
        private NativeArray<CullingInstance> m_CullingInstances;
        private NativeArray<float4> m_FrustumPlanes;
        private NativeList<int> m_VisibleIndices;
        private DecalProjector[] m_SourceProjectors = System.Array.Empty<DecalProjector>();
        private int[] m_VisibleSortIndices = System.Array.Empty<int>();
        private int m_SourceCount;
        private uint m_VirtualTextureRevision = 1u;
        private bool m_TerrainVirtualTextureConfigurationWarningIssued;
        private JobHandle m_PreparedJobHandle;
        private JobHandle m_CullJobHandle;
        private EntityId m_CullScheduledForCameraId;
        private bool m_PrepareScheduled;
        private bool m_CullScheduled;
        private bool m_CullReady;
        private bool m_KickRan;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            InsertIntoPlayerLoop();
        }

        protected override void OnDeinitialize()
        {
            CompleteScheduledWork();
            m_KickRan = false;

            RemoveFromPlayerLoop();

            if (m_Sources.IsCreated)
                m_Sources.Dispose();
            if (m_Prepared.IsCreated)
                m_Prepared.Dispose();
            if (m_CullingInstances.IsCreated)
                m_CullingInstances.Dispose();
            if (m_FrustumPlanes.IsCreated)
                m_FrustumPlanes.Dispose();
            if (m_VisibleIndices.IsCreated)
                m_VisibleIndices.Dispose();
            for (int i = 0; i < m_SourceProjectors.Length; i++)
                m_SourceProjectors[i] = null;
            m_SourceCount = 0;

            m_Projectors.Clear();
            m_VirtualTextureDecals.Clear();
            m_VirtualTextureDirtyRegions.Clear();
            m_PreviousVirtualTextureDecals.Clear();
            m_CurrentVirtualTextureDecals.Clear();
        }

        internal static void Register(DecalProjector projector)
        {
            if (!IsInitialized)
                Initialize();

            if (projector == null)
                return;

            var projectors = Instance.m_Projectors;
            if (!projectors.Contains(projector))
                projectors.Add(projector);
        }

        internal static void Unregister(DecalProjector projector)
        {
            if (!HasInstance)
                return;

            Instance.m_Projectors.Remove(projector);
        }

        private static void PlayerLoopKick()
        {
            if (!IsInitialized)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalKickMarker.Auto())
            {
                Instance.BuildSnapshotAndSchedulePrepare(true);
            }
        }

        private void BuildSnapshotAndSchedulePrepare(bool allowAsyncSchedule)
        {
            // Drain any leftover cull/prepare from the previous frame (e.g. SRP swapped, no camera rendered).
            CompleteScheduledWork();

            BuildVirtualTextureSnapshot();

            int projectorCount = m_Projectors.Count;
            EnsureSnapshotCapacity(projectorCount);

            int sourceCount = 0;
            for (int i = 0; i < projectorCount; i++)
            {
                DecalProjector projector = m_Projectors[i];
                if (projector == null || !projector.isActiveAndEnabled)
                    continue;

                if (!projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData wd))
                    continue;

                m_SourceProjectors[sourceCount] = projector;
                m_Sources[sourceCount] = new DecalSourceData
                {
                    worldCenter = wd.worldCenter,
                    worldRotation = wd.worldRotation,
                    boxSize = wd.boxSize,
                    baseColor = (Vector4)projector.BaseColor,
                    blendDistance = projector.BlendDistance,
                    metallic = projector.Metallic,
                    roughness = projector.Roughness,
                    drawOrder = projector.DrawOrder,
                    stableId = EntityId.ToULong(wd.entityId),
                };
                sourceCount++;
            }

            m_SourceCount = sourceCount;
            m_KickRan = true;

            if (sourceCount == 0)
                return;

            var job = new PrepareDecalsJob
            {
                Sources = m_Sources,
                Prepared = m_Prepared,
                CullingInstances = m_CullingInstances,
            };

            if (!allowAsyncSchedule || ShouldRunInline(sourceCount))
            {
                job.Run(sourceCount);
                return;
            }

            m_PreparedJobHandle = job.Schedule(sourceCount, k_PrepareJobBatchSize);
            m_PrepareScheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }

        private void EnsureSnapshotCapacity(int requiredCapacity)
        {
            int current = m_Sources.IsCreated ? m_Sources.Length : 0;
            if (current >= requiredCapacity
                && m_Prepared.IsCreated && m_Prepared.Length >= requiredCapacity
                && m_CullingInstances.IsCreated && m_CullingInstances.Length >= requiredCapacity
                && m_VisibleIndices.IsCreated && m_VisibleIndices.Capacity >= requiredCapacity)
                return;

            int newCapacity = math.max(8, math.ceilpow2(requiredCapacity));

            if (m_Sources.IsCreated)
                m_Sources.Dispose();
            if (m_Prepared.IsCreated)
                m_Prepared.Dispose();
            if (m_CullingInstances.IsCreated)
                m_CullingInstances.Dispose();
            if (m_VisibleIndices.IsCreated)
                m_VisibleIndices.Dispose();

            m_Sources = new NativeArray<DecalSourceData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Prepared = new NativeArray<DecalPreparedData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_CullingInstances = new NativeArray<CullingInstance>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_VisibleIndices = new NativeList<int>(newCapacity, Allocator.Persistent);

            if (!m_FrustumPlanes.IsCreated)
                m_FrustumPlanes = new NativeArray<float4>(6, Allocator.Persistent);

            if (m_SourceProjectors.Length < newCapacity)
            {
                var grown = new DecalProjector[newCapacity];
                System.Array.Copy(m_SourceProjectors, grown, m_SourceProjectors.Length);
                m_SourceProjectors = grown;
            }

            if (m_VisibleSortIndices.Length < newCapacity)
                System.Array.Resize(ref m_VisibleSortIndices, newCapacity);
        }

        internal static void ScheduleCullForCamera(Camera camera)
        {
            if (!IsInitialized || camera == null)
                return;

            DecalSystem instance = Instance;
            if (!instance.m_KickRan)
                return;

            // Persistent cull buffers are shared across cameras; drain any pending slot before reusing them.
            if (instance.m_CullScheduled || instance.m_CullReady)
                instance.CompleteScheduledWork();

            if (instance.m_SourceCount == 0)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCullScheduleMarker.Auto())
            {
                instance.ScheduleOrRunCull(camera);
            }
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!m_KickRan)
                BuildSnapshotAndSchedulePrepare(false);

            Camera camera = GetCamera(frameData);
            if (camera == null || m_SourceCount == 0)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
                {
                    CompleteScheduledWork();
                }
                UpdateLightDataEmpty(frameData);
                return;
            }

            // Cull was scheduled by RenderCamera for this camera — just join the result.
            EntityId cameraId = camera.GetEntityId();
            if ((m_CullScheduled || m_CullReady) && m_CullScheduledForCameraId == cameraId)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
                {
                    CompleteScheduledWork();
                }

                EmitVisibleDecals(frameData, m_VisibleIndices.AsArray());
                return;
            }

            // Miss path: prepared data is ready but cull was not scheduled (or was scheduled for a different camera).
            // Complete leftover cull, then run cull inline using the persistent buffers.
            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
            {
                CompleteScheduledWork();
            }

            RunCullInlineOrComplete(camera);

            EmitVisibleDecals(frameData, m_VisibleIndices.AsArray());
        }

        private static bool ShouldRunInline(int decalCount)
        {
            return decalCount <= k_InlineJobDecalThreshold;
        }

        private void CompleteScheduledWork()
        {
            bool completedCull = m_CullScheduled;
            if (m_CullScheduled)
                m_CullJobHandle.Complete();
            m_CullJobHandle = default;
            m_CullScheduled = false;
            m_CullReady = false;
            m_CullScheduledForCameraId = default;

            if (!completedCull && m_PrepareScheduled)
                m_PreparedJobHandle.Complete();

            m_PreparedJobHandle = default;
            m_PrepareScheduled = false;
        }

        private void ScheduleOrRunCull(Camera camera)
        {
            var cullingJob = CreateFrustumCullingJob(camera);
            EntityId cameraId = camera.GetEntityId();

            if (!m_PrepareScheduled && ShouldRunInline(m_SourceCount))
            {
                cullingJob.Run(m_SourceCount);
                m_CullReady = true;
                m_CullScheduledForCameraId = cameraId;
                return;
            }

            m_CullJobHandle = cullingJob.Schedule(m_SourceCount, k_CullJobBatchSize, m_PreparedJobHandle);
            m_CullScheduled = true;
            m_CullScheduledForCameraId = cameraId;
            JobHandle.ScheduleBatchedJobs();
        }

        private void RunCullInlineOrComplete(Camera camera)
        {
            var cullingJob = CreateFrustumCullingJob(camera);
            if (ShouldRunInline(m_SourceCount))
            {
                cullingJob.Run(m_SourceCount);
                return;
            }

            cullingJob.Schedule(m_SourceCount, k_CullJobBatchSize).Complete();
        }

        private FrustumCullingJob CreateFrustumCullingJob(Camera camera)
        {
            float4x4 viewProj = (float4x4)(camera.projectionMatrix * camera.worldToCameraMatrix);
            CullingUtility.ExtractFrustumPlanes(viewProj, m_FrustumPlanes);

            m_VisibleIndices.Clear();
            if (m_VisibleIndices.Capacity < m_SourceCount)
                m_VisibleIndices.SetCapacity(m_SourceCount);

            return new FrustumCullingJob
            {
                FrustumPlanes = m_FrustumPlanes,
                Instances = m_CullingInstances.GetSubArray(0, m_SourceCount),
                VisibleIndices = m_VisibleIndices.AsParallelWriter(),
            };
        }

        private void EmitVisibleDecals(ContextContainer frameData, NativeArray<int> visibleIndices)
        {
            var gpuDrivenDecalData = frameData.GetOrCreate<VividGPUDrivenDecalData>();
            var gpuDrivenDecalEnabled = TryResolveGPUDrivenDecalBindlessContainer(out var bindlessTextureContainer);
            gpuDrivenDecalData.isEnabled = gpuDrivenDecalEnabled;

            var lightData = frameData.GetOrCreate<VividLightData>();
            int visibleCount = gpuDrivenDecalEnabled ? visibleIndices.Length : 0;
            lightData.decalCount = visibleCount;

            if (visibleCount == 0)
                return;

            if (lightData.decalClusterData.Length < visibleCount)
                lightData.decalClusterData = new VividLightData.DecalClusterData[visibleCount];

            for (int i = 0; i < visibleCount; i++)
                m_VisibleSortIndices[i] = visibleIndices[i];
            SortVisibleIndices(m_VisibleSortIndices, visibleCount, m_Prepared);

            for (int i = 0; i < visibleCount; i++)
            {
                int idx = m_VisibleSortIndices[i];
                DecalProjector projector = m_SourceProjectors[idx];
                DecalPreparedData prepared = m_Prepared[idx];

                float4x4 m = prepared.worldToDecal;
                Matrix4x4 worldToDecal = new(m.c0, m.c1, m.c2, m.c3);

                lightData.decalClusterData[i] = new VividLightData.DecalClusterData
                {
                    worldToDecal = worldToDecal,
                    baseColor = prepared.baseColor,
                    baseColorTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.BaseColorTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    normalTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.NormalTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    metallicTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.MetallicTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    roughnessTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.RoughnessTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    blendDistance = prepared.normalizedBlendDistance,
                    metallic = prepared.clampedMetallic,
                    roughness = prepared.clampedRoughness,
                    padding = 0f,
                };
            }
        }

        private static void UpdateLightDataEmpty(ContextContainer frameData)
        {
            if (frameData == null)
                return;

            var gpuDrivenDecalData = frameData.GetOrCreate<VividGPUDrivenDecalData>();
            gpuDrivenDecalData.isEnabled = TryResolveGPUDrivenDecalBindlessContainer(out _);

            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.decalCount = 0;
        }

        internal static VividLightData.DecalClusterData CreateDecalClusterData(
            DecalData decal,
            bool gpuDrivenDecalEnabled,
            BindlessTextureContainer bindlessTextureContainer)
        {
            return new VividLightData.DecalClusterData
            {
                worldToDecal = decal.worldToDecal,
                baseColor = decal.baseColor,
                baseColorTextureIndex = ResolveBindlessTextureIndex(
                    decal.baseColorTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                normalTextureIndex = ResolveBindlessTextureIndex(
                    decal.normalTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                metallicTextureIndex = ResolveBindlessTextureIndex(
                    decal.metallicTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                roughnessTextureIndex = ResolveBindlessTextureIndex(
                    decal.roughnessTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                blendDistance = decal.blendDistance,
                metallic = Mathf.Clamp01(decal.metallic),
                roughness = Mathf.Clamp01(decal.roughness),
                padding = 0f,
            };
        }

        internal static Matrix4x4 CreateWorldToDecalMatrix(in BoundProxyWorldData worldData)
        {
            // Match HDRP's decal space: authoring local Z is projection depth, shader samples the XZ plane.
            Vector3 decalSpaceSize = new(worldData.boxSize.x, worldData.boxSize.z, worldData.boxSize.y);
            Quaternion decalSpaceRotation = worldData.worldRotation * s_ProjectorToDecalSpaceRotation;
            return Matrix4x4.TRS(worldData.worldCenter, decalSpaceRotation, decalSpaceSize).inverse;
        }

        internal static float NormalizeBlendDistance(float blendDistance, Vector3 boxSize)
        {
            if (blendDistance <= 0.0f)
                return 0.0f;

            var minDimension = Mathf.Min(
                Mathf.Abs(boxSize.x),
                Mathf.Abs(boxSize.y));

            if (minDimension <= 1e-5f)
                return 0.0f;

            return Mathf.Clamp(blendDistance / minDimension, 0.0f, 0.5f);
        }

        private static bool TryResolveGPUDrivenDecalBindlessContainer(out BindlessTextureContainer bindlessTextureContainer)
        {
            bindlessTextureContainer = null;

            var asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset == null
                || !asset.EnableGPUDriven
                || !asset.EnableGPUDrivenDecal
                || asset.DecalTechnique != VividDecalTechnique.ClusteredBindless)
                return false;

            var gpuDrivenSystem = VividGPUDrivenSystem.instance;
            if (gpuDrivenSystem == null || !gpuDrivenSystem.IsAvailable)
                return false;

            bindlessTextureContainer = gpuDrivenSystem.BindlessTextureContainer;
            return bindlessTextureContainer != null && bindlessTextureContainer.IsAvailable;
        }

        internal static TerrainVirtualTextureDecalSnapshot GetTerrainVirtualTextureSnapshot()
        {
            if (!IsInitialized)
            {
                return new TerrainVirtualTextureDecalSnapshot(
                    0u,
                    System.Array.Empty<TerrainVirtualTextureDecalData>(),
                    System.Array.Empty<TerrainVirtualTextureDecalDirtyRegion>());
            }

            DecalSystem decalSystem = Instance;
            return new TerrainVirtualTextureDecalSnapshot(
                decalSystem.m_VirtualTextureRevision,
                decalSystem.m_VirtualTextureDecals,
                decalSystem.m_VirtualTextureDirtyRegions);
        }

#if UNITY_INCLUDE_TESTS
        internal static void RebuildTerrainVirtualTextureSnapshotForTesting()
        {
            if (!IsInitialized)
                Initialize();

            Instance.BuildSnapshotAndSchedulePrepare(false);
        }
#endif

        internal static int CompareStableOrder(
            int leftDrawOrder,
            ulong leftStableId,
            int rightDrawOrder,
            ulong rightStableId)
        {
            int drawOrderComparison = leftDrawOrder.CompareTo(rightDrawOrder);
            return drawOrderComparison != 0
                ? drawOrderComparison
                : leftStableId.CompareTo(rightStableId);
        }

        private static void SortVisibleIndices(
            int[] indices,
            int count,
            NativeArray<DecalPreparedData> prepared)
        {
            for (int i = 1; i < count; i++)
            {
                int value = indices[i];
                DecalPreparedData valueData = prepared[value];
                int insertIndex = i - 1;
                while (insertIndex >= 0)
                {
                    DecalPreparedData candidateData = prepared[indices[insertIndex]];
                    if (CompareStableOrder(
                            candidateData.drawOrder,
                            candidateData.stableId,
                            valueData.drawOrder,
                            valueData.stableId) <= 0)
                    {
                        break;
                    }

                    indices[insertIndex + 1] = indices[insertIndex];
                    insertIndex--;
                }

                indices[insertIndex + 1] = value;
            }
        }

        private void BuildVirtualTextureSnapshot()
        {
            WarnInvalidTerrainVirtualTextureConfiguration();
            m_VirtualTextureDecals.Clear();
            m_VirtualTextureDirtyRegions.Clear();
            m_CurrentVirtualTextureDecals.Clear();

            for (int projectorIndex = 0; projectorIndex < m_Projectors.Count; projectorIndex++)
            {
                DecalProjector projector = m_Projectors[projectorIndex];
                if (projector == null
                    || !projector.isActiveAndEnabled
                    || !projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData))
                {
                    continue;
                }

                VividVirtualTextureAsset asset = projector.VirtualTextureAsset;
                var data = new TerrainVirtualTextureDecalData(
                    projector,
                    worldData.entityId,
                    worldData.worldAabb,
                    CreateWorldToDecalMatrix(worldData),
                    projector.BaseColor,
                    NormalizeBlendDistance(projector.BlendDistance, worldData.boxSize),
                    projector.Metallic,
                    projector.Roughness,
                    projector.DrawOrder,
                    asset,
                    asset != null ? asset.ContentVersion : 0u);
                m_CurrentVirtualTextureDecals[worldData.entityId] = data;
                m_VirtualTextureDecals.Add(data);

                if (!m_PreviousVirtualTextureDecals.TryGetValue(
                        worldData.entityId,
                        out TerrainVirtualTextureDecalData previous))
                {
                    m_VirtualTextureDirtyRegions.Add(new TerrainVirtualTextureDecalDirtyRegion(
                        worldData.entityId,
                        false,
                        default,
                        true,
                        data.WorldBounds));
                }
                else if (!VirtualTextureDataEquals(previous, data))
                {
                    m_VirtualTextureDirtyRegions.Add(new TerrainVirtualTextureDecalDirtyRegion(
                        worldData.entityId,
                        true,
                        previous.WorldBounds,
                        true,
                        data.WorldBounds));
                }
            }

            foreach (KeyValuePair<EntityId, TerrainVirtualTextureDecalData> previousPair
                     in m_PreviousVirtualTextureDecals)
            {
                if (m_CurrentVirtualTextureDecals.ContainsKey(previousPair.Key))
                    continue;

                m_VirtualTextureDirtyRegions.Add(new TerrainVirtualTextureDecalDirtyRegion(
                    previousPair.Key,
                    true,
                    previousPair.Value.WorldBounds,
                    false,
                    default));
            }

            m_VirtualTextureDecals.Sort(CompareVirtualTextureDecals);
            if (m_VirtualTextureDirtyRegions.Count > 0)
                IncrementVirtualTextureRevision();

            m_PreviousVirtualTextureDecals.Clear();
            foreach (KeyValuePair<EntityId, TerrainVirtualTextureDecalData> currentPair
                     in m_CurrentVirtualTextureDecals)
            {
                m_PreviousVirtualTextureDecals.Add(currentPair.Key, currentPair.Value);
            }
        }

        private void WarnInvalidTerrainVirtualTextureConfiguration()
        {
            if (m_TerrainVirtualTextureConfigurationWarningIssued)
                return;

            VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset == null
                || !asset.EnableGPUDrivenDecal
                || asset.DecalTechnique != VividDecalTechnique.TerrainRuntimeVirtualTexture
                || asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string reason))
            {
                return;
            }

            m_TerrainVirtualTextureConfigurationWarningIssued = true;
            Debug.LogWarning(
                $"[VividRP] Terrain Runtime Virtual Texture decals are disabled: {reason} "
                + "The renderer will not fall back to Clustered Bindless decals.",
                asset);
        }

        private static int CompareVirtualTextureDecals(
            TerrainVirtualTextureDecalData left,
            TerrainVirtualTextureDecalData right)
        {
            return CompareStableOrder(
                left.DrawOrder,
                EntityId.ToULong(left.EntityId),
                right.DrawOrder,
                EntityId.ToULong(right.EntityId));
        }

        internal static bool VirtualTextureDataEquals(
            in TerrainVirtualTextureDecalData left,
            in TerrainVirtualTextureDecalData right)
        {
            return left.WorldBounds.Equals(right.WorldBounds)
                   && left.WorldToDecal == right.WorldToDecal
                   && left.BaseColor == right.BaseColor
                   && left.NormalizedBlendDistance.Equals(right.NormalizedBlendDistance)
                   && left.Metallic.Equals(right.Metallic)
                   && left.Roughness.Equals(right.Roughness)
                   && left.DrawOrder == right.DrawOrder
                   && ReferenceEquals(left.VirtualTextureAsset, right.VirtualTextureAsset)
                   && left.AssetContentVersion == right.AssetContentVersion;
        }

        private void IncrementVirtualTextureRevision()
        {
            unchecked
            {
                m_VirtualTextureRevision++;
                if (m_VirtualTextureRevision == 0u)
                    m_VirtualTextureRevision = 1u;
            }
        }

        private static uint ResolveBindlessTextureIndex(
            Texture texture,
            bool gpuDrivenDecalEnabled,
            BindlessTextureContainer bindlessTextureContainer)
        {
            if (texture == null || !gpuDrivenDecalEnabled || bindlessTextureContainer == null)
                return BindlessTextureContainer.InvalidTextureIndex;

            return bindlessTextureContainer.TryGetOrCreateIndex(texture, out var textureIndex)
                ? textureIndex
                : BindlessTextureContainer.InvalidTextureIndex;
        }

        private static Camera GetCamera(ContextContainer frameData)
        {
            if (frameData == null || !frameData.Contains<VividCameraData>())
                return null;

            return frameData.Get<VividCameraData>().camera;
        }

        private static void InsertIntoPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length + 1);
                bool alreadyPresent = false;
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type == typeof(DecalSystemPlayerLoopMarker))
                        alreadyPresent = true;
                    updatedSubSystems.Add(nestedSystem);
                }

                if (!alreadyPresent)
                    updatedSubSystems.Add(CreatePlayerLoopSystem());

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static void RemoveFromPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length);
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type != typeof(DecalSystemPlayerLoopMarker))
                        updatedSubSystems.Add(nestedSystem);
                }

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static PlayerLoopSystem CreatePlayerLoopSystem()
        {
            return new PlayerLoopSystem
            {
                type = typeof(DecalSystemPlayerLoopMarker),
                updateDelegate = PlayerLoopKick,
            };
        }

        private sealed class DecalSystemPlayerLoopMarker
        {
        }
    }
}
