//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VIVIDMESHLETCOMPUTESHADERS_CS_HLSL
#define VIVIDMESHLETCOMPUTESHADERS_CS_HLSL
//
// VividRP.Runtime.GPUDriven.Meshlets.VividMeshletComputeShaders:  static fields
//
#define MAX_MESH_LODNODES_PER_INSTANCE (16384)
#define GPUINSTANCE_CULLING_THREAD_GROUP_SIZE (32)
#define MESHLET_LIST_BUILD_THREAD_GROUP_SIZE (32)
#define GPUMESHLET_CULLING_THREAD_GROUP_SIZE (32)
#define HZBGENERATION_THREAD_GROUP_SIZE_X (8)
#define HZBGENERATION_THREAD_GROUP_SIZE_Y (8)
#define HZBMAX_LEVEL_COUNT (16)

//
// VividRP.Runtime.GPUDriven.Meshlets.VividMeshletListBuildJob:  static fields
//
#define MAX_LODNODES_PER_THREAD_GROUP (32)

// Generated from VividRP.Runtime.GPUDriven.Meshlets.VividMeshletListBuildJob
// PackingRules = Exact
struct VividMeshletListBuildJob
{
    uint InstanceID;
    uint MeshLODNodeOffset;
    uint MeshLODNodeCount;
    uint Padding0;
};

//
// Accessors for VividRP.Runtime.GPUDriven.Meshlets.VividMeshletListBuildJob
//
uint GetInstanceID(VividMeshletListBuildJob value)
{
    return value.InstanceID;
}
uint GetMeshLODNodeOffset(VividMeshletListBuildJob value)
{
    return value.MeshLODNodeOffset;
}
uint GetMeshLODNodeCount(VividMeshletListBuildJob value)
{
    return value.MeshLODNodeCount;
}
uint GetPadding0(VividMeshletListBuildJob value)
{
    return value.Padding0;
}

#endif
