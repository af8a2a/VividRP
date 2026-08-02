using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;

namespace VividRP.Runtime
{
    public static class VividMETIS
    {
        public static unsafe NativeArray<METISOptions> CreateOptions(Allocator allocator)
        {
            var options = new NativeArray<METISOptions>(METISBindings.METIS_NOPTIONS, allocator, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemSet(options.GetUnsafePtr(), 0xFF, options.Length * sizeof(METISOptions));
            return options;
        }

        public static unsafe METISStatus PartGraphKway(GraphAdjacencyStructure graphAdjacencyStructure, Allocator allocator, int partitions,
            NativeArray<METISOptions> options, out NativeArray<int> vertexPartitioning)
        {
            Assert.IsTrue(partitions > 1);
            graphAdjacencyStructure.Validate();

            int numConstraints = 1;
            int edgeCut = 0;
            int* adjacencyIndex = (int*) graphAdjacencyStructure.AdjacencyIndexList.GetUnsafePtr();
            int* adjacencyList = (int*) graphAdjacencyStructure.AdjacencyList.GetUnsafePtr();
            int* adjacencyWeights = graphAdjacencyStructure.AdjacencyWeightList.IsCreated
                ? (int*) graphAdjacencyStructure.AdjacencyWeightList.GetUnsafePtr()
                : null;

            vertexPartitioning = new NativeArray<int>(graphAdjacencyStructure.VertexCount, allocator, NativeArrayOptions.UninitializedMemory);

            METISStatus status = METISBindings.METIS_PartGraphKway(
                &graphAdjacencyStructure.VertexCount,
                &numConstraints,
                adjacencyIndex,
                adjacencyList,
                null,
                null,
                adjacencyWeights,
                &partitions,
                null,
                null,
                (METISOptions*) options.GetUnsafeReadOnlyPtr(),
                &edgeCut,
                (int*) vertexPartitioning.GetUnsafePtr()
            );

            if (status != METISStatus.METIS_OK)
            {
                vertexPartitioning.Dispose();
                vertexPartitioning = default;
            }

            return status;
        }

        public struct GraphAdjacencyStructure
        {
            public int VertexCount;
            public NativeArray<int> AdjacencyIndexList;
            public NativeArray<int> AdjacencyList;
            public NativeArray<int> AdjacencyWeightList;

            public void Validate()
            {
                Assert.IsTrue(VertexCount >= 1);
                Assert.IsTrue(AdjacencyIndexList.IsCreated);
                Assert.IsTrue(AdjacencyIndexList.Length == VertexCount + 1);
                Assert.IsTrue(AdjacencyList.IsCreated);

                foreach (int adjacencyIndex in AdjacencyIndexList)
                {
                    Assert.IsTrue(adjacencyIndex <= AdjacencyList.Length);
                }

                foreach (int adjacency in AdjacencyList)
                {
                    Assert.IsTrue(adjacency >= 0);
                    Assert.IsTrue(adjacency < VertexCount);
                }

                if (AdjacencyWeightList.IsCreated)
                {
                    Assert.IsTrue(AdjacencyWeightList.Length == AdjacencyList.Length);
                }
            }
        }
    }
}
