using System;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct GPUDrivenSurfaceTextureSet
    {
        internal GPUDrivenSurfaceTextureSet(Texture baseColor, Texture normal, Texture mask)
        {
            BaseColor = baseColor;
            Normal = normal;
            Mask = mask;
        }

        internal Texture BaseColor { get; }

        internal Texture Normal { get; }

        internal Texture Mask { get; }
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
}
