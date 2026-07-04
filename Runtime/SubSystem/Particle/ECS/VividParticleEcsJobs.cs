using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
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
    internal struct VividParticleEcsIntegratePageJob : IVividEcsPageJob
    {
        public float DeltaTime;
        public float3 Gravity;
        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;
        public NativeArray<float> StartLifetimes;
        public NativeArray<float> RemainingLifetimes;
        public NativeArray<byte> KeepMask;

        public void Execute(VividEcsPageInfo page)
        {
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
}
