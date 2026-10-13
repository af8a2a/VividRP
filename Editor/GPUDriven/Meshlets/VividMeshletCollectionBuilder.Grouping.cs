using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using VividRP.Runtime;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    internal static partial class VividMeshletCollectionBuilder
    {
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
