using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.Trail
{
    internal unsafe struct VividParticleTrailSource
    {
        public int SystemId;
        public int ArchetypeLineId;
        public int ConfigSlot;
        public int ActiveCount;
        public int Capacity;
        public int SimulationSpace;
        public int WorldSpace;
        public int DieWithParticles;
        public int SizeAffectsLifetime;
        public float Ratio;
        public float MinimumVertexDistance;
        public float4x4 LocalToWorld;
        public float4x4 WorldToLocal;

        [NativeDisableUnsafePtrRestriction]
        public float3* Positions;
        [NativeDisableUnsafePtrRestriction]
        public float* StartLifetimes;
        [NativeDisableUnsafePtrRestriction]
        public float* RemainingLifetimes;
        [NativeDisableUnsafePtrRestriction]
        public float* Sizes;
        [NativeDisableUnsafePtrRestriction]
        public uint* RandomSeeds;
        [NativeDisableUnsafePtrRestriction]
        public int* TrailHandleIndices;
        [NativeDisableUnsafePtrRestriction]
        public int* TrailHandleGenerations;
    }

    internal struct VividParticleTrailPageWork
    {
        public VividEcsPageInfo Page;
        public VividParticleTrailSource Source;
        public int StartIndex;
        public int Count;
    }

    internal sealed class VividParticleTrailManager : IDisposable
    {
        private readonly VividParticleTrailTable m_Table = new();
        private NativeList<VividParticleTrailPageWork> m_PageWorks;
        private JobHandle m_PendingHandle;
        private bool m_HasPendingJob;
        private int m_UpdateVersion;
        private int m_SourceCount;
        private int m_PageWorkCount;
        private int m_ActiveParticleCount;
        private int m_RequiredTileCapacity;

        public int sourceCount => m_SourceCount;

        public int pageWorkCount => m_PageWorkCount;

        public int activeParticleCount => m_ActiveParticleCount;

        public int allocatedTrailCount
        {
            get
            {
                Complete();
                return m_Table.allocatedCount;
            }
        }

        public int tileCapacity => m_Table.tileCapacity;

        public VividParticleTrailTableView tableView
        {
            get
            {
                Complete();
                return m_Table.GetView();
            }
        }

        public void BeginCollect()
        {
            Complete();
            if (!m_PageWorks.IsCreated)
                m_PageWorks = new NativeList<VividParticleTrailPageWork>(32, Allocator.Persistent);
            else
                m_PageWorks.Clear();
            m_SourceCount = 0;
            m_PageWorkCount = 0;
            m_ActiveParticleCount = 0;
            m_RequiredTileCapacity = 0;
        }

        public unsafe void AddSource(VividParticleTrailSource source)
        {
            if (!m_PageWorks.IsCreated
                || source.ActiveCount <= 0
                || source.Capacity <= 0
                || source.Positions == null
                || source.StartLifetimes == null
                || source.RemainingLifetimes == null
                || source.Sizes == null
                || source.RandomSeeds == null
                || source.TrailHandleIndices == null
                || source.TrailHandleGenerations == null)
            {
                return;
            }

            source.ActiveCount = math.min(source.ActiveCount, source.Capacity);
            int pageCount = (source.ActiveCount + VividEcsConstants.PageEntryCount - 1)
                / VividEcsConstants.PageEntryCount;
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                int startIndex = pageIndex * VividEcsConstants.PageEntryCount;
                m_PageWorks.Add(new VividParticleTrailPageWork
                {
                    Page = new VividEcsPageInfo(
                        source.ArchetypeLineId,
                        pageIndex,
                        startIndex,
                        math.min(
                            VividEcsConstants.PageEntryCount,
                            source.ActiveCount - startIndex)),
                    Source = source,
                    StartIndex = startIndex,
                    Count = math.min(
                        VividEcsConstants.PageEntryCount,
                        source.ActiveCount - startIndex),
                });
                m_PageWorkCount++;
            }

            m_SourceCount++;
            m_ActiveParticleCount += source.ActiveCount;
            m_RequiredTileCapacity += source.Capacity;
        }

        public void Schedule(
            NativeArray<VividParticleNativeRenderModuleConfig> configs,
            float currentTime,
            JobHandle particleDataDependency = default)
        {
            Complete();
            int detachedEstimate = math.max(0, m_Table.allocatedCount - m_ActiveParticleCount);
            int requiredCapacity = math.max(8, m_RequiredTileCapacity + detachedEstimate);
            m_Table.EnsureCapacity(requiredCapacity);
            int tileCapacity = m_Table.tileCapacity;
            m_UpdateVersion = m_UpdateVersion == int.MaxValue ? 1 : m_UpdateVersion + 1;

            JobHandle updateHandle = particleDataDependency;
            if (m_PageWorks.IsCreated && m_PageWorks.Length > 0 && configs.IsCreated)
            {
                var updateJob = new VividParticleTrailUpdateJob
                {
                    Works = m_PageWorks.AsArray(),
                    Configs = configs,
                    Table = m_Table.GetView(),
                    CurrentTime = currentTime,
                    UpdateVersion = m_UpdateVersion,
                };
                updateHandle = updateJob.ScheduleParallelEmbedded(
                    m_PageWorks.AsArray(),
                    pageInfoByteOffset: 0,
                    dependency: particleDataDependency,
                    innerloopBatchCount: 1,
                    dispatchMode: VividEcsPageDispatchMode.Average);
            }

            m_PendingHandle = new VividParticleTrailSweepJob
            {
                Table = m_Table.GetView(),
                CurrentTime = currentTime,
                UpdateVersion = m_UpdateVersion,
            }.Schedule(tileCapacity, innerloopBatchCount: 32, updateHandle);
            m_HasPendingJob = true;
            JobHandle.ScheduleBatchedJobs();
        }

        public void Complete()
        {
            if (!m_HasPendingJob)
                return;

            m_PendingHandle.Complete();
            m_PendingHandle = default;
            m_HasPendingJob = false;
        }

        public void Clear()
        {
            Complete();
            if (m_PageWorks.IsCreated)
                m_PageWorks.Clear();
            m_Table.Clear();
            m_SourceCount = 0;
            m_PageWorkCount = 0;
            m_ActiveParticleCount = 0;
            m_RequiredTileCapacity = 0;
        }

        public void Dispose()
        {
            Complete();
            if (m_PageWorks.IsCreated)
                m_PageWorks.Dispose();
            m_PageWorks = default;
            m_Table.Dispose();
            m_SourceCount = 0;
            m_PageWorkCount = 0;
            m_ActiveParticleCount = 0;
            m_RequiredTileCapacity = 0;
        }
    }

    [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleTrailUpdateJob : IVividEcsPageJob
    {
        [ReadOnly]
        public NativeArray<VividParticleTrailPageWork> Works;
        [ReadOnly]
        public NativeArray<VividParticleNativeRenderModuleConfig> Configs;
        public VividParticleTrailTableView Table;
        public float CurrentTime;
        public int UpdateVersion;

        public void Execute(VividEcsPageInfo page, int workIndex)
        {
            VividParticleTrailPageWork work = Works[workIndex];
            VividParticleTrailSource source = work.Source;
            if ((uint)source.ConfigSlot >= (uint)Configs.Length)
                return;

            VividParticleNativeRenderModuleConfig config = Configs[source.ConfigSlot];
            if (config.TrailsEnabled == 0 || config.TrailsMode != (int)VividParticleTrailMode.PerParticle)
                return;

            int startIndex = math.max(work.StartIndex, page.StartIndex);
            int endIndex = math.min(startIndex + page.EntryCount, source.ActiveCount);
            for (int particleIndex = startIndex; particleIndex < endIndex; particleIndex++)
            {
                VividParticleTrailHandle handle = new(
                    source.TrailHandleIndices[particleIndex],
                    source.TrailHandleGenerations[particleIndex]);
                if (!Table.IsValid(handle))
                {
                    if (!ShouldCreateTrail(source.RandomSeeds[particleIndex], source.Ratio)
                        || !Table.TryAllocate(source.RandomSeeds[particleIndex], out handle))
                    {
                        source.TrailHandleIndices[particleIndex] = -1;
                        source.TrailHandleGenerations[particleIndex] = 0;
                        continue;
                    }

                    source.TrailHandleIndices[particleIndex] = handle.Index;
                    source.TrailHandleGenerations[particleIndex] = handle.Generation;
                }

                float normalizedLifetime = 1.0f - math.saturate(
                    source.RemainingLifetimes[particleIndex]
                    / math.max(0.000001f, source.StartLifetimes[particleIndex]));
                float lifetime = Sample(config.TrailsLifetimeLut, normalizedLifetime);
                if (source.SizeAffectsLifetime != 0)
                    lifetime *= math.max(0.0f, source.Sizes[particleIndex]);

                float3 position = ResolveTrailPosition(source, source.Positions[particleIndex]);
                Table.AppendControlPoint(
                    handle,
                    position,
                    CurrentTime,
                    source.MinimumVertexDistance);
                Table.PruneExpiredControlPoints(handle, CurrentTime, lifetime);
                VividParticleTrailTileHeader header = Table.Headers[handle.Index];
                header.LastSeenUpdate = UpdateVersion;
                header.IsDetached = 0;
                header.DieWithParticles = source.DieWithParticles;
                header.Lifetime = lifetime;
                header.DetachTime = 0.0f;
                Table.Headers[handle.Index] = header;
            }
        }

        private static bool ShouldCreateTrail(uint randomSeed, float ratio)
        {
            if (ratio >= 1.0f)
                return true;
            if (ratio <= 0.0f)
                return false;

            uint hash = randomSeed;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash / (float)uint.MaxValue <= ratio;
        }

        private static float3 ResolveTrailPosition(VividParticleTrailSource source, float3 position)
        {
            bool particleWorldSpace = source.SimulationSpace
                == (int)VividParticleSystemSimulationSpace.World;
            if (source.WorldSpace != 0)
                return particleWorldSpace ? position : math.transform(source.LocalToWorld, position);
            return particleWorldSpace ? math.transform(source.WorldToLocal, position) : position;
        }

        private static float Sample(float* values, float normalizedValue)
        {
            float sample = math.saturate(normalizedValue)
                * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            int lower = (int)math.floor(sample);
            int upper = math.min(
                lower + 1,
                VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            return math.lerp(values[lower], values[upper], sample - lower);
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleTrailSweepJob : IJobParallelFor
    {
        public VividParticleTrailTableView Table;
        public float CurrentTime;
        public int UpdateVersion;

        public void Execute(int tileIndex)
        {
            VividParticleTrailTileHeader header = Table.Headers[tileIndex];
            if (header.IsAllocated == 0 || header.LastSeenUpdate == UpdateVersion)
                return;

            var handle = new VividParticleTrailHandle(tileIndex, header.Generation);
            if (header.DieWithParticles != 0)
            {
                Table.Free(handle);
                return;
            }

            if (header.IsDetached == 0)
            {
                header.IsDetached = 1;
                header.DetachTime = CurrentTime;
                Table.Headers[tileIndex] = header;
            }

            Table.PruneExpiredControlPoints(handle, CurrentTime, header.Lifetime);
            header = Table.Headers[tileIndex];
            if (header.PointCount <= 0
                || CurrentTime - header.DetachTime > math.max(0.0f, header.Lifetime))
            {
                Table.Free(handle);
            }
        }
    }
}
