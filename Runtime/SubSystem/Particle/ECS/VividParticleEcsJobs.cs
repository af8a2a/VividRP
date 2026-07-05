using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal unsafe struct VividParticleEcsIntegratePageWork
    {
        public VividEcsPageInfo Page;
        public float DeltaTime;
        public float3 Gravity;
        public int PositionLength;

        [NativeDisableUnsafePtrRestriction]
        public float3* Positions;

        [NativeDisableUnsafePtrRestriction]
        public float3* Velocities;

        [NativeDisableUnsafePtrRestriction]
        public float* RemainingLifetimes;

        [NativeDisableUnsafePtrRestriction]
        public byte* KeepMask;
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleEcsIntegrateJob : IJob
    {
        public float DeltaTime;
        public float3 Gravity;
        public int ActiveCount;
        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;
        public NativeArray<float> StartLifetimes;
        public NativeArray<float> RemainingLifetimes;
        public NativeArray<float4> Colors;
        public NativeArray<float> Sizes;
        public NativeArray<int> ActiveCountOutput;

        public void Execute()
        {
            int count = math.clamp(ActiveCount, 0, Positions.Length);
            int index = 0;
            while (index < count)
            {
                float remainingLifetime = RemainingLifetimes[index] - DeltaTime;
                if (remainingLifetime <= 0.0f)
                {
                    count--;
                    if (index != count)
                        CopyParticle(count, index);
                    continue;
                }

                float3 velocity = Velocities[index] + Gravity * DeltaTime;
                Velocities[index] = velocity;
                Positions[index] += velocity * DeltaTime;
                RemainingLifetimes[index] = remainingLifetime;
                index++;
            }

            ActiveCountOutput[0] = count;
        }

        private void CopyParticle(int sourceIndex, int destinationIndex)
        {
            Positions[destinationIndex] = Positions[sourceIndex];
            Velocities[destinationIndex] = Velocities[sourceIndex];
            StartLifetimes[destinationIndex] = StartLifetimes[sourceIndex];
            RemainingLifetimes[destinationIndex] = RemainingLifetimes[sourceIndex];
            Colors[destinationIndex] = Colors[sourceIndex];
            Sizes[destinationIndex] = Sizes[sourceIndex];
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleEcsIntegratePagesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividEcsPageInfo> Pages;

        public float DeltaTime;
        public float3 Gravity;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Positions;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Velocities;

        [NativeDisableParallelForRestriction]
        public NativeArray<float> RemainingLifetimes;

        [NativeDisableParallelForRestriction]
        public NativeArray<byte> KeepMask;

        public void Execute(int pageIndex)
        {
            VividEcsPageInfo page = Pages[pageIndex];
            int pageEnd = math.min(page.StartIndex + page.EntryCount, Positions.Length);
            for (int index = page.StartIndex; index < pageEnd; index++)
            {
                float remainingLifetime = RemainingLifetimes[index] - DeltaTime;
                if (remainingLifetime <= 0.0f)
                {
                    KeepMask[index] = 0;
                    continue;
                }

                float3 velocity = Velocities[index] + Gravity * DeltaTime;
                Velocities[index] = velocity;
                Positions[index] += velocity * DeltaTime;
                RemainingLifetimes[index] = remainingLifetime;
                KeepMask[index] = 1;
            }
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsIntegratePageWorksJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleEcsIntegratePageWork> Works;

        public void Execute(int workIndex)
        {
            VividParticleEcsIntegratePageWork work = Works[workIndex];
            VividEcsPageInfo page = work.Page;
            int pageEnd = math.min(page.StartIndex + page.EntryCount, work.PositionLength);
            for (int index = page.StartIndex; index < pageEnd; index++)
            {
                float remainingLifetime = work.RemainingLifetimes[index] - work.DeltaTime;
                if (remainingLifetime <= 0.0f)
                {
                    work.KeepMask[index] = 0;
                    continue;
                }

                float3 velocity = work.Velocities[index] + work.Gravity * work.DeltaTime;
                work.Velocities[index] = velocity;
                work.Positions[index] += velocity * work.DeltaTime;
                work.RemainingLifetimes[index] = remainingLifetime;
                work.KeepMask[index] = 1;
            }
        }
    }
}
