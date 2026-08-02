using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    internal static partial class VividMeshletCollectionBuilder
    {
        public static unsafe void Generate(VividMeshletCollectionAsset meshletCollection, in Parameters parameters)
        {
            if (meshletCollection == null)
            {
                throw new ArgumentNullException(nameof(meshletCollection));
            }

            if (parameters.Mesh == null)
            {
                throw new ArgumentNullException(nameof(parameters.Mesh));
            }

            if (parameters.Mesh.subMeshCount <= 0)
            {
                throw new InvalidOperationException($"Mesh '{parameters.Mesh.name}' has no submeshes.");
            }

            int subMeshIndex = math.clamp(parameters.SubMeshIndex, 0, parameters.Mesh.subMeshCount - 1);

            meshletCollection.SourceMeshGUID = parameters.SourceMeshGUID ?? string.Empty;
            meshletCollection.SourceMeshLocalFileID = parameters.SourceMeshLocalFileID;
            meshletCollection.SourceMeshName = parameters.Mesh.name;
            meshletCollection.SourceSubmeshIndex = subMeshIndex;

            using Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(parameters.Mesh);
            Mesh.MeshData data = dataArray[0];
            SubMeshDescriptor subMeshDescriptor = data.GetSubMesh(subMeshIndex);
            if (subMeshDescriptor.indexCount <= 0)
            {
                throw new InvalidOperationException($"Submesh {subMeshIndex} on mesh '{parameters.Mesh.name}' has no indices.");
            }

            meshletCollection.Bounds = subMeshDescriptor.bounds;

            int positionStream = data.GetVertexAttributeStream(VertexAttribute.Position);
            if (positionStream < 0)
            {
                throw new InvalidOperationException($"Mesh '{parameters.Mesh.name}' is missing position data.");
            }

            uint positionStride = (uint) data.GetVertexBufferStride(positionStream);
            uint positionOffset = (uint) data.GetVertexAttributeOffset(VertexAttribute.Position);
            NativeArray<float> positionData = data.GetVertexData<float>(positionStream);

            int normalStream = data.GetVertexAttributeStream(VertexAttribute.Normal);
            NativeArray<float> normalData = normalStream >= 0 ? data.GetVertexData<float>(normalStream) : default;
            uint normalStride = normalStream >= 0 ? (uint) data.GetVertexBufferStride(normalStream) : 0u;
            uint normalOffset = GetVertexAttributeOffsetOrMax(data, VertexAttribute.Normal);

            int tangentStream = data.GetVertexAttributeStream(VertexAttribute.Tangent);
            NativeArray<float> tangentData = tangentStream >= 0 ? data.GetVertexData<float>(tangentStream) : default;
            uint tangentStride = tangentStream >= 0 ? (uint) data.GetVertexBufferStride(tangentStream) : 0u;
            uint tangentOffset = GetVertexAttributeOffsetOrMax(data, VertexAttribute.Tangent);

            int uvStream = data.GetVertexAttributeStream(VertexAttribute.TexCoord0);
            NativeArray<float> uvData = uvStream >= 0 ? data.GetVertexData<float>(uvStream) : default;
            uint uvStride = uvStream >= 0 ? (uint) data.GetVertexBufferStride(uvStream) : 0u;
            uint uvOffset = GetVertexAttributeOffsetOrMax(data, VertexAttribute.TexCoord0);

            NativeArray<uint> allIndexData = CopyIndexDataToUInt32(data, Allocator.TempJob);
            NativeArray<uint> subMeshIndices = allIndexData.GetSubArray(subMeshDescriptor.indexStart, subMeshDescriptor.indexCount);
            if (subMeshDescriptor.baseVertex != 0)
            {
                uint baseVertex = (uint) subMeshDescriptor.baseVertex;
                for (int index = 0; index < subMeshIndices.Length; index++)
                {
                    subMeshIndices[index] += baseVertex;
                }
            }

            NativeArray<uint> workingIndices = subMeshIndices;
            bool ownsWorkingIndices = false;
            if (parameters.OptimizeVertexCache)
            {
                workingIndices = VividMeshOptimizer.OptimizeVertexCache(Allocator.TempJob, subMeshIndices, (uint) data.vertexCount);
                ownsWorkingIndices = true;
            }

            VividMeshOptimizer.MeshletGenerationParams generationParams = VividMeshletCollectionAsset.MeshletGenerationParams;
            const Allocator allocator = Allocator.TempJob;
            VividMeshOptimizer.MeshletBuildResults mainBuildResults = VividMeshOptimizer.BuildMeshlets(
                allocator,
                positionData,
                positionOffset,
                positionStride,
                workingIndices,
                generationParams
            );

            var meshLODLevels = new NativeList<MeshLODNodeLevel>(allocator);
            var topLOD = new MeshLODNodeLevel
            {
                Nodes = new NativeArray<MeshLODNode>(mainBuildResults.Meshlets.Length, allocator),
                MeshletsNodeLists = new NativeArray<MeshLODNodeLevel.MeshletNodeList>(1, allocator)
                {
                    [0] = new MeshLODNodeLevel.MeshletNodeList
                    {
                        MeshletBuildResults = mainBuildResults,
                    },
                },
            };

            for (int meshletIndex = 0; meshletIndex < topLOD.Nodes.Length; meshletIndex++)
            {
                VividMeshOptimizer.MeshletBuildResults meshletBuildResults = topLOD.MeshletsNodeLists[0].MeshletBuildResults;
                meshopt_Meshlet meshlet = meshletBuildResults.Meshlets[meshletIndex];
                meshopt_Bounds bounds = VividMeshOptimizer.ComputeMeshletBounds(
                    meshletBuildResults,
                    meshletIndex,
                    positionData,
                    positionOffset,
                    positionStride
                );

                topLOD.TriangleCount += meshlet.TriangleCount;
                topLOD.Nodes[meshletIndex] = new MeshLODNode
                {
                    MeshletNodeListIndex = 0,
                    MeshletIndex = meshletIndex,
                    ChildGroupIndex = -1,
                    Error = 0.0f,
                    Bounds = math.float4(bounds.Center[0], bounds.Center[1], bounds.Center[2], bounds.Radius),
                };
            }

            meshLODLevels.Add(topLOD);

            var vertexLayout = new VividMeshOptimizer.VertexLayout
            {
                Vertices = positionData,
                PositionOffset = positionOffset,
                PositionStride = positionStride,
                UV = uvData,
                UVOffset = uvOffset,
                UVStride = uvStride,
            };
            if (parameters.MaxMeshLODLevelCount == 1)
            {
                FinalizeSingleLODLevel(meshLODLevels, allocator);
            }
            else
            {
                BuildLodGraph(meshLODLevels, allocator, vertexLayout, generationParams, parameters);
            }

            int totalMeshLODNodes = 0;
            int totalMeshlets = 0;
            int totalVertices = 0;
            int totalIndices = 0;

            foreach (MeshLODNodeLevel level in meshLODLevels)
            {
                foreach (NativeList<int> levelGroup in level.Groups)
                {
                    totalMeshLODNodes += levelGroup.Length;
                    totalMeshlets += levelGroup.Length;

                    foreach (int nodeIndex in levelGroup)
                    {
                        MeshLODNode node = level.Nodes[nodeIndex];
                        MeshLODNodeLevel.MeshletNodeList meshletNodeList = level.MeshletsNodeLists[node.MeshletNodeListIndex];
                        VividMeshOptimizer.MeshletBuildResults meshletBuildResults = meshletNodeList.MeshletBuildResults;
                        meshopt_Meshlet meshlet = meshletBuildResults.Meshlets[node.MeshletIndex];
                        totalVertices += (int) meshlet.VertexCount;
                        totalIndices += (int) (meshlet.TriangleCount * 3);
                    }
                }
            }

            if (totalMeshLODNodes > VividMeshletComputeShaders.MaxMeshLODNodesPerInstance)
            {
                parameters.LogErrorHandler?.Invoke(
                    $"Mesh LOD node count exceeds the limit: {totalMeshLODNodes}/{VividMeshletComputeShaders.MaxMeshLODNodesPerInstance}."
                );
            }

            meshletCollection.LeafMeshletCount = mainBuildResults.Meshlets.Length;
            meshletCollection.MeshLODLevelCount = meshLODLevels.Length;

            var meshLODLevelNodeCounts = new int[meshLODLevels.Length];
            var meshLODNodes = new VividMeshLODNode[totalMeshLODNodes];
            var meshlets = new VividMeshlet[totalMeshlets];
            var vertexBuffer = new VividMeshletVertex[totalVertices];
            var indexBuffer = new byte[totalIndices];

            var jobHandles = new NativeList<JobHandle>(Allocator.Temp);

            fixed (VividMeshLODNode* pMeshLODNodes = meshLODNodes)
            fixed (VividMeshlet* pDestinationMeshlets = meshlets)
            fixed (VividMeshletVertex* pDestinationVertices = vertexBuffer)
            fixed (byte* pIndexBuffer = indexBuffer)
            {
                byte* pPositionData = (byte*) positionData.GetUnsafeReadOnlyPtr();
                byte* pNormalData = normalData.IsCreated ? (byte*) normalData.GetUnsafeReadOnlyPtr() : null;
                byte* pTangentData = tangentData.IsCreated ? (byte*) tangentData.GetUnsafeReadOnlyPtr() : null;
                byte* pUVData = uvData.IsCreated ? (byte*) uvData.GetUnsafeReadOnlyPtr() : null;

                uint meshLODNodeWriteOffset = 0;
                uint meshletWriteOffset = 0;
                uint vertexWriteOffset = 0;
                uint indexWriteOffset = 0;

                for (int levelIndex = 0; levelIndex < meshLODLevels.Length; levelIndex++)
                {
                    MeshLODNodeLevel level = meshLODLevels[levelIndex];
                    int levelMeshLODNodeCount = 0;
                    foreach (NativeList<int> group in level.Groups)
                    {
                        levelMeshLODNodeCount += group.Length;
                    }

                    meshLODLevelNodeCounts[levelIndex] = levelMeshLODNodeCount;

                    foreach (NativeList<int> group in level.Groups)
                    {
                        foreach (int nodeIndex in group)
                        {
                            if (levelIndex != 0)
                            {
                                Assert.IsTrue(level.Nodes[nodeIndex].Error <= level.Nodes[nodeIndex].ParentError);
                            }
                        }

                        for (int index = 0; index < group.Length; index++)
                        {
                            int nodeIndex = group[index];
                            MeshLODNode node = level.Nodes[nodeIndex];

                            ref VividMeshLODNode destinationNode = ref pMeshLODNodes[meshLODNodeWriteOffset++];
                            destinationNode = new VividMeshLODNode
                            {
                                MeshletCount = 1u,
                                MeshletStartIndex = meshletWriteOffset + (uint) index,
                                LevelIndex = (uint) levelIndex,
                                Error = node.Error,
                                Bounds = node.Bounds,
                                ParentError = node.ParentError,
                                ParentBounds = node.ParentBounds,
                            };
                        }

                        foreach (int nodeIndex in group)
                        {
                            MeshLODNode node = level.Nodes[nodeIndex];
                            MeshLODNodeLevel.MeshletNodeList meshletNodeList = level.MeshletsNodeLists[node.MeshletNodeListIndex];
                            VividMeshOptimizer.MeshletBuildResults meshletBuildResults = meshletNodeList.MeshletBuildResults;
                            ref readonly meshopt_Meshlet meshoptMeshlet = ref meshletBuildResults.Meshlets.ElementAtRefReadonly(node.MeshletIndex);
                            meshopt_Bounds meshoptBounds = VividMeshOptimizer.ComputeMeshletBounds(
                                meshletBuildResults,
                                node.MeshletIndex,
                                positionData,
                                positionOffset,
                                positionStride
                            );

                            pDestinationMeshlets[meshletWriteOffset++] = new VividMeshlet
                            {
                                VertexOffset = vertexWriteOffset,
                                TriangleOffset = indexWriteOffset,
                                VertexCount = meshoptMeshlet.VertexCount,
                                TriangleCount = meshoptMeshlet.TriangleCount,
                                BoundingSphere = math.float4(
                                    meshoptBounds.Center[0],
                                    meshoptBounds.Center[1],
                                    meshoptBounds.Center[2],
                                    meshoptBounds.Radius
                                ),
                                ConeApexCutoff = math.float4(
                                    meshoptBounds.ConeApex[0],
                                    meshoptBounds.ConeApex[1],
                                    meshoptBounds.ConeApex[2],
                                    meshoptBounds.ConeCutoff
                                ),
                                ConeAxis = math.float4(
                                    meshoptBounds.ConeAxis[0],
                                    meshoptBounds.ConeAxis[1],
                                    meshoptBounds.ConeAxis[2],
                                    0.0f
                                ),
                            };

                            jobHandles.Add(new WriteVerticesJob
                            {
                                PositionPtr = pPositionData,
                                PositionStride = positionStride,
                                PositionOffset = positionOffset,
                                NormalPtr = pNormalData,
                                NormalStride = normalStride,
                                NormalOffset = normalOffset,
                                TangentPtr = pTangentData,
                                TangentStride = tangentStride,
                                TangentOffset = tangentOffset,
                                UVPtr = pUVData,
                                UVStride = uvStride,
                                UVOffset = uvOffset,
                                MeshletBuildResults = meshletBuildResults,
                                MeshletIndex = node.MeshletIndex,
                                DestinationPtr = pDestinationVertices + vertexWriteOffset,
                            }.Schedule((int) meshoptMeshlet.VertexCount, WriteVerticesJob.BatchSize));

                            vertexWriteOffset += meshoptMeshlet.VertexCount;

                            uint indexCount = meshoptMeshlet.TriangleCount * 3;
                            UnsafeUtility.MemCpy(
                                pIndexBuffer + indexWriteOffset,
                                (byte*) meshletBuildResults.Indices.GetUnsafeReadOnlyPtr() + meshoptMeshlet.TriangleOffset,
                                indexCount * sizeof(byte)
                            );
                            indexWriteOffset += indexCount;
                        }
                    }
                }
            }

            if (jobHandles.Length > 0)
            {
                JobHandle.CombineDependencies(jobHandles.AsArray()).Complete();
            }
            jobHandles.Dispose();

            meshletCollection.SetMeshData(
                meshLODLevelNodeCounts,
                meshLODNodes,
                meshlets,
                vertexBuffer,
                indexBuffer
            );
            meshletCollection.MarkChanged();

            if (ownsWorkingIndices)
            {
                workingIndices.Dispose();
            }
            allIndexData.Dispose();

            foreach (MeshLODNodeLevel level in meshLODLevels)
            {
                level.Dispose();
            }
            meshLODLevels.Dispose();
        }

        private static NativeArray<uint> CopyIndexDataToUInt32(Mesh.MeshData data, Allocator allocator)
        {
            if (data.indexFormat == IndexFormat.UInt16)
            {
                NativeArray<ushort> sourceIndices = data.GetIndexData<ushort>();
                var result = new NativeArray<uint>(sourceIndices.Length, allocator);
                for (int index = 0; index < sourceIndices.Length; index++)
                {
                    result[index] = sourceIndices[index];
                }

                return result;
            }

            NativeArray<uint> sourceIndexData = data.GetIndexData<uint>();
            var copiedIndices = new NativeArray<uint>(sourceIndexData.Length, allocator, NativeArrayOptions.UninitializedMemory);
            copiedIndices.CopyFrom(sourceIndexData);
            return copiedIndices;
        }

        private static uint GetVertexAttributeOffsetOrMax(Mesh.MeshData data, VertexAttribute attribute)
        {
            return data.GetVertexAttributeStream(attribute) >= 0 ? (uint) data.GetVertexAttributeOffset(attribute) : uint.MaxValue;
        }

        private static void FinalizeSingleLODLevel(NativeList<MeshLODNodeLevel> levels, Allocator allocator)
        {
            Assert.IsTrue(levels.Length == 1);
            ref MeshLODNodeLevel level = ref levels.ElementAtRef(0);
            level.Groups = new NativeArray<NativeList<int>>(1, allocator);
            var group = new NativeList<int>(level.Nodes.Length, allocator);
            for (int nodeIndex = 0; nodeIndex < level.Nodes.Length; nodeIndex++)
            {
                group.Add(nodeIndex);
                ref MeshLODNode node = ref level.Nodes.ElementAtRef(nodeIndex);
                node.ParentError = -1.0f;
                node.ParentBounds = default;
            }

            level.Groups[0] = group;
        }

        private static void BuildLodGraph(NativeList<MeshLODNodeLevel> levels, Allocator allocator, in VividMeshOptimizer.VertexLayout vertexLayout,
            VividMeshOptimizer.MeshletGenerationParams meshletGenerationParams, in Parameters parameters)
        {
            VividMeshOptimizer.SimplifyMode simplifyMode = VividMeshOptimizer.SimplifyMode.Normal;

            while (levels[^1].Nodes.Length > 1)
            {
                ref MeshLODNodeLevel previousLevel = ref levels.ElementAt(levels.Length - 1);
                if (previousLevel.Nodes.Length < 2)
                {
                    break;
                }

                var newLevelNodes = new NativeList<MeshLODNode>(previousLevel.Nodes.Length / 2, Allocator.TempJob);
                var meshletNodeLists = new NativeList<MeshLODNodeLevel.MeshletNodeList>(previousLevel.MeshletsNodeLists.Length / 2, Allocator.TempJob);
                uint newTriangleCount = 0;
                const int meshletsPerGroup = 4;

                NativeArray<NativeList<int>> childMeshletGroups = GroupMeshlets(previousLevel, meshletsPerGroup, Allocator.TempJob);

                for (int childGroupIndex = 0; childGroupIndex < childMeshletGroups.Length; childGroupIndex++)
                {
                    NativeList<int> sourceMeshletGroup = childMeshletGroups[childGroupIndex];
                    var sourceMeshlets = new NativeList<VividMeshOptimizer.MeshletBuildResults>(sourceMeshletGroup.Length, Allocator.Temp);
                    float sourceError = 0.0f;
                    float3 sourceBoundsMin = float.PositiveInfinity;
                    float3 sourceBoundsMax = float.NegativeInfinity;

                    foreach (int nodeIndex in sourceMeshletGroup)
                    {
                        MeshLODNode node = previousLevel.Nodes[nodeIndex];
                        sourceError = math.max(sourceError, node.Error);

                        VividMeshOptimizer.MeshletBuildResults meshletBuildResults = previousLevel.MeshletsNodeLists[node.MeshletNodeListIndex].MeshletBuildResults;
                        meshletBuildResults.Meshlets = meshletBuildResults.Meshlets.GetSubArray(node.MeshletIndex, 1);
                        sourceMeshlets.Add(meshletBuildResults);

                        sourceBoundsMin = math.min(sourceBoundsMin, node.Bounds.xyz - node.Bounds.w);
                        sourceBoundsMax = math.max(sourceBoundsMax, node.Bounds.xyz + node.Bounds.w);
                    }

                    float3 sourceBoundsCenter = (sourceBoundsMin + sourceBoundsMax) * 0.5f;
                    float sourceBoundsRadius = math.length(sourceBoundsCenter - sourceBoundsMin);
                    float4 sourceBounds = math.float4(sourceBoundsCenter, sourceBoundsRadius);
                    float targetError = simplifyMode == VividMeshOptimizer.SimplifyMode.Sloppy ? parameters.TargetErrorSloppy : parameters.TargetError;

                    VividMeshOptimizer.MeshletBuildResults simplifiedMeshlets = VividMeshOptimizer.SimplifyMeshlets(
                        allocator,
                        sourceMeshlets.AsArray(),
                        vertexLayout,
                        meshletGenerationParams,
                        simplifyMode,
                        targetError,
                        out float localError
                    );
                    Assert.IsTrue(localError >= 0.0f);
                    sourceMeshlets.Dispose();

                    const float minSimplificationError = 0.0001f;
                    float error = sourceError + math.max(localError, minSimplificationError);
                    Assert.IsTrue(error > sourceError);

                    float4 bounds = sourceBounds;
                    bounds.w += 0.0001f;

                    for (int meshletIndex = 0; meshletIndex < simplifiedMeshlets.Meshlets.Length; meshletIndex++)
                    {
                        newTriangleCount += simplifiedMeshlets.Meshlets[meshletIndex].TriangleCount;
                        newLevelNodes.Add(new MeshLODNode
                        {
                            MeshletNodeListIndex = meshletNodeLists.Length,
                            MeshletIndex = meshletIndex,
                            ChildGroupIndex = childGroupIndex,
                            Error = error,
                            Bounds = bounds,
                        });
                    }

                    foreach (int nodeIndex in sourceMeshletGroup)
                    {
                        ref MeshLODNode childNode = ref previousLevel.Nodes.ElementAtRef(nodeIndex);
                        childNode.ParentError = error;
                        childNode.ParentBounds = bounds;
                    }

                    meshletNodeLists.Add(new MeshLODNodeLevel.MeshletNodeList
                    {
                        MeshletBuildResults = simplifiedMeshlets,
                    });
                }

                var newMeshLODNodeLevel = new MeshLODNodeLevel
                {
                    TriangleCount = newTriangleCount,
                    Nodes = newLevelNodes.AsArray(),
                    MeshletsNodeLists = meshletNodeLists.AsArray(),
                };

                previousLevel.Groups = childMeshletGroups;

                if (newTriangleCount < previousLevel.TriangleCount * parameters.MinTriangleReductionPerStep)
                {
                    levels.Add(newMeshLODNodeLevel);
                }
                else
                {
                    newMeshLODNodeLevel.Dispose();

                    if (simplifyMode == VividMeshOptimizer.SimplifyMode.Normal)
                    {
                        simplifyMode = VividMeshOptimizer.SimplifyMode.Sloppy;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            for (int index = 0; index < levels.Length / 2; index++)
            {
                ref MeshLODNodeLevel left = ref levels.ElementAtRef(index);
                ref MeshLODNodeLevel right = ref levels.ElementAtRef(levels.Length - 1 - index);
                (left, right) = (right, left);
            }

            for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
            {
                ref MeshLODNodeLevel nodeLevel = ref levels.ElementAtRef(levelIndex);
                if (!nodeLevel.Groups.IsCreated)
                {
                    nodeLevel.Groups = new NativeArray<NativeList<int>>(1, Allocator.TempJob);
                    var group = new NativeList<int>(nodeLevel.Nodes.Length, Allocator.TempJob);
                    for (int nodeIndex = 0; nodeIndex < nodeLevel.Nodes.Length; nodeIndex++)
                    {
                        group.Add(nodeIndex);
                    }

                    nodeLevel.Groups[0] = group;
                }
            }

            if (parameters.MaxMeshLODLevelCount > 0)
            {
                while (levels.Length > parameters.MaxMeshLODLevelCount)
                {
                    int lastIndex = levels.Length - 1;
                    MeshLODNodeLevel level = levels[lastIndex];
                    level.Dispose();
                    levels.RemoveAt(lastIndex);
                }
            }

            MeshLODNodeLevel firstLevel = levels[0];
            for (int index = 0; index < firstLevel.Nodes.Length; index++)
            {
                ref MeshLODNode node = ref firstLevel.Nodes.ElementAtRef(index);
                node.ParentError = -1.0f;
                node.ParentBounds = default;
            }
        }

        public struct Parameters
        {
            public Mesh Mesh;
            public string SourceMeshGUID;
            public long SourceMeshLocalFileID;
            public int SubMeshIndex;
            public Action<string> LogErrorHandler;
            public bool OptimizeVertexCache;
            public int MaxMeshLODLevelCount;
            public float TargetError;
            public float TargetErrorSloppy;
            public float MinTriangleReductionPerStep;
        }

        private struct MeshLODNode
        {
            public int MeshletNodeListIndex;
            public int MeshletIndex;
            public int ChildGroupIndex;
            public float4 Bounds;
            public float Error;
            public float4 ParentBounds;
            public float ParentError;
        }

        private struct MeshLODNodeLevel : IDisposable
        {
            public NativeArray<MeshLODNode> Nodes;
            public NativeArray<MeshletNodeList> MeshletsNodeLists;
            public NativeArray<NativeList<int>> Groups;
            public uint TriangleCount;

            public void Dispose()
            {
                if (MeshletsNodeLists.IsCreated)
                {
                    foreach (MeshletNodeList meshletNodeList in MeshletsNodeLists)
                    {
                        MeshletNodeList copy = meshletNodeList;
                        copy.MeshletBuildResults.Dispose();
                    }

                    MeshletsNodeLists.Dispose();
                }

                if (Nodes.IsCreated)
                {
                    Nodes.Dispose();
                }

                if (Groups.IsCreated)
                {
                    foreach (NativeList<int> group in Groups)
                    {
                        if (group.IsCreated)
                        {
                            group.Dispose();
                        }
                    }

                    Groups.Dispose();
                }
            }

            public struct MeshletNodeList
            {
                public VividMeshOptimizer.MeshletBuildResults MeshletBuildResults;
            }
        }

        [BurstCompile]
        private unsafe struct WriteVerticesJob : IJobParallelFor
        {
            public const int BatchSize = 32;

            [NativeDisableUnsafePtrRestriction]
            public byte* PositionPtr;
            public uint PositionStride;
            public uint PositionOffset;

            [NativeDisableUnsafePtrRestriction]
            public byte* NormalPtr;
            public uint NormalStride;
            public uint NormalOffset;

            [NativeDisableUnsafePtrRestriction]
            public byte* TangentPtr;
            public uint TangentStride;
            public uint TangentOffset;

            [NativeDisableUnsafePtrRestriction]
            public byte* UVPtr;
            public uint UVStride;
            public uint UVOffset;

            [NativeDisableContainerSafetyRestriction]
            public VividMeshOptimizer.MeshletBuildResults MeshletBuildResults;

            [NativeDisableUnsafePtrRestriction]
            public VividMeshletVertex* DestinationPtr;
            public int MeshletIndex;

            public void Execute(int index)
            {
                meshopt_Meshlet meshlet = MeshletBuildResults.Meshlets[MeshletIndex];
                uint sourceVertexIndex = MeshletBuildResults.Vertices[(int) (meshlet.VertexOffset + (uint) index)];

                var meshletVertex = new VividMeshletVertex
                {
                    Position = ReadPosition(sourceVertexIndex),
                    Normal = ReadNormal(sourceVertexIndex),
                    Tangent = ReadTangent(sourceVertexIndex),
                    UV = ReadUV(sourceVertexIndex),
                };

                DestinationPtr[index] = meshletVertex;
            }

            private float4 ReadPosition(uint sourceVertexIndex)
            {
                byte* source = PositionPtr + sourceVertexIndex * PositionStride;
                return math.float4(*(float3*) (source + PositionOffset), 1.0f);
            }

            private float4 ReadNormal(uint sourceVertexIndex)
            {
                if (NormalPtr == null || NormalOffset == uint.MaxValue)
                {
                    return default;
                }

                byte* source = NormalPtr + sourceVertexIndex * NormalStride;
                return math.float4(*(float3*) (source + NormalOffset), 0.0f);
            }

            private float4 ReadTangent(uint sourceVertexIndex)
            {
                if (TangentPtr == null || TangentOffset == uint.MaxValue)
                {
                    return default;
                }

                byte* source = TangentPtr + sourceVertexIndex * TangentStride;
                return *(float4*) (source + TangentOffset);
            }

            private float4 ReadUV(uint sourceVertexIndex)
            {
                if (UVPtr == null || UVOffset == uint.MaxValue)
                {
                    return default;
                }

                byte* source = UVPtr + sourceVertexIndex * UVStride;
                return math.float4(*(float2*) (source + UVOffset), 0.0f, 0.0f);
            }
        }
    }
}
