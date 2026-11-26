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
#define VIVID_SUBSTRATE_COAT_BASE_EXT_UINTS 2u
#define VIVID_SUBSTRATE_COAT_DUAL_NORMAL_EXT_UINTS 3u
#define VIVID_SUBSTRATE_FUZZ_EXT_UINTS 2u
#define VIVID_SUBSTRATE_ANISO_EXT_UINTS 2u
#define VIVID_SUBSTRATE_SINGLE_COMPLEX_MAX_SLICES 13u

#define VIVID_SUBSTRATE_COAT_FLAG_INDEPENDENT_NORMAL (1u << 0)
#define VIVID_SUBSTRATE_ANISO_FLAG_NEGATIVE_HANDEDNESS (1u << 0)

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

// Phase 2.2 Multi-Closure Operator Word bit layout (2 + 8 + 7 + 15 = 32 bits)
#define VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL 3u
#define VIVID_SUBSTRATE_OP_WORD_UINTS 1u
#define VIVID_SUBSTRATE_MAX_OPTICAL_THICKNESS 4.0f
#define VIVID_SUBSTRATE_OP_KIND_SHIFT 0u
#define VIVID_SUBSTRATE_OP_KIND_MASK 0x3u
#define VIVID_SUBSTRATE_OP_WEIGHT_SHIFT 2u
#define VIVID_SUBSTRATE_OP_WEIGHT_MASK 0xFFu
#define VIVID_SUBSTRATE_OP_THICKNESS_SHIFT 10u
#define VIVID_SUBSTRATE_OP_THICKNESS_MASK 0x7Fu
#define VIVID_SUBSTRATE_OP_TRANS_R_SHIFT 17u
#define VIVID_SUBSTRATE_OP_TRANS_G_SHIFT 22u
#define VIVID_SUBSTRATE_OP_TRANS_B_SHIFT 27u
#define VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK 0x1Fu

struct VividSubstrateOperatorDesc
{
    uint opKind;
    float weight;
    float opticalThickness;
    float3 transmittanceColor; // linear RGB in [0, 1]
};

VividSubstrateOperatorDesc VividCreateDefaultSubstrateOperatorDesc(uint opKind, float weight)
{
    VividSubstrateOperatorDesc op;
    op.opKind = opKind;
    op.weight = saturate(weight);
    op.opticalThickness = 0.0f;
    op.transmittanceColor = 1.0f.xxx;
    return op;
}

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

// Phase 2.1 OpenPBR V2 single-closure inputs (superset of VividOpenPBROpaqueInputs).
// Supports Coat (with optional independent normal), Fuzz/Sheen, and Specular Anisotropy.
struct VividOpenPBRV2Inputs
{
    VividOpenPBROpaqueInputs core;
    // Coat
    float coatWeight;
    float3 coatColor;
    float coatRoughness;
    float coatIor;
    float coatDarkening;
    float3 coatNormalWS;
    uint hasIndependentCoatNormal;
    // Fuzz / Sheen
    float fuzzWeight;
    float3 fuzzColor;
    float fuzzRoughness;
    // Specular Anisotropy
    float specularAnisotropy;
    float specularAnisotropyRotation; // radians in [0, TWO_PI)
    float3 tangentWS;
    float tangentHandedness; // +1.0 or -1.0
};

VividOpenPBRV2Inputs VividCreateDefaultOpenPBRV2Inputs()
{
    VividOpenPBRV2Inputs inputs;
    inputs.core = VividCreateDefaultOpenPBROpaqueInputs();
    inputs.coatWeight = 0.0f;
    inputs.coatColor = 1.0f.xxx;
    inputs.coatRoughness = 0.0f;
    inputs.coatIor = 1.6f;
    inputs.coatDarkening = 1.0f;
    inputs.coatNormalWS = inputs.core.normalWS;
    inputs.hasIndependentCoatNormal = 0u;
    inputs.fuzzWeight = 0.0f;
    inputs.fuzzColor = 1.0f.xxx;
    inputs.fuzzRoughness = 0.5f;
    inputs.specularAnisotropy = 0.0f;
    inputs.specularAnisotropyRotation = 0.0f;
    inputs.tangentWS = float3(1.0f, 0.0f, 0.0f);
    inputs.tangentHandedness = 1.0f;
    return inputs;
}

