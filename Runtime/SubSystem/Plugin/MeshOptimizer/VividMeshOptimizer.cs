using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Assertions;
using static VividRP.Runtime.MeshOptimizerBindings;

namespace VividRP.Runtime
{
    public static class VividMeshOptimizer
    {
        public enum SimplifyMode
        {
            Normal,
            Sloppy,
        }

        public static unsafe NativeArray<uint> OptimizeVertexCache(Allocator allocator, NativeArray<uint> indices, uint vertexCount)
        {
            var result = new NativeArray<uint>(indices.Length, allocator);
            meshopt_optimizeVertexCache((uint*) result.GetUnsafePtr(), (uint*) indices.GetUnsafeReadOnlyPtr(), (nuint) indices.Length, vertexCount);
            return result;
        }

        public static unsafe uint OptimizeIndexingInPlace(uint vertexCount, NativeArray<uint> indices, NativeArray<meshopt_Stream> streams)
        {
            var remap = new NativeArray<uint>((int) vertexCount, Allocator.Temp);
            nuint uniqueVertices = meshopt_generateVertexRemapMulti(
                (uint*) remap.GetUnsafePtr(),
                (uint*) indices.GetUnsafeReadOnlyPtr(),
                (nuint) indices.Length,
                vertexCount,
                (meshopt_Stream*) streams.GetUnsafeReadOnlyPtr(),
                (nuint) streams.Length
            );

            Assert.IsTrue(uniqueVertices <= vertexCount);

            meshopt_remapIndexBuffer((uint*) indices.GetUnsafePtr(), (uint*) indices.GetUnsafePtr(), (nuint) indices.Length, (uint*) remap.GetUnsafePtr());

            for (int index = 0; index < streams.Length; index++)
            {
                ref meshopt_Stream stream = ref streams.ElementAtRef(index);
                meshopt_remapVertexBuffer(stream.data, stream.data, vertexCount, stream.stride, (uint*) remap.GetUnsafePtr());
            }

            remap.Dispose();
            return (uint) uniqueVertices;
        }

        public static unsafe MeshletBuildResults BuildMeshlets(Allocator allocator, NativeArray<float> vertices, uint vertexPositionOffset,
            uint vertexPositionStride, NativeArray<uint> indices, in MeshletGenerationParams meshletGenerationParams)
        {
            Assert.IsTrue(vertices.Length > 0);
            Assert.IsTrue(indices.Length > 0);
            Assert.IsTrue(vertexPositionStride > 0);
            Assert.IsTrue(meshletGenerationParams.MaxVertices > 0);
            Assert.IsTrue(meshletGenerationParams.MaxTriangles > 0);

            nuint maxMeshlets = meshopt_buildMeshletsBound((nuint) indices.Length, meshletGenerationParams.MaxVertices, meshletGenerationParams.MaxTriangles);
            Assert.IsTrue(maxMeshlets > 0);

            var meshlets = new NativeArray<meshopt_Meshlet>((int) maxMeshlets, allocator);
            var meshletVertices = new NativeArray<uint>(indices.Length, allocator);
            var meshletIndices = new NativeArray<byte>(indices.Length, allocator);

            uint floatsInVertex = vertexPositionStride / sizeof(float);
            nuint meshletCount = meshopt_buildMeshlets(
                (meshopt_Meshlet*) meshlets.GetUnsafePtr(),
                (uint*) meshletVertices.GetUnsafePtr(),
                (byte*) meshletIndices.GetUnsafePtr(),
                (uint*) indices.GetUnsafeReadOnlyPtr(),
                (nuint) indices.Length,
                (float*) ((byte*) vertices.GetUnsafeReadOnlyPtr() + vertexPositionOffset),
                (nuint) vertices.Length / floatsInVertex,
                vertexPositionStride,
                meshletGenerationParams.MaxVertices,
                meshletGenerationParams.MaxTriangles,
                meshletGenerationParams.ConeWeight
            );

            for (int meshletIndex = 0; meshletIndex < (int) meshletCount; meshletIndex++)
            {
                ref readonly meshopt_Meshlet meshlet = ref meshlets.ElementAtRefReadonly(meshletIndex);
                meshopt_optimizeMeshlet(
                    meshletVertices.ElementPtr((int) meshlet.VertexOffset),
                    meshletIndices.ElementPtr((int) meshlet.TriangleOffset),
                    meshlet.TriangleCount,
                    meshlet.VertexCount
                );
            }

            int usedVertexCount = 0;
            int usedIndexCount = 0;
            if (meshletCount > 0)
            {
                ref readonly meshopt_Meshlet lastMeshlet = ref meshlets.ElementAtRefReadonly((int) meshletCount - 1);
                usedVertexCount = (int) (lastMeshlet.VertexOffset + lastMeshlet.VertexCount);
                usedIndexCount = (int) (lastMeshlet.TriangleOffset + lastMeshlet.TriangleCount * 3);
            }

            return new MeshletBuildResults
            {
                Meshlets = meshlets.GetSubArray(0, (int) meshletCount),
                Vertices = meshletVertices.GetSubArray(0, usedVertexCount),
                Indices = meshletIndices.GetSubArray(0, usedIndexCount),
            };
        }

