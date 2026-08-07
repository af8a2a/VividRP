using System;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public readonly struct VirtualTextureSpaceDesc : IEquatable<VirtualTextureSpaceDesc>
    {
        public VirtualTextureSpaceDesc(
            string spaceName,
            int pageSize,
            int borderSize,
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            int cachePageCount,
            GraphicsFormat graphicsFormat,
            int maxUploadsPerFrame,
            int feedbackCapacity,
            int neighborPrefetchCount = 0)
            : this(
                spaceName,
                virtualPageCountX,
                virtualPageCountY,
                mipCount,
                new VTStackDesc(
                    pageSize,
                    borderSize,
                    cachePageCount,
                    graphicsFormat,
                    maxUploadsPerFrame,
                    feedbackCapacity,
                    neighborPrefetchCount))
        {
        }

        public VirtualTextureSpaceDesc(
            string spaceName,
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            in VTStackDesc stackDesc)
        {
            if (string.IsNullOrWhiteSpace(spaceName))
                throw new ArgumentException("Space name must be non-empty.", nameof(spaceName));
            if (virtualPageCountX <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountX));
            if (virtualPageCountY <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountY));
            if (virtualPageCountX > VirtualTextureFeedbackProcessor.MaxPageCountPerDimension)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountX));
            if (virtualPageCountY > VirtualTextureFeedbackProcessor.MaxPageCountPerDimension)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountY));
            if (mipCount <= 0 || mipCount > VirtualTextureFeedbackProcessor.MaxMipCount)
                throw new ArgumentOutOfRangeException(nameof(mipCount));

            PageTableEntryCount = VirtualTextureSpaceUtility.GetTotalPageCount(
                virtualPageCountX,
                virtualPageCountY,
                mipCount);
            SpaceName = spaceName;
            StackDesc = stackDesc;
            VirtualPageCountX = virtualPageCountX;
            VirtualPageCountY = virtualPageCountY;
            MipCount = mipCount;
        }

        public string SpaceName { get; }

        public VTStackDesc StackDesc { get; }

        public int PageSize => StackDesc.PageSize;

        public int BorderSize => StackDesc.BorderSize;

        public int VirtualPageCountX { get; }

        public int VirtualPageCountY { get; }

        public int MipCount { get; }

        public int PageTableEntryCount { get; }

        public int CachePageCount => StackDesc.CachePageCount;

        public GraphicsFormat GraphicsFormat => StackDesc.GraphicsFormat;

        public int MaxUploadsPerFrame => StackDesc.MaxUploadsPerFrame;

        public int FeedbackCapacity => StackDesc.FeedbackCapacity;

        public int NeighborPrefetchCount => StackDesc.NeighborPrefetchCount;

        public int PhysicalPageSize => StackDesc.PhysicalPageSize;

        public bool Equals(VirtualTextureSpaceDesc other)
        {
            return string.Equals(SpaceName, other.SpaceName, StringComparison.Ordinal)
                   && VirtualPageCountX == other.VirtualPageCountX
                   && VirtualPageCountY == other.VirtualPageCountY
                   && MipCount == other.MipCount
                   && StackDesc.Equals(other.StackDesc);
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureSpaceDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SpaceName,
                VirtualPageCountX,
                VirtualPageCountY,
                MipCount,
                StackDesc);
        }

        public override string ToString()
        {
            return $"{SpaceName} ({VirtualPageCountX}x{VirtualPageCountY}, mips={MipCount}, cache={CachePageCount})";
        }
    }
}
