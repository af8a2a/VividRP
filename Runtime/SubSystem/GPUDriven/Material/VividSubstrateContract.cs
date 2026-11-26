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

    internal struct VividSubstrateOperatorDesc
    {
        internal VividSubstrateTopologyOp OpKind;
        internal float Weight;
        internal float OpticalThickness;
        internal float3 TransmittanceColorSrgb;
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

        // Phase 2.1 optional single-closure extension slice counts (written in canonical bit order: Coat -> Fuzz -> Anisotropy)
        internal const int CoatBaseExtUintCount = 2;      // weight/rough/darkening/ior + coatColor/flags
        internal const int CoatDualNormalExtUintCount = 3; // + independent 16x16 octahedral coat normal
        internal const int FuzzExtUintCount = 2;           // fuzzColor/weight + fuzzRoughness
        internal const int AnisotropyExtUintCount = 2;     // aniso/rotation/handedness + 16x16 octahedral tangent
        internal const int SingleComplexMaxTotalSlices =
            HeaderSliceCount + OpenPBRCoreUintCount + CoatDualNormalExtUintCount + FuzzExtUintCount + AnisotropyExtUintCount; // 13 uints = 52 B/px <= 64 B/px

        internal const uint CoatFlagIndependentNormal = 1u << 0;
        internal const uint AnisoFlagNegativeHandedness = 1u << 0;

        // Phase 2.2 Multi-Closure operator word layout (1 uint per binary operator, up to 3 operators for 4 closures)
        // Bits [0..1]:   opKind (1 = HorizontalMix, 2 = VerticalLayer)
        // Bits [2..9]:   weight (UNorm8: blend factor or VerticalLayer coverage)
        // Bits [10..16]: opticalThickness (UNorm7 mapped to [0, MaxOpticalThickness])
        // Bits [17..31]: transmittanceColor (sRGB RGB555: R 17..21, G 22..26, B 27..31)
        internal const int MaxOperatorsPerPixel = MaxClosuresPerPixel - 1; // 3
        internal const int OperatorWordUintCount = 1;
        internal const float MaxOpticalThickness = 4.0f;
        internal const int OpKindShift = 0;
        internal const uint OpKindMask = 0x3u;
        internal const int OpWeightShift = 2;
        internal const uint OpWeightMask = 0xFFu;
        internal const int OpThicknessShift = 10;
        internal const uint OpThicknessMask = 0x7Fu;
        internal const int OpTransRShift = 17;
        internal const int OpTransGShift = 22;
        internal const int OpTransBShift = 27;
        internal const uint OpTransChannelMask = 0x1Fu;

        internal const GraphicsFormat ClosureBufferFormat = GraphicsFormat.R32_UInt;
        internal const TextureDimension ClosureBufferDimension = TextureDimension.Tex2DArray;

        // Stage M1 Raster GBufferPass UAV slot contract (MRT0..4 occupy slots 0..4).
        // Slot 5 is the standard GBufferPass UAV slot; slot 8 is reserved when VirtualTexture
        // feedback UAVs (u5..u7) coexist in the same raster shader pass (D3D12).
        internal const int RasterClosureBufferUavSlot = 5;
        internal const int RasterClosureBufferVtCoexistUavSlot = 8;
        internal const string ClearClosureHeadersKernelName = "ClearSubstrateClosureHeaders";

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

        internal static uint PackCoatIor8(float coatIor)
        {
            float range = OpenPBROpaqueContract.MaximumSpecularIor - OpenPBROpaqueContract.MinimumSpecularIor;
            float normalized = (coatIor - OpenPBROpaqueContract.MinimumSpecularIor) / range;
            return PackUNorm(normalized, 0xFFu);
        }

        internal static float UnpackCoatIor8(uint quantized)
        {
            float range = OpenPBROpaqueContract.MaximumSpecularIor - OpenPBROpaqueContract.MinimumSpecularIor;
            return OpenPBROpaqueContract.MinimumSpecularIor + UnpackUNorm(quantized & 0xFFu, 0xFFu) * range;
        }

        internal static int ComputeSingleClosureExtensionSlices(VividSubstrateFeatureMask features)
        {
            int slices = 0;
            if ((features & VividSubstrateFeatureMask.Coat) != 0)
            {
                slices += (features & VividSubstrateFeatureMask.DualNormal) != 0
                    ? CoatDualNormalExtUintCount
                    : CoatBaseExtUintCount;
            }
            if ((features & VividSubstrateFeatureMask.Fuzz) != 0)
                slices += FuzzExtUintCount;
            if ((features & VividSubstrateFeatureMask.Anisotropy) != 0)
                slices += AnisotropyExtUintCount;
            return slices;
        }

        internal static int ComputeSingleClosureTotalSlices(VividSubstrateFeatureMask features)
        {
            return HeaderSliceCount + OpenPBRCoreUintCount + ComputeSingleClosureExtensionSlices(features);
        }

        internal static uint PackOperatorWord(in VividSubstrateOperatorDesc op)
        {
            uint opKind = ((uint)op.OpKind) & OpKindMask;
            uint weightBits = PackUNorm(op.Weight, OpWeightMask);
            uint thicknessBits = PackUNorm(op.OpticalThickness / MaxOpticalThickness, OpThicknessMask);
            uint r5 = PackUNorm(op.TransmittanceColorSrgb.x, OpTransChannelMask);
            uint g5 = PackUNorm(op.TransmittanceColorSrgb.y, OpTransChannelMask);
            uint b5 = PackUNorm(op.TransmittanceColorSrgb.z, OpTransChannelMask);

            return (opKind << OpKindShift)
                | (weightBits << OpWeightShift)
                | (thicknessBits << OpThicknessShift)
                | (r5 << OpTransRShift)
                | (g5 << OpTransGShift)
                | (b5 << OpTransBShift);
        }

        internal static VividSubstrateOperatorDesc UnpackOperatorWord(uint word)
        {
            return new VividSubstrateOperatorDesc
            {
                OpKind = (VividSubstrateTopologyOp)((word >> OpKindShift) & OpKindMask),
                Weight = UnpackUNorm((word >> OpWeightShift) & OpWeightMask, OpWeightMask),
                OpticalThickness = UnpackUNorm((word >> OpThicknessShift) & OpThicknessMask, OpThicknessMask) * MaxOpticalThickness,
                TransmittanceColorSrgb = new float3(
                    UnpackUNorm((word >> OpTransRShift) & OpTransChannelMask, OpTransChannelMask),
                    UnpackUNorm((word >> OpTransGShift) & OpTransChannelMask, OpTransChannelMask),
                    UnpackUNorm((word >> OpTransBShift) & OpTransChannelMask, OpTransChannelMask)),
            };
        }

        internal static int ComputeMultiClosureTotalSlices(int closureCount)
        {
            if (closureCount < 1)
                return HeaderSliceCount;
            int operatorCount = Math.Max(0, closureCount - 1);
            return HeaderSliceCount
                + operatorCount * OperatorWordUintCount
                + closureCount * OpenPBRCoreUintCount;
        }

        internal static int ComputeMultiClosureTotalSlices(ReadOnlySpan<VividSubstrateFeatureMask> closureFeatures)
        {
            int count = closureFeatures.Length;
            if (count < 1)
                return HeaderSliceCount;
            int total = HeaderSliceCount + Math.Max(0, count - 1) * OperatorWordUintCount;
            for (int i = 0; i < count; i++)
                total += OpenPBRCoreUintCount + ComputeSingleClosureExtensionSlices(closureFeatures[i]);
            return total;
        }

        internal static bool FitsSliceBudget(int totalSlices, int budgetSlices = DefaultSliceCount)
        {
            return totalSlices >= HeaderSliceCount && totalSlices <= budgetSlices;
        }
    }
}
