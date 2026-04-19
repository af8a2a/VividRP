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
            int feedbackCapacity)
        {
            if (string.IsNullOrWhiteSpace(spaceName))
                throw new ArgumentException("Space name must be non-empty.", nameof(spaceName));
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (borderSize < 0)
                throw new ArgumentOutOfRangeException(nameof(borderSize));
            if (virtualPageCountX <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountX));
            if (virtualPageCountY <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountY));
            if (mipCount <= 0 || mipCount > VirtualTextureFeedbackProcessor.MaxMipCount)
                throw new ArgumentOutOfRangeException(nameof(mipCount));
            if (cachePageCount <= 0 || cachePageCount > VirtualTexturePageTableEntry.MaxPhysicalPageCount)
                throw new ArgumentOutOfRangeException(nameof(cachePageCount));
            if (graphicsFormat == GraphicsFormat.None)
                throw new ArgumentException("Graphics format must be valid.", nameof(graphicsFormat));
            if (maxUploadsPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUploadsPerFrame));
            if (feedbackCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(feedbackCapacity));

            SpaceName = spaceName;
            PageSize = pageSize;
            BorderSize = borderSize;
            VirtualPageCountX = virtualPageCountX;
            VirtualPageCountY = virtualPageCountY;
            MipCount = mipCount;
            CachePageCount = cachePageCount;
            GraphicsFormat = graphicsFormat;
            MaxUploadsPerFrame = maxUploadsPerFrame;
            FeedbackCapacity = feedbackCapacity;
        }

        public string SpaceName { get; }

        public int PageSize { get; }

        public int BorderSize { get; }

        public int VirtualPageCountX { get; }

        public int VirtualPageCountY { get; }

        public int MipCount { get; }

        public int CachePageCount { get; }

        public GraphicsFormat GraphicsFormat { get; }

        public int MaxUploadsPerFrame { get; }

        public int FeedbackCapacity { get; }

        public int PhysicalPageSize => PageSize + BorderSize * 2;

        public bool Equals(VirtualTextureSpaceDesc other)
        {
            return string.Equals(SpaceName, other.SpaceName, StringComparison.Ordinal)
                   && PageSize == other.PageSize
                   && BorderSize == other.BorderSize
                   && VirtualPageCountX == other.VirtualPageCountX
                   && VirtualPageCountY == other.VirtualPageCountY
                   && MipCount == other.MipCount
                   && CachePageCount == other.CachePageCount
                   && GraphicsFormat == other.GraphicsFormat
                   && MaxUploadsPerFrame == other.MaxUploadsPerFrame
                   && FeedbackCapacity == other.FeedbackCapacity;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureSpaceDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SpaceName,
                PageSize,
                BorderSize,
                VirtualPageCountX,
                VirtualPageCountY,
                MipCount,
                HashCode.Combine(CachePageCount,
                    GraphicsFormat,
                    MaxUploadsPerFrame,
                    FeedbackCapacity)
            );
        }

        public override string ToString()
        {
            return $"{SpaceName} ({VirtualPageCountX}x{VirtualPageCountY}, mips={MipCount}, cache={CachePageCount})";
        }
    }
}