uint VividSubstrateGetOpenPBRV2FeatureMask(VividOpenPBRV2Inputs inputs)
{
    uint mask = VIVID_SUBSTRATE_FEATURE_OPENPBR_OPAQUE;
    if (inputs.coatWeight > 0.0f)
    {
        mask |= VIVID_SUBSTRATE_FEATURE_COAT;
        if (inputs.hasIndependentCoatNormal != 0u)
            mask |= VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL;
    }
    if (inputs.fuzzWeight > 0.0f)
        mask |= VIVID_SUBSTRATE_FEATURE_FUZZ;
    if (inputs.specularAnisotropy > 0.0f)
        mask |= VIVID_SUBSTRATE_FEATURE_ANISOTROPY;
    return mask;
}

uint VividSubstrateComputeOpenPBRV2ExtensionUints(uint featureMask)
{
    uint ext = 0u;
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_COAT) != 0u)
    {
        ext += (featureMask & VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL) != 0u
            ? VIVID_SUBSTRATE_COAT_DUAL_NORMAL_EXT_UINTS
            : VIVID_SUBSTRATE_COAT_BASE_EXT_UINTS;
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_FUZZ) != 0u)
        ext += VIVID_SUBSTRATE_FUZZ_EXT_UINTS;
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_ANISOTROPY) != 0u)
        ext += VIVID_SUBSTRATE_ANISO_EXT_UINTS;
    return ext;
}

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

uint VividSubstratePackCoatIor8(float ior)
{
    const float iorRange = VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR
        - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR;
    float normalized = (ior - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR) / iorRange;
    return VividSubstratePackUNorm(normalized, 0xFFu);
}

float VividSubstrateUnpackCoatIor8(uint quantized)
{
    const float iorRange = VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR
        - VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR;
    return VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR
        + VividSubstrateUnpackUNorm(quantized & 0xFFu, 0xFFu) * iorRange;
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

// Writes an OpenPBR V2 single-closure record (6 uints when Simple, up to 13 uints = 52 B/px
// when Coat + independent normal + Fuzz + Anisotropy are all active).
void VividWriteSubstrateOpenPBRV2Single(
    RWTexture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    VividOpenPBRV2Inputs inputs,
    uint headerFlags)
{
    uint featureMask = VividSubstrateGetOpenPBRV2FeatureMask(inputs);
    uint extUints = VividSubstrateComputeOpenPBRV2ExtensionUints(featureMask);
    bool isComplex = extUints > 0u;

    VividSubstratePixelHeader header;
    header.mode = isComplex ? VIVID_SUBSTRATE_MODE_SINGLE_COMPLEX : VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE;
    header.closureCount = 1u;
    header.topologyOp = VIVID_SUBSTRATE_TOPOLOGY_NONE;
    header.featureMask = featureMask;
    header.payloadUintCount = VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT + extUints;
    header.flags = headerFlags;
    header.abiVersion = VIVID_SUBSTRATE_ABI_VERSION;

    uint word0, word1, word2, word3, word4;
    VividPackSubstrateOpenPBRCoreWords(
        inputs.core,
        1.0f,
        featureMask,
        extUints,
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

    uint sliceCursor = 6u;
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_COAT) != 0u)
    {
        uint coat0 = VividSubstratePackUNorm(inputs.coatWeight, 0xFFu)
            | (VividSubstratePackUNorm(inputs.coatRoughness, 0xFFu) << 8u)
            | (VividSubstratePackUNorm(inputs.coatDarkening, 0xFFu) << 16u)
            | (VividSubstratePackCoatIor8(inputs.coatIor) << 24u);
        uint coatFlags = (featureMask & VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL) != 0u
            ? VIVID_SUBSTRATE_COAT_FLAG_INDEPENDENT_NORMAL
            : 0u;
        float3 srgbCoatColor = LinearToSRGB(saturate(inputs.coatColor));
        uint coat1 = (VividSubstratePackUNorm(srgbCoatColor.r, 0xFFu))
            | (VividSubstratePackUNorm(srgbCoatColor.g, 0xFFu) << 8u)
            | (VividSubstratePackUNorm(srgbCoatColor.b, 0xFFu) << 16u)
            | ((coatFlags & 0xFFu) << 24u);
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = coat0;
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = coat1;
        if ((featureMask & VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL) != 0u)
        {
            closureBuffer[uint3(pixelCoord, sliceCursor++)] = VividSubstratePackNormalOct16x2(inputs.coatNormalWS);
        }
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_FUZZ) != 0u)
    {
        float3 srgbFuzzColor = LinearToSRGB(saturate(inputs.fuzzColor));
        uint fuzz0 = VividSubstratePackRGBA8(float4(srgbFuzzColor, saturate(inputs.fuzzWeight)));
        uint fuzz1 = VividSubstratePackUNorm(inputs.fuzzRoughness, 0xFFu);
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = fuzz0;
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = fuzz1;
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_ANISOTROPY) != 0u)
    {
        float wrappedAngle = frac(inputs.specularAnisotropyRotation / TWO_PI);
        uint anisoFlags = inputs.tangentHandedness < 0.0f ? VIVID_SUBSTRATE_ANISO_FLAG_NEGATIVE_HANDEDNESS : 0u;
        uint aniso0 = VividSubstratePackUNorm(inputs.specularAnisotropy, 0xFFu)
            | (VividSubstratePackUNorm(wrappedAngle, 0xFFu) << 8u)
            | ((anisoFlags & 0xFFu) << 16u);
        uint aniso1 = VividSubstratePackNormalOct16x2(inputs.tangentWS);
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = aniso0;
        closureBuffer[uint3(pixelCoord, sliceCursor++)] = aniso1;
    }
}

