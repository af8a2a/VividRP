using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using VividRP.Runtime.ECS;
using VividRP.Runtime.Particle.ECS;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VividRP.Runtime.Particle
{
    public static class VividParticleSystemManager
    {
        public const string DefaultShaderName = "VividRP/Particles/Unlit";
        public const string PickingShaderName = DefaultShaderName;

        internal const uint PerInstanceMetadataMask = 0x80000000u;
        internal const int SizeOfFloat4 = sizeof(float) * 4;
        internal const int SizeOfSharedGpuData = SizeOfFloat4 * 2;
        internal const int ZeroBlockByteSize = SizeOfFloat4;
        internal const int BillboardPageSize = VividEcsConstants.PageEntryCount;
        internal const int SharedDataFloat4Count = 14;
        internal const int SharedDataByteSize = SharedDataFloat4Count * SizeOfFloat4;
        internal const int SpanSharedDataByteSize = SizeOfFloat4;
        internal const int UploadColumnPositionSizeMask = 1 << 0;
        internal const int UploadColumnBaseColorMask = 1 << 1;
        internal const int UploadColumnRotationMask = 1 << 2;
        internal const int UploadColumnVelocityStretchMask = 1 << 3;
        internal const int UploadColumnScaleMask = 1 << 4;
        internal const int UploadColumnUVMask = 1 << 5;
        internal const int UploadColumnCustomData1Mask = 1 << 6;
        internal const int UploadColumnCustomData2Mask = 1 << 7;
        internal const int UploadColumnMeshIndexMask = 1 << 8;
        internal const uint RenderJobTransformUploadFlag = 1u << 0;
        internal const uint RenderJobColorUploadFlag = 1u << 1;
        internal const uint RenderJobVelocityStretchUploadFlag = 1u << 2;
        internal const uint RenderJobUVUploadFlag = 1u << 3;
        internal const uint RenderJobCustomDataUploadFlag = 1u << 4;
        internal const uint RenderJobMeshIndexUploadFlag = 1u << 5;
        internal const uint RenderJobExtraDataUploadFlag = RenderJobUVUploadFlag
            | RenderJobCustomDataUploadFlag
            | RenderJobMeshIndexUploadFlag;
        internal const uint RenderJobSharedDataFlag = 1u << 6;
        internal const uint RenderJobAllPageUploadFlags = RenderJobTransformUploadFlag
            | RenderJobColorUploadFlag
            | RenderJobVelocityStretchUploadFlag
            | RenderJobExtraDataUploadFlag;

        private const int UploadColumnTransformMask = UploadColumnPositionSizeMask
            | UploadColumnRotationMask
            | UploadColumnScaleMask;
        private const int UploadColumnExtraDataMask = UploadColumnUVMask
            | UploadColumnCustomData1Mask
            | UploadColumnCustomData2Mask
            | UploadColumnMeshIndexMask;
        private const int UploadColumnCustomDataMask = UploadColumnCustomData1Mask | UploadColumnCustomData2Mask;
        private const int InstanceDataBufferCount = 3;
        private const int BillboardPageInstanceBaseMask = 0x00ffffff;
        private const int BillboardPageInstanceCountShift = 24;
        private const float GravityAcceleration = 9.81f;
        private const float MinimumSimulationStep = 0.000001f;
        private const float MaximumEditorSimulationStep = 0.1f;

        private static readonly ProfilerMarker s_PlayerLoopKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick");
        private static readonly ProfilerMarker s_KickCompleteSimulationMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick.CompleteSimulation");
        private static readonly ProfilerMarker s_KickCollectActiveMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick.CollectActive");
        private static readonly ProfilerMarker s_KickPrepareSnapshotsMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick.PrepareSnapshots");
        private static readonly ProfilerMarker s_KickScheduleSimulationJobsMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick.ScheduleSimulationJobs");
        private static readonly ProfilerMarker s_KickInitializeEmittedParticlesMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick.InitializeEmittedParticles");
        private static readonly ProfilerMarker s_RendererUpdateMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.RendererUpdate");
        private static readonly ProfilerMarker s_BeginCameraCompleteMarker = new("VividRP.RenderPipeline.BeginCameraRendering/VividParticleSystemManager.Complete");
        private static readonly ProfilerMarker s_ManualDrainMarker = new("VividRP.Particle.Manager.ManualDrain");
        private static readonly ProfilerMarker s_BRGUploadUpdateAllMarker = new("VividRP.Particle.Manager.BRGUpload.UpdateAll");
        private static readonly ProfilerMarker s_BRGUploadUpdateOneMarker = new("VividRP.Particle.Manager.BRGUpload.UpdateOne");

        private static readonly VividEcsManagerJobRegistry<ParticleSimulationJobContext> s_SimulationJobRegistry =
            CreateSimulationJobRegistry();
        private static readonly VividEcsWorld s_ParticleEcsWorld = new();
        private static readonly Dictionary<VividParticleSystem, ParticleSystemState> s_States = new();
        private static readonly List<ParticleSystemState> s_ActiveSimulationStates = new();
        private static readonly Dictionary<ParticleSystemState, int> s_ActiveSimulationIndices = new();
        private static readonly List<ParticleSystemState> s_ActiveRendererStates = new();
        private static readonly Dictionary<ParticleSystemState, int> s_ActiveRendererIndices = new();
        private static readonly List<ParticleSystemState> s_PreparedSimulationStates = new();
        private static readonly List<VividParticleSystemFrameSnapshot> s_PreparedSimulationSnapshots = new();
        private static NativeList<VividParticleSimulationTimeStep> s_PreparedSimulationTimeSteps;
        private static readonly VividParticleRendererManager s_RendererManager = new();
        private static NativeList<VividParticleEcsIntegratePageWork> s_SimulationPageWorks;
        private static NativeList<VividParticleEcsCompactWork> s_SimulationCompactWorks;
        private static NativeList<VividParticleEcsInitializeParticlesWork> s_EmissionInitializeWorks;
        private static JobHandle s_PendingSimulationBatchHandle;
        private static bool s_Initialized;
        private static bool s_HasPendingSimulationBatch;
        private static bool s_ApplyingPendingSimulations;
        private static int s_LastPlayerLoopFrame = -1;
        private static int s_LastRendererUpdateFrame = -1;
        private static int s_LastCompleteAndUploadFrame = -1;
#if UNITY_EDITOR
        private static readonly HashSet<ulong> s_EditorSelectedEntityIds = new();
        private static bool s_EditorSelectionCacheInitialized;
#endif

        public static int registeredSystemCount => s_States.Count;

        internal static int registeredSimulationJobCount => s_SimulationJobRegistry.count;

        internal static int registeredRenderJobCount => VividParticleRenderJobPipeline.registeredJobCount;

        internal static int registeredRenderPageJobDescriptorCount =>
            VividParticleRenderJobPipeline.pageJobDescriptorCount;

        internal static int activeRendererSystemCountForTests => s_ActiveRendererStates.Count;

        internal static int pendingRendererRemoveCountForTests => s_RendererManager.pendingRemoveCount;

        internal static int ResolveRenderJobModuleFlagsForTests(
            bool requestPageUpload,
            bool requestSharedData,
            bool hasPageWorks,
            bool hasSharedDataWorks)
        {
            uint enabledModuleFlags = 0u;
            if (requestPageUpload)
                enabledModuleFlags |= (uint)ParticleRenderJobFlags.AllPageUpload;

            if (requestSharedData)
                enabledModuleFlags |= (uint)ParticleRenderJobFlags.SharedData;

            return (int)VividParticleRenderJobPipeline.FilterEnabledFlags(
                hasPageWorks,
                hasPageWorks,
                hasPageWorks,
                hasPageWorks,
                hasSharedDataWorks,
                enabledModuleFlags);
        }

        internal static int ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(int columnMask)
        {
            return (int)GetRenderJobFlagsForUploadColumnMask(columnMask);
        }

        internal static int CountRenderPageJobModulesForTests(uint renderJobModuleFlags)
        {
            return CountRenderPageJobModules(renderJobModuleFlags);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        public static bool Contains(VividParticleSystem system)
        {
            return system != null && s_States.ContainsKey(system);
        }

        public static void Register(VividParticleSystem system)
        {
            if (system == null)
                return;

            Initialize();
            if (!s_States.ContainsKey(system))
            {
                ParticleSystemState state = new(system);
                s_States.Add(system, state);
                RefreshActiveSimulationState(state);
                RefreshActiveRendererState(state);
            }
        }

        public static void Unregister(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            RemoveActiveSimulationState(state);
            RemoveActiveRendererState(state);
            state.Dispose();
            s_States.Remove(system);
        }

        public static void UpdateSystem(VividParticleSystem system)
        {
            if (system == null)
                return;

            if (Application.isPlaying)
            {
                UpdateSystem(system, Time.deltaTime);
                return;
            }

            UpdateRendering(system);
        }

        internal static void UpdateSystem(VividParticleSystem system, float deltaTime)
        {
            if (system == null)
                return;

            ParticleSystemState state = GetOrCreateState(system);
            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
                state.ScheduleAutomatic(deltaTime, requireActive: false);
                state.CompletePending();
            }

            UploadRenderingState(state, forceUpload: true);
            RequestEditorRenderUpdateIfNeeded(system);
        }

        public static void UpdateRendering(VividParticleSystem system)
        {
            if (system == null)
                return;

            ParticleSystemState state = GetOrCreateState(system);
            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            UploadRenderingState(state, forceUpload: true);
        }

        public static void MarkRendererDirty(VividParticleSystem system)
        {
            if (system != null && s_States.TryGetValue(system, out ParticleSystemState state))
            {
                state.MarkResourcesDirty();
                RefreshActiveRendererState(state);
            }
        }

        internal static void MarkRendererModuleDirty(VividParticleSystem system)
        {
            if (system != null && s_States.TryGetValue(system, out ParticleSystemState state))
            {
                state.MarkRendererModuleDirty();
                RefreshActiveRendererState(state);
            }
        }

        internal static void NotifySimulationStateChanged(VividParticleSystem system)
        {
            if (system != null && s_States.TryGetValue(system, out ParticleSystemState state))
                RefreshActiveSimulationState(state);
        }

        internal static void NotifySettingsChanged(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
            }

            state.NotifySettingsChanged();
            RefreshActiveRendererState(state);
        }

        internal static void Drain(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
            }
        }

        internal static int GetParticleCount(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return 0;

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            return state.activeCount;
        }

        internal static float GetTime(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return 0.0f;

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            return state.time;
        }

        internal static int GetParticleStoragePageSize(VividParticleSystem system)
        {
            return VividEcsConstants.PageEntryCount;
        }

        internal static int GetParticleStorageCapacity(VividParticleSystem system)
        {
            return system != null && s_States.TryGetValue(system, out ParticleSystemState state)
                ? state.storageCapacity
                : 0;
        }

        internal static int GetParticleStoragePageCount(VividParticleSystem system)
        {
            return system != null && s_States.TryGetValue(system, out ParticleSystemState state)
                ? state.storagePageCount
                : 0;
        }

        internal static bool UsesEcsStorage(VividParticleSystem system)
        {
            return system != null && s_States.TryGetValue(system, out ParticleSystemState state) && state.usesEcsStorage;
        }

        internal static Matrix4x4 GetParticleObjectToWorldMatrix(VividParticleSystem system, int particleIndex)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return Matrix4x4.identity;

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            return state.GetParticleObjectToWorldMatrix(particleIndex);
        }

        internal static Color GetParticleRenderColor(VividParticleSystem system, int particleIndex)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return Color.clear;

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            return state.GetParticleRenderColor(particleIndex);
        }

        internal static Bounds GetWorldBounds(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return new Bounds(system != null ? system.transform.position : Vector3.zero, Vector3.zero);

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
            }

            return state.GetWorldBounds();
        }

        internal static void Emit(VividParticleSystem system, int count)
        {
            if (system == null || count <= 0)
                return;

            ParticleSystemState state = GetOrCreateState(system);
            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
                state.Emit(count, state.CaptureFrameSnapshot(0.0f));
            }

            RefreshActiveRendererState(state);
            UploadRenderingState(state, forceUpload: true);
        }

        internal static void Simulate(
            VividParticleSystem system,
            float t,
            bool restart,
            bool fixedTimeStep)
        {
            if (system == null)
                return;

            ParticleSystemState state = GetOrCreateState(system);
            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();

                if (restart)
                    state.ResetSimulation(state.CaptureFrameSnapshot(0.0f), clearParticles: true);

                if (t > 0.0f)
                {
                    float remaining = t;
                    if (fixedTimeStep)
                    {
                        while (remaining > MinimumSimulationStep)
                        {
                            float step = Mathf.Min(VividParticleSystem.FixedSimulationStep, remaining);
                            state.SimulateDeltaImmediate(state.CaptureFrameSnapshot(step), allowEmission: true);
                            remaining -= step;
                        }
                    }
                    else
                    {
                        state.SimulateDeltaImmediate(state.CaptureFrameSnapshot(remaining), allowEmission: true);
                    }
                }
            }

            RefreshActiveRendererState(state);
            UploadRenderingState(state, forceUpload: true);
        }

        internal static void SimulateDeltaImmediate(
            VividParticleSystem system,
            float deltaTime,
            bool allowEmission)
        {
            if (system == null || deltaTime <= 0.0f)
                return;

            ParticleSystemState state = GetOrCreateState(system);
            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
                state.SimulateDeltaImmediate(state.CaptureFrameSnapshot(deltaTime), allowEmission);
            }

            RefreshActiveRendererState(state);
            UploadRenderingState(state, forceUpload: true);
        }

        internal static void ResetSimulation(VividParticleSystem system, bool clearParticles)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            using (s_ManualDrainMarker.Auto())
            {
                s_RendererManager.CompletePendingUpload();
                state.CompletePending();
                state.ResetSimulation(state.CaptureFrameSnapshot(0.0f), clearParticles);
                RefreshActiveSimulationState(state);
                RefreshActiveRendererState(state);
            }
        }

        internal static void ResetEditorUpdateTime(VividParticleSystem system)
        {
            if (system != null && s_States.TryGetValue(system, out ParticleSystemState state))
                state.ResetEditorUpdateTime();
        }

        internal static void RunPlayerLoopForTests(float deltaTime)
        {
            PlayerLoopKick(deltaTime);
        }

        internal static void RunRendererUpdateForTests()
        {
            RendererUpdateKick();
        }

        internal static void CompletePendingRendererUploadForTests()
        {
            CompletePendingUploadForRendering(oncePerFrame: false);
        }

        internal static void CompleteAndUploadForTests()
        {
            CompleteAndUploadAll(forceUpload: true, oncePerFrame: false);
        }

        internal static void ClearForTests()
        {
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.Dispose();

            s_States.Clear();
            s_ActiveSimulationStates.Clear();
            s_ActiveSimulationIndices.Clear();
            s_ActiveRendererStates.Clear();
            s_ActiveRendererIndices.Clear();
            s_PreparedSimulationStates.Clear();
            s_PreparedSimulationSnapshots.Clear();
            if (s_PreparedSimulationTimeSteps.IsCreated)
                s_PreparedSimulationTimeSteps.Dispose();
            if (s_SimulationPageWorks.IsCreated)
                s_SimulationPageWorks.Dispose();
            s_PreparedSimulationTimeSteps = default;
            if (s_SimulationCompactWorks.IsCreated)
                s_SimulationCompactWorks.Dispose();
            if (s_EmissionInitializeWorks.IsCreated)
                s_EmissionInitializeWorks.Dispose();
            s_SimulationPageWorks = default;
            s_SimulationCompactWorks = default;
            s_EmissionInitializeWorks = default;
            s_PendingSimulationBatchHandle = default;
            s_HasPendingSimulationBatch = false;
            s_ApplyingPendingSimulations = false;
            s_RendererManager.Dispose();
            s_ParticleEcsWorld.Dispose();
            s_LastPlayerLoopFrame = -1;
            s_LastRendererUpdateFrame = -1;
            s_LastCompleteAndUploadFrame = -1;
#if UNITY_EDITOR
            s_EditorSelectedEntityIds.Clear();
            s_EditorSelectionCacheInitialized = false;
#endif
        }

        internal static bool TryGetStats(
            VividParticleSystem system,
            out VividParticleSystemManagerStats stats)
        {
            stats = default;
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return false;

            s_RendererManager.CompletePendingUpload();
            s_RendererManager.DrainCullingResults();
            stats = state.stats;
            return true;
        }

        internal static bool TryGetRuntimeStats(
            VividParticleSystem system,
            out VividParticleSystemRuntimeStats stats)
        {
            stats = default;
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return false;

            stats = state.runtimeStats;
            return true;
        }

        internal static VividParticleRendererManagerStats GetRendererStats()
        {
            return s_RendererManager.stats;
        }

        internal static VividParticleRendererManagerStats GetRendererStatsForTests()
        {
            s_RendererManager.CompletePendingUpload();
            s_RendererManager.DrainCullingResults();
            return GetRendererStats();
        }

        internal static int[] GetMeshVisibleCountsForTests()
        {
            s_RendererManager.CompletePendingUpload();
            s_RendererManager.DrainCullingResults();
            return s_RendererManager.GetMeshVisibleCountsSnapshot();
        }

        internal static bool MarkFirstRendererBatchZeroBlockDirtyForTests()
        {
            return s_RendererManager.MarkFirstBatchZeroBlockDirtyForTests();
        }

        internal static bool HasRendererPickingMaterialForTests()
        {
            return s_RendererManager.hasPickingMaterial;
        }

        internal static bool HasPendingRendererUploadForTests()
        {
            return s_RendererManager.hasPendingUpload;
        }

        internal static MetadataValue CreatePerInstanceMetadata(int nameId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = nameId,
                Value = PerInstanceMetadataMask | (uint)byteAddress,
            };
        }

        internal static MetadataValue CreateSharedMetadata(int nameId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = nameId,
                Value = (uint)byteAddress,
            };
        }

        internal static int PositionSizeByteAddress(int capacity)
        {
            return ZeroBlockByteSize;
        }

        internal static int BaseColorByteAddress(int capacity)
        {
            return PositionSizeByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
        }

        internal static int RotationByteAddress(int capacity)
        {
            return BaseColorByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
        }

        internal static int VelocityStretchByteAddress(int capacity)
        {
            return RotationByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
        }

        internal static int SharedRotationByteAddress(int capacity)
        {
            return VelocityStretchByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
        }

        internal static int SharedVelocityStretchByteAddress(int capacity)
        {
            return SharedRotationByteAddress(capacity) + SizeOfFloat4;
        }

        internal static int InstanceDataByteSize(int capacity)
        {
            return SharedVelocityStretchByteAddress(capacity) + SizeOfFloat4;
        }

        internal static bool UsesPageBillboardRenderMode(VividParticleRenderMode renderMode)
        {
            return renderMode is VividParticleRenderMode.Billboard
                or VividParticleRenderMode.Stretch
                or VividParticleRenderMode.HorizontalBillboard
                or VividParticleRenderMode.VerticalBillboard;
        }

        internal static int GetVisibleInstanceCount(VividParticleRenderMode renderMode, int particleCount)
        {
            particleCount = Mathf.Max(0, particleCount);
            return UsesPageBillboardRenderMode(renderMode)
                ? (particleCount + BillboardPageSize - 1) / BillboardPageSize
                : particleCount;
        }

        internal static int GetCullingRecordCount(int particleCount)
        {
            particleCount = Mathf.Max(0, particleCount);
            return (particleCount + BillboardPageSize - 1) / BillboardPageSize;
        }

        internal static int ResolveMeshIndexSlot(int meshIndex, int meshCount)
        {
            meshCount = math.max(1, meshCount);
            return meshIndex < 0 || meshIndex >= meshCount ? 0 : meshIndex;
        }

        internal static bool IsLayerVisibleInCullingMask(uint cullingLayerMask, int layer)
        {
            layer = Mathf.Clamp(layer, 0, 31);
            return (cullingLayerMask & (1u << layer)) != 0u;
        }

        internal static bool HasAnyLayerVisibleInCullingMaskForTests(uint cullingLayerMask, params int[] layers)
        {
            if (layers == null || layers.Length == 0)
                return false;

            for (int index = 0; index < layers.Length; index++)
            {
                if (IsLayerVisibleInCullingMask(cullingLayerMask, layers[index]))
                    return true;
            }

            return false;
        }

        internal static bool CanUseUnfilteredDrawLayout(uint cullingLayerMask)
        {
            return cullingLayerMask == uint.MaxValue;
        }

        internal static bool CanUseUnfilteredDrawLayout(uint cullingLayerMask, uint commandLayerMask)
        {
            return commandLayerMask == 0u || (commandLayerMask & ~cullingLayerMask) == 0u;
        }

        internal static bool HasAnyVisibleCommandLayer(uint cullingLayerMask, uint commandLayerMask)
        {
            return commandLayerMask == 0u || (commandLayerMask & cullingLayerMask) != 0u;
        }

        internal static int GetSortingPositionFloatCount(int visibleInstanceCount)
        {
            return Mathf.Max(0, visibleInstanceCount) * 3;
        }

        internal static bool RequiresSortingPositionsByDefault()
        {
            return false;
        }

        internal static bool RequiresSortingPositions(VividParticleSortMode sortMode)
        {
            return sortMode != VividParticleSortMode.None;
        }

        internal static BatchDrawCommandFlags ResolveParticleDrawCommandFlags(
            bool hasSortingPosition,
            bool hasMotion)
        {
            BatchDrawCommandFlags flags = BatchDrawCommandFlags.None;
            if (hasSortingPosition)
                flags |= BatchDrawCommandFlags.HasSortingPosition;
            if (hasMotion)
                flags |= BatchDrawCommandFlags.HasMotion;
            return flags;
        }

        internal static bool IsPickingOrSelectionView(BatchCullingViewType viewType)
        {
            return viewType is BatchCullingViewType.Picking or BatchCullingViewType.SelectionOutline;
        }

        internal static bool ShouldWritePickingEntityIdsForView(BatchCullingViewType viewType)
        {
            return IsPickingOrSelectionView(viewType);
        }

        internal static bool DoesPickingEntityPassFilterForTests(
            ulong entityId,
            bool includeEnabled,
            ulong[] includeRenderers,
            ulong[] includeEntities,
            ulong[] excludeRenderers,
            ulong[] excludeEntities)
        {
            return DoesPickingEntityPassFilter(
                entityId,
                includeEnabled ? 1 : 0,
                includeRenderers,
                includeEntities,
                excludeRenderers,
                excludeEntities);
        }

        internal static bool ShouldWriteSortingPositionsForView(BatchCullingViewType viewType)
        {
            return viewType == BatchCullingViewType.Camera;
        }

        internal static int ResolveAllDepthSortedFlag(bool hasSortingPosition)
        {
            return hasSortingPosition ? 1 : 0;
        }

        internal static float3 ResolveParticleSortingPosition(
            float3 position,
            float4x4 localToWorld,
            int simulationSpace)
        {
            return simulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.transform(localToWorld, position)
                : position;
        }

        internal static float3 ResolvePageSortingPosition(
            float3 pageBoundsCenter,
            float3 firstParticlePosition,
            float4x4 localToWorld,
            int simulationSpace,
            bool hasFirstParticle)
        {
            return hasFirstParticle
                ? ResolveParticleSortingPosition(firstParticlePosition, localToWorld, simulationSpace)
                : pageBoundsCenter;
        }

        internal static int ResolveSplitVisibilityMaskForView(
            BatchCullingViewType viewType,
            int splitVisibilityMask,
            int splitCount)
        {
            if (viewType != BatchCullingViewType.Light || splitCount <= 0)
                return 0xff;

            return splitVisibilityMask & 0xff;
        }

        private static bool DoesPickingEntityPassFilter(
            ulong entityId,
            int includeEnabled,
            ulong[] includeRenderers,
            ulong[] includeEntities,
            ulong[] excludeRenderers,
            ulong[] excludeEntities)
        {
            if (includeEnabled != 0
                && !ContainsEntityId(includeRenderers, entityId)
                && !ContainsEntityId(includeEntities, entityId))
            {
                return false;
            }

            if (ContainsEntityId(excludeRenderers, entityId)
                || ContainsEntityId(excludeEntities, entityId))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsEntityId(ulong[] entityIds, ulong entityId)
        {
            if (entityIds == null)
                return false;

            for (int index = 0; index < entityIds.Length; index++)
            {
                if (entityIds[index] == entityId)
                    return true;
            }

            return false;
        }

        internal static bool ShouldRenderBatchForView(
            ShadowCastingMode shadowCastingMode,
            BatchCullingViewType viewType)
        {
            return viewType != BatchCullingViewType.Light || shadowCastingMode != ShadowCastingMode.Off;
        }

        internal static bool ShouldKeepDrawCommandForCulling(
            uint cullingLayerMask,
            int layer,
            int recordCount,
            int maxVisibleCount,
            ShadowCastingMode shadowCastingMode,
            BatchCullingViewType viewType)
        {
            return recordCount > 0
                && maxVisibleCount > 0
                && IsLayerVisibleInCullingMask(cullingLayerMask, layer)
                && ShouldRenderBatchForView(shadowCastingMode, viewType);
        }

        internal static void CalculateFilteredDrawLayoutCountsForTests(
            uint cullingLayerMask,
            BatchCullingViewType viewType,
            int[] layers,
            int[] visibleCounts,
            bool[] hasSortingPositions,
            out int drawCommandCount,
            out int drawRangeCount,
            out int visibleInstanceCount,
            out int sortingPositionCount)
        {
            int commandLength = layers?.Length ?? 0;
            var commands = new ParticleDrawCommandInput[commandLength];
            for (int index = 0; index < commandLength; index++)
            {
                int visibleCount = visibleCounts != null && index < visibleCounts.Length ? visibleCounts[index] : 0;
                bool sorting = hasSortingPositions != null && index < hasSortingPositions.Length && hasSortingPositions[index];
                commands[index] = new ParticleDrawCommandInput
                {
                    RecordCount = visibleCount > 0 ? 1 : 0,
                    MaxVisibleCount = Mathf.Max(0, visibleCount),
                    Layer = layers[index],
                    RendererPriority = 0,
                    RenderingLayerMask = uint.MaxValue,
                    ShadowCastingMode = ShadowCastingMode.On,
                    MotionMode = MotionVectorGenerationMode.ForceNoMotion,
                    ReceiveShadows = 1,
                    AllDepthSorted = ResolveAllDepthSortedFlag(sorting),
                    HasSortingPositions = sorting ? 1 : 0,
                };
            }

            CountFilteredDrawLayout(
                commands,
                cullingLayerMask,
                viewType,
                out drawCommandCount,
                out drawRangeCount,
                out visibleInstanceCount,
                out sortingPositionCount);
        }

        internal static bool UsesPerInstanceRotationData(VividParticleRenderMode renderMode)
        {
            return false;
        }

        internal static bool UsesPerInstanceRotationData(VividParticleGpuDataMode rotationDataMode)
        {
            return rotationDataMode == VividParticleGpuDataMode.PerParticle;
        }

        internal static bool UsesPerInstanceVelocityStretchData(VividParticleRenderMode renderMode)
        {
            return renderMode == VividParticleRenderMode.Stretch;
        }

        internal static bool UsesPerInstanceVelocityStretchData(
            VividParticleRenderMode renderMode,
            VividParticleGpuDataMode velocityDataMode)
        {
            return renderMode == VividParticleRenderMode.Stretch
                || velocityDataMode == VividParticleGpuDataMode.PerParticle;
        }

        internal static int EncodeBillboardPageVisibleInstance(int baseParticleIndex, int pageParticleCount)
        {
            int count = pageParticleCount < 1 ? 1 : pageParticleCount > BillboardPageSize ? BillboardPageSize : pageParticleCount;
            return (baseParticleIndex & BillboardPageInstanceBaseMask)
                | ((count - 1) << BillboardPageInstanceCountShift);
        }

        internal static uint ResolveBufferWindowSize()
        {
            return BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                ? (uint)BatchRendererGroup.GetConstantBufferMaxWindowSize()
                : 0u;
        }

        internal static int BufferCountForBytes(int byteCount)
        {
            return (byteCount + sizeof(int) - 1) / sizeof(int);
        }

        private static int AlignTo16(int value)
        {
            return (value + 15) & ~15;
        }

        private static int AlignTo4(int value)
        {
            return (value + 3) & ~3;
        }

        private static GraphicsBuffer.Target ResolveBufferTarget()
        {
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Raw;
            if (BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                || SystemInfo.graphicsDeviceType is GraphicsDeviceType.OpenGLCore or GraphicsDeviceType.OpenGLES3)
            {
                target |= GraphicsBuffer.Target.Constant;
            }

            return target;
        }

        internal static bool IntersectsCullingPlanes(Bounds bounds, Plane[] planes)
        {
            if (planes == null || planes.Length == 0)
                return true;

            for (int i = 0; i < planes.Length; i++)
            {
                if (IsOutsidePlane(bounds, planes[i]))
                    return false;
            }

            return true;
        }

        private static void Initialize()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;
            InsertIntoPlayerLoop();
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
#if UNITY_EDITOR
            Selection.selectionChanged -= OnEditorSelectionChanged;
            Selection.selectionChanged += OnEditorSelectionChanged;
#endif
        }

#if UNITY_EDITOR
        private static void OnEditorSelectionChanged()
        {
            RefreshEditorSelectionIdCache();
            bool changedRendererSelection = false;
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
            {
                ParticleSystemState state = pair.Value;
                if (state == null || !state.RefreshEditorSelectionState())
                    continue;

                changedRendererSelection |= s_RendererManager.SyncEditorSelectionState(state);
            }

            if (changedRendererSelection)
            {
                s_RendererManager.RebuildCullingLayoutForEditorSelection();
                s_LastRendererUpdateFrame = -1;
                s_LastCompleteAndUploadFrame = -1;
            }

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        internal static void RefreshEditorSelectionForTests()
        {
            OnEditorSelectionChanged();
        }

        private static void RefreshEditorSelectionIdCache()
        {
            s_EditorSelectedEntityIds.Clear();
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int index = 0; index < selectedObjects.Length; index++)
            {
                UnityEngine.Object selectedObject = selectedObjects[index];
                if (selectedObject != null)
                    s_EditorSelectedEntityIds.Add(EntityId.ToULong(selectedObject.GetEntityId()));
            }

            s_EditorSelectionCacheInitialized = true;
        }
#endif

        private static ParticleSystemState GetOrCreateState(VividParticleSystem system)
        {
            Register(system);
            return s_States[system];
        }

        private static VividEcsManagerJobRegistry<ParticleSimulationJobContext> CreateSimulationJobRegistry()
        {
            var registry = new VividEcsManagerJobRegistry<ParticleSimulationJobContext>();
            registry.RegisterModule(
                "VividParticle.Simulation.Integrate",
                0,
                (uint)ParticleSimulationJobFlags.Integrate,
                ScheduleParticleIntegrateJob);
            return registry;
        }

        private static JobHandle ScheduleParticleIntegrateJob(
            ParticleSimulationJobContext context,
            JobHandle dependency)
        {
            return context.State != null
                ? context.State.ScheduleIntegrateJob(context.Snapshot, dependency)
                : dependency;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_States.Count == 0)
                return;

            using (s_BeginCameraCompleteMarker.Auto())
            {
                if (s_LastPlayerLoopFrame != Time.frameCount)
                    ScheduleAutomaticUpdates(null);

                if (s_LastRendererUpdateFrame != Time.frameCount)
                    ScheduleRendererUpdate(forceUpload: false, oncePerFrame: true);

                CompletePendingUploadForRendering(oncePerFrame: true);
            }
        }

        private static void PlayerLoopKick()
        {
            PlayerLoopKick(null);
        }

        private static void PlayerLoopKick(float? deltaTimeOverride)
        {
            if (s_States.Count == 0)
                return;

            using (s_PlayerLoopKickMarker.Auto())
            {
                ScheduleAutomaticUpdates(deltaTimeOverride);
            }
        }

        private static void RendererUpdateKick()
        {
            if (s_States.Count == 0)
                return;

            using (s_RendererUpdateMarker.Auto())
            {
                ScheduleRendererUpdate(forceUpload: false, oncePerFrame: true);
            }
        }

        private static void ScheduleAutomaticUpdates(float? deltaTimeOverride)
        {
            s_RendererManager.CompletePendingUpload();
            CompletePendingSimulations();

            using (s_KickCollectActiveMarker.Auto())
            {
                PruneActiveSimulationStates();
            }

            s_PreparedSimulationStates.Clear();
            s_PreparedSimulationSnapshots.Clear();
            EnsurePreparedSimulationTimeStepList();
            s_PreparedSimulationTimeSteps.Clear();
            using (s_KickPrepareSnapshotsMarker.Auto())
            {
                for (int index = 0; index < s_ActiveSimulationStates.Count; index++)
                {
                    ParticleSystemState state = s_ActiveSimulationStates[index];
                    if (state == null)
                        continue;

                    if (!state.TryPrepareAutomaticSnapshot(
                        deltaTimeOverride,
                        requireActive: true,
                        out VividParticleSystemFrameSnapshot snapshot,
                        out VividParticleSimulationTimeStep timeStep))
                    {
                        continue;
                    }

                    s_PreparedSimulationStates.Add(state);
                    s_PreparedSimulationSnapshots.Add(snapshot);
                    s_PreparedSimulationTimeSteps.Add(timeStep);
                }
            }

            bool scheduledAnyJob = false;
            bool scheduledAnySimulation = false;
            using (s_KickScheduleSimulationJobsMarker.Auto())
            {
                EnsureSimulationPageWorkList();
                s_SimulationPageWorks.Clear();
                s_SimulationCompactWorks.Clear();
                for (int index = 0; index < s_PreparedSimulationStates.Count; index++)
                {
                    scheduledAnySimulation |= s_PreparedSimulationStates[index].ScheduleAutomaticBatch(
                        s_PreparedSimulationSnapshots[index],
                        s_PreparedSimulationTimeSteps[index],
                        s_SimulationPageWorks,
                        s_SimulationCompactWorks);
                }

                if (s_SimulationPageWorks.Length > 0)
                {
                    var integrateJob = new VividParticleEcsIntegratePageWorksJob
                    {
                        Works = s_SimulationPageWorks.AsArray(),
                    };
                    JobHandle integrateHandle =
                        integrateJob.Schedule(s_SimulationPageWorks.Length, innerloopBatchCount: 1);
                    if (s_SimulationCompactWorks.Length > 0)
                    {
                        var compactJob = new VividParticleEcsCompactWorksJob
                        {
                            Works = s_SimulationCompactWorks.AsArray(),
                        };
                        s_PendingSimulationBatchHandle = compactJob.Schedule(
                            s_SimulationCompactWorks.Length,
                            innerloopBatchCount: 1,
                            integrateHandle);
                    }
                    else
                    {
                        s_PendingSimulationBatchHandle = integrateHandle;
                    }

                    s_HasPendingSimulationBatch = true;
                    scheduledAnyJob = true;
                }
            }

            if (scheduledAnyJob)
            {
                JobHandle.ScheduleBatchedJobs();
            }

            if (scheduledAnySimulation)
            {
                s_LastRendererUpdateFrame = -1;
                s_LastCompleteAndUploadFrame = -1;
            }

            s_LastPlayerLoopFrame = Time.frameCount;
            RequestEditorRenderUpdateForActiveSystems();
        }

        private static void EnsureSimulationPageWorkList()
        {
            if (!s_SimulationPageWorks.IsCreated)
                s_SimulationPageWorks = new NativeList<VividParticleEcsIntegratePageWork>(64, Allocator.Persistent);
            if (!s_SimulationCompactWorks.IsCreated)
                s_SimulationCompactWorks = new NativeList<VividParticleEcsCompactWork>(32, Allocator.Persistent);
            if (!s_EmissionInitializeWorks.IsCreated)
                s_EmissionInitializeWorks = new NativeList<VividParticleEcsInitializeParticlesWork>(32, Allocator.Persistent);
        }

        private static void EnsurePreparedSimulationTimeStepList()
        {
            if (!s_PreparedSimulationTimeSteps.IsCreated)
                s_PreparedSimulationTimeSteps = new NativeList<VividParticleSimulationTimeStep>(64, Allocator.Persistent);
        }

        private static void CompletePendingSimulations()
        {
            if (s_ActiveSimulationStates.Count == 0 && !s_HasPendingSimulationBatch)
                return;

            using (s_KickCompleteSimulationMarker.Auto())
            {
                JobHandle combinedHandle = default;
                bool hasJob = false;
                if (s_HasPendingSimulationBatch)
                {
                    combinedHandle = s_PendingSimulationBatchHandle;
                    hasJob = true;
                }

                for (int index = 0; index < s_ActiveSimulationStates.Count; index++)
                {
                    ParticleSystemState state = s_ActiveSimulationStates[index];
                    if (state == null || !state.hasPendingSimulationJob)
                        continue;

                    combinedHandle = hasJob
                        ? JobHandle.CombineDependencies(combinedHandle, state.pendingSimulationJob)
                        : state.pendingSimulationJob;
                    hasJob = true;
                }

                if (hasJob)
                    combinedHandle.Complete();

                s_PendingSimulationBatchHandle = default;
                s_HasPendingSimulationBatch = false;
                if (s_SimulationPageWorks.IsCreated)
                    s_SimulationPageWorks.Clear();
                if (s_SimulationCompactWorks.IsCreated)
                    s_SimulationCompactWorks.Clear();
                EnsureSimulationPageWorkList();
                s_EmissionInitializeWorks.Clear();

                s_ApplyingPendingSimulations = true;
                try
                {
                    for (int index = s_ActiveSimulationStates.Count - 1; index >= 0; index--)
                    {
                        ParticleSystemState state = s_ActiveSimulationStates[index];
                        if (state == null)
                            continue;

                        state.ApplyPendingSimulationResult(s_EmissionInitializeWorks);
                        RefreshActiveSimulationState(state);
                        RefreshActiveRendererState(state);
                    }
                }
                finally
                {
                    s_ApplyingPendingSimulations = false;
                }

                if (s_EmissionInitializeWorks.Length > 0)
                {
                    using (s_KickInitializeEmittedParticlesMarker.Auto())
                    {
                        var initializeJob = new VividParticleEcsInitializeParticlesJob
                        {
                            Works = s_EmissionInitializeWorks.AsArray(),
                        };
                        initializeJob.Schedule(
                            s_EmissionInitializeWorks.Length,
                            innerloopBatchCount: 1).Complete();
                        s_EmissionInitializeWorks.Clear();
                    }
                }
            }
        }

        private static void RefreshActiveSimulationState(ParticleSystemState state)
        {
            if (state == null)
                return;

            if (state.shouldBeInActiveSimulationList)
            {
                AddActiveSimulationState(state);
                return;
            }

            RemoveActiveSimulationState(state);
        }

        private static void AddActiveSimulationState(ParticleSystemState state)
        {
            if (state == null || s_ActiveSimulationIndices.ContainsKey(state))
                return;

            s_ActiveSimulationIndices.Add(state, s_ActiveSimulationStates.Count);
            s_ActiveSimulationStates.Add(state);
        }

        private static void RemoveActiveSimulationState(ParticleSystemState state)
        {
            if (state == null || !s_ActiveSimulationIndices.TryGetValue(state, out int index))
                return;

            int lastIndex = s_ActiveSimulationStates.Count - 1;
            ParticleSystemState lastState = s_ActiveSimulationStates[lastIndex];
            s_ActiveSimulationStates[index] = lastState;
            s_ActiveSimulationStates.RemoveAt(lastIndex);
            s_ActiveSimulationIndices.Remove(state);
            if (index != lastIndex && lastState != null)
                s_ActiveSimulationIndices[lastState] = index;
        }

        private static void PruneActiveSimulationStates()
        {
            for (int index = s_ActiveSimulationStates.Count - 1; index >= 0; index--)
                RefreshActiveSimulationState(s_ActiveSimulationStates[index]);
        }

        private static void RefreshActiveRendererState(ParticleSystemState state)
        {
            if (state == null)
                return;

            if (state.shouldBeInActiveRendererList)
            {
                AddActiveRendererState(state);
                return;
            }

            RemoveActiveRendererState(state);
        }

        private static void AddActiveRendererState(ParticleSystemState state)
        {
            if (state == null)
                return;

            s_RendererManager.CancelQueuedRemove(state);
            if (s_ActiveRendererIndices.ContainsKey(state))
                return;

            s_ActiveRendererIndices.Add(state, s_ActiveRendererStates.Count);
            s_ActiveRendererStates.Add(state);
        }

        private static void RemoveActiveRendererState(ParticleSystemState state)
        {
            if (state == null || !s_ActiveRendererIndices.TryGetValue(state, out int index))
                return;

            int lastIndex = s_ActiveRendererStates.Count - 1;
            ParticleSystemState lastState = s_ActiveRendererStates[lastIndex];
            s_ActiveRendererStates[index] = lastState;
            s_ActiveRendererStates.RemoveAt(lastIndex);
            s_ActiveRendererIndices.Remove(state);
            if (index != lastIndex && lastState != null)
                s_ActiveRendererIndices[lastState] = index;

            s_RendererManager.QueueRemove(state);
        }

        private static void PruneActiveRendererStates()
        {
            for (int index = s_ActiveRendererStates.Count - 1; index >= 0; index--)
                RefreshActiveRendererState(s_ActiveRendererStates[index]);
        }

        private static void CompleteAndUploadAll(bool forceUpload, bool oncePerFrame)
        {
            ScheduleRendererUpdate(forceUpload, oncePerFrame);
            CompletePendingUploadForRendering(oncePerFrame);
        }

        private static void ScheduleRendererUpdate(bool forceUpload, bool oncePerFrame)
        {
            if (oncePerFrame && s_LastRendererUpdateFrame == Time.frameCount)
                return;

            s_RendererManager.CompletePendingUpload();
            CompletePendingSimulations();
            PruneActiveRendererStates();
            s_RendererManager.SchedulePostSimulationBoundsUpdates(s_ActiveRendererStates);

            using (s_BRGUploadUpdateAllMarker.Auto())
            {
                s_RendererManager.UpdateAll(s_ActiveRendererStates, forceUpload);
            }

            s_RendererManager.Commit();
            s_LastRendererUpdateFrame = Time.frameCount;
            s_LastCompleteAndUploadFrame = -1;
            RequestEditorRenderUpdateForActiveSystems();
        }

        private static void CompletePendingUploadForRendering(bool oncePerFrame)
        {
            if (oncePerFrame && s_LastCompleteAndUploadFrame == Time.frameCount)
            {
                s_RendererManager.DrainCullingResults();
                return;
            }

            s_RendererManager.CompletePendingUpload();

            s_RendererManager.DrainCullingResults();
            s_LastCompleteAndUploadFrame = Time.frameCount;
        }

        private static void UploadRenderingState(ParticleSystemState state, bool forceUpload)
        {
            if (state == null)
                return;

            s_RendererManager.CompletePendingUpload();
            using (s_BRGUploadUpdateOneMarker.Auto())
            {
                s_RendererManager.Update(state, forceUpload);
            }
            s_LastCompleteAndUploadFrame = -1;
        }

        private static void InsertIntoPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (rootLoop.subSystemList == null)
                return;

            bool changed = RemovePlayerLoopSystem(
                ref rootLoop,
                typeof(VividParticleSystemManagerPlayerLoopMarker));

            changed |= RemovePlayerLoopSystem(
                ref rootLoop,
                typeof(VividParticleSystemManagerRendererUpdateMarker));

            Type lateUpdateAnchorType = ResolvePlayerLoopNestedType(
                typeof(PreLateUpdate),
                "ScriptRunBehaviourLateUpdate");
            Type rendererAnchorType = ResolvePlayerLoopNestedType(
                typeof(PreLateUpdate),
                "UpdateAllRenderers");

            changed |= InsertPlayerLoopSystem(
                ref rootLoop,
                typeof(PreLateUpdate),
                typeof(VividParticleSystemManagerPlayerLoopMarker),
                PlayerLoopKick,
                insertAfterType: lateUpdateAnchorType,
                insertBeforeType: null);

            changed |= InsertPlayerLoopSystem(
                ref rootLoop,
                typeof(PreLateUpdate),
                typeof(VividParticleSystemManagerRendererUpdateMarker),
                RendererUpdateKick,
                insertAfterType: null,
                insertBeforeType: rendererAnchorType);

            if (changed)
                PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static bool InsertPlayerLoopSystem(
            ref PlayerLoopSystem rootLoop,
            Type parentType,
            Type markerType,
            PlayerLoopSystem.UpdateFunction updateFunction,
            Type insertAfterType,
            Type insertBeforeType)
        {
            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != parentType)
                    continue;

                PlayerLoopSystem[] nestedSystems = subSystem.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var updatedSubSystems = new List<PlayerLoopSystem>(nestedSystems.Length + 1);
                bool alreadyPresent = false;
                bool inserted = false;
                foreach (PlayerLoopSystem nestedSystem in nestedSystems)
                {
                    if (nestedSystem.type == markerType)
                        alreadyPresent = true;

                    if (!alreadyPresent
                        && !inserted
                        && insertBeforeType != null
                        && nestedSystem.type == insertBeforeType)
                    {
                        updatedSubSystems.Add(CreatePlayerLoopSystem(markerType, updateFunction));
                        inserted = true;
                    }

                    updatedSubSystems.Add(nestedSystem);

                    if (!alreadyPresent
                        && !inserted
                        && insertAfterType != null
                        && nestedSystem.type == insertAfterType)
                    {
                        updatedSubSystems.Add(CreatePlayerLoopSystem(markerType, updateFunction));
                        inserted = true;
                    }
                }

                if (!alreadyPresent && !inserted)
                    updatedSubSystems.Add(CreatePlayerLoopSystem(markerType, updateFunction));

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                return !alreadyPresent;
            }

            return false;
        }

        private static bool RemovePlayerLoopSystem(
            ref PlayerLoopSystem rootLoop,
            Type markerType)
        {
            bool changed = false;
            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                PlayerLoopSystem[] nestedSystems = subSystem.subSystemList;
                if (nestedSystems == null || nestedSystems.Length == 0)
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(nestedSystems.Length);
                for (int nestedIndex = 0; nestedIndex < nestedSystems.Length; nestedIndex++)
                {
                    if (nestedSystems[nestedIndex].type == markerType)
                    {
                        changed = true;
                        continue;
                    }

                    updatedSubSystems.Add(nestedSystems[nestedIndex]);
                }

                if (updatedSubSystems.Count == nestedSystems.Length)
                    continue;

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
            }

            return changed;
        }

        private static Type ResolvePlayerLoopNestedType(Type parentType, string nestedTypeName)
        {
            Type nestedType = parentType.GetNestedType(nestedTypeName);
            if (nestedType != null)
                return nestedType;

            return Type.GetType(parentType.FullName + "+" + nestedTypeName + ", UnityEngine.CoreModule");
        }

        private static PlayerLoopSystem CreatePlayerLoopSystem(
            Type markerType,
            PlayerLoopSystem.UpdateFunction updateFunction)
        {
            return new PlayerLoopSystem
            {
                type = markerType,
                updateDelegate = updateFunction,
            };
        }

        private static void RequestEditorRenderUpdateIfNeeded(VividParticleSystem system)
        {
#if UNITY_EDITOR
            if (Application.isPlaying
                || system == null
                || !s_States.TryGetValue(system, out ParticleSystemState state)
                || !state.shouldBeInActiveSimulationList)
            {
                return;
            }

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        private static void RequestEditorRenderUpdateForActiveSystems()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            for (int index = 0; index < s_ActiveSimulationStates.Count; index++)
            {
                ParticleSystemState state = s_ActiveSimulationStates[index];
                if (state == null || !state.shouldBeInActiveSimulationList)
                    continue;

                EditorApplication.QueuePlayerLoopUpdate();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                break;
            }
#endif
        }

        private static bool IntersectsCullingPlanes(
            Bounds bounds,
            NativeArray<Plane> planes,
            int start,
            int count)
        {
            if (!planes.IsCreated || count <= 0)
                return true;

            int end = Mathf.Min(planes.Length, start + count);
            for (int i = Mathf.Max(0, start); i < end; i++)
            {
                if (IsOutsidePlane(bounds, planes[i]))
                    return false;
            }

            return true;
        }

        private static bool IsOutsidePlane(Bounds bounds, Plane plane)
        {
            Vector3 normal = plane.normal;
            Vector3 positiveVertex = bounds.center + new Vector3(
                normal.x >= 0.0f ? bounds.extents.x : -bounds.extents.x,
                normal.y >= 0.0f ? bounds.extents.y : -bounds.extents.y,
                normal.z >= 0.0f ? bounds.extents.z : -bounds.extents.z);

            return plane.GetDistanceToPoint(positiveVertex) < 0.0f;
        }

        private static float4 ToFloat4(Color value)
        {
            return new float4(value.r, value.g, value.b, value.a);
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }

        [Flags]
        private enum ParticleSimulationJobFlags : uint
        {
            None = 0u,
            Integrate = 1u << 0,
        }

        private readonly struct ParticleSimulationJobContext : IVividEcsManagerJobModuleFlags
        {
            public ParticleSimulationJobContext(
                ParticleSystemState state,
                VividParticleSystemFrameSnapshot snapshot)
            {
                State = state;
                Snapshot = snapshot;
                EnabledModuleFlags = state != null && state.CanScheduleIntegrateJob(snapshot)
                    ? (uint)ParticleSimulationJobFlags.Integrate
                    : 0u;
            }

            public readonly ParticleSystemState State;
            public readonly VividParticleSystemFrameSnapshot Snapshot;

            public uint EnabledModuleFlags { get; }
        }

        private sealed class ParticleSystemState : IDisposable
        {
            private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
            private static readonly int s_UnlitColorId = Shader.PropertyToID("_UnlitColor");
            private static readonly int s_SurfaceTypeId = Shader.PropertyToID("_SurfaceType");
            private static readonly int s_BlendModeId = Shader.PropertyToID("_BlendMode");
            private static readonly int s_CullModeId = Shader.PropertyToID("_CullMode");
            private static readonly int s_TransparentZWriteId = Shader.PropertyToID("_TransparentZWrite");
            private static readonly int s_QueueOffsetId = Shader.PropertyToID("_QueueOffset");
            private static readonly int s_SrcBlendId = Shader.PropertyToID("_SrcBlend");
            private static readonly int s_DstBlendId = Shader.PropertyToID("_DstBlend");
            private static readonly int s_AlphaSrcBlendId = Shader.PropertyToID("_AlphaSrcBlend");
            private static readonly int s_AlphaDstBlendId = Shader.PropertyToID("_AlphaDstBlend");
            private static readonly int s_ZWriteId = Shader.PropertyToID("_ZWrite");
            private static readonly int s_ParticleRenderModeId = Shader.PropertyToID("_VividParticleRenderMode");

            private readonly VividParticleSystem m_System;
            private readonly VividParticleEcsStorage m_Storage;
            private readonly GraphicsBuffer[] m_InstanceDataBuffers = new GraphicsBuffer[InstanceDataBufferCount];
            private readonly InstanceUploadDirtyRanges[] m_InstanceDirtyRanges = CreateInstanceDirtyRanges();
            private BatchRendererGroup m_BRG;
            private GraphicsBuffer m_InstanceData;
            private Mesh m_QuadMesh;
            private Mesh[] m_RenderMeshBuffer = Array.Empty<Mesh>();
            private int m_RenderMeshCount;
            private Material m_OwnedMaterial;
            private Material m_RegisteredMaterial;
            private Material m_SourceMaterial;
            private Mesh m_SourceMesh;
            private BatchID m_BatchID;
            private BatchMeshID m_MeshID;
            private BatchMaterialID m_MaterialID;
            private System.Random m_Random;
            private bool[] m_BurstTriggered = Array.Empty<bool>();
            private VividParticleBurst[] m_BurstSnapshotBuffer = Array.Empty<VividParticleBurst>();
            private JobHandle m_PendingJob;
            private VividParticleSystemFrameSnapshot m_PendingSnapshot;
            private VividParticleSystemFrameSnapshot m_CachedAutomaticSnapshot;
            private double m_LastEditorUpdateTime;
            private float m_Time;
            private float m_EmissionAccumulator;
            private int m_Capacity;
            private int m_LastUploadedCount;
            private int m_LastCompletedUploadActiveCount;
            private int m_LastUploadedFrame = -1;
            private int m_LastUploadOperationCount;
            private int m_LastUploadByteCount;
            private int m_LastUploadBufferIndex = -1;
            private int m_InstanceDataBufferIndex = -1;
            private int m_RenderQueueOffset;
            private VividParticleRenderMode m_RenderMode = VividParticleRenderMode.Billboard;
            private int m_PendingFrame = -1;
            private Matrix4x4 m_LastUploadedLocalToWorldMatrix;
            private Color m_LastUploadedRendererColor;
            private float m_LastUploadedSizeScale;
            private float m_LastUploadedStretchLengthScale;
            private float m_LastUploadedStretchSpeedScale;
            private Vector3 m_LastUploadedPivot;
            private float m_LastUploadedMinParticleSize;
            private float m_LastUploadedMaxParticleSize;
            private Vector3 m_LastUploadedFlip;
            private int m_LastUploadedRenderStateActiveCount;
            private VividParticleRenderMode m_LastUploadedRenderMode;
            private VividParticleGpuDataLayoutDescriptor m_CachedGpuLayoutDescriptor;
            private VividParticleGpuDataLayout m_CachedGpuLayout;
            private VividParticleRendererSharedKey m_LastRendererSharedKey;
            private Bounds m_CachedWorldBounds;
            private Bounds[] m_CachedPageWorldBounds = Array.Empty<Bounds>();
            private int m_CachedPageWorldBoundsCount;
            private int m_CachedBoundsParticleCount = -1;
            private bool m_BatchCreated;
            private bool m_OwnedMesh;
            private bool m_ResourcesDirty = true;
            private bool m_MissingShaderWarningLogged;
            private bool m_HasPendingJob;
            private bool m_HasPendingStandaloneJob;
            private bool m_HasPendingSimulation;
            private bool m_PendingAllowEmission;
            private bool m_BoundsDirty = true;
            private bool m_IsEditorSelected;
            private bool m_HasUploadedRenderStateSnapshot;
            private bool m_HasCachedAutomaticSnapshot;
            private bool m_AutomaticSnapshotDirty = true;
            private bool m_HasCachedGpuLayout;
            private bool m_HasRendererSharedKey;
            private bool m_HasCachedWorldBounds;
            private bool m_SharedDataDirty;
            private uint m_SharedDataDirtyBits;
            private bool m_RendererInitialized;

            public ParticleSystemState(VividParticleSystem system)
            {
                m_System = system;
                m_Storage = new VividParticleEcsStorage(s_ParticleEcsWorld);
                m_Storage.systemId = ResolveSystemId(system);
                RefreshEditorSelectionState();
                ResetEditorUpdateTime();
            }

            public int activeCount => Mathf.Min(m_Storage.activeCount, m_System != null ? m_System.main.maxParticles : 0);

            public int storageCapacity => m_Storage.capacity;

            public int storagePageCount => m_Storage.pageCount;

            public bool usesEcsStorage => true;

            public float time => m_Time;

            public bool requiresAutomaticUpdate => m_System != null && m_System.requiresAutomaticUpdate;

            internal bool shouldBeInActiveSimulationList => m_System != null
                && m_System.isActiveAndEnabled
                && !m_System.isPaused
                && (m_System.isPlaying || (m_System.stopEmitting && activeCount > 0));

            internal bool shouldBeInActiveRendererList => m_System != null
                && m_System.isActiveAndEnabled
                && CanRender(m_System.rendererModule)
                && activeCount > 0;

            internal bool hasPendingSimulation => m_HasPendingSimulation || m_HasPendingJob;

            internal bool hasPendingSimulationJob => m_HasPendingStandaloneJob;

            internal JobHandle pendingSimulationJob => m_PendingJob;

            public VividParticleSystemManagerStats stats => new(
                IsInitialized,
                m_Capacity,
                m_LastUploadedCount,
                CullingCallCount,
                VisibleCullingCallCount,
                LastVisible,
                LastViewType,
                LastDrawCommandCount,
                LastVisibleInstanceCount,
                m_HasPendingJob ? 1 : 0,
                ScheduledJobCount,
                CompletedJobCount,
                LastScheduledFrame,
                LastCompletedFrame,
                InstanceDataBufferCount,
                m_LastUploadBufferIndex,
                m_LastUploadOperationCount,
                m_LastUploadByteCount);

            public VividParticleSystemRuntimeStats runtimeStats => new(
                activeCount,
                m_Time,
                VividEcsConstants.PageEntryCount,
                storageCapacity,
                storagePageCount,
                usesEcsStorage,
                hasPendingSimulation,
                m_HasPendingJob ? 1 : 0);

            private bool IsInitialized => m_RendererInitialized && m_Capacity > 0;

            private int CullingCallCount { get; set; }

            private int VisibleCullingCallCount { get; set; }

            private bool LastVisible { get; set; }

            private BatchCullingViewType LastViewType { get; set; }

            private int LastDrawCommandCount { get; set; }

            private int LastVisibleInstanceCount { get; set; }

            private int ScheduledJobCount { get; set; }

            private int CompletedJobCount { get; set; }

            private int LastScheduledFrame { get; set; } = -1;

            private int LastCompletedFrame { get; set; } = -1;

            public void Dispose()
            {
                CompletePending();
                CompletePendingBoundsJob();
                s_RendererManager.Unregister(this);
                ReleaseResources();
                m_Storage.Dispose();
                m_Random = null;
                m_BurstTriggered = Array.Empty<bool>();
                m_BurstSnapshotBuffer = Array.Empty<VividParticleBurst>();
                m_CachedPageWorldBounds = Array.Empty<Bounds>();
                ResetCachedRenderBounds();
            }

            public void MarkResourcesDirty()
            {
                m_ResourcesDirty = true;
                InvalidateAutomaticSnapshot();
                MarkBoundsDirty();
                MarkAllInstanceDataDirty();
            }

            public void MarkRendererModuleDirty()
            {
                m_LastUploadedFrame = -1;
                MarkBoundsDirty();
            }

            public void NotifySettingsChanged()
            {
                if (m_System == null)
                    return;

                CompletePending();
                InvalidateAutomaticSnapshot();
                VividParticleSystemFrameSnapshot snapshot = CaptureFrameSnapshot(0.0f);
                int oldCapacity = storageCapacity;
                EnsureStorageCapacity(snapshot.MaxParticles);
                EnsureBurstState(snapshot.Bursts);
                if (oldCapacity != storageCapacity)
                    MarkResourcesDirty();
                else
                {
                    MarkSharedDataDirty(GetCurrentGpuDataLayout().PerSharpValueBits);
                    MarkBoundsDirty();
                }
            }

            public void ResetEditorUpdateTime()
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    m_LastEditorUpdateTime = EditorApplication.timeSinceStartup;
#endif
            }

            internal bool RefreshEditorSelectionState()
            {
#if UNITY_EDITOR
                bool isEditorSelected = IsSelectedForEditorOutline(m_System);
#else
                bool isEditorSelected = false;
#endif
                if (m_IsEditorSelected == isEditorSelected)
                    return false;

                m_IsEditorSelected = isEditorSelected;
                return true;
            }

            internal bool isEditorSelected => m_IsEditorSelected;

            public bool ScheduleAutomatic(float? deltaTimeOverride, bool requireActive)
            {
                return TryPrepareAutomaticSnapshot(
                        deltaTimeOverride,
                        requireActive,
                        out VividParticleSystemFrameSnapshot snapshot)
                    && ScheduleAutomatic(snapshot, requireActive);
            }

            public bool TryPrepareAutomaticSnapshot(
                float? deltaTimeOverride,
                bool requireActive,
                out VividParticleSystemFrameSnapshot snapshot)
            {
                return TryPrepareAutomaticSnapshot(deltaTimeOverride, requireActive, out snapshot, out _);
            }

            public bool TryPrepareAutomaticSnapshot(
                float? deltaTimeOverride,
                bool requireActive,
                out VividParticleSystemFrameSnapshot snapshot,
                out VividParticleSimulationTimeStep timeStep)
            {
                if (m_System == null)
                {
                    snapshot = default;
                    timeStep = default;
                    return false;
                }

                if (!TryPrepareAutomaticTimeStep(deltaTimeOverride, requireActive, out timeStep))
                {
                    snapshot = default;
                    return false;
                }

                snapshot = CaptureAutomaticFrameSnapshot(timeStep);
                return true;
            }

            public bool ScheduleAutomatic(VividParticleSystemFrameSnapshot snapshot)
            {
                return ScheduleAutomatic(snapshot, requireActive: true);
            }

            public bool ScheduleAutomatic(VividParticleSystemFrameSnapshot snapshot, bool requireActive)
            {
                if (!RequiresAutomaticUpdate(snapshot, requireActive))
                    return false;

                return ScheduleSimulation(snapshot, snapshot.IsPlaying && !snapshot.StopEmitting);
            }

            public bool ScheduleAutomaticBatch(
                VividParticleSystemFrameSnapshot snapshot,
                VividParticleSimulationTimeStep timeStep,
                NativeList<VividParticleEcsIntegratePageWork> pageWorks,
                NativeList<VividParticleEcsCompactWork> compactWorks)
            {
                if (!RequiresAutomaticUpdate(timeStep, requireActive: true))
                    return false;

                return ScheduleSimulationBatch(
                    snapshot,
                    timeStep.allowEmission,
                    pageWorks,
                    compactWorks);
            }

            public VividParticleSystemFrameSnapshot CaptureFrameSnapshot(float deltaTime)
            {
                return m_System != null
                    ? m_System.CaptureFrameSnapshot(deltaTime, ref m_BurstSnapshotBuffer)
                    : default;
            }

            private bool TryPrepareAutomaticTimeStep(
                float? deltaTimeOverride,
                bool requireActive,
                out VividParticleSimulationTimeStep timeStep)
            {
                timeStep = default;
                if (m_System == null)
                    return false;

                float deltaTime = ResolveDeltaTime(deltaTimeOverride);
                if (deltaTime <= 0.0f)
                    return false;

                timeStep = new VividParticleSimulationTimeStep(
                    deltaTime,
                    m_System.isActiveAndEnabled,
                    m_System.isPlaying,
                    m_System.isPaused,
                    m_System.stopEmitting,
                    m_System.gameObject.layer,
                    m_System.transform.position,
                    m_System.transform.localToWorldMatrix,
                    m_System.transform.rotation);
                return RequiresAutomaticUpdate(timeStep, requireActive);
            }

            private bool RequiresAutomaticUpdate(VividParticleSimulationTimeStep timeStep, bool requireActive)
            {
                return timeStep.RequiresAutomaticUpdate(activeCount, requireActive);
            }

            private VividParticleSystemFrameSnapshot CaptureAutomaticFrameSnapshot(VividParticleSimulationTimeStep timeStep)
            {
                if (m_System == null)
                    return default;

                if (NeedsAutomaticSnapshotRebuild())
                {
                    m_CachedAutomaticSnapshot = CaptureFrameSnapshot(0.0f);
                    m_HasCachedAutomaticSnapshot = true;
                    m_AutomaticSnapshotDirty = false;
                }

                return m_CachedAutomaticSnapshot.WithFrameState(timeStep);
            }

            private bool NeedsAutomaticSnapshotRebuild()
            {
                return m_System == null || m_AutomaticSnapshotDirty || !m_HasCachedAutomaticSnapshot;
            }

            private void InvalidateAutomaticSnapshot()
            {
                m_AutomaticSnapshotDirty = true;
            }

            public void CompletePending()
            {
                if (!s_ApplyingPendingSimulations)
                    VividParticleSystemManager.CompletePendingSimulations();

                if (!m_HasPendingSimulation && !m_HasPendingJob)
                    return;

                if (m_HasPendingStandaloneJob)
                {
                    m_PendingJob.Complete();
                }

                ApplyPendingSimulationResult();
            }

            public void ApplyPendingSimulationResult()
            {
                ApplyPendingSimulationResult(default);
            }

            public void ApplyPendingSimulationResult(NativeList<VividParticleEcsInitializeParticlesWork> initializeWorks)
            {
                if (!m_HasPendingSimulation && !m_HasPendingJob)
                    return;

                if (m_HasPendingJob)
                {
                    int previousActiveCount = activeCount;
                    m_PendingJob = default;
                    m_HasPendingStandaloneJob = false;
                    m_HasPendingJob = false;
                    m_Storage.ApplyScheduledIntegrateResult();
                    MarkInstanceRangeDirty(0, previousActiveCount);
                    MarkBoundsDirty();
                    CompletedJobCount++;
                }

                if (m_HasPendingSimulation)
                {
                    AdvanceEmission(m_PendingSnapshot, m_PendingAllowEmission, initializeWorks);
                    m_HasPendingSimulation = false;
                    m_PendingAllowEmission = false;
                    m_PendingFrame = -1;
                    if (m_System != null && m_System.CompleteStopEmittingIfEmpty(activeCount))
                        VividParticleSystemManager.RefreshActiveSimulationState(this);
                }

                LastCompletedFrame = Time.frameCount;
                VividParticleSystemManager.RefreshActiveRendererState(this);
            }

            public void SimulateDeltaImmediate(VividParticleSystemFrameSnapshot snapshot, bool allowEmission)
            {
                if (snapshot.DeltaTime <= 0.0f)
                    return;

                EnsureStorageCapacity(snapshot.MaxParticles);
                int previousActiveCount = activeCount;
                if (ScheduleIntegrateViaRegistry(snapshot))
                {
                    m_PendingJob.Complete();
                    m_PendingJob = default;
                    m_HasPendingJob = false;
                    m_HasPendingStandaloneJob = false;
                    CompletedJobCount++;
                    LastCompletedFrame = Time.frameCount;
                    m_Storage.ApplyScheduledIntegrateResult();
                    MarkInstanceRangeDirty(0, previousActiveCount);
                }

                AdvanceEmission(snapshot, allowEmission);
                if (m_System != null && m_System.CompleteStopEmittingIfEmpty(activeCount))
                    VividParticleSystemManager.RefreshActiveSimulationState(this);

                VividParticleSystemManager.RefreshActiveRendererState(this);
            }

            public void Emit(int count, VividParticleSystemFrameSnapshot snapshot)
            {
                Emit(count, snapshot, default);
            }

            private void Emit(
                int count,
                VividParticleSystemFrameSnapshot snapshot,
                NativeList<VividParticleEcsInitializeParticlesWork> initializeWorks)
            {
                if (count <= 0)
                    return;

                EnsureStorageCapacity(snapshot.MaxParticles);
                EnsureRandom(snapshot);

                int available = Mathf.Max(0, snapshot.MaxParticles - activeCount);
                int spawnCount = Mathf.Min(count, available);
                int firstSpawnIndex = activeCount;
                if (spawnCount <= 0)
                    return;

                if (initializeWorks.IsCreated
                    && m_Storage.ReserveInitializeParticles(
                        spawnCount,
                        snapshot,
                        NextRandomSeed(snapshot),
                        initializeWorks,
                        out firstSpawnIndex,
                        out int reservedCount))
                {
                    MarkInstanceRangeDirty(firstSpawnIndex, reservedCount);
                    MarkBoundsDirty();
                    return;
                }

                for (int index = 0; index < spawnCount; index++)
                    SpawnParticle(snapshot);

                MarkInstanceRangeDirty(firstSpawnIndex, activeCount - firstSpawnIndex);
            }

            public void ResetSimulation(VividParticleSystemFrameSnapshot snapshot, bool clearParticles)
            {
                m_Time = 0.0f;
                m_EmissionAccumulator = 0.0f;
                ResetBurstState(snapshot.Bursts);
                if (clearParticles)
                {
                    CompletePendingBoundsJob();
                    m_Storage.Clear();
                    MarkInstanceRangeDirty(0, m_LastUploadedCount);
                    MarkBoundsDirty();
                }
                ResetRandom(snapshot);
                VividParticleSystemManager.RefreshActiveRendererState(this);
            }

            public void UpdateRendering(bool forceUpload)
            {
                s_RendererManager.Update(this, forceUpload);
            }

            internal bool PrepareRenderEntry(bool forceUpload, out ParticleRenderEntry entry)
            {
                entry = default;
                if (m_System == null)
                    return false;

                if (!ParticleSystemState.CanRender(m_System.rendererModule))
                {
                    SetRendererInactive();
                    return false;
                }

                if (!forceUpload && !m_ResourcesDirty && m_LastUploadedFrame == Time.frameCount)
                    return CreateRenderEntry(out entry);

                if (!EnsureResources())
                {
                    SetRendererInactive();
                    return false;
                }

                if (!CreateRenderEntry(out entry))
                {
                    SetRendererInactive();
                    return false;
                }

                m_LastUploadedFrame = Time.frameCount;
                return true;
            }

            private bool CreateRenderEntry(out ParticleRenderEntry entry)
            {
                entry = default;
                if (m_System == null || m_RegisteredMaterial == null || m_QuadMesh == null || m_Capacity <= 0)
                    return false;

                int count = Mathf.Min(activeCount, m_Capacity);
                m_LastUploadedCount = count;
                m_LastUploadOperationCount = 0;
                m_LastUploadByteCount = 0;
                VividParticleGpuDataLayoutDescriptor gpuLayoutDescriptor =
                    VividParticleGpuDataLayoutDescriptor.Create(m_System.rendererModule);
                VividParticleGpuDataLayout gpuLayout = GetGpuDataLayout(gpuLayoutDescriptor);
                EnsureRenderMeshBuffer();
                MarkRenderStateDirtyIfNeeded(count);
                int meshSetHash = m_RenderMode == VividParticleRenderMode.Mesh
                    ? m_System.rendererModule.meshSetHash
                    : m_QuadMesh.GetEntityId().GetHashCode();
                var rendererSharedKey = new VividParticleRendererSharedKey(
                    m_RegisteredMaterial.GetEntityId().GetHashCode(),
                    meshSetHash,
                    (int)m_RenderMode,
                    Mathf.Clamp(m_System.gameObject.layer, 0, 31),
                    gpuLayout.Hash,
                    gpuLayout.DataPerSharpBits,
                    (int)m_System.rendererModule.shadowCastingMode,
                    (int)m_System.rendererModule.sortMode,
                    m_System.rendererModule.renderingLayerMask,
                    m_System.rendererModule.receiveShadows);
                UpdateRendererSharedKey(rendererSharedKey);
                entry = new ParticleRenderEntry(
                    this,
                    m_Storage.archetypeLineId,
                    m_RegisteredMaterial,
                    m_QuadMesh,
                    m_RenderMeshBuffer,
                    m_RenderMeshCount,
                    m_RenderMode,
                    gpuLayout,
                    rendererSharedKey,
                    m_System.gameObject.layer,
                    m_Capacity,
                    count,
                    m_System.transform.localToWorldMatrix,
                    m_System.rendererModule.color,
                    m_System.rendererModule.sizeScale,
                    m_System.rendererModule.stretchLengthScale,
                    m_System.rendererModule.stretchSpeedScale,
                    m_System.rendererModule.pivot,
                    m_System.rendererModule.minParticleSize,
                    m_System.rendererModule.maxParticleSize,
                    m_System.rendererModule.flip,
                    m_System.rendererModule.shadowCastingMode,
                    m_System.rendererModule.renderingLayerMask,
                    m_System.rendererModule.receiveShadows,
                    m_System.gameObject.GetEntityId(),
                    m_IsEditorSelected,
                    m_System.rendererModule.sortMode);
                return true;
            }

            private void UpdateRendererSharedKey(VividParticleRendererSharedKey rendererSharedKey)
            {
                if (m_HasRendererSharedKey && m_LastRendererSharedKey.Equals(rendererSharedKey))
                    return;

                m_Storage.rendererSharedKey = rendererSharedKey;
                m_LastRendererSharedKey = rendererSharedKey;
                m_HasRendererSharedKey = true;
            }

            private VividParticleGpuDataLayout GetGpuDataLayout(VividParticleGpuDataLayoutDescriptor descriptor)
            {
                if (!m_HasCachedGpuLayout || !m_CachedGpuLayoutDescriptor.Equals(descriptor))
                {
                    m_CachedGpuLayoutDescriptor = descriptor;
                    m_CachedGpuLayout = VividParticleGpuDataLayout.Create(descriptor);
                    m_HasCachedGpuLayout = true;
                }

                return m_CachedGpuLayout;
            }

            private VividParticleGpuDataLayout GetCurrentGpuDataLayout()
            {
                return m_System != null
                    ? GetGpuDataLayout(VividParticleGpuDataLayoutDescriptor.Create(m_System.rendererModule))
                    : VividParticleGpuDataLayout.Create(VividParticleRenderMode.Billboard);
            }

            private void EnsureRenderMeshBuffer()
            {
                if (m_System == null || m_QuadMesh == null)
                {
                    m_RenderMeshBuffer = Array.Empty<Mesh>();
                    m_RenderMeshCount = 0;
                    return;
                }

                if (m_RenderMode != VividParticleRenderMode.Mesh)
                {
                    if (m_RenderMeshBuffer.Length != 1)
                        m_RenderMeshBuffer = new Mesh[1];

                    m_RenderMeshBuffer[0] = m_QuadMesh;
                    m_RenderMeshCount = 1;
                    return;
                }

                int meshCount = Mathf.Max(1, m_System.rendererModule.meshCount);
                if (m_RenderMeshBuffer.Length != meshCount)
                    m_RenderMeshBuffer = new Mesh[meshCount];

                m_RenderMeshCount = m_System.rendererModule.GetMeshes(m_RenderMeshBuffer);
                if (m_RenderMeshCount <= 0)
                {
                    m_RenderMeshBuffer[0] = m_QuadMesh;
                    m_RenderMeshCount = 1;
                }
            }

            private void SetRendererInactive()
            {
                m_RendererInitialized = false;
                m_LastUploadedCount = 0;
                LastVisible = false;
                LastDrawCommandCount = 0;
                LastVisibleInstanceCount = 0;
                ResetCachedRenderBounds();
            }

            public Matrix4x4 GetParticleObjectToWorldMatrix(int particleIndex)
            {
                if (particleIndex < 0
                    || particleIndex >= activeCount
                    || !m_Storage.IsValidIndex(particleIndex)
                    || m_System == null)
                {
                    return Matrix4x4.identity;
                }

                Vector3 position = GetParticleWorldPosition(m_Storage.GetPosition(particleIndex));
                float size = GetParticleRenderSize(particleIndex);
                VividParticleRenderMode renderMode = m_System.rendererModule.renderMode;

                if (renderMode == VividParticleRenderMode.Stretch)
                    return GetStretchParticleMatrix(particleIndex, position, size);

                return Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * size);
            }

            public Color GetParticleRenderColor(int particleIndex)
            {
                if (particleIndex < 0
                    || particleIndex >= activeCount
                    || !m_Storage.IsValidIndex(particleIndex)
                    || m_System == null)
                {
                    return Color.clear;
                }

                float startLifetime = m_Storage.GetStartLifetime(particleIndex);
                float lifetimeRatio = startLifetime > 0.0f
                    ? Mathf.Clamp01(m_Storage.GetRemainingLifetime(particleIndex) / startLifetime)
                    : 0.0f;
                Color color = m_Storage.GetColor(particleIndex) * m_System.rendererModule.color;
                color.a *= lifetimeRatio;
                return color;
            }

            public Bounds GetWorldBounds()
            {
                EnsureCachedCullingBounds();
                return m_CachedWorldBounds;
            }

            private void CompletePendingBoundsJob()
            {
                s_RendererManager.CompletePendingBoundsUpdates();
            }

            internal bool NeedsBoundsUpdate(int count)
            {
                count = Mathf.Clamp(count, 0, activeCount);
                return m_BoundsDirty || !m_HasCachedWorldBounds || m_CachedBoundsParticleCount != count;
            }

            internal unsafe bool TryCreateBoundsSource(ParticleRenderRecord record, out ParticleBoundsSource source)
            {
                source = default;
                if (record == null || m_System == null || !m_Storage.isCreated)
                    return false;

                int count = Mathf.Clamp(record.ActiveCount, 0, activeCount);
                return TryCreateBoundsSource(
                    count,
                    record.LocalToWorldMatrix,
                    record.RenderMode,
                    record.Mesh,
                    record.SizeScale,
                    record.StretchLengthScale,
                    record.StretchSpeedScale,
                    out source);
            }

            internal unsafe bool TryCreateCurrentBoundsSource(
                out ParticleBoundsSource source,
                out VividParticleRenderMode renderMode,
                out int count)
            {
                source = default;
                renderMode = VividParticleRenderMode.None;
                count = activeCount;
                if (m_System == null
                    || !m_System.isActiveAndEnabled
                    || !ParticleSystemState.CanRender(m_System.rendererModule)
                    || !m_Storage.isCreated)
                {
                    count = 0;
                    return false;
                }

                renderMode = m_System.rendererModule.renderMode;
                count = Mathf.Clamp(count, 0, Mathf.Max(0, m_System.main.maxParticles));
                return TryCreateBoundsSource(
                    count,
                    m_System.transform.localToWorldMatrix,
                    renderMode,
                    m_System.rendererModule.renderMesh,
                    m_System.rendererModule.sizeScale,
                    m_System.rendererModule.stretchLengthScale,
                    m_System.rendererModule.stretchSpeedScale,
                    out source);
            }

            private unsafe bool TryCreateBoundsSource(
                int count,
                Matrix4x4 localToWorld,
                VividParticleRenderMode renderMode,
                Mesh mesh,
                float sizeScale,
                float stretchLengthScale,
                float stretchSpeedScale,
                out ParticleBoundsSource source)
            {
                source = default;
                if (m_System == null || !m_Storage.isCreated)
                    return false;

                count = Mathf.Clamp(count, 0, activeCount);
                if (count <= 0 || !m_Storage.IsValidIndex(0))
                    return false;

                if (!m_Storage.TryGetCommonArrays(
                    out NativeArray<float3> positions,
                    out NativeArray<float3> velocities,
                    out _,
                    out _,
                    out _,
                    out NativeArray<float> sizes))
                {
                    return false;
                }

                float meshExtent = 0.0f;
                if (renderMode == VividParticleRenderMode.Mesh && mesh != null)
                    meshExtent = mesh.bounds.extents.magnitude;

                source = new ParticleBoundsSource
                {
                    Positions = (float3*)positions.GetUnsafeReadOnlyPtr(),
                    Velocities = (float3*)velocities.GetUnsafeReadOnlyPtr(),
                    Sizes = (float*)sizes.GetUnsafeReadOnlyPtr(),
                    ActiveCount = count,
                    LocalToWorld = ToFloat4x4(localToWorld),
                    SimulationSpace = (int)m_System.main.simulationSpace,
                    RenderMode = (int)renderMode,
                    SizeScale = sizeScale,
                    StretchLengthScale = stretchLengthScale,
                    StretchSpeedScale = stretchSpeedScale,
                    MeshExtent = meshExtent,
                };
                return true;
            }

            internal void ApplyCachedBounds(
                ParticleBoundsData worldBounds,
                NativeArray<ParticleBoundsData> pageBounds,
                int pageStart,
                int pageCount,
                int count,
                bool usesPageBillboard)
            {
                m_CachedWorldBounds = ToBounds(worldBounds, m_System != null ? m_System.transform.position : Vector3.zero);
                if (pageCount > 0 && pageBounds.IsCreated)
                {
                    EnsureCachedPageBoundsCapacity(pageCount);
                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                        m_CachedPageWorldBounds[pageIndex] = ToBounds(pageBounds[pageStart + pageIndex], m_CachedWorldBounds.center);
                    m_CachedPageWorldBoundsCount = pageCount;
                }
                else
                {
                    m_CachedPageWorldBoundsCount = 0;
                }

                m_CachedBoundsParticleCount = Mathf.Clamp(count, 0, activeCount);
                m_HasCachedWorldBounds = true;
                m_BoundsDirty = false;
            }

            private void SetEmptyCachedRenderBounds(Vector3 center, int count)
            {
                m_CachedWorldBounds = new Bounds(center, Vector3.zero);
                m_CachedPageWorldBoundsCount = 0;
                m_CachedBoundsParticleCount = count;
                m_HasCachedWorldBounds = true;
                m_BoundsDirty = false;
            }

            internal void SetEmptyCachedRenderBounds(int count)
            {
                SetEmptyCachedRenderBounds(m_System != null ? m_System.transform.position : Vector3.zero, count);
            }

            private void MarkBoundsDirty()
            {
                m_BoundsDirty = true;
            }

            private static Bounds ToBounds(ParticleBoundsData bounds, Vector3 fallbackCenter)
            {
                if (bounds.IsValid == 0)
                    return new Bounds(fallbackCenter, Vector3.zero);

                var center = new Vector3(bounds.Center.x, bounds.Center.y, bounds.Center.z);
                var size = new Vector3(
                    Mathf.Max(0.0f, bounds.Extents.x) * 2.0f,
                    Mathf.Max(0.0f, bounds.Extents.y) * 2.0f,
                    Mathf.Max(0.0f, bounds.Extents.z) * 2.0f);
                return new Bounds(center, size);
            }

            private void EnsureCachedPageBoundsCapacity(int pageCount)
            {
                if (pageCount <= m_CachedPageWorldBounds.Length)
                    return;

                int capacity = Mathf.Max(pageCount, m_CachedPageWorldBounds.Length == 0 ? 4 : m_CachedPageWorldBounds.Length * 2);
                Array.Resize(ref m_CachedPageWorldBounds, capacity);
            }

            private void ResetCachedRenderBounds()
            {
                CompletePendingBoundsJob();
                m_CachedWorldBounds = new Bounds(m_System != null ? m_System.transform.position : Vector3.zero, Vector3.zero);
                m_CachedPageWorldBoundsCount = 0;
                m_CachedBoundsParticleCount = -1;
                m_HasCachedWorldBounds = false;
                m_BoundsDirty = true;
            }

            internal bool IsVisibleInCullingContext(BatchCullingContext cullingContext)
            {
                return m_System != null
                    && m_System.isActiveAndEnabled
                    && ParticleSystemState.CanRender(m_System.rendererModule)
                    && activeCount > 0
                    && IsVisibleInCullingContext(m_HasCachedWorldBounds ? m_CachedWorldBounds : GetWorldBounds(), cullingContext);
            }

            internal unsafe bool TryCreateMeshVisibleCountWork(
                int meshCount,
                int outputOffset,
                out ParticleMeshVisibleCountWork work)
            {
                work = default;
                int count = activeCount;
                if (meshCount <= 1
                    || count <= 0
                    || m_System == null
                    || m_System.rendererModule.renderMode != VividParticleRenderMode.Mesh)
                {
                    return false;
                }

                if (!m_Storage.TryGetMeshIndexArray(out NativeArray<int> meshIndices))
                    return false;

                work = new ParticleMeshVisibleCountWork
                {
                    MeshIndices = (int*)meshIndices.GetUnsafeReadOnlyPtr(),
                    ActiveCount = Mathf.Min(count, meshIndices.Length),
                    MeshCount = Mathf.Max(1, meshCount),
                    OutputOffset = Mathf.Max(0, outputOffset),
                };
                return true;
            }

            internal unsafe int AppendCullingRecords(
                int batchBaseIndex,
                int spanBaseIndex,
                bool usesPageBillboard,
                bool isEditorSelected,
                NativeList<ParticleCullingRecord> records)
            {
                if (m_System == null
                    || !m_System.isActiveAndEnabled
                    || !ParticleSystemState.CanRender(m_System.rendererModule)
                    || activeCount <= 0
                    || !records.IsCreated)
                {
                    return 0;
                }

                EnsureCachedCullingBounds();
                int* meshIndexPtr = null;
                float3* positionPtr = null;
                int positionCapacity = 0;
                float4x4 localToWorld = ToFloat4x4(m_System.transform.localToWorldMatrix);
                int simulationSpace = (int)m_System.main.simulationSpace;
                if (m_Storage.TryGetCommonArrays(
                    out NativeArray<float3> positions,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
                {
                    positionPtr = (float3*)positions.GetUnsafeReadOnlyPtr();
                    positionCapacity = positions.Length;
                }

                if (m_Storage.TryGetMeshIndexArray(out NativeArray<int> meshIndices))
                    meshIndexPtr = (int*)meshIndices.GetUnsafeReadOnlyPtr();

                int recordStart = records.Length;
                int addedCount = 0;
                int particleCount = activeCount;
                int pageCount = Mathf.Min(m_CachedPageWorldBoundsCount, GetCullingRecordCount(particleCount));
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int pageStart = pageIndex * BillboardPageSize;
                    int pageParticleCount = Mathf.Min(BillboardPageSize, particleCount - pageStart);
                    if (pageParticleCount <= 0)
                        continue;

                    Bounds pageBounds = m_CachedPageWorldBounds[pageIndex];
                    AddCullingRecord(
                        records,
                        pageBounds,
                        batchBaseIndex + pageStart,
                        usesPageBillboard ? spanBaseIndex + addedCount : spanBaseIndex,
                        pageParticleCount,
                        usesPageBillboard,
                        isEditorSelected,
                        pageStart,
                        meshIndexPtr,
                        positionPtr,
                        positionCapacity,
                        localToWorld,
                        simulationSpace);
                    addedCount++;
                }

                return records.Length - recordStart;
            }

            private void EnsureCachedCullingBounds()
            {
                CompletePendingBoundsJob();

                int count = Mathf.Clamp(activeCount, 0, Mathf.Max(0, m_Capacity));
                if (!NeedsBoundsUpdate(count))
                    return;

                UpdateCachedRenderBoundsImmediate(count);
            }

            private unsafe void UpdateCachedRenderBoundsImmediate(int count)
            {
                if (m_System == null)
                {
                    SetEmptyCachedRenderBounds(Vector3.zero, 0);
                    return;
                }

                count = Mathf.Clamp(count, 0, activeCount);
                VividParticleRenderMode renderMode = m_System.rendererModule.renderMode;
                if (count <= 0
                    || !TryCreateBoundsSource(
                        count,
                        m_System.transform.localToWorldMatrix,
                        renderMode,
                        m_System.rendererModule.renderMesh,
                        m_System.rendererModule.sizeScale,
                        m_System.rendererModule.stretchLengthScale,
                        m_System.rendererModule.stretchSpeedScale,
                        out ParticleBoundsSource source))
                {
                    SetEmptyCachedRenderBounds(count);
                    return;
                }

                int pageCount = Mathf.Max(1, GetCullingRecordCount(count));
                EnsureCachedPageBoundsCapacity(pageCount);

                float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
                float3 max = new(float.MinValue, float.MinValue, float.MinValue);
                bool hasBounds = false;
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int pageStart = pageIndex * BillboardPageSize;
                    int pageCountForBounds = Mathf.Min(BillboardPageSize, count - pageStart);
                    ParticleBoundsData pageBounds = CalculateParticleBoundsPage(source, pageStart, pageCountForBounds);
                    m_CachedPageWorldBounds[pageIndex] = ToBounds(pageBounds, m_System.transform.position);

                    if (pageBounds.IsValid == 0)
                        continue;

                    min = math.min(min, pageBounds.Center - pageBounds.Extents);
                    max = math.max(max, pageBounds.Center + pageBounds.Extents);
                    hasBounds = true;
                }

                m_CachedWorldBounds = hasBounds
                    ? ToBounds(CreateBoundsData(min, max), m_System.transform.position)
                    : new Bounds(m_System.transform.position, Vector3.zero);
                m_CachedPageWorldBoundsCount = pageCount;
                m_CachedBoundsParticleCount = count;
                m_HasCachedWorldBounds = true;
                m_BoundsDirty = false;
            }

            private static unsafe void AddCullingRecord(
                NativeList<ParticleCullingRecord> records,
                Bounds bounds,
                int batchBaseIndex,
                int spanBaseIndex,
                int activeCount,
                bool usesPageBillboard,
                bool isEditorSelected,
                int particleStart,
                int* meshIndices,
                float3* positions,
                int positionCapacity,
                float4x4 localToWorld,
                int simulationSpace)
            {
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                records.Add(new ParticleCullingRecord
                {
                    BoundsCenter = new float3(center.x, center.y, center.z),
                    BoundsExtents = new float3(extents.x, extents.y, extents.z),
                    BatchBaseIndex = batchBaseIndex,
                    SpanBaseIndex = spanBaseIndex,
                    ActiveCount = activeCount,
                    UsesPageBillboard = usesPageBillboard ? 1 : 0,
                    IsEditorSelected = isEditorSelected ? 1 : 0,
                    ParticleStart = Mathf.Max(0, particleStart),
                    MeshIndices = meshIndices,
                    Positions = positions,
                    PositionCapacity = Mathf.Max(0, positionCapacity),
                    LocalToWorld = localToWorld,
                    SimulationSpace = simulationSpace,
                });
            }

            private bool RequiresAutomaticUpdate(VividParticleSystemFrameSnapshot snapshot, bool requireActive)
            {
                return (!requireActive || snapshot.IsActiveAndEnabled)
                    && !snapshot.IsPaused
                    && (snapshot.IsPlaying || (snapshot.StopEmitting && activeCount > 0));
            }

            private bool ScheduleSimulation(VividParticleSystemFrameSnapshot snapshot, bool allowEmission)
            {
                CompletePending();
                EnsureStorageCapacity(snapshot.MaxParticles);

                m_PendingSnapshot = snapshot;
                m_PendingAllowEmission = allowEmission;
                m_HasPendingSimulation = true;
                m_PendingFrame = Time.frameCount;

                return ScheduleIntegrateViaRegistry(snapshot);
            }

            private bool ScheduleSimulationBatch(
                VividParticleSystemFrameSnapshot snapshot,
                bool allowEmission,
                NativeList<VividParticleEcsIntegratePageWork> pageWorks,
                NativeList<VividParticleEcsCompactWork> compactWorks)
            {
                EnsureStorageCapacity(snapshot.MaxParticles);

                m_PendingSnapshot = snapshot;
                m_PendingAllowEmission = allowEmission;
                m_HasPendingSimulation = true;
                m_PendingFrame = Time.frameCount;
                m_PendingJob = default;
                m_HasPendingStandaloneJob = false;
                m_HasPendingJob = false;

                if (m_Storage.AddIntegratePageWorks(
                    snapshot.DeltaTime,
                    Vector3.down * (GravityAcceleration * snapshot.GravityModifier),
                    pageWorks,
                    compactWorks))
                {
                    m_HasPendingJob = true;
                    ScheduledJobCount++;
                    LastScheduledFrame = Time.frameCount;
                }
                else
                {
                    LastScheduledFrame = Time.frameCount;
                }

                return true;
            }

            internal bool CanScheduleIntegrateJob(VividParticleSystemFrameSnapshot snapshot)
            {
                return m_Storage.isCreated && activeCount > 0 && snapshot.DeltaTime > 0.0f;
            }

            internal JobHandle ScheduleIntegrateJob(
                VividParticleSystemFrameSnapshot snapshot,
                JobHandle dependency)
            {
                if (!m_Storage.ScheduleIntegrate(
                    snapshot.DeltaTime,
                    Vector3.down * (GravityAcceleration * snapshot.GravityModifier),
                    dependency,
                    out JobHandle handle))
                {
                    return dependency;
                }

                m_HasPendingJob = true;
                m_HasPendingStandaloneJob = true;
                ScheduledJobCount++;
                LastScheduledFrame = Time.frameCount;
                return handle;
            }

            private bool ScheduleIntegrateViaRegistry(VividParticleSystemFrameSnapshot snapshot)
            {
                m_PendingJob = default;
                m_HasPendingJob = false;
                m_HasPendingStandaloneJob = false;

                var context = new ParticleSimulationJobContext(this, snapshot);
                JobHandle scheduledHandle = s_SimulationJobRegistry.ScheduleEnabled(
                    context,
                    context.EnabledModuleFlags);
                if (!m_HasPendingJob)
                {
                    LastScheduledFrame = Time.frameCount;
                    return false;
                }

                m_PendingJob = scheduledHandle;
                m_HasPendingStandaloneJob = true;
                return true;
            }

            private float ResolveDeltaTime(float? deltaTimeOverride)
            {
                if (deltaTimeOverride.HasValue)
                    return Mathf.Max(0.0f, deltaTimeOverride.Value);

                if (Application.isPlaying)
                    return Mathf.Max(0.0f, Time.deltaTime);

#if UNITY_EDITOR
                double currentTime = EditorApplication.timeSinceStartup;
                if (m_LastEditorUpdateTime <= 0.0)
                {
                    m_LastEditorUpdateTime = currentTime;
                    return 0.0f;
                }

                float deltaTime = (float)(currentTime - m_LastEditorUpdateTime);
                m_LastEditorUpdateTime = currentTime;
                return Mathf.Clamp(deltaTime, 0.0f, MaximumEditorSimulationStep);
#else
                return 0.0f;
#endif
            }

            private void EnsureStorageCapacity(int maxParticles)
            {
                CompletePendingBoundsJob();
                m_Storage.EnsureCapacity(maxParticles);
                MarkBoundsDirty();
            }

            private static VividParticleSystemId ResolveSystemId(VividParticleSystem system)
            {
                return system != null
                    ? new VividParticleSystemId(system.GetEntityId().GetHashCode() & int.MaxValue)
                    : VividParticleSystemId.Invalid;
            }

            private static bool IsSelectedForEditorOutline(VividParticleSystem system)
            {
#if UNITY_EDITOR
                if (!s_EditorSelectionCacheInitialized)
                    RefreshEditorSelectionIdCache();

                return system != null
                    && system.gameObject != null
                    && (s_EditorSelectedEntityIds.Contains(EntityId.ToULong(system.gameObject.GetEntityId()))
                        || s_EditorSelectedEntityIds.Contains(EntityId.ToULong(system.GetEntityId())));
#else
                return false;
#endif
            }

            private void EnsureRandom(VividParticleSystemFrameSnapshot snapshot)
            {
                m_Random ??= CreateRandom(snapshot);
            }

            private void ResetRandom(VividParticleSystemFrameSnapshot snapshot)
            {
                m_Random = CreateRandom(snapshot);
            }

            private static System.Random CreateRandom(VividParticleSystemFrameSnapshot snapshot)
            {
                uint seed = snapshot.UseAutoRandomSeed
                    ? unchecked((uint)Environment.TickCount ^ (uint)snapshot.EntityHash)
                    : snapshot.RandomSeed;
                return new System.Random(unchecked((int)seed));
            }

            private void AdvanceEmission(VividParticleSystemFrameSnapshot snapshot, bool allowEmission)
            {
                AdvanceEmission(snapshot, allowEmission, default);
            }

            private void AdvanceEmission(
                VividParticleSystemFrameSnapshot snapshot,
                bool allowEmission,
                NativeList<VividParticleEcsInitializeParticlesWork> initializeWorks)
            {
                float remaining = snapshot.DeltaTime;
                float duration = snapshot.Duration;

                while (remaining > MinimumSimulationStep)
                {
                    float segmentEnd = Mathf.Min(duration, m_Time + remaining);
                    float segmentDelta = Mathf.Max(0.0f, segmentEnd - m_Time);

                    if (allowEmission && snapshot.EmissionEnabled && segmentDelta > 0.0f)
                        EmitForTimeRange(snapshot, m_Time, segmentEnd, segmentDelta, initializeWorks);

                    remaining -= segmentDelta;
                    m_Time = segmentEnd;

                    if (m_Time < duration)
                        break;

                    if (!snapshot.Loop)
                    {
                        m_Time = duration;
                        break;
                    }

                    m_Time = 0.0f;
                    ResetBurstState(snapshot.Bursts);

                    if (segmentDelta <= 0.0f)
                        break;
                }
            }

            private void EmitForTimeRange(
                VividParticleSystemFrameSnapshot snapshot,
                float startTime,
                float endTime,
                float deltaTime,
                NativeList<VividParticleEcsInitializeParticlesWork> initializeWorks)
            {
                m_EmissionAccumulator += snapshot.RateOverTime * deltaTime;
                int continuousCount = Mathf.FloorToInt(m_EmissionAccumulator);
                if (continuousCount > 0)
                {
                    m_EmissionAccumulator -= continuousCount;
                    Emit(continuousCount, snapshot, initializeWorks);
                }

                VividParticleBurst[] bursts = snapshot.Bursts;
                if (bursts == null || bursts.Length == 0)
                    return;

                EnsureBurstState(bursts);
                for (int index = 0; index < bursts.Length; index++)
                {
                    if (m_BurstTriggered[index])
                        continue;

                    VividParticleBurst burst = bursts[index];
                    if (burst.time < startTime || burst.time > endTime)
                        continue;

                    m_BurstTriggered[index] = true;
                    Emit(burst.count, snapshot, initializeWorks);
                }
            }

            private uint NextRandomSeed(VividParticleSystemFrameSnapshot snapshot)
            {
                EnsureRandom(snapshot);
                uint value = unchecked((uint)m_Random.Next(1, int.MaxValue));
                value ^= unchecked((uint)snapshot.EntityHash * 16777619u);
                return value == 0u ? 1u : value;
            }

            private void SpawnParticle(VividParticleSystemFrameSnapshot snapshot)
            {
                SampleShape(snapshot, m_Random, out Vector3 localPosition, out Vector3 localDirection);
                localDirection = localDirection.sqrMagnitude > 0.000001f
                    ? localDirection.normalized
                    : Vector3.forward;

                Vector3 position = localPosition;
                Vector3 velocity = localDirection * snapshot.StartSpeed;
                if (snapshot.SimulationSpace == VividParticleSystemSimulationSpace.World)
                {
                    position = snapshot.LocalToWorldMatrix.MultiplyPoint3x4(localPosition);
                    velocity = (snapshot.WorldRotation * localDirection).normalized * snapshot.StartSpeed;
                }

                m_Storage.Add(
                    position,
                    velocity,
                    snapshot.StartLifetime,
                    snapshot.StartLifetime,
                    snapshot.StartSize,
                    snapshot.StartColor,
                    ResolveParticleMeshIndex(snapshot));
            }

            private int ResolveParticleMeshIndex(VividParticleSystemFrameSnapshot snapshot)
            {
                int meshCount = Mathf.Max(0, snapshot.RendererMeshCount);
                if (meshCount <= 1)
                    return 0;

                return activeCount % meshCount;
            }

            private void EnsureBurstState(VividParticleBurst[] bursts)
            {
                int burstCount = bursts?.Length ?? 0;
                if (m_BurstTriggered == null || m_BurstTriggered.Length != burstCount)
                    m_BurstTriggered = new bool[burstCount];
            }

            private void ResetBurstState(VividParticleBurst[] bursts)
            {
                EnsureBurstState(bursts);
                Array.Clear(m_BurstTriggered, 0, m_BurstTriggered.Length);
            }

            private Vector3 GetParticleWorldPosition(Vector3 position)
            {
                return m_System != null && m_System.main.simulationSpace == VividParticleSystemSimulationSpace.Local
                    ? m_System.transform.TransformPoint(position)
                    : position;
            }

            private float GetParticleRenderSize(int particleIndex)
            {
                return Mathf.Max(
                    VividParticleMainModule.MinimumStartSize,
                    m_Storage.GetSize(particleIndex) * (m_System != null ? m_System.rendererModule.sizeScale : 1.0f));
            }

            private Matrix4x4 GetStretchParticleMatrix(int particleIndex, Vector3 position, float size)
            {
                Vector3 velocity = m_Storage.GetVelocity(particleIndex);
                Vector3 up = velocity.sqrMagnitude > 0.000001f
                    ? velocity.normalized
                    : Vector3.up;
                Vector3 right = Vector3.Cross(Vector3.forward, up);
                if (right.sqrMagnitude <= 0.000001f)
                    right = Vector3.Cross(Vector3.right, up);

                right.Normalize();
                Vector3 forward = Vector3.Cross(right, up).normalized;
                float length = Mathf.Max(
                    VividParticleMainModule.MinimumStartSize,
                    size * m_System.rendererModule.stretchLengthScale
                    + velocity.magnitude * m_System.rendererModule.stretchSpeedScale);

                var matrix = Matrix4x4.identity;
                matrix.SetColumn(0, new Vector4(right.x * size, right.y * size, right.z * size, 0.0f));
                matrix.SetColumn(1, new Vector4(up.x * length, up.y * length, up.z * length, 0.0f));
                matrix.SetColumn(2, new Vector4(forward.x * size, forward.y * size, forward.z * size, 0.0f));
                matrix.SetColumn(3, new Vector4(position.x, position.y, position.z, 1.0f));
                return matrix;
            }

            private bool EnsureResources()
            {
                int capacity = Mathf.Max(1, m_System.maxParticles);
                VividParticleRenderMode renderMode = m_System.rendererModule.renderMode;
                Mesh renderMesh = ParticleSystemState.ResolveRenderMesh(m_System.rendererModule);
                Material sourceMaterial = m_System.rendererModule.material;
                int renderQueueOffset = m_System.rendererModule.renderQueueOffset;
                bool materialChanged = m_SourceMaterial != sourceMaterial
                    || m_SourceMesh != renderMesh
                    || m_RenderMode != renderMode
                    || m_RenderQueueOffset != renderQueueOffset
                    || m_RegisteredMaterial == null;

                if (IsInitialized && !m_ResourcesDirty && m_Capacity == capacity && !materialChanged)
                    return true;

                ReleaseResources();
                m_Capacity = capacity;

                Material material = sourceMaterial;
                bool ownsMaterial = false;
                if (material == null)
                {
                    material = s_RendererManager.GetOrCreateDefaultMaterial(renderMode, renderQueueOffset);
                    if (material == null)
                    {
                        if (!m_MissingShaderWarningLogged)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[VividRP] Could not find shader '{DefaultShaderName}' for {nameof(VividParticleSystem)}.");
                            m_MissingShaderWarningLogged = true;
                        }

                        return false;
                    }
                }
                else if (ParticleSystemState.ShouldOwnMaterialInstance(sourceMaterial, renderQueueOffset))
                {
                    material = new Material(sourceMaterial)
                    {
                        name = $"{sourceMaterial.name} (Vivid Particle Instance)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    ownsMaterial = true;
                }

                if (ownsMaterial)
                {
                    ApplyRenderQueueOffset(material, sourceMaterial, renderQueueOffset);
                    ParticleSystemState.ConfigureParticleRenderMode(material, renderMode);
                    m_OwnedMaterial = material;
                }
                else
                {
                    ParticleSystemState.ConfigureParticleRenderMode(material, renderMode);
                }

                m_MissingShaderWarningLogged = false;
                m_SourceMaterial = sourceMaterial;
                m_SourceMesh = renderMesh;
                m_RenderQueueOffset = renderQueueOffset;
                m_RenderMode = renderMode;
                m_RegisteredMaterial = material;
                m_QuadMesh = renderMesh != null ? renderMesh : CreateBillboardPageMesh();
                m_OwnedMesh = renderMesh == null;

                m_BatchCreated = true;
                m_ResourcesDirty = false;
                CullingCallCount = 0;
                VisibleCullingCallCount = 0;
                LastVisible = false;
                LastViewType = default;
                LastDrawCommandCount = 0;
                m_HasUploadedRenderStateSnapshot = false;
                MarkAllInstanceDataDirty();
                return true;
            }

            internal static void ConfigureDefaultParticleMaterial(Material material)
            {
                SetColor(material, s_UnlitColorId, Color.white);
                SetColor(material, s_BaseColorId, Color.white);
                SetFloat(material, s_SurfaceTypeId, 1.0f);
                SetFloat(material, s_BlendModeId, 0.0f);
                SetFloat(material, s_CullModeId, (float)CullMode.Off);
                SetFloat(material, s_TransparentZWriteId, 0.0f);
                SetFloat(material, s_QueueOffsetId, 0.0f);
                SetFloat(material, s_SrcBlendId, (float)BlendMode.SrcAlpha);
                SetFloat(material, s_DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(material, s_AlphaSrcBlendId, (float)BlendMode.One);
                SetFloat(material, s_AlphaDstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(material, s_ZWriteId, 0.0f);
                material.SetOverrideTag("RenderType", "Transparent");
            }

            internal static void ConfigureParticleRenderMode(Material material, VividParticleRenderMode renderMode)
            {
                SetFloat(material, s_ParticleRenderModeId, (float)renderMode);
            }

            private static bool ShouldOwnMaterialInstance(Material sourceMaterial, int renderQueueOffset)
            {
                return sourceMaterial != null
                    && (renderQueueOffset != 0 || sourceMaterial.HasProperty(s_ParticleRenderModeId));
            }

            private static Mesh ResolveRenderMesh(VividParticleRendererModule rendererModule)
            {
                return rendererModule != null && rendererModule.renderMode == VividParticleRenderMode.Mesh
                    ? rendererModule.renderMesh
                    : null;
            }

            private static bool CanRender(VividParticleRendererModule rendererModule)
            {
                if (rendererModule == null || !rendererModule.enabled)
                    return false;

                return rendererModule.renderMode switch
                {
                    VividParticleRenderMode.None => false,
                    VividParticleRenderMode.Mesh => rendererModule.hasRenderMesh,
                    _ => true,
                };
            }

            internal static void ApplyRenderQueueOffset(
                Material material,
                Material sourceMaterial,
                int renderQueueOffset)
            {
                int baseQueue = sourceMaterial != null && sourceMaterial.renderQueue >= 0
                    ? sourceMaterial.renderQueue
                    : ResolveShaderRenderQueue(material?.shader);
                material.renderQueue = baseQueue + renderQueueOffset;
            }

            private static int ResolveShaderRenderQueue(Shader shader)
            {
                return shader != null && shader.renderQueue >= 0
                    ? shader.renderQueue
                    : (int)RenderQueue.Transparent;
            }

            private static void SetColor(Material material, int propertyId, Color value)
            {
                if (material != null && material.HasProperty(propertyId))
                    material.SetColor(propertyId, value);
            }

            private static void SetFloat(Material material, int propertyId, float value)
            {
                if (material != null && material.HasProperty(propertyId))
                    material.SetFloat(propertyId, value);
            }

            private void ReleaseResources()
            {
                m_BatchCreated = false;
                m_RendererInitialized = false;

                for (int index = 0; index < m_InstanceDataBuffers.Length; index++)
                {
                    m_InstanceDataBuffers[index]?.Dispose();
                    m_InstanceDataBuffers[index] = null;
                    m_InstanceDirtyRanges[index].Clear();
                }

                m_InstanceData = null;

                if (m_OwnedMaterial != null)
                {
                    CoreUtils.Destroy(m_OwnedMaterial);
                    m_OwnedMaterial = null;
                }

                if (m_OwnedMesh && m_QuadMesh != null)
                {
                    CoreUtils.Destroy(m_QuadMesh);
                    m_QuadMesh = null;
                }
                else
                {
                    m_QuadMesh = null;
                }

                m_RegisteredMaterial = null;
                m_SourceMesh = null;
                m_LastUploadedCount = 0;
                m_LastCompletedUploadActiveCount = 0;
                m_LastUploadedFrame = -1;
                m_LastUploadOperationCount = 0;
                m_LastUploadByteCount = 0;
                m_LastUploadBufferIndex = -1;
                m_InstanceDataBufferIndex = -1;
                m_HasUploadedRenderStateSnapshot = false;
                m_OwnedMesh = false;
            }

            private unsafe void UploadInstanceData()
            {
                if (m_System == null)
                    return;

                int count = Mathf.Min(activeCount, m_Capacity);
                m_LastUploadedCount = count;
                m_LastUploadOperationCount = 0;
                m_LastUploadByteCount = 0;

                MarkRenderStateDirtyIfNeeded(count);

                if (m_InstanceDataBufferIndex >= 0
                    && m_InstanceDirtyRanges[m_InstanceDataBufferIndex].EstimateUploadByteCount(count) <= 0)
                {
                    return;
                }

                int uploadBufferIndex = SelectUploadBufferIndex(count);
                if (uploadBufferIndex < 0)
                    return;

                GraphicsBuffer uploadBuffer = m_InstanceDataBuffers[uploadBufferIndex];
                if (uploadBuffer == null)
                    return;

                if (uploadBufferIndex != m_InstanceDataBufferIndex)
                {
                    m_InstanceDataBufferIndex = uploadBufferIndex;
                    m_InstanceData = uploadBuffer;
                    if (m_BatchCreated)
                        m_BRG.SetBatchBuffer(m_BatchID, uploadBuffer.bufferHandle);
                }

                InstanceUploadDirtyRanges dirtyRanges = m_InstanceDirtyRanges[uploadBufferIndex];
                dirtyRanges.Compact();
                for (int operationIndex = 0; operationIndex < dirtyRanges.Count; operationIndex++)
                {
                    InstanceUploadOperation operation = dirtyRanges[operationIndex];
                    int byteOffset = GetUploadOperationByteOffset(operation);
                    int byteCount = GetUploadOperationByteCount(operation, count);
                    if (byteCount <= 0)
                        continue;

                    int elementOffset = byteOffset / sizeof(int);
                    int elementCount = BufferCountForBytes(byteCount);
                    NativeArray<int> mappedData = default;
                    try
                    {
                        mappedData = uploadBuffer.LockBufferForWrite<int>(elementOffset, elementCount);
                        WriteUploadOperation((byte*)mappedData.GetUnsafePtr(), operation, count);
                    }
                    finally
                    {
                        if (mappedData.IsCreated)
                            uploadBuffer.UnlockBufferAfterWrite<int>(elementCount);
                    }

                    m_LastUploadOperationCount++;
                    m_LastUploadByteCount += byteCount;
                }

                dirtyRanges.Clear();
                m_LastUploadBufferIndex = uploadBufferIndex;
            }

            private static InstanceUploadDirtyRanges[] CreateInstanceDirtyRanges()
            {
                var ranges = new InstanceUploadDirtyRanges[InstanceDataBufferCount];
                for (int index = 0; index < ranges.Length; index++)
                    ranges[index] = new InstanceUploadDirtyRanges();

                return ranges;
            }

            private void MarkAllInstanceDataDirty()
            {
                MarkAllInstanceDataDirty(m_InstanceDirtyRanges[0]);
            }

            private void MarkInstanceRangeDirty(int startIndex, int count)
            {
                MarkInstanceRangeDirty(startIndex, count, GetCurrentGpuDataLayout().PerInstanceUploadColumnMask);
            }

            private void MarkInstanceRangeDirty(int startIndex, int count, int columnMask)
            {
                if (count <= 0)
                    return;

                startIndex = Mathf.Max(0, startIndex);
                count = Mathf.Min(count, Mathf.Max(0, m_Capacity - startIndex));
                if (count <= 0)
                    return;

                VividParticleGpuDataLayout layout = GetCurrentGpuDataLayout();
                columnMask &= layout.PerInstanceUploadColumnMask;
                if (columnMask == 0)
                    return;

                m_InstanceDirtyRanges[0].AddInstanceRange(startIndex, count, layout, columnMask);
                s_RendererManager.QueueUploadDirty(this);
                if (UploadColumnMaskAffectsBounds(columnMask))
                    MarkBoundsDirty();
            }

            private void MarkAllInstanceDataDirty(InstanceUploadDirtyRanges ranges)
            {
                ranges.Clear();
                ranges.AddZeroBlock();
                ranges.AddInstanceRange(0, m_Capacity, GetCurrentGpuDataLayout());
                s_RendererManager.QueueUploadDirty(this);
                MarkBoundsDirty();
            }

            private void MarkRenderStateDirtyIfNeeded(int count)
            {
                if (m_System == null)
                    return;

                VividParticleGpuDataLayout layout = GetCurrentGpuDataLayout();
                Matrix4x4 localToWorld = m_System.transform.localToWorldMatrix;
                Color rendererColor = m_System.rendererModule.color;
                float sizeScale = m_System.rendererModule.sizeScale;
                float stretchLengthScale = m_System.rendererModule.stretchLengthScale;
                float stretchSpeedScale = m_System.rendererModule.stretchSpeedScale;
                Vector3 pivot = m_System.rendererModule.pivot;
                float minParticleSize = m_System.rendererModule.minParticleSize;
                float maxParticleSize = m_System.rendererModule.maxParticleSize;
                Vector3 flip = m_System.rendererModule.flip;
                VividParticleRenderMode renderMode = m_System.rendererModule.renderMode;
                int previousActiveCount = m_LastUploadedRenderStateActiveCount;
                bool activeCountChanged = !m_HasUploadedRenderStateSnapshot || previousActiveCount != count;
                bool localToWorldChanged = !m_HasUploadedRenderStateSnapshot || m_LastUploadedLocalToWorldMatrix != localToWorld;
                bool rendererColorChanged = !m_HasUploadedRenderStateSnapshot || m_LastUploadedRendererColor != rendererColor;
                bool sizeScaleChanged = !m_HasUploadedRenderStateSnapshot
                    || !Mathf.Approximately(m_LastUploadedSizeScale, sizeScale);
                bool stretchLengthScaleChanged = !m_HasUploadedRenderStateSnapshot
                    || !Mathf.Approximately(m_LastUploadedStretchLengthScale, stretchLengthScale);
                bool stretchSpeedScaleChanged = !m_HasUploadedRenderStateSnapshot
                    || !Mathf.Approximately(m_LastUploadedStretchSpeedScale, stretchSpeedScale);
                bool pivotChanged = !m_HasUploadedRenderStateSnapshot || m_LastUploadedPivot != pivot;
                bool minParticleSizeChanged = !m_HasUploadedRenderStateSnapshot
                    || !Mathf.Approximately(m_LastUploadedMinParticleSize, minParticleSize);
                bool maxParticleSizeChanged = !m_HasUploadedRenderStateSnapshot
                    || !Mathf.Approximately(m_LastUploadedMaxParticleSize, maxParticleSize);
                bool flipChanged = !m_HasUploadedRenderStateSnapshot || m_LastUploadedFlip != flip;
                bool renderModeChanged = !m_HasUploadedRenderStateSnapshot || m_LastUploadedRenderMode != renderMode;
                bool renderStateChanged = !m_HasUploadedRenderStateSnapshot
                    || localToWorldChanged
                    || rendererColorChanged
                    || sizeScaleChanged
                    || stretchLengthScaleChanged
                    || stretchSpeedScaleChanged
                    || pivotChanged
                    || minParticleSizeChanged
                    || maxParticleSizeChanged
                    || flipChanged
                    || renderModeChanged;
                if (!renderStateChanged && !activeCountChanged)
                    return;

                if (renderStateChanged)
                {
                    bool hasPositionInfo =
                        layout.TryGetDataInfo(VividParticleGpuDataId.PositionSize, out VividParticleGpuDataInfo positionInfo);
                    bool hasColorInfo =
                        layout.TryGetDataInfo(VividParticleGpuDataId.BaseColor, out VividParticleGpuDataInfo colorInfo);
                    bool hasScaleInfo =
                        layout.TryGetDataInfo(VividParticleGpuDataId.Scale, out VividParticleGpuDataInfo scaleInfo);
                    bool hasVelocityInfo =
                        layout.TryGetDataInfo(VividParticleGpuDataId.VelocityStretch, out VividParticleGpuDataInfo velocityInfo);
                    bool rendererColorUsesPerInstanceData = hasColorInfo && colorInfo.IsPerInstance;
                    bool scaleUsesPerInstanceData = hasScaleInfo && scaleInfo.IsPerInstance;
                    bool velocityUsesPerInstanceData = hasVelocityInfo && velocityInfo.IsPerInstance;
                    bool transformAffectsInstancePositions =
                        localToWorldChanged
                        && m_System.main.simulationSpace == VividParticleSystemSimulationSpace.Local;
                    int instanceColumnMask = 0;
                    if (!m_HasUploadedRenderStateSnapshot || renderModeChanged)
                    {
                        instanceColumnMask = layout.PerInstanceUploadColumnMask;
                    }
                    else
                    {
                        if (transformAffectsInstancePositions && hasPositionInfo)
                            instanceColumnMask |= positionInfo.UploadColumnMask;
                        if (transformAffectsInstancePositions && velocityUsesPerInstanceData)
                            instanceColumnMask |= velocityInfo.UploadColumnMask;
                        if (rendererColorChanged && rendererColorUsesPerInstanceData)
                            instanceColumnMask |= colorInfo.UploadColumnMask;
                        if (sizeScaleChanged && scaleUsesPerInstanceData)
                            instanceColumnMask |= scaleInfo.UploadColumnMask;
                        if ((stretchLengthScaleChanged || stretchSpeedScaleChanged) && velocityUsesPerInstanceData)
                            instanceColumnMask |= velocityInfo.UploadColumnMask;
                    }

                    bool instanceRenderStateChanged = !m_HasUploadedRenderStateSnapshot
                        || transformAffectsInstancePositions
                        || (rendererColorChanged && rendererColorUsesPerInstanceData)
                        || (sizeScaleChanged && scaleUsesPerInstanceData)
                        || ((stretchLengthScaleChanged || stretchSpeedScaleChanged) && velocityUsesPerInstanceData)
                        || renderModeChanged;
                    uint sharedDataBits = 0u;
                    if (!m_HasUploadedRenderStateSnapshot || renderModeChanged)
                    {
                        sharedDataBits = layout.DataPerSharpBits;
                    }
                    else
                    {
                        if (activeCountChanged)
                            sharedDataBits |= layout.SharedDataBlockBits;

                        if (localToWorldChanged
                            || stretchLengthScaleChanged
                            || stretchSpeedScaleChanged
                            || pivotChanged
                            || minParticleSizeChanged
                            || maxParticleSizeChanged
                            || flipChanged)
                        {
                            sharedDataBits |= layout.SharedDataBlockBits;
                        }

                        if (rendererColorChanged && hasColorInfo && !rendererColorUsesPerInstanceData)
                            sharedDataBits |= colorInfo.DataBit;

                        if (sizeScaleChanged && hasScaleInfo && !scaleUsesPerInstanceData)
                            sharedDataBits |= scaleInfo.DataBit;

                        if ((stretchLengthScaleChanged || stretchSpeedScaleChanged)
                            && hasVelocityInfo
                            && !velocityUsesPerInstanceData)
                        {
                            sharedDataBits |= velocityInfo.DataBit;
                        }
                    }

                    if (instanceRenderStateChanged && count > 0)
                        MarkInstanceRangeDirty(0, count, instanceColumnMask);

                    MarkSharedDataDirty(sharedDataBits);
                }
                else if (activeCountChanged)
                {
                    MarkSharedDataDirty(layout.SharedDataBlockBits);

                    if (count < previousActiveCount)
                    {
                        int dirtyStart = UsesPageBillboardRenderMode(renderMode)
                            ? Mathf.Max(0, count / BillboardPageSize * BillboardPageSize)
                            : Mathf.Max(0, count);
                        int dirtyEnd = Mathf.Max(previousActiveCount, count);
                        MarkInstanceRangeDirty(dirtyStart, dirtyEnd - dirtyStart);
                    }
                }

                m_LastUploadedLocalToWorldMatrix = localToWorld;
                m_LastUploadedRendererColor = rendererColor;
                m_LastUploadedSizeScale = sizeScale;
                m_LastUploadedStretchLengthScale = stretchLengthScale;
                m_LastUploadedStretchSpeedScale = stretchSpeedScale;
                m_LastUploadedPivot = pivot;
                m_LastUploadedMinParticleSize = minParticleSize;
                m_LastUploadedMaxParticleSize = maxParticleSize;
                m_LastUploadedFlip = flip;
                m_LastUploadedRenderStateActiveCount = count;
                m_LastUploadedRenderMode = renderMode;
                m_HasUploadedRenderStateSnapshot = true;
            }

            private void MarkSharedDataDirty()
            {
                MarkSharedDataDirty(GetCurrentGpuDataLayout().DataPerSharpBits);
            }

            private void MarkSharedDataDirty(uint dataBits)
            {
                dataBits &= GetCurrentGpuDataLayout().DataPerSharpBits;
                if (dataBits == 0u)
                    return;

                m_SharedDataDirty = true;
                m_SharedDataDirtyBits |= dataBits;
                s_RendererManager.QueueUploadDirty(this);
            }

            internal bool TryGetUploadRange(
                bool forceFullUpload,
                out int startIndex,
                out int count,
                out int columnMask,
                out bool spanDataDirty)
            {
                int active = Mathf.Min(activeCount, m_Capacity);
                spanDataDirty = forceFullUpload || active != m_LastCompletedUploadActiveCount;
                if (forceFullUpload)
                {
                    startIndex = 0;
                    count = active;
                    columnMask = GetCurrentGpuDataLayout().PerInstanceUploadColumnMask;
                    return count > 0;
                }

                return m_InstanceDirtyRanges[0].TryGetInstanceRange(active, out startIndex, out count, out columnMask);
            }

            internal bool HasPendingUploadData()
            {
                return m_InstanceDirtyRanges[0].HasPendingData || m_SharedDataDirty;
            }

            internal void ClearUploadDirty()
            {
                m_InstanceDirtyRanges[0].Clear();
                m_SharedDataDirty = false;
                m_SharedDataDirtyBits = 0u;
            }

            internal bool HasPendingSharedData()
            {
                return m_SharedDataDirty;
            }

            internal void MarkEditorSelectionSharedDataDirty()
            {
                MarkSharedDataDirty(GetCurrentGpuDataLayout().SharedDataBlockBits);
            }

            internal uint GetPendingSharedDataBits()
            {
                return m_SharedDataDirty ? m_SharedDataDirtyBits : 0u;
            }

            internal unsafe bool TryCreateRenderUploadSource(
                int batchBaseIndex,
                int batchCapacity,
                int batchDataOffset,
                byte* bufferBase,
                out ParticleRenderUploadSource source)
            {
                source = default;
                if (m_System == null || !m_Storage.isCreated)
                    return false;

                if (!m_Storage.TryGetCommonArrays(
                    out NativeArray<float3> positions,
                    out NativeArray<float3> velocities,
                    out NativeArray<float> startLifetimes,
                    out NativeArray<float> remainingLifetimes,
                    out NativeArray<float4> colors,
                    out NativeArray<float> sizes))
                {
                    return false;
                }

                if (!m_Storage.TryGetMeshIndexArray(out NativeArray<int> meshIndices))
                    return false;

                source = new ParticleRenderUploadSource
                {
                    Positions = (float3*)positions.GetUnsafeReadOnlyPtr(),
                    Velocities = (float3*)velocities.GetUnsafeReadOnlyPtr(),
                    StartLifetimes = (float*)startLifetimes.GetUnsafeReadOnlyPtr(),
                    RemainingLifetimes = (float*)remainingLifetimes.GetUnsafeReadOnlyPtr(),
                    Colors = (float4*)colors.GetUnsafeReadOnlyPtr(),
                    Sizes = (float*)sizes.GetUnsafeReadOnlyPtr(),
                    MeshIndices = (int*)meshIndices.GetUnsafeReadOnlyPtr(),
                    ActiveCount = activeCount,
                    BatchBaseIndex = batchBaseIndex,
                    BatchCapacity = batchCapacity,
                    BatchDataOffset = batchDataOffset,
                    LocalToWorld = ToFloat4x4(m_System.transform.localToWorldMatrix),
                    SimulationSpace = (int)m_System.main.simulationSpace,
                    RenderMode = (int)m_System.rendererModule.renderMode,
                    SizeScale = m_System.rendererModule.sizeScale,
                    StretchLengthScale = m_System.rendererModule.stretchLengthScale,
                    StretchSpeedScale = m_System.rendererModule.stretchSpeedScale,
                    MeshCount = Mathf.Max(0, m_System.rendererModule.meshCount),
                    RendererColor = ToFloat4(m_System.rendererModule.color),
                    BufferBase = bufferBase,
                };
                return true;
            }

            internal bool TryGetPerSharpGpuDataValue(
                VividParticleGpuDataId dataId,
                out float4 value)
            {
                value = default;
                if (m_System == null)
                    return false;

                switch (dataId)
                {
                    case VividParticleGpuDataId.BaseColor:
                        value = ToFloat4(m_System.main.startColor * m_System.rendererModule.color);
                        return true;
                    case VividParticleGpuDataId.Scale:
                        float size = Mathf.Max(
                            VividParticleMainModule.MinimumStartSize,
                            m_System.main.startSize * m_System.rendererModule.sizeScale);
                        value = new float4(size, size, size, 1.0f);
                        return true;
                    case VividParticleGpuDataId.Rotation:
                        value = new float4(0.0f, 0.0f, 0.0f, 1.0f);
                        return true;
                    case VividParticleGpuDataId.VelocityStretch:
                        Vector3 velocity = m_System.transform.forward * m_System.main.startSpeed;
                        value = new float4(
                            velocity.x,
                            velocity.y,
                            velocity.z,
                            Mathf.Max(VividParticleMainModule.MinimumStartSize, m_System.main.startSize));
                        return true;
                    case VividParticleGpuDataId.UV:
                        value = new float4(0.0f, 0.0f, 1.0f, 1.0f);
                        return true;
                    case VividParticleGpuDataId.CustomData1:
                    case VividParticleGpuDataId.CustomData2:
                    case VividParticleGpuDataId.MeshIndex:
                        value = float4.zero;
                        return true;
                    default:
                        return false;
                }
            }

            internal void SetRendererUploadStats(
                bool initialized,
                int lastUploadedCount,
                int lastUploadOperationCount,
                int lastUploadByteCount,
                int lastUploadBufferIndex)
            {
                m_RendererInitialized = initialized;
                m_LastUploadedCount = lastUploadedCount;
                m_LastCompletedUploadActiveCount = initialized ? lastUploadedCount : 0;
                m_LastUploadOperationCount = lastUploadOperationCount;
                m_LastUploadByteCount = lastUploadByteCount;
                m_LastUploadBufferIndex = lastUploadBufferIndex;
            }

            internal void RecordCulling(
                BatchCullingViewType viewType,
                bool visible,
                int drawCommandCount,
                int visibleInstanceCount)
            {
                CullingCallCount++;
                LastViewType = viewType;
                LastVisible = visible;
                LastDrawCommandCount = drawCommandCount;
                LastVisibleInstanceCount = visibleInstanceCount;
                if (visible)
                    VisibleCullingCallCount++;
            }

            internal void ResetRendererCullingStats()
            {
                CullingCallCount = 0;
                VisibleCullingCallCount = 0;
                LastVisible = false;
                LastViewType = default;
                LastDrawCommandCount = 0;
                LastVisibleInstanceCount = 0;
            }

            private int SelectUploadBufferIndex(int activeCount)
            {
                int bestIndex = -1;
                int bestByteCount = int.MaxValue;
                int startIndex = m_InstanceDataBufferIndex < 0 ? 0 : (m_InstanceDataBufferIndex + 1) % InstanceDataBufferCount;

                for (int offset = 0; offset < m_InstanceDirtyRanges.Length; offset++)
                {
                    int index = (startIndex + offset) % m_InstanceDirtyRanges.Length;
                    int byteCount = m_InstanceDirtyRanges[index].EstimateUploadByteCount(activeCount);
                    if (byteCount <= 0)
                        continue;

                    if (byteCount < bestByteCount)
                    {
                        bestByteCount = byteCount;
                        bestIndex = index;
                    }
                }

                return bestIndex;
            }

            private int GetUploadOperationByteOffset(InstanceUploadOperation operation)
            {
                return operation.Segment switch
                {
                    InstanceUploadSegment.ZeroBlock => 0,
                    InstanceUploadSegment.PositionSize => PositionSizeByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfFloat4,
                    InstanceUploadSegment.BaseColor => BaseColorByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfFloat4,
                    InstanceUploadSegment.Rotation => RotationByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfFloat4,
                    InstanceUploadSegment.VelocityStretch => VelocityStretchByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfFloat4,
                    _ => 0,
                };
            }

            private int GetUploadOperationByteCount(InstanceUploadOperation operation, int activeCount)
            {
                if (operation.Segment == InstanceUploadSegment.ZeroBlock)
                    return ZeroBlockByteSize;

                int count = Mathf.Clamp(activeCount - operation.StartIndex, 0, operation.Count);
                return count * SizeOfFloat4;
            }

            private unsafe void WriteUploadOperation(byte* baseAddress, InstanceUploadOperation operation, int activeCount)
            {
                if (operation.Segment == InstanceUploadSegment.ZeroBlock)
                {
                    UnsafeUtility.MemClear(baseAddress, ZeroBlockByteSize);
                    return;
                }

                int endIndex = Mathf.Min(activeCount, operation.StartIndex + operation.Count);
                if (endIndex <= operation.StartIndex)
                    return;

                switch (operation.Segment)
                {
                    case InstanceUploadSegment.PositionSize:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            Matrix4x4 objectToWorld = GetParticleObjectToWorldMatrix(index);
                            Vector3 position = objectToWorld.GetColumn(3);
                            Vector3 axis = objectToWorld.GetColumn(0);
                            float size = Mathf.Max(VividParticleMainModule.MinimumStartSize, axis.magnitude);
                            WriteArrayElement(
                                baseAddress,
                                0,
                                index - operation.StartIndex,
                                new Vector4(position.x, position.y, position.z, size));
                        }
                        break;
                    case InstanceUploadSegment.BaseColor:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            WriteArrayElement(
                                baseAddress,
                                0,
                                index - operation.StartIndex,
                                (Vector4)GetParticleRenderColor(index));
                        }
                        break;
                    case InstanceUploadSegment.Rotation:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            WriteArrayElement(
                                baseAddress,
                                0,
                                index - operation.StartIndex,
                                new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                        }
                        break;
                    case InstanceUploadSegment.VelocityStretch:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            Matrix4x4 objectToWorld = GetParticleObjectToWorldMatrix(index);
                            Vector3 velocity = m_Storage.IsValidIndex(index) ? m_Storage.GetVelocity(index) : Vector3.zero;
                            float length = m_System != null && m_System.rendererModule.renderMode == VividParticleRenderMode.Stretch
                                ? objectToWorld.GetColumn(1).magnitude
                                : objectToWorld.GetColumn(0).magnitude;
                            WriteArrayElement(
                                baseAddress,
                                0,
                                index - operation.StartIndex,
                                new Vector4(velocity.x, velocity.y, velocity.z, length));
                        }
                        break;
                }
            }

            private unsafe JobHandle OnPerformCulling(
                BatchRendererGroup rendererGroup,
                BatchCullingContext cullingContext,
                BatchCullingOutput cullingOutput,
                IntPtr userContext)
            {
                CullingCallCount++;
                LastViewType = cullingContext.viewType;

                bool visible = m_System != null
                    && m_System.isActiveAndEnabled
                    && ParticleSystemState.CanRender(m_System.rendererModule)
                    && activeCount > 0
                    && IsVisibleInCullingContext(GetWorldBounds(), cullingContext);
                LastVisible = visible;

                int visibleCount = visible ? Mathf.Min(activeCount, m_LastUploadedCount) : 0;
                if (!visible || !m_BatchCreated || visibleCount <= 0)
                {
                    LastDrawCommandCount = 0;
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                VisibleCullingCallCount++;
                LastDrawCommandCount = 1;
                WriteVisibleDrawCommands(cullingOutput, visibleCount);
                return default;
            }

            private static bool IsVisibleInCullingContext(Bounds bounds, BatchCullingContext cullingContext)
            {
                NativeArray<Plane> planes = cullingContext.cullingPlanes;
                if (!planes.IsCreated || planes.Length == 0)
                    return true;

                var splits = cullingContext.cullingSplits;
                if (!splits.IsCreated || splits.Length == 0)
                    return IntersectsCullingPlanes(bounds, planes, 0, planes.Length);

                for (int splitIndex = 0; splitIndex < splits.Length; splitIndex++)
                {
                    var split = splits[splitIndex];
                    if (split.cullingPlaneCount <= 0)
                        return true;

                    if (IntersectsCullingPlanes(bounds, planes, split.cullingPlaneOffset, split.cullingPlaneCount))
                        return true;
                }

                return false;
            }

            private unsafe void WriteVisibleDrawCommands(BatchCullingOutput cullingOutput, int visibleCount)
            {
                var draws = new BatchCullingOutputDrawCommands
                {
                    drawCommandCount = 1,
                    drawRangeCount = 1,
                    visibleInstanceCount = visibleCount,
                    drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawCommand>(),
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawRange>(),
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    visibleInstances = (int*)UnsafeUtility.Malloc(
                        sizeof(int) * visibleCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawCommandPickingEntityIds = null,
                    instanceSortingPositions = null,
                    instanceSortingPositionFloatCount = 0,
                };

                draws.drawCommands[0] = new BatchDrawCommand
                {
                    visibleOffset = 0,
                    visibleCount = (uint)visibleCount,
                    batchID = m_BatchID,
                    materialID = m_MaterialID,
                    meshID = m_MeshID,
                    submeshIndex = 0,
                    splitVisibilityMask = 0xff,
                    flags = BatchDrawCommandFlags.None,
                    sortingPosition = 0,
                };

                draws.drawRanges[0] = new BatchDrawRange
                {
                    drawCommandsBegin = 0,
                    drawCommandsCount = 1,
                    drawCommandsType = BatchDrawCommandType.Direct,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = uint.MaxValue,
                        layer = m_System != null ? (byte)m_System.gameObject.layer : (byte)0,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                    },
                };

                for (int index = 0; index < visibleCount; index++)
                    draws.visibleInstances[index] = index;

                cullingOutput.drawCommands[0] = draws;
            }

            private static void WriteEmptyDrawCommands(BatchCullingOutput cullingOutput)
            {
                cullingOutput.drawCommands[0] = new BatchCullingOutputDrawCommands();
            }

            private static Mesh CreateBillboardPageMesh()
            {
                var mesh = new Mesh
                {
                    name = $"Vivid Particle Billboard Page ({BillboardPageSize})",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                int vertexCount = BillboardPageSize * 4;
                int indexCount = BillboardPageSize * 6;
                var vertices = new Vector3[vertexCount];
                var uvs = new Vector2[vertexCount];
                var slots = new Vector2[vertexCount];
                var indices = new int[indexCount];
                Vector3[] quadVertices =
                {
                    new(-0.5f, -0.5f, 0.0f),
                    new(-0.5f, 0.5f, 0.0f),
                    new(0.5f, 0.5f, 0.0f),
                    new(0.5f, -0.5f, 0.0f),
                };
                Vector2[] quadUvs =
                {
                    new(0.0f, 0.0f),
                    new(0.0f, 1.0f),
                    new(1.0f, 1.0f),
                    new(1.0f, 0.0f),
                };

                for (int slot = 0; slot < BillboardPageSize; slot++)
                {
                    int vertexOffset = slot * 4;
                    int indexOffset = slot * 6;
                    for (int vertex = 0; vertex < 4; vertex++)
                    {
                        vertices[vertexOffset + vertex] = quadVertices[vertex];
                        uvs[vertexOffset + vertex] = quadUvs[vertex];
                        slots[vertexOffset + vertex] = new Vector2(slot, 0.0f);
                    }

                    indices[indexOffset] = vertexOffset;
                    indices[indexOffset + 1] = vertexOffset + 1;
                    indices[indexOffset + 2] = vertexOffset + 2;
                    indices[indexOffset + 3] = vertexOffset;
                    indices[indexOffset + 4] = vertexOffset + 2;
                    indices[indexOffset + 5] = vertexOffset + 3;
                }

                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetUVs(1, slots);
                mesh.SetTriangles(indices, 0);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                return mesh;
            }

            private static GraphicsBuffer CreateInstanceBuffer(int capacity)
            {
                return new GraphicsBuffer(
                    ResolveBufferTarget(),
                    GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    BufferCountForBytes(InstanceDataByteSize(capacity)),
                    sizeof(int));
            }

            private static GraphicsBuffer.Target ResolveBufferTarget()
            {
                GraphicsBuffer.Target target = GraphicsBuffer.Target.Raw;
                if (BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                    || SystemInfo.graphicsDeviceType is GraphicsDeviceType.OpenGLCore or GraphicsDeviceType.OpenGLES3)
                {
                    target |= GraphicsBuffer.Target.Constant;
                }

                return target;
            }

            private static uint ResolveBufferWindowSize()
            {
                return BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                    ? (uint)BatchRendererGroup.GetConstantBufferMaxWindowSize()
                    : 0u;
            }

            private static int BufferCountForBytes(int byteCount)
            {
                return (byteCount + sizeof(int) - 1) / sizeof(int);
            }

            private static unsafe void WriteArrayElement<T>(
                byte* baseAddress,
                int byteOffset,
                int index,
                T value)
                where T : struct
            {
                UnsafeUtility.WriteArrayElement(baseAddress + byteOffset, index, value);
            }

            private static void SampleShape(
                VividParticleSystemFrameSnapshot snapshot,
                System.Random random,
                out Vector3 localPosition,
                out Vector3 localDirection)
            {
                random ??= new System.Random(1);
                if (!snapshot.ShapeEnabled)
                {
                    localPosition = Vector3.zero;
                    localDirection = Vector3.forward;
                    return;
                }

                switch (snapshot.ShapeType)
                {
                    case VividParticleShapeType.Sphere:
                        localPosition = SampleInsideUnitSphere(random) * snapshot.ShapeRadius;
                        localDirection = localPosition.sqrMagnitude > 0.000001f
                            ? localPosition.normalized
                            : SampleUnitVector(random);
                        break;
                    case VividParticleShapeType.Box:
                        localPosition = new Vector3(
                            RandomRange(random, -snapshot.ShapeBoxSize.x * 0.5f, snapshot.ShapeBoxSize.x * 0.5f),
                            RandomRange(random, -snapshot.ShapeBoxSize.y * 0.5f, snapshot.ShapeBoxSize.y * 0.5f),
                            RandomRange(random, -snapshot.ShapeBoxSize.z * 0.5f, snapshot.ShapeBoxSize.z * 0.5f));
                        localDirection = Vector3.forward;
                        break;
                    case VividParticleShapeType.Cone:
                        float diskRadius = Mathf.Max(0.0f, snapshot.ShapeRadius);
                        Vector2 disk = SampleInsideUnitCircle(random) * diskRadius;
                        localPosition = new Vector3(disk.x, disk.y, 0.0f);
                        localDirection = SampleConeDirection(random, snapshot.ShapeAngle);
                        break;
                    default:
                        localPosition = Vector3.zero;
                        localDirection = Vector3.forward;
                        break;
                }
            }

            private static Vector3 SampleInsideUnitSphere(System.Random random)
            {
                Vector3 value;
                do
                {
                    value = new Vector3(
                        RandomRange(random, -1.0f, 1.0f),
                        RandomRange(random, -1.0f, 1.0f),
                        RandomRange(random, -1.0f, 1.0f));
                }
                while (value.sqrMagnitude > 1.0f);

                return value;
            }

            private static Vector2 SampleInsideUnitCircle(System.Random random)
            {
                Vector2 value;
                do
                {
                    value = new Vector2(
                        RandomRange(random, -1.0f, 1.0f),
                        RandomRange(random, -1.0f, 1.0f));
                }
                while (value.sqrMagnitude > 1.0f);

                return value;
            }

            private static Vector3 SampleUnitVector(System.Random random)
            {
                Vector3 value = SampleInsideUnitSphere(random);
                return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
            }

            private static Vector3 SampleConeDirection(System.Random random, float angle)
            {
                float clampedAngle = Mathf.Clamp(angle, 0.0f, 89.0f) * Mathf.Deg2Rad;
                float cosMin = Mathf.Cos(clampedAngle);
                float cosTheta = RandomRange(random, cosMin, 1.0f);
                float sinTheta = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - cosTheta * cosTheta));
                float phi = RandomRange(random, 0.0f, Mathf.PI * 2.0f);
                return new Vector3(
                    Mathf.Cos(phi) * sinTheta,
                    Mathf.Sin(phi) * sinTheta,
                    cosTheta).normalized;
            }

            private static float RandomRange(System.Random random, float minInclusive, float maxInclusive)
            {
                return minInclusive + (float)random.NextDouble() * (maxInclusive - minInclusive);
            }
        }

        internal enum InstanceUploadSegment
        {
            ZeroBlock,
            PositionSize,
            BaseColor,
            Rotation,
            VelocityStretch,
            Scale,
            UV,
            CustomData1,
            CustomData2,
            MeshIndex,
        }

        internal static int GetUploadColumnMask(InstanceUploadSegment segment)
        {
            return segment switch
            {
                InstanceUploadSegment.PositionSize => UploadColumnPositionSizeMask,
                InstanceUploadSegment.BaseColor => UploadColumnBaseColorMask,
                InstanceUploadSegment.Rotation => UploadColumnRotationMask,
                InstanceUploadSegment.VelocityStretch => UploadColumnVelocityStretchMask,
                InstanceUploadSegment.Scale => UploadColumnScaleMask,
                InstanceUploadSegment.UV => UploadColumnUVMask,
                InstanceUploadSegment.CustomData1 => UploadColumnCustomData1Mask,
                InstanceUploadSegment.CustomData2 => UploadColumnCustomData2Mask,
                InstanceUploadSegment.MeshIndex => UploadColumnMeshIndexMask,
                _ => 0,
            };
        }

        internal static bool UploadColumnMaskAffectsBounds(int columnMask)
        {
            const int boundsMask = UploadColumnPositionSizeMask
                | UploadColumnVelocityStretchMask
                | UploadColumnScaleMask;
            return (columnMask & boundsMask) != 0;
        }

        private static ParticleRenderJobFlags GetRenderJobFlagsForUploadColumnMask(int columnMask)
        {
            ParticleRenderJobFlags flags = ParticleRenderJobFlags.None;
            if ((columnMask & UploadColumnTransformMask) != 0)
                flags |= ParticleRenderJobFlags.TransformUpload;

            if ((columnMask & UploadColumnBaseColorMask) != 0)
                flags |= ParticleRenderJobFlags.ColorUpload;

            if ((columnMask & UploadColumnVelocityStretchMask) != 0)
                flags |= ParticleRenderJobFlags.VelocityStretchUpload;

            if ((columnMask & UploadColumnUVMask) != 0)
                flags |= ParticleRenderJobFlags.UVUpload;

            if ((columnMask & UploadColumnCustomDataMask) != 0)
                flags |= ParticleRenderJobFlags.CustomDataUpload;

            if ((columnMask & UploadColumnMeshIndexMask) != 0)
                flags |= ParticleRenderJobFlags.MeshIndexUpload;

            return flags;
        }

        private static int CountRenderPageJobModules(uint renderJobModuleFlags)
        {
            return VividParticleRenderJobPipeline.CountEnabledPageModules(renderJobModuleFlags);
        }

        internal static bool CanMergeUploadCopyOperations(
            int previousSrcOffset,
            int previousDstOffset,
            int previousSize,
            int nextSrcOffset,
            int nextDstOffset)
        {
            return previousSrcOffset + previousSize == nextSrcOffset
                && previousDstOffset + previousSize == nextDstOffset;
        }

        internal static int CompareUploadCopyOperationsForMerge(
            int leftByteOffset,
            int leftByteCount,
            int rightByteOffset,
            int rightByteCount)
        {
            int offsetCompare = leftByteOffset.CompareTo(rightByteOffset);
            return offsetCompare != 0 ? offsetCompare : leftByteCount.CompareTo(rightByteCount);
        }

        internal static bool ShouldCopyGpuDataForUploadWork(
            VividParticleGpuDataCopyDescriptor copyDescriptor,
            bool hasInstanceRange,
            bool hasSpanData,
            bool hasSharedData,
            int columnMask,
            uint sharedDataBits)
        {
            return VividParticleRendererManager.ShouldCopyGpuDataForUploadWork(
                copyDescriptor,
                hasInstanceRange,
                hasSpanData,
                hasSharedData,
                columnMask,
                sharedDataBits);
        }

        internal enum VividParticleGpuDataId
        {
            SharedData,
            SpanSharedData,
            PositionSize,
            BaseColor,
            Rotation,
            VelocityStretch,
            Scale,
            UV,
            CustomData1,
            CustomData2,
            MeshIndex,
        }

        internal enum VividParticleGpuDataFrequency
        {
            Shared,
            Span,
            PerInstance,
            PerSharp,
        }

        internal enum VividParticleGpuDataRole
        {
            SharedValue,
            SharedDataBlock,
            SpanSharedDataBlock,
            PerInstanceValue,
            PerSharpValue,
        }

        internal static uint GetGpuDataBit(VividParticleGpuDataId dataId)
        {
            return 1u << (int)dataId;
        }

        internal readonly struct VividParticleGpuDataInfo : IEquatable<VividParticleGpuDataInfo>
        {
            public VividParticleGpuDataInfo(
                VividParticleGpuDataId dataId,
                VividParticleGpuDataFrequency frequency,
                int elementSize,
                InstanceUploadSegment uploadSegment)
            {
                DataId = dataId;
                Frequency = frequency;
                ElementSize = Mathf.Max(1, elementSize);
                UploadSegment = uploadSegment;
                DataBit = GetGpuDataBit(dataId);
                UploadColumnMask = GetUploadColumnMask(uploadSegment);
                RenderJobFlagMask = (uint)GetRenderJobFlagsForUploadColumnMask(UploadColumnMask);
            }

            public VividParticleGpuDataId DataId { get; }

            public VividParticleGpuDataFrequency Frequency { get; }

            public int ElementSize { get; }

            public InstanceUploadSegment UploadSegment { get; }

            public uint DataBit { get; }

            public int UploadColumnMask { get; }

            public uint RenderJobFlagMask { get; }

            public bool IsPerInstance => Frequency == VividParticleGpuDataFrequency.PerInstance;

            public bool UsesInstanceMetadata => Frequency is VividParticleGpuDataFrequency.PerInstance
                or VividParticleGpuDataFrequency.Span;

            public bool HasUploadSegment => UploadSegment != InstanceUploadSegment.ZeroBlock;

            public bool CreatesRecordCopyDescriptor => Frequency is VividParticleGpuDataFrequency.PerInstance
                or VividParticleGpuDataFrequency.PerSharp
                or VividParticleGpuDataFrequency.Span;

            public bool IsSharedValue => Frequency == VividParticleGpuDataFrequency.Shared;

            public bool IsPerSharpValue => Frequency == VividParticleGpuDataFrequency.PerSharp
                && DataId != VividParticleGpuDataId.SharedData;

            public bool IsSharedDataBlock => DataId == VividParticleGpuDataId.SharedData;

            public bool IsSpanSharedDataBlock => DataId == VividParticleGpuDataId.SpanSharedData;

            public VividParticleGpuDataRole Role
            {
                get
                {
                    if (IsSharedDataBlock)
                        return VividParticleGpuDataRole.SharedDataBlock;

                    if (IsSpanSharedDataBlock)
                        return VividParticleGpuDataRole.SpanSharedDataBlock;

                    if (IsSharedValue)
                        return VividParticleGpuDataRole.SharedValue;

                    if (IsPerSharpValue)
                        return VividParticleGpuDataRole.PerSharpValue;

                    return VividParticleGpuDataRole.PerInstanceValue;
                }
            }

            public bool Equals(VividParticleGpuDataInfo other)
            {
                return DataId == other.DataId
                    && Frequency == other.Frequency
                    && ElementSize == other.ElementSize
                    && UploadSegment == other.UploadSegment;
            }

            public override bool Equals(object obj)
            {
                return obj is VividParticleGpuDataInfo other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)DataId;
                    hash = (hash * 397) ^ (int)Frequency;
                    hash = (hash * 397) ^ ElementSize;
                    hash = (hash * 397) ^ (int)UploadSegment;
                    return hash;
                }
            }
        }

        internal readonly struct VividParticleGpuDataLayoutDescriptor : IEquatable<VividParticleGpuDataLayoutDescriptor>
        {
            public VividParticleGpuDataLayoutDescriptor(
                VividParticleRenderMode renderMode,
                VividParticleGpuDataMode colorDataMode,
                VividParticleGpuDataMode rotationDataMode,
                VividParticleGpuDataMode velocityDataMode,
                VividParticleGpuDataMode sizeDataMode,
                bool includeUV,
                bool includeCustomData1,
                bool includeCustomData2,
                bool includeMeshIndex)
            {
                RenderMode = renderMode;
                ColorDataMode = colorDataMode;
                RotationDataMode = rotationDataMode;
                VelocityDataMode = velocityDataMode;
                SizeDataMode = sizeDataMode;
                IncludeUV = includeUV;
                IncludeCustomData1 = includeCustomData1;
                IncludeCustomData2 = includeCustomData2;
                IncludeMeshIndex = includeMeshIndex;
            }

            public VividParticleRenderMode RenderMode { get; }

            public VividParticleGpuDataMode ColorDataMode { get; }

            public VividParticleGpuDataMode RotationDataMode { get; }

            public VividParticleGpuDataMode VelocityDataMode { get; }

            public VividParticleGpuDataMode SizeDataMode { get; }

            public bool IncludeUV { get; }

            public bool IncludeCustomData1 { get; }

            public bool IncludeCustomData2 { get; }

            public bool IncludeMeshIndex { get; }

            public bool Equals(VividParticleGpuDataLayoutDescriptor other)
            {
                return RenderMode == other.RenderMode
                    && ColorDataMode == other.ColorDataMode
                    && RotationDataMode == other.RotationDataMode
                    && VelocityDataMode == other.VelocityDataMode
                    && SizeDataMode == other.SizeDataMode
                    && IncludeUV == other.IncludeUV
                    && IncludeCustomData1 == other.IncludeCustomData1
                    && IncludeCustomData2 == other.IncludeCustomData2
                    && IncludeMeshIndex == other.IncludeMeshIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is VividParticleGpuDataLayoutDescriptor other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)RenderMode;
                    hash = (hash * 397) ^ (int)ColorDataMode;
                    hash = (hash * 397) ^ (int)RotationDataMode;
                    hash = (hash * 397) ^ (int)VelocityDataMode;
                    hash = (hash * 397) ^ (int)SizeDataMode;
                    hash = (hash * 397) ^ (IncludeUV ? 1 : 0);
                    hash = (hash * 397) ^ (IncludeCustomData1 ? 1 : 0);
                    hash = (hash * 397) ^ (IncludeCustomData2 ? 1 : 0);
                    hash = (hash * 397) ^ (IncludeMeshIndex ? 1 : 0);
                    return hash;
                }
            }

            public static VividParticleGpuDataLayoutDescriptor Create(VividParticleRenderMode renderMode)
            {
                return new VividParticleGpuDataLayoutDescriptor(
                    renderMode,
                    VividParticleGpuDataMode.Shared,
                    VividParticleGpuDataMode.Shared,
                    VividParticleGpuDataMode.Shared,
                    VividParticleGpuDataMode.Shared,
                    includeUV: false,
                    includeCustomData1: false,
                    includeCustomData2: false,
                    includeMeshIndex: false);
            }

            public static VividParticleGpuDataLayoutDescriptor Create(VividParticleRendererModule rendererModule)
            {
                if (rendererModule == null)
                    return Create(VividParticleRenderMode.Billboard);

                return new VividParticleGpuDataLayoutDescriptor(
                    rendererModule.renderMode,
                    rendererModule.colorDataMode,
                    rendererModule.rotationDataMode,
                    rendererModule.velocityDataMode,
                    rendererModule.sizeDataMode,
                    rendererModule.uvDataEnabled,
                    rendererModule.customData1Enabled,
                    rendererModule.customData2Enabled,
                    rendererModule.meshIndexDataEnabled);
            }
        }

        internal readonly struct VividParticleGpuBufferDataInfo
        {
            public VividParticleGpuBufferDataInfo(
                VividParticleGpuDataInfo dataInfo,
                int byteOffset,
                int elementCapacity)
            {
                DataInfo = dataInfo;
                ByteOffset = byteOffset;
                ElementCapacity = Mathf.Max(1, elementCapacity);
            }

            public VividParticleGpuDataInfo DataInfo { get; }

            public int ByteOffset { get; }

            public int ElementCapacity { get; }

            public int ByteSize => AlignTo16(ElementCapacity * DataInfo.ElementSize);

            public VividParticleGpuDataCopyDescriptor CopyDescriptor =>
                new(
                    DataInfo.DataId,
                    DataInfo.Frequency,
                    ByteOffset,
                    DataInfo.ElementSize,
                    DataInfo.UploadColumnMask,
                    DataInfo.DataBit);
        }

        internal readonly struct VividParticleGpuDataCopyDescriptor
        {
            public VividParticleGpuDataCopyDescriptor(
                VividParticleGpuDataId dataId,
                VividParticleGpuDataFrequency frequency,
                int byteOffset,
                int elementSize,
                int columnMask)
                : this(
                    dataId,
                    frequency,
                    byteOffset,
                    elementSize,
                    columnMask,
                    GetGpuDataBit(dataId))
            {
            }

            public VividParticleGpuDataCopyDescriptor(
                VividParticleGpuDataId dataId,
                VividParticleGpuDataFrequency frequency,
                int byteOffset,
                int elementSize,
                int columnMask,
                uint dataBit)
            {
                DataId = dataId;
                Frequency = frequency;
                ByteOffset = byteOffset;
                ElementSize = Mathf.Max(1, elementSize);
                ColumnMask = columnMask;
                DataBit = dataBit;
            }

            public VividParticleGpuDataId DataId { get; }

            public VividParticleGpuDataFrequency Frequency { get; }

            public int ByteOffset { get; }

            public int ElementSize { get; }

            public int ColumnMask { get; }

            public uint DataBit { get; }

            public bool IsShared => Frequency == VividParticleGpuDataFrequency.Shared;
        }

        internal readonly struct VividParticleGpuDataLayout
        {
            private static readonly Dictionary<VividParticleGpuDataLayoutDescriptor, VividParticleGpuDataLayout> s_LayoutCache = new();
            private readonly VividParticleGpuDataInfo[] m_DataInfos;

            private VividParticleGpuDataLayout(VividParticleGpuDataInfo[] dataInfos)
            {
                m_DataInfos = dataInfos;
                DataPerSharpBits = ComputeDataPerSharpBits(dataInfos);
                SharedDataBlockBits = ComputeDataBitsForRole(dataInfos, VividParticleGpuDataRole.SharedDataBlock);
                SpanSharedDataBlockBits = ComputeDataBitsForRole(dataInfos, VividParticleGpuDataRole.SpanSharedDataBlock);
                PerSharpValueBits = ComputeDataBitsForRole(dataInfos, VividParticleGpuDataRole.PerSharpValue);
                PerInstanceDataBits = ComputeDataBitsForRole(dataInfos, VividParticleGpuDataRole.PerInstanceValue);
                PerInstanceElementByteSize = ComputePerInstanceElementByteSize(dataInfos);
                PerInstanceUploadColumnMask = ComputePerInstanceUploadColumnMask(dataInfos);
                PerInstanceRenderJobFlagMask = ComputePerInstanceRenderJobFlagMask(dataInfos);
                TransformRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForRenderJobFlag(dataInfos, RenderJobTransformUploadFlag);
                ColorRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForRenderJobFlag(dataInfos, RenderJobColorUploadFlag);
                VelocityStretchRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForRenderJobFlag(dataInfos, RenderJobVelocityStretchUploadFlag);
                ExtraDataRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForRenderJobFlag(dataInfos, RenderJobExtraDataUploadFlag);
                UVRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForDataId(dataInfos, VividParticleGpuDataId.UV);
                CustomDataRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForDataIds(
                        dataInfos,
                        VividParticleGpuDataId.CustomData1,
                        VividParticleGpuDataId.CustomData2);
                MeshIndexRenderJobUploadColumnMask =
                    ComputePerInstanceUploadColumnMaskForDataId(dataInfos, VividParticleGpuDataId.MeshIndex);
                Hash = ComputeHash(dataInfos, DataPerSharpBits);
            }

            public int Count => m_DataInfos?.Length ?? 0;

            public uint DataPerSharpBits { get; }

            public uint SharedDataBlockBits { get; }

            public uint SpanSharedDataBlockBits { get; }

            public uint PerSharpValueBits { get; }

            public uint PerInstanceDataBits { get; }

            public int PerInstanceElementByteSize { get; }

            public int PerInstanceUploadColumnMask { get; }

            public uint PerInstanceRenderJobFlagMask { get; }

            public int TransformRenderJobUploadColumnMask { get; }

            public int ColorRenderJobUploadColumnMask { get; }

            public int VelocityStretchRenderJobUploadColumnMask { get; }

            public int ExtraDataRenderJobUploadColumnMask { get; }

            public int UVRenderJobUploadColumnMask { get; }

            public int CustomDataRenderJobUploadColumnMask { get; }

            public int MeshIndexRenderJobUploadColumnMask { get; }

            public int Hash { get; }

            public VividParticleGpuDataInfo this[int index] => m_DataInfos[index];

            public static VividParticleGpuDataLayout Create(VividParticleRenderMode renderMode)
            {
                return Create(VividParticleGpuDataLayoutDescriptor.Create(renderMode));
            }

            public static VividParticleGpuDataLayout Create(VividParticleRendererModule rendererModule)
            {
                return Create(VividParticleGpuDataLayoutDescriptor.Create(rendererModule));
            }

            public static VividParticleGpuDataLayout Create(VividParticleGpuDataLayoutDescriptor descriptor)
            {
                if (s_LayoutCache.TryGetValue(descriptor, out VividParticleGpuDataLayout layout))
                    return layout;

                layout = CreateUncached(descriptor);
                s_LayoutCache.Add(descriptor, layout);
                return layout;
            }

            private static VividParticleGpuDataLayout CreateUncached(VividParticleGpuDataLayoutDescriptor descriptor)
            {
                int dataInfoCount = 7
                    + (descriptor.IncludeUV ? 1 : 0)
                    + (descriptor.IncludeCustomData1 ? 1 : 0)
                    + (descriptor.IncludeCustomData2 ? 1 : 0)
                    + (descriptor.IncludeMeshIndex ? 1 : 0);
                var dataInfos = new VividParticleGpuDataInfo[dataInfoCount];
                int index = 0;
                dataInfos[index++] = CreateInfo(VividParticleGpuDataId.SharedData, VividParticleGpuDataFrequency.PerSharp);
                dataInfos[index++] = CreateInfo(VividParticleGpuDataId.SpanSharedData, VividParticleGpuDataFrequency.Span);
                dataInfos[index++] = CreateInfo(VividParticleGpuDataId.PositionSize, VividParticleGpuDataFrequency.PerInstance);
                dataInfos[index++] = CreateInfo(
                    VividParticleGpuDataId.BaseColor,
                    ResolveFrequency(descriptor.ColorDataMode, perSharpWhenShared: true));
                dataInfos[index++] = CreateInfo(
                    VividParticleGpuDataId.Scale,
                    ResolveFrequency(descriptor.SizeDataMode, perSharpWhenShared: true));
                dataInfos[index++] = CreateInfo(
                    VividParticleGpuDataId.Rotation,
                    UsesPerInstanceRotationData(descriptor.RotationDataMode)
                        ? VividParticleGpuDataFrequency.PerInstance
                        : VividParticleGpuDataFrequency.PerSharp);
                dataInfos[index++] = CreateInfo(
                    VividParticleGpuDataId.VelocityStretch,
                    UsesPerInstanceVelocityStretchData(descriptor.RenderMode, descriptor.VelocityDataMode)
                        ? VividParticleGpuDataFrequency.PerInstance
                        : VividParticleGpuDataFrequency.PerSharp);

                if (descriptor.IncludeUV)
                    dataInfos[index++] = CreateInfo(VividParticleGpuDataId.UV, VividParticleGpuDataFrequency.PerInstance);

                if (descriptor.IncludeCustomData1)
                    dataInfos[index++] = CreateInfo(VividParticleGpuDataId.CustomData1, VividParticleGpuDataFrequency.PerInstance);

                if (descriptor.IncludeCustomData2)
                    dataInfos[index++] = CreateInfo(VividParticleGpuDataId.CustomData2, VividParticleGpuDataFrequency.PerInstance);

                if (descriptor.IncludeMeshIndex)
                    dataInfos[index] = CreateInfo(VividParticleGpuDataId.MeshIndex, VividParticleGpuDataFrequency.PerInstance);

                return new VividParticleGpuDataLayout(dataInfos);
            }

            public int CalculateByteSize(int instanceCapacity, int sharpCapacity, int spanCapacity)
            {
                int cursor = ZeroBlockByteSize;
                for (int index = 0; index < Count; index++)
                {
                    VividParticleGpuDataInfo dataInfo = m_DataInfos[index];
                    int elementCapacity = GetElementCapacity(dataInfo.Frequency, instanceCapacity, sharpCapacity, spanCapacity);
                    cursor = AlignTo16(cursor);
                    cursor += AlignTo16(elementCapacity * dataInfo.ElementSize);
                }

                return cursor;
            }

            public VividParticleGpuBufferDataInfo[] CreateBufferInfos(
                int instanceCapacity,
                int sharpCapacity,
                int spanCapacity)
            {
                var bufferInfos = new VividParticleGpuBufferDataInfo[Count];
                int cursor = ZeroBlockByteSize;
                for (int index = 0; index < Count; index++)
                {
                    VividParticleGpuDataInfo dataInfo = m_DataInfos[index];
                    int elementCapacity = GetElementCapacity(dataInfo.Frequency, instanceCapacity, sharpCapacity, spanCapacity);
                    cursor = AlignTo16(cursor);
                    bufferInfos[index] = new VividParticleGpuBufferDataInfo(dataInfo, cursor, elementCapacity);
                    cursor += AlignTo16(elementCapacity * dataInfo.ElementSize);
                }

                return bufferInfos;
            }

            public bool TryGetDataInfo(
                VividParticleGpuDataId dataId,
                out VividParticleGpuDataInfo dataInfo)
            {
                for (int index = 0; index < Count; index++)
                {
                    dataInfo = m_DataInfos[index];
                    if (dataInfo.DataId == dataId)
                        return true;
                }

                dataInfo = default;
                return false;
            }

            private static VividParticleGpuDataInfo CreateInfo(
                VividParticleGpuDataId dataId,
                VividParticleGpuDataFrequency frequency)
            {
                return new VividParticleGpuDataInfo(
                    dataId,
                    frequency,
                    GetElementSize(dataId),
                    GetUploadSegment(dataId));
            }

            private static VividParticleGpuDataFrequency ResolveFrequency(
                VividParticleGpuDataMode dataMode,
                bool perSharpWhenShared)
            {
                return dataMode == VividParticleGpuDataMode.PerParticle
                    ? VividParticleGpuDataFrequency.PerInstance
                    : perSharpWhenShared
                        ? VividParticleGpuDataFrequency.PerSharp
                        : VividParticleGpuDataFrequency.Shared;
            }

            private static int GetElementSize(VividParticleGpuDataId dataId)
            {
                return dataId switch
                {
                    VividParticleGpuDataId.SharedData => SharedDataByteSize,
                    VividParticleGpuDataId.SpanSharedData => SpanSharedDataByteSize,
                    _ => SizeOfFloat4,
                };
            }

            private static InstanceUploadSegment GetUploadSegment(VividParticleGpuDataId dataId)
            {
                return dataId switch
                {
                    VividParticleGpuDataId.PositionSize => InstanceUploadSegment.PositionSize,
                    VividParticleGpuDataId.BaseColor => InstanceUploadSegment.BaseColor,
                    VividParticleGpuDataId.Rotation => InstanceUploadSegment.Rotation,
                    VividParticleGpuDataId.VelocityStretch => InstanceUploadSegment.VelocityStretch,
                    VividParticleGpuDataId.Scale => InstanceUploadSegment.Scale,
                    VividParticleGpuDataId.UV => InstanceUploadSegment.UV,
                    VividParticleGpuDataId.CustomData1 => InstanceUploadSegment.CustomData1,
                    VividParticleGpuDataId.CustomData2 => InstanceUploadSegment.CustomData2,
                    VividParticleGpuDataId.MeshIndex => InstanceUploadSegment.MeshIndex,
                    _ => InstanceUploadSegment.ZeroBlock,
                };
            }

            private static int GetElementCapacity(
                VividParticleGpuDataFrequency frequency,
                int instanceCapacity,
                int sharpCapacity,
                int spanCapacity)
            {
                return frequency switch
                {
                    VividParticleGpuDataFrequency.PerInstance => Mathf.Max(1, instanceCapacity),
                    VividParticleGpuDataFrequency.PerSharp => Mathf.Max(1, sharpCapacity),
                    VividParticleGpuDataFrequency.Span => Mathf.Max(1, spanCapacity),
                    _ => 1,
                };
            }

            private static uint ComputeDataPerSharpBits(VividParticleGpuDataInfo[] dataInfos)
            {
                uint bits = 0u;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    if (dataInfos[index].Frequency == VividParticleGpuDataFrequency.PerSharp)
                        bits |= dataInfos[index].DataBit;
                }

                return bits;
            }

            private static uint ComputeDataBitsForRole(
                VividParticleGpuDataInfo[] dataInfos,
                VividParticleGpuDataRole role)
            {
                uint bits = 0u;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    if (dataInfos[index].Role == role)
                        bits |= dataInfos[index].DataBit;
                }

                return bits;
            }

            private static int ComputePerInstanceElementByteSize(VividParticleGpuDataInfo[] dataInfos)
            {
                int byteSize = 0;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    if (dataInfos[index].Frequency == VividParticleGpuDataFrequency.PerInstance)
                        byteSize += dataInfos[index].ElementSize;
                }

                return byteSize;
            }

            private static int ComputePerInstanceUploadColumnMask(VividParticleGpuDataInfo[] dataInfos)
            {
                int columnMask = 0;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    VividParticleGpuDataInfo dataInfo = dataInfos[index];
                    if (dataInfo.Frequency == VividParticleGpuDataFrequency.PerInstance && dataInfo.HasUploadSegment)
                        columnMask |= dataInfo.UploadColumnMask;
                }

                return columnMask;
            }

            private static uint ComputePerInstanceRenderJobFlagMask(VividParticleGpuDataInfo[] dataInfos)
            {
                uint flags = 0u;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    VividParticleGpuDataInfo dataInfo = dataInfos[index];
                    if (dataInfo.Frequency == VividParticleGpuDataFrequency.PerInstance)
                        flags |= dataInfo.RenderJobFlagMask;
                }

                return flags;
            }

            private static int ComputePerInstanceUploadColumnMaskForRenderJobFlag(
                VividParticleGpuDataInfo[] dataInfos,
                uint renderJobFlag)
            {
                int columnMask = 0;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    VividParticleGpuDataInfo dataInfo = dataInfos[index];
                    if (dataInfo.IsPerInstance
                        && dataInfo.HasUploadSegment
                        && (dataInfo.RenderJobFlagMask & renderJobFlag) != 0u)
                    {
                        columnMask |= dataInfo.UploadColumnMask;
                    }
                }

                return columnMask;
            }

            private static int ComputePerInstanceUploadColumnMaskForDataId(
                VividParticleGpuDataInfo[] dataInfos,
                VividParticleGpuDataId dataId)
            {
                int columnMask = 0;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    VividParticleGpuDataInfo dataInfo = dataInfos[index];
                    if (dataInfo.DataId == dataId
                        && dataInfo.IsPerInstance
                        && dataInfo.HasUploadSegment)
                    {
                        columnMask |= dataInfo.UploadColumnMask;
                    }
                }

                return columnMask;
            }

            private static int ComputePerInstanceUploadColumnMaskForDataIds(
                VividParticleGpuDataInfo[] dataInfos,
                VividParticleGpuDataId firstDataId,
                VividParticleGpuDataId secondDataId)
            {
                int columnMask = 0;
                for (int index = 0; index < dataInfos.Length; index++)
                {
                    VividParticleGpuDataInfo dataInfo = dataInfos[index];
                    if ((dataInfo.DataId == firstDataId || dataInfo.DataId == secondDataId)
                        && dataInfo.IsPerInstance
                        && dataInfo.HasUploadSegment)
                    {
                        columnMask |= dataInfo.UploadColumnMask;
                    }
                }

                return columnMask;
            }

            private static int ComputeHash(VividParticleGpuDataInfo[] dataInfos, uint dataPerSharpBits)
            {
                unchecked
                {
                    int hash = dataInfos.Length;
                    for (int index = 0; index < dataInfos.Length; index++)
                        hash = (hash * 397) ^ dataInfos[index].GetHashCode();

                    hash = (hash * 397) ^ (int)dataPerSharpBits;
                    return hash;
                }
            }
        }

        private readonly struct InstanceUploadOperation
        {
            public InstanceUploadOperation(InstanceUploadSegment segment, int startIndex, int count)
            {
                Segment = segment;
                StartIndex = startIndex;
                Count = count;
            }

            public InstanceUploadSegment Segment { get; }

            public int StartIndex { get; }

            public int Count { get; }

            public int EndIndex => StartIndex + Count;
        }

        private sealed class InstanceUploadDirtyRanges
        {
            private bool m_ZeroBlockDirty;
            private bool m_InstanceRangeDirty;
            private int m_PerInstanceElementByteSize;
            private int m_InstanceColumnMask;
            private int m_InstanceStart;
            private int m_InstanceEnd;

            public int Count
            {
                get
                {
                    int count = m_ZeroBlockDirty ? 1 : 0;
                    count += m_InstanceRangeDirty ? 1 : 0;
                    return count;
                }
            }

            public bool HasPendingData => m_ZeroBlockDirty
                || m_InstanceRangeDirty;

            public InstanceUploadOperation this[int index]
            {
                get
                {
                    if (m_ZeroBlockDirty)
                    {
                        if (index == 0)
                            return new InstanceUploadOperation(InstanceUploadSegment.ZeroBlock, 0, 1);

                        index--;
                    }

                    if (m_InstanceRangeDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.PositionSize, m_InstanceStart, m_InstanceEnd);
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public void AddZeroBlock()
            {
                m_ZeroBlockDirty = true;
            }

            public void AddInstanceRange(
                int startIndex,
                int count,
                VividParticleGpuDataLayout layout)
            {
                AddInstanceRange(startIndex, count, layout, layout.PerInstanceUploadColumnMask);
            }

            public void AddInstanceRange(
                int startIndex,
                int count,
                VividParticleGpuDataLayout layout,
                int columnMask)
            {
                if (count <= 0)
                    return;

                startIndex = Mathf.Max(0, startIndex);
                columnMask &= layout.PerInstanceUploadColumnMask;
                if (layout.PerInstanceElementByteSize <= 0 || columnMask == 0)
                    return;

                m_PerInstanceElementByteSize = Mathf.Max(
                    m_PerInstanceElementByteSize,
                    layout.PerInstanceElementByteSize);
                m_InstanceColumnMask |= columnMask;
                AddRange(ref m_InstanceRangeDirty, ref m_InstanceStart, ref m_InstanceEnd, startIndex, count);
            }

            public int EstimateUploadByteCount(int activeCount)
            {
                int byteCount = m_ZeroBlockDirty ? ZeroBlockByteSize : 0;
                if (TryGetInstanceRange(activeCount, out _, out int count))
                    byteCount += count * EstimatePerInstanceElementByteSize();

                return byteCount;
            }

            public bool TryGetInstanceRange(int activeCount, out int startIndex, out int count)
            {
                startIndex = 0;
                count = 0;

                if (!m_InstanceRangeDirty)
                    return false;

                startIndex = Mathf.Clamp(m_InstanceStart, 0, Mathf.Max(0, activeCount));
                int clampedEnd = Mathf.Clamp(m_InstanceEnd, startIndex, Mathf.Max(0, activeCount));
                count = clampedEnd - startIndex;
                return count > 0;
            }

            public bool TryGetInstanceRange(
                int activeCount,
                out int startIndex,
                out int count,
                out int columnMask)
            {
                bool hasRange = TryGetInstanceRange(activeCount, out startIndex, out count);
                columnMask = hasRange ? m_InstanceColumnMask : 0;
                return hasRange && columnMask != 0;
            }

            public void Compact()
            {
            }

            public void Clear()
            {
                m_ZeroBlockDirty = false;
                m_InstanceRangeDirty = false;
                m_InstanceStart = 0;
                m_InstanceEnd = 0;
                m_PerInstanceElementByteSize = 0;
                m_InstanceColumnMask = 0;
            }

            private int EstimatePerInstanceElementByteSize()
            {
                int columnCount = 0;
                int mask = m_InstanceColumnMask;
                while (mask != 0)
                {
                    columnCount += mask & 1;
                    mask >>= 1;
                }

                return Mathf.Max(1, columnCount) * SizeOfFloat4;
            }

            private static void AddRange(
                ref bool dirty,
                ref int start,
                ref int end,
                int startIndex,
                int count)
            {
                int endIndex = startIndex + count;
                if (!dirty)
                {
                    dirty = true;
                    start = startIndex;
                    end = endIndex;
                    return;
                }

                start = Mathf.Min(start, startIndex);
                end = Mathf.Max(end, endIndex);
            }

            private static InstanceUploadOperation CreateOperation(
                InstanceUploadSegment segment,
                int start,
                int end)
            {
                return new InstanceUploadOperation(segment, start, end - start);
            }
        }

        private readonly struct ParticleRenderEntry
        {
            public readonly ParticleSystemState State;
            public readonly int EcsLineId;
            public readonly Material Material;
            public readonly Mesh Mesh;
            public readonly Mesh[] Meshes;
            public readonly int MeshCount;
            public readonly VividParticleRenderMode RenderMode;
            public readonly VividParticleGpuDataLayout GpuLayout;
            public readonly VividParticleRendererSharedKey RendererSharedKey;
            public readonly int Layer;
            public readonly int Capacity;
            public readonly int ActiveCount;
            public readonly Matrix4x4 LocalToWorldMatrix;
            public readonly Color RendererColor;
            public readonly float SizeScale;
            public readonly float StretchLengthScale;
            public readonly float StretchSpeedScale;
            public readonly Vector3 Pivot;
            public readonly float MinParticleSize;
            public readonly float MaxParticleSize;
            public readonly Vector3 Flip;
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly uint RenderingLayerMask;
            public readonly bool ReceiveShadows;
            public readonly EntityId PickingEntityId;
            public readonly bool IsEditorSelected;
            public readonly VividParticleSortMode SortMode;

            public ParticleRenderEntry(
                ParticleSystemState state,
                int ecsLineId,
                Material material,
                Mesh mesh,
                Mesh[] meshes,
                int meshCount,
                VividParticleRenderMode renderMode,
                VividParticleGpuDataLayout gpuLayout,
                VividParticleRendererSharedKey rendererSharedKey,
                int layer,
                int capacity,
                int activeCount,
                Matrix4x4 localToWorldMatrix,
                Color rendererColor,
                float sizeScale,
                float stretchLengthScale,
                float stretchSpeedScale,
                Vector3 pivot,
                float minParticleSize,
                float maxParticleSize,
                Vector3 flip,
                ShadowCastingMode shadowCastingMode,
                uint renderingLayerMask,
                bool receiveShadows,
                EntityId pickingEntityId,
                bool isEditorSelected,
                VividParticleSortMode sortMode)
            {
                State = state;
                EcsLineId = ecsLineId;
                Material = material;
                Mesh = mesh;
                Meshes = meshes ?? Array.Empty<Mesh>();
                MeshCount = Mathf.Clamp(meshCount, 0, Meshes.Length);
                RenderMode = renderMode;
                GpuLayout = gpuLayout;
                RendererSharedKey = rendererSharedKey;
                Layer = layer;
                Capacity = Mathf.Max(1, capacity);
                ActiveCount = Mathf.Clamp(activeCount, 0, Capacity);
                LocalToWorldMatrix = localToWorldMatrix;
                RendererColor = rendererColor;
                SizeScale = sizeScale;
                StretchLengthScale = stretchLengthScale;
                StretchSpeedScale = stretchSpeedScale;
                Pivot = pivot;
                MinParticleSize = Mathf.Max(0.0f, minParticleSize);
                MaxParticleSize = Mathf.Max(0.0f, maxParticleSize);
                Flip = new Vector3(Mathf.Clamp01(flip.x), Mathf.Clamp01(flip.y), Mathf.Clamp01(flip.z));
                ShadowCastingMode = shadowCastingMode;
                RenderingLayerMask = renderingLayerMask;
                ReceiveShadows = receiveShadows;
                PickingEntityId = pickingEntityId;
                IsEditorSelected = isEditorSelected;
                SortMode = sortMode;
            }
        }

        private readonly struct ParticleDrawKey : IEquatable<ParticleDrawKey>
        {
            public readonly int MaterialId;
            public readonly int MeshId;
            public readonly int RenderMode;
            public readonly int Layer;
            public readonly int GpuDataLayoutHash;
            public readonly uint DataPerSharpBits;
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly VividParticleSortMode SortMode;
            public readonly uint RenderingLayerMask;
            public readonly bool ReceiveShadows;

            public ParticleDrawKey(ParticleRenderEntry entry)
            {
                RendererSharedKey = entry.RendererSharedKey;
                MaterialId = RendererSharedKey.MaterialId;
                MeshId = RendererSharedKey.MeshId;
                RenderMode = RendererSharedKey.RenderMode;
                Layer = RendererSharedKey.Layer;
                GpuDataLayoutHash = RendererSharedKey.GpuDataLayoutHash;
                DataPerSharpBits = RendererSharedKey.DataPerSharpBits;
                ShadowCastingMode = (ShadowCastingMode)RendererSharedKey.ShadowCastingMode;
                SortMode = (VividParticleSortMode)RendererSharedKey.SortMode;
                RenderingLayerMask = RendererSharedKey.RenderingLayerMask;
                ReceiveShadows = RendererSharedKey.ReceiveShadows;
            }

            public readonly VividParticleRendererSharedKey RendererSharedKey;

            public bool Equals(ParticleDrawKey other)
            {
                return MaterialId == other.MaterialId
                    && MeshId == other.MeshId
                    && RenderMode == other.RenderMode
                    && Layer == other.Layer
                    && GpuDataLayoutHash == other.GpuDataLayoutHash
                    && DataPerSharpBits == other.DataPerSharpBits
                    && ShadowCastingMode == other.ShadowCastingMode
                    && SortMode == other.SortMode
                    && RenderingLayerMask == other.RenderingLayerMask
                    && ReceiveShadows == other.ReceiveShadows;
            }

            public override bool Equals(object obj)
            {
                return obj is ParticleDrawKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = MaterialId;
                    hash = (hash * 397) ^ MeshId;
                    hash = (hash * 397) ^ RenderMode;
                    hash = (hash * 397) ^ Layer;
                    hash = (hash * 397) ^ GpuDataLayoutHash;
                    hash = (hash * 397) ^ (int)DataPerSharpBits;
                    hash = (hash * 397) ^ (int)ShadowCastingMode;
                    hash = (hash * 397) ^ (int)SortMode;
                    hash = (hash * 397) ^ (int)RenderingLayerMask;
                    hash = (hash * 397) ^ ReceiveShadows.GetHashCode();
                    return hash;
                }
            }

        }

        private sealed class ParticleRenderRecord
        {
            public ParticleSystemState State;
            public int EcsLineId = -1;
            public Material Material;
            public Mesh Mesh;
            public Mesh[] Meshes = Array.Empty<Mesh>();
            public int MeshCount;
            public VividParticleRenderMode RenderMode;
            public VividParticleGpuDataLayout GpuLayout;
            public VividParticleRendererSharedKey RendererSharedKey;
            public int Layer;
            public int Capacity;
            public int ActiveCount;
            public ParticleDrawKey Key;
            public ParticleDrawBatch Batch;
            public ParticleRendererLineGroup LineGroup;
            public int BatchBaseIndex;
            public int LineGroupIndex = -1;
            public int SharpIndex;
            public int SpanBaseIndex;
            public int SpanCapacity;
            public Matrix4x4 LocalToWorldMatrix;
            public Color RendererColor;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
            public Vector3 Pivot;
            public float MinParticleSize;
            public float MaxParticleSize;
            public Vector3 Flip;
            public ShadowCastingMode ShadowCastingMode;
            public uint RenderingLayerMask;
            public bool ReceiveShadows;
            public EntityId PickingEntityId;
            public bool IsEditorSelected;
            public VividParticleSortMode SortMode;
            public bool RequiresSortingPositions;
            public int LastUploadOperationCount;
            public int LastUploadByteCount;
            public int RecordSlot = -1;
            public int RecordVersion;
            public int CullingRecordStart;
            public int CullingRecordCount;
            public int MeshVisibleCountOffset = -1;
            public int MeshVisibleCountCount;
            public int[] VisibleInstanceCapacities = Array.Empty<int>();
            public bool UploadDirtyQueued;

            public void Update(ParticleRenderEntry entry)
            {
                State = entry.State;
                EcsLineId = entry.EcsLineId;
                Material = entry.Material;
                Mesh = entry.Mesh;
                Meshes = entry.Meshes ?? Array.Empty<Mesh>();
                MeshCount = Mathf.Clamp(entry.MeshCount, 0, Meshes.Length);
                RenderMode = entry.RenderMode;
                GpuLayout = entry.GpuLayout;
                RendererSharedKey = entry.RendererSharedKey;
                Layer = Mathf.Clamp(entry.Layer, 0, 31);
                Capacity = Mathf.Max(1, entry.Capacity);
                ActiveCount = Mathf.Clamp(entry.ActiveCount, 0, Capacity);
                LocalToWorldMatrix = entry.LocalToWorldMatrix;
                RendererColor = entry.RendererColor;
                SizeScale = entry.SizeScale;
                StretchLengthScale = entry.StretchLengthScale;
                StretchSpeedScale = entry.StretchSpeedScale;
                Pivot = entry.Pivot;
                MinParticleSize = entry.MinParticleSize;
                MaxParticleSize = entry.MaxParticleSize;
                Flip = entry.Flip;
                ShadowCastingMode = entry.ShadowCastingMode;
                RenderingLayerMask = entry.RenderingLayerMask;
                ReceiveShadows = entry.ReceiveShadows;
                PickingEntityId = entry.PickingEntityId;
                IsEditorSelected = entry.IsEditorSelected;
                SortMode = entry.SortMode;
                RequiresSortingPositions = VividParticleSystemManager.RequiresSortingPositions(SortMode);
                Key = new ParticleDrawKey(entry);
            }
        }

        private sealed class ParticleRendererLineGroup
        {
            public readonly List<ParticleRenderRecord> Records = new();
            public VividParticleRendererSharedKey SharedKey;
            public ParticleDrawKey DrawKey;
            public int GroupIndex = -1;
        }

        private sealed class ParticleDrawBatch
        {
            public readonly List<ParticleRenderRecord> Records = new();
            public ParticleDrawKey Key;
            public Material Material;
            public Mesh Mesh;
            public Mesh[] Meshes = Array.Empty<Mesh>();
            public BatchID BatchId;
            public BatchMeshID MeshId;
            public BatchMeshID[] MeshIds = Array.Empty<BatchMeshID>();
            public BatchMaterialID MaterialId;
            public ShadowCastingMode ShadowCastingMode;
            public bool ReceiveShadows;
            public bool UsesPageBillboard;
            public bool RequiresSortingPositions;
            public VividParticleGpuDataLayout GpuLayout;
            public VividParticleGpuBufferDataInfo[] GpuBufferInfos = Array.Empty<VividParticleGpuBufferDataInfo>();
            public VividParticleGpuDataCopyDescriptor[] RecordCopyDescriptors = Array.Empty<VividParticleGpuDataCopyDescriptor>();
            public VividParticleGpuBufferDataInfo[] SharedValueBufferInfos = Array.Empty<VividParticleGpuBufferDataInfo>();
            public VividParticleGpuBufferDataInfo[] PerSharpValueBufferInfos = Array.Empty<VividParticleGpuBufferDataInfo>();
            public VividParticleGpuBufferDataInfo SharedDataBufferInfo;
            public VividParticleGpuBufferDataInfo SpanSharedDataBufferInfo;
            public ParticleRenderUploadColumnLayout UploadColumnLayout;
            public bool HasSharedDataBufferInfo;
            public bool HasSpanSharedDataBufferInfo;
            public int CullingRecordStart = -1;
            public int CullingRecordCount;
            public int[] VisibleInstanceCapacities = Array.Empty<int>();
            public int BatchIndex = -1;
            public int Capacity;
            public int SharpCapacity;
            public int SpanCapacity;
            public int DataOffset;
            public bool ZeroBlockDirty;
            public bool SharedValuesDirty;
            public bool UploadDirtyQueued;
        }

        private struct ParticleUploadRecordRef
        {
            public int RecordSlot;
            public int RecordVersion;
        }

        private struct ParticleUploadRecordWork
        {
            public int RecordSlot;
            public int RecordVersion;
            public int StartIndex;
            public int Count;
            public int ColumnMask;
            public int HasInstanceRange;
            public int HasSharedData;
            public int HasSpanData;
            public uint SharedDataBits;
        }

        private struct ParticleUploadBatchWork
        {
            public int BatchIndex;
        }

        private struct ParticleGpuDataCopyWork : IComparable<ParticleGpuDataCopyWork>
        {
            public int OwnerRecordSlot;
            public int OwnerRecordVersion;
            public int ByteOffset;
            public int ByteCount;
            public uint DataBit;

            public int CompareTo(ParticleGpuDataCopyWork other)
            {
                return CompareUploadCopyOperationsForMerge(
                    ByteOffset,
                    ByteCount,
                    other.ByteOffset,
                    other.ByteCount);
            }
        }

        private unsafe struct ParticleRenderUploadSource
        {
            [NativeDisableUnsafePtrRestriction]
            public float3* Positions;
            [NativeDisableUnsafePtrRestriction]
            public float3* Velocities;
            [NativeDisableUnsafePtrRestriction]
            public float* StartLifetimes;
            [NativeDisableUnsafePtrRestriction]
            public float* RemainingLifetimes;
            [NativeDisableUnsafePtrRestriction]
            public float4* Colors;
            [NativeDisableUnsafePtrRestriction]
            public float* Sizes;
            [NativeDisableUnsafePtrRestriction]
            public int* MeshIndices;
            [NativeDisableUnsafePtrRestriction]
            public byte* BufferBase;

            public int StartIndex;
            public int Count;
            public int ActiveCount;
            public int BatchBaseIndex;
            public int BatchCapacity;
            public int BatchDataOffset;
            public float4x4 LocalToWorld;
            public int SimulationSpace;
            public int RenderMode;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
            public int MeshCount;
            public float4 RendererColor;
        }

        private struct ParticleRenderUploadPageWork
        {
            public ParticleRenderUploadSource Source;
            public int ColumnMask;
            public int PositionSizeByteOffset;
            public int BaseColorByteOffset;
            public int RotationByteOffset;
            public int VelocityStretchByteOffset;
            public int ScaleByteOffset;
            public int UVByteOffset;
            public int CustomData1ByteOffset;
            public int CustomData2ByteOffset;
            public int MeshIndexByteOffset;
        }

        private struct ParticleRenderUploadColumnLayout
        {
            public int ColumnMask;
            public ParticleRenderJobFlags RenderJobFlags;
            public int TransformUploadColumnMask;
            public int ColorUploadColumnMask;
            public int VelocityStretchUploadColumnMask;
            public int ExtraDataUploadColumnMask;
            public int UVUploadColumnMask;
            public int CustomDataUploadColumnMask;
            public int MeshIndexUploadColumnMask;
            public int PositionSizeByteOffset;
            public int BaseColorByteOffset;
            public int RotationByteOffset;
            public int VelocityStretchByteOffset;
            public int ScaleByteOffset;
            public int UVByteOffset;
            public int CustomData1ByteOffset;
            public int CustomData2ByteOffset;
            public int MeshIndexByteOffset;

            public bool HasColumns => ColumnMask != 0;

            public ParticleRenderJobFlags GetRenderJobFlagsForColumnMask(int columnMask)
            {
                ParticleRenderJobFlags flags = ParticleRenderJobFlags.None;
                if ((columnMask & TransformUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.TransformUpload;

                if ((columnMask & ColorUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.ColorUpload;

                if ((columnMask & VelocityStretchUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.VelocityStretchUpload;

                if ((columnMask & UVUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.UVUpload;

                if ((columnMask & CustomDataUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.CustomDataUpload;

                if ((columnMask & MeshIndexUploadColumnMask) != 0)
                    flags |= ParticleRenderJobFlags.MeshIndexUpload;

                return flags;
            }
        }

        private unsafe struct ParticleRenderSharedDataWork
        {
            [NativeDisableUnsafePtrRestriction]
            public byte* BufferBase;

            public int Kind;
            public int BatchDataOffset;
            public int ColumnByteOffset;
            public int ElementStart;
            public int ElementCount;
            public int SharpIndex;
            public int SpanBaseIndex;
            public int BatchBaseIndex;
            public int ActiveCount;
            public int Capacity;
            public int UsesPageBillboard;
            public int RenderMode;
            public int RendererPriority;
            public int ShadowCastingMode;
            public int ReceiveShadows;
            public int SortMode;
            public int Layer;
            public int IsEditorSelected;
            public uint RenderingLayerMask;
            public uint PickingEntityIdLow;
            public uint PickingEntityIdHigh;
            public uint DataPerSharpBits;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
            public float3 Pivot;
            public float MinParticleSize;
            public float MaxParticleSize;
            public float3 Flip;
            public float4 Value;
            public float4x4 LocalToWorld;
            public float4 RendererColor;
        }

        private struct ParticleCullingRecord
        {
            public float3 BoundsCenter;
            public float3 BoundsExtents;
            public int BatchBaseIndex;
            public int SpanBaseIndex;
            public int ActiveCount;
            public int UsesPageBillboard;
            public int IsEditorSelected;
            public int ParticleStart;
            [NativeDisableUnsafePtrRestriction]
            public unsafe int* MeshIndices;
            [NativeDisableUnsafePtrRestriction]
            public unsafe float3* Positions;
            public int PositionCapacity;
            public float4x4 LocalToWorld;
            public int SimulationSpace;
        }

        private struct ParticleBoundsData
        {
            public float3 Center;
            public float3 Extents;
            public int IsValid;
        }

        private unsafe struct ParticleBoundsSource
        {
            [NativeDisableUnsafePtrRestriction]
            public float3* Positions;
            [NativeDisableUnsafePtrRestriction]
            public float3* Velocities;
            [NativeDisableUnsafePtrRestriction]
            public float* Sizes;

            public int ActiveCount;
            public float4x4 LocalToWorld;
            public int SimulationSpace;
            public int RenderMode;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
            public float MeshExtent;
        }

        private struct ParticleBoundsPageWork
        {
            public ParticleBoundsSource Source;
            public int ParticleStart;
            public int ParticleCount;
        }

        private struct ParticleBoundsRecordReduceWork
        {
            public int PageStart;
            public int PageCount;
            public int ActiveCount;
            public int UsesPageBillboard;
        }

        private struct ParticleBoundsRecordResult
        {
            public ParticleBoundsData WorldBounds;
            public int PageStart;
            public int PageCount;
            public int ActiveCount;
            public int UsesPageBillboard;
        }

        private struct ParticleCullingSplit
        {
            public int PacketOffset;
            public int PacketCount;
        }

        private struct ParticleCullingPlanePacket4
        {
            public float4 NormalX;
            public float4 NormalY;
            public float4 NormalZ;
            public float4 Distance;
        }

        private unsafe struct ParticleMeshVisibleCountWork
        {
            [NativeDisableUnsafePtrRestriction]
            public int* MeshIndices;
            public int ActiveCount;
            public int MeshCount;
            public int OutputOffset;
        }

        private struct ParticleDrawCommandInput
        {
            public int RecordStart;
            public int RecordCount;
            public int VisibleOffset;
            public int MaxVisibleCount;
            public int SortingPositionOffset;
            public int Layer;
            public int SubmeshIndex;
            public int ActiveMeshLod;
            public int RendererPriority;
            public uint RenderingLayerMask;
            public ulong SceneCullingMask;
            public BatchDrawCommandFlags DrawFlags;
            public int HasSortingPositions;
            public ShadowCastingMode ShadowCastingMode;
            public MotionVectorGenerationMode MotionMode;
            public int ReceiveShadows;
            public int StaticShadowCaster;
            public int AllDepthSorted;
            public BatchID BatchId;
            public BatchMeshID MeshId;
            public BatchMaterialID MaterialId;
            public EntityId PickingEntityId;
            public uint PickingEntityIdLow;
            public uint PickingEntityIdHigh;
            public int MeshIndexFilter;
            public int MeshCount;
        }

        private struct ParticlePickingIncludeExcludeFilter
        {
            public NativeArray<ulong> IncludeRenderers;
            public NativeArray<ulong> IncludeEntities;
            public NativeArray<ulong> ExcludeRenderers;
            public NativeArray<ulong> ExcludeEntities;
            public int IncludeEnabled;
            public int FilterEnabled;
        }

        private struct ParticleDrawRangeInput
        {
            public int DrawCommandsBegin;
            public int DrawCommandsCount;
            public int RendererPriority;
            public uint RenderingLayerMask;
            public int Layer;
            public ShadowCastingMode ShadowCastingMode;
            public MotionVectorGenerationMode MotionMode;
            public int ReceiveShadows;
            public int StaticShadowCaster;
            public int AllDepthSorted;
        }

        private static void CountFilteredDrawLayout(
            ParticleDrawCommandInput[] commands,
            uint cullingLayerMask,
            BatchCullingViewType viewType,
            out int drawCommandCount,
            out int drawRangeCount,
            out int visibleInstanceCount,
            out int sortingPositionCount)
        {
            drawCommandCount = 0;
            drawRangeCount = 0;
            visibleInstanceCount = 0;
            sortingPositionCount = 0;
            if (commands == null || commands.Length == 0)
                return;

            bool hasLastRange = false;
            ParticleDrawRangeInput lastRange = default;
            for (int index = 0; index < commands.Length; index++)
            {
                AccumulateFilteredDrawCommand(
                    commands[index],
                    cullingLayerMask,
                    viewType,
                    ref drawCommandCount,
                    ref drawRangeCount,
                    ref visibleInstanceCount,
                    ref sortingPositionCount,
                    ref hasLastRange,
                    ref lastRange);
            }
        }

        private static void CountFilteredDrawLayout(
            NativeArray<ParticleDrawCommandInput> commands,
            uint cullingLayerMask,
            BatchCullingViewType viewType,
            out int drawCommandCount,
            out int drawRangeCount,
            out int visibleInstanceCount,
            out int sortingPositionCount)
        {
            drawCommandCount = 0;
            drawRangeCount = 0;
            visibleInstanceCount = 0;
            sortingPositionCount = 0;
            if (!commands.IsCreated || commands.Length == 0)
                return;

            bool hasLastRange = false;
            ParticleDrawRangeInput lastRange = default;
            for (int index = 0; index < commands.Length; index++)
            {
                AccumulateFilteredDrawCommand(
                    commands[index],
                    cullingLayerMask,
                    viewType,
                    ref drawCommandCount,
                    ref drawRangeCount,
                    ref visibleInstanceCount,
                    ref sortingPositionCount,
                    ref hasLastRange,
                    ref lastRange);
            }
        }

        private static void AccumulateFilteredDrawCommand(
            ParticleDrawCommandInput command,
            uint cullingLayerMask,
            BatchCullingViewType viewType,
            ref int drawCommandCount,
            ref int drawRangeCount,
            ref int visibleInstanceCount,
            ref int sortingPositionCount,
            ref bool hasLastRange,
            ref ParticleDrawRangeInput lastRange)
        {
            if (!ShouldKeepDrawCommandForCulling(
                    cullingLayerMask,
                    command.Layer,
                    command.RecordCount,
                    command.MaxVisibleCount,
                    command.ShadowCastingMode,
                    viewType))
            {
                return;
            }

            ParticleDrawRangeInput nextRange = CreateDrawRangeInput(command, drawCommandCount, drawCommandCount: 1);
            if (hasLastRange && CanMergeDrawRanges(lastRange, nextRange, drawCommandCount))
            {
                lastRange.DrawCommandsCount++;
            }
            else
            {
                drawRangeCount++;
                lastRange = nextRange;
                hasLastRange = true;
            }

            drawCommandCount++;
            visibleInstanceCount += command.MaxVisibleCount;
            if (command.HasSortingPositions != 0)
                sortingPositionCount += command.MaxVisibleCount;
        }

        private static ParticleDrawRangeInput CreateDrawRangeInput(
            ParticleDrawCommandInput command,
            int drawCommandsBegin,
            int drawCommandCount)
        {
            return new ParticleDrawRangeInput
            {
                DrawCommandsBegin = drawCommandsBegin,
                DrawCommandsCount = drawCommandCount,
                RendererPriority = command.RendererPriority,
                RenderingLayerMask = command.RenderingLayerMask,
                Layer = command.Layer,
                ShadowCastingMode = command.ShadowCastingMode,
                MotionMode = command.MotionMode,
                ReceiveShadows = command.ReceiveShadows,
                StaticShadowCaster = command.StaticShadowCaster,
                AllDepthSorted = command.AllDepthSorted,
            };
        }

        private static bool CanMergeDrawRanges(
            ParticleDrawRangeInput left,
            ParticleDrawRangeInput right,
            int rightCommandIndex)
        {
            return left.DrawCommandsBegin + left.DrawCommandsCount == rightCommandIndex
                && left.RendererPriority == right.RendererPriority
                && left.RenderingLayerMask == right.RenderingLayerMask
                && left.Layer == right.Layer
                && left.ShadowCastingMode == right.ShadowCastingMode
                && left.MotionMode == right.MotionMode
                && left.ReceiveShadows == right.ReceiveShadows
                && left.StaticShadowCaster == right.StaticShadowCaster
                && left.AllDepthSorted == right.AllDepthSorted;
        }

        private readonly struct ParticleMaterialVariantKey : IEquatable<ParticleMaterialVariantKey>
        {
            public readonly int RenderMode;
            public readonly int RenderQueueOffset;

            public ParticleMaterialVariantKey(VividParticleRenderMode renderMode, int renderQueueOffset)
            {
                RenderMode = (int)renderMode;
                RenderQueueOffset = renderQueueOffset;
            }

            public bool Equals(ParticleMaterialVariantKey other)
            {
                return RenderMode == other.RenderMode && RenderQueueOffset == other.RenderQueueOffset;
            }

            public override bool Equals(object obj)
            {
                return obj is ParticleMaterialVariantKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RenderMode * 397) ^ RenderQueueOffset;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UploadOperation
        {
            public uint SrcOffset;
            public uint DstOffset;
            public uint Size;
        }

        private unsafe sealed class VividParticleRendererManager : IDisposable
        {
            private const int MaxThreadGroupsPerDispatch = 65535;
            private const int SharedDataWorkKindFloat4 = 0;
            private const int SharedDataWorkKindPerSharp = 1;
            private const int SharedDataWorkKindSpan = 2;

            private static readonly ProfilerMarker s_UpdateRecordsMarker = new("VividRP.Particle.Manager.BRGUpload.UpdateRecords");
            private static readonly ProfilerMarker s_RemoveRecordsMarker = new("VividRP.Particle.Manager.BRGUpload.RemoveRecords");
            private static readonly ProfilerMarker s_CommitMarker = new("VividRP.Particle.Manager.Commit");
            private static readonly ProfilerMarker s_BoundsCollectMarker = new("VividRP.Particle.Manager.Bounds.Collect");
            private static readonly ProfilerMarker s_BoundsScheduleMarker = new("VividRP.Particle.Manager.Bounds.Schedule");
            private static readonly ProfilerMarker s_BoundsCompleteMarker = new("VividRP.Particle.Manager.Bounds.Complete");
            private static readonly ProfilerMarker s_RebuildCullingLayoutMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout");
            private static readonly ProfilerMarker s_CullingLayoutCollectMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout.Collect");
            private static readonly ProfilerMarker s_CullingLayoutMeshVisibleMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout.MeshVisible");
            private static readonly ProfilerMarker s_CullingLayoutCacheMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout.Cache");
            private static readonly ProfilerMarker s_CullingLayoutDrawCommandsMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout.DrawCommands");
            private static readonly ProfilerMarker s_ManagerJobGraphScheduleMarker = new("VividRP.Particle.Manager.JobGraph.Schedule");
            private static readonly ProfilerMarker s_UploadCollectDirtyMarker = new("VividRP.Particle.Manager.JobGraph.Upload.CollectDirty");
            private static readonly ProfilerMarker s_UploadLockBufferMarker = new("VividRP.Particle.Manager.JobGraph.Upload.LockBuffer");
            private static readonly ProfilerMarker s_UploadBuildWorksMarker = new("VividRP.Particle.Manager.JobGraph.Upload.BuildWorks");
            private static readonly ProfilerMarker s_UploadCopyWorkArraysMarker = new("VividRP.Particle.Manager.JobGraph.Upload.CopyWorkArrays");
            private static readonly ProfilerMarker s_UploadScheduleJobsMarker = new("VividRP.Particle.Manager.JobGraph.Upload.ScheduleJobs");
            private static readonly ProfilerMarker s_RebuildBatchesMarker = new("VividRP.Particle.Renderer.RebuildBatches");
            private static readonly ProfilerMarker s_UploadMarker = new("VividRP.Particle.Renderer.Upload");
            private static readonly ProfilerMarker s_CompleteUploadMarker = new("VividRP.Particle.Renderer.CompleteUpload");
            private static readonly int s_CopySrcBufferId = Shader.PropertyToID("_VividParticleUploadSrc");
            private static readonly int s_CopyDstBufferId = Shader.PropertyToID("_VividParticleUploadDst");
            private static readonly int s_CopyOperationsId = Shader.PropertyToID("_VividParticleUploadOperations");
            private static readonly int s_CopyOperationCountId = Shader.PropertyToID("_VividParticleUploadOperationCount");
            private static readonly int s_CopyOperationBaseId = Shader.PropertyToID("_VividParticleUploadOperationBase");
            private static readonly int s_SharedDataId = Shader.PropertyToID("_VividParticleSharedData");
            private static readonly int s_SpanSharedDataId = Shader.PropertyToID("_VividParticleSpanSharedData");
            private static readonly int s_PositionSizeId = Shader.PropertyToID("_VividParticlePositionSize");
            private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
            private static readonly int s_RotationId = Shader.PropertyToID("_VividParticleRotation");
            private static readonly int s_VelocityStretchId = Shader.PropertyToID("_VividParticleVelocityStretch");
            private static readonly int s_ScaleId = Shader.PropertyToID("_VividParticleScale");
            private static readonly int s_UVId = Shader.PropertyToID("_VividParticleUV");
            private static readonly int s_CustomData1Id = Shader.PropertyToID("_VividParticleCustomData1");
            private static readonly int s_CustomData2Id = Shader.PropertyToID("_VividParticleCustomData2");
            private static readonly int s_MeshIndexId = Shader.PropertyToID("_VividParticleMeshIndex");

            private readonly Dictionary<ParticleSystemState, ParticleRenderRecord> m_Records = new();
            private readonly Dictionary<int, ParticleRenderRecord> m_RecordByEcsLineId = new();
            private readonly Dictionary<VividParticleRendererSharedKey, ParticleRendererLineGroup> m_LineGroupLookup = new();
            private readonly List<ParticleRendererLineGroup> m_LineGroups = new();
            private readonly Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> m_EcsRendererLineGroupScratch = new();
            private readonly List<ParticleDrawBatch> m_DrawBatches = new();
            private readonly List<VividParticleGpuDataCopyDescriptor> m_RecordCopyDescriptorScratch = new();
            private readonly List<VividParticleGpuBufferDataInfo> m_SharedValueBufferInfoScratch = new();
            private readonly List<VividParticleGpuBufferDataInfo> m_PerSharpValueBufferInfoScratch = new();
            private readonly List<ParticleRenderRecord> m_RecordSlots = new();
            private readonly List<int> m_RecordSlotVersions = new();
            private readonly Stack<int> m_FreeRecordSlots = new();
            private readonly List<ParticleSystemState> m_RemoveRecordStates = new();
            private readonly HashSet<ParticleSystemState> m_QueuedRemoveStates = new();
            private readonly List<ParticleSystemState> m_BoundsStates = new();
            private readonly Dictionary<ParticleMaterialVariantKey, Material> m_DefaultMaterials = new();
            private readonly VividParticleGPUBuffer m_GPUBuffer = new();
#if UNITY_EDITOR
            private Material m_PickingMaterial;
#endif
            private NativeList<ParticleUploadRecordRef> m_DirtyUploadRecords;
            private NativeList<int> m_DirtyUploadBatchIndices;
            private NativeList<ParticleUploadRecordWork> m_UploadRecordWorks;
            private NativeList<ParticleUploadBatchWork> m_UploadBatchWorks;
            private NativeList<ParticleRenderUploadPageWork> m_TransformUploadPageWorks;
            private NativeList<ParticleRenderUploadPageWork> m_ColorUploadPageWorks;
            private NativeList<ParticleRenderUploadPageWork> m_VelocityStretchUploadPageWorks;
            private NativeList<ParticleRenderUploadPageWork> m_UVUploadPageWorks;
            private NativeList<ParticleRenderUploadPageWork> m_CustomDataUploadPageWorks;
            private NativeList<ParticleRenderUploadPageWork> m_MeshIndexUploadPageWorks;
            private NativeList<ParticleRenderSharedDataWork> m_SharedDataWorks;
            private NativeList<ParticleGpuDataCopyWork> m_UploadCopyWorks;
            private NativeList<ParticleCullingRecord> m_NativeCullingRecords;
            private NativeList<ParticleDrawCommandInput> m_NativeDrawCommandInputs;
            private NativeList<ParticleDrawRangeInput> m_NativeDrawRangeInputs;
            private NativeList<ParticleDrawCommandInput> m_NativeLightDrawCommandInputs;
            private NativeList<ParticleDrawRangeInput> m_NativeLightDrawRangeInputs;
            private NativeList<ParticleDrawCommandInput> m_NativePickingDrawCommandInputs;
            private NativeList<ParticleDrawRangeInput> m_NativePickingDrawRangeInputs;
            private NativeList<ParticleDrawCommandInput> m_NativeSelectionDrawCommandInputs;
            private NativeList<ParticleDrawRangeInput> m_NativeSelectionDrawRangeInputs;
            private NativeList<ParticleMeshVisibleCountWork> m_MeshVisibleCountWorks;
            private NativeArray<int> m_MeshVisibleCounts;
            private NativeList<ParticleBoundsPageWork> m_BoundsPageWorks;
            private NativeList<ParticleBoundsRecordReduceWork> m_BoundsRecordWorks;
            private NativeArray<ParticleBoundsData> m_BoundsPageResults;
            private NativeArray<ParticleBoundsRecordResult> m_BoundsRecordResults;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingTransformUploadPageWorks;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingColorUploadPageWorks;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingVelocityStretchUploadPageWorks;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingUVUploadPageWorks;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingCustomDataUploadPageWorks;
            private NativeArray<ParticleRenderUploadPageWork> m_PendingMeshIndexUploadPageWorks;
            private NativeArray<ParticleRenderSharedDataWork> m_PendingSharedDataWorks;
            private JobHandle m_PendingCullingOutputHandle;
            private JobHandle m_PendingUploadHandle;
            private JobHandle m_PendingBoundsHandle;
            private BatchRendererGroup m_BRG;
            private bool m_HasPendingCullingOutput;
            private bool m_HasPendingUpload;
            private bool m_HasPendingBounds;
            private bool m_LayoutDirty = true;
            private bool m_ForceFullUpload;
            private bool m_AnyShadowCastingBatch;
            private bool m_AnySelectedRecord;
            private int m_NativeVisibleInstanceCapacity;
            private int m_NativeSortingPositionCapacity;
            private int m_NativeLightVisibleInstanceCapacity;
            private int m_NativePickingVisibleInstanceCapacity;
            private int m_NativeSelectionVisibleInstanceCapacity;
            private uint m_NativeDrawCommandLayerMask;
            private uint m_NativeLightDrawCommandLayerMask;
            private uint m_NativePickingDrawCommandLayerMask;
            private uint m_NativeSelectionDrawCommandLayerMask;
            private int m_MeshVisibleCountLength;
            private int m_TotalBufferByteSize;
            private int m_LastBoundsPageWorkCount;
            private int m_LastBoundsRecordWorkCount;
            private int m_LastCullingSingleMeshCacheRecordCount;
            private int m_LastCullingMultiMeshCacheRecordCount;
            private int m_LastCullingMeshFallbackRecordCount;
            private int m_LastCullingRecordVisibleCacheEntryCount;
            private int m_LastCullingBatchVisibleCacheEntryCount;
            private int m_LastEcsRendererLineGroupCount;
            private int m_LastEcsRendererLineCount;
            private int m_LastEcsRendererMatchedLineCount;
            private int m_LastEcsRendererSkippedLineCount;
            private int m_LastDirtyUploadQueueCount;
            private int m_LastInvalidDirtyUploadQueueCount;
            private int m_LastDirtyUploadBatchQueueCount;
            private int m_LastInvalidDirtyUploadBatchQueueCount;
            private int m_LastUploadRecordWorkCount;
            private int m_LastUploadBatchWorkCount;
            private int m_LastUploadPageWorkCount;
            private int m_LastMergedUploadCopyWorkCount;
            private int m_LastUploadColumnMask;
            private uint m_LastUploadDataBits;
            private uint m_LastRenderJobModuleFlags;
            private int m_LastRenderPageJobModuleCount;
            private uint m_PendingRenderJobFlags;

            public bool hasPendingUpload => m_HasPendingUpload;

            public bool hasPickingMaterial
            {
                get
                {
#if UNITY_EDITOR
                    return m_PickingMaterial != null;
#else
                    return false;
#endif
                }
            }

            public int pendingRemoveCount => m_QueuedRemoveStates.Count;

            private int GetExtraDataUploadPageWorkCount()
            {
                return (m_UVUploadPageWorks.IsCreated ? m_UVUploadPageWorks.Length : 0)
                    + (m_CustomDataUploadPageWorks.IsCreated ? m_CustomDataUploadPageWorks.Length : 0)
                    + (m_MeshIndexUploadPageWorks.IsCreated ? m_MeshIndexUploadPageWorks.Length : 0);
            }

            public VividParticleRendererManagerStats stats => new(
                m_Records.Count,
                m_LineGroups.Count,
                m_LastEcsRendererLineGroupCount,
                m_LastEcsRendererLineCount,
                m_LastEcsRendererMatchedLineCount,
                m_LastEcsRendererSkippedLineCount,
                m_DrawBatches.Count,
                m_GPUBuffer.lastLockCount,
                m_GPUBuffer.lastCopyOperationCount,
                m_GPUBuffer.lastCopyByteCount,
                m_GPUBuffer.usesComputeDelta,
                m_LastDirtyUploadQueueCount,
                m_LastInvalidDirtyUploadQueueCount,
                m_LastDirtyUploadBatchQueueCount,
                m_LastInvalidDirtyUploadBatchQueueCount,
                m_LastUploadRecordWorkCount,
                m_LastUploadBatchWorkCount,
                m_LastUploadPageWorkCount,
                m_TransformUploadPageWorks.IsCreated ? m_TransformUploadPageWorks.Length : 0,
                m_ColorUploadPageWorks.IsCreated ? m_ColorUploadPageWorks.Length : 0,
                m_VelocityStretchUploadPageWorks.IsCreated ? m_VelocityStretchUploadPageWorks.Length : 0,
                m_UVUploadPageWorks.IsCreated ? m_UVUploadPageWorks.Length : 0,
                m_CustomDataUploadPageWorks.IsCreated ? m_CustomDataUploadPageWorks.Length : 0,
                m_MeshIndexUploadPageWorks.IsCreated ? m_MeshIndexUploadPageWorks.Length : 0,
                GetExtraDataUploadPageWorkCount(),
                m_SharedDataWorks.IsCreated ? m_SharedDataWorks.Length : 0,
                m_UploadCopyWorks.IsCreated ? m_UploadCopyWorks.Length : 0,
                m_LastMergedUploadCopyWorkCount,
                m_LastUploadColumnMask,
                m_LastUploadDataBits,
                m_LastRenderJobModuleFlags,
                m_LastRenderPageJobModuleCount,
                m_NativeCullingRecords.IsCreated ? m_NativeCullingRecords.Length : 0,
                m_NativeDrawCommandInputs.IsCreated ? m_NativeDrawCommandInputs.Length : 0,
                m_NativeDrawRangeInputs.IsCreated ? m_NativeDrawRangeInputs.Length : 0,
                m_NativeVisibleInstanceCapacity,
                m_NativeSortingPositionCapacity,
                m_NativeLightDrawCommandInputs.IsCreated ? m_NativeLightDrawCommandInputs.Length : 0,
                m_NativeLightDrawRangeInputs.IsCreated ? m_NativeLightDrawRangeInputs.Length : 0,
                m_NativeLightVisibleInstanceCapacity,
                m_NativePickingDrawCommandInputs.IsCreated ? m_NativePickingDrawCommandInputs.Length : 0,
                m_NativePickingDrawRangeInputs.IsCreated ? m_NativePickingDrawRangeInputs.Length : 0,
                m_NativePickingVisibleInstanceCapacity,
                m_NativeSelectionDrawCommandInputs.IsCreated ? m_NativeSelectionDrawCommandInputs.Length : 0,
                m_NativeSelectionDrawRangeInputs.IsCreated ? m_NativeSelectionDrawRangeInputs.Length : 0,
                m_NativeSelectionVisibleInstanceCapacity,
                m_LastBoundsPageWorkCount,
                m_LastBoundsRecordWorkCount,
                m_LastCullingSingleMeshCacheRecordCount,
                m_LastCullingMultiMeshCacheRecordCount,
                m_LastCullingMeshFallbackRecordCount,
                m_LastCullingRecordVisibleCacheEntryCount,
                m_LastCullingBatchVisibleCacheEntryCount,
                m_MeshVisibleCountWorks.IsCreated ? m_MeshVisibleCountWorks.Length : 0,
                m_MeshVisibleCountLength);

            public Material GetOrCreateDefaultMaterial(VividParticleRenderMode renderMode, int renderQueueOffset)
            {
                ParticleMaterialVariantKey key = new(renderMode, renderQueueOffset);
                if (m_DefaultMaterials.TryGetValue(key, out Material material) && material != null)
                    return material;

                Shader shader = Shader.Find(DefaultShaderName);
                if (shader == null)
                    return null;

                material = new Material(shader)
                {
                    name = $"Vivid Particle System Default Material ({renderMode})",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                ParticleSystemState.ConfigureDefaultParticleMaterial(material);
                ParticleSystemState.ApplyRenderQueueOffset(material, null, renderQueueOffset);
                ParticleSystemState.ConfigureParticleRenderMode(material, renderMode);
                m_DefaultMaterials[key] = material;
                return material;
            }

            public void UpdateAll(
                IReadOnlyList<ParticleSystemState> states,
                bool forceUpload)
            {
                using (s_UpdateRecordsMarker.Auto())
                {
                    int stateCount = states?.Count ?? 0;
                    for (int index = 0; index < stateCount; index++)
                    {
                        ParticleSystemState state = states[index];
                        if (state == null)
                            continue;

                        UpdateRecord(state, forceUpload);
                    }
                }

                ProcessQueuedRecordRemovals();
            }

            public void Update(ParticleSystemState state, bool forceUpload)
            {
                if (state == null)
                    return;

                using (s_UpdateRecordsMarker.Auto())
                {
                    UpdateRecord(state, forceUpload);
                }

                Commit();
            }

#if UNITY_EDITOR
            public bool SyncEditorSelectionState(ParticleSystemState state)
            {
                if (state == null || !m_Records.TryGetValue(state, out ParticleRenderRecord record))
                    return false;

                bool isEditorSelected = state.isEditorSelected;
                if (record.IsEditorSelected == isEditorSelected)
                    return false;

                record.IsEditorSelected = isEditorSelected;
                state.MarkEditorSelectionSharedDataDirty();
                return true;
            }

            public bool RebuildCullingLayoutForEditorSelection()
            {
                if (m_LayoutDirty)
                    return false;

                CompletePendingBoundsUpdates();
                RebuildNativeCullingLayout();
                return true;
            }
#endif

            public void SchedulePostSimulationBoundsUpdates(IReadOnlyList<ParticleSystemState> states)
            {
                if (states == null || states.Count == 0)
                    return;

                ScheduleBoundsUpdates(states);
            }

            public void Unregister(ParticleSystemState state)
            {
                CompletePendingUpload();
                RemoveRecord(state);
                Commit();
            }

            public void Dispose()
            {
                CompletePendingUpload();
                DrainCullingResults();
                m_BRG?.Dispose();
                m_BRG = null;
                m_GPUBuffer.Dispose();
                m_Records.Clear();
                m_RecordByEcsLineId.Clear();
                m_LineGroupLookup.Clear();
                m_LineGroups.Clear();
                m_EcsRendererLineGroupScratch.Clear();
                m_DrawBatches.Clear();
                m_RecordSlots.Clear();
                m_RecordSlotVersions.Clear();
                m_FreeRecordSlots.Clear();
                m_RemoveRecordStates.Clear();
                m_QueuedRemoveStates.Clear();
                DisposeNativeUploadQueues();
                m_BoundsStates.Clear();
                DisposeNativeBoundsLayout();
                DisposeNativeCullingLayout();
                foreach (Material material in m_DefaultMaterials.Values)
                    CoreUtils.Destroy(material);

#if UNITY_EDITOR
                CoreUtils.Destroy(m_PickingMaterial);
                m_PickingMaterial = null;
#endif
                m_DefaultMaterials.Clear();
                m_LayoutDirty = true;
                m_ForceFullUpload = false;
                m_AnyShadowCastingBatch = false;
                m_AnySelectedRecord = false;
                m_NativeVisibleInstanceCapacity = 0;
                m_NativeSortingPositionCapacity = 0;
                m_NativeLightVisibleInstanceCapacity = 0;
                m_NativePickingVisibleInstanceCapacity = 0;
                m_NativeSelectionVisibleInstanceCapacity = 0;
                m_NativeDrawCommandLayerMask = 0u;
                m_NativeLightDrawCommandLayerMask = 0u;
                m_NativePickingDrawCommandLayerMask = 0u;
                m_NativeSelectionDrawCommandLayerMask = 0u;
                m_TotalBufferByteSize = 0;
                m_LastEcsRendererLineGroupCount = 0;
                m_LastEcsRendererLineCount = 0;
                m_LastEcsRendererMatchedLineCount = 0;
                m_LastEcsRendererSkippedLineCount = 0;
                m_LastDirtyUploadQueueCount = 0;
                m_LastInvalidDirtyUploadQueueCount = 0;
                m_LastDirtyUploadBatchQueueCount = 0;
                m_LastInvalidDirtyUploadBatchQueueCount = 0;
                m_LastUploadRecordWorkCount = 0;
                m_LastUploadBatchWorkCount = 0;
                m_LastUploadPageWorkCount = 0;
                m_LastMergedUploadCopyWorkCount = 0;
                m_LastRenderJobModuleFlags = 0u;
                m_LastRenderPageJobModuleCount = 0;
            }

            private void UpdateRecord(ParticleSystemState state, bool forceUpload)
            {
                if (!state.PrepareRenderEntry(forceUpload, out ParticleRenderEntry entry))
                {
                    RemoveRecord(state);
                    return;
                }

                ParticleDrawKey key = new(entry);
                if (!m_Records.TryGetValue(state, out ParticleRenderRecord record))
                {
                    record = new ParticleRenderRecord();
                    record.Update(entry);
                    AllocateRecordSlot(record);
                    m_Records.Add(state, record);
                    RegisterRecordEcsLine(record, oldLineId: -1);
                    QueueUploadDirty(record);
                    m_LayoutDirty = true;
                    return;
                }

                int oldLineId = record.EcsLineId;
                bool layoutChanged = !record.Key.Equals(key)
                    || record.Capacity != entry.Capacity
                    || oldLineId != entry.EcsLineId;
                record.Update(entry);
                RegisterRecordEcsLine(record, oldLineId);
                if (layoutChanged)
                    m_LayoutDirty = true;

                if (forceUpload || state.HasPendingUploadData())
                    QueueUploadDirty(record);
            }

            public void QueueUploadDirty(ParticleSystemState state)
            {
                if (state == null || !m_Records.TryGetValue(state, out ParticleRenderRecord record))
                    return;

                QueueUploadDirty(record);
            }

            public bool MarkFirstBatchZeroBlockDirtyForTests()
            {
                if (m_DrawBatches.Count == 0)
                    return false;

                ParticleDrawBatch batch = m_DrawBatches[0];
                if (batch == null)
                    return false;

                batch.ZeroBlockDirty = true;
                QueueUploadDirty(batch);
                return true;
            }

            public void QueueRemove(ParticleSystemState state)
            {
                if (state == null || !m_QueuedRemoveStates.Add(state))
                    return;

                m_RemoveRecordStates.Add(state);
            }

            public void CancelQueuedRemove(ParticleSystemState state)
            {
                if (state == null)
                    return;

                m_QueuedRemoveStates.Remove(state);
            }

            private void ProcessQueuedRecordRemovals()
            {
                if (m_RemoveRecordStates.Count == 0)
                    return;

                using (s_RemoveRecordsMarker.Auto())
                {
                    for (int index = 0; index < m_RemoveRecordStates.Count; index++)
                    {
                        ParticleSystemState state = m_RemoveRecordStates[index];
                        if (state == null || !m_QueuedRemoveStates.Remove(state))
                            continue;

                        RemoveRecord(state);
                    }

                    m_RemoveRecordStates.Clear();
                    m_QueuedRemoveStates.Clear();
                }
            }

            private void QueueUploadDirty(ParticleRenderRecord record)
            {
                if (record == null || record.RecordSlot < 0 || record.UploadDirtyQueued)
                    return;

                EnsureNativeUploadQueues();
                m_DirtyUploadRecords.Add(new ParticleUploadRecordRef
                {
                    RecordSlot = record.RecordSlot,
                    RecordVersion = record.RecordVersion,
                });
                record.UploadDirtyQueued = true;
            }

            private void QueueUploadDirty(ParticleDrawBatch batch)
            {
                if (batch == null || batch.BatchIndex < 0 || batch.UploadDirtyQueued)
                    return;

                EnsureNativeUploadQueues();
                m_DirtyUploadBatchIndices.Add(batch.BatchIndex);
                batch.UploadDirtyQueued = true;
            }

            private void AllocateRecordSlot(ParticleRenderRecord record)
            {
                int slot;
                if (m_FreeRecordSlots.Count > 0)
                {
                    slot = m_FreeRecordSlots.Pop();
                    m_RecordSlots[slot] = record;
                }
                else
                {
                    slot = m_RecordSlots.Count;
                    m_RecordSlots.Add(record);
                    m_RecordSlotVersions.Add(0);
                }

                record.RecordSlot = slot;
                record.RecordVersion = m_RecordSlotVersions[slot];
                record.UploadDirtyQueued = false;
            }

            private void ReleaseRecordSlot(ParticleRenderRecord record)
            {
                if (record == null)
                    return;

                int slot = record.RecordSlot;
                if ((uint)slot < (uint)m_RecordSlots.Count && m_RecordSlots[slot] == record)
                {
                    m_RecordSlots[slot] = null;
                    m_RecordSlotVersions[slot]++;
                    m_FreeRecordSlots.Push(slot);
                }

                record.RecordSlot = -1;
                record.RecordVersion = 0;
                record.UploadDirtyQueued = false;
            }

            private bool TryGetRecord(int recordSlot, int recordVersion, out ParticleRenderRecord record)
            {
                record = null;
                if ((uint)recordSlot >= (uint)m_RecordSlots.Count)
                    return false;

                if (m_RecordSlotVersions[recordSlot] != recordVersion)
                    return false;

                record = m_RecordSlots[recordSlot];
                return record != null;
            }

            private void RemoveRecord(ParticleSystemState state)
            {
                if (state == null || !m_Records.TryGetValue(state, out ParticleRenderRecord record))
                    return;

                CancelQueuedRemove(state);
                record.State.SetRendererUploadStats(false, 0, 0, 0, m_GPUBuffer.bufferIndex);
                record.State.ResetRendererCullingStats();
                m_Records.Remove(state);
                UnregisterRecordEcsLine(record);
                RemoveRecordFromLineGroup(record);
                ReleaseRecordSlot(record);
                m_LayoutDirty = true;
            }

            private void RegisterRecordEcsLine(ParticleRenderRecord record, int oldLineId)
            {
                if (record == null)
                    return;

                if (oldLineId >= 0
                    && oldLineId != record.EcsLineId
                    && m_RecordByEcsLineId.TryGetValue(oldLineId, out ParticleRenderRecord oldRecord)
                    && oldRecord == record)
                {
                    m_RecordByEcsLineId.Remove(oldLineId);
                }

                if (record.EcsLineId >= 0)
                    m_RecordByEcsLineId[record.EcsLineId] = record;
            }

            private void UnregisterRecordEcsLine(ParticleRenderRecord record)
            {
                if (record == null || record.EcsLineId < 0)
                    return;

                if (m_RecordByEcsLineId.TryGetValue(record.EcsLineId, out ParticleRenderRecord mappedRecord)
                    && mappedRecord == record)
                {
                    m_RecordByEcsLineId.Remove(record.EcsLineId);
                }
            }

            private void RebuildRendererLineGroupsFromEcsQuery()
            {
                ClearRendererLineGroups();
                m_LastEcsRendererLineGroupCount = 0;
                m_LastEcsRendererLineCount = 0;
                m_LastEcsRendererMatchedLineCount = 0;
                m_LastEcsRendererSkippedLineCount = 0;

                VividParticleEcsBootstrap.RegisterTypes();
                VividEcsTypeIndex commonTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
                VividEcsTypeIndex rendererSharedKeyTypeIndex =
                    VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
                if (!commonTypeIndex.IsValid || !rendererSharedKeyTypeIndex.IsValid)
                    return;

                VividEcsQuery query = s_ParticleEcsWorld.CreateQuery().WithAll(commonTypeIndex);
                m_LastEcsRendererLineGroupCount = s_ParticleEcsWorld.CreateArchetypeLineGroupMap(
                    query,
                    m_EcsRendererLineGroupScratch,
                    rendererSharedKeyTypeIndex);

                foreach (KeyValuePair<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> pair in m_EcsRendererLineGroupScratch)
                {
                    List<VividEcsArchetypeLine> lines = pair.Value;
                    if (lines == null || lines.Count == 0)
                        continue;

                    m_LastEcsRendererLineCount += lines.Count;
                    ParticleRendererLineGroup rendererGroup = null;
                    for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                    {
                        VividEcsArchetypeLine line = lines[lineIndex];
                        if (line == null
                            || !m_RecordByEcsLineId.TryGetValue(line.ArchetypeLineId, out ParticleRenderRecord record)
                            || record == null)
                        {
                            m_LastEcsRendererSkippedLineCount++;
                            continue;
                        }

                        m_LastEcsRendererMatchedLineCount++;
                        if (rendererGroup == null)
                        {
                            VividParticleRendererSharedKey sharedKey =
                                line.TryGetSharedComponent(out VividParticleRendererSharedKey lineSharedKey)
                                    ? lineSharedKey
                                    : record.RendererSharedKey;
                            rendererGroup = GetOrCreateRendererLineGroup(sharedKey, record.Key);
                        }

                        AddRecordToRendererLineGroup(rendererGroup, record);
                    }
                }
            }

            private ParticleRendererLineGroup GetOrCreateRendererLineGroup(
                VividParticleRendererSharedKey sharedKey,
                ParticleDrawKey drawKey)
            {
                if (m_LineGroupLookup.TryGetValue(sharedKey, out ParticleRendererLineGroup group))
                {
                    group.DrawKey = drawKey;
                    return group;
                }

                group = new ParticleRendererLineGroup
                {
                    SharedKey = sharedKey,
                    DrawKey = drawKey,
                    GroupIndex = m_LineGroups.Count,
                };
                m_LineGroupLookup.Add(sharedKey, group);
                m_LineGroups.Add(group);
                return group;
            }

            private static void AddRecordToRendererLineGroup(
                ParticleRendererLineGroup group,
                ParticleRenderRecord record)
            {
                record.LineGroup = group;
                record.LineGroupIndex = group.Records.Count;
                group.DrawKey = record.Key;
                group.Records.Add(record);
            }

            private void ClearRendererLineGroups()
            {
                for (int groupIndex = 0; groupIndex < m_LineGroups.Count; groupIndex++)
                {
                    ParticleRendererLineGroup group = m_LineGroups[groupIndex];
                    if (group == null)
                        continue;

                    for (int recordIndex = 0; recordIndex < group.Records.Count; recordIndex++)
                    {
                        ParticleRenderRecord record = group.Records[recordIndex];
                        if (record == null)
                            continue;

                        record.LineGroup = null;
                        record.LineGroupIndex = -1;
                    }

                    group.Records.Clear();
                    group.GroupIndex = -1;
                }

                m_LineGroupLookup.Clear();
                m_LineGroups.Clear();
            }

            private void RemoveRecordFromLineGroup(ParticleRenderRecord record)
            {
                if (record == null)
                    return;

                ParticleRendererLineGroup group = record.LineGroup;
                if (group == null)
                    return;

                int index = record.LineGroupIndex;
                int lastIndex = group.Records.Count - 1;
                if ((uint)index < (uint)group.Records.Count && group.Records[index] == record)
                {
                    ParticleRenderRecord lastRecord = group.Records[lastIndex];
                    group.Records[index] = lastRecord;
                    group.Records.RemoveAt(lastIndex);
                    if (lastRecord != record)
                        lastRecord.LineGroupIndex = index;
                }
                else
                {
                    int removedIndex = group.Records.IndexOf(record);
                    if (removedIndex >= 0)
                    {
                        group.Records.RemoveAt(removedIndex);
                        for (int recordIndex = removedIndex; recordIndex < group.Records.Count; recordIndex++)
                            group.Records[recordIndex].LineGroupIndex = recordIndex;
                    }
                }

                record.LineGroup = null;
                record.LineGroupIndex = -1;

                if (group.Records.Count == 0)
                    RemoveLineGroup(group);
            }

            private void RemoveLineGroup(ParticleRendererLineGroup group)
            {
                if (group == null)
                    return;

                m_LineGroupLookup.Remove(group.SharedKey);
                int index = group.GroupIndex;
                int lastIndex = m_LineGroups.Count - 1;
                if ((uint)index < (uint)m_LineGroups.Count && m_LineGroups[index] == group)
                {
                    ParticleRendererLineGroup lastGroup = m_LineGroups[lastIndex];
                    m_LineGroups[index] = lastGroup;
                    m_LineGroups.RemoveAt(lastIndex);
                    if (lastGroup != group)
                        lastGroup.GroupIndex = index;
                }
                else
                {
                    int removedIndex = m_LineGroups.IndexOf(group);
                    if (removedIndex >= 0)
                    {
                        m_LineGroups.RemoveAt(removedIndex);
                        for (int groupIndex = removedIndex; groupIndex < m_LineGroups.Count; groupIndex++)
                            m_LineGroups[groupIndex].GroupIndex = groupIndex;
                    }
                }

                group.GroupIndex = -1;
                group.Records.Clear();
            }

            public void Commit()
            {
                using (s_CommitMarker.Auto())
                {
                    if (m_LayoutDirty)
                        RebuildBatches();

                    ScheduleManagerJobGraph();
                    CompletePendingBoundsUpdates();
                    RebuildNativeCullingLayout();
                }
            }

            private void ScheduleManagerJobGraph()
            {
                using (s_ManagerJobGraphScheduleMarker.Auto())
                {
                    ScheduleRenderUploadGraph();
                    if (!m_HasPendingBounds)
                        ScheduleBoundsUpdatesFromRecords();
                }
            }

            private void ScheduleBoundsUpdates(IReadOnlyList<ParticleSystemState> states)
            {
                CompletePendingBoundsUpdates();
                EnsureNativeBoundsLayout();
                m_BoundsStates.Clear();
                m_BoundsPageWorks.Clear();
                m_BoundsRecordWorks.Clear();

                using (s_BoundsCollectMarker.Auto())
                {
                    int stateCount = states?.Count ?? 0;
                    for (int index = 0; index < stateCount; index++)
                        CollectBoundsState(states[index]);
                }

                ScheduleCollectedBounds();
            }

            private void ScheduleBoundsUpdatesFromRecords()
            {
                CompletePendingBoundsUpdates();
                EnsureNativeBoundsLayout();
                m_BoundsStates.Clear();
                m_BoundsPageWorks.Clear();
                m_BoundsRecordWorks.Clear();

                using (s_BoundsCollectMarker.Auto())
                {
                    for (int groupIndex = 0; groupIndex < m_LineGroups.Count; groupIndex++)
                    {
                        ParticleRendererLineGroup group = m_LineGroups[groupIndex];
                        if (group == null)
                            continue;

                        for (int recordIndex = 0; recordIndex < group.Records.Count; recordIndex++)
                            CollectBoundsState(group.Records[recordIndex]?.State);
                    }
                }

                ScheduleCollectedBounds();
            }

            private void CollectBoundsState(ParticleSystemState state)
            {
                if (state == null)
                    return;

                int count = state.activeCount;
                if (!state.NeedsBoundsUpdate(count))
                    return;

                if (count <= 0
                    || !state.TryCreateCurrentBoundsSource(
                        out ParticleBoundsSource source,
                        out VividParticleRenderMode renderMode,
                        out count))
                {
                    state.SetEmptyCachedRenderBounds(count);
                    return;
                }

                int pageStart = m_BoundsPageWorks.Length;
                int pageCount = Mathf.Max(1, GetCullingRecordCount(count));
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int particleStart = pageIndex * BillboardPageSize;
                    m_BoundsPageWorks.Add(new ParticleBoundsPageWork
                    {
                        Source = source,
                        ParticleStart = particleStart,
                        ParticleCount = Mathf.Clamp(count - particleStart, 0, BillboardPageSize),
                    });
                }

                m_BoundsRecordWorks.Add(new ParticleBoundsRecordReduceWork
                {
                    PageStart = pageStart,
                    PageCount = pageCount,
                    ActiveCount = count,
                    UsesPageBillboard = UsesPageBillboardRenderMode(renderMode) ? 1 : 0,
                });
                m_BoundsStates.Add(state);
            }

            private void ScheduleCollectedBounds()
            {
                if (m_BoundsPageWorks.Length <= 0 || m_BoundsRecordWorks.Length <= 0)
                {
                    m_LastBoundsPageWorkCount = 0;
                    m_LastBoundsRecordWorkCount = 0;
                    return;
                }

                EnsureBoundsResultCapacity(m_BoundsPageWorks.Length, m_BoundsRecordWorks.Length);
                m_LastBoundsPageWorkCount = m_BoundsPageWorks.Length;
                m_LastBoundsRecordWorkCount = m_BoundsRecordWorks.Length;

                using (s_BoundsScheduleMarker.Auto())
                {
                    var pageJob = new ParticleBoundsBatchPageJob
                    {
                        Works = m_BoundsPageWorks.AsArray(),
                        PageBounds = m_BoundsPageResults,
                    };
                    JobHandle pageHandle = pageJob.Schedule(m_BoundsPageWorks.Length, innerloopBatchCount: 1);

                    var reduceJob = new ParticleBoundsBatchReduceJob
                    {
                        Works = m_BoundsRecordWorks.AsArray(),
                        PageBounds = m_BoundsPageResults,
                        RecordResults = m_BoundsRecordResults,
                    };

                    m_PendingBoundsHandle = reduceJob.Schedule(m_BoundsRecordWorks.Length, innerloopBatchCount: 1, pageHandle);
                    m_HasPendingBounds = true;
                    JobHandle.ScheduleBatchedJobs();
                }
            }

            public void CompletePendingBoundsUpdates()
            {
                if (!m_HasPendingBounds)
                    return;

                using (s_BoundsCompleteMarker.Auto())
                {
                    m_PendingBoundsHandle.Complete();
                    m_PendingBoundsHandle = default;
                    m_HasPendingBounds = false;

                    int resultCount = Mathf.Min(m_BoundsStates.Count, m_BoundsRecordResults.IsCreated ? m_BoundsRecordResults.Length : 0);
                    for (int recordIndex = 0; recordIndex < resultCount; recordIndex++)
                    {
                        ParticleSystemState state = m_BoundsStates[recordIndex];
                        if (state == null)
                            continue;

                        ParticleBoundsRecordResult result = m_BoundsRecordResults[recordIndex];
                        state.ApplyCachedBounds(
                            result.WorldBounds,
                            m_BoundsPageResults,
                            result.PageStart,
                            result.PageCount,
                            result.ActiveCount,
                            result.UsesPageBillboard != 0);
                    }

                    m_BoundsStates.Clear();
                    m_BoundsPageWorks.Clear();
                    m_BoundsRecordWorks.Clear();
                }
            }

            private unsafe void RebuildNativeCullingLayout()
            {
                using (s_RebuildCullingLayoutMarker.Auto())
                {
                    DrainCullingResults();
                    EnsureNativeCullingLayout();
                    m_NativeCullingRecords.Clear();
                    m_NativeDrawCommandInputs.Clear();
                    m_NativeDrawRangeInputs.Clear();
                    m_NativeLightDrawCommandInputs.Clear();
                    m_NativeLightDrawRangeInputs.Clear();
                    m_NativePickingDrawCommandInputs.Clear();
                    m_NativePickingDrawRangeInputs.Clear();
                    m_NativeSelectionDrawCommandInputs.Clear();
                    m_NativeSelectionDrawRangeInputs.Clear();
                    m_MeshVisibleCountWorks.Clear();
                    m_NativeVisibleInstanceCapacity = 0;
                    m_NativeSortingPositionCapacity = 0;
                    m_NativeLightVisibleInstanceCapacity = 0;
                    m_NativePickingVisibleInstanceCapacity = 0;
                    m_NativeSelectionVisibleInstanceCapacity = 0;
                    m_AnyShadowCastingBatch = false;
                    m_AnySelectedRecord = false;
                    m_NativeDrawCommandLayerMask = 0u;
                    m_NativeLightDrawCommandLayerMask = 0u;
                    m_NativePickingDrawCommandLayerMask = 0u;
                    m_NativeSelectionDrawCommandLayerMask = 0u;
                    m_MeshVisibleCountLength = 0;
                    m_LastCullingSingleMeshCacheRecordCount = 0;
                    m_LastCullingMultiMeshCacheRecordCount = 0;
                    m_LastCullingMeshFallbackRecordCount = 0;
                    m_LastCullingRecordVisibleCacheEntryCount = 0;
                    m_LastCullingBatchVisibleCacheEntryCount = 0;

                    using (s_CullingLayoutCollectMarker.Auto())
                    {
                        CollectCullingRecordsAndMeshCountWorks();
                    }

                    using (s_CullingLayoutMeshVisibleMarker.Auto())
                    {
                        CompleteMeshVisibleCountWorks();
                    }

                    using (s_CullingLayoutCacheMarker.Auto())
                    {
                        BuildMultiMeshVisibleInstanceCapacityCache();
                    }

                    using (s_CullingLayoutDrawCommandsMarker.Auto())
                    {
                        BuildDrawCommandsFromCullingRecords();
                    }
                }
            }

            private unsafe void CollectCullingRecordsAndMeshCountWorks()
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    int meshCommandCount = GetBatchDrawMeshCommandCount(batch);
                    ResetBatchCullingCache(batch, meshCommandCount);
                    for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                    {
                        ParticleRenderRecord record = batch.Records[recordIndex];
                        record.CullingRecordStart = -1;
                        record.CullingRecordCount = 0;
                        record.MeshVisibleCountOffset = -1;
                        record.MeshVisibleCountCount = 0;
                        ResetRecordVisibleInstanceCapacityCache(record, meshCommandCount);
                        int recordStart = m_NativeCullingRecords.Length;
                        int cullingRecordCount = record.State.AppendCullingRecords(
                            record.BatchBaseIndex,
                            record.SpanBaseIndex,
                            batch.UsesPageBillboard,
                            record.IsEditorSelected,
                            m_NativeCullingRecords);
                        if (cullingRecordCount <= 0)
                            continue;

                        record.CullingRecordStart = recordStart;
                        record.CullingRecordCount = cullingRecordCount;
                        AddBatchCullingRecordRange(batch, recordStart, cullingRecordCount);
                        if (meshCommandCount <= 1)
                        {
                            int visibleCount = GetVisibleInstanceCount(record.RenderMode, record.ActiveCount);
                            record.VisibleInstanceCapacities[0] = visibleCount;
                            batch.VisibleInstanceCapacities[0] += visibleCount;
                            m_LastCullingSingleMeshCacheRecordCount++;
                            continue;
                        }

                        if (!record.State.TryCreateMeshVisibleCountWork(
                                meshCommandCount,
                                m_MeshVisibleCountLength,
                                out ParticleMeshVisibleCountWork work))
                        {
                            int visibleCount = Mathf.Max(0, record.ActiveCount);
                            record.VisibleInstanceCapacities[0] = visibleCount;
                            batch.VisibleInstanceCapacities[0] += visibleCount;
                            m_LastCullingMeshFallbackRecordCount++;
                            continue;
                        }

                        m_MeshVisibleCountWorks.Add(work);
                        record.MeshVisibleCountOffset = m_MeshVisibleCountLength;
                        record.MeshVisibleCountCount = meshCommandCount;
                        m_MeshVisibleCountLength += meshCommandCount;
                    }
                }
            }

            private static void ResetBatchCullingCache(ParticleDrawBatch batch, int meshCommandCount)
            {
                if (batch == null)
                    return;

                batch.CullingRecordStart = -1;
                batch.CullingRecordCount = 0;
                int count = Mathf.Max(1, meshCommandCount);
                if (batch.VisibleInstanceCapacities == null || batch.VisibleInstanceCapacities.Length < count)
                    batch.VisibleInstanceCapacities = new int[count];

                Array.Clear(batch.VisibleInstanceCapacities, 0, count);
            }

            private static void AddBatchCullingRecordRange(
                ParticleDrawBatch batch,
                int recordStart,
                int recordCount)
            {
                if (batch == null || recordCount <= 0)
                    return;

                if (batch.CullingRecordStart < 0)
                {
                    batch.CullingRecordStart = recordStart;
                    batch.CullingRecordCount = recordCount;
                    return;
                }

                int currentEnd = batch.CullingRecordStart + batch.CullingRecordCount;
                int nextEnd = recordStart + recordCount;
                int mergedStart = Mathf.Min(batch.CullingRecordStart, recordStart);
                int mergedEnd = Mathf.Max(currentEnd, nextEnd);
                batch.CullingRecordStart = mergedStart;
                batch.CullingRecordCount = Mathf.Max(0, mergedEnd - mergedStart);
            }

            private void CompleteMeshVisibleCountWorks()
            {
                if (m_MeshVisibleCountLength <= 0 || !m_MeshVisibleCountWorks.IsCreated || m_MeshVisibleCountWorks.Length == 0)
                    return;

                EnsureMeshVisibleCountCapacity(m_MeshVisibleCountLength);
                var job = new ParticleMeshVisibleCountJob
                {
                    Works = m_MeshVisibleCountWorks.AsArray(),
                    MeshVisibleCounts = m_MeshVisibleCounts,
                };
                job.Schedule(m_MeshVisibleCountWorks.Length, innerloopBatchCount: 1).Complete();
            }

            private void BuildMultiMeshVisibleInstanceCapacityCache()
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    int meshCommandCount = GetBatchDrawMeshCommandCount(batch);
                    if (batch == null || batch.CullingRecordCount <= 0 || meshCommandCount <= 1)
                        continue;

                    for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                    {
                        ParticleRenderRecord record = batch.Records[recordIndex];
                        if (record == null || record.CullingRecordCount <= 0 || record.MeshVisibleCountOffset < 0)
                            continue;

                        for (int meshCommandIndex = 0; meshCommandIndex < meshCommandCount; meshCommandIndex++)
                        {
                            int meshIndexFilter = ResolveMeshIndexFilter(batch, meshCommandIndex);
                            int recordVisibleCount = GetRecordVisibleInstanceCapacity(record, batch, meshIndexFilter);
                            record.VisibleInstanceCapacities[meshCommandIndex] = recordVisibleCount;
                            batch.VisibleInstanceCapacities[meshCommandIndex] += recordVisibleCount;
                        }

                        m_LastCullingMultiMeshCacheRecordCount++;
                    }
                }
            }

            private static void ResetRecordVisibleInstanceCapacityCache(
                ParticleRenderRecord record,
                int meshCommandCount)
            {
                if (record == null)
                    return;

                int count = Mathf.Max(1, meshCommandCount);
                if (record.VisibleInstanceCapacities == null || record.VisibleInstanceCapacities.Length < count)
                    record.VisibleInstanceCapacities = new int[count];

                Array.Clear(record.VisibleInstanceCapacities, 0, count);
            }

            private void BuildDrawCommandsFromCullingRecords()
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    int layer = Mathf.Clamp(batch.Key.Layer, 0, 31);
                    int meshCommandCount = GetBatchDrawMeshCommandCount(batch);
                    bool batchCastsShadows = batch.ShadowCastingMode != ShadowCastingMode.Off;
                    if (!TryGetBatchCullingRecordRange(batch, out int batchRecordStart, out int batchRecordCount))
                        continue;

                    for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                    {
                        ParticleRenderRecord record = batch.Records[recordIndex];
                        if (record == null || record.CullingRecordCount <= 0)
                            continue;

                        for (int meshCommandIndex = 0; meshCommandIndex < meshCommandCount; meshCommandIndex++)
                        {
                            int meshIndexFilter = ResolveMeshIndexFilter(batch, meshCommandIndex);
                            int recordCommandVisibleCount =
                                GetCachedRecordVisibleInstanceCapacity(record, meshCommandIndex);
                            if (recordCommandVisibleCount <= 0)
                                continue;

                            m_LastCullingRecordVisibleCacheEntryCount++;
                            ParticleDrawCommandInput pickingCommand = CreateDrawCommandInput(
                                batch,
                                record.CullingRecordStart,
                                record.CullingRecordCount,
                                m_NativePickingVisibleInstanceCapacity,
                                recordCommandVisibleCount,
                                sortingPositionOffset: 0,
                                layer,
                                record.PickingEntityId,
                                requiresSortingPositions: false,
                                meshIndexFilter);
                            AddDrawCommandWithRange(
                                m_NativePickingDrawCommandInputs,
                                m_NativePickingDrawRangeInputs,
                                pickingCommand);
                            m_NativePickingDrawCommandLayerMask |= GetLayerBit(layer);
                            m_NativePickingVisibleInstanceCapacity += recordCommandVisibleCount;
                            if (!record.IsEditorSelected)
                                continue;

                            ParticleDrawCommandInput selectionCommand = CreateDrawCommandInput(
                                batch,
                                record.CullingRecordStart,
                                record.CullingRecordCount,
                                m_NativeSelectionVisibleInstanceCapacity,
                                recordCommandVisibleCount,
                                sortingPositionOffset: 0,
                                layer,
                                record.PickingEntityId,
                                requiresSortingPositions: false,
                                meshIndexFilter);
                            AddDrawCommandWithRange(
                                m_NativeSelectionDrawCommandInputs,
                                m_NativeSelectionDrawRangeInputs,
                                selectionCommand);
                            m_NativeSelectionDrawCommandLayerMask |= GetLayerBit(layer);
                            m_NativeSelectionVisibleInstanceCapacity += recordCommandVisibleCount;
                            m_AnySelectedRecord = true;
                        }
                    }

                    for (int meshCommandIndex = 0; meshCommandIndex < meshCommandCount; meshCommandIndex++)
                    {
                        int meshIndexFilter = ResolveMeshIndexFilter(batch, meshCommandIndex);
                        int batchCommandVisibleCount =
                            GetCachedBatchVisibleInstanceCapacity(batch, meshCommandIndex);
                        if (batchCommandVisibleCount <= 0)
                            continue;

                        m_LastCullingBatchVisibleCacheEntryCount++;
                        int sortingPositionOffset = batch.RequiresSortingPositions
                            ? m_NativeSortingPositionCapacity
                            : 0;
                        ParticleDrawCommandInput command = CreateDrawCommandInput(
                            batch,
                            batchRecordStart,
                            batchRecordCount,
                            m_NativeVisibleInstanceCapacity,
                            batchCommandVisibleCount,
                            sortingPositionOffset,
                            layer,
                            EntityId.None,
                            batch.RequiresSortingPositions,
                            meshIndexFilter);
                        AddDrawCommandWithRange(
                            m_NativeDrawCommandInputs,
                            m_NativeDrawRangeInputs,
                            command);
                        m_NativeDrawCommandLayerMask |= GetLayerBit(layer);
                        m_NativeVisibleInstanceCapacity += batchCommandVisibleCount;
                        if (batch.RequiresSortingPositions)
                            m_NativeSortingPositionCapacity += batchCommandVisibleCount;
                    }

                    if (!batchCastsShadows)
                        continue;

                    for (int meshCommandIndex = 0; meshCommandIndex < meshCommandCount; meshCommandIndex++)
                    {
                        int meshIndexFilter = ResolveMeshIndexFilter(batch, meshCommandIndex);
                        int batchLightCommandVisibleCount =
                            GetCachedBatchVisibleInstanceCapacity(batch, meshCommandIndex);
                        if (batchLightCommandVisibleCount <= 0)
                            continue;

                        ParticleDrawCommandInput lightCommand = CreateDrawCommandInput(
                            batch,
                            batchRecordStart,
                            batchRecordCount,
                            m_NativeLightVisibleInstanceCapacity,
                            batchLightCommandVisibleCount,
                            sortingPositionOffset: 0,
                            layer,
                            EntityId.None,
                            requiresSortingPositions: false,
                            meshIndexFilter);
                        AddDrawCommandWithRange(
                            m_NativeLightDrawCommandInputs,
                            m_NativeLightDrawRangeInputs,
                            lightCommand);
                        m_NativeLightDrawCommandLayerMask |= GetLayerBit(layer);
                        m_NativeLightVisibleInstanceCapacity += batchLightCommandVisibleCount;
                        m_AnyShadowCastingBatch = true;
                    }
                }
            }

            private void RebuildBatches()
            {
                using (s_RebuildBatchesMarker.Auto())
                {
                    DrainCullingResults();
                    RebuildRendererLineGroupsFromEcsQuery();
                    m_DrawBatches.Clear();
                    m_TotalBufferByteSize = 0;

                    for (int groupIndex = 0; groupIndex < m_LineGroups.Count; groupIndex++)
                    {
                        ParticleRendererLineGroup group = m_LineGroups[groupIndex];
                        if (group == null || group.Records.Count == 0)
                            continue;

                        ParticleRenderRecord firstRecord = null;
                        for (int recordIndex = 0; recordIndex < group.Records.Count; recordIndex++)
                        {
                            firstRecord = group.Records[recordIndex];
                            if (firstRecord != null)
                                break;
                        }

                        if (firstRecord == null)
                            continue;

                        ParticleDrawBatch batch = new()
                        {
                            Key = group.DrawKey,
                            Material = firstRecord.Material,
                            Mesh = firstRecord.Mesh,
                            Meshes = firstRecord.Meshes ?? Array.Empty<Mesh>(),
                            ShadowCastingMode = firstRecord.ShadowCastingMode,
                            ReceiveShadows = firstRecord.ReceiveShadows,
                            UsesPageBillboard = UsesPageBillboardRenderMode(firstRecord.RenderMode),
                            RequiresSortingPositions = false,
                            GpuLayout = firstRecord.GpuLayout,
                            BatchId = BatchID.Null,
                            ZeroBlockDirty = true,
                        };

                        for (int recordIndex = 0; recordIndex < group.Records.Count; recordIndex++)
                        {
                            ParticleRenderRecord record = group.Records[recordIndex];
                            if (record != null)
                                batch.Records.Add(record);
                        }

                        batch.BatchIndex = m_DrawBatches.Count;
                        m_DrawBatches.Add(batch);
                    }

                    for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                    {
                        ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                        batch.Capacity = 0;
                        batch.SpanCapacity = 0;
                        batch.RequiresSortingPositions = false;
                        for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                        {
                            ParticleRenderRecord record = batch.Records[recordIndex];
                            record.Batch = batch;
                            record.BatchBaseIndex = batch.Capacity;
                            record.SharpIndex = recordIndex;
                            record.SpanBaseIndex = batch.SpanCapacity;
                            record.SpanCapacity = GetVisibleInstanceCount(record.RenderMode, record.Capacity);
                            batch.RequiresSortingPositions |= record.RequiresSortingPositions;
                            batch.Capacity += Mathf.Max(1, record.Capacity);
                            batch.SpanCapacity += Mathf.Max(1, record.SpanCapacity);
                        }

                        batch.Capacity = Mathf.Max(1, batch.Capacity);
                        NormalizeBatchMeshes(batch);
                        batch.SharpCapacity = Mathf.Max(1, batch.Records.Count);
                        batch.SpanCapacity = Mathf.Max(1, batch.SpanCapacity);
                        batch.DataOffset = AlignTo16(m_TotalBufferByteSize);
                        batch.GpuBufferInfos = batch.GpuLayout.CreateBufferInfos(
                            batch.Capacity,
                            batch.SharpCapacity,
                            batch.SpanCapacity);
                        BuildBatchGpuDataDerivedArrays(batch);
                        m_TotalBufferByteSize = batch.DataOffset + batch.GpuLayout.CalculateByteSize(
                            batch.Capacity,
                            batch.SharpCapacity,
                            batch.SpanCapacity);
                    }

                    bool bufferChanged = m_GPUBuffer.EnsureCapacity(Mathf.Max(ZeroBlockByteSize, m_TotalBufferByteSize));
                    RebuildBatchRendererGroup();
                    m_ForceFullUpload = true;
                    m_LayoutDirty = false;
                }
            }

            private static void NormalizeBatchMeshes(ParticleDrawBatch batch)
            {
                if (batch == null)
                    return;

                if (batch.UsesPageBillboard)
                {
                    batch.Meshes = batch.Mesh != null
                        ? new[] { batch.Mesh }
                        : Array.Empty<Mesh>();
                    return;
                }

                Mesh[] meshes = batch.Meshes;
                int count = meshes?.Length ?? 0;
                if (count <= 0)
                {
                    batch.Meshes = batch.Mesh != null
                        ? new[] { batch.Mesh }
                        : Array.Empty<Mesh>();
                    return;
                }

                int write = 0;
                for (int index = 0; index < count; index++)
                {
                    Mesh mesh = meshes[index];
                    if (mesh == null)
                        continue;

                    meshes[write++] = mesh;
                }

                if (write == count)
                    return;

                if (write <= 0)
                {
                    batch.Meshes = batch.Mesh != null
                        ? new[] { batch.Mesh }
                        : Array.Empty<Mesh>();
                    return;
                }

                var compactMeshes = new Mesh[write];
                Array.Copy(meshes, compactMeshes, write);
                batch.Meshes = compactMeshes;
            }

            private void RebuildBatchRendererGroup()
            {
                m_BRG?.Dispose();
                m_BRG = null;

                if (m_DrawBatches.Count == 0 || m_GPUBuffer.renderBuffer == null)
                    return;

                m_BRG = new BatchRendererGroup(new BatchRendererGroupCreateInfo
                {
                    cullingCallback = OnPerformCulling,
                    userContext = IntPtr.Zero,
                });
#if UNITY_EDITOR
                m_BRG.SetEnabledViewTypes(new[]
                {
                    BatchCullingViewType.Camera,
                    BatchCullingViewType.Light,
                    BatchCullingViewType.Picking,
                    BatchCullingViewType.SelectionOutline,
                });

                Material pickingMaterial = GetOrCreatePickingMaterial();
                if (pickingMaterial != null)
                    m_BRG.SetPickingMaterial(pickingMaterial);
#endif

                for (int index = 0; index < m_DrawBatches.Count; index++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[index];
                    int meshCount = batch.Meshes?.Length ?? 0;
                    if (meshCount <= 0)
                        continue;

                    if (batch.MeshIds == null || batch.MeshIds.Length != meshCount)
                        batch.MeshIds = new BatchMeshID[meshCount];

                    for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                        batch.MeshIds[meshIndex] = m_BRG.RegisterMesh(batch.Meshes[meshIndex]);

                    batch.MeshId = batch.MeshIds[0];
                    batch.MaterialId = m_BRG.RegisterMaterial(batch.Material);

                    var metadata = new NativeArray<MetadataValue>(
                        batch.GpuBufferInfos.Length,
                        Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    try
                    {
                        for (int metadataIndex = 0; metadataIndex < batch.GpuBufferInfos.Length; metadataIndex++)
                        {
                            VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[metadataIndex];
                            metadata[metadataIndex] = CreateMetadataValue(
                                bufferInfo.DataInfo,
                                batch.DataOffset + bufferInfo.ByteOffset);
                        }

                        batch.BatchId = m_BRG.AddBatch(
                            metadata,
                            m_GPUBuffer.renderBuffer.bufferHandle,
                            0u,
                            ResolveBufferWindowSize());
                    }
                    finally
                    {
                        metadata.Dispose();
                    }

                    for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                        batch.Records[recordIndex].State.ResetRendererCullingStats();
                }
            }

            private static MetadataValue CreateMetadataValue(
                VividParticleGpuDataInfo dataInfo,
                int byteAddress)
            {
                int shaderPropertyId = GetGpuDataShaderPropertyId(dataInfo.DataId);
                return dataInfo.UsesInstanceMetadata
                    ? CreatePerInstanceMetadata(shaderPropertyId, byteAddress)
                    : CreateSharedMetadata(shaderPropertyId, byteAddress);
            }

#if UNITY_EDITOR
            private Material GetOrCreatePickingMaterial()
            {
                if (m_PickingMaterial != null)
                    return m_PickingMaterial;

                Shader shader = Shader.Find(PickingShaderName);
                if (shader == null)
                    return null;

                m_PickingMaterial = new Material(shader)
                {
                    name = "Vivid Particle BRG Picking Material",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                ParticleSystemState.ConfigureDefaultParticleMaterial(m_PickingMaterial);
                return m_PickingMaterial;
            }
#endif

            private static int GetGpuDataShaderPropertyId(VividParticleGpuDataId dataId)
            {
                return dataId switch
                {
                    VividParticleGpuDataId.SharedData => s_SharedDataId,
                    VividParticleGpuDataId.SpanSharedData => s_SpanSharedDataId,
                    VividParticleGpuDataId.PositionSize => s_PositionSizeId,
                    VividParticleGpuDataId.BaseColor => s_BaseColorId,
                    VividParticleGpuDataId.Rotation => s_RotationId,
                    VividParticleGpuDataId.VelocityStretch => s_VelocityStretchId,
                    VividParticleGpuDataId.Scale => s_ScaleId,
                    VividParticleGpuDataId.UV => s_UVId,
                    VividParticleGpuDataId.CustomData1 => s_CustomData1Id,
                    VividParticleGpuDataId.CustomData2 => s_CustomData2Id,
                    VividParticleGpuDataId.MeshIndex => s_MeshIndexId,
                    _ => 0,
                };
            }

            private void BuildBatchGpuDataDerivedArrays(ParticleDrawBatch batch)
            {
                m_RecordCopyDescriptorScratch.Clear();
                m_SharedValueBufferInfoScratch.Clear();
                m_PerSharpValueBufferInfoScratch.Clear();

                batch.HasSharedDataBufferInfo = false;
                batch.HasSpanSharedDataBufferInfo = false;
                batch.SharedDataBufferInfo = default;
                batch.SpanSharedDataBufferInfo = default;
                batch.UploadColumnLayout = CreateEmptyUploadColumnLayout();

                VividParticleGpuBufferDataInfo[] bufferInfos = batch.GpuBufferInfos;
                if (bufferInfos != null)
                {
                    for (int dataIndex = 0; dataIndex < bufferInfos.Length; dataIndex++)
                    {
                        VividParticleGpuBufferDataInfo bufferInfo = bufferInfos[dataIndex];
                        VividParticleGpuDataInfo dataInfo = bufferInfo.DataInfo;
                        if (dataInfo.CreatesRecordCopyDescriptor)
                            m_RecordCopyDescriptorScratch.Add(bufferInfo.CopyDescriptor);

                        switch (dataInfo.Role)
                        {
                            case VividParticleGpuDataRole.SharedValue:
                                m_SharedValueBufferInfoScratch.Add(bufferInfo);
                                break;

                            case VividParticleGpuDataRole.PerSharpValue:
                                m_PerSharpValueBufferInfoScratch.Add(bufferInfo);
                                break;

                            case VividParticleGpuDataRole.SharedDataBlock:
                                batch.SharedDataBufferInfo = bufferInfo;
                                batch.HasSharedDataBufferInfo = true;
                                break;

                            case VividParticleGpuDataRole.SpanSharedDataBlock:
                                batch.SpanSharedDataBufferInfo = bufferInfo;
                                batch.HasSpanSharedDataBufferInfo = true;
                                break;
                        }

                        if (dataInfo.IsPerInstance && dataInfo.HasUploadSegment)
                        {
                            SetUploadColumnLayoutOffset(
                                ref batch.UploadColumnLayout,
                                dataInfo,
                                bufferInfo.ByteOffset);
                        }
                    }
                }

                batch.RecordCopyDescriptors = ToArrayOrEmpty(m_RecordCopyDescriptorScratch);
                batch.SharedValueBufferInfos = ToArrayOrEmpty(m_SharedValueBufferInfoScratch);
                batch.PerSharpValueBufferInfos = ToArrayOrEmpty(m_PerSharpValueBufferInfoScratch);
            }

            private static T[] ToArrayOrEmpty<T>(List<T> values)
            {
                return values.Count > 0
                    ? values.ToArray()
                    : Array.Empty<T>();
            }

            private void ScheduleRenderUploadGraph()
            {
                using (s_UploadMarker.Auto())
                {
                    EnsureNativeUploadQueues();
                    bool forceFullUpload = m_ForceFullUpload;
                    bool hasUpload = false;
                    using (s_UploadCollectDirtyMarker.Auto())
                    {
                        m_UploadRecordWorks.Clear();
                        m_UploadBatchWorks.Clear();
                        m_TransformUploadPageWorks.Clear();
                        m_ColorUploadPageWorks.Clear();
                        m_VelocityStretchUploadPageWorks.Clear();
                        m_UVUploadPageWorks.Clear();
                        m_CustomDataUploadPageWorks.Clear();
                        m_MeshIndexUploadPageWorks.Clear();
                        m_SharedDataWorks.Clear();
                        m_UploadCopyWorks.Clear();
                        m_PendingRenderJobFlags = 0u;
                        m_LastDirtyUploadQueueCount = forceFullUpload || !m_DirtyUploadRecords.IsCreated
                            ? 0
                            : m_DirtyUploadRecords.Length;
                        m_LastDirtyUploadBatchQueueCount = forceFullUpload || !m_DirtyUploadBatchIndices.IsCreated
                            ? 0
                            : m_DirtyUploadBatchIndices.Length;
                        m_LastInvalidDirtyUploadQueueCount = 0;
                        m_LastInvalidDirtyUploadBatchQueueCount = 0;
                        m_LastUploadRecordWorkCount = 0;
                        m_LastUploadBatchWorkCount = 0;
                        m_LastUploadPageWorkCount = 0;
                        m_LastMergedUploadCopyWorkCount = 0;
                        m_LastUploadColumnMask = 0;
                        m_LastUploadDataBits = 0u;
                        m_LastRenderJobModuleFlags = 0u;
                        m_LastRenderPageJobModuleCount = 0;

                        if (forceFullUpload)
                        {
                            for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                            {
                                ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                                if (batch != null)
                                {
                                    batch.UploadDirtyQueued = false;
                                    hasUpload |= TryAddUploadBatchWork(batch, forceFullUpload: true);
                                }
                            }

                            if (m_DirtyUploadBatchIndices.IsCreated)
                                m_DirtyUploadBatchIndices.Clear();

                            for (int recordSlot = 0; recordSlot < m_RecordSlots.Count; recordSlot++)
                            {
                                ParticleRenderRecord record = m_RecordSlots[recordSlot];
                                if (record != null)
                                    record.UploadDirtyQueued = false;

                                hasUpload |= TryAddUploadRecordWork(record, forceFullUpload);
                            }

                            if (m_DirtyUploadRecords.IsCreated)
                                m_DirtyUploadRecords.Clear();
                        }
                        else if (m_DirtyUploadRecords.IsCreated)
                        {
                            hasUpload |= DrainDirtyUploadBatchQueue();
                            for (int dirtyIndex = 0; dirtyIndex < m_DirtyUploadRecords.Length; dirtyIndex++)
                            {
                                ParticleUploadRecordRef dirtyRecord = m_DirtyUploadRecords[dirtyIndex];
                                if (!TryGetRecord(
                                        dirtyRecord.RecordSlot,
                                        dirtyRecord.RecordVersion,
                                        out ParticleRenderRecord record))
                                {
                                    m_LastInvalidDirtyUploadQueueCount++;
                                    continue;
                                }

                                record.UploadDirtyQueued = false;
                                hasUpload |= TryAddUploadRecordWork(record, forceFullUpload);
                            }

                            m_DirtyUploadRecords.Clear();
                        }

                        m_LastUploadRecordWorkCount = m_UploadRecordWorks.Length;
                        m_LastUploadBatchWorkCount = m_UploadBatchWorks.Length;
                    }

                    if (!hasUpload)
                    {
                        m_GPUBuffer.ResetLastUploadStats();
                        m_LastUploadRecordWorkCount = 0;
                        m_LastUploadBatchWorkCount = 0;
                        m_LastUploadPageWorkCount = 0;
                        m_LastUploadColumnMask = 0;
                        m_LastUploadDataBits = 0u;
                        m_ForceFullUpload = false;
                        return;
                    }

                    byte* bufferBase;
                    using (s_UploadLockBufferMarker.Auto())
                    {
                        bufferBase = m_GPUBuffer.BeginWrite();
                    }

                    if (bufferBase == null)
                    {
                        return;
                    }

                    try
                    {
                        using (s_UploadBuildWorksMarker.Auto())
                        {
                            AddBatchSharedDataWorks(bufferBase, forceFullUpload);

                            for (int workIndex = 0; workIndex < m_UploadRecordWorks.Length; workIndex++)
                            {
                                ParticleUploadRecordWork work = m_UploadRecordWorks[workIndex];
                                if (!TryGetRecord(work.RecordSlot, work.RecordVersion, out ParticleRenderRecord record))
                                    continue;

                                ParticleDrawBatch batch = record.Batch;
                                int count = Mathf.Clamp(work.Count, 0, Mathf.Max(0, record.ActiveCount - work.StartIndex));
                                bool hasInstanceRange = work.HasInstanceRange != 0 && count > 0;
                                bool hasSpanData = work.HasSpanData != 0;
                                int columnMask = work.ColumnMask;
                                uint sharedDataBits = work.SharedDataBits;
                                int sharedStartIndex = hasInstanceRange ? work.StartIndex : 0;
                                int sharedCount = hasInstanceRange
                                    ? count
                                    : record.ActiveCount;
                                if (!hasInstanceRange && sharedCount <= 0)
                                    continue;

                                if (hasInstanceRange
                                    && record.State.TryCreateRenderUploadSource(
                                    record.BatchBaseIndex,
                                    batch.Capacity,
                                    batch.DataOffset,
                                    bufferBase,
                                    out ParticleRenderUploadSource source))
                                {
                                    AddUploadPageWorks(batch, source, work.StartIndex, count, columnMask);
                                }

                                AddRecordSharedDataWorks(
                                    bufferBase,
                                    batch,
                                    record,
                                    sharedStartIndex,
                                    sharedCount,
                                    sharedDataBits,
                                    includeSpanData: hasSpanData);
                                AddGpuDataCopyWorks(
                                    record,
                                    batch.RecordCopyDescriptors,
                                    batch.DataOffset,
                                    work,
                                    count,
                                    sharedStartIndex,
                                    sharedCount);
                            }
                        }

                        ClearPendingUploadViews();
                        bool hasPageUploadWorks = m_TransformUploadPageWorks.Length > 0
                            || m_ColorUploadPageWorks.Length > 0
                            || m_VelocityStretchUploadPageWorks.Length > 0
                            || m_UVUploadPageWorks.Length > 0
                            || m_CustomDataUploadPageWorks.Length > 0
                            || m_MeshIndexUploadPageWorks.Length > 0;
                        if (hasPageUploadWorks || m_SharedDataWorks.Length > 0)
                        {
                            using (s_UploadCopyWorkArraysMarker.Auto())
                            {
                                if (m_TransformUploadPageWorks.Length > 0)
                                    m_PendingTransformUploadPageWorks = m_TransformUploadPageWorks.AsArray();

                                if (m_ColorUploadPageWorks.Length > 0)
                                    m_PendingColorUploadPageWorks = m_ColorUploadPageWorks.AsArray();

                                if (m_VelocityStretchUploadPageWorks.Length > 0)
                                    m_PendingVelocityStretchUploadPageWorks = m_VelocityStretchUploadPageWorks.AsArray();

                                if (m_UVUploadPageWorks.Length > 0)
                                    m_PendingUVUploadPageWorks = m_UVUploadPageWorks.AsArray();

                                if (m_CustomDataUploadPageWorks.Length > 0)
                                    m_PendingCustomDataUploadPageWorks = m_CustomDataUploadPageWorks.AsArray();

                                if (m_MeshIndexUploadPageWorks.Length > 0)
                                    m_PendingMeshIndexUploadPageWorks = m_MeshIndexUploadPageWorks.AsArray();

                                if (m_SharedDataWorks.Length > 0)
                                    m_PendingSharedDataWorks = m_SharedDataWorks.AsArray();
                            }

                            using (s_UploadScheduleJobsMarker.Auto())
                            {
                                m_LastRenderJobModuleFlags = m_PendingRenderJobFlags;
                                m_LastRenderPageJobModuleCount = CountRenderPageJobModules(m_PendingRenderJobFlags);
                                m_PendingUploadHandle = VividParticleRenderJobPipeline.Schedule(
                                    new ParticleRenderPageJobWorkSet(
                                        m_PendingTransformUploadPageWorks,
                                        m_PendingColorUploadPageWorks,
                                        m_PendingVelocityStretchUploadPageWorks,
                                        m_PendingUVUploadPageWorks,
                                        m_PendingCustomDataUploadPageWorks,
                                        m_PendingMeshIndexUploadPageWorks),
                                    m_PendingSharedDataWorks,
                                    m_PendingRenderJobFlags);
                                JobHandle.ScheduleBatchedJobs();
                            }
                        }
                        else
                        {
                            m_LastRenderJobModuleFlags = 0u;
                            m_LastRenderPageJobModuleCount = 0;
                        }

                        m_HasPendingUpload = true;
                    }
                    catch
                    {
                        m_PendingUploadHandle.Complete();
                        ClearPendingUploadViews();
                        m_PendingUploadHandle = default;
                        m_HasPendingUpload = false;
                        m_GPUBuffer.EndWrite();
                        m_ForceFullUpload = true;
                        throw;
                    }
                }
            }

            public void CompletePendingUpload()
            {
                CompletePendingBoundsUpdates();

                if (!m_HasPendingUpload)
                    return;

                using (s_CompleteUploadMarker.Auto())
                {
                    m_PendingUploadHandle.Complete();
                    m_PendingUploadHandle = default;
                    ClearPendingUploadViews();

                    AddPendingUploadCopyOperations();
                    m_GPUBuffer.EndWrite();

                    for (int workIndex = 0; workIndex < m_UploadRecordWorks.Length; workIndex++)
                    {
                        ParticleUploadRecordWork work = m_UploadRecordWorks[workIndex];
                        if (TryGetRecord(work.RecordSlot, work.RecordVersion, out ParticleRenderRecord record))
                        {
                            record.State.ClearUploadDirty();
                            record.State.SetRendererUploadStats(
                                true,
                                record.ActiveCount,
                                record.LastUploadOperationCount,
                                record.LastUploadByteCount,
                                m_GPUBuffer.bufferIndex);
                        }
                    }

                    m_ForceFullUpload = false;
                    m_HasPendingUpload = false;
                }
            }

            private void ClearPendingUploadViews()
            {
                m_PendingTransformUploadPageWorks = default;
                m_PendingColorUploadPageWorks = default;
                m_PendingVelocityStretchUploadPageWorks = default;
                m_PendingUVUploadPageWorks = default;
                m_PendingCustomDataUploadPageWorks = default;
                m_PendingMeshIndexUploadPageWorks = default;
                m_PendingSharedDataWorks = default;
            }

            private bool TryAddUploadRecordWork(ParticleRenderRecord record, bool forceFullUpload)
            {
                if (record == null || record.State == null)
                    return false;

                record.LastUploadOperationCount = 0;
                record.LastUploadByteCount = 0;
                bool hasInstanceRange = record.State.TryGetUploadRange(
                    forceFullUpload,
                    out int startIndex,
                    out int count,
                    out int columnMask,
                    out bool spanDataDirty);
                uint sharedDataBits = forceFullUpload
                    ? record.GpuLayout.DataPerSharpBits
                    : record.State.GetPendingSharedDataBits();
                bool hasSharedData = sharedDataBits != 0u;
                if (!hasInstanceRange && !hasSharedData)
                {
                    record.State.ClearUploadDirty();
                    return false;
                }

                m_UploadRecordWorks.Add(new ParticleUploadRecordWork
                {
                    RecordSlot = record.RecordSlot,
                    RecordVersion = record.RecordVersion,
                    StartIndex = startIndex,
                    Count = count,
                    ColumnMask = columnMask,
                    HasInstanceRange = hasInstanceRange ? 1 : 0,
                    HasSharedData = hasSharedData ? 1 : 0,
                    HasSpanData = spanDataDirty ? 1 : 0,
                    SharedDataBits = sharedDataBits,
                });
                return true;
            }

            private bool DrainDirtyUploadBatchQueue()
            {
                if (!m_DirtyUploadBatchIndices.IsCreated || m_DirtyUploadBatchIndices.Length == 0)
                    return false;

                bool hasUpload = false;
                for (int dirtyIndex = 0; dirtyIndex < m_DirtyUploadBatchIndices.Length; dirtyIndex++)
                {
                    int dirtyBatchIndex = m_DirtyUploadBatchIndices[dirtyIndex];
                    if (!TryGetBatch(dirtyBatchIndex, out ParticleDrawBatch batch))
                    {
                        m_LastInvalidDirtyUploadBatchQueueCount++;
                        continue;
                    }

                    batch.UploadDirtyQueued = false;
                    hasUpload |= TryAddUploadBatchWork(batch, forceFullUpload: false);
                }

                m_DirtyUploadBatchIndices.Clear();
                return hasUpload;
            }

            private bool TryAddUploadBatchWork(ParticleDrawBatch batch, bool forceFullUpload)
            {
                if (batch == null || batch.BatchIndex < 0)
                    return false;

                bool hasSharedValues = batch.SharedValueBufferInfos != null && batch.SharedValueBufferInfos.Length > 0;
                bool hasDirtySharedValues = hasSharedValues && (forceFullUpload || batch.SharedValuesDirty);
                if (!batch.ZeroBlockDirty && !hasDirtySharedValues)
                    return false;

                m_UploadBatchWorks.Add(new ParticleUploadBatchWork
                {
                    BatchIndex = batch.BatchIndex,
                });
                return true;
            }

            private bool TryGetBatch(int batchIndex, out ParticleDrawBatch batch)
            {
                if ((uint)batchIndex < (uint)m_DrawBatches.Count)
                {
                    batch = m_DrawBatches[batchIndex];
                    return batch != null && batch.BatchIndex == batchIndex;
                }

                batch = null;
                return false;
            }

            private void EnsureNativeUploadQueues()
            {
                if (!m_DirtyUploadRecords.IsCreated)
                    m_DirtyUploadRecords = new NativeList<ParticleUploadRecordRef>(64, Allocator.Persistent);

                if (!m_DirtyUploadBatchIndices.IsCreated)
                    m_DirtyUploadBatchIndices = new NativeList<int>(16, Allocator.Persistent);

                if (!m_UploadRecordWorks.IsCreated)
                    m_UploadRecordWorks = new NativeList<ParticleUploadRecordWork>(64, Allocator.Persistent);

                if (!m_UploadBatchWorks.IsCreated)
                    m_UploadBatchWorks = new NativeList<ParticleUploadBatchWork>(16, Allocator.Persistent);

                if (!m_TransformUploadPageWorks.IsCreated)
                    m_TransformUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(128, Allocator.Persistent);

                if (!m_ColorUploadPageWorks.IsCreated)
                    m_ColorUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(32, Allocator.Persistent);

                if (!m_VelocityStretchUploadPageWorks.IsCreated)
                    m_VelocityStretchUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(32, Allocator.Persistent);

                if (!m_UVUploadPageWorks.IsCreated)
                    m_UVUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(32, Allocator.Persistent);

                if (!m_CustomDataUploadPageWorks.IsCreated)
                    m_CustomDataUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(32, Allocator.Persistent);

                if (!m_MeshIndexUploadPageWorks.IsCreated)
                    m_MeshIndexUploadPageWorks = new NativeList<ParticleRenderUploadPageWork>(32, Allocator.Persistent);

                if (!m_SharedDataWorks.IsCreated)
                    m_SharedDataWorks = new NativeList<ParticleRenderSharedDataWork>(64, Allocator.Persistent);

                if (!m_UploadCopyWorks.IsCreated)
                    m_UploadCopyWorks = new NativeList<ParticleGpuDataCopyWork>(128, Allocator.Persistent);
            }

            private void DisposeNativeUploadQueues()
            {
                ClearPendingUploadViews();

                if (m_DirtyUploadRecords.IsCreated)
                    m_DirtyUploadRecords.Dispose();

                if (m_DirtyUploadBatchIndices.IsCreated)
                    m_DirtyUploadBatchIndices.Dispose();

                if (m_UploadRecordWorks.IsCreated)
                    m_UploadRecordWorks.Dispose();

                if (m_UploadBatchWorks.IsCreated)
                    m_UploadBatchWorks.Dispose();

                if (m_TransformUploadPageWorks.IsCreated)
                    m_TransformUploadPageWorks.Dispose();

                if (m_ColorUploadPageWorks.IsCreated)
                    m_ColorUploadPageWorks.Dispose();

                if (m_VelocityStretchUploadPageWorks.IsCreated)
                    m_VelocityStretchUploadPageWorks.Dispose();

                if (m_UVUploadPageWorks.IsCreated)
                    m_UVUploadPageWorks.Dispose();

                if (m_CustomDataUploadPageWorks.IsCreated)
                    m_CustomDataUploadPageWorks.Dispose();

                if (m_MeshIndexUploadPageWorks.IsCreated)
                    m_MeshIndexUploadPageWorks.Dispose();

                if (m_SharedDataWorks.IsCreated)
                    m_SharedDataWorks.Dispose();

                if (m_UploadCopyWorks.IsCreated)
                    m_UploadCopyWorks.Dispose();

                m_DirtyUploadRecords = default;
                m_DirtyUploadBatchIndices = default;
                m_UploadRecordWorks = default;
                m_UploadBatchWorks = default;
                m_TransformUploadPageWorks = default;
                m_ColorUploadPageWorks = default;
                m_VelocityStretchUploadPageWorks = default;
                m_UVUploadPageWorks = default;
                m_CustomDataUploadPageWorks = default;
                m_MeshIndexUploadPageWorks = default;
                m_SharedDataWorks = default;
                m_UploadCopyWorks = default;
                m_PendingRenderJobFlags = 0u;
            }

            private void EnsureNativeBoundsLayout()
            {
                if (!m_BoundsPageWorks.IsCreated)
                    m_BoundsPageWorks = new NativeList<ParticleBoundsPageWork>(64, Allocator.Persistent);

                if (!m_BoundsRecordWorks.IsCreated)
                    m_BoundsRecordWorks = new NativeList<ParticleBoundsRecordReduceWork>(16, Allocator.Persistent);
            }

            private void EnsureBoundsResultCapacity(int pageCount, int recordCount)
            {
                pageCount = Mathf.Max(1, pageCount);
                recordCount = Mathf.Max(1, recordCount);

                if (!m_BoundsPageResults.IsCreated || m_BoundsPageResults.Length < pageCount)
                {
                    if (m_BoundsPageResults.IsCreated)
                        m_BoundsPageResults.Dispose();

                    m_BoundsPageResults = new NativeArray<ParticleBoundsData>(
                        pageCount,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                }

                if (!m_BoundsRecordResults.IsCreated || m_BoundsRecordResults.Length < recordCount)
                {
                    if (m_BoundsRecordResults.IsCreated)
                        m_BoundsRecordResults.Dispose();

                    m_BoundsRecordResults = new NativeArray<ParticleBoundsRecordResult>(
                        recordCount,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                }
            }

            private void DisposeNativeBoundsLayout()
            {
                CompletePendingBoundsUpdates();

                if (m_BoundsPageWorks.IsCreated)
                    m_BoundsPageWorks.Dispose();

                if (m_BoundsRecordWorks.IsCreated)
                    m_BoundsRecordWorks.Dispose();

                if (m_BoundsPageResults.IsCreated)
                    m_BoundsPageResults.Dispose();

                if (m_BoundsRecordResults.IsCreated)
                    m_BoundsRecordResults.Dispose();

                m_BoundsPageWorks = default;
                m_BoundsRecordWorks = default;
                m_BoundsPageResults = default;
                m_BoundsRecordResults = default;
                m_BoundsStates.Clear();
            }

            private void EnsureNativeCullingLayout()
            {
                if (!m_NativeCullingRecords.IsCreated)
                    m_NativeCullingRecords = new NativeList<ParticleCullingRecord>(64, Allocator.Persistent);

                if (!m_NativeDrawCommandInputs.IsCreated)
                    m_NativeDrawCommandInputs = new NativeList<ParticleDrawCommandInput>(16, Allocator.Persistent);

                if (!m_NativeDrawRangeInputs.IsCreated)
                    m_NativeDrawRangeInputs = new NativeList<ParticleDrawRangeInput>(16, Allocator.Persistent);

                if (!m_NativeLightDrawCommandInputs.IsCreated)
                    m_NativeLightDrawCommandInputs = new NativeList<ParticleDrawCommandInput>(16, Allocator.Persistent);

                if (!m_NativeLightDrawRangeInputs.IsCreated)
                    m_NativeLightDrawRangeInputs = new NativeList<ParticleDrawRangeInput>(16, Allocator.Persistent);

                if (!m_NativePickingDrawCommandInputs.IsCreated)
                    m_NativePickingDrawCommandInputs = new NativeList<ParticleDrawCommandInput>(16, Allocator.Persistent);

                if (!m_NativePickingDrawRangeInputs.IsCreated)
                    m_NativePickingDrawRangeInputs = new NativeList<ParticleDrawRangeInput>(16, Allocator.Persistent);

                if (!m_NativeSelectionDrawCommandInputs.IsCreated)
                    m_NativeSelectionDrawCommandInputs = new NativeList<ParticleDrawCommandInput>(16, Allocator.Persistent);

                if (!m_NativeSelectionDrawRangeInputs.IsCreated)
                    m_NativeSelectionDrawRangeInputs = new NativeList<ParticleDrawRangeInput>(16, Allocator.Persistent);

                if (!m_MeshVisibleCountWorks.IsCreated)
                    m_MeshVisibleCountWorks = new NativeList<ParticleMeshVisibleCountWork>(16, Allocator.Persistent);
            }

            private void EnsureMeshVisibleCountCapacity(int count)
            {
                count = Mathf.Max(1, count);
                if (m_MeshVisibleCounts.IsCreated && m_MeshVisibleCounts.Length >= count)
                    return;

                if (m_MeshVisibleCounts.IsCreated)
                    m_MeshVisibleCounts.Dispose();

                m_MeshVisibleCounts = new NativeArray<int>(
                    count,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            private void DisposeNativeCullingLayout()
            {
                if (m_NativeCullingRecords.IsCreated)
                    m_NativeCullingRecords.Dispose();

                if (m_NativeDrawCommandInputs.IsCreated)
                    m_NativeDrawCommandInputs.Dispose();

                if (m_NativeDrawRangeInputs.IsCreated)
                    m_NativeDrawRangeInputs.Dispose();

                if (m_NativeLightDrawCommandInputs.IsCreated)
                    m_NativeLightDrawCommandInputs.Dispose();

                if (m_NativeLightDrawRangeInputs.IsCreated)
                    m_NativeLightDrawRangeInputs.Dispose();

                if (m_NativePickingDrawCommandInputs.IsCreated)
                    m_NativePickingDrawCommandInputs.Dispose();

                if (m_NativePickingDrawRangeInputs.IsCreated)
                    m_NativePickingDrawRangeInputs.Dispose();

                if (m_NativeSelectionDrawCommandInputs.IsCreated)
                    m_NativeSelectionDrawCommandInputs.Dispose();

                if (m_NativeSelectionDrawRangeInputs.IsCreated)
                    m_NativeSelectionDrawRangeInputs.Dispose();

                if (m_MeshVisibleCountWorks.IsCreated)
                    m_MeshVisibleCountWorks.Dispose();

                if (m_MeshVisibleCounts.IsCreated)
                    m_MeshVisibleCounts.Dispose();

                m_NativeCullingRecords = default;
                m_NativeDrawCommandInputs = default;
                m_NativeDrawRangeInputs = default;
                m_NativeLightDrawCommandInputs = default;
                m_NativeLightDrawRangeInputs = default;
                m_NativePickingDrawCommandInputs = default;
                m_NativePickingDrawRangeInputs = default;
                m_NativeSelectionDrawCommandInputs = default;
                m_NativeSelectionDrawRangeInputs = default;
                m_MeshVisibleCountWorks = default;
                m_MeshVisibleCounts = default;
                m_NativeVisibleInstanceCapacity = 0;
                m_NativeSortingPositionCapacity = 0;
                m_NativeLightVisibleInstanceCapacity = 0;
                m_NativePickingVisibleInstanceCapacity = 0;
                m_NativeSelectionVisibleInstanceCapacity = 0;
                m_MeshVisibleCountLength = 0;
            }

            private void AddPendingUploadCopyOperations()
            {
                m_LastMergedUploadCopyWorkCount = 0;
                if (m_UploadCopyWorks.Length > 1)
                    m_UploadCopyWorks.Sort();

                bool hasPendingRange = false;
                int pendingByteOffset = 0;
                int pendingByteCount = 0;
                for (int workIndex = 0; workIndex < m_UploadCopyWorks.Length; workIndex++)
                {
                    ParticleGpuDataCopyWork work = m_UploadCopyWorks[workIndex];
                    if (work.ByteCount <= 0)
                        continue;

                    m_LastUploadDataBits |= work.DataBit;
                    if (TryGetRecord(work.OwnerRecordSlot, work.OwnerRecordVersion, out ParticleRenderRecord owner))
                    {
                        owner.LastUploadOperationCount++;
                        owner.LastUploadByteCount += work.ByteCount;
                    }

                    int alignedByteCount = AlignTo4(work.ByteCount);
                    if (!hasPendingRange)
                    {
                        pendingByteOffset = work.ByteOffset;
                        pendingByteCount = alignedByteCount;
                        hasPendingRange = true;
                        continue;
                    }

                    if (CanMergeUploadCopyOperations(
                            pendingByteOffset,
                            pendingByteOffset,
                            pendingByteCount,
                            work.ByteOffset,
                            work.ByteOffset))
                    {
                        pendingByteCount += alignedByteCount;
                        continue;
                    }

                    AddMergedUploadCopyOperation(pendingByteOffset, pendingByteCount);
                    pendingByteOffset = work.ByteOffset;
                    pendingByteCount = alignedByteCount;
                }

                if (hasPendingRange)
                    AddMergedUploadCopyOperation(pendingByteOffset, pendingByteCount);
            }

            private void AddMergedUploadCopyOperation(int byteOffset, int byteCount)
            {
                if (byteCount <= 0)
                    return;

                m_GPUBuffer.AddCopyOperation(byteOffset, byteOffset, byteCount);
                m_LastMergedUploadCopyWorkCount++;
            }

            private void AddUploadPageWorks(
                ParticleDrawBatch batch,
                ParticleRenderUploadSource source,
                int startIndex,
                int count,
                int columnMask)
            {
                if (!batch.UploadColumnLayout.HasColumns)
                    return;

                columnMask &= batch.UploadColumnLayout.ColumnMask;
                if (columnMask == 0)
                    return;

                ParticleRenderJobFlags pageRenderJobFlags =
                    batch.UploadColumnLayout.GetRenderJobFlagsForColumnMask(columnMask);
                int endIndex = startIndex + count;
                for (int pageStart = startIndex; pageStart < endIndex; pageStart += BillboardPageSize)
                {
                    int pageCount = Mathf.Min(BillboardPageSize, endIndex - pageStart);
                    ParticleRenderUploadPageWork pageWork = CreateUploadPageWork(
                        source,
                        batch.UploadColumnLayout,
                        columnMask,
                        pageStart,
                        pageCount);
                    m_LastUploadColumnMask |= pageWork.ColumnMask;
                    m_LastUploadPageWorkCount++;
                    if ((pageRenderJobFlags & ParticleRenderJobFlags.TransformUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_TransformUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.TransformUploadColumnMask,
                            ParticleRenderJobFlags.TransformUpload);
                    }

                    if ((pageRenderJobFlags & ParticleRenderJobFlags.ColorUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_ColorUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.ColorUploadColumnMask,
                            ParticleRenderJobFlags.ColorUpload);
                    }

                    if ((pageRenderJobFlags & ParticleRenderJobFlags.VelocityStretchUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_VelocityStretchUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.VelocityStretchUploadColumnMask,
                            ParticleRenderJobFlags.VelocityStretchUpload);
                    }

                    if ((pageRenderJobFlags & ParticleRenderJobFlags.UVUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_UVUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.UVUploadColumnMask,
                            ParticleRenderJobFlags.UVUpload);
                    }

                    if ((pageRenderJobFlags & ParticleRenderJobFlags.CustomDataUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_CustomDataUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.CustomDataUploadColumnMask,
                            ParticleRenderJobFlags.CustomDataUpload);
                    }

                    if ((pageRenderJobFlags & ParticleRenderJobFlags.MeshIndexUpload) != 0)
                    {
                        AddUploadPageWorkForFamily(
                            m_MeshIndexUploadPageWorks,
                            pageWork,
                            batch.UploadColumnLayout.MeshIndexUploadColumnMask,
                            ParticleRenderJobFlags.MeshIndexUpload);
                    }
                }
            }

            private void AddUploadPageWorkForFamily(
                NativeList<ParticleRenderUploadPageWork> works,
                ParticleRenderUploadPageWork pageWork,
                int familyColumnMask,
                ParticleRenderJobFlags flag)
            {
                int columnMask = pageWork.ColumnMask & familyColumnMask;
                if (columnMask == 0)
                    return;

                pageWork.ColumnMask = columnMask;
                works.Add(pageWork);
                m_PendingRenderJobFlags |= (uint)flag;
            }

            private static ParticleRenderUploadPageWork CreateUploadPageWork(
                ParticleRenderUploadSource source,
                ParticleRenderUploadColumnLayout layout,
                int columnMask,
                int startIndex,
                int count)
            {
                source.StartIndex = startIndex;
                source.Count = count;
                return new ParticleRenderUploadPageWork
                {
                    Source = source,
                    ColumnMask = layout.ColumnMask & columnMask,
                    PositionSizeByteOffset = layout.PositionSizeByteOffset,
                    BaseColorByteOffset = layout.BaseColorByteOffset,
                    RotationByteOffset = layout.RotationByteOffset,
                    VelocityStretchByteOffset = layout.VelocityStretchByteOffset,
                    ScaleByteOffset = layout.ScaleByteOffset,
                    UVByteOffset = layout.UVByteOffset,
                    CustomData1ByteOffset = layout.CustomData1ByteOffset,
                    CustomData2ByteOffset = layout.CustomData2ByteOffset,
                    MeshIndexByteOffset = layout.MeshIndexByteOffset,
                };
            }

            private static ParticleRenderUploadColumnLayout CreateEmptyUploadColumnLayout()
            {
                return new ParticleRenderUploadColumnLayout
                {
                    ColumnMask = 0,
                    RenderJobFlags = ParticleRenderJobFlags.None,
                    TransformUploadColumnMask = 0,
                    ColorUploadColumnMask = 0,
                    VelocityStretchUploadColumnMask = 0,
                    ExtraDataUploadColumnMask = 0,
                    PositionSizeByteOffset = -1,
                    BaseColorByteOffset = -1,
                    RotationByteOffset = -1,
                    VelocityStretchByteOffset = -1,
                    ScaleByteOffset = -1,
                    UVByteOffset = -1,
                    CustomData1ByteOffset = -1,
                    CustomData2ByteOffset = -1,
                    MeshIndexByteOffset = -1,
                    UVUploadColumnMask = UploadColumnUVMask,
                    CustomDataUploadColumnMask = UploadColumnCustomDataMask,
                    MeshIndexUploadColumnMask = UploadColumnMeshIndexMask,
                };
            }

            private static void SetUploadColumnLayoutOffset(
                ref ParticleRenderUploadColumnLayout layout,
                VividParticleGpuDataInfo dataInfo,
                int byteOffset)
            {
                int columnMask = dataInfo.UploadColumnMask;
                if (columnMask == 0)
                    return;

                switch (dataInfo.UploadSegment)
                {
                    case InstanceUploadSegment.PositionSize:
                        layout.PositionSizeByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.BaseColor:
                        layout.BaseColorByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.Rotation:
                        layout.RotationByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.VelocityStretch:
                        layout.VelocityStretchByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.Scale:
                        layout.ScaleByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.UV:
                        layout.UVByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.CustomData1:
                        layout.CustomData1ByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.CustomData2:
                        layout.CustomData2ByteOffset = byteOffset;
                        break;
                    case InstanceUploadSegment.MeshIndex:
                        layout.MeshIndexByteOffset = byteOffset;
                        break;
                }

                layout.ColumnMask |= columnMask;
                layout.RenderJobFlags |= (ParticleRenderJobFlags)dataInfo.RenderJobFlagMask;
                if ((dataInfo.RenderJobFlagMask & RenderJobTransformUploadFlag) != 0u)
                    layout.TransformUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobColorUploadFlag) != 0u)
                    layout.ColorUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobVelocityStretchUploadFlag) != 0u)
                    layout.VelocityStretchUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobExtraDataUploadFlag) != 0u)
                    layout.ExtraDataUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobUVUploadFlag) != 0u)
                    layout.UVUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobCustomDataUploadFlag) != 0u)
                    layout.CustomDataUploadColumnMask |= columnMask;

                if ((dataInfo.RenderJobFlagMask & RenderJobMeshIndexUploadFlag) != 0u)
                    layout.MeshIndexUploadColumnMask |= columnMask;
            }

            private void AddBatchSharedDataWorks(byte* bufferBase, bool forceSharedValueUpload)
            {
                for (int workIndex = 0; workIndex < m_UploadBatchWorks.Length; workIndex++)
                {
                    if (!TryGetBatch(m_UploadBatchWorks[workIndex].BatchIndex, out ParticleDrawBatch batch))
                        continue;

                    bool uploadBatchSharedValues = forceSharedValueUpload
                        || batch.ZeroBlockDirty
                        || batch.SharedValuesDirty;
                    if (batch.ZeroBlockDirty)
                    {
                        UnsafeUtility.MemClear(bufferBase + batch.DataOffset, ZeroBlockByteSize);
                        batch.ZeroBlockDirty = false;
                        if (batch.Records.Count > 0)
                        {
                            ParticleRenderRecord owner = batch.Records[0];
                            AddGpuDataCopyWork(owner, batch.DataOffset, ZeroBlockByteSize);
                        }
                    }

                    if (!uploadBatchSharedValues)
                        continue;

                    batch.SharedValuesDirty = false;
                    batch.UploadDirtyQueued = false;
                    for (int dataIndex = 0; dataIndex < batch.SharedValueBufferInfos.Length; dataIndex++)
                    {
                        VividParticleGpuBufferDataInfo bufferInfo = batch.SharedValueBufferInfos[dataIndex];
                        if (!TryGetSharedGpuDataValue(bufferInfo.DataInfo, out float4 value))
                            continue;

                        m_SharedDataWorks.Add(new ParticleRenderSharedDataWork
                        {
                            BufferBase = bufferBase,
                            Kind = SharedDataWorkKindFloat4,
                            BatchDataOffset = batch.DataOffset,
                            ColumnByteOffset = bufferInfo.ByteOffset,
                            ElementStart = 0,
                            ElementCount = 1,
                            Value = value,
                        });
                        m_PendingRenderJobFlags |= (uint)ParticleRenderJobFlags.SharedData;
                        if (batch.Records.Count > 0)
                            AddGpuDataCopyOperation(
                                batch.Records[0],
                                batch.DataOffset,
                                bufferInfo.CopyDescriptor,
                                elementStart: 0,
                                elementCount: 1);
                    }
                }
            }

            private void AddRecordSharedDataWorks(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record,
                int startIndex,
                int count,
                uint sharedDataBits,
                bool includeSpanData)
            {
                if (sharedDataBits != 0u)
                {
                    if ((sharedDataBits & batch.GpuLayout.SharedDataBlockBits) != 0u)
                        AddPerSharpSharedDataWork(bufferBase, batch, record);

                    AddPerSharpGpuDataWorks(bufferBase, batch, record, sharedDataBits);
                }

                if (includeSpanData)
                    AddSpanSharedDataWork(bufferBase, batch, record, startIndex, count);
            }

            private void AddPerSharpSharedDataWork(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record)
            {
                if (!batch.HasSharedDataBufferInfo)
                    return;

                m_SharedDataWorks.Add(new ParticleRenderSharedDataWork
                {
                    BufferBase = bufferBase,
                    Kind = SharedDataWorkKindPerSharp,
                    BatchDataOffset = batch.DataOffset,
                    ColumnByteOffset = batch.SharedDataBufferInfo.ByteOffset,
                    ElementStart = record.SharpIndex,
                    ElementCount = 1,
                    SharpIndex = record.SharpIndex,
                    ActiveCount = record.ActiveCount,
                    Capacity = record.Capacity,
                    UsesPageBillboard = batch.UsesPageBillboard ? 1 : 0,
                    RenderMode = (int)record.RenderMode,
                    RendererPriority = record.Material != null ? record.Material.renderQueue : 0,
                    ShadowCastingMode = (int)record.ShadowCastingMode,
                    ReceiveShadows = record.ReceiveShadows ? 1 : 0,
                    SortMode = (int)record.SortMode,
                    DataPerSharpBits = batch.GpuLayout.DataPerSharpBits,
                    Layer = record.Layer,
                    RenderingLayerMask = record.RenderingLayerMask,
                    SpanBaseIndex = record.SpanBaseIndex,
                    BatchBaseIndex = record.BatchBaseIndex,
                    IsEditorSelected = record.IsEditorSelected ? 1 : 0,
                    PickingEntityIdLow = GetEntityIdLow(record.PickingEntityId),
                    PickingEntityIdHigh = GetEntityIdHigh(record.PickingEntityId),
                    SizeScale = record.SizeScale,
                    StretchLengthScale = record.StretchLengthScale,
                    StretchSpeedScale = record.StretchSpeedScale,
                    Pivot = ToFloat3(record.Pivot),
                    MinParticleSize = record.MinParticleSize,
                    MaxParticleSize = record.MaxParticleSize,
                    Flip = ToFloat3(record.Flip),
                    LocalToWorld = ToFloat4x4(record.LocalToWorldMatrix),
                    RendererColor = ToFloat4(record.RendererColor),
                });
                m_PendingRenderJobFlags |= (uint)ParticleRenderJobFlags.SharedData;
            }

            private void AddPerSharpGpuDataWorks(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record,
                uint sharedDataBits)
            {
                for (int dataIndex = 0; dataIndex < batch.PerSharpValueBufferInfos.Length; dataIndex++)
                {
                    VividParticleGpuBufferDataInfo bufferInfo = batch.PerSharpValueBufferInfos[dataIndex];
                    if ((sharedDataBits & bufferInfo.DataInfo.DataBit) == 0u)
                        continue;

                    if (!TryGetPerSharpGpuDataValue(record, bufferInfo.DataInfo, out float4 value))
                        continue;

                    m_SharedDataWorks.Add(new ParticleRenderSharedDataWork
                    {
                        BufferBase = bufferBase,
                        Kind = SharedDataWorkKindFloat4,
                        BatchDataOffset = batch.DataOffset,
                        ColumnByteOffset = bufferInfo.ByteOffset,
                        ElementStart = record.SharpIndex,
                        ElementCount = 1,
                        Value = value,
                    });
                    m_PendingRenderJobFlags |= (uint)ParticleRenderJobFlags.SharedData;
                }
            }

            private void AddSpanSharedDataWork(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record,
                int startIndex,
                int count)
            {
                if (!batch.HasSpanSharedDataBufferInfo)
                    return;

                if (!TryGetRecordElementCopyRange(
                    batch.SpanSharedDataBufferInfo.CopyDescriptor,
                    record,
                    startIndex,
                    count,
                    out int spanElementStart,
                    out int spanElementCount))
                    return;

                m_SharedDataWorks.Add(new ParticleRenderSharedDataWork
                {
                    BufferBase = bufferBase,
                    Kind = SharedDataWorkKindSpan,
                    BatchDataOffset = batch.DataOffset,
                    ColumnByteOffset = batch.SpanSharedDataBufferInfo.ByteOffset,
                    ElementStart = spanElementStart,
                    ElementCount = spanElementCount,
                    SharpIndex = record.SharpIndex,
                    SpanBaseIndex = record.SpanBaseIndex,
                    BatchBaseIndex = record.BatchBaseIndex,
                    ActiveCount = record.ActiveCount,
                    UsesPageBillboard = batch.UsesPageBillboard ? 1 : 0,
                });
                m_PendingRenderJobFlags |= (uint)ParticleRenderJobFlags.SharedData;
            }

            private static bool TryGetSharedGpuDataValue(
                VividParticleGpuDataInfo dataInfo,
                out float4 value)
            {
                value = dataInfo.UploadSegment switch
                {
                    InstanceUploadSegment.Rotation => new float4(0.0f, 0.0f, 0.0f, 1.0f),
                    InstanceUploadSegment.VelocityStretch => new float4(0.0f, 1.0f, 0.0f, 1.0f),
                    _ => default,
                };

                return dataInfo.UploadSegment is InstanceUploadSegment.Rotation
                    or InstanceUploadSegment.VelocityStretch;
            }

            private static bool TryGetPerSharpGpuDataValue(
                ParticleRenderRecord record,
                VividParticleGpuDataInfo dataInfo,
                out float4 value)
            {
                value = default;
                return record?.State != null
                    && record.State.TryGetPerSharpGpuDataValue(dataInfo.DataId, out value);
            }

            private static uint GetEntityIdLow(EntityId entityId)
            {
                ulong value = EntityId.ToULong(entityId);
                return (uint)(value & uint.MaxValue);
            }

            private static uint GetEntityIdHigh(EntityId entityId)
            {
                ulong value = EntityId.ToULong(entityId);
                return (uint)(value >> 32);
            }

            internal static bool ShouldCopyGpuDataForUploadWork(
                VividParticleGpuDataCopyDescriptor copyDescriptor,
                bool hasInstanceRange,
                bool hasSpanData,
                bool hasSharedData,
                int columnMask,
                uint sharedDataBits)
            {
                return copyDescriptor.Frequency switch
                {
                    VividParticleGpuDataFrequency.PerInstance => hasInstanceRange
                        && copyDescriptor.ColumnMask != 0
                        && (copyDescriptor.ColumnMask & columnMask) != 0,
                    VividParticleGpuDataFrequency.Span => hasSpanData,
                    VividParticleGpuDataFrequency.PerSharp => hasSharedData
                        && (sharedDataBits & copyDescriptor.DataBit) != 0u,
                    _ => false,
                };
            }

            private void AddGpuDataCopyWorks(
                ParticleRenderRecord record,
                VividParticleGpuDataCopyDescriptor[] copyDescriptors,
                int batchDataOffset,
                ParticleUploadRecordWork work,
                int instanceCount,
                int sharedStartIndex,
                int sharedCount)
            {
                bool hasInstanceRange = work.HasInstanceRange != 0 && instanceCount > 0;
                bool hasSpanData = work.HasSpanData != 0;
                bool hasSharedData = work.HasSharedData != 0;
                for (int dataIndex = 0; dataIndex < copyDescriptors.Length; dataIndex++)
                {
                    VividParticleGpuDataCopyDescriptor copyDescriptor = copyDescriptors[dataIndex];
                    if (!ShouldCopyGpuDataForUploadWork(
                            copyDescriptor,
                            hasInstanceRange,
                            hasSpanData,
                            hasSharedData,
                            work.ColumnMask,
                            work.SharedDataBits))
                    {
                        continue;
                    }

                    int startIndex = copyDescriptor.Frequency == VividParticleGpuDataFrequency.PerInstance
                        ? work.StartIndex
                        : sharedStartIndex;
                    int count = copyDescriptor.Frequency == VividParticleGpuDataFrequency.PerInstance
                        ? instanceCount
                        : copyDescriptor.Frequency == VividParticleGpuDataFrequency.PerSharp
                            ? record.ActiveCount
                            : sharedCount;
                    if (!TryGetRecordElementCopyRange(
                            copyDescriptor,
                            record,
                            startIndex,
                            count,
                            out int elementStart,
                            out int elementCount))
                    {
                        continue;
                    }

                    AddGpuDataCopyOperation(record, batchDataOffset, copyDescriptor, elementStart, elementCount);
                }
            }

            private static bool TryGetRecordElementCopyRange(
                VividParticleGpuDataCopyDescriptor copyDescriptor,
                ParticleRenderRecord record,
                int startIndex,
                int count,
                out int elementStart,
                out int elementCount)
            {
                elementStart = 0;
                elementCount = 0;
                if (record == null || count <= 0)
                    return false;

                switch (copyDescriptor.Frequency)
                {
                    case VividParticleGpuDataFrequency.PerInstance:
                        int clampedStart = Mathf.Clamp(startIndex, 0, Mathf.Max(0, record.ActiveCount));
                        int clampedEnd = Mathf.Clamp(startIndex + count, clampedStart, Mathf.Max(0, record.ActiveCount));
                        elementCount = clampedEnd - clampedStart;
                        if (elementCount <= 0)
                            return false;

                        elementStart = record.BatchBaseIndex + clampedStart;
                        return true;

                    case VividParticleGpuDataFrequency.PerSharp:
                        elementStart = record.SharpIndex;
                        elementCount = 1;
                        return true;

                    case VividParticleGpuDataFrequency.Span:
                        int spanStartIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, record.ActiveCount));
                        int spanEndIndex = Mathf.Clamp(startIndex + count, spanStartIndex, Mathf.Max(0, record.ActiveCount));
                        if (spanEndIndex <= spanStartIndex)
                            return false;

                        if (!UsesPageBillboardRenderMode(record.RenderMode))
                        {
                            elementStart = record.SpanBaseIndex + spanStartIndex;
                            elementCount = spanEndIndex - spanStartIndex;
                            return elementCount > 0;
                        }

                        int firstSpan = spanStartIndex / BillboardPageSize;
                        int spanEnd = (spanEndIndex + BillboardPageSize - 1) / BillboardPageSize;
                        elementCount = Mathf.Max(0, spanEnd - firstSpan);
                        if (elementCount <= 0)
                            return false;

                        elementStart = record.SpanBaseIndex + firstSpan;
                        return true;

                    default:
                        return false;
                }
            }

            private void AddGpuDataCopyOperation(
                ParticleRenderRecord owner,
                int batchDataOffset,
                VividParticleGpuDataCopyDescriptor copyDescriptor,
                int elementStart,
                int elementCount)
            {
                if (elementCount <= 0)
                    return;

                int byteCount = elementCount * copyDescriptor.ElementSize;
                int byteOffset = batchDataOffset
                    + copyDescriptor.ByteOffset
                    + elementStart * copyDescriptor.ElementSize;
                AddGpuDataCopyWork(owner, byteOffset, byteCount, copyDescriptor.DataBit);
            }

            private void AddGpuDataCopyWork(
                ParticleRenderRecord owner,
                int byteOffset,
                int byteCount,
                uint dataBit = 0u)
            {
                if (byteCount <= 0)
                    return;

                m_UploadCopyWorks.Add(new ParticleGpuDataCopyWork
                {
                    OwnerRecordSlot = owner != null ? owner.RecordSlot : -1,
                    OwnerRecordVersion = owner != null ? owner.RecordVersion : 0,
                    ByteOffset = byteOffset,
                    ByteCount = byteCount,
                    DataBit = dataBit,
                });
            }

            public void DrainCullingResults()
            {
                if (!m_HasPendingCullingOutput)
                    return;

                m_PendingCullingOutputHandle.Complete();
                m_PendingCullingOutputHandle = default;
                m_HasPendingCullingOutput = false;
            }

            public int[] GetMeshVisibleCountsSnapshot()
            {
                if (!m_MeshVisibleCounts.IsCreated || m_MeshVisibleCountLength <= 0)
                    return Array.Empty<int>();

                int count = Mathf.Min(m_MeshVisibleCountLength, m_MeshVisibleCounts.Length);
                int[] snapshot = new int[count];
                for (int index = 0; index < count; index++)
                    snapshot[index] = m_MeshVisibleCounts[index];

                return snapshot;
            }
            
            [BurstCompile]
            private unsafe JobHandle OnPerformCulling(
                BatchRendererGroup rendererGroup,
                BatchCullingContext cullingContext,
                BatchCullingOutput cullingOutput,
                IntPtr userContext)
            {
                if (cullingContext.viewType == BatchCullingViewType.Light && !m_AnyShadowCastingBatch)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                if (cullingContext.viewType == BatchCullingViewType.SelectionOutline && !m_AnySelectedRecord)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                SelectCullingDrawLayout(
                    cullingContext.viewType,
                    out NativeArray<ParticleDrawCommandInput> commands,
                    out NativeArray<ParticleDrawRangeInput> ranges,
                    out int visibleInstanceCount,
                    out int sortingPositionCount,
                    out uint commandLayerMask);
                NativeArray<ParticleCullingRecord> cullingRecords = m_NativeCullingRecords.IsCreated
                    ? m_NativeCullingRecords.AsArray()
                    : default;
                int sourceDrawCommandCount = commands.IsCreated ? commands.Length : 0;
                int sourceDrawRangeCount = ranges.IsCreated ? ranges.Length : 0;
                bool writesPickingEntityIds = ShouldWritePickingEntityIdsForView(cullingContext.viewType);
                if (sourceDrawCommandCount <= 0
                    || sourceDrawRangeCount <= 0
                    || visibleInstanceCount <= 0
                    || !cullingRecords.IsCreated
                    || cullingRecords.Length <= 0)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                int drawCommandCount;
                int drawRangeCount;
                int filteredVisibleInstanceCount;
                int filteredSortingPositionCount;
                if (!HasAnyVisibleCommandLayer(cullingContext.cullingLayerMask, commandLayerMask))
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                bool canUseSourceDrawLayout = CanUseUnfilteredDrawLayout(
                    cullingContext.cullingLayerMask,
                    commandLayerMask);
                if (canUseSourceDrawLayout)
                {
                    drawCommandCount = sourceDrawCommandCount;
                    drawRangeCount = sourceDrawRangeCount;
                    filteredVisibleInstanceCount = visibleInstanceCount;
                    filteredSortingPositionCount = sortingPositionCount;
                }
                else
                {
                    CountFilteredDrawLayout(
                        commands,
                        cullingContext.cullingLayerMask,
                        cullingContext.viewType,
                        out drawCommandCount,
                        out drawRangeCount,
                        out filteredVisibleInstanceCount,
                        out filteredSortingPositionCount);
                }

                if (drawCommandCount <= 0 || drawRangeCount <= 0 || filteredVisibleInstanceCount <= 0)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                NativeArray<ParticleDrawCommandInput> outputCommands = commands;
                NativeArray<ParticleDrawRangeInput> outputRanges = ranges;
                bool ownsFilteredDrawLayout = !canUseSourceDrawLayout
                    && ShouldBuildFilteredDrawLayout(
                        sourceDrawCommandCount,
                        sourceDrawRangeCount,
                        visibleInstanceCount,
                        sortingPositionCount,
                        drawCommandCount,
                        drawRangeCount,
                        filteredVisibleInstanceCount,
                        filteredSortingPositionCount);
                if (ownsFilteredDrawLayout)
                {
                    outputCommands = new NativeArray<ParticleDrawCommandInput>(
                        drawCommandCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                    outputRanges = new NativeArray<ParticleDrawRangeInput>(
                        drawRangeCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                    FillFilteredDrawLayout(
                        commands,
                        cullingContext.cullingLayerMask,
                        cullingContext.viewType,
                        outputCommands,
                        outputRanges);
                }

                bool hasSortingPositions = ShouldWriteSortingPositionsForView(cullingContext.viewType)
                    && filteredSortingPositionCount > 0;
                var draws = new BatchCullingOutputDrawCommands
                {
                    drawCommandCount = drawCommandCount,
                    drawRangeCount = drawRangeCount,
                    visibleInstanceCount = filteredVisibleInstanceCount,
                    drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawCommand>() * drawCommandCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawRange>() * drawRangeCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    visibleInstances = (int*)UnsafeUtility.Malloc(
                        sizeof(int) * filteredVisibleInstanceCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawCommandPickingEntityIds = writesPickingEntityIds
                        ? (EntityId*)UnsafeUtility.Malloc(
                            UnsafeUtility.SizeOf<EntityId>() * drawCommandCount,
                            UnsafeUtility.AlignOf<long>(),
                            Allocator.TempJob)
                        : null,
                    instanceSortingPositions = hasSortingPositions
                        ? (float*)UnsafeUtility.Malloc(
                            sizeof(float) * GetSortingPositionFloatCount(filteredSortingPositionCount),
                            UnsafeUtility.AlignOf<long>(),
                            Allocator.TempJob)
                        : null,
                    instanceSortingPositionFloatCount = hasSortingPositions
                        ? GetSortingPositionFloatCount(filteredSortingPositionCount)
                        : 0,
                };

                cullingOutput.drawCommands[0] = draws;

                NativeArray<ParticleCullingSplit> cullingSplits = CreatePackedCullingData(
                    cullingContext,
                    out NativeArray<ParticleCullingPlanePacket4> cullingPlanePackets);
                ParticlePickingIncludeExcludeFilter pickingFilter = CreatePickingIncludeExcludeFilter(
                    cullingContext,
                    Allocator.TempJob);

                var job = new ParticleDrawCommandOutputJob
                {
                    Commands = outputCommands,
                    Ranges = outputRanges,
                    CullingRecords = cullingRecords,
                    CullingPlanePackets = cullingPlanePackets,
                    CullingSplits = cullingSplits,
                    CullingLayerMask = cullingContext.cullingLayerMask,
                    SceneCullingMask = cullingContext.sceneCullingMask,
                    ViewType = (int)cullingContext.viewType,
                    DrawCommands = draws.drawCommands,
                    DrawRanges = draws.drawRanges,
                    VisibleInstances = draws.visibleInstances,
                    InstanceSortingPositions = draws.instanceSortingPositions,
                    DrawCommandPickingEntityIds = draws.drawCommandPickingEntityIds,
                    PickingIncludeRenderers = GetEntityIdPointer(pickingFilter.IncludeRenderers),
                    PickingIncludeRendererCount = GetEntityIdCount(pickingFilter.IncludeRenderers),
                    PickingIncludeEntities = GetEntityIdPointer(pickingFilter.IncludeEntities),
                    PickingIncludeEntityCount = GetEntityIdCount(pickingFilter.IncludeEntities),
                    PickingExcludeRenderers = GetEntityIdPointer(pickingFilter.ExcludeRenderers),
                    PickingExcludeRendererCount = GetEntityIdCount(pickingFilter.ExcludeRenderers),
                    PickingExcludeEntities = GetEntityIdPointer(pickingFilter.ExcludeEntities),
                    PickingExcludeEntityCount = GetEntityIdCount(pickingFilter.ExcludeEntities),
                    PickingIncludeEnabled = pickingFilter.IncludeEnabled,
                    PickingFilterEnabled = pickingFilter.FilterEnabled,
                };
                JobHandle outputHandle = job.Schedule(drawCommandCount, 4);
                JobHandle disposePlanesHandle = cullingPlanePackets.Dispose(outputHandle);
                JobHandle disposeSplitsHandle = cullingSplits.Dispose(outputHandle);
                JobHandle combinedHandle = JobHandle.CombineDependencies(disposePlanesHandle, disposeSplitsHandle);
                combinedHandle = JobHandle.CombineDependencies(
                    combinedHandle,
                    DisposePickingIncludeExcludeFilter(pickingFilter, outputHandle));
                if (ownsFilteredDrawLayout)
                {
                    JobHandle disposeCommandsHandle = outputCommands.Dispose(outputHandle);
                    JobHandle disposeRangesHandle = outputRanges.Dispose(outputHandle);
                    combinedHandle = JobHandle.CombineDependencies(
                        combinedHandle,
                        JobHandle.CombineDependencies(disposeCommandsHandle, disposeRangesHandle));
                }

                m_PendingCullingOutputHandle = m_HasPendingCullingOutput
                    ? JobHandle.CombineDependencies(m_PendingCullingOutputHandle, combinedHandle)
                    : combinedHandle;
                m_HasPendingCullingOutput = true;
                return combinedHandle;
            }

            private static ulong* GetEntityIdPointer(NativeArray<ulong> entityIds)
            {
                return entityIds.IsCreated && entityIds.Length > 0
                    ? (ulong*)entityIds.GetUnsafeReadOnlyPtr()
                    : null;
            }

            private static int GetEntityIdCount(NativeArray<ulong> entityIds)
            {
                return entityIds.IsCreated ? entityIds.Length : 0;
            }

            private static ParticlePickingIncludeExcludeFilter CreatePickingIncludeExcludeFilter(
                BatchCullingContext cullingContext,
                Allocator allocator)
            {
#if UNITY_EDITOR
                if (!ShouldWritePickingEntityIdsForView(cullingContext.viewType))
                    return default;

                var includeExcludeList = cullingContext.viewType == BatchCullingViewType.Picking
                    ? HandleUtility.GetPickingIncludeExcludeEntityIdList(Allocator.Temp)
                    : HandleUtility.GetSelectionOutlineIncludeExcludeEntityIdList(Allocator.Temp);
                try
                {
                    NativeArray<ulong> includeRenderers =
                        CopyEntityIdListToUInt64(includeExcludeList.IncludeRenderers, allocator);
                    NativeArray<ulong> includeEntities =
                        CopyEntityIdListToUInt64(includeExcludeList.IncludeEntities, allocator);
                    NativeArray<ulong> excludeRenderers =
                        CopyEntityIdListToUInt64(includeExcludeList.ExcludeRenderers, allocator);
                    NativeArray<ulong> excludeEntities =
                        CopyEntityIdListToUInt64(includeExcludeList.ExcludeEntities, allocator);
                    int includeEnabled =
                        cullingContext.viewType == BatchCullingViewType.SelectionOutline
                        || includeRenderers.IsCreated
                        || includeEntities.IsCreated
                            ? 1
                            : 0;
                    return new ParticlePickingIncludeExcludeFilter
                    {
                        IncludeRenderers = includeRenderers,
                        IncludeEntities = includeEntities,
                        ExcludeRenderers = excludeRenderers,
                        ExcludeEntities = excludeEntities,
                        IncludeEnabled = includeEnabled,
                        FilterEnabled = includeEnabled != 0
                            || excludeRenderers.IsCreated
                            || excludeEntities.IsCreated
                                ? 1
                                : 0,
                    };
                }
                finally
                {
                    includeExcludeList.Dispose();
                }
#else
                return default;
#endif
            }

            private static NativeArray<ulong> CopyEntityIdListToUInt64(
                NativeArray<EntityId> entityIds,
                Allocator allocator)
            {
#if UNITY_EDITOR
                if (!entityIds.IsCreated || entityIds.Length <= 0)
                    return default;

                var copy = new NativeArray<ulong>(
                    entityIds.Length,
                    allocator,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < entityIds.Length; index++)
                    copy[index] = EntityId.ToULong(entityIds[index]);

                return copy;
#else
                return default;
#endif
            }

            private static JobHandle DisposePickingIncludeExcludeFilter(
                ParticlePickingIncludeExcludeFilter filter,
                JobHandle dependency)
            {
                JobHandle handle = dependency;
                if (filter.IncludeRenderers.IsCreated)
                    handle = JobHandle.CombineDependencies(handle, filter.IncludeRenderers.Dispose(dependency));
                if (filter.IncludeEntities.IsCreated)
                    handle = JobHandle.CombineDependencies(handle, filter.IncludeEntities.Dispose(dependency));
                if (filter.ExcludeRenderers.IsCreated)
                    handle = JobHandle.CombineDependencies(handle, filter.ExcludeRenderers.Dispose(dependency));
                if (filter.ExcludeEntities.IsCreated)
                    handle = JobHandle.CombineDependencies(handle, filter.ExcludeEntities.Dispose(dependency));

                return handle;
            }

            private static bool ShouldBuildFilteredDrawLayout(
                int sourceDrawCommandCount,
                int sourceDrawRangeCount,
                int sourceVisibleInstanceCount,
                int sourceSortingPositionCount,
                int filteredDrawCommandCount,
                int filteredDrawRangeCount,
                int filteredVisibleInstanceCount,
                int filteredSortingPositionCount)
            {
                return sourceDrawCommandCount != filteredDrawCommandCount
                    || sourceDrawRangeCount != filteredDrawRangeCount
                    || sourceVisibleInstanceCount != filteredVisibleInstanceCount
                    || sourceSortingPositionCount != filteredSortingPositionCount;
            }

            private static void FillFilteredDrawLayout(
                NativeArray<ParticleDrawCommandInput> sourceCommands,
                uint cullingLayerMask,
                BatchCullingViewType viewType,
                NativeArray<ParticleDrawCommandInput> outputCommands,
                NativeArray<ParticleDrawRangeInput> outputRanges)
            {
                int commandIndex = 0;
                int rangeIndex = 0;
                int visibleOffset = 0;
                int sortingPositionOffset = 0;
                ParticleDrawRangeInput lastRange = default;
                for (int sourceIndex = 0; sourceIndex < sourceCommands.Length; sourceIndex++)
                {
                    ParticleDrawCommandInput command = sourceCommands[sourceIndex];
                    if (!ShouldKeepDrawCommandForCulling(
                            cullingLayerMask,
                            command.Layer,
                            command.RecordCount,
                            command.MaxVisibleCount,
                            command.ShadowCastingMode,
                            viewType))
                    {
                        continue;
                    }

                    command.VisibleOffset = visibleOffset;
                    command.SortingPositionOffset = command.HasSortingPositions != 0 ? sortingPositionOffset : 0;
                    outputCommands[commandIndex] = command;

                    ParticleDrawRangeInput nextRange =
                        VividParticleSystemManager.CreateDrawRangeInput(command, commandIndex, drawCommandCount: 1);
                    if (rangeIndex > 0 && VividParticleSystemManager.CanMergeDrawRanges(lastRange, nextRange, commandIndex))
                    {
                        lastRange.DrawCommandsCount++;
                        outputRanges[rangeIndex - 1] = lastRange;
                    }
                    else
                    {
                        lastRange = nextRange;
                        outputRanges[rangeIndex++] = nextRange;
                    }

                    visibleOffset += command.MaxVisibleCount;
                    if (command.HasSortingPositions != 0)
                        sortingPositionOffset += command.MaxVisibleCount;

                    commandIndex++;
                }
            }

            private void SelectCullingDrawLayout(
                BatchCullingViewType viewType,
                out NativeArray<ParticleDrawCommandInput> commands,
                out NativeArray<ParticleDrawRangeInput> ranges,
                out int visibleInstanceCount,
                out int sortingPositionCount,
                out uint commandLayerMask)
            {
                sortingPositionCount = 0;
                commandLayerMask = 0u;
                if (viewType == BatchCullingViewType.Picking)
                {
                    commands = m_NativePickingDrawCommandInputs.IsCreated
                        ? m_NativePickingDrawCommandInputs.AsArray()
                        : default;
                    ranges = m_NativePickingDrawRangeInputs.IsCreated
                        ? m_NativePickingDrawRangeInputs.AsArray()
                        : default;
                    visibleInstanceCount = m_NativePickingVisibleInstanceCapacity;
                    commandLayerMask = m_NativePickingDrawCommandLayerMask;
                    return;
                }

                if (viewType == BatchCullingViewType.Light)
                {
                    commands = m_NativeLightDrawCommandInputs.IsCreated
                        ? m_NativeLightDrawCommandInputs.AsArray()
                        : default;
                    ranges = m_NativeLightDrawRangeInputs.IsCreated
                        ? m_NativeLightDrawRangeInputs.AsArray()
                        : default;
                    visibleInstanceCount = m_NativeLightVisibleInstanceCapacity;
                    commandLayerMask = m_NativeLightDrawCommandLayerMask;
                    return;
                }

                if (viewType == BatchCullingViewType.SelectionOutline)
                {
                    commands = m_NativeSelectionDrawCommandInputs.IsCreated
                        ? m_NativeSelectionDrawCommandInputs.AsArray()
                        : default;
                    ranges = m_NativeSelectionDrawRangeInputs.IsCreated
                        ? m_NativeSelectionDrawRangeInputs.AsArray()
                        : default;
                    visibleInstanceCount = m_NativeSelectionVisibleInstanceCapacity;
                    commandLayerMask = m_NativeSelectionDrawCommandLayerMask;
                    return;
                }

                commands = m_NativeDrawCommandInputs.IsCreated ? m_NativeDrawCommandInputs.AsArray() : default;
                ranges = m_NativeDrawRangeInputs.IsCreated ? m_NativeDrawRangeInputs.AsArray() : default;
                visibleInstanceCount = m_NativeVisibleInstanceCapacity;
                sortingPositionCount = m_NativeSortingPositionCapacity;
                commandLayerMask = m_NativeDrawCommandLayerMask;
            }

            private static uint GetLayerBit(int layer)
            {
                return 1u << Mathf.Clamp(layer, 0, 31);
            }

            private static ParticleDrawCommandInput CreateDrawCommandInput(
                ParticleDrawBatch batch,
                int recordStart,
                int recordCount,
                int visibleOffset,
                int maxVisibleCount,
                int sortingPositionOffset,
                int layer,
                EntityId pickingEntityId,
                bool requiresSortingPositions,
                int meshIndexFilter = -1)
            {
                return new ParticleDrawCommandInput
                {
                    RecordStart = recordStart,
                    RecordCount = Mathf.Max(0, recordCount),
                    VisibleOffset = Mathf.Max(0, visibleOffset),
                    MaxVisibleCount = Mathf.Max(0, maxVisibleCount),
                    SortingPositionOffset = Mathf.Max(0, sortingPositionOffset),
                    Layer = layer,
                    SubmeshIndex = 0,
                    ActiveMeshLod = 0,
                    RendererPriority = batch.Material != null ? batch.Material.renderQueue : 0,
                    RenderingLayerMask = batch.Key.RenderingLayerMask,
                    SceneCullingMask = 0,
                    DrawFlags = ResolveParticleDrawCommandFlags(requiresSortingPositions, hasMotion: false),
                    HasSortingPositions = requiresSortingPositions ? 1 : 0,
                    ShadowCastingMode = batch.ShadowCastingMode,
                    MotionMode = MotionVectorGenerationMode.ForceNoMotion,
                    ReceiveShadows = batch.ReceiveShadows ? 1 : 0,
                    StaticShadowCaster = 0,
                    AllDepthSorted = ResolveAllDepthSortedFlag(requiresSortingPositions),
                    BatchId = batch.BatchId,
                    MeshId = GetBatchMeshId(batch, meshIndexFilter),
                    MaterialId = batch.MaterialId,
                    PickingEntityId = pickingEntityId,
                    PickingEntityIdLow = GetEntityIdLow(pickingEntityId),
                    PickingEntityIdHigh = GetEntityIdHigh(pickingEntityId),
                    MeshIndexFilter = meshIndexFilter,
                    MeshCount = GetBatchDrawMeshCommandCount(batch),
                };
            }

            private static BatchMeshID GetBatchMeshId(ParticleDrawBatch batch, int meshIndex)
            {
                if (batch == null)
                    return BatchMeshID.Null;

                BatchMeshID[] meshIds = batch.MeshIds;
                int meshCount = meshIds?.Length ?? 0;
                if (meshCount <= 0)
                    return batch.MeshId;

                int resolvedMeshIndex = ResolveMeshIndexSlot(meshIndex, meshCount);
                return meshIds[resolvedMeshIndex];
            }

            private static int GetBatchDrawMeshCommandCount(ParticleDrawBatch batch)
            {
                if (batch == null || batch.UsesPageBillboard)
                    return 1;

                return Mathf.Max(1, batch.MeshIds?.Length ?? batch.Meshes?.Length ?? 1);
            }

            private static int ResolveMeshIndexFilter(ParticleDrawBatch batch, int meshCommandIndex)
            {
                return batch == null || batch.UsesPageBillboard
                    ? -1
                    : Mathf.Max(0, meshCommandIndex);
            }

            private int GetRecordVisibleInstanceCapacity(
                ParticleRenderRecord record,
                ParticleDrawBatch batch,
                int meshIndexFilter)
            {
                if (record == null)
                    return 0;

                int meshCount = GetBatchDrawMeshCommandCount(batch);
                if (meshIndexFilter < 0 || meshCount <= 1)
                    return GetVisibleInstanceCount(record.RenderMode, record.ActiveCount);

                if (record.MeshVisibleCountOffset < 0
                    || record.MeshVisibleCountCount <= meshIndexFilter
                    || !m_MeshVisibleCounts.IsCreated
                    || record.MeshVisibleCountOffset + meshIndexFilter >= m_MeshVisibleCounts.Length)
                {
                    return meshIndexFilter == 0 ? record.ActiveCount : 0;
                }

                return m_MeshVisibleCounts[record.MeshVisibleCountOffset + meshIndexFilter];
            }

            private static int GetCachedBatchVisibleInstanceCapacity(
                ParticleDrawBatch batch,
                int meshCommandIndex)
            {
                if (batch == null || batch.VisibleInstanceCapacities == null)
                    return 0;

                if ((uint)meshCommandIndex >= (uint)batch.VisibleInstanceCapacities.Length)
                    return 0;

                return batch.VisibleInstanceCapacities[meshCommandIndex];
            }

            private static int GetCachedRecordVisibleInstanceCapacity(
                ParticleRenderRecord record,
                int meshCommandIndex)
            {
                if (record == null || record.VisibleInstanceCapacities == null)
                    return 0;

                if ((uint)meshCommandIndex >= (uint)record.VisibleInstanceCapacities.Length)
                    return 0;

                return record.VisibleInstanceCapacities[meshCommandIndex];
            }

            private static bool TryGetBatchCullingRecordRange(
                ParticleDrawBatch batch,
                out int recordStart,
                out int recordCount)
            {
                recordStart = batch?.CullingRecordStart ?? -1;
                recordCount = batch?.CullingRecordCount ?? 0;
                if (recordStart < 0 || recordCount <= 0)
                {
                    recordStart = 0;
                    recordCount = 0;
                    return false;
                }

                return true;
            }

            private static void AddDrawCommandWithRange(
                NativeList<ParticleDrawCommandInput> commands,
                NativeList<ParticleDrawRangeInput> ranges,
                ParticleDrawCommandInput command)
            {
                int commandIndex = commands.Length;
                commands.Add(command);
                ParticleDrawRangeInput nextRange =
                    VividParticleSystemManager.CreateDrawRangeInput(command, commandIndex, drawCommandCount: 1);
                if (ranges.Length > 0)
                {
                    int lastRangeIndex = ranges.Length - 1;
                    ParticleDrawRangeInput lastRange = ranges[lastRangeIndex];
                    if (VividParticleSystemManager.CanMergeDrawRanges(lastRange, nextRange, commandIndex))
                    {
                        lastRange.DrawCommandsCount++;
                        ranges[lastRangeIndex] = lastRange;
                        return;
                    }
                }

                ranges.Add(nextRange);
            }

            private static NativeArray<ParticleCullingSplit> CreatePackedCullingData(
                BatchCullingContext cullingContext,
                out NativeArray<ParticleCullingPlanePacket4> planePackets)
            {
                NativeArray<Plane> sourcePlanes = cullingContext.cullingPlanes;
                int sourcePlaneCount = sourcePlanes.IsCreated ? sourcePlanes.Length : 0;
                var sourceSplits = cullingContext.cullingSplits;
                int splitCount = sourceSplits.IsCreated ? sourceSplits.Length : 0;
                using var receiverPlanes = new NativeList<float4>(Allocator.Temp);
                AddBackfacingReceiverPlanes(cullingContext, sourcePlanes, receiverPlanes);

                if (splitCount <= 0 && (sourcePlaneCount > 0 || receiverPlanes.Length > 0))
                    splitCount = 1;

                if (splitCount <= 0)
                {
                    planePackets = new NativeArray<ParticleCullingPlanePacket4>(
                        0,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                    return new NativeArray<ParticleCullingSplit>(
                        0,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                }

                var splitPlaneCounts = new NativeArray<int>(
                    splitCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                try
                {
                    int packetCount = 0;
                    for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
                    {
                        int basePlaneCount = sourceSplits.IsCreated && splitIndex < sourceSplits.Length
                            ? sourceSplits[splitIndex].cullingPlaneCount
                            : sourcePlaneCount;
                        int totalPlaneCount = basePlaneCount + receiverPlanes.Length;
                        splitPlaneCounts[splitIndex] = totalPlaneCount;
                        packetCount += (totalPlaneCount + 3) / 4;
                    }

                    var splits = new NativeArray<ParticleCullingSplit>(
                        splitCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                    planePackets = new NativeArray<ParticleCullingPlanePacket4>(
                        packetCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);

                    int packetOffset = 0;
                    for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
                    {
                        int basePlaneOffset = sourceSplits.IsCreated && splitIndex < sourceSplits.Length
                            ? sourceSplits[splitIndex].cullingPlaneOffset
                            : 0;
                        int basePlaneCount = sourceSplits.IsCreated && splitIndex < sourceSplits.Length
                            ? sourceSplits[splitIndex].cullingPlaneCount
                            : sourcePlaneCount;
                        int splitPacketCount = (splitPlaneCounts[splitIndex] + 3) / 4;
                        splits[splitIndex] = new ParticleCullingSplit
                        {
                            PacketOffset = packetOffset,
                            PacketCount = splitPacketCount,
                        };

                        for (int packetIndex = 0; packetIndex < splitPacketCount; packetIndex++)
                        {
                            planePackets[packetOffset + packetIndex] = CreatePlanePacket4(
                                sourcePlanes,
                                basePlaneOffset,
                                basePlaneCount,
                                receiverPlanes,
                                packetIndex * 4);
                        }

                        packetOffset += splitPacketCount;
                    }

                    return splits;
                }
                finally
                {
                    splitPlaneCounts.Dispose();
                }
            }

            private static void AddBackfacingReceiverPlanes(
                BatchCullingContext cullingContext,
                NativeArray<Plane> sourcePlanes,
                NativeList<float4> receiverPlanes)
            {
                if (cullingContext.viewType != BatchCullingViewType.Light
                    || cullingContext.receiverPlaneCount <= 0
                    || !sourcePlanes.IsCreated)
                {
                    return;
                }

                bool isOrthographic = cullingContext.projectionType == BatchCullingProjectionType.Orthographic;
                Vector4 lightForwardColumn = cullingContext.localToWorldMatrix.GetColumn(2);
                float3 lightDirection = new(lightForwardColumn.x, lightForwardColumn.y, lightForwardColumn.z);
                Vector4 lightPositionColumn = cullingContext.localToWorldMatrix.GetColumn(3);
                float3 lightPosition = new(lightPositionColumn.x, lightPositionColumn.y, lightPositionColumn.z);
                int receiverEnd = math.min(
                    sourcePlanes.Length,
                    cullingContext.receiverPlaneOffset + cullingContext.receiverPlaneCount);
                for (int planeIndex = math.max(0, cullingContext.receiverPlaneOffset); planeIndex < receiverEnd; planeIndex++)
                {
                    float4 plane = PlaneToFloat4(sourcePlanes[planeIndex]);
                    float3 normal = plane.xyz;
                    const float epsilon = 1e-12f;
                    bool isBackfacing = isOrthographic
                        ? math.dot(normal, lightDirection) < -epsilon
                        : math.dot(normal, lightPosition) + plane.w > 0.0f;
                    if (isBackfacing)
                        receiverPlanes.Add(plane);
                }
            }

            private static ParticleCullingPlanePacket4 CreatePlanePacket4(
                NativeArray<Plane> sourcePlanes,
                int basePlaneOffset,
                int basePlaneCount,
                NativeList<float4> receiverPlanes,
                int firstPlaneIndex)
            {
                float4 normalX = default;
                float4 normalY = default;
                float4 normalZ = default;
                float4 distance = default;
                for (int lane = 0; lane < 4; lane++)
                {
                    float4 plane = GetCombinedPlane(
                        sourcePlanes,
                        basePlaneOffset,
                        basePlaneCount,
                        receiverPlanes,
                        firstPlaneIndex + lane);
                    normalX[lane] = plane.x;
                    normalY[lane] = plane.y;
                    normalZ[lane] = plane.z;
                    distance[lane] = plane.w;
                }

                return new ParticleCullingPlanePacket4
                {
                    NormalX = normalX,
                    NormalY = normalY,
                    NormalZ = normalZ,
                    Distance = distance,
                };
            }

            private static float4 GetCombinedPlane(
                NativeArray<Plane> sourcePlanes,
                int basePlaneOffset,
                int basePlaneCount,
                NativeList<float4> receiverPlanes,
                int planeIndex)
            {
                if (planeIndex < basePlaneCount)
                    return PlaneToFloat4(sourcePlanes[basePlaneOffset + planeIndex]);

                int receiverIndex = planeIndex - basePlaneCount;
                if (receiverIndex < receiverPlanes.Length)
                    return receiverPlanes[receiverIndex];

                return new float4(0.0f, 0.0f, 0.0f, 1.0e20f);
            }

            private static float4 PlaneToFloat4(Plane plane)
            {
                Vector3 normal = plane.normal;
                return new float4(normal.x, normal.y, normal.z, plane.distance);
            }

            private static int AlignTo16(int value)
            {
                return (value + 15) & ~15;
            }

            private static void WriteEmptyDrawCommands(BatchCullingOutput cullingOutput)
            {
                cullingOutput.drawCommands[0] = new BatchCullingOutputDrawCommands();
            }
        }

        private unsafe sealed class VividParticleGPUBuffer : IDisposable
        {
            private const int BufferCount = InstanceDataBufferCount;
            private const int MaxThreadGroupsPerDispatch = 65535;
            private const string CopyShaderResourceName = "VividParticleBufferCopy";
            private const string CopyKernelName = "CopyParticleBufferRanges";

            private static readonly int s_CopySrcBufferId = Shader.PropertyToID("_VividParticleUploadSrc");
            private static readonly int s_CopyDstBufferId = Shader.PropertyToID("_VividParticleUploadDst");
            private static readonly int s_CopyOperationsId = Shader.PropertyToID("_VividParticleUploadOperations");
            private static readonly int s_CopyOperationCountId = Shader.PropertyToID("_VividParticleUploadOperationCount");
            private static readonly int s_CopyOperationBaseId = Shader.PropertyToID("_VividParticleUploadOperationBase");

            private readonly GraphicsBuffer[] m_StagingBuffers = new GraphicsBuffer[BufferCount];
            private NativeList<UploadOperation> m_CopyOperations;
            private ComputeBuffer m_CopyOperationBuffer;
            private ComputeShader m_CopyShader;
            private GraphicsBuffer m_PersistentBuffer;
            private NativeArray<int> m_MappedData;
            private int m_CopyKernel = -1;
            private int m_BufferSizeInBytes;
            private int m_BufferIndex = -1;
            private int m_LastLockCount;
            private int m_LastCopyOperationCount;
            private int m_LastCopyByteCount;
            private bool m_UsesComputeDelta;

            public int bufferIndex => m_BufferIndex;

            public bool usesComputeDelta => m_UsesComputeDelta;

            public GraphicsBuffer renderBuffer => m_PersistentBuffer;

            public int lastLockCount => m_LastLockCount;

            public int lastCopyOperationCount => m_LastCopyOperationCount;

            public int lastCopyByteCount => m_LastCopyByteCount;

            public void ResetLastUploadStats()
            {
                m_LastLockCount = 0;
                m_LastCopyOperationCount = 0;
                m_LastCopyByteCount = 0;
            }

            public bool EnsureCapacity(int requiredByteSize)
            {
                requiredByteSize = Mathf.Max(sizeof(int), requiredByteSize);
                if (m_PersistentBuffer != null
                    && m_PersistentBuffer.IsValid()
                    && m_BufferSizeInBytes >= requiredByteSize)
                {
                    return false;
                }

                ReleaseBuffers();
                m_BufferSizeInBytes = AlignTo16(requiredByteSize);
                EnsureCopyShader();

                if (m_UsesComputeDelta)
                {
                    for (int index = 0; index < m_StagingBuffers.Length; index++)
                    {
                        m_StagingBuffers[index] = new GraphicsBuffer(
                            GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource,
                            GraphicsBuffer.UsageFlags.LockBufferForWrite,
                            BufferCountForBytes(m_BufferSizeInBytes),
                            sizeof(int));
                    }

                    m_PersistentBuffer = new GraphicsBuffer(
                        GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination,
                        GraphicsBuffer.UsageFlags.None,
                        BufferCountForBytes(m_BufferSizeInBytes),
                        sizeof(int));
                }
                else
                {
                    m_PersistentBuffer = new GraphicsBuffer(
                        ResolveBufferTarget(),
                        GraphicsBuffer.UsageFlags.LockBufferForWrite,
                        BufferCountForBytes(m_BufferSizeInBytes),
                        sizeof(int));
                }

                m_BufferIndex = -1;
                return true;
            }

            public byte* BeginWrite()
            {
                if (m_PersistentBuffer == null || !m_PersistentBuffer.IsValid())
                    return null;

                m_CopyOperations.Clear();
                ResetLastUploadStats();
                m_LastLockCount = 1;
                GraphicsBuffer writeBuffer = m_PersistentBuffer;
                if (m_UsesComputeDelta)
                {
                    m_BufferIndex = (m_BufferIndex + 1) % BufferCount;
                    writeBuffer = m_StagingBuffers[m_BufferIndex];
                }
                else if (m_BufferIndex < 0)
                {
                    m_BufferIndex = 0;
                }

                m_MappedData = writeBuffer.LockBufferForWrite<int>(0, BufferCountForBytes(m_BufferSizeInBytes));
                return (byte*)m_MappedData.GetUnsafePtr();
            }

            public void AddCopyOperation(int srcOffset, int dstOffset, int size)
            {
                if (size <= 0)
                    return;

                int alignedSize = AlignTo4(size);
                if (TryMergeLastCopyOperation(srcOffset, dstOffset, alignedSize))
                {
                    m_LastCopyByteCount += alignedSize;
                    return;
                }

                m_CopyOperations.Add(new UploadOperation
                {
                    SrcOffset = (uint)srcOffset,
                    DstOffset = (uint)dstOffset,
                    Size = (uint)alignedSize,
                });
                m_LastCopyOperationCount++;
                m_LastCopyByteCount += alignedSize;
            }

            private bool TryMergeLastCopyOperation(int srcOffset, int dstOffset, int size)
            {
                if (!m_CopyOperations.IsCreated || m_CopyOperations.Length <= 0)
                    return false;

                int lastIndex = m_CopyOperations.Length - 1;
                UploadOperation last = m_CopyOperations[lastIndex];
                uint src = (uint)srcOffset;
                uint dst = (uint)dstOffset;
                if (!CanMergeUploadCopyOperations(
                        (int)last.SrcOffset,
                        (int)last.DstOffset,
                        (int)last.Size,
                        (int)src,
                        (int)dst))
                {
                    return false;
                }

                last.Size += (uint)size;
                m_CopyOperations[lastIndex] = last;
                return true;
            }

            public void EndWrite()
            {
                if (m_MappedData.IsCreated)
                {
                    GraphicsBuffer writeBuffer = m_UsesComputeDelta
                        ? m_StagingBuffers[m_BufferIndex]
                        : m_PersistentBuffer;
                    writeBuffer.UnlockBufferAfterWrite<int>(BufferCountForBytes(m_BufferSizeInBytes));
                    m_MappedData = default;
                }

                if (m_UsesComputeDelta && m_CopyOperations.IsCreated && m_CopyOperations.Length > 0)
                    CopyDirtyRanges();
            }

            public void Dispose()
            {
                if (m_MappedData.IsCreated)
                {
                    GraphicsBuffer writeBuffer = m_UsesComputeDelta && m_BufferIndex >= 0
                        ? m_StagingBuffers[m_BufferIndex]
                        : m_PersistentBuffer;
                    writeBuffer?.UnlockBufferAfterWrite<int>(BufferCountForBytes(m_BufferSizeInBytes));
                    m_MappedData = default;
                }

                ReleaseBuffers();
                if (m_CopyOperations.IsCreated)
                    m_CopyOperations.Dispose();

                m_CopyOperationBuffer?.Release();
                m_CopyOperationBuffer = null;
                m_CopyShader = null;
                m_CopyKernel = -1;
                m_BufferSizeInBytes = 0;
                m_BufferIndex = -1;
                ResetLastUploadStats();
                m_UsesComputeDelta = false;
            }

            private void EnsureCopyShader()
            {
                if (!m_CopyOperations.IsCreated)
                    m_CopyOperations = new NativeList<UploadOperation>(16, Allocator.Persistent);

                m_CopyShader = Resources.Load<ComputeShader>(CopyShaderResourceName);
                m_CopyKernel = -1;
                m_UsesComputeDelta = false;
                if (m_CopyShader == null || !SystemInfo.supportsComputeShaders)
                    return;

                if (!m_CopyShader.HasKernel(CopyKernelName))
                    return;

                m_CopyKernel = m_CopyShader.FindKernel(CopyKernelName);
                m_UsesComputeDelta = m_CopyKernel >= 0;
            }

            private void CopyDirtyRanges()
            {
                if (m_CopyShader == null || m_CopyKernel < 0 || m_CopyOperations.Length <= 0)
                    return;

                EnsureCopyOperationBuffer(m_CopyOperations.Length);
                m_CopyOperationBuffer.SetData(m_CopyOperations.AsArray());
                m_CopyShader.SetBuffer(m_CopyKernel, s_CopySrcBufferId, m_StagingBuffers[m_BufferIndex]);
                m_CopyShader.SetBuffer(m_CopyKernel, s_CopyDstBufferId, m_PersistentBuffer);
                m_CopyShader.SetBuffer(m_CopyKernel, s_CopyOperationsId, m_CopyOperationBuffer);
                m_CopyShader.SetInt(s_CopyOperationCountId, m_CopyOperations.Length);

                int remaining = m_CopyOperations.Length;
                int operationBase = 0;
                while (remaining > 0)
                {
                    int groupCount = Mathf.Min(MaxThreadGroupsPerDispatch, remaining);
                    m_CopyShader.SetInt(s_CopyOperationBaseId, operationBase);
                    m_CopyShader.Dispatch(m_CopyKernel, groupCount, 1, 1);
                    operationBase += groupCount;
                    remaining -= groupCount;
                }
            }

            private void EnsureCopyOperationBuffer(int requiredCount)
            {
                if (m_CopyOperationBuffer != null && m_CopyOperationBuffer.count >= requiredCount)
                    return;

                int capacity = 1;
                while (capacity < requiredCount)
                    capacity <<= 1;

                m_CopyOperationBuffer?.Release();
                m_CopyOperationBuffer = new ComputeBuffer(capacity, UnsafeUtility.SizeOf<UploadOperation>());
            }

            private void ReleaseBuffers()
            {
                for (int index = 0; index < m_StagingBuffers.Length; index++)
                {
                    m_StagingBuffers[index]?.Dispose();
                    m_StagingBuffers[index] = null;
                }

                m_PersistentBuffer?.Dispose();
                m_PersistentBuffer = null;
            }

            private static int AlignTo16(int value)
            {
                return (value + 15) & ~15;
            }

            private static int AlignTo4(int value)
            {
                return (value + 3) & ~3;
            }
        }

        private static unsafe ParticleBoundsData CalculateParticleBoundsPage(
            ParticleBoundsSource source,
            int particleStart,
            int particleCount)
        {
            int start = math.clamp(particleStart, 0, source.ActiveCount);
            int end = math.clamp(start + math.max(0, particleCount), start, source.ActiveCount);
            if (start >= end)
                return default;

            float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 max = new(float.MinValue, float.MinValue, float.MinValue);
            for (int index = start; index < end; index++)
            {
                float3 position = source.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                    ? math.transform(source.LocalToWorld, source.Positions[index])
                    : source.Positions[index];
                float extent = GetParticleWorldExtent(source, index);
                float3 extent3 = new(extent, extent, extent);
                min = math.min(min, position - extent3);
                max = math.max(max, position + extent3);
            }

            return CreateBoundsData(min, max);
        }

        private static unsafe float GetParticleWorldExtent(ParticleBoundsSource source, int particleIndex)
        {
            float renderSize = math.max(
                VividParticleMainModule.MinimumStartSize,
                source.Sizes[particleIndex] * source.SizeScale);

            if (source.RenderMode == (int)VividParticleRenderMode.Mesh && source.MeshExtent > 0.0f)
                return math.max(VividParticleMainModule.MinimumStartSize, source.MeshExtent * renderSize);

            if (source.RenderMode == (int)VividParticleRenderMode.Stretch)
            {
                float length = math.max(
                    VividParticleMainModule.MinimumStartSize,
                    renderSize * source.StretchLengthScale
                    + math.length(source.Velocities[particleIndex]) * source.StretchSpeedScale);
                return math.max(renderSize, length) * 0.5f;
            }

            return renderSize * 0.5f;
        }

        private static ParticleBoundsData CreateBoundsData(float3 min, float3 max)
        {
            float3 center = (min + max) * 0.5f;
            float3 extents = math.max((max - min) * 0.5f, float3.zero);
            return new ParticleBoundsData
            {
                Center = center,
                Extents = extents,
                IsValid = 1,
            };
        }

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        private struct ParticleBoundsBatchPageJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ParticleBoundsPageWork> Works;

            public NativeArray<ParticleBoundsData> PageBounds;

            public void Execute(int index)
            {
                ParticleBoundsPageWork work = Works[index];
                PageBounds[index] = CalculateParticleBoundsPage(
                    work.Source,
                    work.ParticleStart,
                    work.ParticleCount);
            }
        }

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        private struct ParticleBoundsBatchReduceJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ParticleBoundsRecordReduceWork> Works;
            [ReadOnly]
            public NativeArray<ParticleBoundsData> PageBounds;

            public NativeArray<ParticleBoundsRecordResult> RecordResults;

            public void Execute(int index)
            {
                ParticleBoundsRecordReduceWork work = Works[index];
                float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
                float3 max = new(float.MinValue, float.MinValue, float.MinValue);
                bool hasBounds = false;
                int pageEnd = math.min(work.PageStart + work.PageCount, PageBounds.Length);
                for (int pageIndex = work.PageStart; pageIndex < pageEnd; pageIndex++)
                {
                    ParticleBoundsData bounds = PageBounds[pageIndex];
                    if (bounds.IsValid == 0)
                        continue;

                    min = math.min(min, bounds.Center - bounds.Extents);
                    max = math.max(max, bounds.Center + bounds.Extents);
                    hasBounds = true;
                }

                RecordResults[index] = new ParticleBoundsRecordResult
                {
                    WorldBounds = hasBounds ? CreateBoundsData(min, max) : default,
                    PageStart = work.PageStart,
                    PageCount = work.PageCount,
                    ActiveCount = work.ActiveCount,
                    UsesPageBillboard = work.UsesPageBillboard,
                };
            }
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private unsafe struct ParticleMeshVisibleCountJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ParticleMeshVisibleCountWork> Works;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> MeshVisibleCounts;

            public void Execute(int workIndex)
            {
                ParticleMeshVisibleCountWork work = Works[workIndex];
                int meshCount = math.max(1, work.MeshCount);
                int outputOffset = math.max(0, work.OutputOffset);
                for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                    MeshVisibleCounts[outputOffset + meshIndex] = 0;

                int activeCount = math.max(0, work.ActiveCount);
                if (work.MeshIndices == null)
                {
                    MeshVisibleCounts[outputOffset] = activeCount;
                    return;
                }

                for (int particleIndex = 0; particleIndex < activeCount; particleIndex++)
                {
                    int meshIndex = ResolveMeshIndexSlot(work.MeshIndices[particleIndex], meshCount);
                    MeshVisibleCounts[outputOffset + meshIndex]++;
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        private unsafe struct ParticleDrawCommandOutputJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ParticleDrawCommandInput> Commands;
            [ReadOnly]
            public NativeArray<ParticleDrawRangeInput> Ranges;
            [ReadOnly]
            public NativeArray<ParticleCullingRecord> CullingRecords;
            [ReadOnly]
            public NativeArray<ParticleCullingPlanePacket4> CullingPlanePackets;
            [ReadOnly]
            public NativeArray<ParticleCullingSplit> CullingSplits;
            [NativeDisableUnsafePtrRestriction]
            public ulong* PickingIncludeRenderers;
            public int PickingIncludeRendererCount;
            [NativeDisableUnsafePtrRestriction]
            public ulong* PickingIncludeEntities;
            public int PickingIncludeEntityCount;
            [NativeDisableUnsafePtrRestriction]
            public ulong* PickingExcludeRenderers;
            public int PickingExcludeRendererCount;
            [NativeDisableUnsafePtrRestriction]
            public ulong* PickingExcludeEntities;
            public int PickingExcludeEntityCount;

            public uint CullingLayerMask;
            public ulong SceneCullingMask;
            public int ViewType;
            public int PickingIncludeEnabled;
            public int PickingFilterEnabled;
            [NativeDisableUnsafePtrRestriction]
            public BatchDrawCommand* DrawCommands;
            [NativeDisableUnsafePtrRestriction]
            public BatchDrawRange* DrawRanges;
            [NativeDisableUnsafePtrRestriction]
            public int* VisibleInstances;
            [NativeDisableUnsafePtrRestriction]
            public float* InstanceSortingPositions;
            [NativeDisableUnsafePtrRestriction]
            public EntityId* DrawCommandPickingEntityIds;

            public void Execute(int commandIndex)
            {
                ParticleDrawCommandInput command = Commands[commandIndex];
                int splitVisibilityMask = 0;
                int visibleCount = ShouldRenderCommand(command)
                    ? WriteVisibleInstances(command, out splitVisibilityMask)
                    : 0;
                bool writesSortingPositions = InstanceSortingPositions != null && command.HasSortingPositions != 0;
                BatchDrawCommandFlags drawFlags = writesSortingPositions
                    ? command.DrawFlags
                    : command.DrawFlags & ~BatchDrawCommandFlags.HasSortingPosition;

                DrawCommands[commandIndex] = new BatchDrawCommand
                {
                    visibleOffset = (uint)command.VisibleOffset,
                    visibleCount = (uint)visibleCount,
                    batchID = command.BatchId,
                    materialID = command.MaterialId,
                    meshID = command.MeshId,
                    submeshIndex = (ushort)math.clamp(command.SubmeshIndex, 0, ushort.MaxValue),
                    activeMeshLod = (ushort)math.clamp(command.ActiveMeshLod, 0, ushort.MaxValue),
                    splitVisibilityMask = (byte)ResolveSplitVisibilityMask(splitVisibilityMask),
                    flags = drawFlags,
                    sortingPosition = writesSortingPositions ? command.SortingPositionOffset * 3 : 0,
                };
                if (DrawCommandPickingEntityIds != null)
                    DrawCommandPickingEntityIds[commandIndex] = command.PickingEntityId;

                if (commandIndex < Ranges.Length)
                    DrawRanges[commandIndex] = CreateDrawRange(Ranges[commandIndex]);
            }

            private BatchDrawRange CreateDrawRange(ParticleDrawRangeInput range)
            {
                return new BatchDrawRange
                {
                    drawCommandsBegin = (uint)math.max(0, range.DrawCommandsBegin),
                    drawCommandsCount = (uint)math.max(0, range.DrawCommandsCount),
                    drawCommandsType = BatchDrawCommandType.Direct,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = range.RenderingLayerMask,
                        rendererPriority = range.RendererPriority,
                        layer = (byte)math.clamp(range.Layer, 0, 31),
                        shadowCastingMode = range.ShadowCastingMode,
                        receiveShadows = range.ReceiveShadows != 0,
                        motionMode = range.MotionMode,
                        staticShadowCaster = range.StaticShadowCaster != 0,
                        allDepthSorted = range.AllDepthSorted != 0 && InstanceSortingPositions != null,
                        sceneCullingMask = SceneCullingMask,
                    },
                };
            }

            private bool ShouldRenderCommand(ParticleDrawCommandInput command)
            {
                if (!IsLayerVisible(command.Layer))
                    return false;

                if (!ShouldRenderBatchForView(command.ShadowCastingMode, (BatchCullingViewType)ViewType))
                    return false;

                if (!DoesPickingFilterPass(command))
                    return false;

                return command.RecordCount > 0 && command.MaxVisibleCount > 0;
            }

            private bool DoesPickingFilterPass(ParticleDrawCommandInput command)
            {
                bool writesPickingEntityIds = ViewType == (int)BatchCullingViewType.Picking
                    || ViewType == (int)BatchCullingViewType.SelectionOutline;
                if (PickingFilterEnabled == 0 || !writesPickingEntityIds)
                    return true;

                ulong entityId = ((ulong)command.PickingEntityIdHigh << 32) | command.PickingEntityIdLow;
                if (PickingIncludeEnabled != 0
                    && !ContainsEntityId(PickingIncludeRenderers, PickingIncludeRendererCount, entityId)
                    && !ContainsEntityId(PickingIncludeEntities, PickingIncludeEntityCount, entityId))
                {
                    return false;
                }

                if (ContainsEntityId(PickingExcludeRenderers, PickingExcludeRendererCount, entityId)
                    || ContainsEntityId(PickingExcludeEntities, PickingExcludeEntityCount, entityId))
                {
                    return false;
                }

                return true;
            }

            private bool IsLayerVisible(int layer)
            {
                layer = math.clamp(layer, 0, 31);
                return (CullingLayerMask & (1u << layer)) != 0u;
            }

            private static bool ContainsEntityId(ulong* entityIds, int count, ulong entityId)
            {
                if (entityIds == null || count <= 0)
                    return false;

                for (int index = 0; index < count; index++)
                {
                    if (entityIds[index] == entityId)
                        return true;
                }

                return false;
            }

            private int WriteVisibleInstances(
                ParticleDrawCommandInput command,
                out int splitVisibilityMask)
            {
                splitVisibilityMask = 0;
                int visibleOffset = command.VisibleOffset;
                int startOffset = visibleOffset;
                int sortingOffset = command.SortingPositionOffset;
                bool writesSortingPositions = InstanceSortingPositions != null && command.HasSortingPositions != 0;
                int recordEnd = command.RecordStart + command.RecordCount;
                for (int recordIndex = command.RecordStart; recordIndex < recordEnd; recordIndex++)
                {
                    ParticleCullingRecord record = CullingRecords[recordIndex];
                    int recordSplitVisibilityMask = GetRecordSplitVisibilityMask(record);
                    if (!ShouldRenderRecord(record) || recordSplitVisibilityMask == 0)
                        continue;

                    splitVisibilityMask |= recordSplitVisibilityMask;
                    if (record.UsesPageBillboard != 0)
                    {
                        int remaining = record.ActiveCount;
                        int pageIndex = 0;
                        while (remaining > 0 && visibleOffset - startOffset < command.MaxVisibleCount)
                        {
                            if (writesSortingPositions)
                                WriteSortingPosition(sortingOffset++, GetPageSortingPosition(record));

                            VisibleInstances[visibleOffset++] = record.SpanBaseIndex + pageIndex;
                            remaining -= BillboardPageSize;
                            pageIndex++;
                        }
                    }
                    else
                    {
                        for (int particleIndex = 0;
                             particleIndex < record.ActiveCount && visibleOffset - startOffset < command.MaxVisibleCount;
                             particleIndex++)
                        {
                            if (!MatchesMeshIndexFilter(command, record, particleIndex))
                                continue;

                            if (writesSortingPositions)
                                WriteSortingPosition(
                                    sortingOffset++,
                                    GetParticleSortingPosition(record, particleIndex));

                            VisibleInstances[visibleOffset++] = record.BatchBaseIndex + particleIndex;
                        }
                    }
                }

                return visibleOffset - startOffset;
            }

            private static unsafe float3 GetPageSortingPosition(ParticleCullingRecord record)
            {
                int particleIndex = record.ParticleStart;
                bool hasFirstParticle = record.Positions != null
                    && particleIndex >= 0
                    && particleIndex < record.PositionCapacity;
                return ResolvePageSortingPosition(
                    record.BoundsCenter,
                    hasFirstParticle ? record.Positions[particleIndex] : default,
                    record.LocalToWorld,
                    record.SimulationSpace,
                    hasFirstParticle);
            }

            private static unsafe float3 GetParticleSortingPosition(
                ParticleCullingRecord record,
                int localParticleIndex)
            {
                int particleIndex = record.ParticleStart + localParticleIndex;
                if (record.Positions == null
                    || particleIndex < 0
                    || particleIndex >= record.PositionCapacity)
                {
                    return record.BoundsCenter;
                }

                return ResolveParticleSortingPosition(
                    record.Positions[particleIndex],
                    record.LocalToWorld,
                    record.SimulationSpace);
            }

            private static bool MatchesMeshIndexFilter(
                ParticleDrawCommandInput command,
                ParticleCullingRecord record,
                int localParticleIndex)
            {
                if (command.MeshIndexFilter < 0)
                    return true;

                if (record.MeshIndices == null)
                    return command.MeshIndexFilter == 0;

                int particleIndex = record.ParticleStart + localParticleIndex;
                int meshIndex = ResolveMeshIndexSlot(record.MeshIndices[particleIndex], command.MeshCount);
                return meshIndex == command.MeshIndexFilter;
            }

            private bool ShouldRenderRecord(ParticleCullingRecord record)
            {
                return ViewType != (int)BatchCullingViewType.SelectionOutline || record.IsEditorSelected != 0;
            }

            private void WriteSortingPosition(int visibleIndex, float3 position)
            {
                if (InstanceSortingPositions == null)
                    return;

                int positionOffset = visibleIndex * 3;
                InstanceSortingPositions[positionOffset] = position.x;
                InstanceSortingPositions[positionOffset + 1] = position.y;
                InstanceSortingPositions[positionOffset + 2] = position.z;
            }

            private int ResolveSplitVisibilityMask(int splitVisibilityMask)
            {
                return VividParticleSystemManager.ResolveSplitVisibilityMaskForView(
                    (BatchCullingViewType)ViewType,
                    splitVisibilityMask,
                    CullingSplits.Length);
            }

            private int GetRecordSplitVisibilityMask(ParticleCullingRecord record)
            {
                if (record.ActiveCount <= 0)
                    return 0;

                if (CullingPlanePackets.Length == 0 || CullingSplits.Length == 0)
                    return 0xff;

                int splitVisibilityMask = 0;
                for (int splitIndex = 0; splitIndex < CullingSplits.Length; splitIndex++)
                {
                    ParticleCullingSplit split = CullingSplits[splitIndex];
                    if (split.PacketCount <= 0)
                        return 0xff;

                    if (IntersectsPlanePackets(record, split.PacketOffset, split.PacketCount))
                        splitVisibilityMask |= GetSplitVisibilityBit(splitIndex);
                }

                return splitVisibilityMask;
            }

            private static int GetSplitVisibilityBit(int splitIndex)
            {
                return splitIndex < 0 || splitIndex >= 8 ? 0xff : 1 << splitIndex;
            }

            private bool IntersectsPlanePackets(ParticleCullingRecord record, int packetOffset, int packetCount)
            {
                int packetEnd = math.min(CullingPlanePackets.Length, packetOffset + packetCount);
                for (int packetIndex = math.max(0, packetOffset); packetIndex < packetEnd; packetIndex++)
                {
                    ParticleCullingPlanePacket4 packet = CullingPlanePackets[packetIndex];
                    float4 positiveX = math.select(
                        new float4(record.BoundsCenter.x - record.BoundsExtents.x),
                        new float4(record.BoundsCenter.x + record.BoundsExtents.x),
                        packet.NormalX >= 0.0f);
                    float4 positiveY = math.select(
                        new float4(record.BoundsCenter.y - record.BoundsExtents.y),
                        new float4(record.BoundsCenter.y + record.BoundsExtents.y),
                        packet.NormalY >= 0.0f);
                    float4 positiveZ = math.select(
                        new float4(record.BoundsCenter.z - record.BoundsExtents.z),
                        new float4(record.BoundsCenter.z + record.BoundsExtents.z),
                        packet.NormalZ >= 0.0f);
                    float4 distances = packet.NormalX * positiveX
                        + packet.NormalY * positiveY
                        + packet.NormalZ * positiveZ
                        + packet.Distance;
                    if (math.any(distances < 0.0f))
                        return false;
                }

                return true;
            }
        }

        private static class VividParticleRenderJobPipeline
        {
            private static readonly ParticleRenderPageJobDescriptor[] s_PageJobDescriptors =
                CreatePageJobDescriptors();

            private static readonly VividEcsManagerJobRegistry<ParticleRenderJobContext> s_RenderJobRegistry =
                CreateRenderJobRegistry();

            public static int registeredJobCount => s_RenderJobRegistry.count;

            public static int pageJobDescriptorCount => s_PageJobDescriptors.Length;

            public static JobHandle Schedule(
                ParticleRenderPageJobWorkSet pageWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks,
                uint enabledModuleFlags)
            {
                enabledModuleFlags = FilterEnabledFlags(
                    pageWorks,
                    sharedDataWorks.IsCreated && sharedDataWorks.Length > 0,
                    enabledModuleFlags);
                if (enabledModuleFlags == 0u)
                    return default;

                var context = new ParticleRenderJobContext(
                    pageWorks,
                    sharedDataWorks,
                    enabledModuleFlags);
                return s_RenderJobRegistry.ScheduleEnabledParallel(context, context.EnabledModuleFlags);
            }

            public static uint FilterEnabledFlags(
                bool hasTransformPageWorks,
                bool hasColorPageWorks,
                bool hasVelocityStretchPageWorks,
                bool hasExtraDataPageWorks,
                bool hasSharedDataWorks,
                uint enabledModuleFlags)
            {
                return FilterEnabledFlags(
                    new ParticleRenderPageJobAvailability(
                        hasTransformPageWorks,
                        hasColorPageWorks,
                        hasVelocityStretchPageWorks,
                        hasExtraDataPageWorks),
                    hasSharedDataWorks,
                    enabledModuleFlags);
            }

            public static uint FilterEnabledFlags(
                ParticleRenderPageJobWorkSet pageWorks,
                bool hasSharedDataWorks,
                uint enabledModuleFlags)
            {
                return FilterEnabledFlags(pageWorks.GetAvailability(), hasSharedDataWorks, enabledModuleFlags);
            }

            public static int CountEnabledPageModules(uint enabledModuleFlags)
            {
                int count = 0;
                for (int index = 0; index < s_PageJobDescriptors.Length; index++)
                {
                    if ((enabledModuleFlags & (uint)s_PageJobDescriptors[index].Flag) != 0u)
                        count++;
                }

                return count;
            }

            private static uint FilterEnabledFlags(
                ParticleRenderPageJobAvailability pageAvailability,
                bool hasSharedDataWorks,
                uint enabledModuleFlags)
            {
                for (int index = 0; index < s_PageJobDescriptors.Length; index++)
                {
                    ParticleRenderPageJobDescriptor descriptor = s_PageJobDescriptors[index];
                    if (!pageAvailability.HasWork(descriptor.Module))
                        enabledModuleFlags &= ~(uint)descriptor.Flag;
                }

                if (!hasSharedDataWorks)
                    enabledModuleFlags &= ~(uint)ParticleRenderJobFlags.SharedData;

                return enabledModuleFlags;
            }

            private static ParticleRenderPageJobDescriptor[] CreatePageJobDescriptors()
            {
                return new[]
                {
                    new ParticleRenderPageJobDescriptor(
                        ParticleRenderPageJobModule.Transform,
                        "VividParticle.Render.Transform",
                        0,
                        ParticleRenderJobFlags.TransformUpload,
                        UploadColumnTransformMask),
                    new ParticleRenderPageJobDescriptor(
                        ParticleRenderPageJobModule.Color,
                        "VividParticle.Render.Color",
                        10,
                        ParticleRenderJobFlags.ColorUpload,
                        UploadColumnBaseColorMask),
                    new ParticleRenderPageJobDescriptor(
                        ParticleRenderPageJobModule.VelocityStretch,
                        "VividParticle.Render.VelocityStretch",
                        20,
                        ParticleRenderJobFlags.VelocityStretchUpload,
                        UploadColumnVelocityStretchMask),
                    new ParticleRenderPageJobDescriptor(
                        ParticleRenderPageJobModule.ExtraData,
                        "VividParticle.Render.ExtraData",
                        30,
                        ParticleRenderJobFlags.ExtraDataUpload,
                        UploadColumnExtraDataMask),
                };
            }

            private static VividEcsManagerJobRegistry<ParticleRenderJobContext> CreateRenderJobRegistry()
            {
                var registry = new VividEcsManagerJobRegistry<ParticleRenderJobContext>();
                for (int index = 0; index < s_PageJobDescriptors.Length; index++)
                {
                    ParticleRenderPageJobDescriptor descriptor = s_PageJobDescriptors[index];
                    registry.RegisterModule(
                        descriptor.Name,
                        descriptor.Order,
                        (uint)descriptor.Flag,
                        (context, dependency) => SchedulePageUploadJob(context, descriptor, dependency));
                }

                registry.RegisterModule(
                    "VividParticle.Render.SharedData",
                    40,
                    (uint)ParticleRenderJobFlags.SharedData,
                    ScheduleSharedDataJob);
                return registry;
            }

            private static JobHandle SchedulePageUploadJob(
                ParticleRenderJobContext context,
                ParticleRenderPageJobDescriptor descriptor,
                JobHandle dependency)
            {
                NativeArray<ParticleRenderUploadPageWork> works = context.PageWorks.GetWorks(descriptor.Module);
                if (!works.IsCreated || works.Length <= 0)
                    return dependency;

                return new VividParticlePageUploadRenderJob
                {
                    Works = works,
                    ColumnMask = descriptor.ColumnMask,
                }.Schedule(works.Length, 32, dependency);
            }

            private static JobHandle ScheduleSharedDataJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                if (!context.HasSharedDataWorks)
                    return dependency;

                return new VividParticleSharedDataRenderJob
                {
                    Works = context.SharedDataWorks,
                }.Schedule(context.SharedDataWorks.Length, 32, dependency);
            }
        }

        [Flags]
        private enum ParticleRenderJobFlags : uint
        {
            None = 0u,
            TransformUpload = RenderJobTransformUploadFlag,
            ColorUpload = RenderJobColorUploadFlag,
            VelocityStretchUpload = RenderJobVelocityStretchUploadFlag,
            ExtraDataUpload = RenderJobExtraDataUploadFlag,
            SharedData = RenderJobSharedDataFlag,
            AllPageUpload = RenderJobAllPageUploadFlags,
        }

        private enum ParticleRenderPageJobModule
        {
            Transform,
            Color,
            VelocityStretch,
            ExtraData,
        }

        private readonly struct ParticleRenderPageJobDescriptor
        {
            public ParticleRenderPageJobDescriptor(
                ParticleRenderPageJobModule module,
                string name,
                int order,
                ParticleRenderJobFlags flag,
                int columnMask)
            {
                Module = module;
                Name = name;
                Order = order;
                Flag = flag;
                ColumnMask = columnMask;
            }

            public ParticleRenderPageJobModule Module { get; }

            public string Name { get; }

            public int Order { get; }

            public ParticleRenderJobFlags Flag { get; }

            public int ColumnMask { get; }
        }

        private readonly struct ParticleRenderPageJobAvailability
        {
            public ParticleRenderPageJobAvailability(
                bool hasTransformPageWorks,
                bool hasColorPageWorks,
                bool hasVelocityStretchPageWorks,
                bool hasExtraDataPageWorks)
            {
                HasTransformPageWorks = hasTransformPageWorks;
                HasColorPageWorks = hasColorPageWorks;
                HasVelocityStretchPageWorks = hasVelocityStretchPageWorks;
                HasExtraDataPageWorks = hasExtraDataPageWorks;
            }

            public bool HasTransformPageWorks { get; }

            public bool HasColorPageWorks { get; }

            public bool HasVelocityStretchPageWorks { get; }

            public bool HasExtraDataPageWorks { get; }

            public bool HasWork(ParticleRenderPageJobModule module)
            {
                return module switch
                {
                    ParticleRenderPageJobModule.Transform => HasTransformPageWorks,
                    ParticleRenderPageJobModule.Color => HasColorPageWorks,
                    ParticleRenderPageJobModule.VelocityStretch => HasVelocityStretchPageWorks,
                    ParticleRenderPageJobModule.ExtraData => HasExtraDataPageWorks,
                    _ => false,
                };
            }
        }

        private readonly struct ParticleRenderPageJobWorkSet
        {
            public ParticleRenderPageJobWorkSet(
                NativeArray<ParticleRenderUploadPageWork> transformPageWorks,
                NativeArray<ParticleRenderUploadPageWork> colorPageWorks,
                NativeArray<ParticleRenderUploadPageWork> velocityStretchPageWorks,
                NativeArray<ParticleRenderUploadPageWork> extraDataPageWorks)
            {
                TransformPageWorks = transformPageWorks;
                ColorPageWorks = colorPageWorks;
                VelocityStretchPageWorks = velocityStretchPageWorks;
                ExtraDataPageWorks = extraDataPageWorks;
            }

            public readonly NativeArray<ParticleRenderUploadPageWork> TransformPageWorks;

            public readonly NativeArray<ParticleRenderUploadPageWork> ColorPageWorks;

            public readonly NativeArray<ParticleRenderUploadPageWork> VelocityStretchPageWorks;

            public readonly NativeArray<ParticleRenderUploadPageWork> ExtraDataPageWorks;

            public ParticleRenderPageJobAvailability GetAvailability()
            {
                return new ParticleRenderPageJobAvailability(
                    TransformPageWorks.IsCreated && TransformPageWorks.Length > 0,
                    ColorPageWorks.IsCreated && ColorPageWorks.Length > 0,
                    VelocityStretchPageWorks.IsCreated && VelocityStretchPageWorks.Length > 0,
                    ExtraDataPageWorks.IsCreated && ExtraDataPageWorks.Length > 0);
            }

            public NativeArray<ParticleRenderUploadPageWork> GetWorks(ParticleRenderPageJobModule module)
            {
                return module switch
                {
                    ParticleRenderPageJobModule.Transform => TransformPageWorks,
                    ParticleRenderPageJobModule.Color => ColorPageWorks,
                    ParticleRenderPageJobModule.VelocityStretch => VelocityStretchPageWorks,
                    ParticleRenderPageJobModule.ExtraData => ExtraDataPageWorks,
                    _ => default,
                };
            }
        }

        private readonly struct ParticleRenderJobContext : IVividEcsManagerJobModuleFlags
        {
            public ParticleRenderJobContext(
                ParticleRenderPageJobWorkSet pageWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks,
                uint enabledModuleFlags)
            {
                PageWorks = pageWorks;
                SharedDataWorks = sharedDataWorks;
                EnabledModuleFlags = enabledModuleFlags;
            }

            public readonly ParticleRenderPageJobWorkSet PageWorks;

            public readonly NativeArray<ParticleRenderSharedDataWork> SharedDataWorks;

            public bool HasSharedDataWorks => SharedDataWorks.IsCreated && SharedDataWorks.Length > 0;

            public uint EnabledModuleFlags { get; }

        }

        [BurstCompile]
        private struct VividParticlePageUploadRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadPageWork> Works;

            public int ColumnMask;

            public void Execute(int workIndex)
            {
                VividParticleRenderJobUtility.WriteParticlePage(Works[workIndex], ColumnMask);
            }
        }

        [BurstCompile]
        private struct VividParticleSharedDataRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderSharedDataWork> Works;

            public void Execute(int workIndex)
            {
                VividParticleRenderJobUtility.WriteSharedData(Works[workIndex]);
            }
        }

        private static unsafe class VividParticleRenderJobUtility
        {
            public const int RenderColumnPositionSize = 0;
            public const int RenderColumnColor = 1;
            public const int RenderColumnRotation = 2;
            public const int RenderColumnVelocityStretch = 3;
            public const int RenderColumnScale = 4;
            public const int RenderColumnUV = 5;
            public const int RenderColumnCustomData1 = 6;
            public const int RenderColumnCustomData2 = 7;
            public const int RenderColumnMeshIndex = 8;

            private const int SharedDataWorkKindFloat4 = 0;
            private const int SharedDataWorkKindPerSharp = 1;
            private const int SharedDataWorkKindSpan = 2;

            public static void WriteParticlePage(ParticleRenderUploadPageWork work, int columnMask)
            {
                int activeColumnMask = work.ColumnMask & columnMask;
                if (activeColumnMask == 0)
                    return;

                ParticleRenderUploadSource source = work.Source;
                int endIndex = math.min(source.ActiveCount, source.StartIndex + source.Count);
                if (endIndex <= source.StartIndex)
                    return;

                if ((activeColumnMask & UploadColumnTransformMask) != 0)
                    WriteTransformPage(work, source, activeColumnMask, endIndex);

                if ((activeColumnMask & UploadColumnBaseColorMask) != 0)
                    WriteColorPage(work, source, endIndex);

                if ((activeColumnMask & UploadColumnVelocityStretchMask) != 0)
                    WriteVelocityStretchPage(work, source, endIndex);

                if ((activeColumnMask & UploadColumnExtraDataMask) != 0)
                    WriteExtraDataPage(work, source, activeColumnMask, endIndex);
            }

            private static void WriteTransformPage(
                ParticleRenderUploadPageWork work,
                ParticleRenderUploadSource source,
                int activeColumnMask,
                int endIndex)
            {
                for (int particleIndex = source.StartIndex; particleIndex < endIndex; particleIndex++)
                {
                    if ((activeColumnMask & UploadColumnPositionSizeMask) != 0)
                        WriteParticleValue(work, particleIndex, work.PositionSizeByteOffset, GetPositionSize(source, particleIndex));

                    if ((activeColumnMask & UploadColumnRotationMask) != 0)
                        WriteParticleValue(
                            work,
                            particleIndex,
                            work.RotationByteOffset,
                            new float4(0.0f, 0.0f, 0.0f, 1.0f));

                    if ((activeColumnMask & UploadColumnScaleMask) != 0)
                        WriteParticleValue(work, particleIndex, work.ScaleByteOffset, GetScale(source, particleIndex));
                }
            }

            private static void WriteColorPage(
                ParticleRenderUploadPageWork work,
                ParticleRenderUploadSource source,
                int endIndex)
            {
                for (int particleIndex = source.StartIndex; particleIndex < endIndex; particleIndex++)
                    WriteParticleValue(work, particleIndex, work.BaseColorByteOffset, GetRenderColor(source, particleIndex));
            }

            private static void WriteVelocityStretchPage(
                ParticleRenderUploadPageWork work,
                ParticleRenderUploadSource source,
                int endIndex)
            {
                for (int particleIndex = source.StartIndex; particleIndex < endIndex; particleIndex++)
                {
                    WriteParticleValue(
                        work,
                        particleIndex,
                        work.VelocityStretchByteOffset,
                        GetVelocityStretch(source, particleIndex));
                }
            }

            private static void WriteExtraDataPage(
                ParticleRenderUploadPageWork work,
                ParticleRenderUploadSource source,
                int activeColumnMask,
                int endIndex)
            {
                for (int particleIndex = source.StartIndex; particleIndex < endIndex; particleIndex++)
                {
                    if ((activeColumnMask & UploadColumnUVMask) != 0)
                        WriteParticleValue(
                            work,
                            particleIndex,
                            work.UVByteOffset,
                            new float4(0.0f, 0.0f, 1.0f, 1.0f));

                    if ((activeColumnMask & UploadColumnCustomData1Mask) != 0)
                        WriteParticleValue(work, particleIndex, work.CustomData1ByteOffset, float4.zero);

                    if ((activeColumnMask & UploadColumnCustomData2Mask) != 0)
                        WriteParticleValue(work, particleIndex, work.CustomData2ByteOffset, float4.zero);

                    if ((activeColumnMask & UploadColumnMeshIndexMask) != 0)
                        WriteParticleValue(work, particleIndex, work.MeshIndexByteOffset, GetMeshIndex(source, particleIndex));
                }
            }

            public static void WriteSharedData(ParticleRenderSharedDataWork work)
            {
                switch (work.Kind)
                {
                    case SharedDataWorkKindFloat4:
                        WriteSharedFloat4(work);
                        break;
                    case SharedDataWorkKindPerSharp:
                        WritePerSharpSharedData(work);
                        break;
                    case SharedDataWorkKindSpan:
                        WriteSpanSharedData(work);
                        break;
                }
            }

            private static void WriteParticleValue(
                ParticleRenderUploadPageWork work,
                int particleIndex,
                int columnByteOffset,
                float4 value)
            {
                ParticleRenderUploadSource source = work.Source;
                int batchIndex = source.BatchBaseIndex + particleIndex;
                UnsafeUtility.WriteArrayElement(
                    source.BufferBase + source.BatchDataOffset + columnByteOffset,
                    batchIndex,
                    value);
            }

            private static void WriteSharedFloat4(ParticleRenderSharedDataWork work)
            {
                UnsafeUtility.WriteArrayElement(
                    work.BufferBase + work.BatchDataOffset + work.ColumnByteOffset,
                    work.ElementStart,
                    work.Value);
            }

            private static void WritePerSharpSharedData(ParticleRenderSharedDataWork work)
            {
                byte* destination = work.BufferBase
                    + work.BatchDataOffset
                    + work.ColumnByteOffset
                    + work.ElementStart * SharedDataByteSize;
                UnsafeUtility.WriteArrayElement(destination, 0, work.LocalToWorld.c0);
                UnsafeUtility.WriteArrayElement(destination, 1, work.LocalToWorld.c1);
                UnsafeUtility.WriteArrayElement(destination, 2, work.LocalToWorld.c2);
                UnsafeUtility.WriteArrayElement(destination, 3, work.LocalToWorld.c3);
                UnsafeUtility.WriteArrayElement(destination, 4, work.RendererColor);
                UnsafeUtility.WriteArrayElement(
                    destination,
                    5,
                    new float4(
                        math.max(VividParticleMainModule.MinimumStartSize, work.SizeScale),
                        math.max(0.0f, work.MinParticleSize),
                        math.max(0.0f, work.MaxParticleSize),
                        work.ActiveCount));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    6,
                    new float4(
                        work.StretchLengthScale,
                        work.StretchSpeedScale,
                        0.0f,
                        work.RenderMode));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    7,
                    new float4(
                        work.RendererPriority,
                        work.ShadowCastingMode,
                        work.ReceiveShadows,
                        work.SortMode));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    8,
                    new float4(
                        work.DataPerSharpBits,
                        work.UsesPageBillboard,
                        work.Capacity,
                        work.ActiveCount));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    9,
                    new float4(
                        work.SizeScale,
                        work.StretchLengthScale,
                        work.StretchSpeedScale,
                        work.RendererPriority));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    10,
                    new uint4(
                        work.PickingEntityIdLow,
                        work.PickingEntityIdHigh,
                        (uint)math.max(0, work.IsEditorSelected),
                        work.RenderingLayerMask));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    11,
                    new float4(
                        work.SharpIndex,
                        work.SpanBaseIndex,
                        work.BatchBaseIndex,
                        work.Layer));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    12,
                    new float4(
                        work.Pivot,
                        0.0f));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    13,
                    new float4(
                        math.saturate(work.Flip),
                        0.0f));
            }

            private static void WriteSpanSharedData(ParticleRenderSharedDataWork work)
            {
                byte* destination = work.BufferBase
                    + work.BatchDataOffset
                    + work.ColumnByteOffset
                    + work.ElementStart * SpanSharedDataByteSize;
                for (int localSpanIndex = 0; localSpanIndex < work.ElementCount; localSpanIndex++)
                {
                    int spanIndex = work.ElementStart + localSpanIndex;
                    if (work.UsesPageBillboard != 0)
                    {
                        int pageStart = (spanIndex - work.SpanBaseIndex) * BillboardPageSize;
                        int pageCount = math.clamp(work.ActiveCount - pageStart, 0, BillboardPageSize);
                        uint activeMinusOne = pageCount > 0 ? (uint)(pageCount - 1) : 0u;
                        UnsafeUtility.WriteArrayElement(
                            destination,
                            localSpanIndex,
                            new uint4(
                                (uint)math.max(0, work.SharpIndex),
                                (uint)math.max(0, work.BatchBaseIndex + pageStart),
                                activeMinusOne,
                                (uint)math.max(0, work.ActiveCount)));
                    }
                    else
                    {
                        int particleOffset = spanIndex - work.SpanBaseIndex;
                        UnsafeUtility.WriteArrayElement(
                            destination,
                            localSpanIndex,
                            new uint4(
                                (uint)math.max(0, work.SharpIndex),
                                (uint)math.max(0, work.BatchBaseIndex + particleOffset),
                                0u,
                                (uint)math.max(0, work.ActiveCount)));
                    }
                }
            }

            private static float4 GetPositionSize(ParticleRenderUploadSource source, int particleIndex)
            {
                return new float4(
                    GetWorldPosition(source, source.Positions[particleIndex]),
                    GetRenderSize(source, particleIndex));
            }

            private static float4 GetVelocityStretch(ParticleRenderUploadSource source, int particleIndex)
            {
                float size = GetRenderSize(source, particleIndex);
                float3 velocity = GetWorldVelocity(source, source.Velocities[particleIndex]);
                return new float4(velocity, GetStretchLength(source, size, velocity));
            }

            private static float3 GetWorldPosition(ParticleRenderUploadSource source, float3 position)
            {
                return source.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                    ? math.transform(source.LocalToWorld, position)
                    : position;
            }

            private static float3 GetWorldVelocity(ParticleRenderUploadSource source, float3 velocity)
            {
                if (source.SimulationSpace != (int)VividParticleSystemSimulationSpace.Local)
                    return velocity;

                var rotationScale = new float3x3(
                    source.LocalToWorld.c0.xyz,
                    source.LocalToWorld.c1.xyz,
                    source.LocalToWorld.c2.xyz);
                return math.mul(rotationScale, velocity);
            }

            private static float GetRenderSize(ParticleRenderUploadSource source, int particleIndex)
            {
                return math.max(VividParticleMainModule.MinimumStartSize, source.Sizes[particleIndex] * source.SizeScale);
            }

            private static float4 GetScale(ParticleRenderUploadSource source, int particleIndex)
            {
                float size = GetRenderSize(source, particleIndex);
                return new float4(size, size, size, 1.0f);
            }

            private static float GetStretchLength(ParticleRenderUploadSource source, float size, float3 velocity)
            {
                if (source.RenderMode != (int)VividParticleRenderMode.Stretch)
                    return size;

                return math.max(
                    VividParticleMainModule.MinimumStartSize,
                    size * source.StretchLengthScale + math.length(velocity) * source.StretchSpeedScale);
            }

            private static float4 GetRenderColor(ParticleRenderUploadSource source, int particleIndex)
            {
                float startLifetime = source.StartLifetimes[particleIndex];
                float lifetimeRatio = startLifetime > 0.0f
                    ? math.saturate(source.RemainingLifetimes[particleIndex] / startLifetime)
                    : 0.0f;
                float4 color = source.Colors[particleIndex] * source.RendererColor;
                color.w *= lifetimeRatio;
                return color;
            }

            private static float4 GetMeshIndex(ParticleRenderUploadSource source, int particleIndex)
            {
                int meshCount = math.max(1, source.MeshCount);
                int meshIndex = ResolveMeshIndexSlot(source.MeshIndices[particleIndex], meshCount);
                return new float4(meshIndex, meshCount, 0.0f, 0.0f);
            }
        }

        private sealed class VividParticleSystemManagerPlayerLoopMarker
        {
        }

        private sealed class VividParticleSystemManagerRendererUpdateMarker
        {
        }

        internal readonly struct VividParticleRendererManagerStats
        {
            public readonly int RenderRecordCount;
            public readonly int LineGroupCount;
            public readonly int EcsLineGroupCount;
            public readonly int EcsLineCount;
            public readonly int EcsMatchedLineCount;
            public readonly int EcsSkippedLineCount;
            public readonly int DrawBatchCount;
            public readonly int LastLockCount;
            public readonly int LastCopyOperationCount;
            public readonly int LastCopyByteCount;
            public readonly bool UsesComputeDelta;
            public readonly int LastDirtyUploadQueueCount;
            public readonly int LastInvalidDirtyUploadQueueCount;
            public readonly int LastDirtyUploadBatchQueueCount;
            public readonly int LastInvalidDirtyUploadBatchQueueCount;
            public readonly int LastUploadRecordWorkCount;
            public readonly int LastUploadBatchWorkCount;
            public readonly int LastUploadPageWorkCount;
            public readonly int LastTransformUploadPageWorkCount;
            public readonly int LastColorUploadPageWorkCount;
            public readonly int LastVelocityStretchUploadPageWorkCount;
            public readonly int LastUVUploadPageWorkCount;
            public readonly int LastCustomDataUploadPageWorkCount;
            public readonly int LastMeshIndexUploadPageWorkCount;
            public readonly int LastExtraDataUploadPageWorkCount;
            public readonly int LastSharedDataWorkCount;
            public readonly int LastUploadCopyWorkCount;
            public readonly int LastMergedUploadCopyWorkCount;
            public readonly int LastUploadColumnMask;
            public readonly uint LastUploadDataBits;
            public readonly uint LastRenderJobModuleFlags;
            public readonly int LastRenderPageJobModuleCount;
            public readonly int CullingRecordCount;
            public readonly int DrawCommandCount;
            public readonly int DrawRangeCount;
            public readonly int VisibleInstanceCapacity;
            public readonly int SortingPositionCapacity;
            public readonly int LightDrawCommandCount;
            public readonly int LightDrawRangeCount;
            public readonly int LightVisibleInstanceCapacity;
            public readonly int PickingDrawCommandCount;
            public readonly int PickingDrawRangeCount;
            public readonly int PickingVisibleInstanceCapacity;
            public readonly int SelectionDrawCommandCount;
            public readonly int SelectionDrawRangeCount;
            public readonly int SelectionVisibleInstanceCapacity;
            public readonly int LastBoundsPageWorkCount;
            public readonly int LastBoundsRecordWorkCount;
            public readonly int LastCullingSingleMeshCacheRecordCount;
            public readonly int LastCullingMultiMeshCacheRecordCount;
            public readonly int LastCullingMeshFallbackRecordCount;
            public readonly int LastCullingRecordVisibleCacheEntryCount;
            public readonly int LastCullingBatchVisibleCacheEntryCount;
            public readonly int MeshVisibleCountWorkCount;
            public readonly int MeshVisibleCountOutputCount;

            public VividParticleRendererManagerStats(
                int renderRecordCount,
                int lineGroupCount,
                int ecsLineGroupCount,
                int ecsLineCount,
                int ecsMatchedLineCount,
                int ecsSkippedLineCount,
                int drawBatchCount,
                int lastLockCount,
                int lastCopyOperationCount,
                int lastCopyByteCount,
                bool usesComputeDelta,
                int lastDirtyUploadQueueCount,
                int lastInvalidDirtyUploadQueueCount,
                int lastDirtyUploadBatchQueueCount,
                int lastInvalidDirtyUploadBatchQueueCount,
                int lastUploadRecordWorkCount,
                int lastUploadBatchWorkCount,
                int lastUploadPageWorkCount,
                int lastTransformUploadPageWorkCount,
                int lastColorUploadPageWorkCount,
                int lastVelocityStretchUploadPageWorkCount,
                int lastUVUploadPageWorkCount,
                int lastCustomDataUploadPageWorkCount,
                int lastMeshIndexUploadPageWorkCount,
                int lastExtraDataUploadPageWorkCount,
                int lastSharedDataWorkCount,
                int lastUploadCopyWorkCount,
                int lastMergedUploadCopyWorkCount,
                int lastUploadColumnMask,
                uint lastUploadDataBits,
                uint lastRenderJobModuleFlags,
                int lastRenderPageJobModuleCount,
                int cullingRecordCount,
                int drawCommandCount,
                int drawRangeCount,
                int visibleInstanceCapacity,
                int sortingPositionCapacity,
                int lightDrawCommandCount,
                int lightDrawRangeCount,
                int lightVisibleInstanceCapacity,
                int pickingDrawCommandCount,
                int pickingDrawRangeCount,
                int pickingVisibleInstanceCapacity,
                int selectionDrawCommandCount,
                int selectionDrawRangeCount,
                int selectionVisibleInstanceCapacity,
                int lastBoundsPageWorkCount,
                int lastBoundsRecordWorkCount,
                int lastCullingSingleMeshCacheRecordCount,
                int lastCullingMultiMeshCacheRecordCount,
                int lastCullingMeshFallbackRecordCount,
                int lastCullingRecordVisibleCacheEntryCount,
                int lastCullingBatchVisibleCacheEntryCount,
                int meshVisibleCountWorkCount,
                int meshVisibleCountOutputCount)
            {
                RenderRecordCount = renderRecordCount;
                LineGroupCount = lineGroupCount;
                EcsLineGroupCount = ecsLineGroupCount;
                EcsLineCount = ecsLineCount;
                EcsMatchedLineCount = ecsMatchedLineCount;
                EcsSkippedLineCount = ecsSkippedLineCount;
                DrawBatchCount = drawBatchCount;
                LastLockCount = lastLockCount;
                LastCopyOperationCount = lastCopyOperationCount;
                LastCopyByteCount = lastCopyByteCount;
                UsesComputeDelta = usesComputeDelta;
                LastDirtyUploadQueueCount = lastDirtyUploadQueueCount;
                LastInvalidDirtyUploadQueueCount = lastInvalidDirtyUploadQueueCount;
                LastDirtyUploadBatchQueueCount = lastDirtyUploadBatchQueueCount;
                LastInvalidDirtyUploadBatchQueueCount = lastInvalidDirtyUploadBatchQueueCount;
                LastUploadRecordWorkCount = lastUploadRecordWorkCount;
                LastUploadBatchWorkCount = lastUploadBatchWorkCount;
                LastUploadPageWorkCount = lastUploadPageWorkCount;
                LastTransformUploadPageWorkCount = lastTransformUploadPageWorkCount;
                LastColorUploadPageWorkCount = lastColorUploadPageWorkCount;
                LastVelocityStretchUploadPageWorkCount = lastVelocityStretchUploadPageWorkCount;
                LastUVUploadPageWorkCount = lastUVUploadPageWorkCount;
                LastCustomDataUploadPageWorkCount = lastCustomDataUploadPageWorkCount;
                LastMeshIndexUploadPageWorkCount = lastMeshIndexUploadPageWorkCount;
                LastExtraDataUploadPageWorkCount = lastExtraDataUploadPageWorkCount;
                LastSharedDataWorkCount = lastSharedDataWorkCount;
                LastUploadCopyWorkCount = lastUploadCopyWorkCount;
                LastMergedUploadCopyWorkCount = lastMergedUploadCopyWorkCount;
                LastUploadColumnMask = lastUploadColumnMask;
                LastUploadDataBits = lastUploadDataBits;
                LastRenderJobModuleFlags = lastRenderJobModuleFlags;
                LastRenderPageJobModuleCount = lastRenderPageJobModuleCount;
                CullingRecordCount = cullingRecordCount;
                DrawCommandCount = drawCommandCount;
                DrawRangeCount = drawRangeCount;
                VisibleInstanceCapacity = visibleInstanceCapacity;
                SortingPositionCapacity = sortingPositionCapacity;
                LightDrawCommandCount = lightDrawCommandCount;
                LightDrawRangeCount = lightDrawRangeCount;
                LightVisibleInstanceCapacity = lightVisibleInstanceCapacity;
                PickingDrawCommandCount = pickingDrawCommandCount;
                PickingDrawRangeCount = pickingDrawRangeCount;
                PickingVisibleInstanceCapacity = pickingVisibleInstanceCapacity;
                SelectionDrawCommandCount = selectionDrawCommandCount;
                SelectionDrawRangeCount = selectionDrawRangeCount;
                SelectionVisibleInstanceCapacity = selectionVisibleInstanceCapacity;
                LastBoundsPageWorkCount = lastBoundsPageWorkCount;
                LastBoundsRecordWorkCount = lastBoundsRecordWorkCount;
                LastCullingSingleMeshCacheRecordCount = lastCullingSingleMeshCacheRecordCount;
                LastCullingMultiMeshCacheRecordCount = lastCullingMultiMeshCacheRecordCount;
                LastCullingMeshFallbackRecordCount = lastCullingMeshFallbackRecordCount;
                LastCullingRecordVisibleCacheEntryCount = lastCullingRecordVisibleCacheEntryCount;
                LastCullingBatchVisibleCacheEntryCount = lastCullingBatchVisibleCacheEntryCount;
                MeshVisibleCountWorkCount = meshVisibleCountWorkCount;
                MeshVisibleCountOutputCount = meshVisibleCountOutputCount;
            }
        }

        internal readonly struct VividParticleSystemRuntimeStats
        {
            public readonly int ParticleCount;
            public readonly float Time;
            public readonly int PageSize;
            public readonly int StorageCapacity;
            public readonly int StoragePageCount;
            public readonly bool UsesEcsStorage;
            public readonly bool HasPendingSimulation;
            public readonly int PendingJobCount;

            public VividParticleSystemRuntimeStats(
                int particleCount,
                float time,
                int pageSize,
                int storageCapacity,
                int storagePageCount,
                bool usesEcsStorage,
                bool hasPendingSimulation,
                int pendingJobCount)
            {
                ParticleCount = particleCount;
                Time = time;
                PageSize = pageSize;
                StorageCapacity = storageCapacity;
                StoragePageCount = storagePageCount;
                UsesEcsStorage = usesEcsStorage;
                HasPendingSimulation = hasPendingSimulation;
                PendingJobCount = pendingJobCount;
            }
        }

        internal readonly struct VividParticleSystemManagerStats
        {
            public readonly bool IsInitialized;
            public readonly int Capacity;
            public readonly int LastUploadedCount;
            public readonly int CullingCallCount;
            public readonly int VisibleCullingCallCount;
            public readonly bool LastVisible;
            public readonly BatchCullingViewType LastViewType;
            public readonly int LastDrawCommandCount;
            public readonly int LastVisibleInstanceCount;
            public readonly int PendingJobCount;
            public readonly int ScheduledJobCount;
            public readonly int CompletedJobCount;
            public readonly int LastScheduledFrame;
            public readonly int LastCompletedFrame;
            public readonly int InstanceDataBufferCount;
            public readonly int LastUploadBufferIndex;
            public readonly int LastUploadOperationCount;
            public readonly int LastUploadByteCount;

            public VividParticleSystemManagerStats(
                bool isInitialized,
                int capacity,
                int lastUploadedCount,
                int cullingCallCount,
                int visibleCullingCallCount,
                bool lastVisible,
                BatchCullingViewType lastViewType,
                int lastDrawCommandCount,
                int lastVisibleInstanceCount,
                int pendingJobCount,
                int scheduledJobCount,
                int completedJobCount,
                int lastScheduledFrame,
                int lastCompletedFrame,
                int instanceDataBufferCount,
                int lastUploadBufferIndex,
                int lastUploadOperationCount,
                int lastUploadByteCount)
            {
                IsInitialized = isInitialized;
                Capacity = capacity;
                LastUploadedCount = lastUploadedCount;
                CullingCallCount = cullingCallCount;
                VisibleCullingCallCount = visibleCullingCallCount;
                LastVisible = lastVisible;
                LastViewType = lastViewType;
                LastDrawCommandCount = lastDrawCommandCount;
                LastVisibleInstanceCount = lastVisibleInstanceCount;
                PendingJobCount = pendingJobCount;
                ScheduledJobCount = scheduledJobCount;
                CompletedJobCount = completedJobCount;
                LastScheduledFrame = lastScheduledFrame;
                LastCompletedFrame = lastCompletedFrame;
                InstanceDataBufferCount = instanceDataBufferCount;
                LastUploadBufferIndex = lastUploadBufferIndex;
                LastUploadOperationCount = lastUploadOperationCount;
                LastUploadByteCount = lastUploadByteCount;
            }
        }

    }

}
