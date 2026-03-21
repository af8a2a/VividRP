using UnityEngine;

namespace VividRP.Runtime
{
    internal readonly struct VividRayTracingAccelerationStructureStats
    {
        internal VividRayTracingAccelerationStructureStats(
            bool isAvailable,
            string statusMessage,
            string cameraName,
            CameraType cameraType,
            int frameIndex,
            double timestamp,
            VividRTASBuildMode buildMode,
            VividRTASCullingMode cullingMode,
            int candidateRendererCount,
            uint instanceCount,
            ulong memoryBytes,
            bool usedShaderTagFallback)
        {
            IsAvailable = isAvailable;
            StatusMessage = statusMessage;
            CameraName = cameraName;
            CameraType = cameraType;
            FrameIndex = frameIndex;
            Timestamp = timestamp;
            BuildMode = buildMode;
            CullingMode = cullingMode;
            CandidateRendererCount = candidateRendererCount;
            InstanceCount = instanceCount;
            MemoryBytes = memoryBytes;
            UsedShaderTagFallback = usedShaderTagFallback;
        }

        internal bool IsAvailable { get; }

        internal string StatusMessage { get; }

        internal string CameraName { get; }

        internal CameraType CameraType { get; }

        internal int FrameIndex { get; }

        internal double Timestamp { get; }

        internal VividRTASBuildMode BuildMode { get; }

        internal VividRTASCullingMode CullingMode { get; }

        internal int CandidateRendererCount { get; }

        internal uint InstanceCount { get; }

        internal ulong MemoryBytes { get; }

        internal bool UsedShaderTagFallback { get; }

        internal bool HasCullRate => CandidateRendererCount > 0;

        internal float CullRate
        {
            get
            {
                if (!HasCullRate)
                    return 0f;

                return Mathf.Clamp01(1f - (InstanceCount / (float)CandidateRendererCount));
            }
        }
    }

    internal static class VividRayTracingAccelerationStructureStatsRegistry
    {
        private static VividRayTracingAccelerationStructureStats s_LastStats;

        internal static VividRayTracingAccelerationStructureStats LastStats => s_LastStats;

        internal static void Report(VividRayTracingAccelerationStructureStats stats)
        {
            s_LastStats = stats;
        }

        internal static void Clear()
        {
            s_LastStats = default;
        }
    }
}
