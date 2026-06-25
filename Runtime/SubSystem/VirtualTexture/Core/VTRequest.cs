using System;

namespace VividRP.Runtime
{
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
