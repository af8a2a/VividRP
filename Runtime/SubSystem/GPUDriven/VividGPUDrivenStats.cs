using UnityEngine;

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
            bool bindlessAvailable,
            int rendererCount,
            int instanceCount,
            int materialCount,
            int meshLODNodeCount,
            int meshletCount,
            int vertexCount,
            int indexCount,
            int maxMeshletListBuildJobCount,
            int maxVisibleMeshletRenderRequestCount,
            uint descriptorHeapCount,
            uint descriptorCapacity,
            uint allocatedDescriptorCount,
            int registeredTextureCount,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold)
        {
            IsAvailable = isAvailable;
            StatusMessage = statusMessage;
            HasCamera = hasCamera;
            CameraName = cameraName;
            CameraType = cameraType;
            FrameIndex = frameIndex;
            Timestamp = timestamp;
            BindlessAvailable = bindlessAvailable;
            RendererCount = rendererCount;
            InstanceCount = instanceCount;
            MaterialCount = materialCount;
            MeshLODNodeCount = meshLODNodeCount;
            MeshletCount = meshletCount;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            MaxMeshletListBuildJobCount = maxMeshletListBuildJobCount;
            MaxVisibleMeshletRenderRequestCount = maxVisibleMeshletRenderRequestCount;
            DescriptorHeapCount = descriptorHeapCount;
            DescriptorCapacity = descriptorCapacity;
            AllocatedDescriptorCount = allocatedDescriptorCount;
            RegisteredTextureCount = registeredTextureCount;
            ForcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
            MeshLODErrorThreshold = meshLODErrorThreshold;
        }

        internal bool IsAvailable { get; }
        internal string StatusMessage { get; }
        internal bool HasCamera { get; }
        internal string CameraName { get; }
        internal CameraType CameraType { get; }
        internal int FrameIndex { get; }
        internal double Timestamp { get; }
        internal bool BindlessAvailable { get; }
        internal int RendererCount { get; }
        internal int InstanceCount { get; }
        internal int MaterialCount { get; }
        internal int MeshLODNodeCount { get; }
        internal int MeshletCount { get; }
        internal int VertexCount { get; }
        internal int IndexCount { get; }
        internal int MaxMeshletListBuildJobCount { get; }
        internal int MaxVisibleMeshletRenderRequestCount { get; }
        internal uint DescriptorHeapCount { get; }
        internal uint DescriptorCapacity { get; }
        internal uint AllocatedDescriptorCount { get; }
        internal int RegisteredTextureCount { get; }
        internal int ForcedMeshLODNodeDepth { get; }
        internal float MeshLODErrorThreshold { get; }
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