bool VividTryLoadSubstrateOpenPBRClosureAt(
    Texture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    uint startSlice,
    uint maxSlices,
    out VividSubstrateOpenPBRClosureHeader closureHeader,
    out VividOpenPBRV2Inputs inputs,
    out uint nextSlice)
{
    closureHeader = (VividSubstrateOpenPBRClosureHeader)0;
    inputs = VividCreateDefaultOpenPBRV2Inputs();
    nextSlice = startSlice;

    if (startSlice + VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT > maxSlices)
        return false;

    uint word0 = closureBuffer.Load(int4(pixelCoord, (int)(startSlice + 0u), 0));
    uint word1 = closureBuffer.Load(int4(pixelCoord, (int)(startSlice + 1u), 0));
    uint word2 = closureBuffer.Load(int4(pixelCoord, (int)(startSlice + 2u), 0));
    uint word3 = closureBuffer.Load(int4(pixelCoord, (int)(startSlice + 3u), 0));
    uint word4 = closureBuffer.Load(int4(pixelCoord, (int)(startSlice + 4u), 0));

    if (!VividTryUnpackSubstrateOpenPBRCoreWords(
            word0,
            word1,
            word2,
            word3,
            word4,
            closureHeader,
            inputs.core))
    {
        return false;
    }

    inputs.coatNormalWS = inputs.core.normalWS;
    uint sliceCursor = startSlice + VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT;
    uint expectedEnd = sliceCursor + closureHeader.extensionUintCount;
    if (expectedEnd > maxSlices)
        return false;

    uint featureMask = closureHeader.featureBits;
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_COAT) != 0u)
    {
        if (sliceCursor + VIVID_SUBSTRATE_COAT_BASE_EXT_UINTS > expectedEnd)
            return false;
        uint coat0 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        uint coat1 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        inputs.coatWeight = VividSubstrateUnpackUNorm(coat0 & 0xFFu, 0xFFu);
        inputs.coatRoughness = VividSubstrateUnpackUNorm((coat0 >> 8u) & 0xFFu, 0xFFu);
        inputs.coatDarkening = VividSubstrateUnpackUNorm((coat0 >> 16u) & 0xFFu, 0xFFu);
        inputs.coatIor = VividSubstrateUnpackCoatIor8((coat0 >> 24u) & 0xFFu);
        float4 coatColorAndFlags = VividSubstrateUnpackRGBA8(coat1);
        inputs.coatColor = SRGBToLinear(coatColorAndFlags.rgb);
        uint coatFlags = (coat1 >> 24u) & 0xFFu;
        if ((coatFlags & VIVID_SUBSTRATE_COAT_FLAG_INDEPENDENT_NORMAL) != 0u)
        {
            if (sliceCursor >= expectedEnd)
                return false;
            uint coatNormalPacked = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
            inputs.coatNormalWS = VividSubstrateUnpackNormalOct16x2(coatNormalPacked);
            inputs.hasIndependentCoatNormal = 1u;
        }
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_FUZZ) != 0u)
    {
        if (sliceCursor + VIVID_SUBSTRATE_FUZZ_EXT_UINTS > expectedEnd)
            return false;
        uint fuzz0 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        uint fuzz1 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        float4 fuzzColorAndWeight = VividSubstrateUnpackRGBA8(fuzz0);
        inputs.fuzzColor = SRGBToLinear(fuzzColorAndWeight.rgb);
        inputs.fuzzWeight = fuzzColorAndWeight.a;
        inputs.fuzzRoughness = VividSubstrateUnpackUNorm(fuzz1 & 0xFFu, 0xFFu);
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_ANISOTROPY) != 0u)
    {
        if (sliceCursor + VIVID_SUBSTRATE_ANISO_EXT_UINTS > expectedEnd)
            return false;
        uint aniso0 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        uint aniso1 = closureBuffer.Load(int4(pixelCoord, (int)(sliceCursor++), 0));
        inputs.specularAnisotropy = VividSubstrateUnpackUNorm(aniso0 & 0xFFu, 0xFFu);
        inputs.specularAnisotropyRotation = VividSubstrateUnpackUNorm((aniso0 >> 8u) & 0xFFu, 0xFFu) * TWO_PI;
        uint anisoFlags = (aniso0 >> 16u) & 0xFFu;
        inputs.tangentHandedness = (anisoFlags & VIVID_SUBSTRATE_ANISO_FLAG_NEGATIVE_HANDEDNESS) != 0u ? -1.0f : 1.0f;
        inputs.tangentWS = VividSubstrateUnpackNormalOct16x2(aniso1);
    }

    nextSlice = expectedEnd;
    return true;
}

