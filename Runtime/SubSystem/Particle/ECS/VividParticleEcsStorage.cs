using System;
using Unity.Collections;
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
        private NativeArray<int> m_ActiveCountOutput;
        private NativeArray<byte> m_KeepMask;
        private NativeArray<VividEcsPageInfo> m_SimulationPages;
        private VividParticleRendererSharedKey m_RendererSharedKey = VividParticleRendererSharedKey.Invalid;
        private int m_PendingIntegrateActiveCount;

        public VividParticleEcsStorage()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            m_CommonTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            m_SystemIdTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>();
            m_RendererSharedKeyTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            m_World = new VividEcsWorld();
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
            set => m_RendererSharedKey = value;
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
            m_World.Dispose();
            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput.Dispose();

            if (m_KeepMask.IsCreated)
                m_KeepMask.Dispose();

            if (m_SimulationPages.IsCreated)
                m_SimulationPages.Dispose();

            m_ActiveCountOutput = default;
            m_KeepMask = default;
            m_SimulationPages = default;
            m_PendingIntegrateActiveCount = 0;
        }

        public bool Add(
            Vector3 position,
            Vector3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            Color color)
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
            if (m_KeepMask.IsCreated && index < m_KeepMask.Length)
                m_KeepMask[index] = 1;

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

            handle = job.Schedule(livePageCount, innerloopBatchCount: 1, dependency);
            return true;
        }

        public void ApplyScheduledIntegrateResult()
        {
            if (!m_ActiveCountOutput.IsCreated)
                return;

            int originalActiveCount = math.min(
                m_PendingIntegrateActiveCount > 0 ? m_PendingIntegrateActiveCount : activeCount,
                m_KeepMask.IsCreated ? m_KeepMask.Length : activeCount);
            for (int index = originalActiveCount - 1; index >= 0; index--)
            {
                if (m_KeepMask[index] != 0)
                    continue;

                m_Line.RemoveAtSwapBack(index, out _, out _);
            }

            m_ActiveCountOutput[0] = activeCount;
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

        private static Color ToColor(float4 value)
        {
            return new Color(value.x, value.y, value.z, value.w);
        }
    }
}
