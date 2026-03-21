using System;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
    internal static class VividGPUDrivenCullingCapacityUtility
    {
        public static int GetMaxMeshletListBuildJobCount(VividGPUDrivenSceneData sceneData)
        {
            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            int totalJobCount = 0;
            int maxNodesPerJob = Mathf.Max(1, (int) VividMeshletListBuildJob.MaxLODNodesPerThreadGroup);

            for (int instanceIndex = 0; instanceIndex < sceneData.InstanceCount; instanceIndex++)
            {
                uint totalMeshLODCount = sceneData.Instances[instanceIndex].TotalMeshLODCount;
                if (totalMeshLODCount == 0)
                {
                    continue;
                }

                totalJobCount += (int) ((totalMeshLODCount + (uint) maxNodesPerJob - 1) / (uint) maxNodesPerJob);
            }

            return Mathf.Max(1, totalJobCount);
        }

        public static int GetMaxVisibleMeshletRenderRequestCount(VividGPUDrivenSceneData sceneData)
        {
            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            long totalRequestCount = 0;

            for (int instanceIndex = 0; instanceIndex < sceneData.InstanceCount; instanceIndex++)
            {
                VividInstanceData instanceData = sceneData.Instances[instanceIndex];
                int nodeStartIndex = Mathf.Clamp((int) instanceData.TopMeshLODStartIndex, 0, sceneData.MeshLODNodeCount);
                int nodeEndIndex = Mathf.Clamp(
                    nodeStartIndex + (int) instanceData.TotalMeshLODCount,
                    nodeStartIndex,
                    sceneData.MeshLODNodeCount
                );

                for (int nodeIndex = nodeStartIndex; nodeIndex < nodeEndIndex; nodeIndex++)
                {
                    totalRequestCount += sceneData.MeshLODNodes[nodeIndex].MeshletCount;
                }
            }

            return Mathf.Max(1, totalRequestCount > int.MaxValue ? int.MaxValue : (int) totalRequestCount);
        }
    }
}
