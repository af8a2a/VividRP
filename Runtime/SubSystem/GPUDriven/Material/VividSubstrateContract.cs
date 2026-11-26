using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal enum VividSubstratePixelMode : uint
    {
        Empty = 0u,
        SingleSimple = 1u,
        SingleComplex = 2u,
        MultiClosure = 3u,
        Error = 15u,
    }

    internal enum VividSubstrateTopologyOp : uint
    {
        None = 0u,
        HorizontalMix = 1u,
        VerticalLayer = 2u,
        GeneralTree = 3u,
    }

    [Flags]
    internal enum VividSubstrateFeatureMask : uint
    {
        None = 0u,
        OpenPBROpaque = 1u << 0,
        Coat = 1u << 1,
        Fuzz = 1u << 2,
        Anisotropy = 1u << 3,
        Subsurface = 1u << 4,
        Transmission = 1u << 5,
        MultiClosure = 1u << 6,
        DualNormal = 1u << 7,
    }

    [Flags]
    internal enum VividSubstrateHeaderFlags : uint
    {
        None = 0u,
        ReceiveDecals = 1u << 0,
        HasEmission = 1u << 1,
        ThinWalled = 1u << 2,
    }

    internal enum VividSubstrateClosureType : uint
    {
        None = 0u,
        OpenPBRSlab = 1u,
        Hair = 2u,
        Eye = 3u,
    }

    internal struct VividSubstratePixelHeader
    {
        internal VividSubstratePixelMode Mode;
        internal uint ClosureCount;
        internal VividSubstrateTopologyOp TopologyOp;
        internal VividSubstrateFeatureMask FeatureMask;
        internal uint PayloadUintCount;
        internal VividSubstrateHeaderFlags Flags;
        internal uint AbiVersion;
    }

    /// <summary>
    /// Frozen Substrate Closure Buffer ABI v1 contract shared between C# and HLSL
    /// (Shaders/Core/Public/VividSubstrateBuffer.hlsl).
    /// Storage is a per-camera RWTexture2DArray&lt;uint&gt; (R32_UInt) with a fixed
    /// per-pixel byte budget (default 64 B/px = 16 uint slices).
    /// </summary>
    internal static class VividSubstrateContract
    {
        internal const uint AbiVersion = 1u;
        internal const int BytesPerSlice = sizeof(uint);
        internal const int DefaultBytesPerPixel = 64;
        internal const int DefaultSliceCount = DefaultBytesPerPixel / BytesPerSlice; // 16
        internal const int MaxBytesPerPixel = 96;
        internal const int MaxSliceCount = MaxBytesPerPixel / BytesPerSlice; // 24
        internal const int MaxClosuresPerPixel = 4;

        internal const int HeaderSliceCount = 1;
        internal const int OpenPBRCoreUintCount = 5;
        internal const int OpenPBROpaqueSingleTotalSlices = HeaderSliceCount + OpenPBRCoreUintCount; // 6 uints = 24 B/px

        internal const GraphicsFormat ClosureBufferFormat = GraphicsFormat.R32_UInt;
        internal const TextureDimension ClosureBufferDimension = TextureDimension.Tex2DArray;

        // Slice 0 Pixel Header bit layout (32 bits total)
        internal const int HeaderModeShift = 0;
        internal const uint HeaderModeMask = 0xFu;
        internal const int HeaderClosureCountShift = 4;
        internal const uint HeaderClosureCountMask = 0x7u;
        internal const int HeaderTopologyOpShift = 7;
        internal const uint HeaderTopologyOpMask = 0xFu;
        internal const int HeaderFeatureMaskShift = 11;
        internal const uint HeaderFeatureMaskMask = 0xFFu;
        internal const int HeaderPayloadUintCountShift = 19;
        internal const uint HeaderPayloadUintCountMask = 0x3Fu;
        internal const int HeaderFlagsShift = 25;
        internal const uint HeaderFlagsMask = 0x7u;
        internal const int HeaderAbiVersionShift = 28;
        internal const uint HeaderAbiVersionMask = 0xFu;

        // Closure Word0 bit layout (32 bits total)
        internal const int ClosureTypeShift = 0;
        internal const uint ClosureTypeMask = 0xFu;
        internal const int ClosureFeatureBitsShift = 4;
        internal const uint ClosureFeatureBitsMask = 0xFFu;
        internal const int ClosureWeightShift = 12;
        internal const uint ClosureWeightMask = 0xFFu;
        internal const int ClosureBaseWeightShift = 20;
        internal const uint ClosureBaseWeightMask = 0xFFu;
        internal const int ClosureExtUintCountShift = 28;
        internal const uint ClosureExtUintCountMask = 0xFu;

        // Closure Word3 bit layout (10 + 10 + 12 = 32 bits)
        internal const int RoughnessShift = 0;
        internal const uint RoughnessMask = 0x3FFu;
        internal const int MetalnessShift = 10;
        internal const uint MetalnessMask = 0x3FFu;
        internal const int SpecularIorShift = 20;
        internal const uint SpecularIorMask = 0xFFFu;

        internal static int ClampSliceCount(int requestedSlices)
        {
            return Mathf.Clamp(requestedSlices, OpenPBROpaqueSingleTotalSlices, MaxSliceCount);
        }

        internal static uint PackPixelHeader(in VividSubstratePixelHeader header)
        {
            uint word = 0u;
            word |= ((uint)header.Mode & HeaderModeMask) << HeaderModeShift;
            word |= (header.ClosureCount & HeaderClosureCountMask) << HeaderClosureCountShift;
            word |= ((uint)header.TopologyOp & HeaderTopologyOpMask) << HeaderTopologyOpShift;
            word |= ((uint)header.FeatureMask & HeaderFeatureMaskMask) << HeaderFeatureMaskShift;
            word |= (header.PayloadUintCount & HeaderPayloadUintCountMask) << HeaderPayloadUintCountShift;
            word |= ((uint)header.Flags & HeaderFlagsMask) << HeaderFlagsShift;
            word |= (header.AbiVersion & HeaderAbiVersionMask) << HeaderAbiVersionShift;
            return word;
        }

        internal static VividSubstratePixelHeader UnpackPixelHeader(uint word)
        {
            return new VividSubstratePixelHeader
            {
                Mode = (VividSubstratePixelMode)((word >> HeaderModeShift) & HeaderModeMask),
                ClosureCount = (word >> HeaderClosureCountShift) & HeaderClosureCountMask,
                TopologyOp = (VividSubstrateTopologyOp)((word >> HeaderTopologyOpShift) & HeaderTopologyOpMask),
                FeatureMask = (VividSubstrateFeatureMask)((word >> HeaderFeatureMaskShift) & HeaderFeatureMaskMask),
                PayloadUintCount = (word >> HeaderPayloadUintCountShift) & HeaderPayloadUintCountMask,
                Flags = (VividSubstrateHeaderFlags)((word >> HeaderFlagsShift) & HeaderFlagsMask),
                AbiVersion = (word >> HeaderAbiVersionShift) & HeaderAbiVersionMask,
            };
        }

        internal static bool IsHeaderValid(uint word)
        {
            var header = UnpackPixelHeader(word);
            return header.AbiVersion == AbiVersion
                && header.Mode != VividSubstratePixelMode.Empty
                && header.Mode != VividSubstratePixelMode.Error
                && header.ClosureCount > 0u
                && header.ClosureCount <= MaxClosuresPerPixel
                && header.PayloadUintCount > 0u
                && (HeaderSliceCount + header.PayloadUintCount) <= MaxSliceCount;
        }

        internal static uint PackUNorm(float value, uint maxValue)
        {
            return (uint)Mathf.RoundToInt(Mathf.Clamp01(value) * maxValue);
        }

        internal static float UnpackUNorm(uint quantized, uint maxValue)
        {
            return quantized / (float)maxValue;
        }

        internal static uint PackSpecularIor(float ior)
        {
            float range = OpenPBROpaqueContract.MaximumSpecularIor - OpenPBROpaqueContract.MinimumSpecularIor;
            float normalized = (ior - OpenPBROpaqueContract.MinimumSpecularIor) / range;
            return PackUNorm(normalized, SpecularIorMask);
        }

        internal static float UnpackSpecularIor(uint quantized)
        {
            float range = OpenPBROpaqueContract.MaximumSpecularIor - OpenPBROpaqueContract.MinimumSpecularIor;
            return OpenPBROpaqueContract.MinimumSpecularIor + UnpackUNorm(quantized & SpecularIorMask, SpecularIorMask) * range;
        }
    }
}
