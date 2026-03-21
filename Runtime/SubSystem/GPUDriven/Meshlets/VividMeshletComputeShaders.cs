using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    [GenerateHLSL]
    public static class VividMeshletComputeShaders
    {
        public const uint MaxMeshLODNodesPerInstance = 128 * 128;
        public const uint GPUInstanceCullingThreadGroupSize = 32;
        public const uint MeshletListBuildThreadGroupSize = 32;
        public const uint GPUMeshletCullingThreadGroupSize = 32;
        public const uint HZBGenerationThreadGroupSizeX = 8;
        public const uint HZBGenerationThreadGroupSizeY = 8;
        public const uint HZBMaxLevelCount = 16;
    }

    [GenerateHLSL]
    public struct VividMeshletListBuildJob
    {
        public const uint MaxLODNodesPerThreadGroup = VividMeshletComputeShaders.MeshletListBuildThreadGroupSize;

        public uint InstanceID;
        public uint MeshLODNodeOffset;
        public uint MeshLODNodeCount;
        public uint Padding0;
    }
}