        public static unsafe meshopt_Bounds ComputeMeshletBounds(in MeshletBuildResults buildResults, int meshletIndex, NativeArray<float> vertices,
            uint vertexPositionOffset, uint vertexPositionStride)
        {
            ref readonly meshopt_Meshlet meshlet = ref buildResults.Meshlets.ElementAtRefReadonly(meshletIndex);
            uint floatsInVertex = vertexPositionStride / sizeof(float);

            return meshopt_computeMeshletBounds(
                buildResults.Vertices.ElementPtr((int) meshlet.VertexOffset),
                buildResults.Indices.ElementPtr((int) meshlet.TriangleOffset),
                meshlet.TriangleCount,
                (float*) ((byte*) vertices.GetUnsafeReadOnlyPtr() + vertexPositionOffset),
                (nuint) vertices.Length / floatsInVertex,
                vertexPositionStride
            );
        }

        public static unsafe MeshletBuildResults SimplifyMeshlets(Allocator allocator, NativeArray<MeshletBuildResults> meshletGroups,
            in VertexLayout vertexLayout, in MeshletGenerationParams meshletGenerationParams, SimplifyMode simplifyMode, float targetError,
            out float resultError)
        {
            var localVertices = new NativeList<ClusterVertex>(Allocator.Temp);
            var localVerticesGlobalIndices = new NativeList<uint>(Allocator.Temp);
            var localIndices = new NativeList<uint>(Allocator.Temp);

            byte* pVertexPositionsBytes = (byte*) vertexLayout.Vertices.GetUnsafeReadOnlyPtr() + vertexLayout.PositionOffset;

            foreach (MeshletBuildResults group in meshletGroups)
            {
                foreach (meshopt_Meshlet meshlet in group.Meshlets)
                {
                    int localOffset = localVertices.Length;

                    for (uint vertexIndex = 0; vertexIndex < meshlet.VertexCount; vertexIndex++)
                    {
                        uint globalIndex = group.Vertices[(int) (meshlet.VertexOffset + vertexIndex)];
                        localVertices.Add(new ClusterVertex
                        {
                            Position = *(float3*) (pVertexPositionsBytes + globalIndex * vertexLayout.PositionStride),
                        });
                        localVerticesGlobalIndices.Add(globalIndex);
                    }

                    for (uint triangleIndex = 0; triangleIndex < meshlet.TriangleCount; triangleIndex++)
                    {
                        int triangleOffset = (int) (meshlet.TriangleOffset + triangleIndex * 3);
                        localIndices.Add((uint) (localOffset + group.Indices[triangleOffset + 0]));
                        localIndices.Add((uint) (localOffset + group.Indices[triangleOffset + 1]));
                        localIndices.Add((uint) (localOffset + group.Indices[triangleOffset + 2]));
                    }
                }
            }

            int targetIndexCount = (int) (localIndices.Length / 3 * 0.5f * 3);
            float resultErrorValue = 0.0f;

            uint* pDestination = localIndices.GetUnsafePtr();
            nuint indexCount = (nuint) localIndices.Length;
            float* pVertexPositions = (float*) localVertices.GetUnsafePtr();
            nuint vertexCount = (nuint) localVertices.Length;
            nuint vertexPositionsStride = (nuint) UnsafeUtility.SizeOf<ClusterVertex>();
            int simplifiedIndexCount = simplifyMode switch
            {
                SimplifyMode.Normal => (int) meshopt_simplify(
                    pDestination,
                    pDestination,
                    indexCount,
                    pVertexPositions,
                    vertexCount,
                    vertexPositionsStride,
                    (nuint) targetIndexCount,
                    targetError,
                    (uint) meshopt_SimplifyOptions.LockBorder,
                    &resultErrorValue
                ),
                SimplifyMode.Sloppy => (int) meshopt_simplifySloppy(
                    pDestination,
                    pDestination,
                    indexCount,
                    pVertexPositions,
                    vertexCount,
                    vertexPositionsStride,
                    null,
                    (nuint) targetIndexCount,
                    targetError,
                    &resultErrorValue
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(simplifyMode), simplifyMode, null),
            };

            localIndices.Length = simplifiedIndexCount;

            var globalIndices = new NativeList<uint>(localIndices.Length, Allocator.Temp);
            foreach (uint localIndex in localIndices)
            {
                globalIndices.Add(localVerticesGlobalIndices[(int) localIndex]);
            }

            MeshletBuildResults result = BuildMeshlets(
                allocator,
                vertexLayout.Vertices,
                vertexLayout.PositionOffset,
                vertexLayout.PositionStride,
                globalIndices.AsArray(),
                meshletGenerationParams
            );

            globalIndices.Dispose();
            localIndices.Dispose();
            localVerticesGlobalIndices.Dispose();
            localVertices.Dispose();

            resultError = resultErrorValue;
            return result;
        }

