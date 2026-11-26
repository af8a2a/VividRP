#ifndef VIVIDRP_SUBSTRATE_BUFFER_INCLUDED
#define VIVIDRP_SUBSTRATE_BUFFER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "SurfaceSummaryGBuffer.hlsl"
#include "VividOpenPBROpaqueContract.hlsl"

// Frozen Substrate Closure Buffer ABI v1.
// Keep constants and bit layouts in exact agreement with C# VividSubstrateContract.
// Physical storage: RWTexture2DArray<uint> / Texture2DArray<uint> (GraphicsFormat.R32_UInt)
// Default budget: 16 slices = 64 B/px (max 24 slices = 96 B/px).
#define VIVID_SUBSTRATE_ABI_VERSION 1u
#define VIVID_SUBSTRATE_DEFAULT_SLICE_COUNT 16u
#define VIVID_SUBSTRATE_MAX_SLICE_COUNT 24u
#define VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL 4u
#define VIVID_SUBSTRATE_HEADER_SLICE_COUNT 1u
#define VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT 5u
#define VIVID_SUBSTRATE_OPENPBR_SINGLE_TOTAL_SLICES 6u

// Pixel mode (4 bits)
#define VIVID_SUBSTRATE_MODE_EMPTY 0u
#define VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE 1u
#define VIVID_SUBSTRATE_MODE_SINGLE_COMPLEX 2u
#define VIVID_SUBSTRATE_MODE_MULTI_CLOSURE 3u
#define VIVID_SUBSTRATE_MODE_ERROR 15u

// Multi-closure topology operator (4 bits, Phase 2+)
#define VIVID_SUBSTRATE_TOPOLOGY_NONE 0u
#define VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX 1u
#define VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER 2u
#define VIVID_SUBSTRATE_TOPOLOGY_GENERAL_TREE 3u

// Pixel-level and closure-level feature bits (8 bits)
#define VIVID_SUBSTRATE_FEATURE_OPENPBR_OPAQUE (1u << 0)
#define VIVID_SUBSTRATE_FEATURE_COAT (1u << 1)
#define VIVID_SUBSTRATE_FEATURE_FUZZ (1u << 2)
#define VIVID_SUBSTRATE_FEATURE_ANISOTROPY (1u << 3)
#define VIVID_SUBSTRATE_FEATURE_SUBSURFACE (1u << 4)
#define VIVID_SUBSTRATE_FEATURE_TRANSMISSION (1u << 5)
#define VIVID_SUBSTRATE_FEATURE_MULTI_CLOSURE (1u << 6)
#define VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL (1u << 7)

// Pixel header flags (3 bits)
#define VIVID_SUBSTRATE_FLAG_RECEIVE_DECALS (1u << 0)
#define VIVID_SUBSTRATE_FLAG_HAS_EMISSION (1u << 1)
#define VIVID_SUBSTRATE_FLAG_THIN_WALLED (1u << 2)

// Closure types (4 bits)
#define VIVID_SUBSTRATE_CLOSURE_TYPE_NONE 0u
#define VIVID_SUBSTRATE_CLOSURE_TYPE_OPENPBR_SLAB 1u
#define VIVID_SUBSTRATE_CLOSURE_TYPE_HAIR 2u
#define VIVID_SUBSTRATE_CLOSURE_TYPE_EYE 3u

// Slice 0 Header bit layout (4 + 3 + 4 + 8 + 6 + 3 + 4 = 32 bits)
#define VIVID_SUBSTRATE_HEADER_MODE_SHIFT 0u
#define VIVID_SUBSTRATE_HEADER_MODE_MASK 0xFu
#define VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_SHIFT 4u
#define VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_MASK 0x7u
#define VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_SHIFT 7u
#define VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_MASK 0xFu
#define VIVID_SUBSTRATE_HEADER_FEATURE_MASK_SHIFT 11u
#define VIVID_SUBSTRATE_HEADER_FEATURE_MASK_MASK 0xFFu
#define VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_SHIFT 19u
#define VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_MASK 0x3Fu
#define VIVID_SUBSTRATE_HEADER_FLAGS_SHIFT 25u
#define VIVID_SUBSTRATE_HEADER_FLAGS_MASK 0x7u
#define VIVID_SUBSTRATE_HEADER_ABI_VERSION_SHIFT 28u
#define VIVID_SUBSTRATE_HEADER_ABI_VERSION_MASK 0xFu