bool VividTryLoadSubstrateOpenPBRV2Single(
    Texture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    out VividOpenPBRV2Inputs inputs,
    out VividSubstratePixelHeader header)
{
    inputs = VividCreateDefaultOpenPBRV2Inputs();
    uint headerWord = closureBuffer.Load(int4(pixelCoord, 0, 0));
    header = VividUnpackSubstratePixelHeader(headerWord);
    if (!VividIsSubstrateHeaderValid(header)
        || (header.mode != VIVID_SUBSTRATE_MODE_SINGLE_SIMPLE
            && header.mode != VIVID_SUBSTRATE_MODE_SINGLE_COMPLEX)
        || header.closureCount != 1u)
    {
        return false;
    }

    uint maxSlices = VIVID_SUBSTRATE_HEADER_SLICE_COUNT + header.payloadUintCount;
    VividSubstrateOpenPBRClosureHeader closureHeader;
    uint nextSlice;
    return VividTryLoadSubstrateOpenPBRClosureAt(
        closureBuffer,
        pixelCoord,
        VIVID_SUBSTRATE_HEADER_SLICE_COUNT,
        maxSlices,
        closureHeader,
        inputs,
        nextSlice);
}

uint VividPackSubstrateOperatorWord(VividSubstrateOperatorDesc op)
{
    uint opKind = op.opKind & VIVID_SUBSTRATE_OP_KIND_MASK;
    uint weightBits = VividSubstratePackUNorm(op.weight, VIVID_SUBSTRATE_OP_WEIGHT_MASK);
    uint thicknessBits = VividSubstratePackUNorm(
        op.opticalThickness / VIVID_SUBSTRATE_MAX_OPTICAL_THICKNESS,
        VIVID_SUBSTRATE_OP_THICKNESS_MASK);
    float3 srgbTrans = LinearToSRGB(saturate(op.transmittanceColor));
    uint r5 = VividSubstratePackUNorm(srgbTrans.r, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK);
    uint g5 = VividSubstratePackUNorm(srgbTrans.g, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK);
    uint b5 = VividSubstratePackUNorm(srgbTrans.b, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK);

    return (opKind << VIVID_SUBSTRATE_OP_KIND_SHIFT)
        | (weightBits << VIVID_SUBSTRATE_OP_WEIGHT_SHIFT)
        | (thicknessBits << VIVID_SUBSTRATE_OP_THICKNESS_SHIFT)
        | (r5 << VIVID_SUBSTRATE_OP_TRANS_R_SHIFT)
        | (g5 << VIVID_SUBSTRATE_OP_TRANS_G_SHIFT)
        | (b5 << VIVID_SUBSTRATE_OP_TRANS_B_SHIFT);
}

