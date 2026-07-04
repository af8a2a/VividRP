using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.Particle
{
    internal sealed class VividParticleStorage : IDisposable
    {
        internal const int PageSize = 256;

        private NativeArray<float3> m_Positions;
        private NativeArray<float3> m_Velocities;
        private NativeArray<float> m_StartLifetimes;
        private NativeArray<float> m_RemainingLifetimes;
        private NativeArray<float4> m_Colors;
        private NativeArray<float> m_Sizes;
        private NativeArray<int> m_ActiveCountOutput;
        private int m_MaxParticles;
        private int m_ActiveCount;

        public bool isCreated => m_Positions.IsCreated;

        public int capacity => m_Positions.IsCreated ? m_Positions.Length : 0;

        public int maxParticles => m_MaxParticles;

        public int activeCount => math.clamp(m_ActiveCount, 0, math.min(capacity, m_MaxParticles));

        public void EnsureCapacity(int maxParticles)
        {
            int requestedMaxParticles = math.max(1, maxParticles);
            int requestedCapacity = AlignToPage(requestedMaxParticles);
            if (capacity == requestedCapacity)
            {
                m_MaxParticles = requestedMaxParticles;
                m_ActiveCount = math.min(activeCount, m_MaxParticles);
                return;
            }

            var positions = CreateColumn<float3>(requestedCapacity);
            var velocities = CreateColumn<float3>(requestedCapacity);
            var startLifetimes = CreateColumn<float>(requestedCapacity);
            var remainingLifetimes = CreateColumn<float>(requestedCapacity);
            var colors = CreateColumn<float4>(requestedCapacity);
            var sizes = CreateColumn<float>(requestedCapacity);
            var activeCountOutput = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            int copyCount = math.min(activeCount, requestedMaxParticles);
            if (copyCount > 0)
            {
                NativeArray<float3>.Copy(m_Positions, positions, copyCount);
                NativeArray<float3>.Copy(m_Velocities, velocities, copyCount);
                NativeArray<float>.Copy(m_StartLifetimes, startLifetimes, copyCount);
                NativeArray<float>.Copy(m_RemainingLifetimes, remainingLifetimes, copyCount);
                NativeArray<float4>.Copy(m_Colors, colors, copyCount);
                NativeArray<float>.Copy(m_Sizes, sizes, copyCount);
            }

            DisposeColumns();
            m_Positions = positions;
            m_Velocities = velocities;
            m_StartLifetimes = startLifetimes;
            m_RemainingLifetimes = remainingLifetimes;
            m_Colors = colors;
            m_Sizes = sizes;
            m_ActiveCountOutput = activeCountOutput;
            m_MaxParticles = requestedMaxParticles;
            m_ActiveCount = copyCount;
        }

        public void Clear()
        {
            m_ActiveCount = 0;
            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput[0] = 0;
        }

        public void Dispose()
        {
            DisposeColumns();
            m_MaxParticles = 0;
            m_ActiveCount = 0;
        }

        public bool Add(
            Vector3 position,
            Vector3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            Color color)
        {
            if (!isCreated || activeCount >= m_MaxParticles)
                return false;

            int index = m_ActiveCount++;
            m_Positions[index] = ToFloat3(position);
            m_Velocities[index] = ToFloat3(velocity);
            m_StartLifetimes[index] = startLifetime;
            m_RemainingLifetimes[index] = remainingLifetime;
            m_Sizes[index] = size;
            m_Colors[index] = ToFloat4(color);
            return true;
        }

        public void Integrate(float deltaTime, Vector3 gravity)
        {
            int count = activeCount;
            if (!isCreated || count <= 0 || deltaTime <= 0.0f)
                return;

            m_ActiveCountOutput[0] = count;
            var job = new VividParticleIntegrateJob
            {
                DeltaTime = deltaTime,
                Gravity = ToFloat3(gravity),
                ActiveCount = count,
                Positions = m_Positions,
                Velocities = m_Velocities,
                StartLifetimes = m_StartLifetimes,
                RemainingLifetimes = m_RemainingLifetimes,
                Colors = m_Colors,
                Sizes = m_Sizes,
                ActiveCountOutput = m_ActiveCountOutput,
            };

            job.Schedule().Complete();
            m_ActiveCount = math.clamp(m_ActiveCountOutput[0], 0, m_MaxParticles);
        }

        public bool IsValidIndex(int index)
        {
            return isCreated && index >= 0 && index < activeCount;
        }

        public Vector3 GetPosition(int index)
        {
            return ToVector3(m_Positions[index]);
        }

        public Vector3 GetVelocity(int index)
        {
            return ToVector3(m_Velocities[index]);
        }

        public float GetStartLifetime(int index)
        {
            return m_StartLifetimes[index];
        }

        public float GetRemainingLifetime(int index)
        {
            return m_RemainingLifetimes[index];
        }

        public Color GetColor(int index)
        {
            return ToColor(m_Colors[index]);
        }

        public float GetSize(int index)
        {
            return m_Sizes[index];
        }

        internal static int AlignToPage(int value)
        {
            return math.max(PageSize, ((math.max(1, value) + PageSize - 1) / PageSize) * PageSize);
        }

        private static NativeArray<T> CreateColumn<T>(int capacity)
            where T : struct
        {
            return new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private void DisposeColumns()
        {
            DisposeColumn(ref m_Positions);
            DisposeColumn(ref m_Velocities);
            DisposeColumn(ref m_StartLifetimes);
            DisposeColumn(ref m_RemainingLifetimes);
            DisposeColumn(ref m_Colors);
            DisposeColumn(ref m_Sizes);
            DisposeColumn(ref m_ActiveCountOutput);
        }

        private static void DisposeColumn<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
                array.Dispose();

            array = default;
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

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividParticleIntegrateJob : IJob
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