// Closure Word0 bit layout (4 + 8 + 8 + 8 + 4 = 32 bits)
#define VIVID_SUBSTRATE_CLOSURE_TYPE_SHIFT 0u
#define VIVID_SUBSTRATE_CLOSURE_TYPE_MASK 0xFu
#define VIVID_SUBSTRATE_CLOSURE_FEATURES_SHIFT 4u
#define VIVID_SUBSTRATE_CLOSURE_FEATURES_MASK 0xFFu
#define VIVID_SUBSTRATE_CLOSURE_WEIGHT_SHIFT 12u
#define VIVID_SUBSTRATE_CLOSURE_WEIGHT_MASK 0xFFu
#define VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_SHIFT 20u
#define VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_MASK 0xFFu
#define VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_SHIFT 28u
#define VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_MASK 0xFu

// Closure Word3 bit layout (10 + 10 + 12 = 32 bits)
#define VIVID_SUBSTRATE_ROUGHNESS_SHIFT 0u
#define VIVID_SUBSTRATE_ROUGHNESS_MASK 0x3FFu
#define VIVID_SUBSTRATE_METALNESS_SHIFT 10u
#define VIVID_SUBSTRATE_METALNESS_MASK 0x3FFu
#define VIVID_SUBSTRATE_IOR_SHIFT 20u
#define VIVID_SUBSTRATE_IOR_MASK 0xFFFu

struct VividSubstratePixelHeader
{
    uint mode;
    uint closureCount;
    uint topologyOp;
    uint featureMask;
    uint payloadUintCount;
    uint flags;
    uint abiVersion;
};

struct VividSubstrateOpenPBRClosureHeader
{
    uint closureType;
    uint featureBits;
    float closureWeight;
    float baseWeight;
    uint extensionUintCount;
};

uint VividSubstratePackUNorm(float value, uint maxValue)
{
    return (uint)min(round(saturate(value) * (float)maxValue), (float)maxValue);
}

float VividSubstrateUnpackUNorm(uint quantized, uint maxValue)
{
    return (float)quantized / (float)maxValue;
}

uint VividSubstratePackNormalOct16x2(float3 normalWS)
{
    float3 unitNormal = VividSanitizeSurfaceSummaryNormal(normalWS);
    float2 oct = PackNormalOctQuadEncode(unitNormal) * 0.5f + 0.5f;
    uint x = VividSubstratePackUNorm(oct.x, 0xFFFFu);
    uint y = VividSubstratePackUNorm(oct.y, 0xFFFFu);
    return x | (y << 16u);
}

float3 VividSubstrateUnpackNormalOct16x2(uint packedNormal)
{
    float2 oct = float2(
        VividSubstrateUnpackUNorm(packedNormal & 0xFFFFu, 0xFFFFu),
        VividSubstrateUnpackUNorm((packedNormal >> 16u) & 0xFFFFu, 0xFFFFu));
    return VividSanitizeSurfaceSummaryNormal(UnpackNormalOctQuadEncode(oct * 2.0f - 1.0f));
}

uint VividSubstratePackRGBA8(float4 value)
{
    uint r = VividSubstratePackUNorm(value.r, 0xFFu);
    uint g = VividSubstratePackUNorm(value.g, 0xFFu);
    uint b = VividSubstratePackUNorm(value.b, 0xFFu);
    uint a = VividSubstratePackUNorm(value.a, 0xFFu);
    return r | (g << 8u) | (b << 16u) | (a << 24u);
}

float4 VividSubstrateUnpackRGBA8(uint packed)
{
    return float4(
        VividSubstrateUnpackUNorm(packed & 0xFFu, 0xFFu),
        VividSubstrateUnpackUNorm((packed >> 8u) & 0xFFu, 0xFFu),
        VividSubstrateUnpackUNorm((packed >> 16u) & 0xFFu, 0xFFu),
        VividSubstrateUnpackUNorm((packed >> 24u) & 0xFFu, 0xFFu));
}

uint VividSubstratePackSpecularIor(float ior)
{
    const float iorRange = VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR
        - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR;
    float normalized = (ior - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR) / iorRange;
    return VividSubstratePackUNorm(normalized, VIVID_SUBSTRATE_IOR_MASK);
}

