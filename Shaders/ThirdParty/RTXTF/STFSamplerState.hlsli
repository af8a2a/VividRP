/***************************************************************************
 # Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
 #
 # NVIDIA CORPORATION and its licensors retain all intellectual property
 # and proprietary rights in and to this software, related documentation
 # and any modifications thereto.  Any use, reproduction, disclosure or
 # distribution of this software and related documentation without an express
 # license agreement from NVIDIA CORPORATION is strictly prohibited.
 **************************************************************************/

// Stochastic Texture Filtering
// https://research.nvidia.com/publication/2024-05_filtering-after-shading-stochastic-texture-filtering

#include "STFDefinitions.h"
#include "STFMacros.hlsli"

#ifndef __STF_SAMPLER_STATE_HLSLI__
#define __STF_SAMPLER_STATE_HLSLI__

#include "STFSamplerStateImpl.hlsli"

/*
    ------------------------------------------------------------------------------------------
                                 STF_SamplerState example usage 
    ------------------------------------------------------------------------------------------
    To enable STF replace the regular texture sampling with an instance of STF_SamplerState. E.g

    float4 color = tex.Sample(sampler, texCoord);

    Becomes:

    [1] const float2 u          = ... uniform random number(s) [0,1]
    [2] STF_SamplerState stf    = STF_SamplerState::Create(u.xyy, STF_FILTER_TYPE_LINEAR);
    [3] float4 color            = stf.Texture2DSample(tex, sampler, texCoord);

    ------------------------------------------------------------------------------------------
    
    [1]  Generate random uniform numbers [0,1]. For 2D textures only need to set xy, last component can be left as zero. For 3D set xyz.
    For primary rays or use in rasterization blue noise samping produce the best quality. STBN is recommended: https://github.com/NVIDIAGameWorks/SpatiotemporalBlueNoiseSD.
	See sample application for more details.
    const float2 u = stbnTexture.Load[uint3(pixel.xy % XY, frameIndex % N)];
    Alternatively, use your favourite rng 
    const float2 u = yourFavouriteHash( ... );

    [2] Initialize the STF_SamplerState using the random number and desired filter type.
    STF_SamplerState stf = STF_SamplerState::Create(u.xyy, STF_FILTER_TYPE_LINEAR);

    Use the stf in place of the normal texture sampler:

    float4 colorA = stf.Texture2DSample(textureA, sampleState, texCoord);

    // ... 
    [3] It's safe to re-use the STF_SamplerState for multiple texture fetches on different textures and sampler states.
    the internal state(the rng) will be updated.
    Note that the sampler wrap mode will be applied.

    float4 colorA = stf.Texture2DSample(textureA, sampleState, texCoord);
    float4 colorB = stf.Texture2DSample(textureB, sampleState, texCoord);

    // Alternatively, if not using a SamplerState (for custom buffer loads or other texture representations), use 
    a combination of *GetSamplePos* variants together with STF_ApplyAddressingMode
*/

struct STF_SamplerState
{
    // u - Uniform random number(s) Format: 2D(Array) tex : [u, v, (slice), mip], 3D tex : [u, v, w, mip]
    // If 
    static STF_SamplerState Create(float4 u) {
        STF_SamplerState s;
        s.m_impl = STF_SamplerStateImpl::_Create(u);
        return s;
    }

