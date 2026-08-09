using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class VividGPUDrivenShaderIDs
    {
        public static readonly int _InstanceData = Shader.PropertyToID(nameof(_InstanceData));
        public static readonly int _MaterialData = Shader.PropertyToID(nameof(_MaterialData));
        public static readonly int _SurfaceBindingData = Shader.PropertyToID(nameof(_SurfaceBindingData));
        public static readonly int _TerrainMaterialData = Shader.PropertyToID(nameof(_TerrainMaterialData));
        public static readonly int _TerrainLayerData = Shader.PropertyToID(nameof(_TerrainLayerData));
        public static readonly int _MeshLODNodes = Shader.PropertyToID(nameof(_MeshLODNodes));
        public static readonly int _Meshlets = Shader.PropertyToID(nameof(_Meshlets));
        public static readonly int _SharedVertexBuffer = Shader.PropertyToID(nameof(_SharedVertexBuffer));
        public static readonly int _SharedIndexBuffer = Shader.PropertyToID(nameof(_SharedIndexBuffer));

        public static readonly int _InstanceDataCount = Shader.PropertyToID(nameof(_InstanceDataCount));
        public static readonly int _MaterialDataCount = Shader.PropertyToID(nameof(_MaterialDataCount));
        public static readonly int _SurfaceBindingDataCount = Shader.PropertyToID(nameof(_SurfaceBindingDataCount));
        public static readonly int _TerrainMaterialDataCount = Shader.PropertyToID(nameof(_TerrainMaterialDataCount));
        public static readonly int _TerrainLayerDataCount = Shader.PropertyToID(nameof(_TerrainLayerDataCount));
        public static readonly int _MeshLODNodeCount = Shader.PropertyToID(nameof(_MeshLODNodeCount));
        public static readonly int _MeshletCount = Shader.PropertyToID(nameof(_MeshletCount));
        public static readonly int _SharedVertexCount = Shader.PropertyToID(nameof(_SharedVertexCount));
        public static readonly int _SharedIndexCount = Shader.PropertyToID(nameof(_SharedIndexCount));

        public static readonly int _CullingContexts = Shader.PropertyToID(nameof(_CullingContexts));
        public static readonly int _LODSelectionContexts = Shader.PropertyToID(nameof(_LODSelectionContexts));
        public static readonly int _MeshletListBuildJobs = Shader.PropertyToID(nameof(_MeshletListBuildJobs));
        public static readonly int _MeshletListBuildJobCounter = Shader.PropertyToID(nameof(_MeshletListBuildJobCounter));
        public static readonly int _MeshletListBuildIndirectArgs = Shader.PropertyToID(nameof(_MeshletListBuildIndirectArgs));
        public static readonly int _CandidateMeshletRenderRequests = Shader.PropertyToID(nameof(_CandidateMeshletRenderRequests));
        public static readonly int _GPUMeshletCullingIndirectDispatchArgs =
            Shader.PropertyToID(nameof(_GPUMeshletCullingIndirectDispatchArgs));
        public static readonly int _VisibleMeshletRenderRequests = Shader.PropertyToID(nameof(_VisibleMeshletRenderRequests));
        public static readonly int _VisibleMeshletRenderRequestCounter = Shader.PropertyToID(nameof(_VisibleMeshletRenderRequestCounter));
        public static readonly int _VisibleRendererListMeshletCounts = Shader.PropertyToID(nameof(_VisibleRendererListMeshletCounts));
        public static readonly int _VisibleMeshletIndirectDrawArgs = Shader.PropertyToID(nameof(_VisibleMeshletIndirectDrawArgs));
        public static readonly int _OccludedMeshletRenderRequests = Shader.PropertyToID(nameof(_OccludedMeshletRenderRequests));
        public static readonly int _OccludedMeshletRenderRequestCounter = Shader.PropertyToID(nameof(_OccludedMeshletRenderRequestCounter));
        public static readonly int _OccludedMeshletIndirectDispatchArgs = Shader.PropertyToID(nameof(_OccludedMeshletIndirectDispatchArgs));
        public static readonly int _RecoveredMeshletRenderRequests = Shader.PropertyToID(nameof(_RecoveredMeshletRenderRequests));
        public static readonly int _RecoveredRendererListMeshletCounts = Shader.PropertyToID(nameof(_RecoveredRendererListMeshletCounts));
        public static readonly int _RecoveredMeshletIndirectDrawArgs = Shader.PropertyToID(nameof(_RecoveredMeshletIndirectDrawArgs));
        public static readonly int _OccluderDepthPyramid = Shader.PropertyToID(nameof(_OccluderDepthPyramid));
        public static readonly int _OccluderDepthPyramidDestination = Shader.PropertyToID(nameof(_OccluderDepthPyramidDestination));
        public static readonly int _InputDepth = Shader.PropertyToID(nameof(_InputDepth));
        public static readonly int _OccluderViewProjectionMatrix = Shader.PropertyToID(nameof(_OccluderViewProjectionMatrix));
        public static readonly int _OccluderDepthPyramidSize = Shader.PropertyToID(nameof(_OccluderDepthPyramidSize));
        public static readonly int _OccluderDepthPyramidTextureSize = Shader.PropertyToID(nameof(_OccluderDepthPyramidTextureSize));
        public static readonly int _OccluderDepthPyramidMipCount = Shader.PropertyToID(nameof(_OccluderDepthPyramidMipCount));
        public static readonly int _OccluderSourceSize = Shader.PropertyToID(nameof(_OccluderSourceSize));
        public static readonly int _OccluderDestinationSize = Shader.PropertyToID(nameof(_OccluderDestinationSize));
        public static readonly int _OcclusionTestMode = Shader.PropertyToID(nameof(_OcclusionTestMode));
        public static readonly int _OcclusionDepthBias = Shader.PropertyToID(nameof(_OcclusionDepthBias));
        public static readonly int _OccludedMeshletCapacity = Shader.PropertyToID(nameof(_OccludedMeshletCapacity));
        public static readonly int _ForcedMeshLODNodeDepth = Shader.PropertyToID(nameof(_ForcedMeshLODNodeDepth));
        public static readonly int _MeshLODErrorThreshold = Shader.PropertyToID(nameof(_MeshLODErrorThreshold));
    }
}
