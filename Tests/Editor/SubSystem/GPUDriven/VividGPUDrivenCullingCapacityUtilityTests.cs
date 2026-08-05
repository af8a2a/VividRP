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

            sceneData.AddInstance(
                new VividInstanceData
                {
                    TotalMeshLODCount = 1,
                },
                maxVisibleMeshletRenderRequestCount: 1);
            sceneData.AddInstance(
                new VividInstanceData
                {
                    TotalMeshLODCount = maxNodesPerJob + 1,
                },
                maxVisibleMeshletRenderRequestCount: 1);

            int jobCount = VividGPUDrivenCullingCapacityUtility.GetMaxMeshletListBuildJobCount(sceneData);

            Assert.That(jobCount, Is.EqualTo(3));
        }

        [Test]
        public void GetMaxVisibleMeshletRenderRequestCount_ReturnsSceneDataCachedAggregate()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.AddInstance(default, maxVisibleMeshletRenderRequestCount: 5);
            sceneData.AddInstance(default, maxVisibleMeshletRenderRequestCount: 8);

            int requestCount = VividGPUDrivenCullingCapacityUtility.GetMaxVisibleMeshletRenderRequestCount(sceneData);

            Assert.That(requestCount, Is.EqualTo(13));
        }

        [Test]
        public void ClearInstances_ResetsCachedCullingCapacities()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.AddInstance(
                new VividInstanceData
                {
                    TotalMeshLODCount = VividMeshletListBuildJob.MaxLODNodesPerThreadGroup + 1,
                },
                maxVisibleMeshletRenderRequestCount: 12);

            sceneData.ClearInstances();

            Assert.That(
                VividGPUDrivenCullingCapacityUtility.GetMaxMeshletListBuildJobCount(sceneData),
                Is.EqualTo(1));
            Assert.That(
                VividGPUDrivenCullingCapacityUtility.GetMaxVisibleMeshletRenderRequestCount(sceneData),
                Is.EqualTo(1));
        }
    }
}