    STF_MUTATING void SetUniformRandom(float4 u)                 { m_impl.m_u = u; }                               /* Uniform random numbers, 2D(Array) tex : [u, v, (slice), mip], 3D tex : [u, v, w, mip]  */
    STF_MUTATING void SetFilterType(uint filterType)             { m_impl.m_filterType = filterType; }             /*STF_FILTER_TYPE_*/
    STF_MUTATING void SetFrameIndex(uint frameIndex)             { m_impl.m_frameIndex = frameIndex; }             /* Frame index used to calculate odd / even frames for STF_MAGNIFICATION_METHOD_2x2_FINE_TEMPORAL)*/
    STF_MUTATING void SetAnisoMethod(uint anisoMethod)           { m_impl.m_anisoMethod = anisoMethod; }           /*STF_ANISO_LOD_METHOD_*/
    STF_MUTATING void SetMagMethod(uint magMethod)               { m_impl.m_magMethod = magMethod; }               /*STF_MAGNIFICATION_METHOD_*/
    STF_MUTATING void SetFallbackMethod(uint fallbackMethod)     { m_impl.m_fallbackMethod = fallbackMethod; }     /*STF_MAGNIFICATION_FALLBACK_METHOD_*/
    STF_MUTATING void SetAddressingModes(uint3 addressingModes)  { m_impl.m_addressingModes = addressingModes; }   /*STF_ADDRESS_MODE_* - Only applied when using 'Load*/
    STF_MUTATING void SetSigma(float sigma)                      { m_impl.m_sigma = sigma; }
    STF_MUTATING void SetReseedOnSample(bool reseedOnSample)     { m_impl.m_reseedOnSample = reseedOnSample; }     /*Each sample will update the random numbers default: false*/
    STF_MUTATING void SetUserData(uint4 userData)                { m_impl.m_userData = userData; }
    STF_MUTATING void SetDebugFailure(bool debugFailure)         { m_impl.m_debugFallback = debugFailure; }

    float4 GetUniformRandom()         { return m_impl.m_u; }
    uint   GetFilterType()            { return m_impl.m_filterType; }           /*STF_FILTER_TYPE_*/
    uint   GetFrameIndex()            { return m_impl.m_frameIndex; }
    uint   GetAnisoMethod()           { return m_impl.m_anisoMethod; }          /*STF_ANISO_LOD_METHOD_*/
    uint   GetMagMethod()             { return m_impl.m_magMethod; }            /*STF_MAGNIFICATION_METHOD_*/
    uint   GetFallbackMethod()        { return m_impl.m_fallbackMethod; }       /*STF_MAGNIFICATION_FALLBACK_METHOD_*/
    uint3  GetAddressingModes()       { return m_impl.m_addressingModes; }      /*STF_ADDRESS_MODE_* - Only applied when using 'Load'*/ 
    float  GetSigma()                 { return m_impl.m_sigma; }
    bool   GetReseedOnSample()        { return m_impl.m_reseedOnSample; }       /*Each sample will update the random numbers*/
    uint4  GetUserData()              { return m_impl.m_userData; }
    bool   GetDebugFailure()          { return m_impl.m_debugFallback; }

