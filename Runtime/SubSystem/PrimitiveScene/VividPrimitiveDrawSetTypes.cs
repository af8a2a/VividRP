using System;
using System.Runtime.InteropServices;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    [Flags]
    internal enum VividPrimitiveDrawSourceFlags : uint
    {
        None = 0u,
        Valid = 1u << 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveDrawSourceData
    {
        internal VividPrimitiveHandle PrimitiveHandle;
        internal uint AbsoluteDrawSectionIndex;
        internal uint LegacyInstanceIndex;
        internal VividRendererListID RendererListID;
        internal VividPrimitiveDrawSourceFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveDrawSetEntry
    {
        public uint PrimitiveIndex;
        public uint PrimitiveGeneration;
        public uint DrawSectionIndex;
        public uint LegacyInstanceIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveDrawBucket
    {
        internal VividRendererListID RendererListID;
        internal uint DrawOffset;
        internal uint DrawCount;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveDrawSetBuildResult
    {
        internal int VisiblePrimitiveCount;
        internal int DrawCount;
        internal int NonEmptyBucketCount;
        internal int Padding;
    }

    internal readonly struct VividPrimitiveDrawSetStats
    {
        internal VividPrimitiveDrawSetStats(
            int inputPrimitiveCount,
            int inputDrawSourceCount,
            int visiblePrimitiveCount,
            int drawCount,
            int nonEmptyBucketCount,
            int visibilityCapacity,
            int drawCapacity,
            int gpuIndexCapacity,
            int uploadCount,
            long uploadBytes,
            int frameIndex,
            uint sceneRevision)
        {
            InputPrimitiveCount = inputPrimitiveCount;
            InputDrawSourceCount = inputDrawSourceCount;
            VisiblePrimitiveCount = visiblePrimitiveCount;
            DrawCount = drawCount;
            NonEmptyBucketCount = nonEmptyBucketCount;
            VisibilityCapacity = visibilityCapacity;
            DrawCapacity = drawCapacity;
            GPUIndexCapacity = gpuIndexCapacity;
            UploadCount = uploadCount;
            UploadBytes = uploadBytes;
            FrameIndex = frameIndex;
            SceneRevision = sceneRevision;
        }

        internal int InputPrimitiveCount { get; }
        internal int InputDrawSourceCount { get; }
        internal int VisiblePrimitiveCount { get; }
        internal int DrawCount { get; }
        internal int NonEmptyBucketCount { get; }
        internal int VisibilityCapacity { get; }
        internal int DrawCapacity { get; }
        internal int GPUIndexCapacity { get; }
        internal int UploadCount { get; }
        internal long UploadBytes { get; }
        internal int FrameIndex { get; }
        internal uint SceneRevision { get; }
    }
}
