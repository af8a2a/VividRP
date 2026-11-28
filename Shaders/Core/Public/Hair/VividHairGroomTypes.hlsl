#ifndef VIVIDRP_HAIR_GROOM_TYPES_HLSL
#define VIVIDRP_HAIR_GROOM_TYPES_HLSL

// Shared GPU ABI between HairGroomData.cs and Strand-Based Hair compute/graphics shaders.
// All structs match [StructLayout(LayoutKind.Sequential)] in C#.

#define VIVID_HAIR_MAX_SEGMENTS_PER_MODEL 0xFFFFFu
#define VIVID_HAIR_MAX_STRANDS_PER_MODEL  0xFFFFFu
#define VIVID_HAIR_VIS_DEPTH_BITS         12u
#define VIVID_HAIR_VIS_SEG_BITS           20u
#define VIVID_HAIR_VIS_DEPTH_MAX          4095.0f
#define VIVID_HAIR_VIS_EMPTY              0xFFFFFFFFu

// 16 bytes
struct HairGroomStrandDesc
{
    uint firstVertex;
    uint vertexCount;
    uint rootUVPacked;
    uint clumpAndSeed;
};

// 32 bytes
struct HairGroomRestVertex
{
    float3 positionOS;
    float radius;
    float3 tangentOS;
    uint packedAttrs; // low16 = strandU (UNORM16), high16 = strandRandom (UNORM16)
};

// 8 bytes
struct HairGroomSegmentIndex
{
    uint vertexIndex0;        // segment endpoints are (vertexIndex0, vertexIndex0 + 1)
    uint strandAndLocalIndex; // low20 = strandIndex, high12 = localSegmentIndex
};

// 16 bytes
struct HairGroomVisibilitySH
{
    float4 coefficients; // Biased L1 SH (c0, c1, c2, c3)
};

float2 VividHairUnpackHalf2(uint packed)
{
    return float2(f16tof32(packed & 0xFFFFu), f16tof32((packed >> 16u) & 0xFFFFu));
}

float VividHairUnpackStrandU(uint packedAttrs)
{
    return float(packedAttrs & 0xFFFFu) * (1.0f / 65535.0f);
}

float VividHairUnpackStrandRandom(uint packedAttrs)
{
    return float((packedAttrs >> 16u) & 0xFFFFu) * (1.0f / 65535.0f);
}

uint VividHairUnpackClumpId(uint clumpAndSeed)
{
    return clumpAndSeed & 0xFFFFu;
}

float VividHairUnpackStrandSeed(uint clumpAndSeed)
{
    return float((clumpAndSeed >> 16u) & 0xFFFFu) * (1.0f / 65535.0f);
}

uint VividHairUnpackSegmentStrandIndex(uint strandAndLocalIndex)
{
    return strandAndLocalIndex & VIVID_HAIR_MAX_STRANDS_PER_MODEL;
}

uint VividHairUnpackSegmentLocalIndex(uint strandAndLocalIndex)
{
    return (strandAndLocalIndex >> 20u) & 0xFFFu;
}

// 32-bit Tiled Visibility Atlas payload:
// [31:20] 12-bit model-bounds normalized linear depth (smaller = closer to camera)
// [19:0]  20-bit segment index within the hair model
uint VividHairPackVisPayload(float normalizedLinearDepth, uint segmentId)
{
    uint depth12 = (uint)round(saturate(normalizedLinearDepth) * VIVID_HAIR_VIS_DEPTH_MAX) & 0xFFFu;
    return (depth12 << 20u) | (segmentId & VIVID_HAIR_MAX_SEGMENTS_PER_MODEL);
}

float VividHairUnpackVisDepth12(uint payload)
{
    return float((payload >> 20u) & 0xFFFu) * (1.0f / VIVID_HAIR_VIS_DEPTH_MAX);
}

uint VividHairUnpackVisSegmentId20(uint payload)
{
    return payload & VIVID_HAIR_MAX_SEGMENTS_PER_MODEL;
}

// IJGC SIGGRAPH 2025 quadratic total coverage reconstruction from additive coverage.
float VividHairReconstructTotalCoverage(uint additiveCoverageFixed10)
{
    float x = float(additiveCoverageFixed10) * (1.0f / 1024.0f);
    return saturate(x - 0.5f * x * saturate(0.5f * x));
}

#endif // VIVIDRP_HAIR_GROOM_TYPES_HLSL
