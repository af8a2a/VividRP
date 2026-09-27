using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Derived from the same indexed positions as the VSM caster. Serialized meshlet
    // and LOD spheres remain authoritative for error selection and normal cones.
    internal sealed class VirtualShadowMapGeometryBounds : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Bounds
        {
            internal float4 Center; // w = 1 for a complete, finite geometric bound
            internal float4 Extent;
            internal bool IsValid => Center.w != 0f;
        }

        private Bounds[] m_Meshlets = Array.Empty<Bounds>(), m_LodNodes = Array.Empty<Bounds>();
        private GraphicsBuffer m_MeshletBuffer, m_LodBuffer;
        private int m_MeshletCount, m_LodCount;
        internal GraphicsBuffer Meshlets => m_MeshletBuffer;
        internal GraphicsBuffer LodNodes => m_LodBuffer;
        internal IReadOnlyList<Bounds> CpuMeshlets => m_Meshlets;
        internal IReadOnlyList<Bounds> CpuLodNodes => m_LodNodes;
        private static readonly int s_MeshletsId = Shader.PropertyToID("_VSMMeshletBounds");
        private static readonly int s_LodId = Shader.PropertyToID("_VSMLodBounds");
        private static readonly int s_MeshletCountId = Shader.PropertyToID("_VSMMeshletBoundsCount");
        private static readonly int s_LodCountId = Shader.PropertyToID("_VSMLodBoundsCount");

        internal bool Update(VividGPUDrivenSceneData scene, bool geometryChanged)
        {
            if (!geometryChanged && m_MeshletCount == scene.MeshletCount && m_LodCount == scene.MeshLODNodeCount
                && m_MeshletBuffer?.IsValid() == true && m_LodBuffer?.IsValid() == true) return false;
            m_MeshletCount = scene.MeshletCount; m_LodCount = scene.MeshLODNodeCount;
            if (m_Meshlets.Length != Math.Max(m_MeshletCount, 1)) m_Meshlets = new Bounds[Math.Max(m_MeshletCount, 1)];
            if (m_LodNodes.Length != Math.Max(m_LodCount, 1)) m_LodNodes = new Bounds[Math.Max(m_LodCount, 1)];
            for (int i = 0; i < m_MeshletCount; i++) m_Meshlets[i] = BuildMeshlet(scene.Meshlets[i], scene.Vertices, scene.Indices);
            for (int i = 0; i < m_LodCount; i++) m_LodNodes[i] = BuildLod(scene.MeshLODNodes[i]);
            Ensure(ref m_MeshletBuffer, m_Meshlets.Length, "VSMMeshletBounds");
            Ensure(ref m_LodBuffer, m_LodNodes.Length, "VSMLodBounds");
            m_MeshletBuffer.SetData(m_Meshlets); m_LodBuffer.SetData(m_LodNodes);
            return true;
        }

        private static Bounds BuildMeshlet(VividMeshlet meshlet, IReadOnlyList<VividMeshletVertex> vertices,
            IReadOnlyList<byte> indices)
        {
            uint indexCount = meshlet.TriangleCount * 3u;
            if (indexCount == 0 || meshlet.VertexCount == 0
                || meshlet.TriangleOffset > indices.Count || indexCount > (uint)indices.Count - meshlet.TriangleOffset
                || meshlet.VertexOffset > vertices.Count || meshlet.VertexCount > (uint)vertices.Count - meshlet.VertexOffset)
                return default;
            float3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
            for (uint i = 0; i < indexCount; i++)
            {
                uint localIndex = indices[(int)(meshlet.TriangleOffset + i)];
                if (localIndex >= meshlet.VertexCount) return default;
                float3 position = vertices[(int)(meshlet.VertexOffset + localIndex)].Position;
                if (!math.all(math.isfinite(position))) return default;
                low = math.min(low, position); high = math.max(high, position);
            }
            return FromMinMax(low, high);
        }

        private Bounds BuildLod(VividMeshLODNode node)
        {
            if (node.MeshletCount == 0 || node.MeshletStartIndex > m_MeshletCount
                || node.MeshletCount > (uint)m_MeshletCount - node.MeshletStartIndex) return default;
            float3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
            for (uint i = 0; i < node.MeshletCount; i++)
            {
                Bounds child = m_Meshlets[(int)(node.MeshletStartIndex + i)];
                // A partially known range cannot tighten the original node sphere.
                if (!child.IsValid) return default;
                low = math.min(low, child.Center.xyz - child.Extent.xyz);
                high = math.max(high, child.Center.xyz + child.Extent.xyz);
            }
            return FromMinMax(low, high);
        }

        private static Bounds FromMinMax(float3 low, float3 high)
        {
            float3 center = low * .5f + high * .5f;
            float3 extent = math.max(math.abs(low - center), math.abs(high - center));
            // Outward margin covers center/extent conversion and union rounding.
            extent += math.max(math.abs(low), math.abs(high)) * 2e-7f + 1e-7f;
            return math.all(math.isfinite(center)) && math.all(math.isfinite(extent))
                ? new Bounds { Center = new float4(center, 1f), Extent = new float4(extent, 0f) } : default;
        }

        private static void Ensure(ref GraphicsBuffer buffer, int count, string name)
        {
            if (buffer?.IsValid() == true && buffer.count == count) return;
            buffer?.Dispose(); buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 32) { name = name };
        }

        internal void BindLod(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, s_LodId, m_LodBuffer);
            cmd.SetComputeIntParam(shader, s_LodCountId, m_LodCount);
        }

        internal void BindMeshlets(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, s_MeshletsId, m_MeshletBuffer);
            cmd.SetComputeIntParam(shader, s_MeshletCountId, m_MeshletCount);
        }

        public void Dispose()
        {
            m_MeshletBuffer?.Dispose(); m_MeshletBuffer = null;
            m_LodBuffer?.Dispose(); m_LodBuffer = null;
            m_Meshlets = m_LodNodes = Array.Empty<Bounds>(); m_MeshletCount = m_LodCount = 0;
        }
    }
}
