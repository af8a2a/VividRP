using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.Particle.ECS
{
    internal sealed class VividParticleEcsStorage : IDisposable
    {
        private readonly VividParticleArchetypeLine m_Line = new();
        private NativeArray<int> m_ActiveCountOutput;

        public bool isCreated => m_Line.isCreated;

        public int capacity => m_Line.capacity;

        public int maxParticles => m_Line.maxParticles;

        public int activeCount => m_Line.activeCount;

        public int pageCount => m_Line.pageCount;

        public VividParticleSystemId systemId
        {
            get => m_Line.systemId;
            set => m_Line.systemId = value;
        }

        public void EnsureCapacity(int maxParticles)
        {
            m_Line.EnsureCapacity(maxParticles);
            if (!m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        public void Clear()
        {
            m_Line.Clear();
            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput[0] = 0;
        }

        public void Dispose()
        {
            m_Line.Dispose();
            if (m_ActiveCountOutput.IsCreated)
                m_ActiveCountOutput.Dispose();

            m_ActiveCountOutput = default;
        }

        public bool Add(
            Vector3 position,
            Vector3 velocity,
            float startLifetime,
            float remainingLifetime,
            float size,
            Color color)
        {
            if (!isCreated)
                return false;

            return m_Line.Append(
                ToFloat3(position),
                ToFloat3(velocity),
                startLifetime,
                remainingLifetime,
                size,
                ToFloat4(color),
                out _);
        }

        public bool ScheduleIntegrate(float deltaTime, Vector3 gravity, out JobHandle handle)
        {
            handle = default;
            int count = activeCount;
            if (!isCreated || count <= 0 || deltaTime <= 0.0f)
                return false;

            m_ActiveCountOutput[0] = count;
            VividParticleCommonColumns common = m_Line.common;
            var job = new VividParticleEcsIntegrateJob
            {
                DeltaTime = deltaTime,
                Gravity = ToFloat3(gravity),
                ActiveCount = count,
                Positions = common.positions,
                Velocities = common.velocities,
                StartLifetimes = common.startLifetimes,
                RemainingLifetimes = common.remainingLifetimes,
                Colors = common.colors,
                Sizes = common.sizes,
                ActiveCountOutput = m_ActiveCountOutput,
            };

            handle = job.Schedule();
            return true;
        }

        public void ApplyScheduledIntegrateResult()
        {
            if (!m_ActiveCountOutput.IsCreated)
                return;

            m_Line.SetActiveCount(m_ActiveCountOutput[0]);
        }

        public bool IsValidIndex(int index)
        {
            return isCreated && index >= 0 && index < activeCount;
        }

        public Vector3 GetPosition(int index)
        {
            return ToVector3(m_Line.common.GetPosition(index));
        }

        public Vector3 GetVelocity(int index)
        {
            return ToVector3(m_Line.common.GetVelocity(index));
        }

        public float GetStartLifetime(int index)
        {
            return m_Line.common.startLifetimes[index];
        }

        public float GetRemainingLifetime(int index)
        {
            return m_Line.common.remainingLifetimes[index];
        }

        public Color GetColor(int index)
        {
            return ToColor(m_Line.common.GetColor(index));
        }

        public float GetSize(int index)
        {
            return m_Line.common.sizes[index];
        }

        public VividParticlePageInfo GetPageInfo(int pageIndex)
        {
            return m_Line.GetPageInfo(pageIndex);
        }

        public VividParticlePageGroup CreatePageGroup(Allocator allocator)
        {
            return m_Line.CreatePageGroup(allocator);
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