        public static unsafe void SpatialSortTrianglesInPlace(NativeArray<uint> indices, NativeArray<float> vertices, uint vertexCount,
            uint vertexPositionOffset, uint vertexPositionsStride)
        {
            uint* pIndices = (uint*) indices.GetUnsafePtr();
            float* pVertexPositions = (float*) ((byte*) vertices.GetUnsafeReadOnlyPtr() + vertexPositionOffset);
            meshopt_spatialSortTriangles(pIndices, pIndices, (nuint) indices.Length, pVertexPositions, vertexCount, vertexPositionsStride);
        }

        public static unsafe NativeArray<T> SpatialSort<T>(NativeArray<T> items, NativeArray<float3> sortPositions, Allocator allocator)
            where T : struct
        {
            Assert.IsTrue(items.Length == sortPositions.Length);

            int itemsCount = items.Length;
            var remap = new NativeArray<uint>(itemsCount, Allocator.Temp);
            meshopt_spatialSortRemap(
                (uint*) remap.GetUnsafePtr(),
                (float*) sortPositions.GetUnsafeReadOnlyPtr(),
                (nuint) itemsCount,
                (nuint) UnsafeUtility.SizeOf<float3>()
            );

            var sortedItems = new NativeArray<T>(itemsCount, allocator);
            for (int index = 0; index < itemsCount; index++)
            {
                uint remapIndex = remap[index];
                if (remapIndex != uint.MaxValue)
                {
                    sortedItems[(int) remapIndex] = items[index];
                }
            }

            remap.Dispose();
            return sortedItems;
        }

        public struct VertexLayout
        {
            public NativeArray<float> Vertices;
            public uint PositionOffset;
            public uint PositionStride;
            public NativeArray<float> UV;
            public uint UVOffset;
            public uint UVStride;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClusterVertex
        {
            public float3 Position;
        }

        public struct MeshletBuildResults : IDisposable
        {
            public NativeArray<meshopt_Meshlet> Meshlets;
            public NativeArray<uint> Vertices;
            public NativeArray<byte> Indices;

            public JobHandle Dispose(JobHandle inputDeps)
            {
                JobHandle handle = inputDeps;
                if (Meshlets.IsCreated)
                {
                    handle = Meshlets.Dispose(handle);
                }

                if (Vertices.IsCreated)
                {
                    handle = Vertices.Dispose(handle);
                }

                if (Indices.IsCreated)
                {
                    handle = Indices.Dispose(handle);
                }

                return handle;
            }

            public void Dispose()
            {
                Dispose(default).Complete();
            }
        }

        public struct MeshletGenerationParams
        {
            public uint MaxVertices;
            public uint MaxTriangles;
            public float ConeWeight;
        }
    }
}
