using System;

namespace VividRP.Runtime
{
    internal enum VTIOPriorityTier : byte
    {
        Background,
        Normal,
        High,
        Critical,
    }

    internal readonly struct VTRequestPriorityKey : IEquatable<VTRequestPriorityKey>
    {
        private VTRequestPriorityKey(
            bool locked,
            bool isActiveView,
            int cameraPriority,
            int producerPriority,
            int hitCount,
            int mip,
            int requestFrame,
            VTIOPriorityTier ioTier)
        {
            Locked = locked;
            IsActiveView = isActiveView;
            CameraPriority = cameraPriority;
            ProducerPriority = producerPriority;
            HitCount = Math.Max(0, hitCount);
            Mip = Math.Max(0, mip);
            RequestFrame = requestFrame;
            PageScore = VTRequestPriorityUtility.ComputeMipWeightedScore(HitCount, Mip);
            IOTier = ioTier;
        }

        internal bool Locked { get; }

        internal bool IsActiveView { get; }

        internal int CameraPriority { get; }

        internal int ProducerPriority { get; }

        internal long PageScore { get; }

        internal int Mip { get; }

        internal int HitCount { get; }

        internal int RequestFrame { get; }

        internal VTIOPriorityTier IOTier { get; }

        internal bool UsesHighIOPriority => IOTier >= VTIOPriorityTier.High;

        internal static VTRequestPriorityKey FromRequest(
            in VTRequest request,
            bool locked,
            int producerPriority,
            bool mipTail = false)
        {
            VTIOPriorityTier ioTier = locked || request.Priority == int.MaxValue || mipTail
                ? VTIOPriorityTier.Critical
                : request.IsActiveView
                    ? VTIOPriorityTier.High
                    : request.Priority > 0
                        ? VTIOPriorityTier.Normal
                        : VTIOPriorityTier.Background;
            return new VTRequestPriorityKey(
                locked,
                request.IsActiveView,
                request.CameraPriority,
                producerPriority,
                request.Priority,
                request.PageCoord.Mip,
                request.RequestFrame,
                ioTier);
        }

        internal static VTRequestPriorityKey FromLegacyIOPriority(bool highPriority)
        {
            return new VTRequestPriorityKey(
                locked: highPriority,
                isActiveView: highPriority,
                cameraPriority: highPriority ? int.MinValue : int.MaxValue,
                producerPriority: 0,
                hitCount: highPriority ? int.MaxValue : 0,
                mip: 0,
                requestFrame: int.MaxValue,
                ioTier: highPriority ? VTIOPriorityTier.High : VTIOPriorityTier.Normal);
        }

        internal static VTRequestPriorityKey FromFeedbackRequest(
            in VirtualTextureAggregatedFeedbackRequest request,
            int producerPriority)
        {
            return new VTRequestPriorityKey(
                locked: false,
                isActiveView: request.IsActiveView,
                cameraPriority: request.CameraPriority,
                producerPriority: producerPriority,
                hitCount: request.HitCount,
                mip: request.PageCoord.Mip,
                requestFrame: int.MaxValue,
                ioTier: request.IsActiveView
                    ? VTIOPriorityTier.High
                    : request.HitCount > 0
                        ? VTIOPriorityTier.Normal
                        : VTIOPriorityTier.Background);
        }

        public bool Equals(VTRequestPriorityKey other)
        {
            return Locked == other.Locked
                   && IsActiveView == other.IsActiveView
                   && CameraPriority == other.CameraPriority
                   && ProducerPriority == other.ProducerPriority
                   && PageScore == other.PageScore
                   && Mip == other.Mip
                   && HitCount == other.HitCount
                   && RequestFrame == other.RequestFrame
                   && IOTier == other.IOTier;
        }

        public override bool Equals(object obj)
        {
            return obj is VTRequestPriorityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Locked);
            hash.Add(IsActiveView);
            hash.Add(CameraPriority);
            hash.Add(ProducerPriority);
            hash.Add(PageScore);
            hash.Add(Mip);
            hash.Add(HitCount);
            hash.Add(RequestFrame);
            hash.Add(IOTier);
            return hash.ToHashCode();
        }
    }

    internal static class VTRequestPriorityUtility
    {
        internal static long ComputeMipWeightedScore(int hitCount, int mip)
        {
            int resolvedHitCount = hitCount > 0 ? hitCount : 0;
            long mipWeight = mip >= 0 ? (long)mip + 1L : 1L;
            return (long)resolvedHitCount * mipWeight;
        }

        internal static int CompareMipWeightedScoreDescending(
            int leftHitCount,
            int leftMip,
            int rightHitCount,
            int rightMip)
        {
            long leftScore = ComputeMipWeightedScore(leftHitCount, leftMip);
            long rightScore = ComputeMipWeightedScore(rightHitCount, rightMip);
            return rightScore.CompareTo(leftScore);
        }

        internal static int Compare(
            in VTRequestPriorityKey left,
            in VTRequestPriorityKey right)
        {
            if (left.Locked != right.Locked)
                return left.Locked ? -1 : 1;

            if (left.IsActiveView != right.IsActiveView)
                return left.IsActiveView ? -1 : 1;

            int cameraCompare = left.CameraPriority.CompareTo(right.CameraPriority);
            if (cameraCompare != 0)
                return cameraCompare;

            int producerCompare = right.ProducerPriority.CompareTo(left.ProducerPriority);
            if (producerCompare != 0)
                return producerCompare;

            int scoreCompare = right.PageScore.CompareTo(left.PageScore);
            if (scoreCompare != 0)
                return scoreCompare;

            int mipCompare = right.Mip.CompareTo(left.Mip);
            if (mipCompare != 0)
                return mipCompare;

            int hitCompare = right.HitCount.CompareTo(left.HitCount);
            if (hitCompare != 0)
                return hitCompare;

            return left.RequestFrame.CompareTo(right.RequestFrame);
        }

        internal static int CompareForIO(
            in VTRequestPriorityKey left,
            in VTRequestPriorityKey right)
        {
            int tierCompare = right.IOTier.CompareTo(left.IOTier);
            return tierCompare != 0 ? tierCompare : Compare(left, right);
        }

        internal static VTRequestPriorityKey SelectHigher(
            in VTRequestPriorityKey left,
            in VTRequestPriorityKey right)
        {
            return CompareForIO(left, right) <= 0 ? left : right;
        }
    }

    public readonly struct VTRequest : IEquatable<VTRequest>
    {
        public VTRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int physicalPageId,
            int generation,
            int priority,
            int requestFrame,
            int cameraPriority = int.MaxValue,
            bool isActiveView = false)
        {
            SpaceId = spaceId;
            PageCoord = pageCoord;
            PhysicalPageId = physicalPageId;
            Generation = generation;
            Priority = priority;
            RequestFrame = requestFrame;
            CameraPriority = cameraPriority;
            IsActiveView = isActiveView;
        }

        public int SpaceId { get; }

        public VirtualTexturePageCoord PageCoord { get; }

        public int PhysicalPageId { get; }

        public int Generation { get; }

        public int Priority { get; }

        public int RequestFrame { get; }

        public int CameraPriority { get; }

        public bool IsActiveView { get; }

        public bool Equals(VTRequest other)
        {
            return SpaceId == other.SpaceId
                   && PageCoord.Equals(other.PageCoord)
                   && PhysicalPageId == other.PhysicalPageId
                   && Generation == other.Generation
                   && Priority == other.Priority
                   && RequestFrame == other.RequestFrame
                   && CameraPriority == other.CameraPriority
                   && IsActiveView == other.IsActiveView;
        }

        public override bool Equals(object obj)
        {
            return obj is VTRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SpaceId,
                PageCoord,
                PhysicalPageId,
                Generation,
                Priority,
                RequestFrame,
                CameraPriority,
                IsActiveView);
        }
    }
}
