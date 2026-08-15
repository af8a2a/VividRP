using System;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    internal sealed class BindlessGPUDrivenTextureBackend : IGPUDrivenTextureBackend
    {
        private readonly VividGPUDrivenObjectTracker m_ObjectTracker;
        private bool m_IsDisposed;

        internal BindlessGPUDrivenTextureBackend()
            : this(new BindlessTextureContainer())
        {
        }

        internal BindlessGPUDrivenTextureBackend(IBindlessTextureDescriptorAllocator allocator)
            : this(new BindlessTextureContainer(allocator))
        {
        }

        private BindlessGPUDrivenTextureBackend(BindlessTextureContainer textureContainer)
        {
            TextureContainer = textureContainer ?? throw new ArgumentNullException(nameof(textureContainer));
            m_ObjectTracker = new VividGPUDrivenObjectTracker(TextureContainer);
        }

        public string DisplayName => "Bindless";

        public bool IsAvailable => TextureContainer.IsAvailable;

        public string UnavailableReason => TextureContainer.UnavailableReason;

        public uint BindingRevision => TextureContainer.TextureBindingRevision;

        internal BindlessTextureContainer TextureContainer { get; }

        public void PrepareFrame()
        {
            ThrowIfDisposed();
            TextureContainer.PreRender();
        }

        public void ResetPerFrameStats()
        {
            ThrowIfDisposed();
            TextureContainer.ResetPerFrameStats();
        }

        public bool CanUseStreamedVirtualTexture(VividVirtualTextureAsset asset)
        {
            return false;
        }

        public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
        {
            ThrowIfDisposed();

            VividSurfaceBindingFlags flags = VividSurfaceBindingFlags.None;
            uint baseColorResource = ResolveResource(textures.BaseColor, VividSurfaceBindingFlags.BaseColor, ref flags);
            uint normalResource = ResolveResource(textures.Normal, VividSurfaceBindingFlags.Normal, ref flags);
            uint maskResource = ResolveResource(textures.Mask, VividSurfaceBindingFlags.Mask, ref flags);
            float addressScaleSign = textures.AddressMode == GPUDrivenSurfaceAddressMode.Clamp ? -1.0f : 1.0f;

            return new VividSurfaceBindingData
            {
                BaseColorResource = baseColorResource,
                NormalResource = normalResource,
                MaskResource = maskResource,
                Flags = flags,
                UVScaleBias = new float4(addressScaleSign, addressScaleSign, 0.0f, 0.0f),
            };
        }

        public GPUDrivenTextureBackendStats GetStats()
        {
            ThrowIfDisposed();
            return new GPUDrivenTextureBackendStats(
                TextureContainer.DescriptorHeapCount,
                TextureContainer.DescriptorCapacity,
                TextureContainer.AllocatedDescriptorCount,
                TextureContainer.CreateSRVDescriptorCallCountThisFrame,
                TextureContainer.RegisteredTextureCount);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_ObjectTracker.Dispose();
            TextureContainer.Dispose();
            m_IsDisposed = true;
        }

        private uint ResolveResource(
            Texture texture,
            VividSurfaceBindingFlags resourceFlag,
            ref VividSurfaceBindingFlags flags)
        {
            if (!TextureContainer.TryGetOrCreateIndex(texture, out uint resource))
            {
                return VividSurfaceBindingData.InvalidResource;
            }

            flags |= resourceFlag;
            return resource;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(BindlessGPUDrivenTextureBackend));
            }
        }
    }
}
