/***************************************************************************
 # Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
 #
 # NVIDIA CORPORATION and its licensors retain all intellectual property
 # and proprietary rights in and to this software, related documentation
 # and any modifications thereto.  Any use, reproduction, disclosure or
 # distribution of this software and related documentation without an express
 # license agreement from NVIDIA CORPORATION is strictly prohibited.
 **************************************************************************/

#ifndef __RNG_HLSL__
#define __RNG_HLSL__

struct RNG
{
    // Note: this will turn 0 into 0! if that's a problem do Hash32( x+constant ) - HashCombine does something similar already
    // This little gem is from https://nullprogram.com/blog/2018/07/31/, "Prospecting for Hash Functions" by Chris Wellons
    static uint Hash32(uint x)
    {
        x ^= x >> 17;
        x *= uint(0xed5ad4bb);
        x ^= x >> 11;
        x *= uint(0xac4c1b51);
        x ^= x >> 15;
        x *= uint(0x31848bab);
        x ^= x >> 14;
        return x;
    }

    // popular hash_combine (boost, etc.)
    static uint Hash32Combine(const uint seed, const uint value)
    {
        return seed ^ (Hash32(value) + 0x9e3779b9 + (seed << 6) + (seed >> 2));
    }

    static float SampleNext1D(inout uint hash)
    {
        // Use upper 24 bits and divide by 2^24 to get a number u in [0,1).
        // In floating-point precision this also ensures that 1.0-u != 0.0.
        hash = Hash32(hash);
        return (hash >> 8) / float(1 << 24);
    }

    static float2 SampleNext2D(inout uint hash)
    {
        float2 sample;
        // Don't use the float2 initializer to ensure consistent order of evaluation.
        sample.x = SampleNext1D(hash);
        sample.y = SampleNext1D(hash);
        return sample;
    }

    static float3 SampleNext3D(inout uint hash)
    {
        float3 sample;
        // Don't use the float3 initializer to ensure consistent order of evaluation.
        sample.x = SampleNext1D(hash);
        sample.y = SampleNext1D(hash);
        sample.z = SampleNext1D(hash);
        return sample;
    }

    static float STWNWhiteNoise1D(uint2 screenCoord, uint frameIndex)
    {
        uint hash = Hash32Combine(Hash32(frameIndex + 0x035F9F29), (screenCoord.x << 16) | screenCoord.y);
        return SampleNext1D(hash);
    }

    static float2 STWNWhiteNoise2D(uint2 screenCoord, uint frameIndex)
    {
        uint hash = Hash32Combine(Hash32(frameIndex + 0x035F9F29), (screenCoord.x << 16) | screenCoord.y);
        return SampleNext2D(hash);
    }

    static float3 STWNWhiteNoise3D(uint2 screenCoord, uint frameIndex)
    {
        uint hash = Hash32Combine(Hash32(frameIndex + 0x035F9F29), (screenCoord.x << 16) | screenCoord.y);
        return SampleNext3D(hash);
    }

    static float3 SpatioTemporalWhiteNoise3D(float2 pixel, uint frameIndex)
    {
        return STWNWhiteNoise3D(uint2(pixel.xy), frameIndex);
    }
};

#endif // __RNG_HLSL__
