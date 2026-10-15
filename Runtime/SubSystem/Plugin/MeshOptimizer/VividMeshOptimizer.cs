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
        public const float DefaultNormalWeight = 0.1f;
        public const float DefaultUVWeight = 0.05f;

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
                OwnedMeshlets = meshlets,
                OwnedVertices = meshletVertices,
                OwnedIndices = meshletIndices,
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

        public static MeshletBuildResults SimplifyMeshlets(Allocator allocator, NativeArray<MeshletBuildResults> meshletGroups,
            in VertexLayout vertexLayout, in MeshletGenerationParams meshletGenerationParams, SimplifyMode simplifyMode, float targetError,
            out float resultError)
        {
            return SimplifyMeshlets(allocator, meshletGroups, vertexLayout, meshletGenerationParams, simplifyMode, targetError,
                out resultError, default);
        }

        public static unsafe MeshletBuildResults SimplifyMeshlets(Allocator allocator, NativeArray<MeshletBuildResults> meshletGroups,
            in VertexLayout vertexLayout, in MeshletGenerationParams meshletGenerationParams, SimplifyMode simplifyMode, float targetError,
            out float resultError, NativeArray<byte> vertexLocks)
        {
            bool useNormals = vertexLayout.Normals.IsCreated && vertexLayout.NormalWeight > 0f;
            bool useUV = vertexLayout.UV.IsCreated && vertexLayout.UVWeight > 0f;
            if (!math.isfinite(vertexLayout.NormalWeight) || vertexLayout.NormalWeight < 0f
                || !math.isfinite(vertexLayout.UVWeight) || vertexLayout.UVWeight < 0f)
                throw new ArgumentOutOfRangeException(nameof(vertexLayout), "Attribute weights must be finite and nonnegative.");
            // Sloppy ignores attributes. Never silently bypass an appearance constraint.
            if ((useNormals || useUV) && simplifyMode == SimplifyMode.Sloppy)
                throw new ArgumentException("Sloppy simplification does not support attribute weights.", nameof(simplifyMode));
            using var localVertices = new NativeList<ClusterVertex>(Allocator.Temp);
            using var vertexRemap = new NativeHashMap<uint, uint>(128, Allocator.Temp);
            using var localLocks = new NativeList<byte>(Allocator.Temp);
            using var localVerticesGlobalIndices = new NativeList<uint>(Allocator.Temp);
            using var localIndices = new NativeList<uint>(Allocator.Temp);

            byte* pVertexPositionsBytes = (byte*) vertexLayout.Vertices.GetUnsafeReadOnlyPtr() + vertexLayout.PositionOffset;
            byte* normals = useNormals ? (byte*) vertexLayout.Normals.GetUnsafeReadOnlyPtr() + vertexLayout.NormalOffset : null;
            byte* uv = useUV ? (byte*) vertexLayout.UV.GetUnsafeReadOnlyPtr() + vertexLayout.UVOffset : null;

            foreach (MeshletBuildResults group in meshletGroups)
            {
                foreach (meshopt_Meshlet meshlet in group.Meshlets)
                {
                    // Merge meshlet-local duplicates by source ID, without welding attribute seams.
                    for (uint i = 0; i < meshlet.TriangleCount * 3; i++)
                    {
                        uint globalIndex = group.Vertices[(int) meshlet.VertexOffset + group.Indices[(int) (meshlet.TriangleOffset + i)]];
                        if (!vertexRemap.TryGetValue(globalIndex, out uint localIndex))
                        {
                            localIndex = (uint) localVertices.Length;
                            vertexRemap.Add(globalIndex, localIndex);
                            float3 normal = useNormals ? *(float3*) (normals + globalIndex * vertexLayout.NormalStride) : default;
                            float2 texcoord = useUV ? *(float2*) (uv + globalIndex * vertexLayout.UVStride) : default;
                            if (!math.all(math.isfinite(normal)) || !math.all(math.isfinite(texcoord)))
                                throw new ArgumentException("Simplification attributes must be finite.", nameof(vertexLayout));
                            localVertices.Add(new ClusterVertex
                            {
                                Position = *(float3*) (pVertexPositionsBytes + globalIndex * vertexLayout.PositionStride),
                                Normal = math.normalizesafe(normal),
                                UV = texcoord,
                            });
                            localVerticesGlobalIndices.Add(globalIndex);
                            localLocks.Add(vertexLocks.IsCreated ? vertexLocks[(int) globalIndex] : (byte) 0);
                        }
                        localIndices.Add(localIndex);
                    }
                }
            }

            int targetIndexCount = (localIndices.Length / 3 / 2) * 3;
            using var simplifiedIndices = new NativeArray<uint>(localIndices.Length, Allocator.Temp);
            float resultErrorValue = 0.0f;

            uint* pDestination = (uint*) simplifiedIndices.GetUnsafePtr();
            nuint indexCount = (nuint) localIndices.Length;
            float* pVertexPositions = (float*) localVertices.GetUnsafePtr();
            nuint vertexCount = (nuint) localVertices.Length;
            nuint vertexPositionsStride = (nuint) UnsafeUtility.SizeOf<ClusterVertex>();
            float normalWeight = useNormals ? vertexLayout.NormalWeight : 0f;
            float uvWeight = useUV ? vertexLayout.UVWeight : 0f;
            float* weights = stackalloc float[5] { normalWeight, normalWeight, normalWeight, uvWeight, uvWeight };
            bool useAttributes = useNormals || useUV;
            int simplifiedIndexCount = simplifyMode switch
            {
                SimplifyMode.Normal => (int) meshopt_simplifyWithAttributes(
                    pDestination,
                    localIndices.GetUnsafePtr(),
                    indexCount,
                    pVertexPositions,
                    vertexCount,
                    vertexPositionsStride,
                    useAttributes ? pVertexPositions + 3 : null, useAttributes ? vertexPositionsStride : 0,
                    useAttributes ? weights : null, useAttributes ? 5u : 0u, localLocks.GetUnsafePtr(),
                    (nuint) targetIndexCount,
                    targetError,
                    (uint) meshopt_SimplifyOptions.LockBorder,
                    &resultErrorValue
                ),
                SimplifyMode.Sloppy => (int) meshopt_simplifySloppy(
                    pDestination,
                    localIndices.GetUnsafePtr(),
                    indexCount,
                    pVertexPositions,
                    vertexCount,
                    vertexPositionsStride,
                    localLocks.GetUnsafePtr(),
                    (nuint) targetIndexCount,
                    targetError,
                    &resultErrorValue
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(simplifyMode), simplifyMode, null),
            };

            // With attributes this is the combined position/appearance metric, not a pure
            // Hausdorff distance. Both APIs return relative error; LOD consumes object units.
            resultErrorValue *= meshopt_simplifyScale(pVertexPositions, vertexCount, vertexPositionsStride);
            // An empty representation cannot replace a child group: retain its geometry.
            if (simplifiedIndexCount == 0)
            {
                resultErrorValue = 0.0f;
            }

            using var globalIndices = new NativeList<uint>(localIndices.Length, Allocator.Temp);
            NativeArray<uint> resultIndices = simplifiedIndexCount > 0
                ? simplifiedIndices.GetSubArray(0, simplifiedIndexCount)
                : localIndices.AsArray();
            foreach (uint localIndex in resultIndices)
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
            public NativeArray<float> Normals;
            public uint NormalOffset;
            public uint NormalStride;
            public float NormalWeight;
            public float UVWeight;

            public bool HasWeightedAttributes => (Normals.IsCreated && NormalWeight > 0f)
                || (UV.IsCreated && UVWeight > 0f);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClusterVertex
        {
            public float3 Position;
            public float3 Normal;
            public float2 UV;
        }

        public struct MeshletBuildResults : IDisposable
        {
            public NativeArray<meshopt_Meshlet> Meshlets;
            public NativeArray<uint> Vertices;
            public NativeArray<byte> Indices;

            // Public arrays are bounded views; keep the actual allocations for disposal.
            internal NativeArray<meshopt_Meshlet> OwnedMeshlets;
            internal NativeArray<uint> OwnedVertices;
            internal NativeArray<byte> OwnedIndices;

            public JobHandle Dispose(JobHandle inputDeps)
            {
                JobHandle handle = inputDeps;
                if (Meshlets.IsCreated)
                {
                    handle = (OwnedMeshlets.IsCreated ? OwnedMeshlets : Meshlets).Dispose(handle);
                }

                if (Vertices.IsCreated)
                {
                    handle = (OwnedVertices.IsCreated ? OwnedVertices : Vertices).Dispose(handle);
                }

                if (Indices.IsCreated)
                {
                    handle = (OwnedIndices.IsCreated ? OwnedIndices : Indices).Dispose(handle);
                }

                Meshlets = default;
                Vertices = default;
                Indices = default;
                OwnedMeshlets = default;
                OwnedVertices = default;
                OwnedIndices = default;
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
