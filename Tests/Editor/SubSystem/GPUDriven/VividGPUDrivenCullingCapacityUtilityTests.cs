using NUnit.Framework;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenCullingCapacityUtilityTests
    {
        [Test]
        public void GetMaxMeshletListBuildJobCount_SumsPerInstanceDispatchChunks_WhenSceneHasMultipleInstances()
        {
            var sceneData = new VividGPUDrivenSceneData();
            uint maxNodesPerJob = VividMeshletListBuildJob.MaxLODNodesPerThreadGroup;

            sceneData.MutableInstances.Add(new VividInstanceData
            {
                TotalMeshLODCount = 1,
            });
            sceneData.MutableInstances.Add(new VividInstanceData
            {
                TotalMeshLODCount = maxNodesPerJob + 1,
            });

            int jobCount = VividGPUDrivenCullingCapacityUtility.GetMaxMeshletListBuildJobCount(sceneData);

            Assert.That(jobCount, Is.EqualTo(3));
        }

        [Test]
        public void GetMaxVisibleMeshletRenderRequestCount_SumsReferencedNodeMeshletCounts_WhenInstancesReferenceNodeRanges()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletCount = 2,
            });
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletCount = 3,
            });
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletCount = 5,
            });

            sceneData.MutableInstances.Add(new VividInstanceData
            {
                TopMeshLODStartIndex = 0,
                TotalMeshLODCount = 2,
            });
            sceneData.MutableInstances.Add(new VividInstanceData
            {
                TopMeshLODStartIndex = 1,
                TotalMeshLODCount = 2,
            });

            int requestCount = VividGPUDrivenCullingCapacityUtility.GetMaxVisibleMeshletRenderRequestCount(sceneData);

            Assert.That(requestCount, Is.EqualTo(13));
        }
    }
}
