using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.Particle
{
    internal unsafe struct VividParticleLightSource
    {
        public int SystemId;
        public int ConfigSlot;
        public int ActiveCount;
        public int OutputOffset;
        public int OutputCapacity;
        public int SimulationSpace;
        public int LightType;
        public float BaseRange;
        public float BaseIntensity;
        public float SpotAngle;
        public float InnerSpotAngle;
        public float3 LightColor;
        public float4 RendererColor;
        public float4x4 LocalToWorld;
        public float3 FallbackForward;
        public uint RenderingLayerMask;
        public VividLightRenderDataFlags Flags;

        [NativeDisableUnsafePtrRestriction]
        public float3* Positions;

        [NativeDisableUnsafePtrRestriction]
        public float3* Velocities;

        [NativeDisableUnsafePtrRestriction]
        public float3* AnimatedVelocities;

        [NativeDisableUnsafePtrRestriction]
        public float* StartLifetimes;

        [NativeDisableUnsafePtrRestriction]
        public float* RemainingLifetimes;

        [NativeDisableUnsafePtrRestriction]
        public float4* Colors;

        [NativeDisableUnsafePtrRestriction]
        public float* Sizes;

        [NativeDisableUnsafePtrRestriction]
        public float* NoiseSizeMultipliers;

        [NativeDisableUnsafePtrRestriction]
        public uint* RandomSeeds;
    }

    internal sealed class VividParticleLightManager : IVividLightRenderDataProvider, IDisposable
    {
        private NativeList<VividParticleLightSource> m_Sources;
        private NativeArray<VividLightRenderData> m_Output;
        private NativeArray<int> m_SourceCounts;
        private NativeArray<int> m_OutputCount;
        private JobHandle m_PendingHandle;
        private bool m_HasPendingJob;
        private bool m_IsRegistered;
        private int m_EstimatedLightCount;

        public int estimatedLightCount => m_EstimatedLightCount;

        public int sourceCount => m_Sources.IsCreated ? m_Sources.Length : 0;

        public int lightCount
        {
            get
            {
                Complete();
                return m_OutputCount.IsCreated ? m_OutputCount[0] : 0;
            }
        }

        public void BeginCollect()
        {
            EnsureRegistered();
            Complete();
            EnsureSourceStorage();
            m_Sources.Clear();
            m_EstimatedLightCount = 0;
            if (m_OutputCount.IsCreated)
                m_OutputCount[0] = 0;
        }

        public void AddSource(VividParticleLightSource source)
        {
            if (!m_Sources.IsCreated || source.ActiveCount <= 0 || source.OutputCapacity <= 0)
                return;

            source.OutputOffset = m_EstimatedLightCount;
            source.OutputCapacity = math.min(source.OutputCapacity, source.ActiveCount);
            m_EstimatedLightCount += source.OutputCapacity;
            m_Sources.Add(source);
        }

        public void Schedule(
            NativeArray<VividParticleNativeRenderModuleConfig> configs,
            JobHandle particleDataDependency = default)
        {
            Complete();
            if (!m_Sources.IsCreated || m_Sources.Length == 0 || m_EstimatedLightCount <= 0)
            {
                if (m_OutputCount.IsCreated)
                    m_OutputCount[0] = 0;
                return;
            }

            EnsureOutputCapacity(m_EstimatedLightCount, m_Sources.Length);
            var buildJob = new VividParticleLightBuildJob
            {
                Sources = m_Sources.AsArray(),
                Configs = configs,
                Output = m_Output,
                SourceCounts = m_SourceCounts,
            };
            JobHandle buildHandle = buildJob.Schedule(
                m_Sources.Length,
                1,
                particleDataDependency);
            m_PendingHandle = new VividParticleLightCompactJob
            {
                Sources = m_Sources.AsArray(),
                SourceCounts = m_SourceCounts,
                Output = m_Output,
                OutputCount = m_OutputCount,
            }.Schedule(buildHandle);
            m_HasPendingJob = true;
            JobHandle.ScheduleBatchedJobs();
        }

        public int CopyLightData(NativeArray<VividLightRenderData> destination, int destinationOffset)
        {
            Complete();
            if (!destination.IsCreated || !m_OutputCount.IsCreated)
                return 0;

            int count = math.min(
                math.max(0, m_OutputCount[0]),
                math.max(0, destination.Length - destinationOffset));
            if (count <= 0)
                return 0;

            NativeArray<VividLightRenderData>.Copy(m_Output, 0, destination, destinationOffset, count);
            return count;
        }

        public void Clear()
        {
            Complete();
            if (m_Sources.IsCreated)
                m_Sources.Clear();
            if (m_OutputCount.IsCreated)
                m_OutputCount[0] = 0;
            m_EstimatedLightCount = 0;
        }

        public void Dispose()
        {
            Complete();
            if (m_IsRegistered)
            {
                VividLightRenderDatabase.instance.UnregisterProvider(this);
                m_IsRegistered = false;
            }
            if (m_Sources.IsCreated)
                m_Sources.Dispose();
            if (m_Output.IsCreated)
                m_Output.Dispose();
            if (m_SourceCounts.IsCreated)
                m_SourceCounts.Dispose();
            if (m_OutputCount.IsCreated)
                m_OutputCount.Dispose();
            m_Sources = default;
            m_Output = default;
            m_SourceCounts = default;
            m_OutputCount = default;
            m_EstimatedLightCount = 0;
        }

        private void EnsureRegistered()
        {
            if (m_IsRegistered)
                return;

            VividLightRenderDatabase.instance.RegisterProvider(this);
            VividSceneLightSystem.EnsureInitialized();
            m_IsRegistered = true;
        }

        private void EnsureSourceStorage()
        {
            if (!m_Sources.IsCreated)
                m_Sources = new NativeList<VividParticleLightSource>(16, Allocator.Persistent);
            if (!m_OutputCount.IsCreated)
                m_OutputCount = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        private void EnsureOutputCapacity(int lightCapacity, int sourceCapacity)
        {
            int resolvedLightCapacity = ResolveCapacity(lightCapacity);
            if (!m_Output.IsCreated || m_Output.Length < resolvedLightCapacity)
            {
                if (m_Output.IsCreated)
                    m_Output.Dispose();
                m_Output = new NativeArray<VividLightRenderData>(
                    resolvedLightCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            int resolvedSourceCapacity = ResolveCapacity(sourceCapacity);
            if (!m_SourceCounts.IsCreated || m_SourceCounts.Length < resolvedSourceCapacity)
            {
                if (m_SourceCounts.IsCreated)
                    m_SourceCounts.Dispose();
                m_SourceCounts = new NativeArray<int>(
                    resolvedSourceCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
        }

        private void Complete()
        {
            if (!m_HasPendingJob)
                return;

            m_PendingHandle.Complete();
            m_PendingHandle = default;
            m_HasPendingJob = false;
        }

        private static int ResolveCapacity(int required)
        {
            return math.max(8, Mathf.NextPowerOfTwo(math.max(1, required)));
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private unsafe struct VividParticleLightBuildJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<VividParticleLightSource> Sources;

            [ReadOnly]
            public NativeArray<VividParticleNativeRenderModuleConfig> Configs;

            [NativeDisableParallelForRestriction]
            public NativeArray<VividLightRenderData> Output;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> SourceCounts;

            public void Execute(int sourceIndex)
            {
                VividParticleLightSource source = Sources[sourceIndex];
                if ((uint)source.ConfigSlot >= (uint)Configs.Length)
                {
                    SourceCounts[sourceIndex] = 0;
                    return;
                }

                VividParticleNativeRenderModuleConfig config = Configs[source.ConfigSlot];
                if (config.LightsEnabled == 0 || config.LightsRatio <= 0.0f)
                {
                    SourceCounts[sourceIndex] = 0;
                    return;
                }

                int outputCount = 0;
                int activeCount = math.max(0, source.ActiveCount);
                int outputCapacity = math.min(source.OutputCapacity, config.LightsMaxLights);
                for (int particleIndex = 0;
                     particleIndex < activeCount && outputCount < outputCapacity;
                     particleIndex++)
                {
                    if (!ShouldCreateLight(source, config, particleIndex))
                        continue;

                    Output[source.OutputOffset + outputCount] = BuildLight(source, config, particleIndex);
                    outputCount++;
                }

                SourceCounts[sourceIndex] = outputCount;
            }

            private static bool ShouldCreateLight(
                VividParticleLightSource source,
                VividParticleNativeRenderModuleConfig config,
                int particleIndex)
            {
                float ratio = math.saturate(config.LightsRatio);
                if (ratio >= 1.0f)
                    return true;

                if (config.LightsUseRandomDistribution != 0)
                {
                    uint seed = source.RandomSeeds != null
                        ? source.RandomSeeds[particleIndex]
                        : (uint)(source.SystemId * 397 + particleIndex + 1);
                    uint hash = Hash(seed == 0u ? 1u : seed);
                    return (hash & 0x00ffffffu) * (1.0f / 16777216.0f) < ratio;
                }

                return (int)math.floor((particleIndex + 1) * ratio)
                    != (int)math.floor(particleIndex * ratio);
            }

            private static VividLightRenderData BuildLight(
                VividParticleLightSource source,
                VividParticleNativeRenderModuleConfig config,
                int particleIndex)
            {
                float startLifetime = math.max(source.StartLifetimes[particleIndex], 0.000001f);
                float normalizedLifetime = math.saturate(
                    1.0f - source.RemainingLifetimes[particleIndex] / startLifetime);
                float3 velocity = source.Velocities[particleIndex];
                if (source.AnimatedVelocities != null)
                    velocity += source.AnimatedVelocities[particleIndex];

                float3 position = source.Positions[particleIndex];
                if (source.SimulationSpace == (int)VividParticleSystemSimulationSpace.Local)
                {
                    position = math.transform(source.LocalToWorld, position);
                    velocity = math.mul(new float3x3(source.LocalToWorld), velocity);
                }

                float3 forward = math.normalizesafe(velocity, source.FallbackForward);
                float3 upReference = math.abs(forward.y) < 0.999f
                    ? new float3(0.0f, 1.0f, 0.0f)
                    : new float3(1.0f, 0.0f, 0.0f);
                float3 right = math.normalizesafe(math.cross(upReference, forward), new float3(1.0f, 0.0f, 0.0f));
                float3 up = math.normalizesafe(math.cross(forward, right), new float3(0.0f, 1.0f, 0.0f));

                float speed = math.length(velocity);
                float4 particleColor = EvaluateParticleColor(
                    source,
                    config,
                    particleIndex,
                    normalizedLifetime,
                    speed);
                float size = EvaluateParticleSize(source, config, particleIndex, normalizedLifetime, speed);
                float range = source.BaseRange * SampleLut(config.LightsRangeLut, normalizedLifetime);
                if (config.LightsSizeAffectsRange != 0)
                    range *= math.max(0.0f, size);
                float intensity = source.BaseIntensity
                    * SampleLut(config.LightsIntensityLut, normalizedLifetime);
                if (config.LightsAlphaAffectsIntensity != 0)
                    intensity *= math.saturate(particleColor.w);
                float3 color = source.LightColor;
                if (config.LightsUseParticleColor != 0)
                    color *= math.max(particleColor.xyz, float3.zero);

                float safeRange = math.max(0.0f, range);
                return new VividLightRenderData
                {
                    lightEntityId = default,
                    lightType = (LightType)source.LightType,
                    positionWS = new Vector3(position.x, position.y, position.z),
                    range = safeRange,
                    forwardWS = new Vector3(forward.x, forward.y, forward.z),
                    rightWS = new Vector3(right.x, right.y, right.z),
                    upWS = new Vector3(up.x, up.y, up.z),
                    areaSize = Vector2.zero,
                    shapeRadius = 0.0f,
                    barnDoorAngle = 90.0f,
                    barnDoorLength = 0.05f,
                    volumetricDimmer = 1.0f,
                    volumetricFadeDistance = 10000.0f,
                    volumetricShadowDimmer = 1.0f,
                    intensity = math.max(0.0f, intensity),
                    color = new Vector3(color.x, color.y, color.z),
                    shadowStrength = 0.0f,
                    spotAngle = source.SpotAngle,
                    innerSpotAngle = source.InnerSpotAngle,
                    rangeAttenuationScale = safeRange > 0.0f
                        ? 1.0f / math.max(safeRange * safeRange, 0.000001f)
                        : 0.0f,
                    rangeAttenuationBias = 1.0f,
                    renderingLayerMask = source.RenderingLayerMask,
                    shadowRenderingLayerMask = source.RenderingLayerMask,
                    flags = source.Flags,
                };
            }

            private static float4 EvaluateParticleColor(
                VividParticleLightSource source,
                VividParticleNativeRenderModuleConfig config,
                int particleIndex,
                float normalizedLifetime,
                float speed)
            {
                float4 color = source.Colors[particleIndex] * source.RendererColor;
                if (config.ColorOverLifetimeEnabled != 0)
                    color *= SampleFloat4Lut(config.ColorOverLifetimeLut, normalizedLifetime);
                if (config.ColorBySpeedEnabled != 0)
                {
                    float speedT = InverseLerp(config.ColorBySpeedRange, speed);
                    color *= SampleFloat4Lut(config.ColorBySpeedLut, speedT);
                }
                return math.max(color, float4.zero);
            }

            private static float EvaluateParticleSize(
                VividParticleLightSource source,
                VividParticleNativeRenderModuleConfig config,
                int particleIndex,
                float normalizedLifetime,
                float speed)
            {
                float size = source.Sizes[particleIndex];
                if (config.SizeOverLifetimeEnabled != 0)
                    size *= SampleLut(config.SizeOverLifetimeLut, normalizedLifetime);
                if (config.SizeBySpeedEnabled != 0)
                    size *= SampleLut(config.SizeBySpeedLut, InverseLerp(config.SizeBySpeedRange, speed));
                if (source.NoiseSizeMultipliers != null)
                    size *= source.NoiseSizeMultipliers[particleIndex];
                return math.max(0.0f, size);
            }

            private static float InverseLerp(float2 range, float value)
            {
                return range.y > range.x
                    ? math.saturate((value - range.x) / (range.y - range.x))
                    : 0.0f;
            }

            private static float SampleLut(float* lut, float t)
            {
                float sample = math.saturate(t)
                    * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
                int lower = (int)math.floor(sample);
                int upper = math.min(
                    lower + 1,
                    VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
                return math.max(0.0f, math.lerp(lut[lower], lut[upper], sample - lower));
            }

            private static float4 SampleFloat4Lut(float* lut, float t)
            {
                float sample = math.saturate(t)
                    * (VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
                int lower = (int)math.floor(sample);
                int upper = math.min(
                    lower + 1,
                    VividParticleNativeRenderModuleConfig.LifetimeLutResolution - 1);
                float interpolation = sample - lower;
                return math.lerp(
                    ((float4*)lut)[lower],
                    ((float4*)lut)[upper],
                    interpolation);
            }

            private static uint Hash(uint value)
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct VividParticleLightCompactJob : IJob
        {
            [ReadOnly]
            public NativeArray<VividParticleLightSource> Sources;

            [ReadOnly]
            public NativeArray<int> SourceCounts;

            public NativeArray<VividLightRenderData> Output;
            public NativeArray<int> OutputCount;

            public void Execute()
            {
                int writeOffset = 0;
                for (int sourceIndex = 0; sourceIndex < Sources.Length; sourceIndex++)
                {
                    VividParticleLightSource source = Sources[sourceIndex];
                    int count = math.clamp(SourceCounts[sourceIndex], 0, source.OutputCapacity);
                    for (int lightIndex = 0; lightIndex < count; lightIndex++)
                    {
                        int readIndex = source.OutputOffset + lightIndex;
                        if (writeOffset != readIndex)
                            Output[writeOffset] = Output[readIndex];
                        writeOffset++;
                    }
                }
                OutputCount[0] = writeOffset;
            }
        }
    }
}
