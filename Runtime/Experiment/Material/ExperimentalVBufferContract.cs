using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.Experimental.Material
{
    public static class ExperimentalVBufferContract
    {
        public const uint Version = 1;
        public const uint InvalidMaterialValue = 0;
        public const uint MaterialValueOffset = 1;
        public const int BytesPerPixel = 24;
        public const int MaterialRecordStride = 272;
    }

    [Flags]
    internal enum ExperimentalVBufferMaterialFeatureFlags : uint
    {
        None = 0,
        NormalMap = 1 << 0,
        MetallicMap = 1 << 1,
        RoughnessMap = 1 << 2,
        SmoothnessFromAlbedoAlpha = 1 << 3,
        OcclusionMap = 1 << 4,
        EmissionMap = 1 << 5,
        ClearCoat = 1 << 6,
        ReceiveSSR = 1 << 7,
        ReceiveDecals = 1 << 8,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ExperimentalVBufferMaterialData
    {
        internal VividSurfaceBindingData BaseBinding;
        internal VividSurfaceBindingData AuxiliaryBinding;
        internal VividSurfaceBindingData TopBinding;

        internal float4 BaseColor;
        internal float4 BaseMapST;
        internal float4 EmissionColor;
        internal float4 BaseSurface;
        internal float4 BaseRemap0;
        internal float4 BaseRemap1;
        internal float4 BaseClosure;
        internal float4 TopColor;
        internal float4 TopMapST;
        internal float4 TopSurface;
        internal uint4 FeatureFlags;
    }
}
