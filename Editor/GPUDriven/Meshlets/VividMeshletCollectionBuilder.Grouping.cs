using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using VividRP.Runtime;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    internal static partial class VividMeshletCollectionBuilder
    {
        internal static float AccumulateError(float previous, float local)
        {
            if (!math.isfinite(previous) || previous < 0f || !math.isfinite(local) || local < 0f)
                throw new ArgumentOutOfRangeException(nameof(local), "LOD errors must be finite and nonnegative.");
            // Keep the additive envelope. max(previous, local) from the reference is
            // monotone, but can underestimate successive displacements in one direction.
            // Zero-error/reclustering steps need no artificial object-space bias.
            double sum = (double) previous + local;
            float result = (float) sum;
            if ((double) result < sum || (local > 0f && previous > 0f && result == math.max(previous, local)))
                result = math.asfloat(math.asuint(result) + 1u);
            if (!math.isfinite(result)) throw new OverflowException("Accumulated LOD error exceeds FP32.");
            return result;
        }

        internal static unsafe float4 ComputeGroupBounds(NativeArray<float4> spheres)
        {
            if (spheres.Length == 0) throw new ArgumentException("A group must contain a sphere.", nameof(spheres));
            foreach (float4 sphere in spheres)
                if (!math.all(math.isfinite(sphere)) || sphere.w < 0f)
                    throw new ArgumentException("Group spheres must be finite with nonnegative radii.", nameof(spheres));
            float* data = (float*) spheres.GetUnsafeReadOnlyPtr();
            meshopt_Bounds merged = MeshOptimizerBindings.meshopt_computeSphereBounds(data, (nuint) spheres.Length, 16, data + 3, 16);
            float3 center = new(merged.Center[0], merged.Center[1], merged.Center[2]);
            if (!math.all(math.isfinite(center))) throw new OverflowException("Group sphere center exceeds FP32.");
            double radius = 0;
            foreach (float4 sphere in spheres)
            {
                double3 delta = (double3) center - (double3) sphere.xyz;
                double distance = math.length(delta);
                double required = distance == 0 ? sphere.w
                    : (distance + sphere.w) * (1.0 + 8.0 * 2.2204460492503131e-16);
                radius = Math.Max(radius, required);
            }
            float rounded = (float) radius;
            if ((double) rounded < radius) rounded = math.asfloat(math.asuint(rounded) + 1u);
            if (!math.isfinite(rounded)) throw new OverflowException("Group sphere radius exceeds FP32.");
            return new float4(center, rounded);
        }

        internal static unsafe float ComputeUVWeight(NativeArray<uint> indices, NativeArray<float> uv,
            uint offset, uint stride, float multiplier)
        {
            if (!uv.IsCreated || multiplier == 0f) return 0f;
            byte* data = (byte*) uv.GetUnsafeReadOnlyPtr() + offset;
            double area = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                double2 a = *(float2*) (data + indices[i] * stride);
                double2 b = *(float2*) (data + indices[i + 1] * stride);
                double2 c = *(float2*) (data + indices[i + 2] * stride);
                if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c)))
                    throw new ArgumentException("UV coordinates must be finite.", nameof(uv));
                double2 ab = b - a, ac = c - a;
                area += Math.Abs(ab.x * ac.y - ab.y * ac.x) * 0.5;
            }
            // Degenerate/one-dimensional UV maps retain the explicit weight. Cap the
            // density factor for nearly degenerate UVs; the import multiplier remains explicit.
            double densityWeight = area > 0 ? Math.Min(1024.0, Math.Sqrt((indices.Length / 3.0) / area)) : 1.0;
            float weight = (float) (multiplier * densityWeight);
            if (!math.isfinite(weight)) throw new OverflowException("UV weight exceeds FP32.");
            return weight;
        }

        // Like vk_lod_clusters' clusterlod partition/lockBoundary: connectivity is based on
        // position, while group compaction retains the original vertex IDs (including seams).
        private static unsafe NativeArray<uint> CreatePositionRemap(in VividMeshOptimizer.VertexLayout layout)
        {
            int vertexCount = layout.Vertices.Length * sizeof(float) / (int) layout.PositionStride;
            var remap = new NativeArray<uint>(vertexCount, Allocator.Temp);
            MeshOptimizerBindings.meshopt_generatePositionRemap(
                (uint*) remap.GetUnsafePtr(),
                (float*) ((byte*) layout.Vertices.GetUnsafeReadOnlyPtr() + layout.PositionOffset),
                (nuint) vertexCount, layout.PositionStride);
            return remap;
        }

        private static unsafe NativeArray<NativeList<int>> GroupMeshlets(MeshLODNodeLevel level, int meshletsPerGroup,
            in VividMeshOptimizer.VertexLayout layout, NativeArray<uint> positionRemap, Allocator allocator)
        {
            int nodeCount = level.Nodes.Length;
            using var clusterIndices = new NativeList<uint>(Allocator.Temp);
            var clusterCounts = new NativeArray<uint>(nodeCount, Allocator.Temp);
            using var partitions = new NativeArray<uint>(nodeCount, Allocator.Temp);
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                MeshLODNode node = level.Nodes[nodeIndex];
                VividMeshOptimizer.MeshletBuildResults build = level.MeshletsNodeLists[node.MeshletNodeListIndex].MeshletBuildResults;
                meshopt_Meshlet meshlet = build.Meshlets[node.MeshletIndex];
                clusterCounts[nodeIndex] = meshlet.TriangleCount * 3;
                for (int i = 0; i < meshlet.TriangleCount * 3; i++)
                {
                    uint vertex = build.Vertices[(int) meshlet.VertexOffset + build.Indices[(int) meshlet.TriangleOffset + i]];
                    clusterIndices.Add(positionRemap[(int) vertex]);
                }
            }

            int partitionCount = nodeCount <= meshletsPerGroup ? 1 : (int) MeshOptimizerBindings.meshopt_partitionClusters(
                (uint*) partitions.GetUnsafePtr(), clusterIndices.GetUnsafePtr(), (nuint) clusterIndices.Length,
                (uint*) clusterCounts.GetUnsafeReadOnlyPtr(), (nuint) nodeCount,
                (float*) ((byte*) layout.Vertices.GetUnsafeReadOnlyPtr() + layout.PositionOffset),
                (nuint) positionRemap.Length, layout.PositionStride, (nuint) meshletsPerGroup);

            // partition size is a target, not a hard limit; use the returned count and grow lists as needed.
            var groups = new NativeArray<NativeList<int>>(partitionCount, allocator);
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new NativeList<int>(meshletsPerGroup, allocator);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                groups[(int) partitions[i]].Add(i);
            }
            clusterCounts.Dispose();
            return groups;
        }

        private static NativeArray<byte> LockGroupBoundaries(MeshLODNodeLevel level,
            NativeArray<NativeList<int>> groups, NativeArray<uint> positionRemap)
        {
            var owners = new NativeArray<int>(positionRemap.Length, Allocator.Temp);
            var locks = new NativeArray<byte>(positionRemap.Length, Allocator.Temp);
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                foreach (int nodeIndex in groups[groupIndex])
                {
                    MeshLODNode node = level.Nodes[nodeIndex];
                    VividMeshOptimizer.MeshletBuildResults build = level.MeshletsNodeLists[node.MeshletNodeListIndex].MeshletBuildResults;
                    meshopt_Meshlet meshlet = build.Meshlets[node.MeshletIndex];
                    for (int i = 0; i < meshlet.VertexCount; i++)
                    {
                        int canonical = (int) positionRemap[(int) build.Vertices[(int) meshlet.VertexOffset + i]];
                        if (owners[canonical] != 0 && owners[canonical] != groupIndex + 1)
                        {
                            locks[canonical] = (byte) meshopt_SimplifyVertexFlags.Lock;
                        }
                        owners[canonical] = groupIndex + 1;
                    }
                }
            }

            // The remap points to canonical fixed points, so this propagation is safe in place.
            for (int i = 0; i < locks.Length; i++)
            {
                locks[i] = locks[(int) positionRemap[i]];
            }
            owners.Dispose();
            return locks;
        }
    }
}
