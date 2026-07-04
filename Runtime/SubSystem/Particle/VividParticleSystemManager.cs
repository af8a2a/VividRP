using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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
        internal const int SizeOfMatrix = sizeof(float) * 4 * 4;
        internal const int SizeOfPackedMatrix = sizeof(float) * 4 * 3;
        internal const int SizeOfFloat4 = sizeof(float) * 4;
        internal const int ZeroBlockByteSize = SizeOfPackedMatrix * 2;

        private const int InstanceDataBufferCount = 3;
        private const float GravityAcceleration = 9.81f;
        private const float MinimumSimulationStep = 0.000001f;
        private const float MaximumEditorSimulationStep = 0.1f;

        private static readonly ProfilerMarker s_PlayerLoopKickMarker = new("VividRP.PlayerLoop.PreLateUpdate/VividParticleSystemManager.Kick");
        private static readonly ProfilerMarker s_BeginCameraCompleteMarker = new("VividRP.RenderPipeline.BeginCameraRendering/VividParticleSystemManager.Complete");
        private static readonly ProfilerMarker s_ManualDrainMarker = new("VividRP.Particle.Manager.ManualDrain");
        private static readonly ProfilerMarker s_BRGUploadMarker = new("VividRP.Particle.Manager.BRGUpload");

        private static readonly Dictionary<VividParticleSystem, ParticleSystemState> s_States = new();
        private static bool s_Initialized;
        private static int s_LastPlayerLoopFrame = -1;
        private static int s_LastCompleteAndUploadFrame = -1;

        public static int registeredSystemCount => s_States.Count;

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
                s_States.Add(system, new ParticleSystemState(system));
        }

        public static void Unregister(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

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

        internal static void NotifySettingsChanged(VividParticleSystem system)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            using (s_ManualDrainMarker.Auto())
            {
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
                state.CompletePending();
                state.Emit(count, system.CaptureFrameSnapshot(0.0f));
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
                state.CompletePending();

                if (restart)
                    state.ResetSimulation(system.CaptureFrameSnapshot(0.0f), clearParticles: true);

                if (t > 0.0f)
                {
                    float remaining = t;
                    if (fixedTimeStep)
                    {
                        while (remaining > MinimumSimulationStep)
                        {
                            float step = Mathf.Min(VividParticleSystem.FixedSimulationStep, remaining);
                            state.SimulateDeltaImmediate(system.CaptureFrameSnapshot(step), allowEmission: true);
                            remaining -= step;
                        }
                    }
                    else
                    {
                        state.SimulateDeltaImmediate(system.CaptureFrameSnapshot(remaining), allowEmission: true);
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
                state.CompletePending();
                state.SimulateDeltaImmediate(system.CaptureFrameSnapshot(deltaTime), allowEmission);
            }

            UploadRenderingState(state, forceUpload: true);
        }

        internal static void ResetSimulation(VividParticleSystem system, bool clearParticles)
        {
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return;

            using (s_ManualDrainMarker.Auto())
            {
                state.CompletePending();
                state.ResetSimulation(system.CaptureFrameSnapshot(0.0f), clearParticles);
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

        internal static void CompleteAndUploadForTests()
        {
            CompleteAndUploadAll(forceUpload: true, oncePerFrame: false);
        }

        internal static void ClearForTests()
        {
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.Dispose();

            s_States.Clear();
            s_LastPlayerLoopFrame = -1;
            s_LastCompleteAndUploadFrame = -1;
        }

        internal static bool TryGetStats(
            VividParticleSystem system,
            out VividParticleSystemManagerStats stats)
        {
            stats = default;
            if (system == null || !s_States.TryGetValue(system, out ParticleSystemState state))
                return false;

            stats = state.stats;
            return true;
        }

        internal static MetadataValue CreatePerInstanceMetadata(int nameId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = nameId,
                Value = PerInstanceMetadataMask | (uint)byteAddress,
            };
        }

        internal static int ObjectToWorldByteAddress(int capacity)
        {
            return ZeroBlockByteSize;
        }

        internal static int WorldToObjectByteAddress(int capacity)
        {
            return ObjectToWorldByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfPackedMatrix;
        }

        internal static int BaseColorByteAddress(int capacity)
        {
            return WorldToObjectByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfPackedMatrix;
        }

        internal static int InstanceDataByteSize(int capacity)
        {
            return BaseColorByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
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
        }

        private static ParticleSystemState GetOrCreateState(VividParticleSystem system)
        {
            Register(system);
            return s_States[system];
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_States.Count == 0)
                return;

            using (s_BeginCameraCompleteMarker.Auto())
            {
                if (s_LastPlayerLoopFrame != Time.frameCount)
                    ScheduleAutomaticUpdates(null);

                CompleteAndUploadAll(forceUpload: false, oncePerFrame: true);
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

        private static void ScheduleAutomaticUpdates(float? deltaTimeOverride)
        {
            bool scheduledAnyJob = false;
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.CompletePending();

            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                scheduledAnyJob |= pair.Value.ScheduleAutomatic(deltaTimeOverride, requireActive: true);

            if (scheduledAnyJob)
                JobHandle.ScheduleBatchedJobs();

            s_LastPlayerLoopFrame = Time.frameCount;
            RequestEditorRenderUpdateForActiveSystems();
        }

        private static void CompleteAndUploadAll(bool forceUpload, bool oncePerFrame)
        {
            if (oncePerFrame && s_LastCompleteAndUploadFrame == Time.frameCount)
                return;

            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.CompletePending();

            using (s_BRGUploadMarker.Auto())
            {
                foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                    pair.Value.UpdateRendering(forceUpload);
            }

            s_LastCompleteAndUploadFrame = Time.frameCount;
        }

        private static void UploadRenderingState(ParticleSystemState state, bool forceUpload)
        {
            if (state == null)
                return;

            using (s_BRGUploadMarker.Auto())
            {
                state.UpdateRendering(forceUpload);
            }
        }

        private static void InsertIntoPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (rootLoop.subSystemList == null)
                return;

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                PlayerLoopSystem[] nestedSystems = subSystem.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var updatedSubSystems = new List<PlayerLoopSystem>(nestedSystems.Length + 1);
                bool alreadyPresent = false;
                foreach (PlayerLoopSystem nestedSystem in nestedSystems)
                {
                    if (nestedSystem.type == typeof(VividParticleSystemManagerPlayerLoopMarker))
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

        private static PlayerLoopSystem CreatePlayerLoopSystem()
        {
            return new PlayerLoopSystem
            {
                type = typeof(VividParticleSystemManagerPlayerLoopMarker),
                updateDelegate = PlayerLoopKick,
            };
        }

        private static void RequestEditorRenderUpdateIfNeeded(VividParticleSystem system)
        {
#if UNITY_EDITOR
            if (Application.isPlaying || system == null || !system.requiresAutomaticUpdate)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        private static void RequestEditorRenderUpdateForActiveSystems()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
            {
                if (!pair.Value.requiresAutomaticUpdate)
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
            private static readonly int s_ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
            private static readonly int s_WorldToObjectId = Shader.PropertyToID("unity_WorldToObject");

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
            private JobHandle m_PendingJob;
            private VividParticleSystemFrameSnapshot m_PendingSnapshot;
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
            private VividParticleRenderMode m_LastUploadedRenderMode;
            private bool m_BatchCreated;
            private bool m_OwnedMesh;
            private bool m_ResourcesDirty = true;
            private bool m_MissingShaderWarningLogged;
            private bool m_HasPendingJob;
            private bool m_HasPendingSimulation;
            private bool m_PendingAllowEmission;
            private bool m_HasUploadedRenderStateSnapshot;

            public ParticleSystemState(VividParticleSystem system)
            {
                m_System = system;
                m_Storage.systemId = ResolveSystemId(system);
                ResetEditorUpdateTime();
            }

            public int activeCount => Mathf.Min(m_Storage.activeCount, m_System != null ? m_System.main.maxParticles : 0);

            public int storageCapacity => m_Storage.capacity;

            public int storagePageCount => m_Storage.pageCount;

            public bool usesEcsStorage => true;

            public float time => m_Time;

            public bool requiresAutomaticUpdate => m_System != null && m_System.requiresAutomaticUpdate;

            public VividParticleSystemManagerStats stats => new(
                IsInitialized,
                m_Capacity,
                m_LastUploadedCount,
                CullingCallCount,
                VisibleCullingCallCount,
                LastVisible,
                LastViewType,
                LastDrawCommandCount,
                m_HasPendingJob ? 1 : 0,
                ScheduledJobCount,
                CompletedJobCount,
                LastScheduledFrame,
                LastCompletedFrame,
                InstanceDataBufferCount,
                m_LastUploadBufferIndex,
                m_LastUploadOperationCount,
                m_LastUploadByteCount);

            private bool IsInitialized => m_BRG != null
                && m_InstanceData != null
                && m_BatchCreated
                && m_Capacity > 0;

            private int CullingCallCount { get; set; }

            private int VisibleCullingCallCount { get; set; }

            private bool LastVisible { get; set; }

            private BatchCullingViewType LastViewType { get; set; }

            private int LastDrawCommandCount { get; set; }

            private int ScheduledJobCount { get; set; }

            private int CompletedJobCount { get; set; }

            private int LastScheduledFrame { get; set; } = -1;

            private int LastCompletedFrame { get; set; } = -1;

            public void Dispose()
            {
                CompletePending();
                ReleaseResources();
                m_Storage.Dispose();
                m_Random = null;
                m_BurstTriggered = Array.Empty<bool>();
            }

            public void MarkResourcesDirty()
            {
                m_ResourcesDirty = true;
                MarkAllInstanceDataDirty();
            }

            public void NotifySettingsChanged()
            {
                if (m_System == null)
                    return;

                VividParticleSystemFrameSnapshot snapshot = m_System.CaptureFrameSnapshot(0.0f);
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

            public bool ScheduleAutomatic(float? deltaTimeOverride, bool requireActive)
            {
                if (m_System == null)
                    return false;

                float deltaTime = ResolveDeltaTime(deltaTimeOverride);
                if (deltaTime <= 0.0f)
                    return false;

                VividParticleSystemFrameSnapshot snapshot = m_System.CaptureFrameSnapshot(deltaTime);
                if (!RequiresAutomaticUpdate(snapshot, requireActive))
                    return false;

                return ScheduleSimulation(snapshot, snapshot.IsPlaying && !snapshot.StopEmitting);
            }

            public void CompletePending()
            {
                if (!m_HasPendingSimulation && !m_HasPendingJob)
                    return;

                if (m_HasPendingJob)
                {
                    int previousActiveCount = activeCount;
                    m_PendingJob.Complete();
                    m_PendingJob = default;
                    m_HasPendingJob = false;
                    m_Storage.ApplyScheduledIntegrateResult();
                    MarkInstanceRangeDirty(0, previousActiveCount);
                    CompletedJobCount++;
                }

                if (m_HasPendingSimulation)
                {
                    AdvanceEmission(m_PendingSnapshot, m_PendingAllowEmission);
                    m_HasPendingSimulation = false;
                    m_PendingAllowEmission = false;
                    m_PendingFrame = -1;
                    m_System?.CompleteStopEmittingIfEmpty();
                }

                LastCompletedFrame = Time.frameCount;
            }

            public void SimulateDeltaImmediate(VividParticleSystemFrameSnapshot snapshot, bool allowEmission)
            {
                if (snapshot.DeltaTime <= 0.0f)
                    return;

                EnsureStorageCapacity(snapshot.MaxParticles);
                int previousActiveCount = activeCount;
                if (m_Storage.ScheduleIntegrate(
                    snapshot.DeltaTime,
                    Vector3.down * (GravityAcceleration * snapshot.GravityModifier),
                    out JobHandle handle))
                {
                    ScheduledJobCount++;
                    LastScheduledFrame = Time.frameCount;
                    handle.Complete();
                    CompletedJobCount++;
                    LastCompletedFrame = Time.frameCount;
                    m_Storage.ApplyScheduledIntegrateResult();
                    MarkInstanceRangeDirty(0, previousActiveCount);
                }

                AdvanceEmission(snapshot, allowEmission);
                m_System?.CompleteStopEmittingIfEmpty();
            }

            public void Emit(int count, VividParticleSystemFrameSnapshot snapshot)
            {
                if (count <= 0)
                    return;

                EnsureStorageCapacity(snapshot.MaxParticles);
                EnsureRandom(snapshot);

                int available = Mathf.Max(0, snapshot.MaxParticles - activeCount);
                int spawnCount = Mathf.Min(count, available);
                int firstSpawnIndex = activeCount;
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
                    m_Storage.Clear();
                    MarkInstanceRangeDirty(0, m_LastUploadedCount);
                }
                ResetRandom(snapshot);
            }

            public void UpdateRendering(bool forceUpload)
            {
                if (m_System == null)
                    return;

                if (!ParticleSystemState.CanRender(m_System.rendererModule))
                {
                    m_LastUploadedCount = 0;
                    return;
                }

                if (!forceUpload && m_LastUploadedFrame == Time.frameCount)
                    return;

                if (!EnsureResources())
                    return;

                UploadInstanceData();
                m_LastUploadedFrame = Time.frameCount;
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
                if (m_System == null)
                    return new Bounds(Vector3.zero, Vector3.zero);

                int particleCount = activeCount;
                if (particleCount <= 0 || !m_Storage.isCreated || !m_Storage.IsValidIndex(0))
                    return new Bounds(m_System.transform.position, Vector3.zero);

                Vector3 firstPosition = GetParticleWorldPosition(m_Storage.GetPosition(0));
                float firstExtent = GetParticleWorldExtent(0);
                var bounds = new Bounds(firstPosition, Vector3.one * (firstExtent * 2.0f));

                for (int index = 1; index < particleCount; index++)
                {
                    Vector3 position = GetParticleWorldPosition(m_Storage.GetPosition(index));
                    float extent = GetParticleWorldExtent(index);
                    bounds.Encapsulate(position + Vector3.one * extent);
                    bounds.Encapsulate(position - Vector3.one * extent);
                }

                return bounds;
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

                if (!m_Storage.ScheduleIntegrate(
                    snapshot.DeltaTime,
                    Vector3.down * (GravityAcceleration * snapshot.GravityModifier),
                    out m_PendingJob))
                {
                    LastScheduledFrame = Time.frameCount;
                    return false;
                }

                m_HasPendingJob = true;
                ScheduledJobCount++;
                LastScheduledFrame = Time.frameCount;
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
                m_Storage.EnsureCapacity(maxParticles);
            }

            private static VividParticleSystemId ResolveSystemId(VividParticleSystem system)
            {
                return system != null
                    ? new VividParticleSystemId(system.GetEntityId().GetHashCode() & int.MaxValue)
                    : VividParticleSystemId.Invalid;
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
                float remaining = snapshot.DeltaTime;
                float duration = snapshot.Duration;

                while (remaining > MinimumSimulationStep)
                {
                    float segmentEnd = Mathf.Min(duration, m_Time + remaining);
                    float segmentDelta = Mathf.Max(0.0f, segmentEnd - m_Time);

                    if (allowEmission && snapshot.EmissionEnabled && segmentDelta > 0.0f)
                        EmitForTimeRange(snapshot, m_Time, segmentEnd, segmentDelta);

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
                float deltaTime)
            {
                m_EmissionAccumulator += snapshot.RateOverTime * deltaTime;
                int continuousCount = Mathf.FloorToInt(m_EmissionAccumulator);
                if (continuousCount > 0)
                {
                    m_EmissionAccumulator -= continuousCount;
                    Emit(continuousCount, snapshot);
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
                    Emit(burst.count, snapshot);
                }
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

            private float GetParticleWorldExtent(int particleIndex)
            {
                float renderSize = GetParticleRenderSize(particleIndex);
                if (m_System != null
                    && m_System.rendererModule.renderMode == VividParticleRenderMode.Mesh
                    && m_System.rendererModule.mesh != null)
                {
                    return Mathf.Max(
                        VividParticleMainModule.MinimumStartSize,
                        m_System.rendererModule.mesh.bounds.extents.magnitude * renderSize);
                }

                if (m_System != null && m_System.rendererModule.renderMode == VividParticleRenderMode.Stretch)
                {
                    Vector3 velocity = m_Storage.IsValidIndex(particleIndex)
                        ? m_Storage.GetVelocity(particleIndex)
                        : Vector3.zero;
                    float stretchLength = renderSize * m_System.rendererModule.stretchLengthScale
                        + velocity.magnitude * m_System.rendererModule.stretchSpeedScale;
                    return Mathf.Max(renderSize, stretchLength) * 0.5f;
                }

                return renderSize * 0.5f;
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

                Shader shader = null;
                Material material = sourceMaterial;
                bool ownsMaterial = false;
                bool usingDefaultMaterial = false;
                if (material == null)
                {
                    shader = Shader.Find(DefaultShaderName);
                    if (shader == null)
                    {
                        if (!m_MissingShaderWarningLogged)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[VividRP] Could not find shader '{DefaultShaderName}' for {nameof(VividParticleSystem)}.");
                            m_MissingShaderWarningLogged = true;
                        }

                        return false;
                    }

                    material = new Material(shader)
                    {
                        name = "Vivid Particle System Billboard Material",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    ownsMaterial = true;
                    usingDefaultMaterial = true;
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
                    if (usingDefaultMaterial)
                        ConfigureDefaultParticleMaterial(material);

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
                m_QuadMesh = renderMesh != null ? renderMesh : CreateQuadMesh();
                m_OwnedMesh = renderMesh == null;
                for (int index = 0; index < InstanceDataBufferCount; index++)
                    m_InstanceDataBuffers[index] = CreateInstanceBuffer(capacity);

                m_InstanceDataBufferIndex = 0;
                m_LastUploadBufferIndex = -1;
                m_InstanceData = m_InstanceDataBuffers[m_InstanceDataBufferIndex];

                m_BRG = new BatchRendererGroup(new BatchRendererGroupCreateInfo
                {
                    cullingCallback = OnPerformCulling,
                    userContext = IntPtr.Zero,
                });
#if UNITY_EDITOR
                m_BRG.SetEnabledViewTypes(new[]
                {
                    BatchCullingViewType.Camera,
                });
#endif
                m_MeshID = m_BRG.RegisterMesh(m_QuadMesh);
                m_MaterialID = m_BRG.RegisterMaterial(m_RegisteredMaterial);

                var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
                try
                {
                    metadata[0] = CreatePerInstanceMetadata(s_ObjectToWorldId, ObjectToWorldByteAddress(capacity));
                    metadata[1] = CreatePerInstanceMetadata(s_WorldToObjectId, WorldToObjectByteAddress(capacity));
                    metadata[2] = CreatePerInstanceMetadata(s_BaseColorId, BaseColorByteAddress(capacity));

                    m_BatchID = m_BRG.AddBatch(
                        metadata,
                        m_InstanceData.bufferHandle,
                        0u,
                        ResolveBufferWindowSize());
                }
                finally
                {
                    metadata.Dispose();
                }

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

            private static void ConfigureDefaultParticleMaterial(Material material)
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

            private static void ConfigureParticleRenderMode(Material material, VividParticleRenderMode renderMode)
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

            private static void ApplyRenderQueueOffset(
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
                m_BRG?.Dispose();
                m_BRG = null;

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
                for (int index = 0; index < m_InstanceDirtyRanges.Length; index++)
                    MarkAllInstanceDataDirty(m_InstanceDirtyRanges[index]);
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

                for (int index = 0; index < m_InstanceDirtyRanges.Length; index++)
                    m_InstanceDirtyRanges[index].AddInstanceRange(startIndex, count);
            }

            private void MarkAllInstanceDataDirty(InstanceUploadDirtyRanges ranges)
            {
                ranges.Clear();
                ranges.AddZeroBlock();
                ranges.AddInstanceRange(0, m_Capacity);
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
                if (m_HasUploadedRenderStateSnapshot
                    && m_LastUploadedLocalToWorldMatrix == localToWorld
                    && m_LastUploadedRendererColor == rendererColor
                    && Mathf.Approximately(m_LastUploadedSizeScale, sizeScale)
                    && Mathf.Approximately(m_LastUploadedStretchLengthScale, stretchLengthScale)
                    && Mathf.Approximately(m_LastUploadedStretchSpeedScale, stretchSpeedScale)
                    && m_LastUploadedRenderMode == renderMode)
                {
                    return;
                }

                if (count > 0)
                    MarkInstanceRangeDirty(0, count);

                m_LastUploadedLocalToWorldMatrix = localToWorld;
                m_LastUploadedRendererColor = rendererColor;
                m_LastUploadedSizeScale = sizeScale;
                m_LastUploadedStretchLengthScale = stretchLengthScale;
                m_LastUploadedStretchSpeedScale = stretchSpeedScale;
                m_LastUploadedRenderMode = renderMode;
                m_HasUploadedRenderStateSnapshot = true;
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
                    InstanceUploadSegment.ObjectToWorld => ObjectToWorldByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfPackedMatrix,
                    InstanceUploadSegment.WorldToObject => WorldToObjectByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfPackedMatrix,
                    InstanceUploadSegment.BaseColor => BaseColorByteAddress(m_Capacity)
                        + operation.StartIndex * SizeOfFloat4,
                    _ => 0,
                };
            }

            private int GetUploadOperationByteCount(InstanceUploadOperation operation, int activeCount)
            {
                if (operation.Segment == InstanceUploadSegment.ZeroBlock)
                    return ZeroBlockByteSize;

                int count = Mathf.Clamp(activeCount - operation.StartIndex, 0, operation.Count);
                return operation.Segment == InstanceUploadSegment.BaseColor
                    ? count * SizeOfFloat4
                    : count * SizeOfPackedMatrix;
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
                    case InstanceUploadSegment.ObjectToWorld:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            Matrix4x4 objectToWorld = GetParticleObjectToWorldMatrix(index);
                            WriteArrayElement(baseAddress, 0, index - operation.StartIndex, new PackedMatrix(objectToWorld));
                        }
                        break;
                    case InstanceUploadSegment.WorldToObject:
                        for (int index = operation.StartIndex; index < endIndex; index++)
                        {
                            Matrix4x4 objectToWorld = GetParticleObjectToWorldMatrix(index);
                            WriteArrayElement(baseAddress, 0, index - operation.StartIndex, new PackedMatrix(objectToWorld.inverse));
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

            private static Mesh CreateQuadMesh()
            {
                var mesh = new Mesh
                {
                    name = "Vivid Particle Billboard Quad",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                mesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, -0.5f, 0.0f),
                    new Vector3(-0.5f, 0.5f, 0.0f),
                    new Vector3(0.5f, 0.5f, 0.0f),
                    new Vector3(0.5f, -0.5f, 0.0f),
                });
                mesh.SetUVs(0, new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(1.0f, 0.0f),
                });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                mesh.RecalculateBounds();
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

        private enum InstanceUploadSegment
        {
            ZeroBlock,
            ObjectToWorld,
            WorldToObject,
            BaseColor,
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
            private bool m_ObjectToWorldDirty;
            private bool m_WorldToObjectDirty;
            private bool m_BaseColorDirty;
            private int m_ObjectToWorldStart;
            private int m_ObjectToWorldEnd;
            private int m_WorldToObjectStart;
            private int m_WorldToObjectEnd;
            private int m_BaseColorStart;
            private int m_BaseColorEnd;

            public int Count
            {
                get
                {
                    int count = m_ZeroBlockDirty ? 1 : 0;
                    count += m_ObjectToWorldDirty ? 1 : 0;
                    count += m_WorldToObjectDirty ? 1 : 0;
                    count += m_BaseColorDirty ? 1 : 0;
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

                    if (m_ObjectToWorldDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.ObjectToWorld, m_ObjectToWorldStart, m_ObjectToWorldEnd);

                        index--;
                    }

                    if (m_WorldToObjectDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.WorldToObject, m_WorldToObjectStart, m_WorldToObjectEnd);

                        index--;
                    }

                    if (m_BaseColorDirty)
                    {
                        if (index == 0)
                            return CreateOperation(InstanceUploadSegment.BaseColor, m_BaseColorStart, m_BaseColorEnd);
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
                AddRange(ref m_ObjectToWorldDirty, ref m_ObjectToWorldStart, ref m_ObjectToWorldEnd, startIndex, count);
                AddRange(ref m_WorldToObjectDirty, ref m_WorldToObjectStart, ref m_WorldToObjectEnd, startIndex, count);
                AddRange(ref m_BaseColorDirty, ref m_BaseColorStart, ref m_BaseColorEnd, startIndex, count);
            }

            public int EstimateUploadByteCount(int activeCount)
            {
                int byteCount = m_ZeroBlockDirty ? ZeroBlockByteSize : 0;
                byteCount += EstimateRangeByteCount(m_ObjectToWorldDirty, m_ObjectToWorldStart, m_ObjectToWorldEnd, activeCount, SizeOfPackedMatrix);
                byteCount += EstimateRangeByteCount(m_WorldToObjectDirty, m_WorldToObjectStart, m_WorldToObjectEnd, activeCount, SizeOfPackedMatrix);
                byteCount += EstimateRangeByteCount(m_BaseColorDirty, m_BaseColorStart, m_BaseColorEnd, activeCount, SizeOfFloat4);
                return byteCount;
            }

            public void Compact()
            {
            }

            public void Clear()
            {
                m_ZeroBlockDirty = false;
                m_ObjectToWorldDirty = false;
                m_WorldToObjectDirty = false;
                m_BaseColorDirty = false;
                m_ObjectToWorldStart = 0;
                m_ObjectToWorldEnd = 0;
                m_WorldToObjectStart = 0;
                m_WorldToObjectEnd = 0;
                m_BaseColorStart = 0;
                m_BaseColorEnd = 0;
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

            private static InstanceUploadOperation CreateOperation(
                InstanceUploadSegment segment,
                int start,
                int end)
            {
                return new InstanceUploadOperation(segment, start, end - start);
            }
        }

        private sealed class VividParticleSystemManagerPlayerLoopMarker
        {
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct PackedMatrix
        {
            public float c0x;
            public float c0y;
            public float c0z;
            public float c1x;
            public float c1y;
            public float c1z;
            public float c2x;
            public float c2y;
            public float c2z;
            public float c3x;
            public float c3y;
            public float c3z;

            public PackedMatrix(Matrix4x4 matrix)
            {
                c0x = matrix.m00;
                c0y = matrix.m10;
                c0z = matrix.m20;
                c1x = matrix.m01;
                c1y = matrix.m11;
                c1z = matrix.m21;
                c2x = matrix.m02;
                c2y = matrix.m12;
                c2z = matrix.m22;
                c3x = matrix.m03;
                c3y = matrix.m13;
                c3z = matrix.m23;
            }
        }
    }

}
