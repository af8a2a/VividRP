using System;

namespace VividRP.Runtime
{
    public readonly struct VTAllocationDesc : IEquatable<VTAllocationDesc>
    {
        public VTAllocationDesc(
            string name,
            in VirtualTextureSpaceDesc spaceDesc,
            VTProducerHandle producerHandle,
            bool privateSpace = false,
            bool shareDuplicateLayers = true)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Allocation name must be non-empty.", nameof(name));
            if (!producerHandle.IsValid)
                throw new ArgumentException("Producer handle must be valid.", nameof(producerHandle));

            Name = name;
            SpaceDesc = spaceDesc;
            ProducerHandle = producerHandle;
            PrivateSpace = privateSpace;
            ShareDuplicateLayers = shareDuplicateLayers;
        }

        public string Name { get; }

        public VirtualTextureSpaceDesc SpaceDesc { get; }

        public VTProducerHandle ProducerHandle { get; }

        public bool PrivateSpace { get; }

        public bool ShareDuplicateLayers { get; }

        internal static VTAllocationDesc FromSpaceDesc(
            in VirtualTextureSpaceDesc spaceDesc,
            VTProducerHandle producerHandle)
        {
            return new VTAllocationDesc(spaceDesc.SpaceName, spaceDesc, producerHandle);
        }

        public bool Equals(VTAllocationDesc other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && SpaceDesc.Equals(other.SpaceDesc)
                   && ProducerHandle.Equals(other.ProducerHandle)
                   && PrivateSpace == other.PrivateSpace
                   && ShareDuplicateLayers == other.ShareDuplicateLayers;
        }

        public override bool Equals(object obj)
        {
            return obj is VTAllocationDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, SpaceDesc, ProducerHandle, PrivateSpace, ShareDuplicateLayers);
        }
    }

    public sealed class VTAllocatedVirtualTexture
    {
        internal VTAllocatedVirtualTexture(
            int allocationId,
            int spaceId,
            in VTAllocationDesc desc)
        {
            AllocationId = allocationId;
            SpaceId = spaceId;
            Description = desc;
        }

        public int AllocationId { get; }

        public int SpaceId { get; }

        public VTAllocationDesc Description { get; }

        public string Name => Description.Name;

        public VTProducerHandle ProducerHandle => Description.ProducerHandle;

        public VirtualTextureSpaceDesc SpaceDesc => Description.SpaceDesc;

        public bool IsValid => AllocationId > 0 && SpaceId > 0 && ProducerHandle.IsValid;
    }
}
