using UnityEngine;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct VividGPUDrivenStats
    {
        internal VividGPUDrivenStats(
            bool isAvailable,
            string statusMessage,
            bool hasCamera,
            string cameraName,
            CameraType cameraType,
            int frameIndex,
            double timestamp,
            string textureBackendName,
            bool textureBackendAvailable,
            int rendererCount,
            int instanceCount,
            int materialCount,
            int surfaceBindingCount,
            int meshLODNodeCount,
            int meshletCount,
            int vertexCount,
            int indexCount,
            int maxMeshletListBuildJobCount,
            int maxVisibleMeshletRenderRequestCount,
            uint backendPoolCount,
            uint backendResourceCapacity,
            uint allocatedBackendResourceCount,
            uint createBackendResourceCallCountThisFrame,
            int registeredBackendResourceCount,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold,
            VividPrimitiveSceneStats primitiveSceneStats = default,
            VividPrimitiveDrawSetStats primitiveDrawSetStats = default)
        {
            IsAvailable = isAvailable;
            StatusMessage = statusMessage;
            HasCamera = hasCamera;
            CameraName = cameraName;
            CameraType = cameraType;
            FrameIndex = frameIndex;
            Timestamp = timestamp;
            TextureBackendName = textureBackendName;
            TextureBackendAvailable = textureBackendAvailable;
            RendererCount = rendererCount;
            InstanceCount = instanceCount;
            MaterialCount = materialCount;
            SurfaceBindingCount = surfaceBindingCount;
            MeshLODNodeCount = meshLODNodeCount;
            MeshletCount = meshletCount;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            MaxMeshletListBuildJobCount = maxMeshletListBuildJobCount;
            MaxVisibleMeshletRenderRequestCount = maxVisibleMeshletRenderRequestCount;
            BackendPoolCount = backendPoolCount;
            BackendResourceCapacity = backendResourceCapacity;
            AllocatedBackendResourceCount = allocatedBackendResourceCount;
            CreateBackendResourceCallCountThisFrame = createBackendResourceCallCountThisFrame;
            RegisteredBackendResourceCount = registeredBackendResourceCount;
            ForcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
            MeshLODErrorThreshold = meshLODErrorThreshold;
            PrimitiveSceneStats = primitiveSceneStats;
            PrimitiveDrawSetStats = primitiveDrawSetStats;
        }

        internal bool IsAvailable { get; }
        internal string StatusMessage { get; }
        internal bool HasCamera { get; }
        internal string CameraName { get; }
        internal CameraType CameraType { get; }
        internal int FrameIndex { get; }
        internal double Timestamp { get; }
        internal string TextureBackendName { get; }
        internal bool TextureBackendAvailable { get; }
        internal int RendererCount { get; }
        internal int InstanceCount { get; }
        internal int MaterialCount { get; }
        internal int SurfaceBindingCount { get; }
        internal int MeshLODNodeCount { get; }
        internal int MeshletCount { get; }
        internal int VertexCount { get; }
        internal int IndexCount { get; }
        internal int MaxMeshletListBuildJobCount { get; }
        internal int MaxVisibleMeshletRenderRequestCount { get; }
        internal uint BackendPoolCount { get; }
        internal uint BackendResourceCapacity { get; }
        internal uint AllocatedBackendResourceCount { get; }
        internal uint CreateBackendResourceCallCountThisFrame { get; }
        internal int RegisteredBackendResourceCount { get; }
        internal int ForcedMeshLODNodeDepth { get; }
        internal float MeshLODErrorThreshold { get; }
        internal VividPrimitiveSceneStats PrimitiveSceneStats { get; }
        internal VividPrimitiveDrawSetStats PrimitiveDrawSetStats { get; }
    }

    internal static class VividGPUDrivenStatsRegistry
    {
        private static VividGPUDrivenStats s_LastStats;

        internal static VividGPUDrivenStats LastStats => s_LastStats;

        internal static void Report(VividGPUDrivenStats stats)
        {
            s_LastStats = stats;
        }

        internal static void Clear()
        {
            s_LastStats = default;
        }
    }
}
