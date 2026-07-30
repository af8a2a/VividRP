#ifndef VIVIDRP_REFERENCED_PATH_TRACING_SAMPLING_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_SAMPLING_INCLUDED

#if defined(VIVID_REFERENCE_PT_INDEXED_BND)
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BlueNoise.hlsl"
#endif

#define REFERENCED_PATH_SAMPLING_CONTRACT_VERSION 7
#define REFERENCED_PATH_SAMPLING_INDEXED_BND 0
#define REFERENCED_PATH_SAMPLING_INDEXED_HASH 1

static const uint kReferencedPathtracingFilmDimension = 0u;
static const uint kReferencedPathtracingLensDimension = 2u;
static const uint kReferencedPathtracingCameraReservedDimension = 4u;
static const uint kReferencedPathtracingBounceBaseDimension = 8u;
static const uint kReferencedPathtracingBounceDimensionStride = 20u;
static const uint kReferencedPathtracingBsdfDimensionOffset = 0u;
static const uint kReferencedPathtracingNeeDimensionOffset = 3u;
static const uint kReferencedPathtracingRussianRouletteDimensionOffset = 6u;
static const uint kReferencedPathtracingStochasticAlphaDimensionOffset = 7u;
static const uint kReferencedPathtracingVolumeDimensionOffset = 8u;
static const uint kReferencedPathtracingAtmosphereSunDimensionOffset = 12u;
static const uint kReferencedPathtracingCloudDimensionOffset = 14u;
static const uint kReferencedPathtracingFutureDimensionOffset = 18u;
// Append-only block kept outside the established bounce stride so disabling
// global fog preserves every existing surface, atmosphere, and cloud sample.
static const uint kReferencedPathtracingGlobalFogBaseDimension = 200u;
static const uint kReferencedPathtracingGlobalFogDimensionStride = 3u;
static const uint kReferencedPathtracingGlobalFogDistanceDimensionOffset = 0u;
static const uint kReferencedPathtracingGlobalFogPhaseDimensionOffset = 1u;

float2 ReferencedPathtracingSampleConcentricDisk(float2 sample)
{
    float2 offset = 2.0 * sample - 1.0;
    if (all(abs(offset) < 1e-7))
        return 0.0;

    float radius;
    float theta;
    if (abs(offset.x) > abs(offset.y))
    {
        radius = offset.x;
        theta = (3.14159265358979323846 / 4.0)
            * (offset.y / offset.x);
    }
    else
    {
        radius = offset.y;
        theta = (3.14159265358979323846 / 2.0)
            - (3.14159265358979323846 / 4.0)
                * (offset.x / offset.y);
    }

    return radius * float2(cos(theta), sin(theta));
}

uint ReferencedPathtracingHash(uint value)
{
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    value ^= value >> 16u;
    return value;
}

float ReferencedPathtracingHashToUnitFloat(uint value)
{
    return (float)(ReferencedPathtracingHash(value) >> 8u)
        * (1.0 / 16777216.0);
}

uint ReferencedPathtracingGetBounceSampleDimension(
    uint bounceIndex,
    uint dimensionOffset)
{
    return kReferencedPathtracingBounceBaseDimension
        + bounceIndex * kReferencedPathtracingBounceDimensionStride
        + dimensionOffset;
}

uint ReferencedPathtracingGetGlobalFogSampleDimension(
    uint bounceIndex,
    uint dimensionOffset)
{
    return kReferencedPathtracingGlobalFogBaseDimension
        + bounceIndex
            * kReferencedPathtracingGlobalFogDimensionStride
        + dimensionOffset;
}

float ReferencedPathtracingGetIndexedHashSample(
    uint2 pixelCoord,
    uint sampleIndex,
    uint sampleDimension,
    uint seed)
{
    uint pixelHash = ReferencedPathtracingHash(
        pixelCoord.x
        ^ ReferencedPathtracingHash(pixelCoord.y + 0x9e3779b9u));
    uint sampleHash = ReferencedPathtracingHash(
        sampleIndex
        ^ ReferencedPathtracingHash(
            sampleDimension * 0x85ebca6bu + 0xc2b2ae35u));
    return ReferencedPathtracingHashToUnitFloat(
        pixelHash
        ^ sampleHash
        ^ ReferencedPathtracingHash(seed + 0x27d4eb2du));
}

float ReferencedPathtracingGetIndexedBndSample(
    uint2 pixelCoord,
    uint sampleIndex,
    uint sampleDimension,
    uint seed)
{
#if defined(VIVID_REFERENCE_PT_INDEXED_BND)
    // The renderer-owned BND set contains 256 samples. Keep its within-block
    // stratification, then decorrelate every subsequent block with a stable
    // dimension/pixel permutation and a Cranley-Patterson rotation. This avoids
    // repeating the same BND lookup tuple at sample 256 while retaining random
    // access.
    uint sampleBlock = sampleIndex >> 8u;
    uint sampleInBlock = sampleIndex & 255u;
    uint blockSeed = ReferencedPathtracingHash(
        seed
        ^ ReferencedPathtracingHash(
            sampleBlock * 0x9e3779b9u + 0x68bc21ebu));
    uint2 sequencePixel = pixelCoord ^ uint2(
        blockSeed,
        ReferencedPathtracingHash(blockSeed ^ 0x02e5be93u));
    uint dimensionOffset = (blockSeed >> 5u) & 248u;
    uint sequenceDimension = sampleDimension + dimensionOffset;
    float bndSample = GetBNDSequenceSample256SPP(
        sequencePixel,
        sampleInBlock,
        sequenceDimension);
    float blockRotation = ReferencedPathtracingHashToUnitFloat(
        blockSeed
        ^ ReferencedPathtracingHash(
            sampleDimension * 0x27d4eb2du + 0x165667b1u));
    return frac(bndSample + blockRotation);
#else
    return ReferencedPathtracingGetIndexedHashSample(
        pixelCoord,
        sampleIndex,
        sampleDimension,
        seed);
#endif
}

float ReferencedPathtracingGetPathSample(
    uint2 pixelCoord,
    uint sampleIndex,
    uint sampleDimension,
    uint seed,
    uint samplingMode)
{
#if defined(VIVID_REFERENCE_PT_INDEXED_BND)
    return samplingMode == REFERENCED_PATH_SAMPLING_INDEXED_BND
        ? ReferencedPathtracingGetIndexedBndSample(
            pixelCoord,
            sampleIndex,
            sampleDimension,
            seed)
        : ReferencedPathtracingGetIndexedHashSample(
            pixelCoord,
            sampleIndex,
            sampleDimension,
            seed);
#else
    return ReferencedPathtracingGetIndexedHashSample(
        pixelCoord,
        sampleIndex,
        sampleDimension,
        seed);
#endif
}

float3 ReferencedPathtracingGetPathSample3D(
    uint2 pixelCoord,
    uint sampleIndex,
    uint firstSampleDimension,
    uint seed,
    uint samplingMode)
{
    return float3(
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            firstSampleDimension,
            seed,
            samplingMode),
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            firstSampleDimension + 1u,
            seed,
            samplingMode),
        ReferencedPathtracingGetPathSample(
            pixelCoord,
            sampleIndex,
            firstSampleDimension + 2u,
            seed,
            samplingMode));
}

#endif
