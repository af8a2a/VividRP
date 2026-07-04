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
        private static readonly ProfilerMarker s_BeginCameraCompleteMarker = new("VividRP.RenderPipeline.BeginCameraRendering/VividParticleSystemManager.Complete");
        private static readonly ProfilerMarker s_ManualDrainMarker = new("VividRP.Particle.Manager.ManualDrain");
        private static readonly ProfilerMarker s_BRGUploadMarker = new("VividRP.Particle.Manager.BRGUpload");

        private static readonly VividEcsManagerJobRegistry<ParticleSimulationJobContext> s_SimulationJobRegistry =
            CreateSimulationJobRegistry();
        private static readonly Dictionary<VividParticleSystem, ParticleSystemState> s_States = new();
        private static readonly VividParticleRendererManager s_RendererManager = new();
        private static bool s_Initialized;
        private static int s_LastPlayerLoopFrame = -1;
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
                s_RendererManager.CompletePendingUpload();
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
                s_RendererManager.CompletePendingUpload();
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
                s_RendererManager.CompletePendingUpload();
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
            s_RendererManager.CompletePendingUpload();
        }

        internal static void ClearForTests()
        {
            foreach (KeyValuePair<VividParticleSystem, ParticleSystemState> pair in s_States)
                pair.Value.Dispose();

            s_States.Clear();
            s_RendererManager.Dispose();
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
        }

        private static ParticleSystemState GetOrCreateState(VividParticleSystem system)
        {
            Register(system);
            return s_States[system];
        }

        private static VividEcsManagerJobRegistry<ParticleSimulationJobContext> CreateSimulationJobRegistry()
        {
            var registry = new VividEcsManagerJobRegistry<ParticleSimulationJobContext>();
            registry.Register(
                "VividParticle.Simulation.Integrate",
                0,
                ScheduleParticleIntegrateJob,
                CanScheduleParticleIntegrateJob);
            return registry;
        }

        private static bool CanScheduleParticleIntegrateJob(ParticleSimulationJobContext context)
        {
            return context.State != null && context.State.CanScheduleIntegrateJob(context.Snapshot);
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
            s_RendererManager.CompletePendingUpload();
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
                s_RendererManager.UpdateAll(s_States.Values, forceUpload);
            }

            s_LastCompleteAndUploadFrame = Time.frameCount;
        }

        private static void UploadRenderingState(ParticleSystemState state, bool forceUpload)
        {
            if (state == null)
                return;

            using (s_BRGUploadMarker.Auto())
            {
                s_RendererManager.Update(state, forceUpload);
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

        private readonly struct ParticleSimulationJobContext
        {
            public ParticleSimulationJobContext(
                ParticleSystemState state,
                VividParticleSystemFrameSnapshot snapshot)
            {
                State = state;
                Snapshot = snapshot;
            }

            public readonly ParticleSystemState State;
            public readonly VividParticleSystemFrameSnapshot Snapshot;
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
            private bool m_RendererInitialized;

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
                s_RendererManager.Unregister(this);
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
                if (ScheduleIntegrateViaRegistry(snapshot))
                {
                    m_PendingJob.Complete();
                    m_PendingJob = default;
                    m_HasPendingJob = false;
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
                entry = new ParticleRenderEntry(
                    this,
                    m_RegisteredMaterial,
                    m_QuadMesh,
                    m_RenderMode,
                    VividParticleGpuDataLayout.Create(m_System.rendererModule),
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
                    IsSelectedForEditorOutline(m_System));
                return true;
            }

            private void SetRendererInactive()
            {
                m_RendererInitialized = false;
                m_LastUploadedCount = 0;
                LastVisible = false;
                LastDrawCommandCount = 0;
                LastVisibleInstanceCount = 0;
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

            private Bounds GetParticleRangeWorldBounds(int startIndex, int count)
            {
                if (m_System == null || count <= 0 || activeCount <= 0)
                    return new Bounds(m_System != null ? m_System.transform.position : Vector3.zero, Vector3.zero);

                int clampedStart = Mathf.Clamp(startIndex, 0, activeCount - 1);
                int clampedEnd = Mathf.Clamp(startIndex + count, clampedStart + 1, activeCount);
                Vector3 firstPosition = GetParticleWorldPosition(m_Storage.GetPosition(clampedStart));
                float firstExtent = GetParticleWorldExtent(clampedStart);
                var bounds = new Bounds(firstPosition, Vector3.one * (firstExtent * 2.0f));

                for (int index = clampedStart + 1; index < clampedEnd; index++)
                {
                    Vector3 position = GetParticleWorldPosition(m_Storage.GetPosition(index));
                    float extent = GetParticleWorldExtent(index);
                    bounds.Encapsulate(position + Vector3.one * extent);
                    bounds.Encapsulate(position - Vector3.one * extent);
                }

                return bounds;
            }

            internal bool IsVisibleInCullingContext(BatchCullingContext cullingContext)
            {
                return m_System != null
                    && m_System.isActiveAndEnabled
                    && ParticleSystemState.CanRender(m_System.rendererModule)
                    && activeCount > 0
                    && IsVisibleInCullingContext(GetWorldBounds(), cullingContext);
            }

            internal int AppendCullingRecords(
                int batchBaseIndex,
                int spanBaseIndex,
                bool usesPageBillboard,
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
                    Bounds bounds = GetWorldBounds();
                    AddCullingRecord(records, bounds, batchBaseIndex, spanBaseIndex, activeCount, usesPageBillboard: false);
                    return 1;
                }

                int addedCount = 0;
                int particleCount = activeCount;
                for (int pageStart = 0; pageStart < particleCount; pageStart += BillboardPageSize)
                {
                    int pageCount = Mathf.Min(BillboardPageSize, particleCount - pageStart);
                    Bounds pageBounds = GetParticleRangeWorldBounds(pageStart, pageCount);
                    AddCullingRecord(
                        records,
                        pageBounds,
                        batchBaseIndex + pageStart,
                        spanBaseIndex + addedCount,
                        pageCount,
                        usesPageBillboard: true);
                    addedCount++;
                }

                return addedCount;
            }

            private static void AddCullingRecord(
                List<ParticleCullingRecord> records,
                Bounds bounds,
                int batchBaseIndex,
                int spanBaseIndex,
                int activeCount,
                bool usesPageBillboard)
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
                ScheduledJobCount++;
                LastScheduledFrame = Time.frameCount;
                return handle;
            }

            private bool ScheduleIntegrateViaRegistry(VividParticleSystemFrameSnapshot snapshot)
            {
                m_PendingJob = default;
                m_HasPendingJob = false;

                JobHandle scheduledHandle = s_SimulationJobRegistry.ScheduleEnabled(
                    new ParticleSimulationJobContext(this, snapshot));
                if (!m_HasPendingJob)
                {
                    LastScheduledFrame = Time.frameCount;
                    return false;
                }

                m_PendingJob = scheduledHandle;
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

        internal readonly struct VividParticleGpuDataLayoutDescriptor
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
                var dataInfos = new List<VividParticleGpuDataInfo>(11)
                {
                    CreateInfo(VividParticleGpuDataId.SharedData, VividParticleGpuDataFrequency.PerSharp),
                    CreateInfo(VividParticleGpuDataId.SpanSharedData, VividParticleGpuDataFrequency.Span),
                    CreateInfo(VividParticleGpuDataId.PositionSize, VividParticleGpuDataFrequency.PerInstance),
                    CreateInfo(
                        VividParticleGpuDataId.BaseColor,
                        ResolveFrequency(descriptor.ColorDataMode, perSharpWhenShared: true)),
                    CreateInfo(
                        VividParticleGpuDataId.Scale,
                        ResolveFrequency(descriptor.SizeDataMode, perSharpWhenShared: true)),
                    CreateInfo(
                        VividParticleGpuDataId.Rotation,
                        ResolveFrequency(descriptor.RotationDataMode, perSharpWhenShared: false)),
                    CreateInfo(
                        VividParticleGpuDataId.VelocityStretch,
                        descriptor.RenderMode == VividParticleRenderMode.Stretch
                            ? VividParticleGpuDataFrequency.PerInstance
                            : ResolveFrequency(descriptor.VelocityDataMode, perSharpWhenShared: false)),
                };

                if (descriptor.IncludeUV)
                    dataInfos.Add(CreateInfo(VividParticleGpuDataId.UV, VividParticleGpuDataFrequency.PerInstance));

                if (descriptor.IncludeCustomData1)
                    dataInfos.Add(CreateInfo(VividParticleGpuDataId.CustomData1, VividParticleGpuDataFrequency.PerInstance));

                if (descriptor.IncludeCustomData2)
                    dataInfos.Add(CreateInfo(VividParticleGpuDataId.CustomData2, VividParticleGpuDataFrequency.PerInstance));

                if (descriptor.IncludeMeshIndex)
                    dataInfos.Add(CreateInfo(VividParticleGpuDataId.MeshIndex, VividParticleGpuDataFrequency.PerInstance));

                return new VividParticleGpuDataLayout(dataInfos.ToArray());
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

            public ParticleRenderEntry(
                ParticleSystemState state,
                Material material,
                Mesh mesh,
                VividParticleRenderMode renderMode,
                VividParticleGpuDataLayout gpuLayout,
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
                bool isEditorSelected)
            {
                State = state;
                Material = material;
                Mesh = mesh;
                RenderMode = renderMode;
                GpuLayout = gpuLayout;
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
                MaterialId = entry.Material != null ? entry.Material.GetEntityId().GetHashCode() : 0;
                MeshId = entry.Mesh != null ? entry.Mesh.GetEntityId().GetHashCode() : 0;
                RenderMode = (int)entry.RenderMode;
                Layer = Mathf.Clamp(entry.Layer, 0, 31);
                GpuDataLayoutHash = entry.GpuLayout.Hash;
                DataPerSharpBits = entry.GpuLayout.DataPerSharpBits;
                ShadowCastingMode = entry.ShadowCastingMode;
                ReceiveShadows = entry.ReceiveShadows;
            }

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
            public bool HasPendingCullingResult;
            public bool PendingCullingVisible;
            public int PendingCullingVisibleInstanceCount;
            public int LastUploadOperationCount;
            public int LastUploadByteCount;

            public void Update(ParticleRenderEntry entry)
            {
                State = entry.State;
                Material = entry.Material;
                Mesh = entry.Mesh;
                RenderMode = entry.RenderMode;
                GpuLayout = entry.GpuLayout;
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
        }

        private struct ParticleCullingSplit
        {
            public int PlaneOffset;
            public int PlaneCount;
        }

        private struct ParticleCullingResult
        {
            public int Visible;
            public int VisibleInstanceCount;
        }

        private struct ParticleDrawCommandInput
        {
            public int RecordStart;
            public int RecordCount;
            public int Layer;
            public int SubmeshIndex;
            public int ActiveMeshLod;
            public int RendererPriority;
            public uint RenderingLayerMask;
            public ulong SceneCullingMask;
            public BatchDrawCommandFlags DrawFlags;
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
            private readonly List<ParticleDrawCommandInput> m_DrawCommandInputs = new();
            private readonly List<ParticleRenderRecord> m_CullingResultRecords = new();
            private readonly List<ParticleRenderRecord> m_CullingUniqueResultRecords = new();
            private readonly Dictionary<ParticleMaterialVariantKey, Material> m_DefaultMaterials = new();
            private readonly VividParticleGPUBuffer m_GPUBuffer = new();
            private NativeArray<ParticleCullingResult> m_PendingCullingResults;
            private NativeArray<ParticleRenderUploadColumnWork> m_PendingUploadColumnWorks;
            private NativeArray<ParticleRenderSharedDataWork> m_PendingSharedDataWorks;
            private JobHandle m_PendingCullingHandle;
            private JobHandle m_PendingUploadHandle;
            private BatchRendererGroup m_BRG;
            private BatchCullingViewType m_PendingCullingViewType;
            private bool m_HasPendingCullingResults;
            private bool m_HasPendingUpload;
            private bool m_LayoutDirty = true;
            private bool m_ForceFullUpload;
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

            public void UpdateAll(IEnumerable<ParticleSystemState> states, bool forceUpload)
            {
                CompletePendingUpload();
                m_SeenStates.Clear();
                foreach (ParticleSystemState state in states)
                {
                    if (state == null)
                        continue;

                    m_SeenStates.Add(state);
                    UpdateRecord(state, forceUpload);
                }

                m_RemoveRecords.Clear();
                foreach (ParticleRenderRecord record in m_Records.Values)
                {
                    if (!m_SeenStates.Contains(record.State))
                        m_RemoveRecords.Add(record);
                }

                for (int index = 0; index < m_RemoveRecords.Count; index++)
                    RemoveRecord(m_RemoveRecords[index].State);

                Commit();
            }

            public void Update(ParticleSystemState state, bool forceUpload)
            {
                if (state == null)
                    return;

                CompletePendingUpload();
                UpdateRecord(state, forceUpload);
                Commit();
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
                m_DrawCommandInputs.Clear();
                m_CullingResultRecords.Clear();
                m_CullingUniqueResultRecords.Clear();
                DisposePendingUploadArrays();
                foreach (Material material in m_DefaultMaterials.Values)
                    CoreUtils.Destroy(material);

                m_DefaultMaterials.Clear();
                m_LayoutDirty = true;
                m_ForceFullUpload = false;
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
                CompletePendingUpload();

                if (m_LayoutDirty)
                    RebuildBatches();

                ScheduleUpload();
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
                        for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                        {
                            ParticleRenderRecord record = batch.Records[recordIndex];
                            record.Batch = batch;
                            record.BatchBaseIndex = batch.Capacity;
                            record.SharpIndex = recordIndex;
                            record.SpanBaseIndex = batch.SpanCapacity;
                            record.SpanCapacity = GetVisibleInstanceCount(record.RenderMode, record.Capacity);
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

                    if (!hasUpload)
                    {
                        m_GPUBuffer.ResetLastUploadStats();
                        PublishCleanStats();
                        m_ForceFullUpload = false;
                        return;
                    }

                    byte* bufferBase = m_GPUBuffer.BeginWrite();
                    if (bufferBase == null)
                    {
                        PublishCleanStats();
                        return;
                    }

                    try
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

                        DisposePendingUploadArrays();
                        if (m_UploadColumnWorks.Count > 0 || m_SharedDataWorks.Count > 0)
                        {
                            if (m_UploadColumnWorks.Count > 0)
                            {
                                m_PendingUploadColumnWorks = new NativeArray<ParticleRenderUploadColumnWork>(
                                    m_UploadColumnWorks.Count,
                                    Allocator.Persistent,
                                    NativeArrayOptions.UninitializedMemory);
                                for (int workIndex = 0; workIndex < m_UploadColumnWorks.Count; workIndex++)
                                    m_PendingUploadColumnWorks[workIndex] = m_UploadColumnWorks[workIndex];
                            }

                            if (m_SharedDataWorks.Count > 0)
                            {
                                m_PendingSharedDataWorks = new NativeArray<ParticleRenderSharedDataWork>(
                                    m_SharedDataWorks.Count,
                                    Allocator.Persistent,
                                    NativeArrayOptions.UninitializedMemory);
                                for (int workIndex = 0; workIndex < m_SharedDataWorks.Count; workIndex++)
                                    m_PendingSharedDataWorks[workIndex] = m_SharedDataWorks[workIndex];
                            }

                            m_PendingUploadHandle = VividParticleRenderJobPipeline.Schedule(
                                m_PendingUploadColumnWorks,
                                m_PendingSharedDataWorks);
                            JobHandle.ScheduleBatchedJobs();
                        }

                        m_HasPendingUpload = true;
                    }
                    catch
                    {
                        m_PendingUploadHandle.Complete();
                        DisposePendingUploadArrays();
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
                if (!m_HasPendingUpload)
                    return;

                using (s_CompleteUploadMarker.Auto())
                {
                    m_PendingUploadHandle.Complete();
                    m_PendingUploadHandle = default;
                    DisposePendingUploadArrays();

                    AddPendingUploadCopyOperations();
                    m_GPUBuffer.EndWrite();

                    for (int workIndex = 0; workIndex < m_UploadWorks.Count; workIndex++)
                        m_UploadWorks[workIndex].Record.State.ClearUploadDirty();

                    PublishCleanStats();
                    m_ForceFullUpload = false;
                    m_HasPendingUpload = false;
                }
            }

            private void DisposePendingUploadArrays()
            {
                if (m_PendingUploadColumnWorks.IsCreated)
                    m_PendingUploadColumnWorks.Dispose();

                if (m_PendingSharedDataWorks.IsCreated)
                    m_PendingSharedDataWorks.Dispose();

                m_PendingUploadColumnWorks = default;
                m_PendingSharedDataWorks = default;
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
                if (!m_HasPendingCullingResults)
                    return;

                m_PendingCullingHandle.Complete();
                if (m_PendingCullingResults.IsCreated)
                {
                    int count = Mathf.Min(m_CullingResultRecords.Count, m_PendingCullingResults.Length);
                    for (int index = 0; index < count; index++)
                    {
                        ParticleRenderRecord record = m_CullingResultRecords[index];
                        if (record == null)
                            continue;

                        if (!record.HasPendingCullingResult)
                        {
                            record.HasPendingCullingResult = true;
                            record.PendingCullingVisible = false;
                            record.PendingCullingVisibleInstanceCount = 0;
                            m_CullingUniqueResultRecords.Add(record);
                        }

                        ParticleCullingResult result = m_PendingCullingResults[index];
                        if (result.Visible != 0)
                        {
                            record.PendingCullingVisible = true;
                            record.PendingCullingVisibleInstanceCount += result.VisibleInstanceCount;
                        }
                    }

                    for (int index = 0; index < m_CullingUniqueResultRecords.Count; index++)
                    {
                        ParticleRenderRecord record = m_CullingUniqueResultRecords[index];
                        record.State?.RecordCulling(
                            m_PendingCullingViewType,
                            record.PendingCullingVisible,
                            record.PendingCullingVisible ? 1 : 0,
                            record.PendingCullingVisibleInstanceCount);
                        record.HasPendingCullingResult = false;
                        record.PendingCullingVisible = false;
                        record.PendingCullingVisibleInstanceCount = 0;
                    }

                    m_PendingCullingResults.Dispose();
                    m_PendingCullingResults = default;
                }

                m_CullingResultRecords.Clear();
                m_CullingUniqueResultRecords.Clear();
                m_PendingCullingHandle = default;
                m_PendingCullingViewType = default;
                m_HasPendingCullingResults = false;
            }
            
            [BurstCompile]
            private unsafe JobHandle OnPerformCulling(
                BatchRendererGroup rendererGroup,
                BatchCullingContext cullingContext,
                BatchCullingOutput cullingOutput,
                IntPtr userContext)
            {
                CompletePendingUpload();
                DrainCullingResults();
                m_CullingRecords.Clear();
                m_DrawCommandInputs.Clear();
                m_CullingResultRecords.Clear();
                m_CullingUniqueResultRecords.Clear();
                int visibleInstanceCount = 0;
                bool isPerRecordPickingView = IsPickingOrSelectionView(cullingContext.viewType);

                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    ParticleDrawBatch batch = m_DrawBatches[batchIndex];
                    if (!ShouldRenderBatchForView(batch.ShadowCastingMode, cullingContext.viewType))
                        continue;

                    int layer = Mathf.Clamp(batch.Key.Layer, 0, 31);
                    if (!IsLayerVisibleInCullingMask(cullingContext.cullingLayerMask, layer))
                        continue;

                    int batchVisibleRecordStart = m_CullingRecords.Count;
                    int batchMaxVisibleCount = 0;
                    for (int recordIndex = 0; recordIndex < batch.Records.Count; recordIndex++)
                    {
                        ParticleRenderRecord record = batch.Records[recordIndex];
                        if (!ShouldRenderRecordForView(record, cullingContext.viewType))
                            continue;

                        int recordStart = m_CullingRecords.Count;
                        int cullingRecordCount = record.State.AppendCullingRecords(
                            record.BatchBaseIndex,
                            record.SpanBaseIndex,
                            batch.UsesPageBillboard,
                            m_CullingRecords);
                        if (cullingRecordCount <= 0)
                            continue;

                        for (int cullingRecordIndex = 0; cullingRecordIndex < cullingRecordCount; cullingRecordIndex++)
                            m_CullingResultRecords.Add(record);

                        int recordVisibleCount = GetVisibleInstanceCount(record.RenderMode, record.ActiveCount);
                        batchMaxVisibleCount += recordVisibleCount;

                        if (isPerRecordPickingView)
                        {
                            m_DrawCommandInputs.Add(CreateDrawCommandInput(
                                batch,
                                recordStart,
                                cullingRecordCount,
                                layer,
                                cullingContext,
                                record.PickingEntityId));
                            visibleInstanceCount += recordVisibleCount;
                        }
                    }

                    if (batchMaxVisibleCount <= 0)
                        continue;

                    if (!isPerRecordPickingView)
                    {
                        m_DrawCommandInputs.Add(CreateDrawCommandInput(
                            batch,
                            batchVisibleRecordStart,
                            m_CullingRecords.Count - batchVisibleRecordStart,
                            layer,
                            cullingContext,
                            EntityId.None));
                        visibleInstanceCount += batchMaxVisibleCount;
                    }
                }

                int drawCommandCount = m_DrawCommandInputs.Count;
                if (drawCommandCount <= 0 || visibleInstanceCount <= 0)
                {
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

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
                    drawCommandPickingEntityIds = (EntityId*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<EntityId>() * drawCommandCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    instanceSortingPositions = (float*)UnsafeUtility.Malloc(
                        sizeof(float) * GetSortingPositionFloatCount(visibleInstanceCount),
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    instanceSortingPositionFloatCount = GetSortingPositionFloatCount(visibleInstanceCount),
                };

                cullingOutput.drawCommands[0] = draws;

                var commandInputs = new NativeArray<ParticleDrawCommandInput>(
                    drawCommandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < drawCommandCount; index++)
                    commandInputs[index] = m_DrawCommandInputs[index];

                var cullingRecords = new NativeArray<ParticleCullingRecord>(
                    m_CullingRecords.Count,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < m_CullingRecords.Count; index++)
                    cullingRecords[index] = m_CullingRecords[index];

                NativeArray<float4> cullingPlanes = CreateCullingPlaneArray(cullingContext);
                NativeArray<ParticleCullingSplit> cullingSplits = CreateCullingSplitArray(
                    cullingContext,
                    cullingPlanes.Length);
                m_PendingCullingResults = new NativeArray<ParticleCullingResult>(
                    cullingRecords.Length,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);

                var job = new ParticleDrawCommandOutputJob
                {
                    Commands = commandInputs,
                    CullingRecords = cullingRecords,
                    CullingPlanes = cullingPlanes,
                    CullingSplits = cullingSplits,
                    ReceiverPlaneOffset = cullingContext.receiverPlaneOffset,
                    ReceiverPlaneCount = cullingContext.receiverPlaneCount,
                    CullingResults = m_PendingCullingResults,
                    OutputDrawCommands = cullingOutput.drawCommands,
                    DrawCommands = draws.drawCommands,
                    DrawRanges = draws.drawRanges,
                    VisibleInstances = draws.visibleInstances,
                    InstanceSortingPositions = draws.instanceSortingPositions,
                    DrawCommandPickingEntityIds = draws.drawCommandPickingEntityIds,
                };
                JobHandle outputHandle = job.Schedule();
                JobHandle disposeCommandsHandle = commandInputs.Dispose(outputHandle);
                JobHandle disposeRecordsHandle = cullingRecords.Dispose(outputHandle);
                JobHandle disposePlanesHandle = cullingPlanes.Dispose(outputHandle);
                JobHandle disposeSplitsHandle = cullingSplits.Dispose(outputHandle);
                JobHandle combinedHandle = JobHandle.CombineDependencies(
                    disposeCommandsHandle,
                    disposeRecordsHandle,
                    JobHandle.CombineDependencies(disposePlanesHandle, disposeSplitsHandle));

                m_PendingCullingHandle = combinedHandle;
                m_PendingCullingViewType = cullingContext.viewType;
                m_HasPendingCullingResults = true;
                return combinedHandle;
            }

            private static bool ShouldRenderRecordForView(
                ParticleRenderRecord record,
                BatchCullingViewType viewType)
            {
                if (record == null || record.State == null)
                    return false;

                return viewType != BatchCullingViewType.SelectionOutline || record.IsEditorSelected;
            }

            private static ParticleDrawCommandInput CreateDrawCommandInput(
                ParticleDrawBatch batch,
                int recordStart,
                int recordCount,
                int layer,
                BatchCullingContext cullingContext,
                EntityId pickingEntityId)
            {
                return new ParticleDrawCommandInput
                {
                    RecordStart = recordStart,
                    RecordCount = Mathf.Max(0, recordCount),
                    Layer = layer,
                    SubmeshIndex = 0,
                    ActiveMeshLod = 0,
                    RendererPriority = batch.Material != null ? batch.Material.renderQueue : 0,
                    RenderingLayerMask = uint.MaxValue,
                    SceneCullingMask = cullingContext.sceneCullingMask,
                    DrawFlags = ResolveParticleDrawCommandFlags(hasSortingPosition: true, hasMotion: false),
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

            private static NativeArray<float4> CreateCullingPlaneArray(BatchCullingContext cullingContext)
            {
                NativeArray<Plane> sourcePlanes = cullingContext.cullingPlanes;
                int planeCount = sourcePlanes.IsCreated ? sourcePlanes.Length : 0;
                var planes = new NativeArray<float4>(
                    planeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < planeCount; index++)
                {
                    Plane plane = sourcePlanes[index];
                    Vector3 normal = plane.normal;
                    planes[index] = new float4(normal.x, normal.y, normal.z, plane.distance);
                }

                return planes;
            }

            private static NativeArray<ParticleCullingSplit> CreateCullingSplitArray(
                BatchCullingContext cullingContext,
                int planeCount)
            {
                var sourceSplits = cullingContext.cullingSplits;
                int splitCount = sourceSplits.IsCreated ? sourceSplits.Length : 0;
                if (splitCount <= 0 && planeCount > 0)
                {
                    var singleSplit = new NativeArray<ParticleCullingSplit>(
                        1,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory);
                    singleSplit[0] = new ParticleCullingSplit
                    {
                        PlaneOffset = 0,
                        PlaneCount = planeCount,
                    };
                    return singleSplit;
                }

                var splits = new NativeArray<ParticleCullingSplit>(
                    splitCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < splitCount; index++)
                {
                    var split = sourceSplits[index];
                    splits[index] = new ParticleCullingSplit
                    {
                        PlaneOffset = split.cullingPlaneOffset,
                        PlaneCount = split.cullingPlaneCount,
                    };
                }

                return splits;
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

        [BurstCompile]
        private unsafe struct ParticleDrawCommandOutputJob : IJob
        {
            [ReadOnly]
            public NativeArray<ParticleDrawCommandInput> Commands;
            [ReadOnly]
            public NativeArray<ParticleCullingRecord> CullingRecords;
            [ReadOnly]
            public NativeArray<float4> CullingPlanes;
            [ReadOnly]
            public NativeArray<ParticleCullingSplit> CullingSplits;

            public int ReceiverPlaneOffset;
            public int ReceiverPlaneCount;

            public NativeArray<ParticleCullingResult> CullingResults;

            [NativeDisableContainerSafetyRestriction]
            public NativeArray<BatchCullingOutputDrawCommands> OutputDrawCommands;
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

            public void Execute()
            {
                int outputCommandIndex = 0;
                int visibleOffset = 0;
                for (int commandIndex = 0; commandIndex < Commands.Length; commandIndex++)
                {
                    ParticleDrawCommandInput command = Commands[commandIndex];
                    int commandVisibleOffset = visibleOffset;
                    int commandVisibleCount = WriteVisibleInstances(command, ref visibleOffset);
                    if (commandVisibleCount <= 0)
                        continue;

                    DrawCommands[outputCommandIndex] = new BatchDrawCommand
                    {
                        visibleOffset = (uint)commandVisibleOffset,
                        visibleCount = (uint)commandVisibleCount,
                        batchID = command.BatchId,
                        materialID = command.MaterialId,
                        meshID = command.MeshId,
                        submeshIndex = (ushort)math.clamp(command.SubmeshIndex, 0, ushort.MaxValue),
                        activeMeshLod = (ushort)math.clamp(command.ActiveMeshLod, 0, ushort.MaxValue),
                        splitVisibilityMask = 0xff,
                        flags = command.DrawFlags,
                        sortingPosition = commandVisibleOffset * 3,
                    };
                    DrawCommandPickingEntityIds[outputCommandIndex] = command.PickingEntityId;

                    DrawRanges[outputCommandIndex] = new BatchDrawRange
                    {
                        drawCommandsBegin = (uint)outputCommandIndex,
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
                            sceneCullingMask = command.SceneCullingMask,
                        },
                    };
                    outputCommandIndex++;
                }

                OutputDrawCommands[0] = new BatchCullingOutputDrawCommands
                {
                    drawCommandCount = outputCommandIndex,
                    drawRangeCount = outputCommandIndex,
                    visibleInstanceCount = visibleOffset,
                    drawCommands = DrawCommands,
                    drawRanges = DrawRanges,
                    visibleInstances = VisibleInstances,
                    drawCommandPickingEntityIds = DrawCommandPickingEntityIds,
                    instanceSortingPositions = InstanceSortingPositions,
                    instanceSortingPositionFloatCount = visibleOffset * 3,
                };
            }

            private int WriteVisibleInstances(ParticleDrawCommandInput command, ref int visibleOffset)
            {
                int startOffset = visibleOffset;
                int recordEnd = command.RecordStart + command.RecordCount;
                for (int recordIndex = command.RecordStart; recordIndex < recordEnd; recordIndex++)
                {
                    ParticleCullingRecord record = CullingRecords[recordIndex];
                    if (!IsVisible(record))
                    {
                        CullingResults[recordIndex] = default;
                        continue;
                    }

                    int recordVisibleInstanceCount = 0;
                    if (record.UsesPageBillboard != 0)
                    {
                        int remaining = record.ActiveCount;
                        int pageIndex = 0;
                        while (remaining > 0)
                        {
                            WriteSortingPosition(visibleOffset, record.BoundsCenter);
                            VisibleInstances[visibleOffset++] = record.SpanBaseIndex + pageIndex;
                            recordVisibleInstanceCount++;
                            remaining -= BillboardPageSize;
                            pageIndex++;
                        }
                    }
                    else
                    {
                        for (int particleIndex = 0; particleIndex < record.ActiveCount; particleIndex++)
                        {
                            WriteSortingPosition(visibleOffset, record.BoundsCenter);
                            VisibleInstances[visibleOffset++] = record.BatchBaseIndex + particleIndex;
                        }

                        recordVisibleInstanceCount = record.ActiveCount;
                    }

                    CullingResults[recordIndex] = new ParticleCullingResult
                    {
                        Visible = 1,
                        VisibleInstanceCount = recordVisibleInstanceCount,
                    };
                }

                return visibleOffset - startOffset;
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

                if (CullingPlanes.Length == 0 || CullingSplits.Length == 0)
                    return IntersectsReceiverPlanes(record);

                for (int splitIndex = 0; splitIndex < CullingSplits.Length; splitIndex++)
                {
                    ParticleCullingSplit split = CullingSplits[splitIndex];
                    if (split.PlaneCount <= 0)
                        return IntersectsReceiverPlanes(record);

                    if (IntersectsPlaneRange(record, split.PlaneOffset, split.PlaneCount)
                        && IntersectsReceiverPlanes(record))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IntersectsReceiverPlanes(ParticleCullingRecord record)
            {
                if (ReceiverPlaneCount <= 0)
                    return true;

                return IntersectsPlaneRange(record, ReceiverPlaneOffset, ReceiverPlaneCount);
            }

            private bool IntersectsPlaneRange(ParticleCullingRecord record, int planeOffset, int planeCount)
            {
                int planeEnd = math.min(CullingPlanes.Length, planeOffset + planeCount);
                for (int planeIndex = math.max(0, planeOffset); planeIndex < planeEnd; planeIndex++)
                {
                    float4 plane = CullingPlanes[planeIndex];
                    float3 normal = plane.xyz;
                    float3 positiveVertex = record.BoundsCenter + math.select(
                        -record.BoundsExtents,
                        record.BoundsExtents,
                        normal >= 0.0f);
                    if (math.dot(normal, positiveVertex) + plane.w < 0.0f)
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
                return s_RenderJobRegistry.ScheduleEnabled(new ParticleRenderJobContext(columnWorks, sharedDataWorks));
            }

            private static VividEcsManagerJobRegistry<ParticleRenderJobContext> CreateRenderJobRegistry()
            {
                var registry = new VividEcsManagerJobRegistry<ParticleRenderJobContext>();
                registry.Register(
                    "VividParticle.Render.Transform",
                    0,
                    ScheduleTransformJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.Color",
                    10,
                    ScheduleColorJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.VelocityStretch",
                    20,
                    ScheduleVelocityStretchJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.UV",
                    25,
                    ScheduleUVJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.CustomData",
                    26,
                    ScheduleCustomDataJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.MeshIndex",
                    27,
                    ScheduleMeshIndexJob,
                    context => context.HasColumnWorks);
                registry.Register(
                    "VividParticle.Render.SharedData",
                    30,
                    ScheduleSharedDataJob,
                    context => context.HasSharedDataWorks);
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

        private readonly struct ParticleRenderJobContext
        {
            public ParticleRenderJobContext(
                NativeArray<ParticleRenderUploadColumnWork> columnWorks,
                NativeArray<ParticleRenderSharedDataWork> sharedDataWorks)
            {
                ColumnWorks = columnWorks;
                SharedDataWorks = sharedDataWorks;
            }

            public readonly NativeArray<ParticleRenderUploadColumnWork> ColumnWorks;
            public readonly NativeArray<ParticleRenderSharedDataWork> SharedDataWorks;

            public bool HasColumnWorks => ColumnWorks.IsCreated && ColumnWorks.Length > 0;

            public bool HasSharedDataWorks => SharedDataWorks.IsCreated && SharedDataWorks.Length > 0;
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