float VividSubstrateUnpackSpecularIor(uint quantized)
{
    const float iorRange = VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR
        - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR;
    return VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR
        + VividSubstrateUnpackUNorm(quantized & VIVID_SUBSTRATE_IOR_MASK, VIVID_SUBSTRATE_IOR_MASK) * iorRange;
}

uint VividPackSubstratePixelHeader(VividSubstratePixelHeader header)
{
    uint word = 0u;
    word |= (header.mode & VIVID_SUBSTRATE_HEADER_MODE_MASK) << VIVID_SUBSTRATE_HEADER_MODE_SHIFT;
    word |= (header.closureCount & VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_MASK) << VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_SHIFT;
    word |= (header.topologyOp & VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_MASK) << VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_SHIFT;
    word |= (header.featureMask & VIVID_SUBSTRATE_HEADER_FEATURE_MASK_MASK) << VIVID_SUBSTRATE_HEADER_FEATURE_MASK_SHIFT;
    word |= (header.payloadUintCount & VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_MASK) << VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_SHIFT;
    word |= (header.flags & VIVID_SUBSTRATE_HEADER_FLAGS_MASK) << VIVID_SUBSTRATE_HEADER_FLAGS_SHIFT;
    word |= (header.abiVersion & VIVID_SUBSTRATE_HEADER_ABI_VERSION_MASK) << VIVID_SUBSTRATE_HEADER_ABI_VERSION_SHIFT;
    return word;
}

VividSubstratePixelHeader VividUnpackSubstratePixelHeader(uint word)
{
    VividSubstratePixelHeader header;
    header.mode = (word >> VIVID_SUBSTRATE_HEADER_MODE_SHIFT) & VIVID_SUBSTRATE_HEADER_MODE_MASK;
    header.closureCount = (word >> VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_SHIFT) & VIVID_SUBSTRATE_HEADER_CLOSURE_COUNT_MASK;
    header.topologyOp = (word >> VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_SHIFT) & VIVID_SUBSTRATE_HEADER_TOPOLOGY_OP_MASK;
    header.featureMask = (word >> VIVID_SUBSTRATE_HEADER_FEATURE_MASK_SHIFT) & VIVID_SUBSTRATE_HEADER_FEATURE_MASK_MASK;
    header.payloadUintCount = (word >> VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_SHIFT) & VIVID_SUBSTRATE_HEADER_PAYLOAD_UINTS_MASK;
    header.flags = (word >> VIVID_SUBSTRATE_HEADER_FLAGS_SHIFT) & VIVID_SUBSTRATE_HEADER_FLAGS_MASK;
    header.abiVersion = (word >> VIVID_SUBSTRATE_HEADER_ABI_VERSION_SHIFT) & VIVID_SUBSTRATE_HEADER_ABI_VERSION_MASK;
    return header;
}

bool VividIsSubstrateHeaderValid(VividSubstratePixelHeader header)
{
    return header.abiVersion == VIVID_SUBSTRATE_ABI_VERSION
        && header.mode != VIVID_SUBSTRATE_MODE_EMPTY
        && header.mode != VIVID_SUBSTRATE_MODE_ERROR
        && header.closureCount > 0u
        && header.closureCount <= VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL
        && header.payloadUintCount > 0u
        && (VIVID_SUBSTRATE_HEADER_SLICE_COUNT + header.payloadUintCount) <= VIVID_SUBSTRATE_MAX_SLICE_COUNT;
}

