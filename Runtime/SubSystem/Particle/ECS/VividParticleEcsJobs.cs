using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal unsafe struct VividParticleTriggerJobConfig
    {
        public int Enabled;
        public int ColliderCount;
        public int SelectedColliderCount;
        public int InsideAction;
        public int OutsideAction;
        public int EnterAction;
        public int ExitAction;
        public int ColliderQueryMode;
        public int SimulationSpace;
        public float RadiusScale;
        public int TriggerEventCapacity;
        public float4x4 ParticleLocalToWorld;

        [NativeDisableUnsafePtrRestriction]
        public int* SelectedColliderIndices;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeCollider* Colliders;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeTriggerEvent* TriggerEvents;

        [NativeDisableUnsafePtrRestriction]
        public int* TriggerEventCount;
    }

    internal unsafe struct VividParticleCollisionJobConfig
    {
        public int Enabled;
        public int PlaneCount;
        public int ColliderCount;
        public int MaxCollisionShapes;
        public int CollidesWith;
        public int EnableDynamicColliders;
        public int Quality;
        public int SimulationSpace;
        public float Dampen;
        public float Bounce;
        public float LifetimeLoss;
        public float MinKillSpeedSquared;
        public float MaxKillSpeedSquared;
        public float RadiusScale;
        public int SendCollisionEvents;
        public int CollisionEventCapacity;
        public float4x4 ParticleLocalToWorld;
        public float4x4 ParticleWorldToLocal;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeCollisionPlane* Planes;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeCollider* Colliders;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeCollisionEvent* CollisionEvents;

        [NativeDisableUnsafePtrRestriction]
        public int* CollisionEventCount;
    }

    internal unsafe struct VividParticleExternalForcesJobConfig
    {
        public int Enabled;
        public int InfluenceCount;
        public int ForceFieldCount;
        public int VectorFieldValueCount;
        public int WindZoneCount;
        public int SimulationSpace;
        public float TimeSinceLevelLoad;
        public float4x4 ParticleLocalToWorld;
        public float4x4 ParticleWorldToLocal;

        [NativeDisableUnsafePtrRestriction]
        public int* InfluenceIndices;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeForceField* ForceFields;

        [NativeDisableUnsafePtrRestriction]
        public float4* VectorFieldData;

        [NativeDisableUnsafePtrRestriction]
        public float* MultiplierLut;

        [NativeDisableUnsafePtrRestriction]
        public VividParticleNativeWindZone* WindZones;
    }

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
        public float3* AnimatedVelocities;

        [NativeDisableUnsafePtrRestriction]
        public float3* InitialEmitterVelocities;

        [NativeDisableUnsafePtrRestriction]
        public float* StartLifetimes;

        [NativeDisableUnsafePtrRestriction]
        public float3* VelocityOverLifetimeLut;

        public float3x3 VelocityOverLifetimeTransform;
        public int VelocityOverLifetimeEnabled;

        [NativeDisableUnsafePtrRestriction]
        public float3* LimitVelocityLut;

        [NativeDisableUnsafePtrRestriction]
        public float* LimitVelocityDragLut;

        public float3x3 LimitVelocityTransform;
        public int LimitVelocityEnabled;
        public int LimitVelocitySeparateAxes;
        public float LimitVelocityDampen;
        public int LimitVelocityMultiplyDragByParticleSize;
        public int LimitVelocityMultiplyDragByParticleVelocity;

        [NativeDisableUnsafePtrRestriction]
        public float* Sizes;

        [NativeDisableUnsafePtrRestriction]
        public float3* AccumulatedRotations;

        [NativeDisableUnsafePtrRestriction]
        public float3* RotationBySpeedLut;

        public float2 RotationBySpeedRange;
        public int RotationBySpeedEnabled;

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
        public float3* NoiseStrengthLut;

        [NativeDisableUnsafePtrRestriction]
        public float* NoiseScrollSpeedLut;

        [NativeDisableUnsafePtrRestriction]
        public float* NoisePositionAmountLut;

        [NativeDisableUnsafePtrRestriction]
        public float* NoiseRotationAmountLut;

        [NativeDisableUnsafePtrRestriction]
        public float* NoiseSizeAmountLut;

        [NativeDisableUnsafePtrRestriction]
        public float3* NoiseRemapLut;

        public float NoiseFrequency;
        public float NoiseOctaveMultiplier;
        public float NoiseOctaveScale;
        public int NoiseOctaveCount;
        public int NoiseDamping;
        public int NoiseEnabled;
        public int NoiseQuality;
        public int NoiseRemapEnabled;

        [NativeDisableUnsafePtrRestriction]
        public float* InheritVelocityLut;

        public float3 EmitterVelocity;
        public int InheritVelocityEnabled;
        public int InheritVelocityMode;

        public VividParticleExternalForcesJobConfig ExternalForces;

        public VividParticleCollisionJobConfig Collision;

        public VividParticleTriggerJobConfig Trigger;

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
        public int LifetimeByEmitterSpeedEnabled;
        public float LifetimeByEmitterSpeedMultiplier;
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
        public float3* AnimatedVelocities;

        [NativeDisableUnsafePtrRestriction]
        public float3* InitialEmitterVelocities;

        public float3 EmitterVelocity;

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
                    initializeWork.LifetimeByEmitterSpeedEnabled = config.LifetimeByEmitterSpeedEnabled;
                    initializeWork.LifetimeByEmitterSpeedMultiplier =
                        EvaluateLifetimeByEmitterSpeed(config, initializeWork.EmitterVelocity);
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

        private static float EvaluateLifetimeByEmitterSpeed(
            VividParticleNativeSimulationConfig config,
            float3 emitterVelocity)
        {
            if (config.LifetimeByEmitterSpeedEnabled == 0)
                return 1.0f;

            float minimumSpeed = config.LifetimeByEmitterSpeedRange.x;
            float maximumSpeed = config.LifetimeByEmitterSpeedRange.y;
            float speed = math.length(emitterVelocity);
            float normalizedSpeed = maximumSpeed > minimumSpeed
                ? math.saturate((speed - minimumSpeed) / (maximumSpeed - minimumSpeed))
                : 0.0f;
            float samplePosition = normalizedSpeed
                * (VividParticleNativeSimulationConfig.LifetimeByEmitterSpeedLutResolution - 1);
            int lowerIndex = (int)math.floor(samplePosition);
            int upperIndex = math.min(
                lowerIndex + 1,
                VividParticleNativeSimulationConfig.LifetimeByEmitterSpeedLutResolution - 1);
            float interpolation = samplePosition - lowerIndex;
            float* lut = config.LifetimeByEmitterSpeedLut;
            return math.max(0.0f, math.lerp(lut[lowerIndex], lut[upperIndex], interpolation));
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
            if ((work.VelocityOverLifetimeEnabled != 0
                    && work.AnimatedVelocities != null
                    && work.VelocityOverLifetimeLut != null)
                || work.LimitVelocityEnabled != 0
                || work.RotationBySpeedEnabled != 0
                || work.NoiseEnabled != 0
                || work.InheritVelocityEnabled != 0
                || work.ExternalForces.Enabled != 0
                || work.Collision.Enabled != 0
                || work.Trigger.Enabled != 0)
            {
                IntegrateModulePage(work, page.StartIndex, pageEnd);
                return;
            }

            IntegrateBasePage(work, page.StartIndex, pageEnd);
        }

        private static void IntegrateBasePage(
            VividParticleEcsIntegratePageWork work,
            int pageStart,
            int pageEnd)
        {
            for (int index = pageStart; index < pageEnd; index++)
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

        private static void IntegrateModulePage(
            VividParticleEcsIntegratePageWork work,
            int pageStart,
            int pageEnd)
        {
            for (int index = pageStart; index < pageEnd; index++)
            {
                float remainingLifetime = work.RemainingLifetimes[index] - work.DeltaTime;
                if (remainingLifetime <= 0.0f)
                {
                    work.KeepMask[index] = 0;
                    continue;
                }

                float3 velocity = work.Velocities[index] + work.Gravity * work.DeltaTime;
                float startLifetime = work.StartLifetimes[index];
                float normalizedLifetime = startLifetime > 0.0f
                    ? 1.0f - math.saturate(work.RemainingLifetimes[index] / startLifetime)
                    : 0.0f;
                float3 animatedVelocity = float3.zero;
                if (work.VelocityOverLifetimeEnabled != 0
                    && work.AnimatedVelocities != null
                    && work.VelocityOverLifetimeLut != null)
                {
                    animatedVelocity = math.mul(
                        work.VelocityOverLifetimeTransform,
                        SampleVelocityLut(work.VelocityOverLifetimeLut, normalizedLifetime));
                }

                if (work.InheritVelocityEnabled != 0
                    && work.InheritVelocityLut != null)
                {
                    float3 inheritedVelocity = work.InheritVelocityMode
                            == (int)VividParticleInheritVelocityMode.Current
                        ? work.EmitterVelocity
                        : work.InitialEmitterVelocities != null
                            ? work.InitialEmitterVelocities[index]
                            : float3.zero;
                    animatedVelocity += inheritedVelocity * SampleScalarLut(
                        work.InheritVelocityLut,
                        normalizedLifetime);
                }

                if (work.NoiseEnabled != 0
                    && work.NoisePhases != null
                    && work.NoiseStrengthLut != null
                    && work.NoiseScrollSpeedLut != null)
                {
                    float3 noiseValue = EvaluateNoiseValue(
                        work,
                        index,
                        normalizedLifetime);
                    if (work.NoisePositionAmountLut != null)
                    {
                        animatedVelocity += noiseValue * SampleScalarLut(
                            work.NoisePositionAmountLut,
                            normalizedLifetime);
                    }

                    if (work.NoiseRotationAmountLut != null
                        && work.AccumulatedRotations != null)
                    {
                        float rotationAmount = SampleScalarLut(
                            work.NoiseRotationAmountLut,
                            normalizedLifetime);
                        work.AccumulatedRotations[index] += noiseValue
                            * math.radians(rotationAmount)
                            * (0.5f * work.DeltaTime);
                    }

                    if (work.NoiseSizeMultipliers != null)
                    {
                        float sizeAmount = work.NoiseSizeAmountLut != null
                            ? SampleScalarLut(work.NoiseSizeAmountLut, normalizedLifetime)
                            : 0.0f;
                        work.NoiseSizeMultipliers[index] = math.max(
                            0.0f,
                            1.0f + noiseValue.x * sizeAmount * 0.5f);
                    }
                }

                if (work.AnimatedVelocities != null)
                    work.AnimatedVelocities[index] = animatedVelocity;

                if (work.LimitVelocityEnabled != 0
                    && work.LimitVelocityLut != null
                    && work.LimitVelocityDragLut != null)
                {
                    velocity = ApplyLimitVelocity(
                        work,
                        index,
                        normalizedLifetime,
                        velocity,
                        animatedVelocity);
                }

                if (work.RotationBySpeedEnabled != 0
                    && work.RotationBySpeedLut != null
                    && work.AccumulatedRotations != null)
                {
                    float3 totalVelocity = velocity + animatedVelocity;
                    float rangeLength = math.max(
                        0.000001f,
                        work.RotationBySpeedRange.y - work.RotationBySpeedRange.x);
                    float normalizedSpeed = math.saturate(
                        (math.length(totalVelocity) - work.RotationBySpeedRange.x) / rangeLength);
                    float3 angularVelocityDegrees =
                        SampleVelocityLut(work.RotationBySpeedLut, normalizedSpeed);
                    work.AccumulatedRotations[index] +=
                        math.radians(angularVelocityDegrees) * work.DeltaTime;
                }

                if (work.ExternalForces.Enabled != 0)
                {
                    velocity = ApplyExternalForces(
                        work,
                        index,
                        normalizedLifetime,
                        velocity,
                        animatedVelocity);
                }

                float3 nextPosition = work.Positions[index]
                    + (velocity + animatedVelocity) * work.DeltaTime;
                if (work.Collision.Enabled != 0)
                {
                    ResolveCollisions(
                        work,
                        index,
                        animatedVelocity,
                        ref velocity,
                        ref nextPosition,
                        ref remainingLifetime);
                }

                if (work.Trigger.Enabled != 0)
                {
                    ResolveTriggers(
                        work,
                        index,
                        nextPosition,
                        ref remainingLifetime);
                }

                work.Velocities[index] = velocity;
                work.Positions[index] = nextPosition;
                work.RemainingLifetimes[index] = remainingLifetime;
                work.KeepMask[index] = remainingLifetime > 0.0f ? (byte)1 : (byte)0;
            }
        }

        private static void ResolveTriggers(
            VividParticleEcsIntegratePageWork work,
            int particleIndex,
            float3 simulationPosition,
            ref float remainingLifetime)
        {
            VividParticleTriggerJobConfig config = work.Trigger;
            if (config.Colliders == null
                || config.SelectedColliderIndices == null
                || config.SelectedColliderCount <= 0
                || work.TriggerPreviousInside == null
                || work.TriggerCurrentInside == null
                || work.TriggerColliderEntityIds == null)
            {
                return;
            }

            float3 worldPosition = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.transform(config.ParticleLocalToWorld, simulationPosition)
                : simulationPosition;
            float particleSize = work.Sizes != null
                ? math.max(VividParticleMainModule.MinimumStartSize, work.Sizes[particleIndex])
                : 1.0f;
            float particleRadius = math.max(0.0f, particleSize * config.RadiusScale * 0.5f);
            bool isInside = false;
            ulong currentColliderId = 0UL;
            bool wasInside = work.TriggerPreviousInside[particleIndex] != 0;
            bool queryAll = config.ColliderQueryMode == (int)VividParticleColliderQueryMode.All;
            VividParticleTriggerEventType insideEventType = wasInside
                ? VividParticleTriggerEventType.Inside
                : VividParticleTriggerEventType.Enter;
            int insideAction = wasInside ? config.InsideAction : config.EnterAction;
            bool recordedAllInsideCallbacks = false;
            for (int selectedIndex = 0; selectedIndex < config.SelectedColliderCount; selectedIndex++)
            {
                int colliderIndex = config.SelectedColliderIndices[selectedIndex];
                if ((uint)colliderIndex >= (uint)config.ColliderCount)
                    continue;
                VividParticleNativeCollider collider = config.Colliders[colliderIndex];
                if (collider.Active == 0
                    || !TryResolvePrimitiveCollider(
                        collider,
                        worldPosition,
                        particleRadius,
                        out _,
                        out _))
                {
                    continue;
                }
                isInside = true;
                if (currentColliderId == 0UL)
                    currentColliderId = collider.EntityId;
                if (queryAll && insideAction == (int)VividParticleOverlapAction.Callback)
                {
                    RecordTriggerEvent(
                        config,
                        particleIndex,
                        collider.EntityId,
                        insideEventType);
                    recordedAllInsideCallbacks = true;
                }
                if (!queryAll)
                    break;
            }

            ulong previousColliderId = work.TriggerColliderEntityIds[particleIndex];
            work.TriggerCurrentInside[particleIndex] = isInside ? (byte)1 : (byte)0;
            work.TriggerPreviousInside[particleIndex] = isInside ? (byte)1 : (byte)0;
            work.TriggerColliderEntityIds[particleIndex] = isInside ? currentColliderId : 0UL;

            VividParticleTriggerEventType eventType;
            int action;
            ulong eventColliderId;
            if (isInside)
            {
                bool enters = !wasInside;
                eventType = enters
                    ? VividParticleTriggerEventType.Enter
                    : VividParticleTriggerEventType.Inside;
                action = enters ? config.EnterAction : config.InsideAction;
                eventColliderId = currentColliderId;
            }
            else
            {
                bool exits = wasInside;
                eventType = exits
                    ? VividParticleTriggerEventType.Exit
                    : VividParticleTriggerEventType.Outside;
                action = exits ? config.ExitAction : config.OutsideAction;
                eventColliderId = exits ? previousColliderId : 0UL;
            }

            if (action == (int)VividParticleOverlapAction.Kill)
            {
                remainingLifetime = 0.0f;
                return;
            }
            if (action == (int)VividParticleOverlapAction.Callback
                && !(isInside && recordedAllInsideCallbacks))
            {
                RecordTriggerEvent(config, particleIndex, eventColliderId, eventType);
            }
        }

        private static void RecordTriggerEvent(
            VividParticleTriggerJobConfig config,
            int particleIndex,
            ulong colliderEntityId,
            VividParticleTriggerEventType eventType)
        {
            if (config.TriggerEvents == null
                || config.TriggerEventCount == null
                || config.TriggerEventCapacity <= 0)
            {
                return;
            }
            int eventIndex = Interlocked.Increment(ref config.TriggerEventCount[0]) - 1;
            if ((uint)eventIndex >= (uint)config.TriggerEventCapacity)
                return;
            config.TriggerEvents[eventIndex] = new VividParticleNativeTriggerEvent
            {
                ColliderEntityId = config.ColliderQueryMode == (int)VividParticleColliderQueryMode.Disabled
                    ? 0UL
                    : colliderEntityId,
                ParticleIndex = particleIndex,
                EventType = (int)eventType,
            };
        }

        private static void ResolveCollisions(
            VividParticleEcsIntegratePageWork work,
            int particleIndex,
            float3 animatedVelocity,
            ref float3 velocity,
            ref float3 nextPosition,
            ref float remainingLifetime)
        {
            VividParticleCollisionJobConfig config = work.Collision;
            if ((config.Planes == null || config.PlaneCount <= 0)
                && (config.Colliders == null || config.ColliderCount <= 0))
                return;

            float3 currentPosition = work.Positions[particleIndex];
            float3 totalVelocity = velocity + animatedVelocity;
            float3 worldCurrent = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.transform(config.ParticleLocalToWorld, currentPosition)
                : currentPosition;
            float3 worldNext = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.transform(config.ParticleLocalToWorld, nextPosition)
                : nextPosition;
            float3 worldVelocity = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.mul((float3x3)config.ParticleLocalToWorld, totalVelocity)
                : totalVelocity;
            float particleSize = work.Sizes != null
                ? math.max(VividParticleMainModule.MinimumStartSize, work.Sizes[particleIndex])
                : 1.0f;
            float radius = math.max(0.0f, particleSize * config.RadiusScale * 0.5f);
            bool collided = false;
            bool highQuality = config.Quality == (int)VividParticleCollisionQuality.High;

            if (config.Planes != null)
            {
                for (int planeIndex = 0; planeIndex < config.PlaneCount; planeIndex++)
                {
                    VividParticleNativeCollisionPlane plane = config.Planes[planeIndex];
                    float3 normal = math.normalizesafe(plane.Normal, new float3(0.0f, 1.0f, 0.0f));
                    float previousDistance = math.dot(worldCurrent - plane.Position, normal);
                    float nextDistance = math.dot(worldNext - plane.Position, normal);
                    float normalSpeed = math.dot(worldVelocity, normal);
                    if (nextDistance >= radius || (previousDistance < radius && normalSpeed >= 0.0f))
                        continue;

                    if (ShouldKillCollision(config, worldVelocity))
                    {
                        remainingLifetime = 0.0f;
                        return;
                    }

                    worldNext += normal * (radius - nextDistance);
                    float3 collisionPoint = worldNext;
                    ResolveCollisionVelocity(config, normal, ref worldVelocity);
                    RecordCollisionEvent(
                        config,
                        particleIndex,
                        plane.EntityId,
                        collisionPoint,
                        normal,
                        worldVelocity);
                    collided = true;
                }
            }

            if (config.Colliders != null)
            {
                int resolvedColliderCount = 0;
                for (int colliderIndex = 0; colliderIndex < config.ColliderCount; colliderIndex++)
                {
                    VividParticleNativeCollider collider = config.Colliders[colliderIndex];
                    if (collider.Active == 0
                        || collider.IsTrigger != 0
                        || (config.EnableDynamicColliders == 0 && collider.IsDynamic != 0)
                        || (config.CollidesWith & (1 << collider.Layer)) == 0)
                    {
                        continue;
                    }

                    bool overlapsAtEnd = TryResolvePrimitiveCollider(
                        collider,
                        worldNext,
                        radius,
                        out float3 normal,
                        out float penetration);
                    float hitTime = 0.0f;
                    bool swept = !overlapsAtEnd
                        && highQuality
                        && TrySweepPrimitiveCollider(
                            collider,
                            worldCurrent,
                            worldNext,
                            radius,
                            out hitTime,
                            out normal);
                    if (!overlapsAtEnd && !swept)
                    {
                        continue;
                    }

                    if (ShouldKillCollision(config, worldVelocity))
                    {
                        remainingLifetime = 0.0f;
                        return;
                    }

                    float3 collisionPoint;
                    if (swept)
                    {
                        collisionPoint = math.lerp(worldCurrent, worldNext, hitTime);
                    }
                    else
                    {
                        worldNext += normal * penetration;
                        collisionPoint = worldNext;
                    }
                    ResolveCollisionVelocity(config, normal, ref worldVelocity);
                    if (swept)
                    {
                        worldNext = collisionPoint
                            + worldVelocity * work.DeltaTime * (1.0f - hitTime);
                        worldCurrent = collisionPoint;
                    }
                    RecordCollisionEvent(
                        config,
                        particleIndex,
                        collider.EntityId,
                        collisionPoint,
                        normal,
                        worldVelocity);
                    collided = true;
                    resolvedColliderCount++;
                    if (resolvedColliderCount >= math.max(1, config.MaxCollisionShapes))
                        break;
                }
            }

            if (!collided)
                return;

            if (config.LifetimeLoss > 0.0f && work.StartLifetimes != null)
            {
                remainingLifetime -= work.StartLifetimes[particleIndex]
                    * math.saturate(config.LifetimeLoss);
            }

            if (config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local)
            {
                nextPosition = math.transform(config.ParticleWorldToLocal, worldNext);
                float3 resolvedTotalVelocity = math.mul(
                    (float3x3)config.ParticleWorldToLocal,
                    worldVelocity);
                velocity = resolvedTotalVelocity - animatedVelocity;
            }
            else
            {
                nextPosition = worldNext;
                velocity = worldVelocity - animatedVelocity;
            }
        }

        private static bool ShouldKillCollision(
            VividParticleCollisionJobConfig config,
            float3 velocity)
        {
            float speedSquared = math.lengthsq(velocity);
            return speedSquared < config.MinKillSpeedSquared
                || speedSquared > config.MaxKillSpeedSquared;
        }

        private static void RecordCollisionEvent(
            VividParticleCollisionJobConfig config,
            int particleIndex,
            ulong colliderEntityId,
            float3 intersection,
            float3 normal,
            float3 velocity)
        {
            if (config.SendCollisionEvents == 0
                || config.CollisionEvents == null
                || config.CollisionEventCount == null
                || config.CollisionEventCapacity <= 0)
            {
                return;
            }

            int eventIndex = Interlocked.Increment(ref config.CollisionEventCount[0]) - 1;
            if ((uint)eventIndex >= (uint)config.CollisionEventCapacity)
                return;
            config.CollisionEvents[eventIndex] = new VividParticleNativeCollisionEvent
            {
                ColliderEntityId = colliderEntityId,
                ParticleIndex = particleIndex,
                Intersection = intersection,
                Normal = normal,
                Velocity = velocity,
            };
        }

        private static void ResolveCollisionVelocity(
            VividParticleCollisionJobConfig config,
            float3 normal,
            ref float3 velocity)
        {
            float normalSpeed = math.dot(velocity, normal);
            if (normalSpeed >= 0.0f)
                return;
            float3 normalVelocity = normal * normalSpeed;
            float3 tangentVelocity = velocity - normalVelocity;
            velocity = tangentVelocity * (1.0f - math.saturate(config.Dampen))
                - normalVelocity * math.max(0.0f, config.Bounce);
        }

        private static bool TrySweepPrimitiveCollider(
            VividParticleNativeCollider collider,
            float3 start,
            float3 end,
            float particleRadius,
            out float hitTime,
            out float3 normal)
        {
            switch ((VividParticleNativeColliderShape)collider.Shape)
            {
                case VividParticleNativeColliderShape.Sphere:
                    return TrySweepSphere(
                        start,
                        end,
                        collider.Center,
                        collider.Radius + particleRadius,
                        out hitTime,
                        out normal);
                case VividParticleNativeColliderShape.Box:
                    return TrySweepBox(
                        start,
                        end,
                        collider,
                        particleRadius,
                        out hitTime,
                        out normal);
                case VividParticleNativeColliderShape.Capsule:
                    return TrySweepCapsule(
                        start,
                        end,
                        collider.SegmentA,
                        collider.SegmentB,
                        collider.Radius + particleRadius,
                        out hitTime,
                        out normal);
                default:
                    hitTime = 0.0f;
                    normal = default;
                    return false;
            }
        }

        private static bool TrySweepSphere(
            float3 start,
            float3 end,
            float3 center,
            float radius,
            out float hitTime,
            out float3 normal)
        {
            float3 direction = end - start;
            float directionLengthSquared = math.lengthsq(direction);
            float3 offset = start - center;
            float c = math.lengthsq(offset) - radius * radius;
            if (c <= 0.0f || directionLengthSquared <= 0.0000001f)
            {
                hitTime = 0.0f;
                normal = default;
                return false;
            }
            float b = math.dot(offset, direction);
            float discriminant = b * b - directionLengthSquared * c;
            if (discriminant < 0.0f)
            {
                hitTime = 0.0f;
                normal = default;
                return false;
            }
            float t = (-b - math.sqrt(discriminant)) / directionLengthSquared;
            if (t < 0.0f || t > 1.0f)
            {
                hitTime = 0.0f;
                normal = default;
                return false;
            }
            float3 hitPoint = math.lerp(start, end, t);
            hitTime = t;
            normal = math.normalizesafe(hitPoint - center, new float3(0.0f, 1.0f, 0.0f));
            return true;
        }

        private static bool TrySweepBox(
            float3 start,
            float3 end,
            VividParticleNativeCollider collider,
            float particleRadius,
            out float hitTime,
            out float3 normal)
        {
            float3 startOffset = start - collider.Center;
            float3 direction = end - start;
            float3 localStart = new(
                math.dot(startOffset, collider.AxisX),
                math.dot(startOffset, collider.AxisY),
                math.dot(startOffset, collider.AxisZ));
            float3 localDirection = new(
                math.dot(direction, collider.AxisX),
                math.dot(direction, collider.AxisY),
                math.dot(direction, collider.AxisZ));
            float3 extents = math.max(float3.zero, collider.HalfExtents) + particleRadius;
            if (math.all(math.abs(localStart) <= extents))
            {
                hitTime = 0.0f;
                normal = default;
                return false;
            }

            float tMin = 0.0f;
            float tMax = 1.0f;
            int hitAxis = -1;
            float hitSign = 0.0f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = localStart[axis];
                float delta = localDirection[axis];
                float extent = extents[axis];
                if (math.abs(delta) <= 0.000001f)
                {
                    if (origin < -extent || origin > extent)
                    {
                        hitTime = 0.0f;
                        normal = default;
                        return false;
                    }
                    continue;
                }
                float inverse = 1.0f / delta;
                float first = (-extent - origin) * inverse;
                float second = (extent - origin) * inverse;
                float near = math.min(first, second);
                float far = math.max(first, second);
                if (near > tMin)
                {
                    tMin = near;
                    hitAxis = axis;
                    hitSign = first < second ? -1.0f : 1.0f;
                }
                tMax = math.min(tMax, far);
                if (tMin > tMax)
                {
                    hitTime = 0.0f;
                    normal = default;
                    return false;
                }
            }
            if (hitAxis < 0 || tMin < 0.0f || tMin > 1.0f)
            {
                hitTime = 0.0f;
                normal = default;
                return false;
            }
            hitTime = tMin;
            normal = hitAxis switch
            {
                0 => collider.AxisX * hitSign,
                1 => collider.AxisY * hitSign,
                _ => collider.AxisZ * hitSign,
            };
            return true;
        }

        private static bool TrySweepCapsule(
            float3 start,
            float3 end,
            float3 segmentA,
            float3 segmentB,
            float radius,
            out float hitTime,
            out float3 normal)
        {
            float3 direction = end - start;
            float3 segment = segmentB - segmentA;
            float3 origin = start - segmentA;
            float segmentLengthSquared = math.lengthsq(segment);
            float directionLengthSquared = math.lengthsq(direction);
            float segmentDirection = math.dot(segment, direction);
            float segmentOrigin = math.dot(segment, origin);
            float directionOrigin = math.dot(direction, origin);
            float originLengthSquared = math.lengthsq(origin);
            float a = segmentLengthSquared * directionLengthSquared
                - segmentDirection * segmentDirection;
            float b = segmentLengthSquared * directionOrigin
                - segmentOrigin * segmentDirection;
            float c = segmentLengthSquared * originLengthSquared
                - segmentOrigin * segmentOrigin
                - radius * radius * segmentLengthSquared;

            bool found = false;
            float bestTime = 2.0f;
            float3 bestNormal = default;
            float discriminant = b * b - a * c;
            if (math.abs(a) > 0.0000001f && discriminant >= 0.0f)
            {
                float t = (-b - math.sqrt(discriminant)) / a;
                float y = segmentOrigin + t * segmentDirection;
                if (t >= 0.0f && t <= 1.0f && y > 0.0f && y < segmentLengthSquared)
                {
                    float3 hitPoint = math.lerp(start, end, t);
                    float3 closest = segmentA + segment * (y / segmentLengthSquared);
                    bestTime = t;
                    bestNormal = math.normalizesafe(
                        hitPoint - closest,
                        new float3(0.0f, 1.0f, 0.0f));
                    found = true;
                }
            }

            if (TrySweepSphere(start, end, segmentA, radius, out float capATime, out float3 capANormal)
                && capATime < bestTime)
            {
                bestTime = capATime;
                bestNormal = capANormal;
                found = true;
            }
            if (TrySweepSphere(start, end, segmentB, radius, out float capBTime, out float3 capBNormal)
                && capBTime < bestTime)
            {
                bestTime = capBTime;
                bestNormal = capBNormal;
                found = true;
            }
            hitTime = found ? bestTime : 0.0f;
            normal = bestNormal;
            return found;
        }

        private static bool TryResolvePrimitiveCollider(
            VividParticleNativeCollider collider,
            float3 position,
            float particleRadius,
            out float3 normal,
            out float penetration)
        {
            switch ((VividParticleNativeColliderShape)collider.Shape)
            {
                case VividParticleNativeColliderShape.Sphere:
                    return TryResolveSphere(
                        position,
                        particleRadius,
                        collider.Center,
                        collider.Radius,
                        out normal,
                        out penetration);
                case VividParticleNativeColliderShape.Box:
                    return TryResolveBox(
                        position,
                        particleRadius,
                        collider,
                        out normal,
                        out penetration);
                case VividParticleNativeColliderShape.Capsule:
                    float3 segment = collider.SegmentB - collider.SegmentA;
                    float segmentLengthSquared = math.lengthsq(segment);
                    float t = segmentLengthSquared > 0.0000001f
                        ? math.saturate(math.dot(position - collider.SegmentA, segment) / segmentLengthSquared)
                        : 0.0f;
                    float3 closest = collider.SegmentA + segment * t;
                    return TryResolveSphere(
                        position,
                        particleRadius,
                        closest,
                        collider.Radius,
                        out normal,
                        out penetration);
                default:
                    normal = new float3(0.0f, 1.0f, 0.0f);
                    penetration = 0.0f;
                    return false;
            }
        }

        private static bool TryResolveSphere(
            float3 position,
            float particleRadius,
            float3 center,
            float colliderRadius,
            out float3 normal,
            out float penetration)
        {
            float3 delta = position - center;
            float distanceSquared = math.lengthsq(delta);
            float combinedRadius = math.max(0.0f, particleRadius + colliderRadius);
            if (distanceSquared >= combinedRadius * combinedRadius)
            {
                normal = default;
                penetration = 0.0f;
                return false;
            }
            float distance = math.sqrt(distanceSquared);
            normal = distance > 0.000001f
                ? delta / distance
                : new float3(0.0f, 1.0f, 0.0f);
            penetration = combinedRadius - distance;
            return penetration > 0.0f;
        }

        private static bool TryResolveBox(
            float3 position,
            float particleRadius,
            VividParticleNativeCollider collider,
            out float3 normal,
            out float penetration)
        {
            float3 delta = position - collider.Center;
            float3 local = new(
                math.dot(delta, collider.AxisX),
                math.dot(delta, collider.AxisY),
                math.dot(delta, collider.AxisZ));
            float3 halfExtents = math.max(float3.zero, collider.HalfExtents);
            float3 closestLocal = math.clamp(local, -halfExtents, halfExtents);
            float3 closest = collider.Center
                + collider.AxisX * closestLocal.x
                + collider.AxisY * closestLocal.y
                + collider.AxisZ * closestLocal.z;
            float3 outsideDelta = position - closest;
            float outsideDistanceSquared = math.lengthsq(outsideDelta);
            if (outsideDistanceSquared > 0.0000001f)
            {
                float outsideDistance = math.sqrt(outsideDistanceSquared);
                if (outsideDistance >= particleRadius)
                {
                    normal = default;
                    penetration = 0.0f;
                    return false;
                }
                normal = outsideDelta / outsideDistance;
                penetration = particleRadius - outsideDistance;
                return true;
            }

            float3 faceDistances = halfExtents - math.abs(local);
            if (faceDistances.x <= faceDistances.y && faceDistances.x <= faceDistances.z)
            {
                normal = collider.AxisX * (local.x >= 0.0f ? 1.0f : -1.0f);
                penetration = particleRadius + faceDistances.x;
            }
            else if (faceDistances.y <= faceDistances.z)
            {
                normal = collider.AxisY * (local.y >= 0.0f ? 1.0f : -1.0f);
                penetration = particleRadius + faceDistances.y;
            }
            else
            {
                normal = collider.AxisZ * (local.z >= 0.0f ? 1.0f : -1.0f);
                penetration = particleRadius + faceDistances.z;
            }
            return penetration > 0.0f;
        }

        private static float3 ApplyExternalForces(
            VividParticleEcsIntegratePageWork work,
            int particleIndex,
            float normalizedLifetime,
            float3 velocity,
            float3 animatedVelocity)
        {
            VividParticleExternalForcesJobConfig config = work.ExternalForces;
            float multiplier = config.MultiplierLut != null
                ? SampleScalarLut(config.MultiplierLut, normalizedLifetime)
                : 1.0f;
            if (math.abs(multiplier) <= 0.000001f)
                return velocity;

            float3 simulationPosition = work.Positions[particleIndex];
            float3 worldPosition = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.transform(config.ParticleLocalToWorld, simulationPosition)
                : simulationPosition;
            float particleSize = work.Sizes != null
                ? math.max(VividParticleMainModule.MinimumStartSize, work.Sizes[particleIndex])
                : 1.0f;

            velocity = ApplyWindZones(
                config,
                worldPosition,
                velocity,
                multiplier,
                work.DeltaTime);

            if (config.ForceFields == null
                || config.InfluenceIndices == null
                || config.InfluenceCount <= 0)
            {
                return velocity;
            }

            for (int influenceIndex = 0; influenceIndex < config.InfluenceCount; influenceIndex++)
            {
                int fieldIndex = config.InfluenceIndices[influenceIndex];
                if ((uint)fieldIndex >= (uint)config.ForceFieldCount)
                    continue;
                VividParticleNativeForceField* field = config.ForceFields + fieldIndex;
                if (field->Active == 0 || field->EndRange <= 0.0f)
                    continue;

                float3 localPosition = math.transform(field->WorldToLocal, worldPosition);
                if (!TryGetNormalizedFieldDistance(field, localPosition, out float distance, out float t))
                    continue;

                float3 localDirection = new float3(
                    SampleForceFieldLut(field->DirectionXLut, t),
                    SampleForceFieldLut(field->DirectionYLut, t),
                    SampleForceFieldLut(field->DirectionZLut, t));
                if (math.lengthsq(localDirection) > 0.0000001f)
                {
                    velocity += TransformFieldDirection(config, field, localDirection, preserveMagnitude: true)
                        * multiplier
                        * work.DeltaTime;
                }

                float gravity = SampleForceFieldLut(field->GravityLut, t);
                if (math.abs(gravity) > 0.000001f)
                {
                    float3 radial = math.normalizesafe(localPosition);
                    float focusDistance = math.lerp(
                        field->StartRange,
                        field->EndRange,
                        math.saturate(field->GravityFocus));
                    if (distance < focusDistance)
                        radial = -radial;
                    float3 gravityDirection = TransformFieldDirection(
                        config,
                        field,
                        -radial,
                        preserveMagnitude: false);
                    velocity += gravityDirection
                        * gravity
                        * multiplier
                        * work.DeltaTime
                        * 30.0f;
                }

                float rotationSpeed = SampleForceFieldLut(field->RotationSpeedLut, t);
                float rotationAttraction = SampleForceFieldLut(field->RotationAttractionLut, t);
                if (math.abs(rotationSpeed) > 0.000001f
                    && math.abs(rotationAttraction) > 0.000001f)
                {
                    float3 tangent = new float3(localPosition.z, 0.0f, -localPosition.x);
                    tangent = ApplyRotationRandomness(field, tangent, particleIndex);
                    tangent = TransformFieldDirection(config, field, tangent, preserveMagnitude: false)
                        * rotationSpeed
                        * multiplier;
                    velocity += (tangent - velocity)
                        * rotationAttraction
                        * multiplier
                        * work.DeltaTime
                        * 30.0f;
                }

                if (field->VectorFieldOffset >= 0
                    && config.VectorFieldData != null
                    && field->VectorFieldWidth > 0
                    && field->VectorFieldHeight > 0
                    && field->VectorFieldDepth > 0)
                {
                    float attraction = SampleForceFieldLut(field->VectorFieldAttractionLut, t);
                    if (math.abs(attraction) > 0.000001f)
                    {
                        float3 vectorVelocity = SampleVectorField(config, field, localPosition)
                            * SampleForceFieldLut(field->VectorFieldSpeedLut, t);
                        vectorVelocity = TransformFieldDirection(
                            config,
                            field,
                            vectorVelocity,
                            preserveMagnitude: true);
                        velocity = math.lerp(
                            velocity,
                            vectorVelocity,
                            math.saturate(attraction * multiplier * work.DeltaTime * 30.0f));
                    }
                }

                float drag = SampleForceFieldLut(field->DragLut, t);
                if (drag > 0.0f)
                {
                    float3 totalVelocity = velocity + animatedVelocity;
                    float speedSquared = math.lengthsq(totalVelocity);
                    float dragAmount = drag;
                    if (field->MultiplyDragByParticleSize != 0)
                    {
                        float radius = particleSize * 0.5f;
                        dragAmount *= math.PI * radius * radius;
                    }
                    if (field->MultiplyDragByParticleVelocity != 0)
                        dragAmount *= speedSquared;
                    float speed = math.sqrt(speedSquared);
                    float reducedSpeed = math.max(
                        0.0f,
                        speed - dragAmount * work.DeltaTime * multiplier);
                    totalVelocity = speed > 0.000001f
                        ? totalVelocity * (reducedSpeed / speed)
                        : float3.zero;
                    velocity = totalVelocity - animatedVelocity;
                }
            }

            return velocity;
        }

        private static float3 ApplyWindZones(
            VividParticleExternalForcesJobConfig config,
            float3 worldPosition,
            float3 velocity,
            float multiplier,
            float deltaTime)
        {
            if (config.WindZones == null || config.WindZoneCount <= 0)
                return velocity;

            for (int zoneIndex = 0; zoneIndex < config.WindZoneCount; zoneIndex++)
            {
                VividParticleNativeWindZone zone = config.WindZones[zoneIndex];
                if (zone.Active == 0)
                    continue;
                float phase = config.TimeSinceLevelLoad * math.PI * zone.PulseFrequency;
                float pulse = (
                        math.cos(phase)
                        + math.cos(phase * 0.375f)
                        + math.cos(phase * 0.05f))
                    * (1.0f / 3.0f);
                float strength = zone.WindMain * (1.0f + pulse * zone.PulseMagnitude);
                if (zone.Mode == (int)UnityEngine.WindZoneMode.Directional)
                {
                    float3 windDirection = ResolveWorldDirection(config, zone.Forward);
                    velocity += windDirection * strength * multiplier * deltaTime;
                    continue;
                }

                if (zone.Radius <= 0.0f)
                    continue;
                float3 offset = worldPosition - zone.Position;
                float distanceSquared = math.lengthsq(offset);
                float radiusSquared = zone.Radius * zone.Radius;
                if (distanceSquared > radiusSquared)
                    continue;
                float distance = math.sqrt(distanceSquared);
                float attenuation = 1.0f - math.saturate(distance / zone.Radius);
                attenuation = 1.0f - (1.0f - attenuation) * (1.0f - attenuation);
                float3 sphericalDirection = ResolveWorldDirection(config, math.normalizesafe(offset));
                velocity += sphericalDirection * attenuation * strength * multiplier * deltaTime;
            }
            return velocity;
        }

        private static float3 ResolveWorldDirection(
            VividParticleExternalForcesJobConfig config,
            float3 worldDirection)
        {
            float3 direction = config.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local
                ? math.mul((float3x3)config.ParticleWorldToLocal, worldDirection)
                : worldDirection;
            return math.normalizesafe(direction);
        }

        private static bool TryGetNormalizedFieldDistance(
            VividParticleNativeForceField* field,
            float3 localPosition,
            out float distance,
            out float normalizedDistance)
        {
            distance = field->Shape switch
            {
                (int)VividParticleForceFieldShape.Box => math.cmax(math.abs(localPosition)),
                (int)VividParticleForceFieldShape.Cylinder => math.length(localPosition.xz),
                _ => math.length(localPosition),
            };
            if (field->Shape == (int)VividParticleForceFieldShape.Hemisphere
                && localPosition.y < 0.0f)
            {
                normalizedDistance = 0.0f;
                return false;
            }
            if (field->Shape == (int)VividParticleForceFieldShape.Cylinder
                && math.abs(localPosition.y) > field->Length * 0.5f)
            {
                normalizedDistance = 0.0f;
                return false;
            }
            if (distance < field->StartRange || distance > field->EndRange)
            {
                normalizedDistance = 0.0f;
                return false;
            }
            normalizedDistance = math.saturate(
                (distance - field->StartRange)
                / math.max(0.000001f, field->EndRange - field->StartRange));
            return true;
        }

        private static float3 TransformFieldDirection(
            VividParticleExternalForcesJobConfig config,
            VividParticleNativeForceField* field,
            float3 localDirection,
            bool preserveMagnitude)
        {
            float magnitude = math.length(localDirection);
            float3 worldDirection = math.mul((float3x3)field->LocalToWorld, localDirection);
            float3 simulationDirection = config.SimulationSpace
                    == (int)VividParticleSystemSimulationSpace.Local
                ? math.mul((float3x3)config.ParticleWorldToLocal, worldDirection)
                : worldDirection;
            if (!preserveMagnitude)
                return math.normalizesafe(simulationDirection);
            return math.normalizesafe(simulationDirection) * magnitude;
        }

        private static float3 ApplyRotationRandomness(
            VividParticleNativeForceField* field,
            float3 tangent,
            int particleIndex)
        {
            if (math.cmax(field->RotationRandomness) <= 0.000001f)
                return tangent;
            uint seed = math.hash(new uint3(
                (uint)particleIndex,
                (uint)(field->EntityId & uint.MaxValue),
                (uint)(field->EntityId >> 32)));
            var random = Unity.Mathematics.Random.CreateFromIndex(seed);
            float x = (random.NextFloat() - 0.5f) * math.PI * 2.0f * field->RotationRandomness.x;
            float z = (random.NextFloat() - 0.5f) * math.PI * 2.0f * field->RotationRandomness.y;
            return math.mul(float3x3.EulerXYZ(new float3(x, 0.0f, z)), tangent);
        }

        private static float3 SampleVectorField(
            VividParticleExternalForcesJobConfig config,
            VividParticleNativeForceField* field,
            float3 localPosition)
        {
            float3 uvw = localPosition / math.max(0.000001f, field->EndRange) * 0.5f + 0.5f;
            int x = math.clamp((int)(uvw.x * field->VectorFieldWidth), 0, field->VectorFieldWidth - 1);
            int y = math.clamp((int)(uvw.y * field->VectorFieldHeight), 0, field->VectorFieldHeight - 1);
            int z = math.clamp((int)(uvw.z * field->VectorFieldDepth), 0, field->VectorFieldDepth - 1);
            int index = field->VectorFieldOffset
                + x
                + y * field->VectorFieldWidth
                + z * field->VectorFieldWidth * field->VectorFieldHeight;
            if ((uint)index >= (uint)config.VectorFieldValueCount)
                return float3.zero;
            return config.VectorFieldData[index].xyz;
        }

        private static float SampleForceFieldLut(float* lut, float normalizedDistance)
        {
            float sample = math.saturate(normalizedDistance)
                * (VividParticleNativeForceField.LutResolution - 1);
            int lower = (int)math.floor(sample);
            int upper = math.min(lower + 1, VividParticleNativeForceField.LutResolution - 1);
            return math.lerp(lut[lower], lut[upper], sample - lower);
        }

        private static float3 SampleVelocityLut(float3* lut, float normalizedLifetime)
        {
            float sample = math.saturate(normalizedLifetime)
                * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            int lower = (int)math.floor(sample);
            int upper = math.min(
                lower + 1,
                VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            return math.lerp(lut[lower], lut[upper], sample - lower);
        }

        private static float SampleScalarLut(float* lut, float normalizedLifetime)
        {
            float sample = math.saturate(normalizedLifetime)
                * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            int lower = (int)math.floor(sample);
            int upper = math.min(
                lower + 1,
                VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            return math.lerp(lut[lower], lut[upper], sample - lower);
        }

        private static float3 EvaluateNoiseValue(
            VividParticleEcsIntegratePageWork work,
            int particleIndex,
            float normalizedLifetime)
        {
            float scrollSpeed = SampleScalarLut(
                work.NoiseScrollSpeedLut,
                normalizedLifetime);
            float3 phase = work.NoisePhases[particleIndex]
                + new float3(1.0f, 1.371f, 1.913f) * scrollSpeed * work.DeltaTime;
            work.NoisePhases[particleIndex] = phase;

            float frequency = math.max(0.000001f, work.NoiseFrequency);
            float3 samplePosition = phase + work.Positions[particleIndex] * frequency;
            float amplitude = 1.0f;
            float octaveScale = 1.0f;
            float amplitudeSum = 0.0f;
            float3 value = float3.zero;
            int octaveCount = math.clamp(work.NoiseOctaveCount, 1, 4);
            for (int octave = 0; octave < octaveCount; octave++)
            {
                float3 octavePosition = samplePosition * octaveScale;
                value += new float3(
                    SampleNoise(work.NoiseQuality, octavePosition, new float3(19.19f, 3.17f, 7.13f)),
                    SampleNoise(work.NoiseQuality, octavePosition, new float3(5.71f, 23.23f, 11.11f)),
                    SampleNoise(work.NoiseQuality, octavePosition, new float3(13.37f, 17.17f, 29.29f))) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= math.saturate(work.NoiseOctaveMultiplier);
                octaveScale *= math.max(1.0f, work.NoiseOctaveScale);
            }

            value /= math.max(0.000001f, amplitudeSum);
            if (work.NoiseRemapEnabled != 0 && work.NoiseRemapLut != null)
            {
                float3 normalizedValue = math.saturate(value * 0.5f + 0.5f);
                value = new float3(
                    SampleRemapLut(work.NoiseRemapLut, normalizedValue.x, 0),
                    SampleRemapLut(work.NoiseRemapLut, normalizedValue.y, 1),
                    SampleRemapLut(work.NoiseRemapLut, normalizedValue.z, 2));
            }
            float3 strength = SampleVelocityLut(
                work.NoiseStrengthLut,
                normalizedLifetime);
            float dampingScale = work.NoiseDamping != 0 ? math.rcp(frequency) : 1.0f;
            return value * strength * dampingScale;
        }

        private static float SampleNoise(int quality, float3 position, float3 offset)
        {
            if (quality == (int)VividParticleNoiseQuality.Low)
                return noise.snoise(new float2(position.x + offset.x, offset.y));

            if (quality == (int)VividParticleNoiseQuality.Medium)
            {
                return noise.snoise(new float2(
                    position.x + offset.x,
                    position.y + offset.y));
            }

            return noise.snoise(position + offset);
        }

        private static float SampleRemapLut(float3* lut, float normalizedValue, int axis)
        {
            float sample = math.saturate(normalizedValue)
                * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            int lower = (int)math.floor(sample);
            int upper = math.min(
                lower + 1,
                VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
            float lowerValue = lut[lower][axis];
            float upperValue = lut[upper][axis];
            return math.lerp(lowerValue, upperValue, sample - lower);
        }

        private static float3 ApplyLimitVelocity(
            VividParticleEcsIntegratePageWork work,
            int particleIndex,
            float normalizedLifetime,
            float3 baseVelocity,
            float3 animatedVelocity)
        {
            float3 totalVelocity = baseVelocity + animatedVelocity;
            float3 moduleVelocity = math.mul(work.LimitVelocityTransform, totalVelocity);
            float3 limit = math.max(
                float3.zero,
                SampleVelocityLut(work.LimitVelocityLut, normalizedLifetime));
            float3 targetVelocity;
            if (work.LimitVelocitySeparateAxes != 0)
            {
                targetVelocity = math.clamp(moduleVelocity, -limit, limit);
            }
            else
            {
                float speed = math.length(moduleVelocity);
                float magnitudeLimit = limit.x;
                targetVelocity = speed > magnitudeLimit && speed > 0.000001f
                    ? moduleVelocity * (magnitudeLimit / speed)
                    : moduleVelocity;
            }

            float dampen = math.saturate(work.LimitVelocityDampen);
            float dampenFactor = dampen <= 0.0f
                ? 0.0f
                : 1.0f - math.pow(math.max(0.0f, 1.0f - dampen), work.DeltaTime * 30.0f);
            moduleVelocity = math.lerp(moduleVelocity, targetVelocity, dampenFactor);

            float drag = math.max(0.0f, SampleScalarLut(
                work.LimitVelocityDragLut,
                normalizedLifetime));
            if (work.LimitVelocityMultiplyDragByParticleSize != 0 && work.Sizes != null)
                drag *= math.max(0.0f, work.Sizes[particleIndex]);
            if (work.LimitVelocityMultiplyDragByParticleVelocity != 0)
                drag *= math.length(moduleVelocity);
            moduleVelocity *= math.max(0.0f, 1.0f - drag * work.DeltaTime);

            totalVelocity = math.mul(math.transpose(work.LimitVelocityTransform), moduleVelocity);
            return totalVelocity - animatedVelocity;
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
            if (work.AnimatedVelocities != null)
                work.AnimatedVelocities[destinationIndex] = work.AnimatedVelocities[sourceIndex];
            if (work.InitialEmitterVelocities != null)
                work.InitialEmitterVelocities[destinationIndex] = work.InitialEmitterVelocities[sourceIndex];
            work.StartLifetimes[destinationIndex] = work.StartLifetimes[sourceIndex];
            work.RemainingLifetimes[destinationIndex] = work.RemainingLifetimes[sourceIndex];
            work.Colors[destinationIndex] = work.Colors[sourceIndex];
            work.Sizes[destinationIndex] = work.Sizes[sourceIndex];
            work.MeshIndices[destinationIndex] = work.MeshIndices[sourceIndex];
            if (work.AccumulatedRotations != null)
                work.AccumulatedRotations[destinationIndex] = work.AccumulatedRotations[sourceIndex];
            if (work.NoisePhases != null)
                work.NoisePhases[destinationIndex] = work.NoisePhases[sourceIndex];
            if (work.NoiseSizeMultipliers != null)
                work.NoiseSizeMultipliers[destinationIndex] = work.NoiseSizeMultipliers[sourceIndex];
            if (work.TriggerPreviousInside != null)
                work.TriggerPreviousInside[destinationIndex] = work.TriggerPreviousInside[sourceIndex];
            if (work.TriggerCurrentInside != null)
                work.TriggerCurrentInside[destinationIndex] = work.TriggerCurrentInside[sourceIndex];
            if (work.TriggerColliderEntityIds != null)
                work.TriggerColliderEntityIds[destinationIndex] = work.TriggerColliderEntityIds[sourceIndex];
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
                if (work.AnimatedVelocities != null)
                    work.AnimatedVelocities[particleIndex] = float3.zero;
                if (work.InitialEmitterVelocities != null)
                    work.InitialEmitterVelocities[particleIndex] = work.EmitterVelocity;
                float lifetimeMultiplier = work.LifetimeByEmitterSpeedEnabled != 0
                    ? math.max(0.0f, work.LifetimeByEmitterSpeedMultiplier)
                    : 1.0f;
                float startLifetime = math.max(0.0f, work.StartLifetime * lifetimeMultiplier);
                work.StartLifetimes[particleIndex] = startLifetime;
                work.RemainingLifetimes[particleIndex] = startLifetime;
                work.Colors[particleIndex] = work.StartColor;
                work.Sizes[particleIndex] = work.StartSize;
                work.MeshIndices[particleIndex] = ResolveMeshIndex(work, particleIndex);
                if (work.AccumulatedRotations != null)
                    work.AccumulatedRotations[particleIndex] = float3.zero;
                if (work.NoisePhases != null)
                    work.NoisePhases[particleIndex] = random.NextFloat3(
                        new float3(-1024.0f),
                        new float3(1024.0f));
                if (work.NoiseSizeMultipliers != null)
                    work.NoiseSizeMultipliers[particleIndex] = 1.0f;
                if (work.TriggerPreviousInside != null)
                    work.TriggerPreviousInside[particleIndex] = 0;
                if (work.TriggerCurrentInside != null)
                    work.TriggerCurrentInside[particleIndex] = 0;
                if (work.TriggerColliderEntityIds != null)
                    work.TriggerColliderEntityIds[particleIndex] = 0UL;
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
