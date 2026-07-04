using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal sealed class VividParticleEcsStorage : IDisposable
    {
        private readonly VividEcsArchetypeLine m_Line;
        private readonly VividEcsTypeIndex m_CommonTypeIndex;
        private NativeArray<int> m_ActiveCountOutput;

        public VividParticleEcsStorage()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            m_CommonTypeIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            m_Line = new VividEcsArchetypeLine(0, m_CommonTypeIndex, VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>());
            m_Line.SetSharedComponent(VividParticleSystemId.Invalid);
        }

        public bool isCreated => m_Line.isCreated;

        public int capacity => m_Line.capacity;

        public int maxParticles => m_Line.maxEntries;

        public int activeCount => m_Line.activeCount;

        public int pageCount => m_Line.pageCount;

        public VividParticleSystemId systemId
        {
            get => m_Line.TryGetSharedComponent(out VividParticleSystemId value) ? value : VividParticleSystemId.Invalid;
            set => m_Line.SetSharedComponent(value);
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
            if (!isCreated || !m_Line.Append(out int index))
                return false;

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            common.SetFieldValue(VividParticleCommon.PositionFieldIndex, index, ToFloat3(position));
            common.SetFieldValue(VividParticleCommon.VelocityFieldIndex, index, ToFloat3(velocity));
            common.SetFieldValue(VividParticleCommon.StartLifetimeFieldIndex, index, startLifetime);
            common.SetFieldValue(VividParticleCommon.RemainingLifetimeFieldIndex, index, remainingLifetime);
            common.SetFieldValue(VividParticleCommon.StartColorFieldIndex, index, ToFloat4(color));
            common.SetFieldValue(VividParticleCommon.SizeFieldIndex, index, size);
            return true;
        }

        public bool ScheduleIntegrate(float deltaTime, Vector3 gravity, out JobHandle handle)
        {
            handle = default;
            int count = activeCount;
            if (!isCreated || count <= 0 || deltaTime <= 0.0f)
                return false;

            m_ActiveCountOutput[0] = count;
            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            var job = new VividParticleEcsIntegrateJob
            {
                DeltaTime = deltaTime,
                Gravity = ToFloat3(gravity),
                ActiveCount = count,
                Positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex),
                Velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex),
                StartLifetimes = common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex),
                RemainingLifetimes = common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex),
                Colors = common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex),
                Sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex),
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
            return ToVector3(commonColumn.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex)[index]);
        }

        public Vector3 GetVelocity(int index)
        {
            return ToVector3(commonColumn.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex)[index]);
        }

        public float GetStartLifetime(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex)[index];
        }

        public float GetRemainingLifetime(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex)[index];
        }

        public Color GetColor(int index)
        {
            return ToColor(commonColumn.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex)[index]);
        }

        public float GetSize(int index)
        {
            return commonColumn.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex)[index];
        }

        public VividEcsPageInfo GetPageInfo(int pageIndex)
        {
            return m_Line.GetPageInfo(pageIndex);
        }

        public VividEcsPageGroup CreatePageGroup(Allocator allocator)
        {
            return m_Line.CreatePageGroup(allocator);
        }

        public bool TryGetCommonArrays(
            out NativeArray<float3> positions,
            out NativeArray<float3> velocities,
            out NativeArray<float> startLifetimes,
            out NativeArray<float> remainingLifetimes,
            out NativeArray<float4> colors,
            out NativeArray<float> sizes)
        {
            positions = default;
            velocities = default;
            startLifetimes = default;
            remainingLifetimes = default;
            colors = default;
            sizes = default;

            if (!isCreated)
                return false;

            VividEcsSoaColumn<VividParticleCommon> common = commonColumn;
            positions = common.GetFieldArray<float3>(VividParticleCommon.PositionFieldIndex);
            velocities = common.GetFieldArray<float3>(VividParticleCommon.VelocityFieldIndex);
            startLifetimes = common.GetFieldArray<float>(VividParticleCommon.StartLifetimeFieldIndex);
            remainingLifetimes = common.GetFieldArray<float>(VividParticleCommon.RemainingLifetimeFieldIndex);
            colors = common.GetFieldArray<float4>(VividParticleCommon.StartColorFieldIndex);
            sizes = common.GetFieldArray<float>(VividParticleCommon.SizeFieldIndex);
            return true;
        }

        private VividEcsSoaColumn<VividParticleCommon> commonColumn =>
            m_Line.GetColumn<VividEcsSoaColumn<VividParticleCommon>>(m_CommonTypeIndex);

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
