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

        internal const uint PerInstanceMetadataMask = 0x80000000u;
        internal const int SizeOfFloat4 = sizeof(float) * 4;
        internal const int SizeOfParticleGpuData = SizeOfFloat4 * 4;
        internal const int SizeOfSharedGpuData = SizeOfFloat4 * 2;
        internal const int ZeroBlockByteSize = SizeOfFloat4;
        internal const int BillboardPageSize = VividEcsConstants.PageEntryCount;
        internal const int SharedDataFloat4Count = 9;
        internal const int SharedDataByteSize = SharedDataFloat4Count * SizeOfFloat4;
        internal const int SpanSharedDataByteSize = SizeOfFloat4;

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
        private static readonly Dictionary<VividParticleSystem, ParticleSystemState> s_States = new();
        private static readonly List<ParticleSystemState> s_ActiveSimulationStates = new();
        private static readonly Dictionary<ParticleSystemState, int> s_ActiveSimulationIndices = new();
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

        public static int registeredSystemCount => s_States.Count;

        internal static int registeredSimulationJobCount => s_SimulationJobRegistry.count;

        internal static int registeredRenderJobCount => VividParticleRenderJobPipeline.registeredJobCount;

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
            }
        }

        public static void Unregister(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            RemoveActiveSimulationState(state);
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
                state.MarkResourcesDirty();
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
            s_LastPlayerLoopFrame = -1;
            s_LastRendererUpdateFrame = -1;
            s_LastCompleteAndUploadFrame = -1;
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

        internal static VividParticleRendererManagerStats GetRendererStatsForTests()
        {
            s_RendererManager.CompletePendingUpload();
            s_RendererManager.DrainCullingResults();
            return s_RendererManager.stats;
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

        internal static bool IsLayerVisibleInCullingMask(uint cullingLayerMask, int layer)
        {
            layer = Mathf.Clamp(layer, 0, 31);
            return (cullingLayerMask & (1u << layer)) != 0u;
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

        internal static bool ShouldRenderBatchForView(
            ShadowCastingMode shadowCastingMode,
            BatchCullingViewType viewType)
        {
            return viewType != BatchCullingViewType.Light || shadowCastingMode != ShadowCastingMode.Off;
        }

        internal static bool UsesPerInstanceRotationData(VividParticleRenderMode renderMode)
        {
            return false;
        }

        internal static bool UsesPerInstanceVelocityStretchData(VividParticleRenderMode renderMode)
        {
            return renderMode == VividParticleRenderMode.Stretch;
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
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.RefreshEditorSelectionState();

            s_LastRendererUpdateFrame = -1;
            s_LastCompleteAndUploadFrame = -1;
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
            s_RendererManager.SchedulePostSimulationBoundsUpdates(s_States);

            using (s_BRGUploadUpdateAllMarker.Auto())
            {
                s_RendererManager.UpdateAll(s_States, forceUpload);
            }

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
            private readonly VividParticleEcsStorage m_Storage = new();
            private readonly GraphicsBuffer[] m_InstanceDataBuffers = new GraphicsBuffer[InstanceDataBufferCount];
            private readonly InstanceUploadDirtyRanges[] m_InstanceDirtyRanges = CreateInstanceDirtyRanges();
            private BatchRendererGroup m_BRG;
            private GraphicsBuffer m_InstanceData;
            private Mesh m_QuadMesh;
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
            private bool m_RendererInitialized;

            public ParticleSystemState(VividParticleSystem system)
            {
                m_System = system;
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
                    MarkActiveInstanceDataDirty();
            }

            public void ResetEditorUpdateTime()
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    m_LastEditorUpdateTime = EditorApplication.timeSinceStartup;
#endif
            }

            internal void RefreshEditorSelectionState()
            {
#if UNITY_EDITOR
                m_IsEditorSelected = IsSelectedForEditorOutline(m_System);
#else
                m_IsEditorSelected = false;
#endif
            }

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
                MarkRenderStateDirtyIfNeeded(count);
                VividParticleGpuDataLayoutDescriptor gpuLayoutDescriptor =
                    VividParticleGpuDataLayoutDescriptor.Create(m_System.rendererModule);
                VividParticleGpuDataLayout gpuLayout = GetGpuDataLayout(gpuLayoutDescriptor);
                var rendererSharedKey = new VividParticleRendererSharedKey(
                    m_RegisteredMaterial.GetEntityId().GetHashCode(),
                    m_QuadMesh.GetEntityId().GetHashCode(),
                    (int)m_RenderMode,
                    Mathf.Clamp(m_System.gameObject.layer, 0, 31),
                    gpuLayout.Hash,
                    gpuLayout.DataPerSharpBits,
                    (int)m_System.rendererModule.shadowCastingMode,
                    m_System.rendererModule.receiveShadows);
                UpdateRendererSharedKey(rendererSharedKey);
                entry = new ParticleRenderEntry(
                    this,
                    m_RegisteredMaterial,
                    m_QuadMesh,
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
                    m_System.rendererModule.shadowCastingMode,
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
                    m_System.rendererModule.mesh,
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
                if (usesPageBillboard)
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

            internal int AppendCullingRecords(
                int batchBaseIndex,
                int spanBaseIndex,
                bool usesPageBillboard,
                bool isEditorSelected,
                List<ParticleCullingRecord> records)
            {
                if (m_System == null
                    || !m_System.isActiveAndEnabled
                    || !ParticleSystemState.CanRender(m_System.rendererModule)
                    || activeCount <= 0
                    || records == null)
                {
                    return 0;
                }

                if (!usesPageBillboard)
                {
                    EnsureCachedCullingBounds();
                    Bounds bounds = m_CachedWorldBounds;
                    AddCullingRecord(
                        records,
                        bounds,
                        batchBaseIndex,
                        spanBaseIndex,
                        activeCount,
                        usesPageBillboard: false,
                        isEditorSelected);
                    return 1;
                }

                EnsureCachedCullingBounds();
                int addedCount = 0;
                int particleCount = activeCount;
                int pageCount = Mathf.Min(m_CachedPageWorldBoundsCount, GetVisibleInstanceCount(m_System.rendererModule.renderMode, particleCount));
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int pageStart = pageIndex * BillboardPageSize;
                    int pageParticleCount = Mathf.Min(BillboardPageSize, particleCount - pageStart);
                    Bounds pageBounds = m_CachedPageWorldBounds[pageIndex];
                    AddCullingRecord(
                        records,
                        pageBounds,
                        batchBaseIndex + pageStart,
                        spanBaseIndex + addedCount,
                        pageParticleCount,
                        usesPageBillboard: true,
                        isEditorSelected);
                    addedCount++;
                }

                return addedCount;
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
                        m_System.rendererModule.mesh,
                        m_System.rendererModule.sizeScale,
                        m_System.rendererModule.stretchLengthScale,
                        m_System.rendererModule.stretchSpeedScale,
                        out ParticleBoundsSource source))
                {
                    SetEmptyCachedRenderBounds(count);
                    return;
                }

                bool usesPageBillboard = UsesPageBillboardRenderMode(renderMode);
                int pageCount = Mathf.Max(1, GetVisibleInstanceCount(renderMode, count));
                EnsureCachedPageBoundsCapacity(pageCount);

                float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
                float3 max = new(float.MinValue, float.MinValue, float.MinValue);
                bool hasBounds = false;
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int pageStart = pageIndex * BillboardPageSize;
                    int pageCountForBounds = Mathf.Min(BillboardPageSize, count - pageStart);
                    ParticleBoundsData pageBounds = CalculateParticleBoundsPage(source, pageStart, pageCountForBounds);
                    if (usesPageBillboard)
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
                m_CachedPageWorldBoundsCount = usesPageBillboard ? pageCount : 0;
                m_CachedBoundsParticleCount = count;
                m_HasCachedWorldBounds = true;
                m_BoundsDirty = false;
            }

            private static void AddCullingRecord(
                List<ParticleCullingRecord> records,
                Bounds bounds,
                int batchBaseIndex,
                int spanBaseIndex,
                int activeCount,
                bool usesPageBillboard,
                bool isEditorSelected)
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
                return system != null
                    && system.gameObject != null
                    && (Selection.Contains(system.gameObject) || Selection.Contains(system));
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
                    snapshot.StartColor);
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
                    ? rendererModule.mesh
                    : null;
            }

            private static bool CanRender(VividParticleRendererModule rendererModule)
            {
                if (rendererModule == null || !rendererModule.enabled)
                    return false;

                return rendererModule.renderMode switch
                {
                    VividParticleRenderMode.None => false,
                    VividParticleRenderMode.Mesh => rendererModule.mesh != null,
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

            private void MarkActiveInstanceDataDirty()
            {
                MarkInstanceRangeDirty(0, activeCount);
            }

            private void MarkInstanceRangeDirty(int startIndex, int count)
            {
                if (count <= 0)
                    return;

                startIndex = Mathf.Max(0, startIndex);
                count = Mathf.Min(count, Mathf.Max(0, m_Capacity - startIndex));
                if (count <= 0)
                    return;

                m_InstanceDirtyRanges[0].AddInstanceRange(startIndex, count);
                MarkBoundsDirty();
            }

            private void MarkAllInstanceDataDirty(InstanceUploadDirtyRanges ranges)
            {
                ranges.Clear();
                ranges.AddZeroBlock();
                ranges.AddInstanceRange(0, m_Capacity);
                MarkBoundsDirty();
            }

            private void MarkRenderStateDirtyIfNeeded(int count)
            {
                if (m_System == null)
                    return;

                Matrix4x4 localToWorld = m_System.transform.localToWorldMatrix;
                Color rendererColor = m_System.rendererModule.color;
                float sizeScale = m_System.rendererModule.sizeScale;
                float stretchLengthScale = m_System.rendererModule.stretchLengthScale;
                float stretchSpeedScale = m_System.rendererModule.stretchSpeedScale;
                VividParticleRenderMode renderMode = m_System.rendererModule.renderMode;
                int previousActiveCount = m_LastUploadedRenderStateActiveCount;
                bool activeCountChanged = !m_HasUploadedRenderStateSnapshot || previousActiveCount != count;
                bool renderStateChanged = !m_HasUploadedRenderStateSnapshot
                    || m_LastUploadedLocalToWorldMatrix != localToWorld
                    || m_LastUploadedRendererColor != rendererColor
                    || !Mathf.Approximately(m_LastUploadedSizeScale, sizeScale)
                    || !Mathf.Approximately(m_LastUploadedStretchLengthScale, stretchLengthScale)
                    || !Mathf.Approximately(m_LastUploadedStretchSpeedScale, stretchSpeedScale)
                    || m_LastUploadedRenderMode != renderMode;
                if (!renderStateChanged && !activeCountChanged)
                    return;

                if (renderStateChanged)
                {
                    if (count > 0)
                        MarkInstanceRangeDirty(0, count);
                }
                else if (activeCountChanged && count < previousActiveCount)
                {
                    int dirtyStart = UsesPageBillboardRenderMode(renderMode)
                        ? Mathf.Max(0, count / BillboardPageSize * BillboardPageSize)
                        : Mathf.Max(0, count);
                    int dirtyEnd = Mathf.Max(previousActiveCount, count);
                    MarkInstanceRangeDirty(dirtyStart, dirtyEnd - dirtyStart);
                }

                m_LastUploadedLocalToWorldMatrix = localToWorld;
                m_LastUploadedRendererColor = rendererColor;
                m_LastUploadedSizeScale = sizeScale;
                m_LastUploadedStretchLengthScale = stretchLengthScale;
                m_LastUploadedStretchSpeedScale = stretchSpeedScale;
                m_LastUploadedRenderStateActiveCount = count;
                m_LastUploadedRenderMode = renderMode;
                m_HasUploadedRenderStateSnapshot = true;
            }

            internal bool TryGetUploadRange(bool forceFullUpload, out int startIndex, out int count)
            {
                int active = Mathf.Min(activeCount, m_Capacity);
                if (forceFullUpload)
                {
                    startIndex = 0;
                    count = active;
                    return count > 0;
                }

                return m_InstanceDirtyRanges[0].TryGetInstanceRange(active, out startIndex, out count);
            }

            internal void ClearUploadDirty()
            {
                m_InstanceDirtyRanges[0].Clear();
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

                source = new ParticleRenderUploadSource
                {
                    Positions = (float3*)positions.GetUnsafeReadOnlyPtr(),
                    Velocities = (float3*)velocities.GetUnsafeReadOnlyPtr(),
                    StartLifetimes = (float*)startLifetimes.GetUnsafeReadOnlyPtr(),
                    RemainingLifetimes = (float*)remainingLifetimes.GetUnsafeReadOnlyPtr(),
                    Colors = (float4*)colors.GetUnsafeReadOnlyPtr(),
                    Sizes = (float*)sizes.GetUnsafeReadOnlyPtr(),
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
            }

            public VividParticleGpuDataId DataId { get; }

            public VividParticleGpuDataFrequency Frequency { get; }

            public int ElementSize { get; }

            public InstanceUploadSegment UploadSegment { get; }

            public bool IsPerInstance => Frequency == VividParticleGpuDataFrequency.PerInstance;

            public bool UsesInstanceMetadata => Frequency is VividParticleGpuDataFrequency.PerInstance
                or VividParticleGpuDataFrequency.Span;

            public bool HasUploadSegment => UploadSegment != InstanceUploadSegment.ZeroBlock;

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
                    VividParticleGpuDataMode.PerParticle,
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
        }

        internal readonly struct VividParticleGpuDataLayout
        {
            private static readonly Dictionary<VividParticleGpuDataLayoutDescriptor, VividParticleGpuDataLayout> s_LayoutCache = new();
            private readonly VividParticleGpuDataInfo[] m_DataInfos;

            private VividParticleGpuDataLayout(VividParticleGpuDataInfo[] dataInfos)
            {
                m_DataInfos = dataInfos;
                DataPerSharpBits = ComputeDataPerSharpBits(dataInfos);
                Hash = ComputeHash(dataInfos, DataPerSharpBits);
            }

            public int Count => m_DataInfos?.Length ?? 0;

            public uint DataPerSharpBits { get; }

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
                    ResolveFrequency(descriptor.RotationDataMode, perSharpWhenShared: false));
                dataInfos[index++] = CreateInfo(
                    VividParticleGpuDataId.VelocityStretch,
                    descriptor.RenderMode == VividParticleRenderMode.Stretch
                        ? VividParticleGpuDataFrequency.PerInstance
                        : ResolveFrequency(descriptor.VelocityDataMode, perSharpWhenShared: false));

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
                        bits |= 1u << (int)dataInfos[index].DataId;
                }

                return bits;
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
            private bool m_PositionSizeDirty;
            private bool m_BaseColorDirty;
            private bool m_RotationDirty;
            private bool m_VelocityStretchDirty;
            private int m_PositionSizeStart;
            private int m_PositionSizeEnd;
            private int m_BaseColorStart;
            private int m_BaseColorEnd;
            private int m_RotationStart;
            private int m_RotationEnd;
            private int m_VelocityStretchStart;
            private int m_VelocityStretchEnd;

            public int Count
            {
                get
                {
                    int count = m_ZeroBlockDirty ? 1 : 0;
                    count += m_PositionSizeDirty ? 1 : 0;
                    count += m_BaseColorDirty ? 1 : 0;
                    count += m_RotationDirty ? 1 : 0;
                    count += m_VelocityStretchDirty ? 1 : 0;
                    return count;
                }
            }

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

                    if (m_PositionSizeDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.PositionSize, m_PositionSizeStart, m_PositionSizeEnd);

                        index--;
                    }

                    if (m_BaseColorDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.BaseColor, m_BaseColorStart, m_BaseColorEnd);

                        index--;
                    }

                    if (m_RotationDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.Rotation, m_RotationStart, m_RotationEnd);

                        index--;
                    }

                    if (m_VelocityStretchDirty)
                    {
                        if (index == 0)
                            return CreateOperation(
                                InstanceUploadSegment.VelocityStretch,
                                m_VelocityStretchStart,
                                m_VelocityStretchEnd);
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public void AddZeroBlock()
            {
                m_ZeroBlockDirty = true;
            }

            public void AddInstanceRange(int startIndex, int count)
            {
                if (count <= 0)
                    return;

                startIndex = Mathf.Max(0, startIndex);
                AddRange(ref m_PositionSizeDirty, ref m_PositionSizeStart, ref m_PositionSizeEnd, startIndex, count);
                AddRange(ref m_BaseColorDirty, ref m_BaseColorStart, ref m_BaseColorEnd, startIndex, count);
                AddRange(ref m_RotationDirty, ref m_RotationStart, ref m_RotationEnd, startIndex, count);
                AddRange(
                    ref m_VelocityStretchDirty,
                    ref m_VelocityStretchStart,
                    ref m_VelocityStretchEnd,
                    startIndex,
                    count);
            }

            public int EstimateUploadByteCount(int activeCount)
            {
                int byteCount = m_ZeroBlockDirty ? ZeroBlockByteSize : 0;
                byteCount += EstimateRangeByteCount(m_PositionSizeDirty, m_PositionSizeStart, m_PositionSizeEnd, activeCount, SizeOfFloat4);
                byteCount += EstimateRangeByteCount(m_BaseColorDirty, m_BaseColorStart, m_BaseColorEnd, activeCount, SizeOfFloat4);
                byteCount += EstimateRangeByteCount(m_RotationDirty, m_RotationStart, m_RotationEnd, activeCount, SizeOfFloat4);
                byteCount += EstimateRangeByteCount(
                    m_VelocityStretchDirty,
                    m_VelocityStretchStart,
                    m_VelocityStretchEnd,
                    activeCount,
                    SizeOfFloat4);
                return byteCount;
            }

            public bool TryGetInstanceRange(int activeCount, out int startIndex, out int count)
            {
                startIndex = 0;
                count = 0;

                bool hasRange = false;
                int start = int.MaxValue;
                int end = 0;
                AddSegmentRange(m_PositionSizeDirty, m_PositionSizeStart, m_PositionSizeEnd, ref hasRange, ref start, ref end);
                AddSegmentRange(m_BaseColorDirty, m_BaseColorStart, m_BaseColorEnd, ref hasRange, ref start, ref end);
                AddSegmentRange(m_RotationDirty, m_RotationStart, m_RotationEnd, ref hasRange, ref start, ref end);
                AddSegmentRange(
                    m_VelocityStretchDirty,
                    m_VelocityStretchStart,
                    m_VelocityStretchEnd,
                    ref hasRange,
                    ref start,
                    ref end);
                if (!hasRange)
                    return false;

                startIndex = Mathf.Clamp(start, 0, Mathf.Max(0, activeCount));
                int clampedEnd = Mathf.Clamp(end, startIndex, Mathf.Max(0, activeCount));
                count = clampedEnd - startIndex;
                return count > 0;
            }

            public void Compact()
            {
            }

            public void Clear()
            {
                m_ZeroBlockDirty = false;
                m_PositionSizeDirty = false;
                m_BaseColorDirty = false;
                m_RotationDirty = false;
                m_VelocityStretchDirty = false;
                m_PositionSizeStart = 0;
                m_PositionSizeEnd = 0;
                m_BaseColorStart = 0;
                m_BaseColorEnd = 0;
                m_RotationStart = 0;
                m_RotationEnd = 0;
                m_VelocityStretchStart = 0;
                m_VelocityStretchEnd = 0;
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

            private static int EstimateRangeByteCount(
                bool dirty,
                int start,
                int end,
                int activeCount,
                int elementByteSize)
            {
                if (!dirty)
                    return 0;

                int count = Mathf.Clamp(activeCount - start, 0, end - start);
                return count * elementByteSize;
            }

            private static void AddSegmentRange(
                bool dirty,
                int segmentStart,
                int segmentEnd,
                ref bool hasRange,
                ref int start,
                ref int end)
            {
                if (!dirty)
                    return;

                hasRange = true;
                start = Mathf.Min(start, segmentStart);
                end = Mathf.Max(end, segmentEnd);
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
            public readonly Material Material;
            public readonly Mesh Mesh;
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
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly bool ReceiveShadows;
            public readonly EntityId PickingEntityId;
            public readonly bool IsEditorSelected;
            public readonly VividParticleSortMode SortMode;

            public ParticleRenderEntry(
                ParticleSystemState state,
                Material material,
                Mesh mesh,
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
                ShadowCastingMode shadowCastingMode,
                bool receiveShadows,
                EntityId pickingEntityId,
                bool isEditorSelected,
                VividParticleSortMode sortMode)
            {
                State = state;
                Material = material;
                Mesh = mesh;
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
                ShadowCastingMode = shadowCastingMode;
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
                    hash = (hash * 397) ^ ReceiveShadows.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class ParticleRenderRecord
        {
            public ParticleSystemState State;
            public Material Material;
            public Mesh Mesh;
            public VividParticleRenderMode RenderMode;
            public VividParticleGpuDataLayout GpuLayout;
            public VividParticleRendererSharedKey RendererSharedKey;
            public int Layer;
            public int Capacity;
            public int ActiveCount;
            public ParticleDrawKey Key;
            public ParticleDrawBatch Batch;
            public int BatchBaseIndex;
            public int SharpIndex;
            public int SpanBaseIndex;
            public int SpanCapacity;
            public Matrix4x4 LocalToWorldMatrix;
            public Color RendererColor;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
            public ShadowCastingMode ShadowCastingMode;
            public bool ReceiveShadows;
            public EntityId PickingEntityId;
            public bool IsEditorSelected;
            public VividParticleSortMode SortMode;
            public bool RequiresSortingPositions;
            public int LastUploadOperationCount;
            public int LastUploadByteCount;

            public void Update(ParticleRenderEntry entry)
            {
                State = entry.State;
                Material = entry.Material;
                Mesh = entry.Mesh;
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
                ShadowCastingMode = entry.ShadowCastingMode;
                ReceiveShadows = entry.ReceiveShadows;
                PickingEntityId = entry.PickingEntityId;
                IsEditorSelected = entry.IsEditorSelected;
                SortMode = entry.SortMode;
                RequiresSortingPositions = VividParticleSystemManager.RequiresSortingPositions(SortMode);
                Key = new ParticleDrawKey(entry);
            }
        }

        private sealed class ParticleDrawBatch
        {
            public readonly List<ParticleRenderRecord> Records = new();
            public ParticleDrawKey Key;
            public Material Material;
            public Mesh Mesh;
            public BatchID BatchId;
            public BatchMeshID MeshId;
            public BatchMaterialID MaterialId;
            public ShadowCastingMode ShadowCastingMode;
            public bool ReceiveShadows;
            public bool UsesPageBillboard;
            public bool RequiresSortingPositions;
            public VividParticleGpuDataLayout GpuLayout;
            public VividParticleGpuBufferDataInfo[] GpuBufferInfos = Array.Empty<VividParticleGpuBufferDataInfo>();
            public int Capacity;
            public int SharpCapacity;
            public int SpanCapacity;
            public int DataOffset;
            public bool ZeroBlockDirty;
            public bool ZeroBlockUploadPending;

            public bool TryGetBufferInfo(
                VividParticleGpuDataId dataId,
                out VividParticleGpuBufferDataInfo bufferInfo)
            {
                for (int index = 0; index < GpuBufferInfos.Length; index++)
                {
                    bufferInfo = GpuBufferInfos[index];
                    if (bufferInfo.DataInfo.DataId == dataId)
                        return true;
                }

                bufferInfo = default;
                return false;
            }
        }

        private struct ParticleUploadWork
        {
            public ParticleRenderRecord Record;
            public int StartIndex;
            public int Count;
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
            public float4 RendererColor;
        }

        private struct ParticleRenderUploadColumnWork
        {
            public ParticleRenderUploadSource Source;
            public InstanceUploadSegment Segment;
            public int ColumnByteOffset;
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
            public int UsesPageBillboard;
            public int RenderMode;
            public uint DataPerSharpBits;
            public float SizeScale;
            public float StretchLengthScale;
            public float StretchSpeedScale;
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

        private struct ParticleDrawCommandInput
        {
            public int RecordStart;
            public int RecordCount;
            public int VisibleOffset;
            public int MaxVisibleCount;
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
            private static readonly ProfilerMarker s_CommitMarker = new("VividRP.Particle.Manager.BRGUpload.Commit");
            private static readonly ProfilerMarker s_BoundsCollectMarker = new("VividRP.Particle.Manager.Bounds.Collect");
            private static readonly ProfilerMarker s_BoundsScheduleMarker = new("VividRP.Particle.Manager.Bounds.Schedule");
            private static readonly ProfilerMarker s_BoundsCompleteMarker = new("VividRP.Particle.Manager.Bounds.Complete");
            private static readonly ProfilerMarker s_RefreshFastCullingFlagsMarker = new("VividRP.Particle.Manager.BRGUpload.RefreshFastCullingFlags");
            private static readonly ProfilerMarker s_RebuildCullingLayoutMarker = new("VividRP.Particle.Manager.BRGUpload.RebuildCullingLayout");
            private static readonly ProfilerMarker s_UploadCollectDirtyMarker = new("VividRP.Particle.Manager.BRGUpload.Upload.CollectDirty");
            private static readonly ProfilerMarker s_UploadLockBufferMarker = new("VividRP.Particle.Manager.BRGUpload.Upload.LockBuffer");
            private static readonly ProfilerMarker s_UploadBuildWorksMarker = new("VividRP.Particle.Manager.BRGUpload.Upload.BuildWorks");
            private static readonly ProfilerMarker s_UploadCopyWorkArraysMarker = new("VividRP.Particle.Manager.BRGUpload.Upload.CopyWorkArrays");
            private static readonly ProfilerMarker s_UploadScheduleJobsMarker = new("VividRP.Particle.Manager.BRGUpload.Upload.ScheduleJobs");
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
            private readonly Dictionary<ParticleDrawKey, ParticleDrawBatch> m_BatchLookup = new();
            private readonly List<ParticleDrawBatch> m_DrawBatches = new();
            private readonly List<ParticleRenderRecord> m_RemoveRecords = new();
            private readonly HashSet<ParticleSystemState> m_SeenStates = new();
            private readonly List<ParticleUploadWork> m_UploadWorks = new();
            private readonly List<ParticleRenderUploadColumnWork> m_UploadColumnWorks = new();
            private readonly List<ParticleRenderSharedDataWork> m_SharedDataWorks = new();
            private readonly List<ParticleCullingRecord> m_CullingRecords = new();
            private readonly List<ParticleSystemState> m_BoundsStates = new();
            private readonly Dictionary<ParticleMaterialVariantKey, Material> m_DefaultMaterials = new();
            private readonly VividParticleGPUBuffer m_GPUBuffer = new();
            private NativeList<ParticleCullingRecord> m_NativeCullingRecords;
            private NativeList<ParticleDrawCommandInput> m_NativeDrawCommandInputs;
            private NativeList<ParticleDrawCommandInput> m_NativePickingDrawCommandInputs;
            private NativeList<ParticleBoundsPageWork> m_BoundsPageWorks;
            private NativeList<ParticleBoundsRecordReduceWork> m_BoundsRecordWorks;
            private NativeArray<ParticleBoundsData> m_BoundsPageResults;
            private NativeArray<ParticleBoundsRecordResult> m_BoundsRecordResults;
            private NativeArray<ParticleRenderUploadColumnWork> m_UploadColumnWorkBuffer;
            private NativeArray<ParticleRenderSharedDataWork> m_SharedDataWorkBuffer;
            private NativeArray<ParticleRenderUploadColumnWork> m_PendingUploadColumnWorks;
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
            private int m_NativePickingVisibleInstanceCapacity;
            private int m_TotalBufferByteSize;

            public bool hasPendingUpload => m_HasPendingUpload;

            public VividParticleRendererManagerStats stats => new(
                m_Records.Count,
                m_DrawBatches.Count,
                m_GPUBuffer.lastLockCount,
                m_GPUBuffer.lastCopyOperationCount,
                m_GPUBuffer.lastCopyByteCount,
                m_GPUBuffer.usesComputeDelta,
                m_UploadColumnWorks.Count,
                m_SharedDataWorks.Count);

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
                Dictionary<VividParticleSystem, ParticleSystemState> states,
                bool forceUpload)
            {
                m_SeenStates.Clear();
                using (s_UpdateRecordsMarker.Auto())
                {
                    foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in states)
                    {
                        ParticleSystemState state = pair.Value;
                        if (state == null)
                            continue;

                        m_SeenStates.Add(state);
                        UpdateRecord(state, forceUpload);
                    }
                }

                using (s_RemoveRecordsMarker.Auto())
                {
                    m_RemoveRecords.Clear();
                    foreach (ParticleRenderRecord record in m_Records.Values)
                    {
                        if (!m_SeenStates.Contains(record.State))
                            m_RemoveRecords.Add(record);
                    }

                    for (int index = 0; index < m_RemoveRecords.Count; index++)
                        RemoveRecord(m_RemoveRecords[index].State);
                }

                Commit();
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

            public void SchedulePostSimulationBoundsUpdates(Dictionary<VividParticleSystem, ParticleSystemState> states)
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
                m_BatchLookup.Clear();
                m_DrawBatches.Clear();
                m_RemoveRecords.Clear();
                m_SeenStates.Clear();
                m_UploadWorks.Clear();
                m_UploadColumnWorks.Clear();
                m_SharedDataWorks.Clear();
                m_CullingRecords.Clear();
                m_BoundsStates.Clear();
                DisposeNativeBoundsLayout();
                DisposePendingUploadArrays();
                DisposeNativeCullingLayout();
                foreach (Material material in m_DefaultMaterials.Values)
                    CoreUtils.Destroy(material);

                m_DefaultMaterials.Clear();
                m_LayoutDirty = true;
                m_ForceFullUpload = false;
                m_AnyShadowCastingBatch = false;
                m_AnySelectedRecord = false;
                m_NativeVisibleInstanceCapacity = 0;
                m_NativePickingVisibleInstanceCapacity = 0;
                m_TotalBufferByteSize = 0;
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
                    m_Records.Add(state, record);
                    m_LayoutDirty = true;
                    return;
                }

                if (!record.Key.Equals(key) || record.Capacity != entry.Capacity)
                    m_LayoutDirty = true;

                record.Update(entry);
            }

            private void RemoveRecord(ParticleSystemState state)
            {
                if (state == null || !m_Records.TryGetValue(state, out ParticleRenderRecord record))
                    return;

                record.State.SetRendererUploadStats(false, 0, 0, 0, m_GPUBuffer.bufferIndex);
                record.State.ResetRendererCullingStats();
                m_Records.Remove(state);
                m_LayoutDirty = true;
            }

            private void Commit()
            {
                using (s_CommitMarker.Auto())
                {
                    if (m_LayoutDirty)
                        RebuildBatches();

                    if (!m_HasPendingBounds)
                        ScheduleBoundsUpdatesFromRecords();
                    RefreshFastCullingFlags();
                    CompletePendingBoundsUpdates();
                    RebuildNativeCullingLayout();
                    ScheduleUpload();
                }
            }

            private void ScheduleBoundsUpdates(Dictionary<VividParticleSystem, ParticleSystemState> states)
            {
                CompletePendingBoundsUpdates();
                EnsureNativeBoundsLayout();
                m_BoundsStates.Clear();
                m_BoundsPageWorks.Clear();
                m_BoundsRecordWorks.Clear();

                using (s_BoundsCollectMarker.Auto())
                {
                    foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in states)
                        CollectBoundsState(pair.Value);
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
                    foreach (ParticleRenderRecord record in m_Records.Values)
                        CollectBoundsState(record?.State);
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
                int pageCount = Mathf.Max(1, GetVisibleInstanceCount(renderMode, count));
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    int particleStart = pageIndex * BillboardPageSize;
                    m_BoundsPageWorks.Add(new ParticleBoundsPageWork
                    {
                        Source = source,
                        ParticleStart = particleStart,
                        ParticleCount = Mathf.Min(BillboardPageSize, count - particleStart),
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
                    return;

                EnsureBoundsResultCapacity(m_BoundsPageWorks.Length, m_BoundsRecordWorks.Length);

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

            private void RefreshFastCullingFlags()
            {
                using (s_RefreshFastCullingFlagsMarker.Auto())
                {
                    m_AnyShadowCastingBatch = false;
                    for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                    {
                        if (m_DrawBatches[batchIndex].ShadowCastingMode == ShadowCastingMode.Off)
                            continue;

                        m_AnyShadowCastingBatch = true;
                        break;
                    }

                    m_AnySelectedRecord = false;
                    foreach (ParticleRenderRecord record in m_Records.Values)
                    {
                        if (!record.IsEditorSelected)
                            continue;

                        m_AnySelectedRecord = true;
                        break;
                    }
                }
            }

            private void RebuildNativeCullingLayout()
            {
                using (s_RebuildCullingLayoutMarker.Auto())
                {
                    DrainCullingResults();
                    EnsureNativeCullingLayout();
                    m_NativeCullingRecords.Clear();
                    m_NativeDrawCommandInputs.Clear();
                    m_NativePickingDrawCommandInputs.Clear();
                    m_NativeVisibleInstanceCapacity = 0;
                    m_NativePickingVisibleInstanceCapacity = 0;
                    m_CullingRecords.Clear();

                    for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                    {
                        ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                        int layer = Mathf.Clamp(batch.Key.Layer, 0, 31);
                        int batchRecordStart = m_NativeCullingRecords.Length;
                        int batchMaxVisibleCount = 0;
                        int batchVisibleOffset = m_NativeVisibleInstanceCapacity;

                        for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                        {
                            ParticleRenderRecord record = batch.Records[recordIndex];
                            int recordStart = m_NativeCullingRecords.Length;
                            m_CullingRecords.Clear();
                            int cullingRecordCount = record.State.AppendCullingRecords(
                                record.BatchBaseIndex,
                                record.SpanBaseIndex,
                                batch.UsesPageBillboard,
                                record.IsEditorSelected,
                                m_CullingRecords);
                            if (cullingRecordCount <= 0)
                                continue;

                            for (int cullingRecordIndex = 0; cullingRecordIndex < cullingRecordCount; cullingRecordIndex++)
                                m_NativeCullingRecords.Add(m_CullingRecords[cullingRecordIndex]);

                            int recordMaxVisibleCount = GetVisibleInstanceCount(record.RenderMode, record.ActiveCount);
                            if (recordMaxVisibleCount <= 0)
                                continue;

                            ParticleDrawCommandInput pickingCommand = CreateDrawCommandInput(
                                batch,
                                recordStart,
                                cullingRecordCount,
                                m_NativePickingVisibleInstanceCapacity,
                                recordMaxVisibleCount,
                                layer,
                                record.PickingEntityId,
                                record.RequiresSortingPositions);
                            m_NativePickingDrawCommandInputs.Add(pickingCommand);
                            m_NativePickingVisibleInstanceCapacity += recordMaxVisibleCount;
                            batchMaxVisibleCount += recordMaxVisibleCount;
                        }

                        int batchRecordCount = m_NativeCullingRecords.Length - batchRecordStart;
                        if (batchRecordCount <= 0 || batchMaxVisibleCount <= 0)
                            continue;

                        ParticleDrawCommandInput command = CreateDrawCommandInput(
                            batch,
                            batchRecordStart,
                            batchRecordCount,
                            batchVisibleOffset,
                            batchMaxVisibleCount,
                            layer,
                            EntityId.None,
                            batch.RequiresSortingPositions);
                        m_NativeDrawCommandInputs.Add(command);
                        m_NativeVisibleInstanceCapacity += batchMaxVisibleCount;
                    }

                    m_CullingRecords.Clear();
                }
            }

            private void RebuildBatches()
            {
                using (s_RebuildBatchesMarker.Auto())
                {
                    DrainCullingResults();
                    m_BatchLookup.Clear();
                    m_DrawBatches.Clear();
                    m_TotalBufferByteSize = 0;

                    foreach (ParticleRenderRecord record in m_Records.Values)
                    {
                        if (!m_BatchLookup.TryGetValue(record.Key, out ParticleDrawBatch batch))
                        {
                            batch = new ParticleDrawBatch
                            {
                                Key = record.Key,
                                Material = record.Material,
                                Mesh = record.Mesh,
                                ShadowCastingMode = record.ShadowCastingMode,
                                ReceiveShadows = record.ReceiveShadows,
                                UsesPageBillboard = UsesPageBillboardRenderMode(record.RenderMode),
                                RequiresSortingPositions = false,
                                GpuLayout = record.GpuLayout,
                                BatchId = BatchID.Null,
                                ZeroBlockDirty = true,
                            };
                            m_BatchLookup.Add(record.Key, batch);
                            m_DrawBatches.Add(batch);
                        }

                        batch.Records.Add(record);
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
                        batch.SharpCapacity = Mathf.Max(1, batch.Records.Count);
                        batch.SpanCapacity = Mathf.Max(1, batch.SpanCapacity);
                        batch.DataOffset = AlignTo16(m_TotalBufferByteSize);
                        batch.GpuBufferInfos = batch.GpuLayout.CreateBufferInfos(
                            batch.Capacity,
                            batch.SharpCapacity,
                            batch.SpanCapacity);
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
#endif

                for (int index = 0; index < m_DrawBatches.Count; index++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[index];
                    batch.MeshId = m_BRG.RegisterMesh(batch.Mesh);
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

            private void ScheduleUpload()
            {
                using (s_UploadMarker.Auto())
                {
                    bool forceFullUpload = m_ForceFullUpload;
                    bool hasUpload = false;
                    using (s_UploadCollectDirtyMarker.Auto())
                    {
                        m_UploadWorks.Clear();
                        m_UploadColumnWorks.Clear();
                        m_SharedDataWorks.Clear();

                        for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                        {
                            if (m_DrawBatches[batchIndex].ZeroBlockDirty)
                                hasUpload = true;
                        }

                        foreach (ParticleRenderRecord record in m_Records.Values)
                        {
                            record.LastUploadOperationCount = 0;
                            record.LastUploadByteCount = 0;
                            if (record.State.TryGetUploadRange(forceFullUpload, out int startIndex, out int count))
                            {
                                m_UploadWorks.Add(new ParticleUploadWork
                                {
                                    Record = record,
                                    StartIndex = startIndex,
                                    Count = count,
                                });
                                hasUpload = true;
                            }
                        }
                    }

                    if (!hasUpload)
                    {
                        m_GPUBuffer.ResetLastUploadStats();
                        PublishCleanStats();
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
                        PublishCleanStats();
                        return;
                    }

                    try
                    {
                        using (s_UploadBuildWorksMarker.Auto())
                        {
                            AddBatchSharedDataWorks(bufferBase);

                            for (int workIndex = 0; workIndex < m_UploadWorks.Count; workIndex++)
                            {
                                ParticleUploadWork work = m_UploadWorks[workIndex];
                                ParticleRenderRecord record = work.Record;
                                ParticleDrawBatch batch = record.Batch;
                                int count = Mathf.Clamp(work.Count, 0, Mathf.Max(0, record.ActiveCount - work.StartIndex));
                                if (count <= 0)
                                    continue;

                                if (record.State.TryCreateRenderUploadSource(
                                    record.BatchBaseIndex,
                                    batch.Capacity,
                                    batch.DataOffset,
                                    bufferBase,
                                    out ParticleRenderUploadSource source))
                                {
                                    AddUploadColumnWorks(batch, source, work.StartIndex, count);
                                }

                                AddRecordSharedDataWorks(bufferBase, batch, record, work.StartIndex, count);
                            }
                        }

                        ClearPendingUploadViews();
                        if (m_UploadColumnWorks.Count > 0 || m_SharedDataWorks.Count > 0)
                        {
                            using (s_UploadCopyWorkArraysMarker.Auto())
                            {
                                if (m_UploadColumnWorks.Count > 0)
                                {
                                    EnsureUploadColumnWorkCapacity(m_UploadColumnWorks.Count);
                                    for (int workIndex = 0; workIndex < m_UploadColumnWorks.Count; workIndex++)
                                        m_UploadColumnWorkBuffer[workIndex] = m_UploadColumnWorks[workIndex];

                                    m_PendingUploadColumnWorks = m_UploadColumnWorkBuffer.GetSubArray(
                                        0,
                                        m_UploadColumnWorks.Count);
                                }

                                if (m_SharedDataWorks.Count > 0)
                                {
                                    EnsureSharedDataWorkCapacity(m_SharedDataWorks.Count);
                                    for (int workIndex = 0; workIndex < m_SharedDataWorks.Count; workIndex++)
                                        m_SharedDataWorkBuffer[workIndex] = m_SharedDataWorks[workIndex];

                                    m_PendingSharedDataWorks = m_SharedDataWorkBuffer.GetSubArray(
                                        0,
                                        m_SharedDataWorks.Count);
                                }
                            }

                            using (s_UploadScheduleJobsMarker.Auto())
                            {
                                m_PendingUploadHandle = VividParticleRenderJobPipeline.Schedule(
                                    m_PendingUploadColumnWorks,
                                    m_PendingSharedDataWorks);
                                JobHandle.ScheduleBatchedJobs();
                            }
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

                    for (int workIndex = 0; workIndex < m_UploadWorks.Count; workIndex++)
                        m_UploadWorks[workIndex].Record.State.ClearUploadDirty();

                    PublishCleanStats();
                    m_ForceFullUpload = false;
                    m_HasPendingUpload = false;
                }
            }

            private void ClearPendingUploadViews()
            {
                m_PendingUploadColumnWorks = default;
                m_PendingSharedDataWorks = default;
            }

            private void DisposePendingUploadArrays()
            {
                ClearPendingUploadViews();

                if (m_UploadColumnWorkBuffer.IsCreated)
                    m_UploadColumnWorkBuffer.Dispose();

                if (m_SharedDataWorkBuffer.IsCreated)
                    m_SharedDataWorkBuffer.Dispose();

                m_UploadColumnWorkBuffer = default;
                m_SharedDataWorkBuffer = default;
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

                if (!m_NativePickingDrawCommandInputs.IsCreated)
                    m_NativePickingDrawCommandInputs = new NativeList<ParticleDrawCommandInput>(16, Allocator.Persistent);
            }

            private void DisposeNativeCullingLayout()
            {
                if (m_NativeCullingRecords.IsCreated)
                    m_NativeCullingRecords.Dispose();

                if (m_NativeDrawCommandInputs.IsCreated)
                    m_NativeDrawCommandInputs.Dispose();

                if (m_NativePickingDrawCommandInputs.IsCreated)
                    m_NativePickingDrawCommandInputs.Dispose();

                m_NativeCullingRecords = default;
                m_NativeDrawCommandInputs = default;
                m_NativePickingDrawCommandInputs = default;
                m_NativeVisibleInstanceCapacity = 0;
                m_NativePickingVisibleInstanceCapacity = 0;
            }

            private void EnsureUploadColumnWorkCapacity(int requestedCount)
            {
                requestedCount = Mathf.Max(1, requestedCount);
                if (m_UploadColumnWorkBuffer.IsCreated && m_UploadColumnWorkBuffer.Length >= requestedCount)
                    return;

                if (m_UploadColumnWorkBuffer.IsCreated)
                    m_UploadColumnWorkBuffer.Dispose();

                m_UploadColumnWorkBuffer = new NativeArray<ParticleRenderUploadColumnWork>(
                    requestedCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            private void EnsureSharedDataWorkCapacity(int requestedCount)
            {
                requestedCount = Mathf.Max(1, requestedCount);
                if (m_SharedDataWorkBuffer.IsCreated && m_SharedDataWorkBuffer.Length >= requestedCount)
                    return;

                if (m_SharedDataWorkBuffer.IsCreated)
                    m_SharedDataWorkBuffer.Dispose();

                m_SharedDataWorkBuffer = new NativeArray<ParticleRenderSharedDataWork>(
                    requestedCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            private void AddPendingUploadCopyOperations()
            {
                AddPendingZeroBlockCopyOperations();
                AddBatchSharedCopyOperations();

                for (int workIndex = 0; workIndex < m_UploadWorks.Count; workIndex++)
                {
                    ParticleUploadWork work = m_UploadWorks[workIndex];
                    ParticleRenderRecord record = work.Record;
                    if (record == null || record.Batch == null)
                        continue;

                    int count = Mathf.Clamp(work.Count, 0, Mathf.Max(0, record.ActiveCount - work.StartIndex));
                    if (count > 0)
                        AddGpuDataCopyOperations(record, record.Batch, work.StartIndex, count);
                }
            }

            private void AddPendingZeroBlockCopyOperations()
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    if (!batch.ZeroBlockUploadPending)
                        continue;

                    m_GPUBuffer.AddCopyOperation(batch.DataOffset, batch.DataOffset, ZeroBlockByteSize);
                    batch.ZeroBlockUploadPending = false;
                }
            }

            private void AddUploadColumnWorks(
                ParticleDrawBatch batch,
                ParticleRenderUploadSource source,
                int startIndex,
                int count)
            {
                int endIndex = startIndex + count;
                for (int pageStart = startIndex; pageStart < endIndex; pageStart += BillboardPageSize)
                {
                    int pageCount = Mathf.Min(BillboardPageSize, endIndex - pageStart);
                    for (int dataIndex = 0; dataIndex < batch.GpuBufferInfos.Length; dataIndex++)
                    {
                        VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[dataIndex];
                        if (!bufferInfo.DataInfo.IsPerInstance || !bufferInfo.DataInfo.HasUploadSegment)
                            continue;

                        AddUploadColumnWork(source, bufferInfo, pageStart, pageCount);
                    }
                }
            }

            private void AddUploadColumnWork(
                ParticleRenderUploadSource source,
                VividParticleGpuBufferDataInfo bufferInfo,
                int startIndex,
                int count)
            {
                source.StartIndex = startIndex;
                source.Count = count;
                m_UploadColumnWorks.Add(new ParticleRenderUploadColumnWork
                {
                    Source = source,
                    Segment = bufferInfo.DataInfo.UploadSegment,
                    ColumnByteOffset = bufferInfo.ByteOffset,
                });
            }

            private void AddBatchSharedDataWorks(byte* bufferBase)
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    if (batch.ZeroBlockDirty)
                    {
                        UnsafeUtility.MemClear(bufferBase + batch.DataOffset, ZeroBlockByteSize);
                        batch.ZeroBlockDirty = false;
                        batch.ZeroBlockUploadPending = true;
                        if (batch.Records.Count > 0)
                        {
                            ParticleRenderRecord owner = batch.Records[0];
                            owner.LastUploadOperationCount++;
                            owner.LastUploadByteCount += ZeroBlockByteSize;
                        }
                    }

                    for (int dataIndex = 0; dataIndex < batch.GpuBufferInfos.Length; dataIndex++)
                    {
                        VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[dataIndex];
                        if (bufferInfo.DataInfo.Frequency != VividParticleGpuDataFrequency.Shared)
                            continue;

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
                    }
                }
            }

            private void AddBatchSharedCopyOperations()
            {
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    if (batch.Records.Count <= 0)
                        continue;

                    ParticleRenderRecord owner = batch.Records[0];
                    for (int dataIndex = 0; dataIndex < batch.GpuBufferInfos.Length; dataIndex++)
                    {
                        VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[dataIndex];
                        if (bufferInfo.DataInfo.Frequency != VividParticleGpuDataFrequency.Shared)
                            continue;

                        AddGpuDataCopyOperation(owner, batch, bufferInfo, elementStart: 0, elementCount: 1);
                    }
                }
            }

            private void AddRecordSharedDataWorks(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record,
                int startIndex,
                int count)
            {
                AddPerSharpSharedDataWork(bufferBase, batch, record);
                AddPerSharpGpuDataWorks(bufferBase, batch, record);
                AddSpanSharedDataWork(bufferBase, batch, record, startIndex, count);
            }

            private void AddPerSharpSharedDataWork(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record)
            {
                if (!batch.TryGetBufferInfo(VividParticleGpuDataId.SharedData, out VividParticleGpuBufferDataInfo bufferInfo))
                    return;

                m_SharedDataWorks.Add(new ParticleRenderSharedDataWork
                {
                    BufferBase = bufferBase,
                    Kind = SharedDataWorkKindPerSharp,
                    BatchDataOffset = batch.DataOffset,
                    ColumnByteOffset = bufferInfo.ByteOffset,
                    ElementStart = record.SharpIndex,
                    ElementCount = 1,
                    SharpIndex = record.SharpIndex,
                    ActiveCount = record.ActiveCount,
                    UsesPageBillboard = batch.UsesPageBillboard ? 1 : 0,
                    RenderMode = (int)record.RenderMode,
                    DataPerSharpBits = batch.GpuLayout.DataPerSharpBits,
                    SizeScale = record.SizeScale,
                    StretchLengthScale = record.StretchLengthScale,
                    StretchSpeedScale = record.StretchSpeedScale,
                    LocalToWorld = ToFloat4x4(record.LocalToWorldMatrix),
                    RendererColor = ToFloat4(record.RendererColor),
                });
            }

            private void AddPerSharpGpuDataWorks(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record)
            {
                for (int dataIndex = 0; dataIndex < batch.GpuBufferInfos.Length; dataIndex++)
                {
                    VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[dataIndex];
                    if (bufferInfo.DataInfo.Frequency != VividParticleGpuDataFrequency.PerSharp
                        || bufferInfo.DataInfo.DataId == VividParticleGpuDataId.SharedData)
                    {
                        continue;
                    }

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
                }
            }

            private void AddSpanSharedDataWork(
                byte* bufferBase,
                ParticleDrawBatch batch,
                ParticleRenderRecord record,
                int startIndex,
                int count)
            {
                if (!batch.TryGetBufferInfo(VividParticleGpuDataId.SpanSharedData, out VividParticleGpuBufferDataInfo bufferInfo))
                    return;

                if (!TryGetRecordElementCopyRange(
                    bufferInfo.DataInfo,
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
                    ColumnByteOffset = bufferInfo.ByteOffset,
                    ElementStart = spanElementStart,
                    ElementCount = spanElementCount,
                    SharpIndex = record.SharpIndex,
                    SpanBaseIndex = record.SpanBaseIndex,
                    BatchBaseIndex = record.BatchBaseIndex,
                    ActiveCount = record.ActiveCount,
                    UsesPageBillboard = batch.UsesPageBillboard ? 1 : 0,
                });
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

            private void AddGpuDataCopyOperations(
                ParticleRenderRecord record,
                ParticleDrawBatch batch,
                int startIndex,
                int count)
            {
                for (int dataIndex = 0; dataIndex < batch.GpuBufferInfos.Length; dataIndex++)
                {
                    VividParticleGpuBufferDataInfo bufferInfo = batch.GpuBufferInfos[dataIndex];
                    if (bufferInfo.DataInfo.Frequency == VividParticleGpuDataFrequency.Shared)
                        continue;

                    if (!TryGetRecordElementCopyRange(
                        bufferInfo.DataInfo,
                        record,
                        startIndex,
                        count,
                        out int elementStart,
                        out int elementCount))
                    {
                        continue;
                    }

                    AddGpuDataCopyOperation(record, batch, bufferInfo, elementStart, elementCount);
                }
            }

            private static bool TryGetRecordElementCopyRange(
                VividParticleGpuDataInfo dataInfo,
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

                switch (dataInfo.Frequency)
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
                ParticleDrawBatch batch,
                VividParticleGpuBufferDataInfo bufferInfo,
                int elementStart,
                int elementCount)
            {
                if (elementCount <= 0)
                    return;

                int byteCount = elementCount * bufferInfo.DataInfo.ElementSize;
                int byteOffset = batch.DataOffset
                    + bufferInfo.ByteOffset
                    + elementStart * bufferInfo.DataInfo.ElementSize;
                m_GPUBuffer.AddCopyOperation(byteOffset, byteOffset, byteCount);
                if (owner != null)
                {
                    owner.LastUploadOperationCount++;
                    owner.LastUploadByteCount += byteCount;
                }
            }

            private void PublishCleanStats()
            {
                foreach (ParticleRenderRecord record in m_Records.Values)
                {
                    record.State.SetRendererUploadStats(
                        true,
                        record.ActiveCount,
                        record.LastUploadOperationCount,
                        record.LastUploadByteCount,
                        m_GPUBuffer.bufferIndex);
                }
            }

            public void DrainCullingResults()
            {
                if (!m_HasPendingCullingOutput)
                    return;

                m_PendingCullingOutputHandle.Complete();
                m_PendingCullingOutputHandle = default;
                m_HasPendingCullingOutput = false;
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

                bool isPerRecordPickingView = IsPickingOrSelectionView(cullingContext.viewType);
                NativeArray<ParticleDrawCommandInput> commands = isPerRecordPickingView
                    ? (m_NativePickingDrawCommandInputs.IsCreated ? m_NativePickingDrawCommandInputs.AsArray() : default)
                    : (m_NativeDrawCommandInputs.IsCreated ? m_NativeDrawCommandInputs.AsArray() : default);
                NativeArray<ParticleCullingRecord> cullingRecords = m_NativeCullingRecords.IsCreated
                    ? m_NativeCullingRecords.AsArray()
                    : default;
                int drawCommandCount = commands.IsCreated ? commands.Length : 0;
                int visibleInstanceCount = isPerRecordPickingView
                    ? m_NativePickingVisibleInstanceCapacity
                    : m_NativeVisibleInstanceCapacity;
                if (drawCommandCount <= 0 || visibleInstanceCount <= 0 || !cullingRecords.IsCreated || cullingRecords.Length <= 0)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                bool hasSortingPositions = RequiresSortingPositions(commands);
                var draws = new BatchCullingOutputDrawCommands
                {
                    drawCommandCount = drawCommandCount,
                    drawRangeCount = drawCommandCount,
                    visibleInstanceCount = visibleInstanceCount,
                    drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawCommand>() * drawCommandCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawRange>() * drawCommandCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    visibleInstances = (int*)UnsafeUtility.Malloc(
                        sizeof(int) * visibleInstanceCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawCommandPickingEntityIds = isPerRecordPickingView
                        ? (EntityId*)UnsafeUtility.Malloc(
                            UnsafeUtility.SizeOf<EntityId>() * drawCommandCount,
                            UnsafeUtility.AlignOf<long>(),
                            Allocator.TempJob)
                        : null,
                    instanceSortingPositions = hasSortingPositions
                        ? (float*)UnsafeUtility.Malloc(
                            sizeof(float) * GetSortingPositionFloatCount(visibleInstanceCount),
                            UnsafeUtility.AlignOf<long>(),
                            Allocator.TempJob)
                        : null,
                    instanceSortingPositionFloatCount = hasSortingPositions
                        ? GetSortingPositionFloatCount(visibleInstanceCount)
                        : 0,
                };

                cullingOutput.drawCommands[0] = draws;

                NativeArray<ParticleCullingSplit> cullingSplits = CreatePackedCullingData(
                    cullingContext,
                    out NativeArray<ParticleCullingPlanePacket4> cullingPlanePackets);

                var job = new ParticleDrawCommandOutputJob
                {
                    Commands = commands,
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
                };
                JobHandle outputHandle = job.Schedule(drawCommandCount, 4);
                JobHandle disposePlanesHandle = cullingPlanePackets.Dispose(outputHandle);
                JobHandle disposeSplitsHandle = cullingSplits.Dispose(outputHandle);
                JobHandle combinedHandle = JobHandle.CombineDependencies(disposePlanesHandle, disposeSplitsHandle);
                m_PendingCullingOutputHandle = m_HasPendingCullingOutput
                    ? JobHandle.CombineDependencies(m_PendingCullingOutputHandle, combinedHandle)
                    : combinedHandle;
                m_HasPendingCullingOutput = true;
                return combinedHandle;
            }

            private static ParticleDrawCommandInput CreateDrawCommandInput(
                ParticleDrawBatch batch,
                int recordStart,
                int recordCount,
                int visibleOffset,
                int maxVisibleCount,
                int layer,
                EntityId pickingEntityId,
                bool requiresSortingPositions)
            {
                return new ParticleDrawCommandInput
                {
                    RecordStart = recordStart,
                    RecordCount = Mathf.Max(0, recordCount),
                    VisibleOffset = Mathf.Max(0, visibleOffset),
                    MaxVisibleCount = Mathf.Max(0, maxVisibleCount),
                    Layer = layer,
                    SubmeshIndex = 0,
                    ActiveMeshLod = 0,
                    RendererPriority = batch.Material != null ? batch.Material.renderQueue : 0,
                    RenderingLayerMask = uint.MaxValue,
                    SceneCullingMask = 0,
                    DrawFlags = ResolveParticleDrawCommandFlags(requiresSortingPositions, hasMotion: false),
                    HasSortingPositions = requiresSortingPositions ? 1 : 0,
                    ShadowCastingMode = batch.ShadowCastingMode,
                    MotionMode = MotionVectorGenerationMode.ForceNoMotion,
                    ReceiveShadows = batch.ReceiveShadows ? 1 : 0,
                    StaticShadowCaster = 0,
                    AllDepthSorted = 1,
                    BatchId = batch.BatchId,
                    MeshId = batch.MeshId,
                    MaterialId = batch.MaterialId,
                    PickingEntityId = pickingEntityId,
                };
            }

            private static bool RequiresSortingPositions(NativeArray<ParticleDrawCommandInput> commands)
            {
                if (!commands.IsCreated)
                    return false;

                for (int index = 0; index < commands.Length; index++)
                {
                    if (commands[index].HasSortingPositions != 0)
                        return true;
                }

                return false;
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

                m_CopyOperations.Add(new UploadOperation
                {
                    SrcOffset = (uint)srcOffset,
                    DstOffset = (uint)dstOffset,
                    Size = (uint)AlignTo4(size),
                });
                m_LastCopyOperationCount++;
                m_LastCopyByteCount += AlignTo4(size);
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

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        private unsafe struct ParticleDrawCommandOutputJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ParticleDrawCommandInput> Commands;
            [ReadOnly]
            public NativeArray<ParticleCullingRecord> CullingRecords;
            [ReadOnly]
            public NativeArray<ParticleCullingPlanePacket4> CullingPlanePackets;
            [ReadOnly]
            public NativeArray<ParticleCullingSplit> CullingSplits;

            public uint CullingLayerMask;
            public ulong SceneCullingMask;
            public int ViewType;
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
                int visibleCount = ShouldRenderCommand(command) ? WriteVisibleInstances(command) : 0;

                DrawCommands[commandIndex] = new BatchDrawCommand
                {
                    visibleOffset = (uint)command.VisibleOffset,
                    visibleCount = (uint)visibleCount,
                    batchID = command.BatchId,
                    materialID = command.MaterialId,
                    meshID = command.MeshId,
                    submeshIndex = (ushort)math.clamp(command.SubmeshIndex, 0, ushort.MaxValue),
                    activeMeshLod = (ushort)math.clamp(command.ActiveMeshLod, 0, ushort.MaxValue),
                    splitVisibilityMask = 0xff,
                    flags = command.DrawFlags,
                    sortingPosition = command.HasSortingPositions != 0 ? command.VisibleOffset * 3 : 0,
                };
                if (DrawCommandPickingEntityIds != null)
                    DrawCommandPickingEntityIds[commandIndex] = command.PickingEntityId;

                DrawRanges[commandIndex] = new BatchDrawRange
                {
                    drawCommandsBegin = (uint)commandIndex,
                    drawCommandsCount = 1,
                    drawCommandsType = BatchDrawCommandType.Direct,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = command.RenderingLayerMask,
                        rendererPriority = command.RendererPriority,
                        layer = (byte)math.clamp(command.Layer, 0, 31),
                        shadowCastingMode = command.ShadowCastingMode,
                        receiveShadows = command.ReceiveShadows != 0,
                        motionMode = command.MotionMode,
                        staticShadowCaster = command.StaticShadowCaster != 0,
                        allDepthSorted = command.AllDepthSorted != 0,
                        sceneCullingMask = SceneCullingMask,
                    },
                };
            }

            private bool ShouldRenderCommand(ParticleDrawCommandInput command)
            {
                if (!IsLayerVisible(command.Layer))
                    return false;

                if (ViewType == (int)BatchCullingViewType.Light && command.ShadowCastingMode == ShadowCastingMode.Off)
                    return false;

                return command.RecordCount > 0 && command.MaxVisibleCount > 0;
            }

            private bool IsLayerVisible(int layer)
            {
                layer = math.clamp(layer, 0, 31);
                return (CullingLayerMask & (1u << layer)) != 0u;
            }

            private int WriteVisibleInstances(ParticleDrawCommandInput command)
            {
                int visibleOffset = command.VisibleOffset;
                int startOffset = visibleOffset;
                int recordEnd = command.RecordStart + command.RecordCount;
                for (int recordIndex = command.RecordStart; recordIndex < recordEnd; recordIndex++)
                {
                    ParticleCullingRecord record = CullingRecords[recordIndex];
                    if (!ShouldRenderRecord(record) || !IsVisible(record))
                        continue;

                    if (record.UsesPageBillboard != 0)
                    {
                        int remaining = record.ActiveCount;
                        int pageIndex = 0;
                        while (remaining > 0 && visibleOffset - startOffset < command.MaxVisibleCount)
                        {
                            WriteSortingPosition(visibleOffset, record.BoundsCenter);
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
                            WriteSortingPosition(visibleOffset, record.BoundsCenter);
                            VisibleInstances[visibleOffset++] = record.BatchBaseIndex + particleIndex;
                        }
                    }
                }

                return visibleOffset - startOffset;
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

            private bool IsVisible(ParticleCullingRecord record)
            {
                if (record.ActiveCount <= 0)
                    return false;

                if (CullingPlanePackets.Length == 0 || CullingSplits.Length == 0)
                    return true;

                for (int splitIndex = 0; splitIndex < CullingSplits.Length; splitIndex++)
                {
                    ParticleCullingSplit split = CullingSplits[splitIndex];
                    if (split.PacketCount <= 0)
                        return true;

                    if (IntersectsPlanePackets(record, split.PacketOffset, split.PacketCount))
                        return true;
                }

                return false;
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
            private static readonly VividEcsManagerJobRegistry<ParticleRenderJobContext> s_RenderJobRegistry =
                CreateRenderJobRegistry();

            public static int registeredJobCount => s_RenderJobRegistry.count;

            public static JobHandle Schedule(
                NativeArray<ParticleRenderUploadColumnWork> columnWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks)
            {
                var context = new ParticleRenderJobContext(columnWorks, sharedDataWorks);
                return s_RenderJobRegistry.ScheduleEnabled(context, context.EnabledModuleFlags);
            }

            private static VividEcsManagerJobRegistry<ParticleRenderJobContext> CreateRenderJobRegistry()
            {
                var registry = new VividEcsManagerJobRegistry<ParticleRenderJobContext>();
                registry.RegisterModule(
                    "VividParticle.Render.Transform",
                    0,
                    (uint)ParticleRenderJobFlags.Transform,
                    ScheduleTransformJob);
                registry.RegisterModule(
                    "VividParticle.Render.Color",
                    10,
                    (uint)ParticleRenderJobFlags.Color,
                    ScheduleColorJob);
                registry.RegisterModule(
                    "VividParticle.Render.VelocityStretch",
                    20,
                    (uint)ParticleRenderJobFlags.VelocityStretch,
                    ScheduleVelocityStretchJob);
                registry.RegisterModule(
                    "VividParticle.Render.UV",
                    25,
                    (uint)ParticleRenderJobFlags.UV,
                    ScheduleUVJob);
                registry.RegisterModule(
                    "VividParticle.Render.CustomData",
                    26,
                    (uint)ParticleRenderJobFlags.CustomData,
                    ScheduleCustomDataJob);
                registry.RegisterModule(
                    "VividParticle.Render.MeshIndex",
                    27,
                    (uint)ParticleRenderJobFlags.MeshIndex,
                    ScheduleMeshIndexJob);
                registry.RegisterModule(
                    "VividParticle.Render.SharedData",
                    30,
                    (uint)ParticleRenderJobFlags.SharedData,
                    ScheduleSharedDataJob);
                return registry;
            }

            private static JobHandle ScheduleTransformJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleTransformRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleColorJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleColorRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleVelocityStretchJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleVelocityStretchRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleUVJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleUVRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleCustomDataJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleCustomDataRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleMeshIndexJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleMeshIndexRenderJob
                {
                    Works = context.ColumnWorks,
                }.Schedule(context.ColumnWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }

            private static JobHandle ScheduleSharedDataJob(ParticleRenderJobContext context, JobHandle dependency)
            {
                JobHandle handle = new VividParticleSharedDataRenderJob
                {
                    Works = context.SharedDataWorks,
                }.Schedule(context.SharedDataWorks.Length, 32);
                return JobHandle.CombineDependencies(dependency, handle);
            }
        }

        [Flags]
        private enum ParticleRenderJobFlags : uint
        {
            None = 0u,
            Transform = 1u << 0,
            Color = 1u << 1,
            VelocityStretch = 1u << 2,
            UV = 1u << 3,
            CustomData = 1u << 4,
            MeshIndex = 1u << 5,
            SharedData = 1u << 6,
        }

        private readonly struct ParticleRenderJobContext : IVividEcsManagerJobModuleFlags
        {
            public ParticleRenderJobContext(
                NativeArray<ParticleRenderUploadColumnWork> columnWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks)
            {
                ColumnWorks = columnWorks;
                SharedDataWorks = sharedDataWorks;
                EnabledModuleFlags = ResolveEnabledModuleFlags(columnWorks, sharedDataWorks);
            }

            public readonly NativeArray<ParticleRenderUploadColumnWork> ColumnWorks;
            public readonly NativeArray<ParticleRenderSharedDataWork> SharedDataWorks;

            public bool HasColumnWorks => ColumnWorks.IsCreated && ColumnWorks.Length > 0;

            public bool HasSharedDataWorks => SharedDataWorks.IsCreated && SharedDataWorks.Length > 0;

            public uint EnabledModuleFlags { get; }

            private static uint ResolveEnabledModuleFlags(
                NativeArray<ParticleRenderUploadColumnWork> columnWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks)
            {
                uint flags = sharedDataWorks.IsCreated && sharedDataWorks.Length > 0
                    ? (uint)ParticleRenderJobFlags.SharedData
                    : 0u;

                if (!columnWorks.IsCreated)
                    return flags;

                for (int index = 0; index < columnWorks.Length; index++)
                {
                    flags |= columnWorks[index].Segment switch
                    {
                        InstanceUploadSegment.PositionSize
                            or InstanceUploadSegment.Rotation
                            or InstanceUploadSegment.Scale => (uint)ParticleRenderJobFlags.Transform,
                        InstanceUploadSegment.BaseColor => (uint)ParticleRenderJobFlags.Color,
                        InstanceUploadSegment.VelocityStretch => (uint)ParticleRenderJobFlags.VelocityStretch,
                        InstanceUploadSegment.UV => (uint)ParticleRenderJobFlags.UV,
                        InstanceUploadSegment.CustomData1
                            or InstanceUploadSegment.CustomData2 => (uint)ParticleRenderJobFlags.CustomData,
                        InstanceUploadSegment.MeshIndex => (uint)ParticleRenderJobFlags.MeshIndex,
                        _ => 0u,
                    };
                }

                return flags;
            }
        }

        [BurstCompile]
        private struct VividParticleTransformRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment == InstanceUploadSegment.PositionSize)
                {
                    VividParticleRenderJobUtility.WriteParticleRange(
                        work,
                        VividParticleRenderJobUtility.RenderColumnPositionSize);
                }
                else if (work.Segment == InstanceUploadSegment.Rotation)
                {
                    VividParticleRenderJobUtility.WriteParticleRange(
                        work,
                        VividParticleRenderJobUtility.RenderColumnRotation);
                }
                else if (work.Segment == InstanceUploadSegment.Scale)
                {
                    VividParticleRenderJobUtility.WriteParticleRange(
                        work,
                        VividParticleRenderJobUtility.RenderColumnScale);
                }
            }
        }

        [BurstCompile]
        private struct VividParticleColorRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment != InstanceUploadSegment.BaseColor)
                    return;

                VividParticleRenderJobUtility.WriteParticleRange(
                    work,
                    VividParticleRenderJobUtility.RenderColumnColor);
            }
        }

        [BurstCompile]
        private struct VividParticleVelocityStretchRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment != InstanceUploadSegment.VelocityStretch)
                    return;

                VividParticleRenderJobUtility.WriteParticleRange(
                    work,
                    VividParticleRenderJobUtility.RenderColumnVelocityStretch);
            }
        }

        [BurstCompile]
        private struct VividParticleUVRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment != InstanceUploadSegment.UV)
                    return;

                VividParticleRenderJobUtility.WriteParticleRange(
                    work,
                    VividParticleRenderJobUtility.RenderColumnUV);
            }
        }

        [BurstCompile]
        private struct VividParticleCustomDataRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment is not InstanceUploadSegment.CustomData1 and not InstanceUploadSegment.CustomData2)
                    return;

                VividParticleRenderJobUtility.WriteParticleRange(
                    work,
                    work.Segment == InstanceUploadSegment.CustomData1
                        ? VividParticleRenderJobUtility.RenderColumnCustomData1
                        : VividParticleRenderJobUtility.RenderColumnCustomData2);
            }
        }

        [BurstCompile]
        private struct VividParticleMeshIndexRenderJob : IJobParallelFor
        {
            [ReadOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<ParticleRenderUploadColumnWork> Works;

            public void Execute(int workIndex)
            {
                ParticleRenderUploadColumnWork work = Works[workIndex];
                if (work.Segment != InstanceUploadSegment.MeshIndex)
                    return;

                VividParticleRenderJobUtility.WriteParticleRange(
                    work,
                    VividParticleRenderJobUtility.RenderColumnMeshIndex);
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

            public static void WriteParticleRange(ParticleRenderUploadColumnWork work, int renderColumn)
            {
                ParticleRenderUploadSource source = work.Source;
                int endIndex = math.min(source.ActiveCount, source.StartIndex + source.Count);
                if (endIndex <= source.StartIndex)
                    return;

                for (int particleIndex = source.StartIndex; particleIndex < endIndex; particleIndex++)
                {
                    WriteParticleValue(
                        work,
                        particleIndex,
                        renderColumn switch
                        {
                            RenderColumnPositionSize => GetPositionSize(source, particleIndex),
                            RenderColumnColor => GetRenderColor(source, particleIndex),
                            RenderColumnRotation => new float4(0.0f, 0.0f, 0.0f, 1.0f),
                            RenderColumnVelocityStretch => GetVelocityStretch(source, particleIndex),
                            RenderColumnScale => GetScale(source, particleIndex),
                            RenderColumnUV => new float4(0.0f, 0.0f, 1.0f, 1.0f),
                            RenderColumnCustomData1 => float4.zero,
                            RenderColumnCustomData2 => float4.zero,
                            RenderColumnMeshIndex => float4.zero,
                            _ => default,
                        });
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
                ParticleRenderUploadColumnWork work,
                int particleIndex,
                float4 value)
            {
                ParticleRenderUploadSource source = work.Source;
                int batchIndex = source.BatchBaseIndex + particleIndex;
                UnsafeUtility.WriteArrayElement(
                    source.BufferBase + source.BatchDataOffset + work.ColumnByteOffset,
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
                        VividParticleMainModule.MinimumStartSize,
                        0.0f,
                        work.ActiveCount));
                UnsafeUtility.WriteArrayElement(
                    destination,
                    6,
                    new float4(
                        work.StretchLengthScale,
                        work.StretchSpeedScale,
                        0.0f,
                        work.RenderMode));
                UnsafeUtility.WriteArrayElement(destination, 7, new float4(0.0f, 0.0f, 1.0f, 1.0f));
                UnsafeUtility.WriteArrayElement(destination, 8, new float4(work.DataPerSharpBits, 0.0f, 0.0f, work.ActiveCount));
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
                                0u));
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
                                0u));
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
            public readonly int DrawBatchCount;
            public readonly int LastLockCount;
            public readonly int LastCopyOperationCount;
            public readonly int LastCopyByteCount;
            public readonly bool UsesComputeDelta;
            public readonly int LastUploadColumnWorkCount;
            public readonly int LastSharedDataWorkCount;

            public VividParticleRendererManagerStats(
                int renderRecordCount,
                int drawBatchCount,
                int lastLockCount,
                int lastCopyOperationCount,
                int lastCopyByteCount,
                bool usesComputeDelta,
                int lastUploadColumnWorkCount,
                int lastSharedDataWorkCount)
            {
                RenderRecordCount = renderRecordCount;
                DrawBatchCount = drawBatchCount;
                LastLockCount = lastLockCount;
                LastCopyOperationCount = lastCopyOperationCount;
                LastCopyByteCount = lastCopyByteCount;
                UsesComputeDelta = usesComputeDelta;
                LastUploadColumnWorkCount = lastUploadColumnWorkCount;
                LastSharedDataWorkCount = lastSharedDataWorkCount;
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