void VividPackSubstrateOpenPBRCoreWords(
    VividOpenPBROpaqueInputs inputs,
    float closureWeight,
    uint closureFeatureBits,
    uint extensionUintCount,
    out uint word0,
    out uint word1,
    out uint word2,
    out uint word3,
    out uint word4)
{
    word0 = ((VIVID_SUBSTRATE_CLOSURE_TYPE_OPENPBR_SLAB & VIVID_SUBSTRATE_CLOSURE_TYPE_MASK) << VIVID_SUBSTRATE_CLOSURE_TYPE_SHIFT)
        | ((closureFeatureBits & VIVID_SUBSTRATE_CLOSURE_FEATURES_MASK) << VIVID_SUBSTRATE_CLOSURE_FEATURES_SHIFT)
        | ((VividSubstratePackUNorm(closureWeight, VIVID_SUBSTRATE_CLOSURE_WEIGHT_MASK)) << VIVID_SUBSTRATE_CLOSURE_WEIGHT_SHIFT)
        | ((VividSubstratePackUNorm(inputs.baseWeight, VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_MASK)) << VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_SHIFT)
        | ((extensionUintCount & VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_MASK) << VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_SHIFT);

    float3 srgbBaseColor = LinearToSRGB(saturate(inputs.baseColor));
    word1 = VividSubstratePackRGBA8(float4(srgbBaseColor, saturate(inputs.baseDiffuseRoughness)));
    word2 = VividSubstratePackRGBA8(float4(saturate(inputs.specularColor), saturate(inputs.specularWeight)));

    uint roughnessBits = VividSubstratePackUNorm(inputs.specularRoughness, VIVID_SUBSTRATE_ROUGHNESS_MASK);
    uint metalnessBits = VividSubstratePackUNorm(inputs.baseMetalness, VIVID_SUBSTRATE_METALNESS_MASK);
    uint iorBits = VividSubstratePackSpecularIor(inputs.specularIor);
    word3 = (roughnessBits << VIVID_SUBSTRATE_ROUGHNESS_SHIFT)
        | (metalnessBits << VIVID_SUBSTRATE_METALNESS_SHIFT)
        | (iorBits << VIVID_SUBSTRATE_IOR_SHIFT);

    word4 = VividSubstratePackNormalOct16x2(inputs.normalWS);
}

bool VividTryUnpackSubstrateOpenPBRCoreWords(
    uint word0,
    uint word1,
    uint word2,
    uint word3,
    uint word4,
    out VividSubstrateOpenPBRClosureHeader closureHeader,
    out VividOpenPBROpaqueInputs inputs)
{
    closureHeader.closureType = (word0 >> VIVID_SUBSTRATE_CLOSURE_TYPE_SHIFT) & VIVID_SUBSTRATE_CLOSURE_TYPE_MASK;
    closureHeader.featureBits = (word0 >> VIVID_SUBSTRATE_CLOSURE_FEATURES_SHIFT) & VIVID_SUBSTRATE_CLOSURE_FEATURES_MASK;
    closureHeader.closureWeight = VividSubstrateUnpackUNorm(
        (word0 >> VIVID_SUBSTRATE_CLOSURE_WEIGHT_SHIFT) & VIVID_SUBSTRATE_CLOSURE_WEIGHT_MASK,
        VIVID_SUBSTRATE_CLOSURE_WEIGHT_MASK);
    closureHeader.baseWeight = VividSubstrateUnpackUNorm(
        (word0 >> VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_SHIFT) & VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_MASK,
        VIVID_SUBSTRATE_CLOSURE_BASE_WEIGHT_MASK);
    closureHeader.extensionUintCount = (word0 >> VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_SHIFT) & VIVID_SUBSTRATE_CLOSURE_EXT_UINTS_MASK;

    float4 baseAndDiffRough = VividSubstrateUnpackRGBA8(word1);
    float4 specAndWeight = VividSubstrateUnpackRGBA8(word2);

    inputs = (VividOpenPBROpaqueInputs)0;
    inputs.baseWeight = closureHeader.baseWeight;
    inputs.baseColor = SRGBToLinear(baseAndDiffRough.rgb);
    inputs.baseDiffuseRoughness = baseAndDiffRough.a;
    inputs.specularColor = specAndWeight.rgb;
    inputs.specularWeight = specAndWeight.a;
    inputs.specularRoughness = VividSubstrateUnpackUNorm(
        (word3 >> VIVID_SUBSTRATE_ROUGHNESS_SHIFT) & VIVID_SUBSTRATE_ROUGHNESS_MASK,
        VIVID_SUBSTRATE_ROUGHNESS_MASK);
    inputs.baseMetalness = VividSubstrateUnpackUNorm(
        (word3 >> VIVID_SUBSTRATE_METALNESS_SHIFT) & VIVID_SUBSTRATE_METALNESS_MASK,
        VIVID_SUBSTRATE_METALNESS_MASK);
    inputs.specularIor = VividSubstrateUnpackSpecularIor(
        (word3 >> VIVID_SUBSTRATE_IOR_SHIFT) & VIVID_SUBSTRATE_IOR_MASK);
    inputs.normalWS = VividSubstrateUnpackNormalOct16x2(word4);
    // Emission is stored in TopLayer Summary RT3 and composed once by Deferred.
    inputs.emissionLuminance = 0.0f;
    inputs.emissionColor = 1.0f.xxx;

    return closureHeader.closureType == VIVID_SUBSTRATE_CLOSURE_TYPE_OPENPBR_SLAB
        && VividValidateOpenPBROpaqueInputs(inputs, 0u) == 0u;
}