VividSubstrateOperatorDesc VividUnpackSubstrateOperatorWord(uint word)
{
    VividSubstrateOperatorDesc op;
    op.opKind = (word >> VIVID_SUBSTRATE_OP_KIND_SHIFT) & VIVID_SUBSTRATE_OP_KIND_MASK;
    op.weight = VividSubstrateUnpackUNorm(
        (word >> VIVID_SUBSTRATE_OP_WEIGHT_SHIFT) & VIVID_SUBSTRATE_OP_WEIGHT_MASK,
        VIVID_SUBSTRATE_OP_WEIGHT_MASK);
    op.opticalThickness = VividSubstrateUnpackUNorm(
        (word >> VIVID_SUBSTRATE_OP_THICKNESS_SHIFT) & VIVID_SUBSTRATE_OP_THICKNESS_MASK,
        VIVID_SUBSTRATE_OP_THICKNESS_MASK) * VIVID_SUBSTRATE_MAX_OPTICAL_THICKNESS;
    float3 srgbTrans = float3(
        VividSubstrateUnpackUNorm((word >> VIVID_SUBSTRATE_OP_TRANS_R_SHIFT) & VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK),
        VividSubstrateUnpackUNorm((word >> VIVID_SUBSTRATE_OP_TRANS_G_SHIFT) & VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK),
        VividSubstrateUnpackUNorm((word >> VIVID_SUBSTRATE_OP_TRANS_B_SHIFT) & VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK, VIVID_SUBSTRATE_OP_TRANS_CHANNEL_MASK));
    op.transmittanceColor = SRGBToLinear(srgbTrans);
    return op;
}