    // Texture2D with Texture and SamplerState objects, will use 'tex.Sample' internally
    STF_MUTATING float4 Texture2DSample(Texture2D tex, SamplerState s, float2 uv) { return m_impl._Texture2DSample(tex, s, uv); }
    STF_MUTATING float4 Texture2DSampleGrad(Texture2D tex, SamplerState s, float2 uv, float2 ddxUV, float2 ddyUV) { return m_impl._Texture2DSampleGrad(tex, s, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture2DSampleLevel(Texture2D tex, SamplerState s, float2 uv, float mipLevel) { return m_impl._Texture2DSampleLevel(tex, s, uv, mipLevel); }
    STF_MUTATING float4 Texture2DSampleBias(Texture2D tex, SamplerState s, float2 uv, float mipBias) { return m_impl._Texture2DSampleBias(tex, s, uv, mipBias); }

    // Texture2D with Texture objects, will use 'tex.Load' internally
    STF_MUTATING float4 Texture2DLoad(Texture2D tex, float2 uv) { return m_impl._Texture2DLoad(tex, uv); }
    STF_MUTATING float4 Texture2DLoadGrad(Texture2D tex, float2 uv, float2 ddxUV, float2 ddyUV) { return m_impl._Texture2DLoadGrad(tex, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture2DLoadLevel(Texture2D tex, float2 uv, float mipLevel) { return m_impl._Texture2DLoadLevel(tex, uv, mipLevel); }
    STF_MUTATING float4 Texture2DLoadBias(Texture2D tex, float2 uv, float mipBias) { return m_impl._Texture2DLoadBias(tex, uv, mipBias); }

    // Texture2D/Texture2DArray without Texture objects.
    // These functions return float3(x, y, lod) where (x, y) point at texel centers in UV space, lod is integer.
    // Note: use floor(f) to convert the sample positions to integer texel coordinates, not round(f).
    STF_MUTATING float3 Texture2DGetSamplePos(uint width, uint height, uint numberOfLevels, float2 uv) { return m_impl._Texture2DGetSamplePos(width, height, numberOfLevels, uv); }
    STF_MUTATING float3 Texture2DGetSamplePosGrad(uint width, uint height, uint numberOfLevels, float2 uv, float2 ddxUV, float2 ddyUV) { return m_impl._Texture2DGetSamplePosGrad(width, height, numberOfLevels, uv, ddxUV, ddyUV); }
    STF_MUTATING float3 Texture2DGetSamplePosLevel(uint width, uint height, uint numberOfLevels, float2 uv, float mipLevel) { return m_impl._Texture2DGetSamplePosLevel(width, height, numberOfLevels, uv, mipLevel); }
    STF_MUTATING float3 Texture2DGetSamplePosBias(uint width, uint height, uint numberOfLevels, float2 uv, float mipBias) { return m_impl._Texture2DGetSamplePosBias(width, height, numberOfLevels, uv, mipBias); }

    // Texture2DArray with Texture and SamplerState objects, will use 'tex.Sample' internally
    STF_MUTATING float4 Texture2DArraySample(Texture2DArray tex, SamplerState s, float3 uv) { return m_impl._Texture2DArraySample(tex, s, uv); }
    STF_MUTATING float4 Texture2DArraySampleGrad(Texture2DArray tex, SamplerState s, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._Texture2DArraySampleGrad(tex, s, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture2DArraySampleLevel(Texture2DArray tex, SamplerState s, float3 uv, float mipLevel) { return m_impl._Texture2DArraySampleLevel(tex, s, uv, mipLevel); }
    STF_MUTATING float4 Texture2DArraySampleBias(Texture2DArray tex, SamplerState s, float3 uv, float mipBias) { return m_impl._Texture2DArraySampleBias(tex, s, uv, mipBias); }

    // Texture2DArray with Texture objects, will use 'tex.Load' internally
    STF_MUTATING float4 Texture2DArrayLoad(Texture2DArray tex, float3 uv) { return m_impl._Texture2DArrayLoad(tex, uv); }
    STF_MUTATING float4 Texture2DArrayLoadGrad(Texture2DArray tex, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._Texture2DArrayLoadGrad(tex, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture2DArrayLoadLevel(Texture2DArray tex, float3 uv, float mipLevel) { return m_impl._Texture2DArrayLoadLevel(tex, uv, mipLevel); }
    STF_MUTATING float4 Texture2DArrayLoadBias(Texture2DArray tex, float3 uv, float mipBias) { return m_impl._Texture2DArrayLoadBias(tex, uv, mipBias); }

    // Texture3D with Texture and SamplerState objects, will use 'tex.Sample' internally
    STF_MUTATING float4 Texture3DSample(Texture3D tex, SamplerState s, float3 uv) { return m_impl._Texture3DSample(tex, s, uv); }
    STF_MUTATING float4 Texture3DSampleGrad(Texture3D tex, SamplerState s, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._Texture3DSampleGrad(tex, s, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture3DSampleLevel(Texture3D tex, SamplerState s, float3 uv, float mipLevel) { return m_impl._Texture3DSampleLevel(tex, s, uv, mipLevel); }
    STF_MUTATING float4 Texture3DSampleBias(Texture3D tex, SamplerState s, float3 uv, float mipBias) { return m_impl._Texture3DSampleBias(tex, s, uv, mipBias); }

    // Texture3D with Texture objects, will use 'tex.Load' internally
    STF_MUTATING float4 Texture3DLoad(Texture3D tex, float3 uv) { return m_impl._Texture3DLoad(tex, uv); }
    STF_MUTATING float4 Texture3DLoadGrad(Texture3D tex, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._Texture3DLoadGrad(tex, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture3DLoadLevel(Texture3D tex, float3 uv, float mipLevel) { return m_impl._Texture3DLoadLevel(tex, uv, mipLevel); }
    STF_MUTATING float4 Texture3DLoadBias(Texture3D tex, float3 uv, float mipBias) { return m_impl._Texture3DLoadBias(tex, uv, mipBias); }

    // Texture3D without Texture objects.
    // These functions return float4(x, y, z, lod) where (x, y, z) point at texel centers in UV space, lod is integer.
    STF_MUTATING float4 Texture3DGetSamplePos(uint width, uint height, uint depth, uint numberOfLevels, float3 uv) { return m_impl._Texture3DGetSamplePos(width, height, depth, numberOfLevels, uv); }
    STF_MUTATING float4 Texture3DGetSamplePosGrad(uint width, uint height, uint depth, uint numberOfLevels, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._Texture3DGetSamplePosGrad(width, height, depth, numberOfLevels, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 Texture3DGetSamplePosLevel(uint width, uint height, uint depth, uint numberOfLevels, float3 uv, float mipLevel) { return m_impl._Texture3DGetSamplePosLevel(width, height, depth, numberOfLevels, uv, mipLevel); }
    STF_MUTATING float4 Texture3DGetSamplePosBias(uint width, uint height, uint depth, uint numberOfLevels, float3 uv, float mipBias) { return m_impl._Texture3DGetSamplePosBias(width, height, depth, numberOfLevels, uv, mipBias); }

    // TextureCube with Texture and SamplerState objects, will use 'tex.Sample' internally.  Load variant isn't supported according to hlsl spec.  https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/texturecube
    STF_MUTATING float4 TextureCubeSample(TextureCube tex, SamplerState s, float3 uv) { return m_impl._TextureCubeSample(tex, s, uv); }
    STF_MUTATING float4 TextureCubeSampleGrad(TextureCube tex, SamplerState s, float3 uv, float3 ddxUV, float3 ddyUV) { return m_impl._TextureCubeSampleGrad(tex, s, uv, ddxUV, ddyUV); }
    STF_MUTATING float4 TextureCubeSampleLevel(TextureCube tex, SamplerState s, float3 uv, float mipLevel) { return m_impl._TextureCubeSampleLevel(tex, s, uv, mipLevel); }
    STF_MUTATING float4 TextureCubeSampleBias(TextureCube tex, SamplerState s, float3 uv, float mipBias) { return m_impl._TextureCubeSampleBias(tex, s, uv, mipBias); }

    // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    // ~~~~~~ private member : do not access or modify directly ~~~~~
    // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

    STF_SamplerStateImpl m_impl;
};

// If not using the sampler from d3d then the various filter variations need to be implemented in SW:

// Applies the texture addressing mode specified by 'mode' (one of the STF_ADDRESS_MODE_... constants)
// to the texture coordinate 'u' and returns the sample position inside the (0, 1) interval.
// The 'size' parameter is the texture size in the corresponding dimension on the right mip level.
// The 'isBorder' out parameter will be set to 'true' if the sample lands on a border in BORDER mode.
// Should be used in combination with one of the GetSamplePos functions above when no hardware sampler is available.
float STF_ApplyAddressingMode1D(float u, uint size, uint /*STF_ADDRESS_MODE_*/ mode, out bool isBorder);

// 2D version of the ApplyAddressingMode function. See STF_ApplyAddressingMode1D for more info.
float2 STF_ApplyAddressingMode2D(float2 uv, uint2 size, uint2 /*STF_ADDRESS_MODE_*/ modes, out bool isBorder);

// 3D version of the ApplyAddressingMode function. See STF_ApplyAddressingMode1D for more info.
float3 STF_ApplyAddressingMode3D(float3 uvw, uint3 size, uint3 /*STF_ADDRESS_MODE_*/ modes, out bool isBorder);


#endif // __STF_SAMPLER_STATE_HLSLI__
