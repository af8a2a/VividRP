using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Auxiliary acceleration over existing LOD records. It does not change the
    // serialized LOD graph, node order, or the leaf error-selection contract.
    internal sealed class VirtualShadowMapLODHierarchy : IDisposable
    {
        internal const int NodesPerLeaf = 32;
        [StructLayout(LayoutKind.Sequential)]
        internal struct Node
        {
            internal float4 Min; // xyz bounds, w minimum LOD level
            internal float4 Max; // xyz bounds, w maximum LOD level
            internal uint4 Range; // first LOD record, count, escape index, leaf
        }

        private readonly List<Node> m_Nodes = new();
        private uint[] m_Roots = Array.Empty<uint>(), m_RangeCounts = Array.Empty<uint>();
        private GraphicsBuffer m_NodeBuffer, m_RootBuffer;
        internal GraphicsBuffer Nodes => m_NodeBuffer;
        internal GraphicsBuffer Roots => m_RootBuffer;
        internal int NodeCount => m_Nodes.Count;
        internal IReadOnlyList<Node> CpuNodes => m_Nodes;
        private static readonly int s_NodesId = Shader.PropertyToID("_VSMLodHierarchy");
        private static readonly int s_RootsId = Shader.PropertyToID("_VSMLodRoots");
        private static readonly int s_CountId = Shader.PropertyToID("_VSMLodHierarchyCount");
        private static readonly int s_RootCountId = Shader.PropertyToID("_VSMLodRootCount");
        private static readonly int s_ForcedDepthId = Shader.PropertyToID("_ForcedMeshLODNodeDepth");

        internal void Update(IReadOnlyList<VividMeshLODNode> lodNodes,
            IReadOnlyList<VividInstanceData> instances, bool geometryChanged)
        {
            int count = Math.Max(lodNodes.Count, 1);
            if (m_Roots.Length != count)
            {
                m_Roots = new uint[count]; m_RangeCounts = new uint[count];
                geometryChanged = true;
            }
            if (geometryChanged) Array.Clear(m_RangeCounts, 0, count);
            bool changed = geometryChanged || m_NodeBuffer?.IsValid() != true || m_RootBuffer?.IsValid() != true;
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i]; uint start = instance.TopMeshLODStartIndex, length = instance.TotalMeshLODCount;
                if (start >= lodNodes.Count || length == 0 || length > (uint)lodNodes.Count - start) continue;
                if (length <= m_RangeCounts[start]) continue;
                m_RangeCounts[start] = length; changed = true;
            }
            if (!changed) return;
            m_Nodes.Clear(); Array.Fill(m_Roots, uint.MaxValue);
            for (int start = 0; start < count; start++)
                if (m_RangeCounts[start] != 0)
                    m_Roots[start] = (uint)Build(lodNodes, start, (int)m_RangeCounts[start]);
            Ensure(ref m_NodeBuffer, Math.Max(m_Nodes.Count, 1), 48, "VSMLodHierarchy");
            Ensure(ref m_RootBuffer, count, 4, "VSMLodRoots");
            if (m_Nodes.Count != 0) m_NodeBuffer.SetData(m_Nodes);
            m_RootBuffer.SetData(m_Roots);
        }

        private int Build(IReadOnlyList<VividMeshLODNode> source, int start, int count)
        {
            int index = m_Nodes.Count; m_Nodes.Add(default);
            float4 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
            if (count <= NodesPerLeaf)
            {
                for (int i = start; i < start + count; i++)
                {
                    var node = source[i]; float radius = math.max(node.Bounds.w, 0f);
                    float3 center = node.Bounds.xyz;
                    // Invalid bounds must conservatively keep this branch.
                    bool finite = math.all(math.isfinite(node.Bounds));
                    low = math.min(low, new float4(finite ? center - radius : new float3(float.NegativeInfinity), node.LevelIndex));
                    high = math.max(high, new float4(finite ? center + radius : new float3(float.PositiveInfinity), node.LevelIndex));
                }
            }
            else
            {
                int leaves = (count + NodesPerLeaf - 1) / NodesPerLeaf;
                int leftCount = (leaves / 2) * NodesPerLeaf;
                int left = Build(source, start, leftCount), right = Build(source, start + leftCount, count - leftCount);
                low = math.min(m_Nodes[left].Min, m_Nodes[right].Min);
                high = math.max(m_Nodes[left].Max, m_Nodes[right].Max);
            }
            m_Nodes[index] = new Node { Min = low, Max = high,
                Range = new uint4((uint)start, (uint)count, (uint)m_Nodes.Count, count <= NodesPerLeaf ? 1u : 0u) };
            return index;
        }

        private static void Ensure(ref GraphicsBuffer buffer, int count, int stride, string name)
        {
            if (buffer?.IsValid() == true && buffer.count == count) return;
            buffer?.Dispose(); buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride) { name = name };
        }

        internal void Bind(CommandBuffer cmd, ComputeShader shader, int kernel, int forcedDepth)
        {
            cmd.SetComputeBufferParam(shader, kernel, s_NodesId, m_NodeBuffer);
            cmd.SetComputeBufferParam(shader, kernel, s_RootsId, m_RootBuffer);
            cmd.SetComputeIntParam(shader, s_CountId, m_Nodes.Count);
            cmd.SetComputeIntParam(shader, s_RootCountId, m_Roots.Length);
            cmd.SetComputeIntParam(shader, s_ForcedDepthId, forcedDepth < 0 ? VividGPUDrivenDefaults.ForcedMeshLODNodeDepth : forcedDepth);
        }

        public void Dispose()
        {
            m_NodeBuffer?.Dispose(); m_NodeBuffer = null;
            m_RootBuffer?.Dispose(); m_RootBuffer = null;
            m_Nodes.Clear(); m_Roots = m_RangeCounts = Array.Empty<uint>();
        }
    }
}