void VividWriteSubstrateOpenPBRClosureAt(
    RWTexture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    uint startSlice,
    VividOpenPBRV2Inputs inputs,
    float closureWeight,
    out uint nextSlice)
{
    uint featureMask = VividSubstrateGetOpenPBRV2FeatureMask(inputs);
    uint extUints = VividSubstrateComputeOpenPBRV2ExtensionUints(featureMask);

    uint word0, word1, word2, word3, word4;
    VividPackSubstrateOpenPBRCoreWords(
        inputs.core,
        closureWeight,
        featureMask,
        extUints,
        word0,
        word1,
        word2,
        word3,
        word4);

    uint cursor = startSlice;
    closureBuffer[uint3(pixelCoord, cursor++)] = word0;
    closureBuffer[uint3(pixelCoord, cursor++)] = word1;
    closureBuffer[uint3(pixelCoord, cursor++)] = word2;
    closureBuffer[uint3(pixelCoord, cursor++)] = word3;
    closureBuffer[uint3(pixelCoord, cursor++)] = word4;

    if ((featureMask & VIVID_SUBSTRATE_FEATURE_COAT) != 0u)
    {
        uint coat0 = VividSubstratePackUNorm(inputs.coatWeight, 0xFFu)
            | (VividSubstratePackUNorm(inputs.coatRoughness, 0xFFu) << 8u)
            | (VividSubstratePackUNorm(inputs.coatDarkening, 0xFFu) << 16u)
            | (VividSubstratePackCoatIor8(inputs.coatIor) << 24u);
        uint coatFlags = (featureMask & VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL) != 0u
            ? VIVID_SUBSTRATE_COAT_FLAG_INDEPENDENT_NORMAL
            : 0u;
        float3 srgbCoatColor = LinearToSRGB(saturate(inputs.coatColor));
        uint coat1 = (VividSubstratePackUNorm(srgbCoatColor.r, 0xFFu))
            | (VividSubstratePackUNorm(srgbCoatColor.g, 0xFFu) << 8u)
            | (VividSubstratePackUNorm(srgbCoatColor.b, 0xFFu) << 16u)
            | ((coatFlags & 0xFFu) << 24u);
        closureBuffer[uint3(pixelCoord, cursor++)] = coat0;
        closureBuffer[uint3(pixelCoord, cursor++)] = coat1;
        if ((featureMask & VIVID_SUBSTRATE_FEATURE_DUAL_NORMAL) != 0u)
            closureBuffer[uint3(pixelCoord, cursor++)] = VividSubstratePackNormalOct16x2(inputs.coatNormalWS);
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_FUZZ) != 0u)
    {
        float3 srgbFuzzColor = LinearToSRGB(saturate(inputs.fuzzColor));
        uint fuzz0 = VividSubstratePackRGBA8(float4(srgbFuzzColor, saturate(inputs.fuzzWeight)));
        uint fuzz1 = VividSubstratePackUNorm(inputs.fuzzRoughness, 0xFFu);
        closureBuffer[uint3(pixelCoord, cursor++)] = fuzz0;
        closureBuffer[uint3(pixelCoord, cursor++)] = fuzz1;
    }
    if ((featureMask & VIVID_SUBSTRATE_FEATURE_ANISOTROPY) != 0u)
    {
        float wrappedAngle = frac(inputs.specularAnisotropyRotation / TWO_PI);
        uint anisoFlags = inputs.tangentHandedness < 0.0f ? VIVID_SUBSTRATE_ANISO_FLAG_NEGATIVE_HANDEDNESS : 0u;
        uint aniso0 = VividSubstratePackUNorm(inputs.specularAnisotropy, 0xFFu)
            | (VividSubstratePackUNorm(wrappedAngle, 0xFFu) << 8u)
            | ((anisoFlags & 0xFFu) << 16u);
        uint aniso1 = VividSubstratePackNormalOct16x2(inputs.tangentWS);
        closureBuffer[uint3(pixelCoord, cursor++)] = aniso0;
        closureBuffer[uint3(pixelCoord, cursor++)] = aniso1;
    }

    nextSlice = cursor;
}

