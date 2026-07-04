using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VividRP.Runtime.Particle.ECS
{
    internal interface IVividParticlePageJob
    {
        void Execute(VividParticlePageInfo page);
    }

    internal static class VividParticlePageJobExtensions
    {
        public static JobHandle Schedule<TJob>(
            this TJob jobData,
            NativeArray<VividParticlePageInfo> pages,
            JobHandle dependency = default)
            where TJob : struct, IVividParticlePageJob
        {
            if (!pages.IsCreated || pages.Length == 0)
                return dependency;

            var wrapper = new VividParticlePageJobWrapper<TJob>
            {
                Pages = pages,
                JobData = jobData,
            };
            return wrapper.Schedule(pages.Length, 1, dependency);
        }
    }

    internal struct VividParticlePageJobWrapper<TJob> : IJobParallelFor
        where TJob : struct, IVividParticlePageJob
    {
        [ReadOnly]
        public NativeArray<VividParticlePageInfo> Pages;

        public TJob JobData;

        public void Execute(int index)
        {
            JobData.Execute(Pages[index]);
        }
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
}
