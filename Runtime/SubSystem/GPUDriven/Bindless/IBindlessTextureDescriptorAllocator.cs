using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public interface IBindlessTextureDescriptorAllocator
    {
        bool IsAvailable { get; }

        uint DescriptorHeapCount { get; }

        uint DescriptorStartIndex { get; }

        uint DescriptorCapacity { get; }

        ulong CompletedFrameFenceValue { get; }

        ulong PendingFrameFenceValue { get; }

        string UnavailableReason { get; }

        uint CreateSRVDescriptorCallCountThisFrame { get; }

        void ResetPerFrameStats();

        bool TryCreateTextureDescriptor(Texture texture, uint index);
    }
}
