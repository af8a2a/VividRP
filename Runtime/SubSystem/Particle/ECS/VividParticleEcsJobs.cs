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
        public int* MeshIndices;

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
        public int RandomIndexOffset;
        public int ShapeEnabled;
        public int ShapeType;
        public int SimulationSpace;
        public int MeshCount;
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
        public int* MeshIndices;

        [NativeDisableUnsafePtrRestriction]
        public byte* KeepMask;
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleSimulationPrepareJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleNativeSimulationConfig> Configs;

        [ReadOnly]
        public NativeArray<VividParticleSimulationPrepareInput> Inputs;

        [WriteOnly]
        public NativeArray<VividParticleSimulationPrepareOutput> Outputs;

        public float GravityAcceleration;

        public void Execute(int index)
        {
            VividParticleSimulationPrepareInput input = Inputs[index];
            var output = new VividParticleSimulationPrepareOutput
            {
                SystemId = input.SystemId,
                TimeStep = input.TimeStep,
            };

            if ((uint)input.ConfigSlot >= (uint)Configs.Length)
            {
                Outputs[index] = output;
                return;
            }

            VividParticleNativeSimulationConfig config = Configs[input.ConfigSlot];
            output.ShouldSchedule = input.TimeStep.RequiresAutomaticUpdate(
                    input.ActiveCount,
                    requireActive: true)
                ? 1
                : 0;
            float3 acceleration = new float3(0.0f, -GravityAcceleration * config.GravityModifier, 0.0f);
            if (config.ForceOverLifetimeEnabled != 0)
            {
                float3 force = config.ForceOverLifetime;
                bool particlesUseWorldSpace = config.SimulationSpace == 1;
                bool forceUsesWorldSpace = config.ForceOverLifetimeSpace == 1;
                if (particlesUseWorldSpace != forceUsesWorldSpace)
                {
                    force = particlesUseWorldSpace
                        ? math.mul(input.TimeStep.WorldRotation, force)
                        : math.mul(math.inverse(input.TimeStep.WorldRotation), force);
                }

                acceleration += force;
            }

            output.Gravity = acceleration;
            Outputs[index] = output;
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEmissionPlanJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleNativeSimulationConfig> Configs;

        [ReadOnly]
        public NativeArray<VividParticleNativeBurst> Bursts;

        [ReadOnly]
        public NativeArray<VividParticleEmissionPlanInput> Inputs;

        [WriteOnly]
        public NativeArray<VividParticleEmissionPlanOutput> Outputs;

        public float MinimumSimulationStep;
        public float EmissionAccumulatorTolerance;

        public void Execute(int index)
        {
            VividParticleEmissionPlanInput input = Inputs[index];
            var output = new VividParticleEmissionPlanOutput
            {
                SystemId = input.SystemId,
                Time = input.Time,
                EmissionAccumulator = input.EmissionAccumulator,
                BurstTriggeredMask = input.BurstTriggeredMask,
                RandomState = input.RandomState,
            };

            if ((uint)input.ConfigSlot >= (uint)Configs.Length)
            {
                output.RequiresManagedFallback = 1;
                Outputs[index] = output;
                return;
            }

            VividParticleNativeSimulationConfig config = Configs[input.ConfigSlot];
            if (config.BurstCount > 64
                || config.BurstOffset < 0
                || config.BurstCount < 0
                || config.BurstOffset + config.BurstCount > Bursts.Length)
            {
                output.RequiresManagedFallback = 1;
                Outputs[index] = output;
                return;
            }

            float duration = math.max(MinimumSimulationStep, config.Duration);
            float remaining = math.max(0.0f, input.DeltaTime);
            while (remaining > MinimumSimulationStep)
            {
                float segmentEnd = math.min(duration, output.Time + remaining);
                float segmentDelta = math.max(0.0f, segmentEnd - output.Time);
                if (input.AllowEmission != 0 && config.EmissionEnabled != 0 && segmentDelta > 0.0f)
                {
                    PlanTimeRange(
                        config,
                        output.Time,
                        segmentEnd,
                        segmentDelta,
                        ref output);
                }

                remaining -= segmentDelta;
                output.Time = segmentEnd;
                if (output.Time < duration)
                    break;

                if (config.Loop == 0)
                {
                    output.Time = duration;
                    break;
                }

                output.Time = 0.0f;
                output.BurstTriggeredMask = 0UL;
                if (segmentDelta <= 0.0f)
                    break;
            }

            if (output.EmitCount > 0)
            {
                if (input.CanReserveNative == 0 || input.ActiveCountOutput == null)
                {
                    output.RequiresManagedFallback = 1;
                    Outputs[index] = output;
                    return;
                }

                int activeCount = math.max(0, *input.ActiveCountOutput);
                int maxParticles = math.min(
                    math.max(0, config.MaxParticles),
                    math.max(0, input.InitializeTemplate.Capacity));
                int reservedCount = math.min(output.EmitCount, math.max(0, maxParticles - activeCount));
                if (reservedCount > 0)
                {
                    VividParticleEcsInitializeParticlesWork initializeWork = input.InitializeTemplate;
                    initializeWork.StartIndex = activeCount;
                    initializeWork.Count = reservedCount;
                    initializeWork.RandomSeed = NextRandomState(ref output.RandomState);
                    output.InitializeWork = initializeWork;
                    output.ReservedCount = reservedCount;
                    *input.ActiveCountOutput = activeCount + reservedCount;
                }
            }

            Outputs[index] = output;
        }

        private void PlanTimeRange(
            VividParticleNativeSimulationConfig config,
            float startTime,
            float endTime,
            float deltaTime,
            ref VividParticleEmissionPlanOutput output)
        {
            output.EmissionAccumulator += config.RateOverTime * deltaTime;
            float nearestWholeCount = math.round(output.EmissionAccumulator);
            int continuousCount = math.abs(output.EmissionAccumulator - nearestWholeCount)
                <= EmissionAccumulatorTolerance
                    ? math.max(0, (int)math.round(nearestWholeCount))
                    : math.max(0, (int)math.floor(output.EmissionAccumulator));
            if (continuousCount > 0)
            {
                output.EmissionAccumulator = math.max(
                    0.0f,
                    output.EmissionAccumulator - continuousCount);
                output.EmitCount = AddSaturated(output.EmitCount, continuousCount);
            }

            for (int burstIndex = 0; burstIndex < config.BurstCount; burstIndex++)
            {
                ulong burstBit = 1UL << burstIndex;
                if ((output.BurstTriggeredMask & burstBit) != 0UL)
                    continue;

                VividParticleNativeBurst burst = Bursts[config.BurstOffset + burstIndex];
                if (burst.Time < startTime || burst.Time > endTime)
                    continue;

                output.BurstTriggeredMask |= burstBit;
                output.EmitCount = AddSaturated(output.EmitCount, math.max(0, burst.Count));
            }
        }

        private static int AddSaturated(int left, int right)
        {
            long value = (long)left + right;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static uint NextRandomState(ref uint state)
        {
            uint value = state == 0u ? 1u : state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value == 0u ? 1u : value;
            return state;
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
        public NativeArray<int> MeshIndices;
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
            if (MeshIndices.IsCreated)
                MeshIndices[destinationIndex] = MeshIndices[sourceIndex];
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleEcsIntegratePagesJob : IVividEcsPageJob
    {
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

        public void Execute(VividEcsPageInfo page, int pageIndex)
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

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsIntegratePageWorksJob : IVividEcsPageJob
    {
        [ReadOnly]
        public NativeArray<VividParticleEcsIntegratePageWork> Works;

        public void Execute(VividEcsPageInfo page, int workIndex)
        {
            VividParticleEcsIntegratePageWork work = Works[workIndex];
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
            work.MeshIndices[destinationIndex] = work.MeshIndices[sourceIndex];
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
            ExecuteWork(Works[workIndex]);
        }

        internal static void ExecuteWork(VividParticleEcsInitializeParticlesWork work)
        {
            int count = math.max(0, work.Count);
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                int particleIndex = work.StartIndex + localIndex;
                if ((uint)particleIndex >= (uint)work.Capacity)
                    break;

                Random random = CreateRandom(
                    work.RandomSeed,
                    particleIndex,
                    work.RandomIndexOffset + localIndex);
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
                work.MeshIndices[particleIndex] = ResolveMeshIndex(work, particleIndex);
                work.KeepMask[particleIndex] = 1;
            }
        }

        private static int ResolveMeshIndex(
            VividParticleEcsInitializeParticlesWork work,
            int particleIndex)
        {
            int meshCount = math.max(1, work.MeshCount);
            if (meshCount <= 1)
                return 0;

            return particleIndex % meshCount;
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

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsBuildInitializePageWorksJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VividParticleEmissionPlanOutput> Plans;

        public NativeList<VividParticleEcsInitializeParticlesWork>.ParallelWriter PageWorks;

        public void Execute(int index)
        {
            VividParticleEmissionPlanOutput plan = Plans[index];
            if (plan.RequiresManagedFallback != 0 || plan.ReservedCount <= 0)
                return;

            VividParticleEcsInitializeParticlesWork source = plan.InitializeWork;
            int sourceStart = source.StartIndex;
            int cursor = sourceStart;
            int remaining = math.min(source.Count, math.max(0, source.Capacity - sourceStart));
            while (remaining > 0)
            {
                int pageRemaining = VividEcsConstants.PageEntryCount
                    - cursor % VividEcsConstants.PageEntryCount;
                int pageCount = math.min(remaining, pageRemaining);
                VividParticleEcsInitializeParticlesWork pageWork = source;
                pageWork.StartIndex = cursor;
                pageWork.Count = pageCount;
                pageWork.RandomIndexOffset = cursor - sourceStart;
                PageWorks.AddNoResize(pageWork);
                cursor += pageCount;
                remaining -= pageCount;
            }
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal unsafe struct VividParticleEcsInitializeParticlePagesJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<VividParticleEcsInitializeParticlesWork> PageWorks;

        public void Execute(int index)
        {
            VividParticleEcsInitializeParticlesJob.ExecuteWork(PageWorks[index]);
        }
    }
}
