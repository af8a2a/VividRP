using System;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public readonly struct VTStackDesc : IEquatable<VTStackDesc>
    {
        public VTStackDesc(
            int pageSize,
            int borderSize,
            int cachePageCount,
            GraphicsFormat graphicsFormat,
            int maxUploadsPerFrame,
            int feedbackCapacity)
        {
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (borderSize < 0)
                throw new ArgumentOutOfRangeException(nameof(borderSize));
            if (cachePageCount <= 0 || cachePageCount > VirtualTexturePageTableEntry.MaxPhysicalPageCount)
                throw new ArgumentOutOfRangeException(nameof(cachePageCount));
            if (graphicsFormat == GraphicsFormat.None)
                throw new ArgumentException("Graphics format must be valid.", nameof(graphicsFormat));
            if (maxUploadsPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUploadsPerFrame));
            if (feedbackCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(feedbackCapacity));

            PageSize = pageSize;
            BorderSize = borderSize;
            CachePageCount = cachePageCount;
            GraphicsFormat = graphicsFormat;
            MaxUploadsPerFrame = maxUploadsPerFrame;
            FeedbackCapacity = feedbackCapacity;
        }

        public int PageSize { get; }

        public int BorderSize { get; }

        public int CachePageCount { get; }

        public GraphicsFormat GraphicsFormat { get; }

        public int MaxUploadsPerFrame { get; }

        public int FeedbackCapacity { get; }

        public int PhysicalPageSize => PageSize + BorderSize * 2;

        public bool Equals(VTStackDesc other)
        {
            return PageSize == other.PageSize
                   && BorderSize == other.BorderSize
                   && CachePageCount == other.CachePageCount
                   && GraphicsFormat == other.GraphicsFormat
                   && MaxUploadsPerFrame == other.MaxUploadsPerFrame
                   && FeedbackCapacity == other.FeedbackCapacity;
        }

        public override bool Equals(object obj)
        {
            return obj is VTStackDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                PageSize,
                BorderSize,
                CachePageCount,
                GraphicsFormat,
                MaxUploadsPerFrame,
                FeedbackCapacity);
        }
    }
}
