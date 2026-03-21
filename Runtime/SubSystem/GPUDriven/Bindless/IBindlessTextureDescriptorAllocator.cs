using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public interface IBindlessTextureDescriptorAllocator
    {
        bool IsAvailable { get; }

        uint DescriptorHeapCount { get; }

        string UnavailableReason { get; }

        bool TryCreateTextureDescriptor(Texture texture, uint index);
    }
}
