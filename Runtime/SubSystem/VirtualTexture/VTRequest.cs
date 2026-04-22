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
            int requestFrame)
        {
            SpaceId = spaceId;
            PageCoord = pageCoord;
            PhysicalPageId = physicalPageId;
            Generation = generation;
            Priority = priority;
            RequestFrame = requestFrame;
        }

        public int SpaceId { get; }

        public VirtualTexturePageCoord PageCoord { get; }

        public int PhysicalPageId { get; }

        public int Generation { get; }

        public int Priority { get; }

        public int RequestFrame { get; }

        public bool Equals(VTRequest other)
        {
            return SpaceId == other.SpaceId
                   && PageCoord.Equals(other.PageCoord)
                   && PhysicalPageId == other.PhysicalPageId
                   && Generation == other.Generation
                   && Priority == other.Priority
                   && RequestFrame == other.RequestFrame;
        }

        public override bool Equals(object obj)
        {
            return obj is VTRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SpaceId, PageCoord, PhysicalPageId, Generation, Priority, RequestFrame);
        }
    }
}
