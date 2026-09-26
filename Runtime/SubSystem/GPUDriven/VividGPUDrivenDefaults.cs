namespace VividRP.Runtime.GPUDriven
{
    public static class VividGPUDrivenDefaults
    {
        // SetComputeIntParam preserves these bits as uint 0xffffffff in HLSL.
        // Keep in sync with VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH.
        public const int ForcedMeshLODNodeDepth = -1;
        public const float MeshLODErrorThreshold = 50.0f;
    }
}
