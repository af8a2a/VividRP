using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class VividGPUDrivenShaderIDs
    {
        public static readonly int _InstanceData = Shader.PropertyToID(nameof(_InstanceData));
        public static readonly int _MaterialData = Shader.PropertyToID(nameof(_MaterialData));
        public static readonly int _MeshLODNodes = Shader.PropertyToID(nameof(_MeshLODNodes));
        public static readonly int _Meshlets = Shader.PropertyToID(nameof(_Meshlets));
        public static readonly int _SharedVertexBuffer = Shader.PropertyToID(nameof(_SharedVertexBuffer));
        public static readonly int _SharedIndexBuffer = Shader.PropertyToID(nameof(_SharedIndexBuffer));

        public static readonly int _InstanceDataCount = Shader.PropertyToID(nameof(_InstanceDataCount));
        public static readonly int _MaterialDataCount = Shader.PropertyToID(nameof(_MaterialDataCount));
        public static readonly int _MeshLODNodeCount = Shader.PropertyToID(nameof(_MeshLODNodeCount));
        public static readonly int _MeshletCount = Shader.PropertyToID(nameof(_MeshletCount));
        public static readonly int _SharedVertexCount = Shader.PropertyToID(nameof(_SharedVertexCount));
        public static readonly int _SharedIndexCount = Shader.PropertyToID(nameof(_SharedIndexCount));

        public static readonly int _CullingContexts = Shader.PropertyToID(nameof(_CullingContexts));
        public static readonly int _LODSelectionContexts = Shader.PropertyToID(nameof(_LODSelectionContexts));
        public static readonly int _MeshletListBuildJobs = Shader.PropertyToID(nameof(_MeshletListBuildJobs));
        public static readonly int _MeshletListBuildJobCounter = Shader.PropertyToID(nameof(_MeshletListBuildJobCounter));
        public static readonly int _MeshletListBuildIndirectArgs = Shader.PropertyToID(nameof(_MeshletListBuildIndirectArgs));
        public static readonly int _VisibleMeshletRenderRequests = Shader.PropertyToID(nameof(_VisibleMeshletRenderRequests));
        public static readonly int _VisibleMeshletRenderRequestCounter = Shader.PropertyToID(nameof(_VisibleMeshletRenderRequestCounter));
        public static readonly int _VisibleRendererListMeshletCounts = Shader.PropertyToID(nameof(_VisibleRendererListMeshletCounts));
        public static readonly int _ForcedMeshLODNodeDepth = Shader.PropertyToID(nameof(_ForcedMeshLODNodeDepth));
        public static readonly int _MeshLODErrorThreshold = Shader.PropertyToID(nameof(_MeshLODErrorThreshold));
    }
}
