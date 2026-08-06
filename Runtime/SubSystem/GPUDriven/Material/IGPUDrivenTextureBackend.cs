using System;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    public enum GPUDrivenTextureBackendMode
    {
        VirtualTexture = 0,
        Bindless = 1,
    }

    internal enum GPUDrivenSurfaceAddressMode
    {
        Repeat,
        Clamp,
    }

    internal readonly struct GPUDrivenSurfaceTextureSet
    {
        internal GPUDrivenSurfaceTextureSet(Texture baseColor, Texture normal, Texture mask)
            : this(null, baseColor, normal, mask)
        {
        }

        internal GPUDrivenSurfaceTextureSet(
            VividVirtualTextureAsset streamedVirtualTexture,
            Texture baseColor,
            Texture normal,
            Texture mask)
        {
            StreamedVirtualTexture = streamedVirtualTexture;
            BaseColor = baseColor;
            Normal = normal;
            Mask = mask;
            AddressMode = ResolveAddressMode(baseColor ?? normal ?? mask, out bool unsupportedAddressMode);
            bool baseColorIsMixed = IsMixedAddressMode(baseColor, AddressMode, ref unsupportedAddressMode);
            bool normalIsMixed = IsMixedAddressMode(normal, AddressMode, ref unsupportedAddressMode);
            bool maskIsMixed = IsMixedAddressMode(mask, AddressMode, ref unsupportedAddressMode);
            HasMixedAddressModes = baseColorIsMixed || normalIsMixed || maskIsMixed;
            HasUnsupportedAddressMode = unsupportedAddressMode;
        }

        internal Texture BaseColor { get; }

        internal VividVirtualTextureAsset StreamedVirtualTexture { get; }

        internal Texture Normal { get; }

        internal Texture Mask { get; }

        internal GPUDrivenSurfaceAddressMode AddressMode { get; }

        internal bool HasMixedAddressModes { get; }

        internal bool HasUnsupportedAddressMode { get; }

        private static bool IsMixedAddressMode(
            Texture texture,
            GPUDrivenSurfaceAddressMode expected,
            ref bool unsupportedAddressMode)
        {
            if (texture == null)
                return false;

            GPUDrivenSurfaceAddressMode actual = ResolveAddressMode(texture, out bool unsupported);
            unsupportedAddressMode |= unsupported;
            return actual != expected;
        }

        private static GPUDrivenSurfaceAddressMode ResolveAddressMode(
            Texture texture,
            out bool unsupported)
        {
            unsupported = false;
            if (texture == null || texture.wrapMode == TextureWrapMode.Repeat)
                return GPUDrivenSurfaceAddressMode.Repeat;
            if (texture.wrapMode == TextureWrapMode.Clamp)
                return GPUDrivenSurfaceAddressMode.Clamp;

            unsupported = true;
            return GPUDrivenSurfaceAddressMode.Repeat;
        }
    }

    internal readonly struct GPUDrivenTextureBackendStats
    {
        internal GPUDrivenTextureBackendStats(
            uint poolCount,
            uint resourceCapacity,
            uint allocatedResourceCount,
            uint createResourceCallCountThisFrame,
            int registeredResourceCount)
        {
            PoolCount = poolCount;
            ResourceCapacity = resourceCapacity;
            AllocatedResourceCount = allocatedResourceCount;
            CreateResourceCallCountThisFrame = createResourceCallCountThisFrame;
            RegisteredResourceCount = registeredResourceCount;
        }

        internal uint PoolCount { get; }

        internal uint ResourceCapacity { get; }

        internal uint AllocatedResourceCount { get; }

        internal uint CreateResourceCallCountThisFrame { get; }

        internal int RegisteredResourceCount { get; }
    }

    internal interface IGPUDrivenTextureBackend : IDisposable
    {
        string DisplayName { get; }

        bool IsAvailable { get; }

        string UnavailableReason { get; }

        uint BindingRevision { get; }

        void PrepareFrame();

        void ResetPerFrameStats();

        VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures);

        GPUDrivenTextureBackendStats GetStats();
    }

    internal interface IGPUDrivenTextureBindingLifecycle
    {
        void BeginSurfaceBindingUpdate();

        void EndSurfaceBindingUpdate();

        void CancelSurfaceBindingUpdate();
    }

    internal interface IGPUDrivenVirtualTextureBackend
    {
        int VirtualTextureSpaceId { get; }

        int VirtualTextureAllocationId { get; }
    }
}
