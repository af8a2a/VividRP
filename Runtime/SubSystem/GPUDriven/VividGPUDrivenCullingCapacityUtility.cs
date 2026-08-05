using System;

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

            return sceneData.MaxMeshletListBuildJobCount;
        }

        public static int GetMaxVisibleMeshletRenderRequestCount(VividGPUDrivenSceneData sceneData)
        {
            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            return sceneData.MaxVisibleMeshletRenderRequestCount;
        }
    }
}