void VividWriteSubstrateEmpty(RWTexture2DArray<uint> closureBuffer, uint2 pixelCoord)
{
    closureBuffer[uint3(pixelCoord, 0u)] = 0u;
}

void VividWriteSubstrateOpenPBROpaqueSingle(
    RWTexture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    VividOpenPBROpaqueInputs inputs,
    uint headerFlags)
{
    VividSubstratePixelHeader header;
    header.mode = VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE;
    header.closureCount = 1u;
    header.topologyOp = VIVID_SUBSTRATE_TOPOLOGY_NONE;
    header.featureMask = VIVID_SUBSTRATE_FEATURE_OPENPBR_OPAQUE;
    header.payloadUintCount = VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT;
    header.flags = headerFlags;
    header.abiVersion = VIVID_SUBSTRATE_ABI_VERSION;

    uint word0, word1, word2, word3, word4;
    VividPackSubstrateOpenPBRCoreWords(
        inputs,
        1.0f,
        VIVID_SUBSTRATE_FEATURE_OPENPBR_OPAQUE,
        0u,
        word0,
        word1,
        word2,
        word3,
        word4);

    closureBuffer[uint3(pixelCoord, 0u)] = VividPackSubstratePixelHeader(header);
    closureBuffer[uint3(pixelCoord, 1u)] = word0;
    closureBuffer[uint3(pixelCoord, 2u)] = word1;
    closureBuffer[uint3(pixelCoord, 3u)] = word2;
    closureBuffer[uint3(pixelCoord, 4u)] = word3;
    closureBuffer[uint3(pixelCoord, 5u)] = word4;
}

bool VividTryLoadSubstrateOpenPBROpaqueSingle(
    Texture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    out VividOpenPBROpaqueInputs inputs)
{
    inputs = (VividOpenPBROpaqueInputs)0;
    uint headerWord = closureBuffer.Load(int4(pixelCoord, 0, 0));
    VividSubstratePixelHeader header = VividUnpackSubstratePixelHeader(headerWord);
    if (!VividIsSubstrateHeaderValid(header)
        || header.mode != VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE
        || header.closureCount != 1u
        || header.payloadUintCount < VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT)
    {
        return false;
    }

    uint word0 = closureBuffer.Load(int4(pixelCoord, 1, 0));
    uint word1 = closureBuffer.Load(int4(pixelCoord, 2, 0));
    uint word2 = closureBuffer.Load(int4(pixelCoord, 3, 0));
    uint word3 = closureBuffer.Load(int4(pixelCoord, 4, 0));
    uint word4 = closureBuffer.Load(int4(pixelCoord, 5, 0));

    VividSubstrateOpenPBRClosureHeader closureHeader;
    return VividTryUnpackSubstrateOpenPBRCoreWords(
        word0,
        word1,
        word2,
        word3,
        word4,
        closureHeader,
        inputs);
}

// Maps Slice 0 Substrate header directly to the 4-tier deferred tile mask.
// Returns 0u when the pixel has no valid Substrate payload (fall back to GBuffer0 class).
uint VividGetSubstrateTileClassBit(uint headerWord)
{
    if (headerWord == 0u)
        return 0u;

    VividSubstratePixelHeader header = VividUnpackSubstratePixelHeader(headerWord);
    if (header.abiVersion != VIVID_SUBSTRATE_ABI_VERSION || header.mode == VIVID_SUBSTRATE_MODE_ERROR)
        return VIVID_DEFERRED_CLASS_BIT_CATCH_ALL;

    if (header.mode == VIVID_SUBSTRATE_MODE_EMPTY)
        return 0u;

    if (header.mode == VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE)
        return VIVID_DEFERRED_CLASS_BIT_GENERAL_SLAB;

    if (header.mode == VIVID_SUBSTRATE_MODE_SINGLE_COMPLEX)
        return VIVID_DEFERRED_CLASS_BIT_DUAL_SLAB;

    return VIVID_DEFERRED_CLASS_BIT_CATCH_ALL;
}

#endif // VIVIDRP_SUBSTRATE_BUFFER_INCLUDED
