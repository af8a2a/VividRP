using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal unsafe struct VividParticleEcsColumnView
    {
        [NativeDisableUnsafePtrRestriction]
        public float3* Positions;
        [NativeDisableUnsafePtrRestriction]
        public float3* Velocities;
        [NativeDisableUnsafePtrRestriction]
        public float3* AnimatedVelocities;
        [NativeDisableUnsafePtrRestriction]
        public float3* InitialEmitterVelocities;
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
        public float3* AccumulatedRotations;
        [NativeDisableUnsafePtrRestriction]
        public uint* RandomSeeds;
        [NativeDisableUnsafePtrRestriction]
        public float3* NoisePhases;
        [NativeDisableUnsafePtrRestriction]
        public float* NoiseSizeMultipliers;
        [NativeDisableUnsafePtrRestriction]
        public byte* TriggerPreviousInside;
        [NativeDisableUnsafePtrRestriction]
        public byte* TriggerCurrentInside;
        [NativeDisableUnsafePtrRestriction]
        public ulong* TriggerColliderEntityIds;
        [NativeDisableUnsafePtrRestriction]
        public byte* KeepMask;
        [NativeDisableUnsafePtrRestriction]
        public int* ActiveCountOutput;

        public int ArchetypeLineId;
        public int Capacity;
        public int Version;

        public bool IsValid => Positions != null
            && Velocities != null
            && StartLifetimes != null
            && RemainingLifetimes != null
            && Colors != null
            && Sizes != null
            && MeshIndices != null
            && AccumulatedRotations != null
            && RandomSeeds != null
            && Capacity > 0;
    }

    internal sealed class VividParticleEcsStorage : IDisposable
    {
        private readonly VividEcsWorld m_World;
        private readonly VividEcsArchetypeLine m_Line;
        private readonly VividEcsTypeIndex m_CommonTypeIndex;
        private readonly VividEcsTypeIndex m_AnimatedMotionTypeIndex;
        private readonly VividEcsTypeIndex m_NoiseStateTypeIndex;
        private readonly VividEcsTypeIndex m_InheritVelocityStateTypeIndex;
        private readonly VividEcsTypeIndex m_TriggerStateTypeIndex;
        private readonly VividEcsTypeIndex m_SystemIdTypeIndex;
        private readonly VividEcsTypeIndex m_ModuleSharedKeyTypeIndex;
        private readonly VividEcsTypeIndex m_SimulationKernelSharedKeyTypeIndex;
        private readonly VividEcsTypeIndex m_RenderKernelSharedKeyTypeIndex;
        private readonly VividEcsTypeIndex m_RendererSharedKeyTypeIndex;
        private readonly VividEcsTypeIndex m_SimulationActiveTypeIndex;
        private readonly VividEcsTypeIndex m_RendererActiveTypeIndex;
        private readonly VividEcsSoaColumn<VividParticleCommon> m_CommonColumn;
        private VividEcsSoaColumn<VividParticleAnimatedMotion> m_AnimatedMotionColumn;
        private VividEcsSoaColumn<VividParticleNoiseState> m_NoiseStateColumn;
        private VividEcsSoaColumn<VividParticleInheritVelocityState> m_InheritVelocityStateColumn;
        private VividEcsSoaColumn<VividParticleTriggerState> m_TriggerStateColumn;
        private readonly bool m_OwnsWorld;
        private readonly Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> m_LineGroupScratch = new();
        private NativeArray<int> m_ActiveCountOutput;
        private NativeArray<byte> m_KeepMask;
        private NativeArray<VividEcsPageInfo> m_SimulationPages;
        private NativeArray<VividParticleEcsIntegratePageWork> m_StandaloneIntegrateWorks;
        private NativeArray<VividParticleEcsCompactWork> m_StandaloneCompactWorks;
        private VividParticleRendererSharedKey m_RendererSharedKey = VividParticleRendererSharedKey.Invalid;
        private VividParticleRendererHandle m_RendererHandle = VividParticleRendererHandle.Invalid;
        private VividParticleModuleSharedKey m_ModuleSharedKey = VividParticleModuleSharedKey.None;
        private VividParticleSimulationKernelSharedKey m_SimulationKernelSharedKey =
            VividParticleSimulationKernelSharedKey.Base;
        private VividParticleRenderKernelSharedKey m_RenderKernelSharedKey =
            VividParticleRenderKernelSharedKey.Base;
        private VividParticleEcsColumnView m_ColumnView;
        private int m_CachedCommonColumnVersion = -1;
        private int m_CachedAnimatedMotionColumnVersion = -1;
        private int m_CachedNoiseStateColumnVersion = -1;
        private int m_CachedInheritVelocityStateColumnVersion = -1;
        private int m_CachedTriggerStateColumnVersion = -1;
        private int m_CachedKeepMaskCapacity = -1;
        private int m_CachedActiveCountOutputLength = -1;
        private int m_ColumnViewVersion;
        private int m_ColumnViewRefreshCount;
        private int m_RendererHandleBindingWriteCount;
        private int m_PendingIntegrateActiveCount;

        public VividParticleEcsStorage()
            : this(new VividEcsWorld(), ownsWorld: true)
        {
        }

        public VividParticleEcsStorage(VividEcsWorld world)
            : this(world, ownsWorld: false)
        {
        }

        private VividParticleEcsStorage(VividEcsWorld world, bool ownsWorld)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            VividParticleEcsBootstrap.RegisterTypes();
            m_CommonTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            m_AnimatedMotionTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleAnimatedMotion>();
            m_NoiseStateTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleNoiseState>();
            m_InheritVelocityStateTypeIndex =
                VividEcsTypeManager.GetTypeIndex<VividParticleInheritVelocityState>();
            m_TriggerStateTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleTriggerState>();
            m_SystemIdTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>();
            m_ModuleSharedKeyTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleModuleSharedKey>();
            m_SimulationKernelSharedKeyTypeIndex =
                VividEcsTypeManager.GetTypeIndex<VividParticleSimulationKernelSharedKey>();
            m_RenderKernelSharedKeyTypeIndex =
                VividEcsTypeManager.GetTypeIndex<VividParticleRenderKernelSharedKey>();
            m_RendererSharedKeyTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            m_SimulationActiveTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSimulationActive>();
            m_RendererActiveTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererActive>();
            m_World = world;
            m_OwnsWorld = ownsWorld;
            m_Line = m_World.CreateArchetypeLine(
                0,
                m_CommonTypeIndex,
                m_SystemIdTypeIndex,
                m_ModuleSharedKeyTypeIndex,
                m_SimulationKernelSharedKeyTypeIndex,
                m_RenderKernelSharedKeyTypeIndex,
                m_RendererSharedKeyTypeIndex);
            m_Line.SetSharedComponent(VividParticleSystemId.Invalid);
            m_Line.SetSharedComponent(VividParticleModuleSharedKey.None);
            m_Line.SetSharedComponent(VividParticleSimulationKernelSharedKey.Base);
            m_Line.SetSharedComponent(VividParticleRenderKernelSharedKey.Base);
            m_Line.SetSharedComponent(VividParticleRendererSharedKey.Invalid);
            m_CommonColumn = m_Line.GetColumn<VividEcsSoaColumn<VividParticleCommon>>(m_CommonTypeIndex);
        }

        public bool isCreated => m_Line.isCreated;

        public int capacity => m_Line.capacity;

        public int maxParticles => m_Line.maxEntries;

        public int activeCount => m_Line.activeCount;

        public int pageCount => m_Line.pageCount;

        public int archetypeLineId => m_Line.ArchetypeLineId;

        public int tileStart => m_Line.tileRange.StartTile;

        public int tileCount => m_Line.tileRange.TileCount;

        public int allocatorLiveTileCount => m_World.tileAllocator.liveTileCount;

        public int allocatorHighWatermarkTileCount => m_World.tileAllocator.highWatermarkTileCount;

        public int allocatorFreeRangeCount => m_World.tileAllocator.freeRangeCount;

        public int queryLineGroupCount => CountLineGroups();

        public int columnViewVersion => m_ColumnView.Version;

        public int columnViewRefreshCount => m_ColumnViewRefreshCount;

        public int rendererHandleBindingWriteCount => m_RendererHandleBindingWriteCount;

        public bool hasRendererHandleBinding =>
            m_World.TryGetLineAttachment(m_Line, out VividParticleRendererHandle _);

        public bool hasAnimatedMotionColumn => m_AnimatedMotionColumn != null;

        public bool hasNoiseStateColumn => m_NoiseStateColumn != null;

        public bool hasInheritVelocityStateColumn => m_InheritVelocityStateColumn != null;

        public bool hasTriggerStateColumn => m_TriggerStateColumn != null;

        public void EnsureAnimatedMotionColumn()
        {
            if (m_AnimatedMotionColumn != null)
                return;

            m_World.AddComponentType(m_Line, m_AnimatedMotionTypeIndex);
            m_AnimatedMotionColumn =
                m_Line.GetColumn<VividEcsSoaColumn<VividParticleAnimatedMotion>>(m_AnimatedMotionTypeIndex);
            m_CachedAnimatedMotionColumnVersion = -1;
            m_ColumnView = default;
        }

        public void ClearAnimatedMotion()
        {
            if (m_AnimatedMotionColumn == null || !m_AnimatedMotionColumn.isCreated)
                return;

            NativeArray<float3> velocities = m_AnimatedMotionColumn.GetFieldArray<float3>(
                VividParticleAnimatedMotion.VelocityFieldIndex);
            int count = math.min(activeCount, velocities.Length);
            for (int index = 0; index < count; index++)
                velocities[index] = float3.zero;
        }

        public void EnsureNoiseStateColumn()
        {
            if (m_NoiseStateColumn != null)
                return;

            m_World.AddComponentType(m_Line, m_NoiseStateTypeIndex);
            m_NoiseStateColumn =
                m_Line.GetColumn<VividEcsSoaColumn<VividParticleNoiseState>>(m_NoiseStateTypeIndex);
            NativeArray<float3> phases = m_NoiseStateColumn.GetFieldArray<float3>(
                VividParticleNoiseState.PhaseFieldIndex);
            NativeArray<float> sizeMultipliers = m_NoiseStateColumn.GetFieldArray<float>(
                VividParticleNoiseState.SizeMultiplierFieldIndex);
            int count = math.min(activeCount, math.min(phases.Length, sizeMultipliers.Length));
            for (int index = 0; index < count; index++)
            {
                phases[index] = CreateNoisePhase(index);
                sizeMultipliers[index] = 1.0f;
            }
            m_CachedNoiseStateColumnVersion = -1;
            m_CachedInheritVelocityStateColumnVersion = -1;
            m_ColumnView = default;
        }

        public void EnsureInheritVelocityStateColumn(Vector3 initialVelocity)
        {
            if (m_InheritVelocityStateColumn != null)
                return;

            m_World.AddComponentType(m_Line, m_InheritVelocityStateTypeIndex);
            m_InheritVelocityStateColumn =
                m_Line.GetColumn<VividEcsSoaColumn<VividParticleInheritVelocityState>>(
                    m_InheritVelocityStateTypeIndex);
            NativeArray<float3> initialVelocities = m_InheritVelocityStateColumn.GetFieldArray<float3>(
                VividParticleInheritVelocityState.InitialVelocityFieldIndex);
            int count = math.min(activeCount, initialVelocities.Length);
            float3 value = ToFloat3(initialVelocity);
            for (int index = 0; index < count; index++)
                initialVelocities[index] = value;
            m_CachedInheritVelocityStateColumnVersion = -1;
            m_ColumnView = default;
        }

        public void EnsureTriggerStateColumn()
        {
            if (m_TriggerStateColumn != null)
                return;
            m_World.AddComponentType(m_Line, m_TriggerStateTypeIndex);
            m_TriggerStateColumn =
                m_Line.GetColumn<VividEcsSoaColumn<VividParticleTriggerState>>(m_TriggerStateTypeIndex);
            NativeArray<byte> previous = m_TriggerStateColumn.GetFieldArray<byte>(
                VividParticleTriggerState.PreviousInsideFieldIndex);
            NativeArray<byte> current = m_TriggerStateColumn.GetFieldArray<byte>(
                VividParticleTriggerState.CurrentInsideFieldIndex);
            NativeArray<ulong> colliderIds = m_TriggerStateColumn.GetFieldArray<ulong>(
                VividParticleTriggerState.ColliderEntityIdFieldIndex);
            int count = math.min(activeCount, math.min(previous.Length, math.min(current.Length, colliderIds.Length)));
            for (int index = 0; index < count; index++)
            {
                previous[index] = 0;
                current[index] = 0;
                colliderIds[index] = 0UL;
            }
            m_CachedTriggerStateColumnVersion = -1;
            m_ColumnView = default;
        }

        public void ClearTriggerState()
        {
            if (m_TriggerStateColumn == null)
                return;
            NativeArray<byte> previous = m_TriggerStateColumn.GetFieldArray<byte>(
                VividParticleTriggerState.PreviousInsideFieldIndex);
            NativeArray<byte> current = m_TriggerStateColumn.GetFieldArray<byte>(
                VividParticleTriggerState.CurrentInsideFieldIndex);
            NativeArray<ulong> colliderIds = m_TriggerStateColumn.GetFieldArray<ulong>(
                VividParticleTriggerState.ColliderEntityIdFieldIndex);
            int count = math.min(activeCount, math.min(previous.Length, math.min(current.Length, colliderIds.Length)));
            for (int index = 0; index < count; index++)
            {
                previous[index] = 0;
                current[index] = 0;
                colliderIds[index] = 0UL;
            }
        }

        public VividParticleSystemId systemId
        {
            get => m_Line.TryGetSharedComponent(out VividParticleSystemId value) ? value : VividParticleSystemId.Invalid;
            set => m_Line.SetSharedComponent(value);
        }

        public VividParticleRendererSharedKey rendererSharedKey
        {
            get => m_RendererSharedKey;
            set
            {
                if (m_RendererSharedKey.Equals(value))
                    return;

                m_RendererSharedKey = value;
                m_Line.SetSharedComponent(value);
            }
        }

        public VividParticleModuleSharedKey moduleSharedKey
        {
            get => m_ModuleSharedKey;
            set
            {
                if (m_ModuleSharedKey.Equals(value))
                    return;

                m_ModuleSharedKey = value;
                m_Line.SetSharedComponent(value);
            }
        }

        public VividParticleSimulationKernelSharedKey simulationKernelSharedKey
        {
            get => m_SimulationKernelSharedKey;
            set
            {
                if (m_SimulationKernelSharedKey.Equals(value))
                    return;

                m_SimulationKernelSharedKey = value;
                m_Line.SetSharedComponent(value);
            }
        }

        public VividParticleRenderKernelSharedKey renderKernelSharedKey
        {
            get => m_RenderKernelSharedKey;
            set
            {
                if (m_RenderKernelSharedKey.Equals(value))
                    return;

                m_RenderKernelSharedKey = value;
                m_Line.SetSharedComponent(value);
            }
        }

        public VividParticleRendererHandle rendererHandle
        {
            get
            {
                if (!m_RendererHandle.IsValid
                    && m_World.TryGetLineAttachment(
                        m_Line,
                        out VividParticleRendererHandle binding))
                {
                    m_RendererHandle = binding;
                }

                return m_RendererHandle;
            }
            set => SetRendererHandle(value);
        }

        public bool SetRendererHandle(VividParticleRendererHandle value)
        {
            if (m_RendererHandle.Equals(value))
                return false;

            m_RendererHandle = value;
            if (value.IsValid)
                m_World.SetLineAttachment(m_Line, value);
            else
                m_World.RemoveLineAttachment<VividParticleRendererHandle>(m_Line);
            m_RendererHandleBindingWriteCount++;
            return true;
        }

        public void SetLineAttachment<T>(T value)
            where T : struct, IVividEcsLineAttachmentData
        {
            m_World.SetLineAttachment(m_Line, value);
        }

        public bool TryGetLineAttachment<T>(out T value)
            where T : struct, IVividEcsLineAttachmentData
        {
            return m_World.TryGetLineAttachment(m_Line, out value);
        }

        public bool RemoveLineAttachment<T>()
            where T : struct, IVividEcsLineAttachmentData
        {
            return m_World.RemoveLineAttachment<T>(m_Line);
        }

        public bool rendererActive
        {
            get => m_Line.Contains(m_RendererActiveTypeIndex);
            set
            {
                if (value == m_Line.Contains(m_RendererActiveTypeIndex))
                    return;

                if (value)
                    m_World.AddComponentType(m_Line, m_RendererActiveTypeIndex);
                else
                    m_World.RemoveComponentType(m_Line, m_RendererActiveTypeIndex);
            }
        }

        public bool simulationActive
        {
            get => m_Line.Contains(m_SimulationActiveTypeIndex);
            set
            {
                if (value == m_Line.Contains(m_SimulationActiveTypeIndex))
                    return;

                if (value)
                    m_World.AddComponentType(m_Line, m_SimulationActiveTypeIndex);
                else
                    m_World.RemoveComponentType(m_Line, m_SimulationActiveTypeIndex);
            }
        }

        public void EnsureCapacity(int maxParticles)
        {
            m_Line.EnsureCapacity(maxParticles);
            if (!m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            EnsureKeepMaskCapacity(capacity);
        }

        public void Clear()
        {
            m_Line.Clear();
            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput[0] = 0;
        }

        public void Dispose()
        {
            if (m_OwnsWorld)
                m_World.Dispose();
            else
                m_World.DestroyArchetypeLine(m_Line);

            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput.Dispose();

            if (m_KeepMask.IsCreated)
                m_KeepMask.Dispose();

            if (m_SimulationPages.IsCreated)
                m_SimulationPages.Dispose();

            if (m_StandaloneCompactWorks.IsCreated)
                m_StandaloneCompactWorks.Dispose();
            if (m_StandaloneIntegrateWorks.IsCreated)
                m_StandaloneIntegrateWorks.Dispose();

            m_ActiveCountOutput = default;
            m_KeepMask = default;
            m_SimulationPages = default;
            m_StandaloneCompactWorks = default;
            m_StandaloneIntegrateWorks = default;
            m_LineGroupScratch.Clear();
            m_ColumnView = default;
            m_CachedCommonColumnVersion = -1;
            m_CachedAnimatedMotionColumnVersion = -1;
            m_CachedNoiseStateColumnVersion = -1;
            m_CachedInheritVelocityStateColumnVersion = -1;
            m_CachedTriggerStateColumnVersion = -1;
            m_CachedKeepMaskCapacity = -1;
            m_CachedActiveCountOutputLength = -1;
            m_RendererHandle = VividParticleRendererHandle.Invalid;
            m_RendererHandleBindingWriteCount = 0;
            m_PendingIntegrateActiveCount = 0;
        }

        public bool Add(
            Vector3 position,
            Vector3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            Color color,
            int meshIndex = 0,
            Vector3 initialEmitterVelocity = default,
            uint randomSeed = 1u)
        {
            if (!isCreated || !m_Line.Append(out int index))
                return false;

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            common.SetFieldValue(VividParticleCommon.PositionFieldIndex, index, ToFloat3(position));
            common.SetFieldValue(VividParticleCommon.VelocityFieldIndex, index, ToFloat3(velocity));
            common.SetFieldValue(VividParticleCommon.StartLifetimeFieldIndex, index, startLifetime);
            common.SetFieldValue(VividParticleCommon.RemainingLifetimeFieldIndex, index, remainingLifetime);
            common.SetFieldValue(VividParticleCommon.StartColorFieldIndex, index, ToFloat4(color));
            common.SetFieldValue(VividParticleCommon.SizeFieldIndex, index, size);
            common.SetFieldValue(VividParticleCommon.MeshIndexFieldIndex, index, math.max(0, meshIndex));
            common.SetFieldValue(VividParticleCommon.AccumulatedRotationFieldIndex, index, float3.zero);
            common.SetFieldValue(
                VividParticleCommon.RandomSeedFieldIndex,
                index,
                randomSeed == 0u ? 1u : randomSeed);
            if (m_AnimatedMotionColumn != null)
            {
                m_AnimatedMotionColumn.SetFieldValue(
                    VividParticleAnimatedMotion.VelocityFieldIndex,
                    index,
                    float3.zero);
            }
            if (m_NoiseStateColumn != null)
            {
                m_NoiseStateColumn.SetFieldValue(
                    VividParticleNoiseState.PhaseFieldIndex,
                    index,
                    CreateNoisePhase(index));
                m_NoiseStateColumn.SetFieldValue(
                    VividParticleNoiseState.SizeMultiplierFieldIndex,
                    index,
                    1.0f);
            }
            if (m_InheritVelocityStateColumn != null)
            {
                m_InheritVelocityStateColumn.SetFieldValue(
                    VividParticleInheritVelocityState.InitialVelocityFieldIndex,
                    index,
                    ToFloat3(initialEmitterVelocity));
            }
            if (m_TriggerStateColumn != null)
            {
                m_TriggerStateColumn.SetFieldValue(
                    VividParticleTriggerState.PreviousInsideFieldIndex,
                    index,
                    (byte)0);
                m_TriggerStateColumn.SetFieldValue(
                    VividParticleTriggerState.CurrentInsideFieldIndex,
                    index,
                    (byte)0);
                m_TriggerStateColumn.SetFieldValue(
                    VividParticleTriggerState.ColliderEntityIdFieldIndex,
                    index,
                    0UL);
            }
            if (m_KeepMask.IsCreated && index < m_KeepMask.Length)
                m_KeepMask[index] = 1;

            return true;
        }

        public unsafe bool ReserveInitializeParticles(
            int requestedCount,
            VividParticleSystemFrameSnapshot snapshot,
            uint randomSeed,
            NativeList<VividParticleEcsInitializeParticlesWork> works,
            out int firstIndex,
            out int reservedCount)
        {
            return ReserveInitializeParticles(
                requestedCount,
                snapshot,
                randomSeed,
                Vector3.zero,
                works,
                out firstIndex,
                out reservedCount);
        }

        public unsafe bool ReserveInitializeParticles(
            int requestedCount,
            VividParticleSystemFrameSnapshot snapshot,
            uint randomSeed,
            Vector3 emitterVelocity,
            NativeList<VividParticleEcsInitializeParticlesWork> works,
            out int firstIndex,
            out int reservedCount)
        {
            firstIndex = activeCount;
            reservedCount = 0;
            if (!isCreated || requestedCount <= 0 || !works.IsCreated)
                return false;

            reservedCount = m_Line.AppendRange(requestedCount, out firstIndex);
            if (reservedCount <= 0)
                return false;

            if (!TryCreateInitializeParticlesTemplate(
                snapshot,
                randomSeed,
                emitterVelocity,
                out VividParticleEcsInitializeParticlesWork work,
                out _))
            {
                return false;
            }

            work.StartIndex = firstIndex;
            work.Count = reservedCount;
            works.Add(work);
            return true;
        }

        public unsafe bool TryCreateInitializeParticlesTemplate(
            VividParticleSystemFrameSnapshot snapshot,
            uint randomSeed,
            Vector3 emitterVelocity,
            out VividParticleEcsInitializeParticlesWork work,
            out int* activeCountOutput)
        {
            work = default;
            activeCountOutput = null;
            if (!isCreated)
                return false;

            if (!m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            EnsureKeepMaskCapacity(capacity);
            if (!TryGetColumnView(out VividParticleEcsColumnView columnView))
                return false;

            m_ActiveCountOutput[0] = activeCount;
            activeCountOutput = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(m_ActiveCountOutput);
            work = new VividParticleEcsInitializeParticlesWork
            {
                StartIndex = activeCount,
                Count = 0,
                Capacity = columnView.Capacity,
                ShapeEnabled = snapshot.ShapeEnabled ? 1 : 0,
                ShapeType = (int)snapshot.ShapeType,
                SimulationSpace = (int)snapshot.SimulationSpace,
                MeshCount = math.max(1, snapshot.RendererMeshCount),
                RandomSeed = randomSeed == 0u ? 1u : randomSeed,
                StartLifetime = snapshot.StartLifetime,
                StartSpeed = snapshot.StartSpeed,
                StartSize = snapshot.StartSize,
                ShapeRadius = snapshot.ShapeRadius,
                ShapeAngleRadians = math.radians(snapshot.ShapeAngle),
                ShapeBoxSize = ToFloat3(snapshot.ShapeBoxSize),
                StartColor = ToFloat4(snapshot.StartColor),
                LocalToWorldMatrix = ToFloat4x4(snapshot.LocalToWorldMatrix),
                WorldRotation = ToQuaternion(snapshot.WorldRotation),
                Positions = columnView.Positions,
                Velocities = columnView.Velocities,
                StartLifetimes = columnView.StartLifetimes,
                RemainingLifetimes = columnView.RemainingLifetimes,
                Colors = columnView.Colors,
                Sizes = columnView.Sizes,
                MeshIndices = columnView.MeshIndices,
                AccumulatedRotations = columnView.AccumulatedRotations,
                RandomSeeds = columnView.RandomSeeds,
                NoisePhases = columnView.NoisePhases,
                NoiseSizeMultipliers = columnView.NoiseSizeMultipliers,
                TriggerPreviousInside = columnView.TriggerPreviousInside,
                TriggerCurrentInside = columnView.TriggerCurrentInside,
                TriggerColliderEntityIds = columnView.TriggerColliderEntityIds,
                AnimatedVelocities = columnView.AnimatedVelocities,
                InitialEmitterVelocities = columnView.InitialEmitterVelocities,
                EmitterVelocity = ToFloat3(emitterVelocity),
                KeepMask = columnView.KeepMask,
            };
            return true;
        }

        public bool ScheduleIntegrate(float deltaTime, Vector3 gravity, out JobHandle handle)
        {
            return ScheduleIntegrate(deltaTime, gravity, default, out handle);
        }

        public bool ScheduleIntegrate(
            float deltaTime,
            Vector3 gravity,
            JobHandle dependency,
            out JobHandle handle)
        {
            unsafe
            {
                return ScheduleIntegrate(
                    deltaTime,
                    gravity,
                    default,
                    default,
                    default,
                    null,
                    0,
                    float3x3.identity,
                    null,
                    0,
                    (int)VividParticleInheritVelocityMode.Initial,
                    float3.zero,
                    null,
                    null,
                    0,
                    0,
                    0.0f,
                    0,
                    0,
                    float3x3.identity,
                    null,
                    0,
                    new float2(0.0f, 1.0f),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    (int)VividParticleNoiseQuality.High,
                    0,
                    0.0f,
                    0,
                    1,
                    0.5f,
                    2.0f,
                    dependency,
                    out handle);
            }
        }

        public unsafe bool ScheduleIntegrate(
            float deltaTime,
            Vector3 gravity,
            VividParticleCollisionJobConfig collision,
            VividParticleTriggerJobConfig trigger,
            VividParticleExternalForcesJobConfig externalForces,
            float3* velocityOverLifetimeLut,
            int velocityOverLifetimeEnabled,
            float3x3 velocityOverLifetimeTransform,
            float* inheritVelocityLut,
            int inheritVelocityEnabled,
            int inheritVelocityMode,
            float3 emitterVelocity,
            float3* limitVelocityLut,
            float* limitVelocityDragLut,
            int limitVelocityEnabled,
            int limitVelocitySeparateAxes,
            float limitVelocityDampen,
            int limitVelocityMultiplyDragByParticleSize,
            int limitVelocityMultiplyDragByParticleVelocity,
            float3x3 limitVelocityTransform,
            float3* rotationBySpeedLut,
            int rotationBySpeedEnabled,
            float2 rotationBySpeedRange,
            float3* noiseStrengthLut,
            float* noiseScrollSpeedLut,
            float3* noiseRemapLut,
            float* noisePositionAmountLut,
            float* noiseRotationAmountLut,
            float* noiseSizeAmountLut,
            int noiseEnabled,
            int noiseQuality,
            int noiseRemapEnabled,
            float noiseFrequency,
            int noiseDamping,
            int noiseOctaveCount,
            float noiseOctaveMultiplier,
            float noiseOctaveScale,
            JobHandle dependency,
            out JobHandle handle)
        {
            handle = default;
            int count = activeCount;
            if (!isCreated || count <= 0 || deltaTime <= 0.0f)
                return false;

            m_ActiveCountOutput[0] = count;
            m_PendingIntegrateActiveCount = count;
            int livePageCount = (count + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount;
            if (livePageCount <= 0)
                return false;

            EnsureSimulationPageCapacity(livePageCount);
            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
                m_SimulationPages[pageIndex] = m_Line.GetPageInfo(pageIndex);

            NativeArray<VividEcsPageInfo> pages = m_SimulationPages.GetSubArray(0, livePageCount);
            EnsureStandaloneCompactWorkCapacity();
            m_StandaloneCompactWorks[0] = CreateCompactWork(count);
            EnsureStandaloneIntegrateWorkCapacity(livePageCount);
            if (!TryGetColumnView(out VividParticleEcsColumnView columnView))
                return false;

            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
            {
                m_StandaloneIntegrateWorks[pageIndex] = new VividParticleEcsIntegratePageWork
                {
                    Page = m_SimulationPages[pageIndex],
                    DeltaTime = deltaTime,
                    Gravity = ToFloat3(gravity),
                    Collision = collision,
                    Trigger = trigger,
                    ExternalForces = externalForces,
                    PositionLength = columnView.Capacity,
                    Positions = columnView.Positions,
                    Velocities = columnView.Velocities,
                    AnimatedVelocities = columnView.AnimatedVelocities,
                    InitialEmitterVelocities = columnView.InitialEmitterVelocities,
                    StartLifetimes = columnView.StartLifetimes,
                    VelocityOverLifetimeLut = velocityOverLifetimeLut,
                    VelocityOverLifetimeEnabled = velocityOverLifetimeEnabled,
                    VelocityOverLifetimeTransform = velocityOverLifetimeTransform,
                    InheritVelocityLut = inheritVelocityLut,
                    InheritVelocityEnabled = inheritVelocityEnabled,
                    InheritVelocityMode = inheritVelocityMode,
                    EmitterVelocity = emitterVelocity,
                    LimitVelocityLut = limitVelocityLut,
                    LimitVelocityDragLut = limitVelocityDragLut,
                    LimitVelocityEnabled = limitVelocityEnabled,
                    LimitVelocitySeparateAxes = limitVelocitySeparateAxes,
                    LimitVelocityDampen = limitVelocityDampen,
                    LimitVelocityMultiplyDragByParticleSize = limitVelocityMultiplyDragByParticleSize,
                    LimitVelocityMultiplyDragByParticleVelocity = limitVelocityMultiplyDragByParticleVelocity,
                    LimitVelocityTransform = limitVelocityTransform,
                    Sizes = columnView.Sizes,
                    AccumulatedRotations = columnView.AccumulatedRotations,
                    NoisePhases = columnView.NoisePhases,
                    NoiseSizeMultipliers = columnView.NoiseSizeMultipliers,
                    TriggerPreviousInside = columnView.TriggerPreviousInside,
                    TriggerCurrentInside = columnView.TriggerCurrentInside,
                    TriggerColliderEntityIds = columnView.TriggerColliderEntityIds,
                    NoisePositionAmountLut = noisePositionAmountLut,
                    NoiseRotationAmountLut = noiseRotationAmountLut,
                    NoiseSizeAmountLut = noiseSizeAmountLut,
                    NoiseRemapLut = noiseRemapLut,
                    RotationBySpeedLut = rotationBySpeedLut,
                    RotationBySpeedEnabled = rotationBySpeedEnabled,
                    RotationBySpeedRange = rotationBySpeedRange,
                    NoiseStrengthLut = noiseStrengthLut,
                    NoiseScrollSpeedLut = noiseScrollSpeedLut,
                    NoiseEnabled = noiseEnabled,
                    NoiseQuality = noiseQuality,
                    NoiseRemapEnabled = noiseRemapEnabled,
                    NoiseFrequency = noiseFrequency,
                    NoiseDamping = noiseDamping,
                    NoiseOctaveCount = noiseOctaveCount,
                    NoiseOctaveMultiplier = noiseOctaveMultiplier,
                    NoiseOctaveScale = noiseOctaveScale,
                    RemainingLifetimes = columnView.RemainingLifetimes,
                    KeepMask = columnView.KeepMask,
                };
            }

            var job = new VividParticleEcsIntegratePageWorksJob
            {
                Works = m_StandaloneIntegrateWorks.GetSubArray(0, livePageCount),
            };

            JobHandle integrateHandle = job.ScheduleParallel(
                pages,
                dependency,
                innerloopBatchCount: 1,
                VividEcsPageDispatchMode.Average);
            var compactJob = new VividParticleEcsCompactWorksJob
            {
                Works = m_StandaloneCompactWorks,
            };
            handle = compactJob.Schedule(1, innerloopBatchCount: 1, integrateHandle);
            return true;
        }

        public unsafe bool AddIntegratePageWorks(
            float deltaTime,
            Vector3 gravity,
            VividParticleCollisionJobConfig collision,
            VividParticleTriggerJobConfig trigger,
            VividParticleExternalForcesJobConfig externalForces,
            float3* velocityOverLifetimeLut,
            int velocityOverLifetimeEnabled,
            float3x3 velocityOverLifetimeTransform,
            float* inheritVelocityLut,
            int inheritVelocityEnabled,
            int inheritVelocityMode,
            float3 emitterVelocity,
            float3* limitVelocityLut,
            float* limitVelocityDragLut,
            int limitVelocityEnabled,
            int limitVelocitySeparateAxes,
            float limitVelocityDampen,
            int limitVelocityMultiplyDragByParticleSize,
            int limitVelocityMultiplyDragByParticleVelocity,
            float3x3 limitVelocityTransform,
            float3* rotationBySpeedLut,
            int rotationBySpeedEnabled,
            float2 rotationBySpeedRange,
            float3* noiseStrengthLut,
            float* noiseScrollSpeedLut,
            float3* noiseRemapLut,
            float* noisePositionAmountLut,
            float* noiseRotationAmountLut,
            float* noiseSizeAmountLut,
            int noiseEnabled,
            int noiseQuality,
            int noiseRemapEnabled,
            float noiseFrequency,
            int noiseDamping,
            int noiseOctaveCount,
            float noiseOctaveMultiplier,
            float noiseOctaveScale,
            NativeList<VividParticleEcsIntegratePageWork> pageWorks,
            NativeList<VividParticleEcsCompactWork> compactWorks)
        {
            int count = activeCount;
            if (!isCreated
                || count <= 0
                || deltaTime <= 0.0f
                || !pageWorks.IsCreated
                || !compactWorks.IsCreated)
            {
                return false;
            }

            if (!m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            EnsureKeepMaskCapacity(capacity);
            m_ActiveCountOutput[0] = count;
            m_PendingIntegrateActiveCount = count;

            int livePageCount = (count + VividEcsConstants.PageEntryCount - 1) / VividEcsConstants.PageEntryCount;
            if (livePageCount <= 0)
                return false;

            if (!TryGetColumnView(out VividParticleEcsColumnView columnView))
                return false;

            float3 gravityValue = ToFloat3(gravity);
            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
            {
                VividEcsPageInfo page = m_Line.GetPageInfo(pageIndex);
                pageWorks.Add(new VividParticleEcsIntegratePageWork
                {
                    Page = page,
                    DeltaTime = deltaTime,
                    Gravity = gravityValue,
                    Collision = collision,
                    Trigger = trigger,
                    ExternalForces = externalForces,
                    PositionLength = columnView.Capacity,
                    Positions = columnView.Positions,
                    Velocities = columnView.Velocities,
                    AnimatedVelocities = columnView.AnimatedVelocities,
                    InitialEmitterVelocities = columnView.InitialEmitterVelocities,
                    StartLifetimes = columnView.StartLifetimes,
                    VelocityOverLifetimeLut = velocityOverLifetimeLut,
                    VelocityOverLifetimeEnabled = velocityOverLifetimeEnabled,
                    VelocityOverLifetimeTransform = velocityOverLifetimeTransform,
                    InheritVelocityLut = inheritVelocityLut,
                    InheritVelocityEnabled = inheritVelocityEnabled,
                    InheritVelocityMode = inheritVelocityMode,
                    EmitterVelocity = emitterVelocity,
                    LimitVelocityLut = limitVelocityLut,
                    LimitVelocityDragLut = limitVelocityDragLut,
                    LimitVelocityEnabled = limitVelocityEnabled,
                    LimitVelocitySeparateAxes = limitVelocitySeparateAxes,
                    LimitVelocityDampen = limitVelocityDampen,
                    LimitVelocityMultiplyDragByParticleSize = limitVelocityMultiplyDragByParticleSize,
                    LimitVelocityMultiplyDragByParticleVelocity = limitVelocityMultiplyDragByParticleVelocity,
                    LimitVelocityTransform = limitVelocityTransform,
                    Sizes = columnView.Sizes,
                    AccumulatedRotations = columnView.AccumulatedRotations,
                    NoisePhases = columnView.NoisePhases,
                    NoiseSizeMultipliers = columnView.NoiseSizeMultipliers,
                    TriggerPreviousInside = columnView.TriggerPreviousInside,
                    TriggerCurrentInside = columnView.TriggerCurrentInside,
                    TriggerColliderEntityIds = columnView.TriggerColliderEntityIds,
                    NoisePositionAmountLut = noisePositionAmountLut,
                    NoiseRotationAmountLut = noiseRotationAmountLut,
                    NoiseSizeAmountLut = noiseSizeAmountLut,
                    NoiseRemapLut = noiseRemapLut,
                    RotationBySpeedLut = rotationBySpeedLut,
                    RotationBySpeedEnabled = rotationBySpeedEnabled,
                    RotationBySpeedRange = rotationBySpeedRange,
                    NoiseStrengthLut = noiseStrengthLut,
                    NoiseScrollSpeedLut = noiseScrollSpeedLut,
                    NoiseEnabled = noiseEnabled,
                    NoiseQuality = noiseQuality,
                    NoiseRemapEnabled = noiseRemapEnabled,
                    NoiseFrequency = noiseFrequency,
                    NoiseDamping = noiseDamping,
                    NoiseOctaveCount = noiseOctaveCount,
                    NoiseOctaveMultiplier = noiseOctaveMultiplier,
                    NoiseOctaveScale = noiseOctaveScale,
                    RemainingLifetimes = columnView.RemainingLifetimes,
                    KeepMask = columnView.KeepMask,
                });
            }

            compactWorks.Add(CreateCompactWork(count));
            return true;
        }

        public void ApplyScheduledIntegrateResult()
        {
            if (!m_ActiveCountOutput.IsCreated)
                return;

            m_Line.SetActiveCount(m_ActiveCountOutput[0]);
            m_PendingIntegrateActiveCount = 0;
        }

        public bool IsValidIndex(int index)
        {
            return isCreated && index >= 0 && index < activeCount;
        }

        public Vector3 GetPosition(int index)
        {
            return ToVector3(commonColumn.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex)[index]);
        }

        public Vector3 GetVelocity(int index)
        {
            return ToVector3(commonColumn.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex)[index]);
        }

        public Vector3 GetAnimatedVelocity(int index)
        {
            if (m_AnimatedMotionColumn == null)
                return Vector3.zero;

            return ToVector3(
                m_AnimatedMotionColumn
                    .GetFieldArray<float3>(VividParticleAnimatedMotion.VelocityFieldIndex)[index]);
        }

        public Vector3 GetInitialEmitterVelocity(int index)
        {
            if (m_InheritVelocityStateColumn == null)
                return Vector3.zero;

            return ToVector3(
                m_InheritVelocityStateColumn
                    .GetFieldArray<float3>(VividParticleInheritVelocityState.InitialVelocityFieldIndex)[index]);
        }

        public float GetStartLifetime(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex)[index];
        }

        public float GetRemainingLifetime(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex)[index];
        }

        public Color GetColor(int index)
        {
            return ToColor(commonColumn.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex)[index]);
        }

        public float GetSize(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex)[index];
        }

        public int GetMeshIndex(int index)
        {
            return commonColumn.GetFieldArray<int>(VividParticleCommon.MeshIndexFieldIndex)[index];
        }

        public Vector3 GetAccumulatedRotation(int index)
        {
            return ToVector3(commonColumn.GetFieldArray<float3>(
                VividParticleCommon.AccumulatedRotationFieldIndex)[index]);
        }

        public uint GetRandomSeed(int index)
        {
            return commonColumn.GetFieldArray<uint>(VividParticleCommon.RandomSeedFieldIndex)[index];
        }

        public float GetNoiseSizeMultiplier(int index)
        {
            if (m_NoiseStateColumn == null)
                return 1.0f;

            return m_NoiseStateColumn.GetFieldArray<float>(
                VividParticleNoiseState.SizeMultiplierFieldIndex)[index];
        }

        public VividEcsPageInfo GetPageInfo(int pageIndex)
        {
            return m_Line.GetPageInfo(pageIndex);
        }

        public VividEcsPageGroup CreatePageGroup(Allocator allocator)
        {
            return m_Line.CreatePageGroup(allocator);
        }

        public VividEcsPageGroup CreateSimulationPageGroup(Allocator allocator)
        {
            VividEcsQuery query = m_World.CreateQuery().WithAll(m_CommonTypeIndex);
            return m_World.CreatePageGroup(query, allocator);
        }

        public List<VividEcsArchetypeLineGroup> CreateLineGroups()
        {
            SyncRendererSharedKeyForQueries();
            VividEcsQuery query = m_World.CreateQuery().WithAll(m_CommonTypeIndex);
            return m_World.CreateArchetypeLineGroups(query, m_RendererSharedKeyTypeIndex);
        }

        private int CountLineGroups()
        {
            SyncRendererSharedKeyForQueries();
            VividEcsQuery query = m_World.CreateQuery().WithAll(m_CommonTypeIndex);
            return m_World.CreateArchetypeLineGroupMap(
                query,
                m_LineGroupScratch,
                m_RendererSharedKeyTypeIndex);
        }

        public unsafe bool TryGetColumnView(out VividParticleEcsColumnView view)
        {
            view = default;
            if (!isCreated)
                return false;

            VividEcsSoaColumn<VividParticleCommon> common = m_CommonColumn;
            int keepMaskCapacity = m_KeepMask.IsCreated ? m_KeepMask.Length : 0;
            int activeCountOutputLength = m_ActiveCountOutput.IsCreated ? m_ActiveCountOutput.Length : 0;
            if (!m_ColumnView.IsValid
                || m_CachedCommonColumnVersion != common.version
                || m_CachedAnimatedMotionColumnVersion != (m_AnimatedMotionColumn?.version ?? -1)
                || m_CachedNoiseStateColumnVersion != (m_NoiseStateColumn?.version ?? -1)
                || m_CachedInheritVelocityStateColumnVersion
                    != (m_InheritVelocityStateColumn?.version ?? -1)
                || m_CachedTriggerStateColumnVersion != (m_TriggerStateColumn?.version ?? -1)
                || m_CachedKeepMaskCapacity != keepMaskCapacity
                || m_CachedActiveCountOutputLength != activeCountOutputLength)
            {
                NativeArray<float3> positions =
                    common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
                NativeArray<float3> velocities =
                    common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
                NativeArray<float> startLifetimes =
                    common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex);
                NativeArray<float> remainingLifetimes =
                    common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);
                NativeArray<float4> colors =
                    common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex);
                NativeArray<float> sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex);
                NativeArray<int> meshIndices = common.GetFieldArray<int>(VividParticleCommon.MeshIndexFieldIndex);
                NativeArray<float3> accumulatedRotations = common.GetFieldArray<float3>(
                    VividParticleCommon.AccumulatedRotationFieldIndex);
                NativeArray<uint> randomSeeds = common.GetFieldArray<uint>(
                    VividParticleCommon.RandomSeedFieldIndex);
                NativeArray<float3> animatedVelocities = m_AnimatedMotionColumn != null
                    ? m_AnimatedMotionColumn.GetFieldArray<float3>(VividParticleAnimatedMotion.VelocityFieldIndex)
                    : default;
                NativeArray<float3> noisePhases = m_NoiseStateColumn != null
                    ? m_NoiseStateColumn.GetFieldArray<float3>(VividParticleNoiseState.PhaseFieldIndex)
                    : default;
                NativeArray<float> noiseSizeMultipliers = m_NoiseStateColumn != null
                    ? m_NoiseStateColumn.GetFieldArray<float>(VividParticleNoiseState.SizeMultiplierFieldIndex)
                    : default;
                NativeArray<float3> initialEmitterVelocities = m_InheritVelocityStateColumn != null
                    ? m_InheritVelocityStateColumn.GetFieldArray<float3>(
                        VividParticleInheritVelocityState.InitialVelocityFieldIndex)
                    : default;
                NativeArray<byte> triggerPreviousInside = m_TriggerStateColumn != null
                    ? m_TriggerStateColumn.GetFieldArray<byte>(
                        VividParticleTriggerState.PreviousInsideFieldIndex)
                    : default;
                NativeArray<byte> triggerCurrentInside = m_TriggerStateColumn != null
                    ? m_TriggerStateColumn.GetFieldArray<byte>(
                        VividParticleTriggerState.CurrentInsideFieldIndex)
                    : default;
                NativeArray<ulong> triggerColliderEntityIds = m_TriggerStateColumn != null
                    ? m_TriggerStateColumn.GetFieldArray<ulong>(
                        VividParticleTriggerState.ColliderEntityIdFieldIndex)
                    : default;

                m_ColumnViewVersion = m_ColumnViewVersion == int.MaxValue ? 1 : m_ColumnViewVersion + 1;
                m_ColumnView = new VividParticleEcsColumnView
                {
                    Positions = (float3*)positions.GetUnsafePtr(),
                    Velocities = (float3*)velocities.GetUnsafePtr(),
                    AnimatedVelocities = animatedVelocities.IsCreated
                        ? (float3*)animatedVelocities.GetUnsafePtr()
                        : null,
                    InitialEmitterVelocities = initialEmitterVelocities.IsCreated
                        ? (float3*)initialEmitterVelocities.GetUnsafePtr()
                        : null,
                    StartLifetimes = (float*)startLifetimes.GetUnsafePtr(),
                    RemainingLifetimes = (float*)remainingLifetimes.GetUnsafePtr(),
                    Colors = (float4*)colors.GetUnsafePtr(),
                    Sizes = (float*)sizes.GetUnsafePtr(),
                    MeshIndices = (int*)meshIndices.GetUnsafePtr(),
                    AccumulatedRotations = (float3*)accumulatedRotations.GetUnsafePtr(),
                    RandomSeeds = (uint*)randomSeeds.GetUnsafePtr(),
                    NoisePhases = noisePhases.IsCreated ? (float3*)noisePhases.GetUnsafePtr() : null,
                    NoiseSizeMultipliers = noiseSizeMultipliers.IsCreated
                        ? (float*)noiseSizeMultipliers.GetUnsafePtr()
                        : null,
                    TriggerPreviousInside = triggerPreviousInside.IsCreated
                        ? (byte*)triggerPreviousInside.GetUnsafePtr()
                        : null,
                    TriggerCurrentInside = triggerCurrentInside.IsCreated
                        ? (byte*)triggerCurrentInside.GetUnsafePtr()
                        : null,
                    TriggerColliderEntityIds = triggerColliderEntityIds.IsCreated
                        ? (ulong*)triggerColliderEntityIds.GetUnsafePtr()
                        : null,
                    KeepMask = m_KeepMask.IsCreated ? (byte*)m_KeepMask.GetUnsafePtr() : null,
                    ActiveCountOutput = m_ActiveCountOutput.IsCreated
                        ? (int*)m_ActiveCountOutput.GetUnsafePtr()
                        : null,
                    ArchetypeLineId = m_Line.ArchetypeLineId,
                    Capacity = positions.Length,
                    Version = m_ColumnViewVersion,
                };
                m_CachedCommonColumnVersion = common.version;
                m_CachedAnimatedMotionColumnVersion = m_AnimatedMotionColumn?.version ?? -1;
                m_CachedNoiseStateColumnVersion = m_NoiseStateColumn?.version ?? -1;
                m_CachedInheritVelocityStateColumnVersion =
                    m_InheritVelocityStateColumn?.version ?? -1;
                m_CachedTriggerStateColumnVersion = m_TriggerStateColumn?.version ?? -1;
                m_CachedKeepMaskCapacity = keepMaskCapacity;
                m_CachedActiveCountOutputLength = activeCountOutputLength;
                m_ColumnViewRefreshCount++;
            }

            view = m_ColumnView;
            return view.IsValid;
        }

        public bool EnsureColumnView()
        {
            unsafe
            {
                return TryGetColumnView(out _);
            }
        }

        public bool TryGetCommonArrays(
            out NativeArray<float3> positions,
            out NativeArray<float3> velocities,
            out NativeArray<float> startLifetimes,
            out NativeArray<float> remainingLifetimes,
            out NativeArray<float4> colors,
            out NativeArray<float> sizes)
        {
            positions = default;
            velocities = default;
            startLifetimes = default;
            remainingLifetimes = default;
            colors = default;
            sizes = default;

            if (!isCreated)
                return false;

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
            velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
            startLifetimes = common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex);
            remainingLifetimes = common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);
            colors = common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex);
            sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex);
            return true;
        }

        public bool TryGetMeshIndexArray(out NativeArray<int> meshIndices)
        {
            meshIndices = default;
            if (!isCreated)
                return false;

            meshIndices = commonColumn.GetFieldArray<int>(VividParticleCommon.MeshIndexFieldIndex);
            return true;
        }

        private VividEcsSoaColumn<VividParticleCommon> commonColumn => m_CommonColumn;

        private void EnsureKeepMaskCapacity(int requestedCapacity)
        {
            requestedCapacity = math.max(1, requestedCapacity);
            if (m_KeepMask.IsCreated && m_KeepMask.Length >= requestedCapacity)
                return;

            if (m_KeepMask.IsCreated)
                m_KeepMask.Dispose();

            m_KeepMask = new NativeArray<byte>(requestedCapacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        private void EnsureSimulationPageCapacity(int requestedPageCount)
        {
            requestedPageCount = math.max(1, requestedPageCount);
            if (m_SimulationPages.IsCreated && m_SimulationPages.Length >= requestedPageCount)
                return;

            if (m_SimulationPages.IsCreated)
                m_SimulationPages.Dispose();

            m_SimulationPages = new NativeArray<VividEcsPageInfo>(
                requestedPageCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private void EnsureStandaloneCompactWorkCapacity()
        {
            if (m_StandaloneCompactWorks.IsCreated)
                return;

            m_StandaloneCompactWorks = new NativeArray<VividParticleEcsCompactWork>(
                1,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private void EnsureStandaloneIntegrateWorkCapacity(int requestedCount)
        {
            if (m_StandaloneIntegrateWorks.IsCreated
                && m_StandaloneIntegrateWorks.Length >= requestedCount)
            {
                return;
            }

            if (m_StandaloneIntegrateWorks.IsCreated)
                m_StandaloneIntegrateWorks.Dispose();

            m_StandaloneIntegrateWorks = new NativeArray<VividParticleEcsIntegratePageWork>(
                math.max(1, requestedCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private unsafe VividParticleEcsCompactWork CreateCompactWork(int count)
        {
            if (!TryGetColumnView(out VividParticleEcsColumnView columnView))
                return default;

            return new VividParticleEcsCompactWork
            {
                ActiveCount = count,
                Capacity = columnView.Capacity,
                Positions = columnView.Positions,
                Velocities = columnView.Velocities,
                AnimatedVelocities = columnView.AnimatedVelocities,
                InitialEmitterVelocities = columnView.InitialEmitterVelocities,
                StartLifetimes = columnView.StartLifetimes,
                RemainingLifetimes = columnView.RemainingLifetimes,
                Colors = columnView.Colors,
                Sizes = columnView.Sizes,
                MeshIndices = columnView.MeshIndices,
                AccumulatedRotations = columnView.AccumulatedRotations,
                RandomSeeds = columnView.RandomSeeds,
                NoisePhases = columnView.NoisePhases,
                NoiseSizeMultipliers = columnView.NoiseSizeMultipliers,
                TriggerPreviousInside = columnView.TriggerPreviousInside,
                TriggerCurrentInside = columnView.TriggerCurrentInside,
                TriggerColliderEntityIds = columnView.TriggerColliderEntityIds,
                KeepMask = columnView.KeepMask,
                ActiveCountOutput = columnView.ActiveCountOutput,
            };
        }

        private void SyncRendererSharedKeyForQueries()
        {
            m_Line.SetSharedComponent(m_RendererSharedKey);
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private static float3 CreateNoisePhase(int index)
        {
            uint state = (uint)(index + 1) * 747796405u + 2891336453u;
            var random = new Unity.Mathematics.Random(state == 0u ? 1u : state);
            return random.NextFloat3(new float3(-1024.0f), new float3(1024.0f));
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
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

        private static quaternion ToQuaternion(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }

        private static Color ToColor(float4 value)
        {
            return new Color(value.x, value.y, value.z, value.w);
        }
    }
}