// Writes a Multi-Closure Substrate record (2..4 OpenPBR closures + N-1 operator words).
// Layout: Slice 0 = PixelHeader, Slices [1 .. N-1] = Operator words, Slices [N ..] = Closures [0 .. N-1].
// Returns false and writes an explicit ERROR header if the stack exceeds maxSliceBudget.
bool VividWriteSubstrateMultiClosure(
    RWTexture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    uint closureCount,
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL],
    uint headerFlags,
    uint maxSliceBudget)
{
    if (closureCount < 2u || closureCount > VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL)
    {
        VividSubstratePixelHeader errHeader = (VividSubstratePixelHeader)0;
        errHeader.mode = VIVID_SUBSTRATE_MODE_ERROR;
        errHeader.abiVersion = VIVID_SUBSTRATE_ABI_VERSION;
        closureBuffer[uint3(pixelCoord, 0u)] = VividPackSubstratePixelHeader(errHeader);
        return false;
    }

    uint opCount = closureCount - 1u;
    uint combinedFeatures = VIVID_SUBSTRATE_FEATURE_OPENPBR_OPAQUE | VIVID_SUBSTRATE_FEATURE_MULTI_CLOSURE;
    uint payloadUints = opCount * VIVID_SUBSTRATE_OP_WORD_UINTS;
    [unroll]
    for (uint i = 0u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
    {
        if (i < closureCount)
        {
            uint fm = VividSubstrateGetOpenPBRV2FeatureMask(closures[i]);
            combinedFeatures |= fm;
            payloadUints += VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT + VividSubstrateComputeOpenPBRV2ExtensionUints(fm);
        }
    }

    uint effectiveBudget = min(maxSliceBudget, VIVID_SUBSTRATE_MAX_SLICE_COUNT);
    if ((VIVID_SUBSTRATE_HEADER_SLICE_COUNT + payloadUints) > effectiveBudget)
    {
        VividSubstratePixelHeader errHeader = (VividSubstratePixelHeader)0;
        errHeader.mode = VIVID_SUBSTRATE_MODE_ERROR;
        errHeader.abiVersion = VIVID_SUBSTRATE_ABI_VERSION;
        closureBuffer[uint3(pixelCoord, 0u)] = VividPackSubstratePixelHeader(errHeader);
        return false;
    }

    bool hasHMix = false;
    bool hasVLayer = false;
    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx < opCount)
        {
            if (operators[opIdx].opKind == VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX)
                hasHMix = true;
            else if (operators[opIdx].opKind == VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER)
                hasVLayer = true;
        }
    }

    VividSubstratePixelHeader header;
    header.mode = VIVID_SUBSTRATE_MODE_MULTI_CLOSURE;
    header.closureCount = closureCount;
    header.topologyOp = (hasHMix && hasVLayer)
        ? VIVID_SUBSTRATE_TOPOLOGY_GENERAL_TREE
        : (hasVLayer ? VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER : VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX);
    header.featureMask = combinedFeatures;
    header.payloadUintCount = payloadUints;
    header.flags = headerFlags;
    header.abiVersion = VIVID_SUBSTRATE_ABI_VERSION;

    closureBuffer[uint3(pixelCoord, 0u)] = VividPackSubstratePixelHeader(header);

    uint cursor = VIVID_SUBSTRATE_HEADER_SLICE_COUNT;
    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx < opCount)
            closureBuffer[uint3(pixelCoord, cursor++)] = VividPackSubstrateOperatorWord(operators[opIdx]);
    }

    [unroll]
    for (uint cIdx = 0u; cIdx < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++cIdx)
    {
        if (cIdx < closureCount)
        {
            uint nextSlice;
            VividWriteSubstrateOpenPBRClosureAt(closureBuffer, pixelCoord, cursor, closures[cIdx], 1.0f, nextSlice);
            cursor = nextSlice;
        }
    }
    return true;
}

bool VividTryLoadSubstrateMultiClosure(
    Texture2DArray<uint> closureBuffer,
    uint2 pixelCoord,
    out VividSubstratePixelHeader header,
    out VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    out VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL])
{
    [unroll]
    for (uint i = 0u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
        closures[i] = VividCreateDefaultOpenPBRV2Inputs();
    [unroll]
    for (uint j = 0u; j < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++j)
        operators[j] = VividCreateDefaultSubstrateOperatorDesc(VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX, 0.0f);

    uint headerWord = closureBuffer.Load(int4(pixelCoord, 0, 0));
    header = VividUnpackSubstratePixelHeader(headerWord);
    if (!VividIsSubstrateHeaderValid(header)
        || header.mode != VIVID_SUBSTRATE_MODE_MULTI_CLOSURE
        || header.closureCount < 2u
        || header.closureCount > VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL)
    {
        return false;
    }

    uint opCount = header.closureCount - 1u;
    uint maxSlices = VIVID_SUBSTRATE_HEADER_SLICE_COUNT + header.payloadUintCount;
    uint cursor = VIVID_SUBSTRATE_HEADER_SLICE_COUNT;
    if (cursor + opCount > maxSlices)
        return false;

    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx < opCount)
        {
            uint opWord = closureBuffer.Load(int4(pixelCoord, (int)(cursor++), 0));
            operators[opIdx] = VividUnpackSubstrateOperatorWord(opWord);
        }
    }

    [unroll]
    for (uint cIdx = 0u; cIdx < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++cIdx)
    {
        if (cIdx < header.closureCount)
        {
            VividSubstrateOpenPBRClosureHeader cHeader;
            uint nextSlice;
            if (!VividTryLoadSubstrateOpenPBRClosureAt(
                    closureBuffer,
                    pixelCoord,
                    cursor,
                    maxSlices,
                    cHeader,
                    closures[cIdx],
                    nextSlice))
            {
                return false;
            }
            cursor = nextSlice;
        }
    }
    return true;
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
