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

    internal unsafe struct VividParticleEcsCompactWork
    {
        public int ActiveCount;
        public int Capacity;

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
        public byte* KeepMask;

        [NativeDisableUnsafePtrRestriction]
        public int* ActiveCountOutput;
    }

    internal unsafe struct VividParticleEcsInitializeParticlesWork
    {
        public int StartIndex;
        public int Count;
        public int Capacity;
        public int ShapeEnabled;
        public int ShapeType;
        public int SimulationSpace;
        public uint RandomSeed;
        public float StartLifetime;
        public float StartSpeed;
        public float StartSize;
        public float ShapeRadius;
        public float ShapeAngleRadians;
        public float3 ShapeBoxSize;
        public float4 StartColor;
        public float4x4 LocalToWorldMatrix;
        public quaternion WorldRotation;

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

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsCompactWorksJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleEcsCompactWork> Works;

        public void Execute(int workIndex)
        {
            VividParticleEcsCompactWork work = Works[workIndex];
            int count = math.clamp(work.ActiveCount, 0, work.Capacity);
            int index = 0;
            while (index < count)
            {
                if (work.KeepMask[index] != 0)
                {
                    index++;
                    continue;
                }

                count--;
                if (index != count)
                    CopyParticle(work, count, index);
            }

            work.ActiveCountOutput[0] = count;
        }

        private static void CopyParticle(VividParticleEcsCompactWork work, int sourceIndex, int destinationIndex)
        {
            work.Positions[destinationIndex] = work.Positions[sourceIndex];
            work.Velocities[destinationIndex] = work.Velocities[sourceIndex];
            work.StartLifetimes[destinationIndex] = work.StartLifetimes[sourceIndex];
            work.RemainingLifetimes[destinationIndex] = work.RemainingLifetimes[sourceIndex];
            work.Colors[destinationIndex] = work.Colors[sourceIndex];
            work.Sizes[destinationIndex] = work.Sizes[sourceIndex];
            work.KeepMask[destinationIndex] = work.KeepMask[sourceIndex];
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsInitializeParticlesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleEcsInitializeParticlesWork> Works;

        public void Execute(int workIndex)
        {
            VividParticleEcsInitializeParticlesWork work = Works[workIndex];
            int count = math.max(0, work.Count);
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                int particleIndex = work.StartIndex + localIndex;
                if ((uint)particleIndex >= (uint)work.Capacity)
                    break;

                Random random = CreateRandom(work.RandomSeed, particleIndex, localIndex);
                SampleShape(work, ref random, out float3 localPosition, out float3 localDirection);
                localDirection = math.lengthsq(localDirection) > 0.000001f
                    ? math.normalize(localDirection)
                    : new float3(0.0f, 0.0f, 1.0f);

                float3 position = localPosition;
                float3 velocity = localDirection * work.StartSpeed;
                if (work.SimulationSpace == 1)
                {
                    position = math.transform(work.LocalToWorldMatrix, localPosition);
                    float3 worldDirection = math.mul(work.WorldRotation, localDirection);
                    velocity = math.lengthsq(worldDirection) > 0.000001f
                        ? math.normalize(worldDirection) * work.StartSpeed
                        : new float3(0.0f, 0.0f, work.StartSpeed);
                }

                work.Positions[particleIndex] = position;
                work.Velocities[particleIndex] = velocity;
                work.StartLifetimes[particleIndex] = work.StartLifetime;
                work.RemainingLifetimes[particleIndex] = work.StartLifetime;
                work.Colors[particleIndex] = work.StartColor;
                work.Sizes[particleIndex] = work.StartSize;
                work.KeepMask[particleIndex] = 1;
            }
        }

        private static Random CreateRandom(uint seed, int particleIndex, int localIndex)
        {
            uint value = seed
                ^ (uint)(particleIndex + 1) * 747796405u
                ^ (uint)(localIndex + 1) * 2891336453u;
            return new Random(value == 0u ? 1u : value);
        }

        private static void SampleShape(
            VividParticleEcsInitializeParticlesWork work,
            ref Random random,
            out float3 localPosition,
            out float3 localDirection)
        {
            if (work.ShapeEnabled == 0)
            {
                localPosition = float3.zero;
                localDirection = new float3(0.0f, 0.0f, 1.0f);
                return;
            }

            switch (work.ShapeType)
            {
                case 1:
                    localPosition = SampleInsideUnitSphere(ref random) * work.ShapeRadius;
                    localDirection = math.lengthsq(localPosition) > 0.000001f
                        ? math.normalize(localPosition)
                        : SampleUnitVector(ref random);
                    break;
                case 2:
                    localPosition = new float3(
                        random.NextFloat(-work.ShapeBoxSize.x * 0.5f, work.ShapeBoxSize.x * 0.5f),
                        random.NextFloat(-work.ShapeBoxSize.y * 0.5f, work.ShapeBoxSize.y * 0.5f),
                        random.NextFloat(-work.ShapeBoxSize.z * 0.5f, work.ShapeBoxSize.z * 0.5f));
                    localDirection = new float3(0.0f, 0.0f, 1.0f);
                    break;
                case 3:
                    float2 disk = SampleInsideUnitCircle(ref random) * math.max(0.0f, work.ShapeRadius);
                    localPosition = new float3(disk.x, disk.y, 0.0f);
                    localDirection = SampleConeDirection(ref random, work.ShapeAngleRadians);
                    break;
                default:
                    localPosition = float3.zero;
                    localDirection = new float3(0.0f, 0.0f, 1.0f);
                    break;
            }
        }

        private static float3 SampleInsideUnitSphere(ref Random random)
        {
            float3 value;
            do
            {
                value = random.NextFloat3(new float3(-1.0f), new float3(1.0f));
            }
            while (math.lengthsq(value) > 1.0f);

            return value;
        }

        private static float2 SampleInsideUnitCircle(ref Random random)
        {
            float2 value;
            do
            {
                value = random.NextFloat2(new float2(-1.0f), new float2(1.0f));
            }
            while (math.lengthsq(value) > 1.0f);

            return value;
        }

        private static float3 SampleUnitVector(ref Random random)
        {
            float3 value = SampleInsideUnitSphere(ref random);
            return math.lengthsq(value) > 0.000001f ? math.normalize(value) : new float3(0.0f, 0.0f, 1.0f);
        }

        private static float3 SampleConeDirection(ref Random random, float angleRadians)
        {
            float cosMin = math.cos(math.clamp(angleRadians, 0.0f, math.radians(89.0f)));
            float cosTheta = random.NextFloat(cosMin, 1.0f);
            float sinTheta = math.sqrt(math.max(0.0f, 1.0f - cosTheta * cosTheta));
            float phi = random.NextFloat(0.0f, math.PI * 2.0f);
            return math.normalize(new float3(math.cos(phi) * sinTheta, math.sin(phi) * sinTheta, cosTheta));
        }
    }
}
