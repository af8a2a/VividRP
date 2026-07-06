using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal sealed class VividParticleEcsStorage : IDisposable
    {
        private readonly VividEcsWorld m_World;
        private readonly VividEcsArchetypeLine m_Line;
        private readonly VividEcsTypeIndex m_CommonTypeIndex;
        private readonly VividEcsTypeIndex m_SystemIdTypeIndex;
        private readonly VividEcsTypeIndex m_RendererSharedKeyTypeIndex;
        private readonly bool m_OwnsWorld;
        private NativeArray<int> m_ActiveCountOutput;
        private NativeArray<byte> m_KeepMask;
        private NativeArray<VividEcsPageInfo> m_SimulationPages;
        private NativeArray<VividParticleEcsCompactWork> m_StandaloneCompactWorks;
        private VividParticleRendererSharedKey m_RendererSharedKey = VividParticleRendererSharedKey.Invalid;
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
            m_SystemIdTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>();
            m_RendererSharedKeyTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            m_World = world;
            m_OwnsWorld = ownsWorld;
            m_Line = m_World.CreateArchetypeLine(
                0,
                m_CommonTypeIndex,
                m_SystemIdTypeIndex,
                m_RendererSharedKeyTypeIndex);
            m_Line.SetSharedComponent(VividParticleSystemId.Invalid);
            m_Line.SetSharedComponent(VividParticleRendererSharedKey.Invalid);
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

        public int queryLineGroupCount => CreateLineGroups().Count;

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

            m_ActiveCountOutput = default;
            m_KeepMask = default;
            m_SimulationPages = default;
            m_StandaloneCompactWorks = default;
            m_PendingIntegrateActiveCount = 0;
        }

        public bool Add(
            Vector3 position,
            Vector3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            Color color,
            int meshIndex = 0)
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
            firstIndex = activeCount;
            reservedCount = 0;
            if (!isCreated || requestedCount <= 0 || !works.IsCreated)
                return false;

            reservedCount = m_Line.AppendRange(requestedCount, out firstIndex);
            if (reservedCount <= 0)
                return false;

            EnsureKeepMaskCapacity(capacity);
            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            NativeArray<float3> positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
            NativeArray<float3> velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
            NativeArray<float> startLifetimes =
                common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex);
            NativeArray<float> remainingLifetimes =
                common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);
            NativeArray<float4> colors = common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex);
            NativeArray<float> sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex);
            NativeArray<int> meshIndices = common.GetFieldArray<int>(VividParticleCommon.MeshIndexFieldIndex);

            works.Add(new VividParticleEcsInitializeParticlesWork
            {
                StartIndex = firstIndex,
                Count = reservedCount,
                Capacity = positions.Length,
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
                Positions = (float3*)positions.GetUnsafePtr(),
                Velocities = (float3*)velocities.GetUnsafePtr(),
                StartLifetimes = (float*)startLifetimes.GetUnsafePtr(),
                RemainingLifetimes = (float*)remainingLifetimes.GetUnsafePtr(),
                Colors = (float4*)colors.GetUnsafePtr(),
                Sizes = (float*)sizes.GetUnsafePtr(),
                MeshIndices = (int*)meshIndices.GetUnsafePtr(),
                KeepMask = (byte*)m_KeepMask.GetUnsafePtr(),
            });
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

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            NativeArray<VividEcsPageInfo> pages = m_SimulationPages.GetSubArray(0, livePageCount);
            var job = new VividParticleEcsIntegratePagesJob
            {
                Pages = pages,
                DeltaTime = deltaTime,
                Gravity = ToFloat3(gravity),
                Positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex),
                Velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex),
                RemainingLifetimes = common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex),
                KeepMask = m_KeepMask,
            };

            JobHandle integrateHandle = job.Schedule(livePageCount, innerloopBatchCount: 1, dependency);
            EnsureStandaloneCompactWorkCapacity();
            m_StandaloneCompactWorks[0] = CreateCompactWork(count);
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

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            NativeArray<float3> positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
            NativeArray<float3> velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
            NativeArray<float> remainingLifetimes =
                common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);

            float3 gravityValue = ToFloat3(gravity);
            for (int pageIndex = 0; pageIndex < livePageCount; pageIndex++)
            {
                pageWorks.Add(new VividParticleEcsIntegratePageWork
                {
                    Page = m_Line.GetPageInfo(pageIndex),
                    DeltaTime = deltaTime,
                    Gravity = gravityValue,
                    PositionLength = positions.Length,
                    Positions = (float3*)positions.GetUnsafePtr(),
                    Velocities = (float3*)velocities.GetUnsafePtr(),
                    RemainingLifetimes = (float*)remainingLifetimes.GetUnsafePtr(),
                    KeepMask = (byte*)m_KeepMask.GetUnsafePtr(),
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

        public System.Collections.Generic.List<VividEcsArchetypeLineGroup> CreateLineGroups()
        {
            SyncRendererSharedKeyForQueries();
            VividEcsQuery query = m_World.CreateQuery().WithAll(m_CommonTypeIndex);
            return m_World.CreateArchetypeLineGroups(query);
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

        private VividEcsSoaColumn<VividParticleCommon> commonColumn =>
            m_Line.GetColumn<VividEcsSoaColumn<VividParticleCommon>>(m_CommonTypeIndex);

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

        private unsafe VividParticleEcsCompactWork CreateCompactWork(int count)
        {
            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            NativeArray<float3> positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
            NativeArray<float3> velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
            NativeArray<float> startLifetimes =
                common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex);
            NativeArray<float> remainingLifetimes =
                common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);
            NativeArray<float4> colors = common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex);
            NativeArray<float> sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex);
            NativeArray<int> meshIndices = common.GetFieldArray<int>(VividParticleCommon.MeshIndexFieldIndex);
            return new VividParticleEcsCompactWork
            {
                ActiveCount = count,
                Capacity = positions.Length,
                Positions = (float3*)positions.GetUnsafePtr(),
                Velocities = (float3*)velocities.GetUnsafePtr(),
                StartLifetimes = (float*)startLifetimes.GetUnsafePtr(),
                RemainingLifetimes = (float*)remainingLifetimes.GetUnsafePtr(),
                Colors = (float4*)colors.GetUnsafePtr(),
                Sizes = (float*)sizes.GetUnsafePtr(),
                MeshIndices = (int*)meshIndices.GetUnsafePtr(),
                KeepMask = (byte*)m_KeepMask.GetUnsafePtr(),
                ActiveCountOutput = (int*)m_ActiveCountOutput.GetUnsafePtr(),
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
