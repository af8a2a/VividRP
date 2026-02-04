/***************************************************************************
 # Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
 #
 # NVIDIA CORPORATION and its licensors retain all intellectual property
 # and proprietary rights in and to this software, related documentation
 # and any modifications thereto.  Any use, reproduction, disclosure or
 # distribution of this software and related documentation without an express
 # license agreement from NVIDIA CORPORATION is strictly prohibited.
 **************************************************************************/

#ifndef __STF_SAMPLER_STATE_IMPL_HLSLI__
#define __STF_SAMPLER_STATE_IMPL_HLSLI__

// This file should be included by StochasticTextureFiltering.hlsli
int2 STF_NearestNeighbor2D(float2 coordinate)
{
    return int2(round(coordinate));
}

int3 STF_NearestNeighbor3D(float3 coordinate)
{
    return int3(round(coordinate));
}

// Takes the input coordinate and computes a probability based on the distance the float value is to the floor of itself
// The random value u is used to compare whether to increment the coordinate based on the probability to select a neighboring integer for sampling
int STF_StochasticLinear(float coordinate, inout float u)
{
    int s = int(floor(coordinate));

    // Compute the float remainder from the floor on the input value,
    // probabilityS is used as the probability that s input coordinate will be incremented
    const float probabilityS = coordinate - s;

    // Increment the input coordinate s if the random value u [0,1] is less than the probability to select an incremented coordinate
    if (u < probabilityS)
    {
        ++s;

        // Generates a new random number from u
        u /= probabilityS;
    }
    else 
    {
        // Generates a new random number from u
        u = (u - probabilityS) / (1 - probabilityS);
    }

    return s;
}

// Takes a 3D coordinate and computes a texel coordinate based on a random input [0,1]
int2 STF_StochasticBilinear(float2 st, inout float2 u)
{
    int2 res;
    res.x = STF_StochasticLinear(st.x, u.x);
    res.y = STF_StochasticLinear(st.y, u.y);
    return res;
}

// Takes a 3D coordinate and computes a texel coordinate based on a random input [0,1]
int3 STF_StochasticTrilinear(float3 st, float3 u)
{
    return int3(floor(st+u));
}

// Helper function to compute Bicubic weights
float4 STF_GetStochasticBicubicWeights(float t)
{
    const float t2 = t * t;

    float4 w;
    w.x = (1.f/6.f) * (-t*t2 + 3*t2 - 3*t + 1);
    w.y = (1.f/6.f) * (3*t*t2 - 6*t2 + 4);
    w.z = (1.f/6.f) * (-3*t*t2 + 3*t2 + 3*t + 1);
    w.w = (1.f/6.f) * t*t2;
    return w;
}

// Takes in bicubic weights to determine which texel in the bicubic footprint to select.
// Bicubic is a 4x4 texel area footprint and so we increment between [0-3]
int STF_StochasticLinear(float4 w, float u)
{
    float w_sum = w.x;

    if (u < w_sum)
    {
        return 0;
    }

    w_sum += w.y;

    if (u < w_sum)
    {
        return 1;
    }

    w_sum += w.z;

    if (u < w_sum)
    {
        return 2;
    }

    return 3;
}

int2 STF_StochasticBicubic(float2 st, float u, float u2)
{
    // Gather the bicubic weights for each coordinate
    float4 ws = STF_GetStochasticBicubicWeights(st.x - floor(st.x));
    float4 wt = STF_GetStochasticBicubicWeights(st.y - floor(st.y));

    // Sets a beggining texel location to offset from for the bicubic footprint
    int s0 = int(floor(st.x)) - 1;
    int t0 = int(floor(st.y)) - 1;

    // For each coordinate determine the texel location based on separate probabilities
    int s = STF_StochasticLinear(ws, u);
    int t = STF_StochasticLinear(wt, u2);

    return int2(s0 + s, t0 + t);
}

int3 STF_StochasticTricubic(float3 st, float3 u)
{
    // Gather the bicubic weights for each coordinate
    float4 ws = STF_GetStochasticBicubicWeights(st.x - floor(st.x));
    float4 wt = STF_GetStochasticBicubicWeights(st.y - floor(st.y));
    float4 wv = STF_GetStochasticBicubicWeights(st.z - floor(st.z));

    // Sets a beggining texel location to offset from for the bicubic footprint
    int3 st0 = int3(floor(st)) - 1;

    // For each coordinate determine the texel location based on separate probabilities
    int s = STF_StochasticLinear(ws, u.x);
    int t = STF_StochasticLinear(wt, u.y);
    int v = STF_StochasticLinear(wv, u.z);

    return st0 + int3(s, t, v);
}

float2 STF_BoxMullerTransform(float2 u)
{
    float2 r;
    float mag = sqrt(-2.0 * log(u.x));
    return mag * float2(cos(2.0 * STF_PI * u.y), sin(2.0 * STF_PI * u.y));
}

// Generates a 2D Gaussian by computing the Box-Muller 2D transform
int2 STF_StochasticGaussian2D(float  sigma, float2 st, float2 u)
{
    float2 offset = sigma * STF_BoxMullerTransform(u);
    return int2(round(st + offset));
}

// Generates a 3D Gaussian by computing the Box-Muller 2D transform and
// extending to the third dimension
int3 STF_StochasticGaussian3D(  float  sigma,
                                float3 st,
                                /*inout- TODO */ float3 u)
{
    float3 offset = sigma * float3(STF_BoxMullerTransform(u.xy), sqrt(-2.0 * log(u.z)) * cos(2.0 * STF_PI * u.z));
    return int3(round(st + offset));
}

float STF_BSplineCubic_PDF(float t)
{
    t = abs(t);
    if (t <= 1.0)
        return 0.5*t*t*t - t*t + 2.0/3.0;
    if (t <= 2.0)
        return -1.0/6.0*t*t*t + t*t - 2.0*t + 4.0/3.0;
    return 0;
}

float STF_Gaussian_PDF(float t, float sigma)
{
    return exp(-0.5 * t * t / (sigma * sigma));
}

// Method described in the stf paper
float STF_ComputeTextureLod_STF( uint2 dim,
                                 float4 textureGrads,
                                 float  minLod,
                                 float  maxLod,
                                 float  mipBias)
{
    const float dudx = dim.x * textureGrads.x;
    const float dvdx = dim.y * textureGrads.y;
    const float dudy = dim.x * textureGrads.z;
    const float dvdy = dim.y * textureGrads.w;

    float2 maxAxis = float2(dudy, dvdy);
    float2 minAxis = float2(dudx, dvdx);

    if (dot(minAxis, minAxis) > dot(maxAxis, maxAxis))
    {
        minAxis = float2(dudy, dvdy);
        maxAxis = float2(dudx, dvdx);
    }

    float minAxisLength = length(minAxis);
    const float maxAxisLength = length(maxAxis);

    float maxAnisotropy = 16;
    if ( minAxisLength > 0 && (minAxisLength * maxAnisotropy) < maxAxisLength)
    {
        float scale = maxAxisLength / (minAxisLength * maxAnisotropy);
        minAxisLength *= scale;
    }

    const float log2MinAxis = minAxisLength > 0.00001 ? log2(minAxisLength) : minLod;
    const float mipValue = clamp(log2MinAxis + mipBias, minLod, maxLod);
    return mipValue;
}

// Computation extracted from https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#LODCalculation
float STF_ComputeIsotropicLod_Cube(float3 uv,
                            uint   width,
                            float3 ddxUVW,
                            float3 ddyUVW,
                            float  minLod,
                            float  maxLod,
                            float  mipBias,
                            inout float  u)
{
    // Anisotropic is not supported for TextureCube so we compute isotropic of selected cube face
    const float dudx = width * ddxUVW.x;
    const float dvdx = width * ddxUVW.y;
    const float dwdx = width * ddxUVW.z;
    const float dudy = width * ddyUVW.x;
    const float dvdy = width * ddyUVW.y;
    const float dwdy = width * ddyUVW.z;
    float maxComponent = max(abs(uv.z), max(abs(uv.x), abs(uv.y)));
    float lengthX = 0;
    float lengthY = 0;
    if(maxComponent == abs(uv.x))
    {
        lengthX = sqrt(dvdx*dvdx + dwdx*dwdx);
        lengthY = sqrt(dvdy*dvdy + dwdy*dwdy);
    }
    else if(maxComponent == abs(uv.y))
    {
        lengthX = sqrt(dudx*dudx + dwdx*dwdx);
        lengthY = sqrt(dudy*dudy + dwdy*dwdy);
    }
    else
    {
        lengthX = sqrt(dudx*dudx + dvdx*dvdx);
        lengthY = sqrt(dudy*dudy + dvdy*dvdy);
    }
    const float log2MaxAxis = log2(max(lengthX, lengthY));
    int ilod = STF_StochasticLinear(log2MaxAxis + mipBias, u);
    return (uint)clamp(ilod, minLod, maxLod);
}

// Computation extracted from https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#LODCalculation
float STF_ComputeIsotropicLod_3D(float3 uv,
                            uint3  dim,
                            float3 ddxUVW,
                            float3 ddyUVW,
                            float  minLod,
                            float  maxLod,
                            float  mipBias,
                            float  u)
{
    // Anisotropic is not supported for Texture3D so we compute isotropic
    const float dudx = dim.x * ddxUVW.x;
    const float dvdx = dim.y * ddxUVW.y;
    const float dwdx = dim.z * ddxUVW.z;
    const float dudy = dim.x * ddyUVW.x;
    const float dvdy = dim.y * ddyUVW.y;
    const float dwdy = dim.z * ddyUVW.z;
    float lengthX = sqrt(dudx*dudx + dvdx*dvdx + dwdx*dwdx);
    float lengthY = sqrt(dudy*dudy + dvdy*dvdy + dwdy*dwdy);
    const float log2MaxAxis = log2(max(lengthX, lengthY));
    int ilod = STF_StochasticLinear(log2MaxAxis, u);
    return (uint)clamp(ilod, minLod, maxLod);
}


// If not using the sampler from d3d then the various filter variations need to be implemented in SW:

// Applies the texture addressing mode specified by 'mode' (one of the STF_ADDRESS_MODE_... constants)
// to the texture coordinate 'u' and returns the sample position inside the (0, 1) interval.
// The 'size' parameter is the texture size in the corresponding dimension on the right mip level.
// The 'isBorder' out parameter will be set to 'true' if the sample lands on a border in BORDER mode.
// Should be used in combination with one of the GetSamplePos functions above when no hardware sampler is available.
float STF_ApplyAddressingMode1D(float u,
                                uint size,
                                uint mode,
                                out bool isBorder)
{
    float invSize = 1.0 / float(size);
    isBorder = false;
    switch (mode)
    {
        case STF_ADDRESS_MODE_WRAP:
        default:
            return frac(u);

        case STF_ADDRESS_MODE_MIRROR:
            return 1.0 - abs(1.0 - frac(u * 0.5) * 2.0);

        case STF_ADDRESS_MODE_CLAMP:
            return clamp(u, invSize, 1.0 - invSize);

        case STF_ADDRESS_MODE_BORDER:
            isBorder = (u <= 0.0) || (u >= 1.0);
            return clamp(u, invSize, 1.0 - invSize);

        case STF_ADDRESS_MODE_MIRROR_ONCE:
            return clamp(abs(u), invSize, 1.0 - invSize);
    }
}

// 2D version of the ApplyAddressingMode function. See STF_ApplyAddressingMode1D for more info.
float2 STF_ApplyAddressingMode2D(float2 uv,
                                 uint2 size,
                                 uint2 modes,
                                 out bool isBorder)
{
    bool2 borders;
    float2 results;
    results.x = STF_ApplyAddressingMode1D(uv.x, size.x, modes.x, borders.x);
    results.y = STF_ApplyAddressingMode1D(uv.y, size.y, modes.y, borders.y);
    isBorder = any(borders);
    return results;
}

// 3D version of the ApplyAddressingMode function. See STF_ApplyAddressingMode1D for more info.
float3 STF_ApplyAddressingMode3D(float3 uvw,
                                 uint3 size,
                                 uint3 modes,
                                 out bool isBorder)
{
    bool3 borders;
    float3 results;
    results.x = STF_ApplyAddressingMode1D(uvw.x, size.x, modes.x, borders.x);
    results.y = STF_ApplyAddressingMode1D(uvw.y, size.y, modes.y, borders.y);
    results.z = STF_ApplyAddressingMode1D(uvw.z, size.z, modes.z, borders.z);
    isBorder = any(borders);
    return results;
}

struct STF_SamplerStateImpl
{
    uint _GetFilterType()
    {
        return m_filterType;
    }
    uint _GetAnisoMethod()
    {
        return m_anisoMethod;
    }
    uint _GetMagMethod()
    {
        return m_magMethod;
    } 
    uint _GetFallbackMethod()
    {
        return m_fallbackMethod;
    } 
    bool _GetDebugFallback()
    {
        return m_debugFallback == 1;
    } 
    uint3 _GetAddressingModes()
    {
        return m_addressingModes;
    }
    float _GetSigma()
    {
        return m_sigma;
    }
    uint4 _GetUserData()
    {
        return m_userData;
    }
    
    float _ComputeTextureLod(
                            uint2  dim,
                            float4 textureGrads,
                            float  minLod,
                            float  maxLod,
                            float  mipBias)
{
    float lod = 0;
    if (_GetAnisoMethod() == STF_ANISO_LOD_METHOD_DEFAULT)
    {
        lod = STF_ComputeTextureLod_STF(dim, textureGrads, minLod, maxLod, mipBias);
    }
    return lod;
}

STF_MUTATING 
float3 _GetTexture2DSamplePos(  uint mipValueType,
                                uint width,
                                uint height,
                                uint numberOfLevels,
                                float2 uv,
                                float2 ddxUV,
                                float2 ddyUV,
                                float mipValue)
{
    float4 u = m_u.xyzw;
    int maxLevel = (int)numberOfLevels - 1;

    // Stochastically compute the texture mip level
    uint lod = 0;
    if (mipValueType == STF_MIP_VALUE_MODE_NONE)
    {
        const float lodf = _ComputeTextureLod(uint2(width, height), float4(ddxUV, ddyUV), 0, maxLevel, 0.f);

        int ilod = STF_StochasticLinear(lodf, u.w);
        lod = (uint)clamp(ilod, 0, maxLevel);
    }
    else if (mipValueType == STF_MIP_VALUE_MODE_MIP_LEVEL)
    {
        int ilod = STF_StochasticLinear(mipValue, u.w);
        lod = (uint)clamp(ilod, 0, maxLevel);
    }
    else if (mipValueType == STF_MIP_VALUE_MODE_MIP_BIAS)
    {
        const float lodf = _ComputeTextureLod(uint2(width, height), float4(ddxUV, ddyUV), 0, maxLevel, mipValue);
        int ilod = STF_StochasticLinear(lodf, u.w);
        lod = (uint)clamp(ilod, 0, maxLevel);
    }

    // Query the W/H for the specified mip level.
    width = max(1u, width >> lod);
    height = max(1u, height >> lod);

    // Convert the uv coordinate to a texel position
    const float2 st = uv * float2(width, height) - 0.5f;
    int2 idx = 0;

    if (_GetFilterType() == STF_FILTER_TYPE_POINT)
    {
        idx = STF_NearestNeighbor2D(st);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_LINEAR)
    {
        idx = STF_StochasticBilinear(st, u.xy);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_CUBIC)
    {
        idx = STF_StochasticBicubic(st, u.x, u.y);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_GAUSSIAN)
    {
        idx = STF_StochasticGaussian2D(m_sigma, st, u.xy);
    }

    float2 idxUV = (idx + 0.5f) / float2(width, height);

    if (m_reseedOnSample)
    {
        m_u = u;
    }
        
    return float3(idxUV, lod);
}

STF_MUTATING
float4 _GetTextureCubeSamplePos(uint   mipValueType,
                                uint   width,
                                uint   numberOfLevels,
                                float3 uv,
                                float3 ddxUVW,
                                float3 ddyUVW,
                                float  mipValue)
{
    float4 u = m_u.xyzw;
    int maxLevel = (int)numberOfLevels - 1;

    // Stochastically compute the texture mip level
    uint lod = 0;
    if (mipValueType == STF_MIP_VALUE_MODE_NONE ||
        mipValueType == STF_MIP_VALUE_MODE_MIP_BIAS)
    {
        lod = uint(STF_ComputeIsotropicLod_Cube(uv, width, ddxUVW, ddyUVW, 0, maxLevel, mipValue, u.w));
    }
    else if (mipValueType == STF_MIP_VALUE_MODE_MIP_LEVEL)
    {
        int ilod = STF_StochasticLinear(mipValue, u.w);
        lod = (uint)clamp(ilod, 0, maxLevel);
    }

    // Calculate the width for the specified mip level.
    width = max(1u, width >> lod);

    // Compute the stochastic discrete UV coordinate.
    const float3 offset = float3(width, width, width) / 2.0f;
    const float3 normalizedUV = normalize(uv);
    const float3 st = normalizedUV * offset;
    int3 idx = 0;

    if (_GetFilterType() == STF_FILTER_TYPE_POINT)
    {
        idx = STF_NearestNeighbor3D(st);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_LINEAR)
    {
        idx = STF_StochasticTrilinear(st, u.xyz);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_CUBIC)
    {
        idx = STF_StochasticTricubic(st, u.xyz);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_GAUSSIAN)
    {
        idx = STF_StochasticGaussian3D(m_sigma, st, u.xyz);
    }

    float3 idxUV = idx / offset;

    if (m_reseedOnSample)
    {
        m_u = u;
    }

    return float4(idxUV, lod);
}

STF_MUTATING
float4 _GetTexture3DSamplePos(uint   mipValueType,
                                uint   width,
                                uint   height,
                                uint   depth,
                                uint   numberOfLevels,
                                float3 uv,
                                float3 ddxUVW,
                                float3 ddyUVW,
                                float  mipValue)
{
    float4 u = m_u.xyzw;
    int maxLevel = (int)numberOfLevels - 1;

    // Stochastically compute the texture mip level
    uint lod = 0;
    if (mipValueType == STF_MIP_VALUE_MODE_NONE ||
        mipValueType == STF_MIP_VALUE_MODE_MIP_BIAS)
    {
        lod = uint(STF_ComputeIsotropicLod_3D(uv, uint3(width, height, depth), ddxUVW, ddyUVW, 0, maxLevel, mipValue, u.w));
    }
    else if (mipValueType == STF_MIP_VALUE_MODE_MIP_LEVEL)
    {
        int ilod = STF_StochasticLinear(mipValue, u.w);
        lod = (uint)clamp(ilod, 0 /*minLod*/, maxLevel);
    }

    // Calculate the W/H/D for the specified mip level.
    width = max(1u, width >> lod);
    height = max(1u, height >> lod);
    depth = max(1u, depth >> lod);

    // Convert the uv coordinate to a texel position
    const float3 st = uv * float3(width, height, depth) - 0.5f;
    int3 idx = 0;

    if (_GetFilterType() == STF_FILTER_TYPE_POINT)
    {
        idx = STF_NearestNeighbor3D(st);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_LINEAR)
    {
        idx = STF_StochasticTrilinear(st, u.xyz);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_CUBIC)
    {
        idx = STF_StochasticTricubic(st, u.xyz);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_GAUSSIAN)
    {
        idx = STF_StochasticGaussian3D(m_sigma, st, u.xyz);
    }

    float3 idxUV = (idx + 0.5f) / float3(width, height, depth);

    if (m_reseedOnSample)
    {
        m_u = u;
    }

    return float4(idxUV, lod);
}

float _GetFilterPDF(float2 scaledUV, float2 scaledTexelPosition)
{
    float2 filterPDF;
    float2 texelDistance = scaledUV - scaledTexelPosition;
    if (_GetFilterType() == STF_FILTER_TYPE_LINEAR)
    {
        filterPDF = clamp(1 - abs(texelDistance), 0, 1);
    }
    else if (_GetFilterType() == STF_FILTER_TYPE_CUBIC)
    {
        filterPDF = float2(STF_BSplineCubic_PDF(texelDistance.x), STF_BSplineCubic_PDF(texelDistance.y));
    }
    else // if (GetFilterType() == STF_FILTER_TYPE_GAUSSIAN)
    {
        filterPDF = float2(STF_Gaussian_PDF(texelDistance.x, m_sigma), STF_Gaussian_PDF(texelDistance.y, m_sigma));
    }

    return filterPDF.x * filterPDF.y;
}

float _GetQuadShareWeight(float2 texelCoord, int2 coordOther, float otherPDF)
{
    // This is needed only in compute shaders and for... shadows.
    // Some lanes will be inactive, and then the returned value is 0.0f.
    // Normally, this should not happen.
    if (otherPDF == 0.0f)
        return 0.0f;

    return _GetFilterPDF(texelCoord, coordOther)/otherPDF;
}

bool _GetIsHelperLane()
{
#if STF_SHADER_MODEL_MAJOR >= 6 && STF_SHADER_MODEL_MINOR >= 6 && (STF_SHADER_STAGE == STF_SHADER_STAGE_PIXEL)
    return IsHelperLane();
#else
    return false;
#endif
}
    
#if STF_ALLOW_WAVE_READ
uint4 GetActiveThreadMask()
{
#if STF_SHADER_MODEL_MAJOR >= 6 && STF_SHADER_MODEL_MINOR >= 6 && (STF_SHADER_STAGE == STF_SHADER_STAGE_PIXEL)
    // WaveReadLaneAt is undefined when reading from helper lanes.
    const uint4 activeThreads = WaveActiveBallot( !_GetIsHelperLane() );
#else
    const uint4 activeThreads = WaveActiveBallot(true);
#endif
    return activeThreads;
}

uint4 GetActiveThreadMask(bool isAvailable)
{
#if STF_SHADER_MODEL_MAJOR >= 6 && STF_SHADER_MODEL_MINOR >= 6 && (STF_SHADER_STAGE == STF_SHADER_STAGE_PIXEL)
    // WaveReadLaneAt is undefined when reading from helper lanes.
    const uint4 activeThreads = WaveActiveBallot( isAvailable && !_GetIsHelperLane() );
#else
    const uint4 activeThreads = WaveActiveBallot(isAvailable);
#endif
    return activeThreads;
}

bool IsLaneActive(uint lane, uint4 activeThreadMask)
{
    const uint iMask = 1u << lane;
    return (activeThreadMask.x & iMask) == iMask;
}
#endif


float4 _Tex2D2x2WaveImpl(float4 val, float2 uv, float2 samplePos, uint width, uint height, uint method, uint frameNo)
{
#if STF_ALLOW_WAVE_READ
    // Setup for quad communication
    float2 texelCoord = float2(width, height) * uv - 0.5;
    int2 coordToSample = int2(round(float2(width, height) * samplePos.xy - 0.5));
    float samplePDF = _GetFilterPDF(texelCoord, coordToSample);

    const uint4 activeThreadMask = GetActiveThreadMask();

    uint baseIndex;
    if(method == STF_MAGNIFICATION_METHOD_2x2_QUAD)
        baseIndex = WaveGetLaneIndex() & 22;   // Make it even and remove bit 4th bit (with value 8).
    else if(method == STF_MAGNIFICATION_METHOD_2x2_FINE)
    {
        const uint l = WaveGetLaneIndex();
        uint allThreeLowestBitsSet = (((l >> 2) & (l >> 1) & l) & 1);
        uint bothBit3and4Set = ((l & (l >> 1)) & 8);
        uint offset = allThreeLowestBitsSet | bothBit3and4Set;
        baseIndex = l - offset;
    }
    else if (method == STF_MAGNIFICATION_METHOD_2x2_FINE_TEMPORAL)
    {
        const uint l = WaveGetLaneIndex();
        uint offset;
        if(bool(frameNo & 1)) // Odd frames.
        {
            uint allThreeLowestBitsSet = (((l >> 2) & (l >> 1) & l) & 1);
            uint bothBit3and4Set = ((l & (l >> 1)) & 8);
            offset = allThreeLowestBitsSet | bothBit3and4Set;
        }
        else // Even frames.
        {
            uint anyThreeLowestBitsSet = (((l >> 2) | (l >> 1) | l) & 1);
            uint eitherBit3and4Set = ((l | (l >> 1)) & 8);
            offset = anyThreeLowestBitsSet | eitherBit3and4Set;
        }
        baseIndex = l - offset;
    }
    float4 res = float4(0.0f, 0.0f, 0.0f, 0.0f);
    float accum_w = 0.0f;
    for (uint i = 0; i < 4; i++)
    {
        uint laneID = baseIndex + (((i & 2) << 2) | (i & 1));   // Computes baseIndex + {0, 1, 8, 9}. That is, this can be unrolled easily by hand.
        if (IsLaneActive(laneID, activeThreadMask))
        {
            int2 coordOther = WaveReadLaneAt(coordToSample, laneID);
            float other_pdf = WaveReadLaneAt(samplePDF, laneID);
            float4 other_value = WaveReadLaneAt(val, laneID);
            float weight = _GetQuadShareWeight(texelCoord, coordOther, other_pdf);
            res += other_value * weight;
            accum_w += weight;
        }
    }

    return res * (1.0f / accum_w); // Might faster because division is done once and then multiplication, instead of possibly three divisions.
#else
        return 0.f;
#endif

}

uint GetBaseIndexWave3x3(uint l, bool useLUT)
{
    if (useLUT)
    {
        uint LUT[32] =
        {
            0, 0, 1, 2, 3, 4, 5, 5,
            0, 0, 1, 2, 3, 4, 5, 5,
            8, 8, 9, 10, 11, 12, 13, 13,
            8, 8, 9, 10, 11, 12, 13, 13
        };
            return LUT[l];
        }
    else
    {
        int t = l - ((((l >> 4) & 1) + ((l >> 3) & 1)) << 3);
        uint baseIndex2 = t < 8 ? min(max(t - 1, 0), 5) : min(max(t - 1, 8), 13);
        return baseIndex2;
    }
}

float4 _Tex2D3x3WaveImpl(float4 val, float2 uv, float2 samplePos, uint width, uint height, bool useLUT, uint frameNo)
{
#if STF_ALLOW_WAVE_READ
    const uint4 activeThreadMask = GetActiveThreadMask();

    // Setup for quad communication
    float2 texelCoord = float2(width, height) * uv - 0.5;
    int2 coordToSample = int2(round(float2(width, height) * samplePos.xy - 0.5));
    float samplePDF = _GetFilterPDF(texelCoord, coordToSample);
    float4 res = 0.0f;
    float accum_w = 0.0f;

    const uint l = WaveGetLaneIndex();
    uint baseIndex = GetBaseIndexWave3x3(l, useLUT);

    for (uint y = 0; y < 3; y++)
    {
        for (uint x = 0; x < 3; x++)
        {
            uint offset = (y << 3) | x;
            uint laneID = baseIndex + offset;
            if (IsLaneActive(laneID, activeThreadMask))
            {
                int2 coordOther = WaveReadLaneAt(coordToSample, laneID);
                float other_pdf = WaveReadLaneAt(samplePDF, laneID);
                float4 other_value = WaveReadLaneAt(val, laneID);
                float weight = _GetQuadShareWeight(texelCoord, coordOther, other_pdf);
                res += other_value * weight;
                accum_w += weight;
            }
        }
    }
    return res * (1.0f / accum_w);
#else
    return 0.f;
#endif
}

float4 _Tex2D4x4WaveImpl(float4 val, float2 uv, float2 samplePos, uint width, uint height, uint method, uint frameNo)
{
#if STF_ALLOW_WAVE_READ
    const uint4 activeThreadMask = GetActiveThreadMask();

    // Setup for quad communication
    float2 texelCoord = float2(width, height) * uv - 0.5;
    int2 coordToSample = int2(round(float2(width, height) * samplePos.xy - 0.5));
    float samplePDF = _GetFilterPDF(texelCoord, coordToSample);
    float4 res = 0.0f;
    float accum_w = 0.0f;

    const uint l = WaveGetLaneIndex();
    uint bit0 = l & 1;
    uint bit1 = (l >> 1) & 1;
    uint bit2 = (l >> 2) & 1;
    uint y2 = bit2 & bit1;
    uint y1 = (~bit2 & bit1 & bit0) | (bit2 & ~bit1);
    uint y0 = (~bit2 & bit1 & ~bit0) | (bit2 & ~bit1 & bit0);
    uint baseIndex = (y2 << 2) | (y1 << 1) | y0;
    for (uint i = 0; i < 16; i++)
    {
        uint offset = ((i << 1) & 24) | (i & 3);    // Becomes {0,1,2,3, 8,9,10,11, 16,17,18,19, 24,25,26,27}.
        uint laneID = baseIndex + offset;
        if (IsLaneActive(laneID, activeThreadMask))
        {
            int2 coordOther = WaveReadLaneAt(coordToSample, laneID);
            float other_pdf = WaveReadLaneAt(samplePDF, laneID);
            float4 other_value = WaveReadLaneAt(val, laneID);
            float weight = _GetQuadShareWeight(texelCoord, coordOther, other_pdf);
            res += other_value * weight;
            accum_w += weight;
        }
    }
    return res * (1.0f / accum_w);
#else
    return 0.f;
#endif
}

float4 _Tex2DWaveImpl(float4 val, float2 uv, float2 samplePos, uint width, uint height)
{
#if STF_ALLOW_WAVE_READ
    const uint4 activeThreadMask = GetActiveThreadMask();
    
    // Setup for quad communication
    float2 texelCoord = float2(width, height) * uv - 0.5;
    int2 coordToSample = int2(round(float2(width, height) * samplePos.xy - 0.5));
    float samplePDF = _GetFilterPDF(texelCoord, coordToSample);
    float4 res = 0.0f;
    float accum_w = 0.0f;
    uint base_index = (WaveGetLaneIndex() / STF_WAVE_READ_SAMPLES_PER_PIXEL) * STF_WAVE_READ_SAMPLES_PER_PIXEL;
    for (uint id0 = 0; id0 < STF_WAVE_READ_SAMPLES_PER_PIXEL; ++id0)
    {
        const uint i = base_index + id0;
        if (IsLaneActive(i, activeThreadMask))
        {
            int2 coordOther = WaveReadLaneAt(coordToSample, i);
            float other_pdf = WaveReadLaneAt(samplePDF, i);
            float4 other_value = WaveReadLaneAt(val, i);
            float weight = _GetQuadShareWeight(texelCoord, coordOther, other_pdf);
            res += other_value * weight;
            accum_w += weight;
        }
    }
    return res / accum_w;
#else
    return 0.f;
#endif
}
    
float4 _Texture2DMagImpl(float4 val, float2 uv, float2 samplePos, uint width, uint height)
{
#if STF_ALLOW_WAVE_READ
    if (STF_MAGNIFICATION_METHOD_2x2_QUAD == _GetMagMethod())
    {
        return _Tex2D2x2WaveImpl(val, uv, samplePos, width, height, STF_MAGNIFICATION_METHOD_2x2_QUAD, 0);
    }
    if (STF_MAGNIFICATION_METHOD_2x2_FINE == _GetMagMethod())
    {
        return _Tex2D2x2WaveImpl(val, uv, samplePos, width, height, STF_MAGNIFICATION_METHOD_2x2_FINE, 0);
    }
    if (STF_MAGNIFICATION_METHOD_2x2_FINE_TEMPORAL == _GetMagMethod())
    {
        return _Tex2D2x2WaveImpl(val, uv, samplePos, width, height, STF_MAGNIFICATION_METHOD_2x2_FINE_TEMPORAL, m_frameIndex);
    }
    if (STF_MAGNIFICATION_METHOD_3x3_FINE_LUT == _GetMagMethod())
    {
        return _Tex2D3x3WaveImpl(val, uv, samplePos, width, height, true /*useLUT*/, 0);
    }
    if (STF_MAGNIFICATION_METHOD_3x3_FINE_ALU == _GetMagMethod())
    {
        return _Tex2D3x3WaveImpl(val, uv, samplePos, width, height, false /*useLUT*/, 0);
    }
    if (STF_MAGNIFICATION_METHOD_4x4_FINE == _GetMagMethod())
    {
        return _Tex2D4x4WaveImpl(val, uv, samplePos, width, height, STF_MAGNIFICATION_METHOD_4x4_FINE, 0);
    }
#endif
    return val;
}

STF_MUTATING
float4 _Texture2DSampleImpl(
                            uint         mipValueType,
                            Texture2D    tex,
                            SamplerState s,
                            float2       uv,
                            float2       ddxUV,
                            float2       ddyUV,
                            float        mipValue)
{

    uint width;
    uint height;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, numberOfLevels);

    float3 samplePos = _GetTexture2DSamplePos(mipValueType, width, height, numberOfLevels, uv, ddxUV, ddyUV, mipValue);
    uint lod = uint(samplePos.z);
    width = width >> lod;
    height = height >> lod;

    if (_GetFilterType() == STF_FILTER_TYPE_POINT)
    {
        return tex.SampleLevel(s, samplePos.xy, samplePos.z);
    }

    // We use SampleLevel with the supplied sampler to make sure
    // we capture the right tiling mode (Wrap, Clamp and Mirror modes)
    const float4 val = tex.SampleLevel(s, samplePos.xy, samplePos.z);
    
    return _Texture2DMagImpl(val, uv, samplePos.xy, width, height);
}

float4 _GetCividis(int index, int min, int max)
{
    const float t = (float)index / (max - min);

    const float t2 = t * t;
    const float t3 = t2 * t;
    
    float4 color;
    color.r = 0.8688 * t3 - 1.5484 * t2 + 0.0081 * t + 0.2536;
    color.g = 0.8353 * t3 - 1.6375 * t2 + 0.2351 * t + 0.8669;
    color.b = 0.6812 * t3 - 1.0197 * t2 + 0.3935 * t + 0.8815;
    color.a = 0.f;
    
    return color;
}

bool _LanesLowerThanCountActive(uint count)
{
    uint activeLanesBitMask = WaveActiveBallot(true).x;
    // This is overly conservative and an optimization. Instead of doing this, we could use prefix sums and map
    // lane index to active lane index and then loop over using WaveReadLaneAt().
    // Let's see on "real" geometry if partial waves with inactive early lanes are a problem.
    uint desiredActiveMask = (1u << count) - 1u;

    // I think the above expression could overflow, so I added "allActive", to verify.
    bool allActive = activeLanesBitMask == 0xFFFFFFFF;
    return allActive || (activeLanesBitMask & desiredActiveMask) == desiredActiveMask;
}

void _ComputeSTCoords(uint width, uint height, float2 uv, out int2 integerCoords, out float2 stCoords, out float2 floatCoords)
{
    floatCoords = uv * uint2(width, height) - float2(0.5f, 0.5f);
    integerCoords = int2(floor(floatCoords));
    stCoords = floatCoords - integerCoords;
}

void _ClampIntCoords(uint width, uint height, inout int2 upperLeftIntCoords, inout int2 lowerRightIntCoords)
{
    upperLeftIntCoords = clamp(upperLeftIntCoords, int2(0, 0), int2(width, height) - int2(1, 1));
    lowerRightIntCoords = clamp(lowerRightIntCoords, int2(0, 0), int2(width, height) - int2(1, 1));
}

void _ClampIntCoords(uint width, uint height, inout int2 intCoords)
{
    intCoords = clamp(intCoords, int2(0, 0), int2(width, height) - int2(1, 1));
}

float4 _BilinearWeights(float2 uv)
{
    const float oneMinusU = 1.0f - uv.x;
    const float oneMinusV = 1.0f - uv.y;
    return float4(oneMinusU * oneMinusV, uv.x * oneMinusV, oneMinusU * uv.y, uv.x * uv.y);
}

int2 _LaneIdxToCoord(uint laneIdx, int2 waveUpperLeftIntCoords, uint bbWidth)
{
    uint laneY = laneIdx / bbWidth;
    uint laneX = laneIdx % bbWidth;
    return waveUpperLeftIntCoords + int2(laneX, laneY);
}

uint _CoordToLaneIdx(int2 coord, int2 waveUpperLeftIntCoords, uint bbWidth)
{
    coord -= waveUpperLeftIntCoords;
    return coord.x + coord.y * bbWidth;
}

int _SampleDiscrete4(float4 weights, inout float rnd)
{
    float sumWeights = weights.x + weights.y + weights.z + weights.w;
    float up = rnd * sumWeights;
    //    if (up == sumWeights)         // From PBRT. TODO. For now, I think that that <3 in the while loop below "solves" (avoids) the problem.
    //        up = NextFloatDown(up);

    // Find offset in weights corresponding to up, i.e., rnd.
    int offset = 0;
    float sum = 0;
    while (sum + weights[offset] <= up && offset < 3)
    {
        sum += weights[offset++];
    }
    const float OneMinusEpsilon = 1.0f - 1.0e-7f;
    rnd = min((up - sum) / weights[offset], OneMinusEpsilon);
    return offset;
}

float4 _CubicBSplineWeights(float t)
{
    float4 weights;
    const float oneSixth = 1.0f / 6.0f;
    const float t2 = t * t;
    const float t3 = t2 * t;
    weights.x = -t3 + 3.0f * (t2 - t) + 1.0f;   // TODO: check if these factorizations actually are faster.
    weights.y = 3.0f * t2 * (t - 2.0f) + 4.0f;
    weights.z = 3.0f * (-t3 + t2 + t) + 1.0f;
    weights.w = t3;
    return weights * oneSixth;
}

float4 _BBS1STFilter(Texture2D texture, uint width, uint height, float2 uv, out int2 upperLeftCoords, out int2 iCoords, out float2 stCoords, out float2 fCoords, in out float2 rnd01)
{
    _ComputeSTCoords(width, height, uv, iCoords, stCoords, fCoords);

    float4 sWeights = _CubicBSplineWeights(stCoords.x);
    float4 tWeights = _CubicBSplineWeights(stCoords.y);
    iCoords -= int2(1, 1);
    upperLeftCoords = iCoords;

    int s = _SampleDiscrete4(sWeights, rnd01.x);
    int t = _SampleDiscrete4(tWeights, rnd01.y);
    iCoords += int2(s, t);
    int2 coordsToSample = iCoords;
    _ClampIntCoords(width, height, coordsToSample);
    return texture[int2(coordsToSample.x, coordsToSample.y)];
}
float4 _BBSWaveGatherFilter(Texture2D texture, uint width, uint height, float2 uv, float2 rnd01)
{
    int kINT32_MIN = -2147483648;

    int2 upperLeftIntCoords, sampledTexelIntCoords = int2(kINT32_MIN, kINT32_MIN);
    float2 stCoords, texelFloatCoords;
    float4 curPixelTexelValue = //_IsCatRom() ? _BCR1STSelectSingleSample(texture, uv, upperLeftIntCoords, sampledTexelIntCoords, stCoords, texelFloatCoords, rnd01) : 
                                              _BBS1STFilter(texture, width, height, uv, upperLeftIntCoords, sampledTexelIntCoords, stCoords, texelFloatCoords, rnd01);

    uint numActiveMaxWeigthLanes = 0;
    float4 bbsWeightsX = /*_IsCatRom() ? _cubicCatmullRomWeights(stCoords.x) : */_CubicBSplineWeights(stCoords.x);
    float4 bbsWeightsY = /*_IsCatRom() ? _cubicCatmullRomWeights(stCoords.y) : */_CubicBSplineWeights(stCoords.y);

    uint gotTexelMask = 0; // bit0 = (0,0), bit1 = (1,0), bit2 = (0,1), bit3 = (1,1).
    float4 filteredColor = float4(0.0f, 0.0f, 0.0f, 0.0f);
    float4 accumSamples = float4(0.0f, 0.0f, 0.0f, 0.0f);
    float sumWeight = 0.0f;
    int cc = 0;
    for (int laneIdx = 0; laneIdx < 32; laneIdx++)
    {
        int2 deltaIntCoords = WaveReadLaneAt(sampledTexelIntCoords, laneIdx) - upperLeftIntCoords;
        uint bitNo = (deltaIntCoords.y << 2) + deltaIntCoords.x;
        uint mask = 1u << bitNo;
        float4 texelValue = WaveReadLaneAt(curPixelTexelValue, laneIdx);
        if ((gotTexelMask & mask) == 0)  // Faster with this if case before the next one.
        {
            if ((uint)(deltaIntCoords.x | deltaIntCoords.y) < 4)
            {
                gotTexelMask |= mask;               // Set bit to indicate that we already have this texel weighted in.
                float weight = bbsWeightsX[deltaIntCoords.x] * bbsWeightsY[deltaIntCoords.y];
                sumWeight += weight;
                filteredColor += texelValue * weight;
                accumSamples += texelValue;
                cc++;
            }
        }
    }

    int numGatheredTexels = countbits(gotTexelMask);
    // print("cc = ", cc);
    // print("numGatheredTexels =", numGatheredTexels);

    // Very seldom we get sumWeight == 0.0f, and then we just use the STF texel.
    if (sumWeight == 0.0f)
        return curPixelTexelValue;

    // Unbiased estimate of the missing sample(s) - average of the existing ones.
    float4 estimatedMissingSamples = accumSamples / numGatheredTexels;
    filteredColor += estimatedMissingSamples * (1.0f - sumWeight);
    filteredColor.w = numActiveMaxWeigthLanes / 32.0;
    return filteredColor;
}

float4 _Tex2DWaveMinMaxImpl(Texture2D tex, float2 uv, uint width, uint height, float2 rnd01, uint fallbackMethod, bool helperAware, bool debugFallback)
{
#if STF_ALLOW_WAVE_READ
    int2 upperLeftIntCoords;
    float2 floatTexCoords;
    float2 stCoords;
    _ComputeSTCoords(width, height, uv, upperLeftIntCoords, stCoords, floatTexCoords);
    int2 lowerRightIntCoords = upperLeftIntCoords + int2(1, 1);
    _ClampIntCoords(width, height, upperLeftIntCoords, lowerRightIntCoords);

    int kMAX_INT = 2147483647;
    int kMIN_INT = -2147483647;

    upperLeftIntCoords = helperAware && _GetIsHelperLane() ? kMAX_INT.xx : upperLeftIntCoords;
    lowerRightIntCoords = helperAware && _GetIsHelperLane() ? kMIN_INT.xx : lowerRightIntCoords;
    
    int2 waveUpperLeftIntCoords = WaveActiveMin(upperLeftIntCoords);
    int2 waveLowerRightIntCoords = WaveActiveMax(lowerRightIntCoords);

    uint bbWidth = uint(waveLowerRightIntCoords.x - waveUpperLeftIntCoords.x) + 1;
    uint bbHeight = uint(waveLowerRightIntCoords.y - waveUpperLeftIntCoords.y) + 1;
    uint activeTexels = bbWidth * bbHeight;

    bool requiredLanesActive = _LanesLowerThanCountActive(activeTexels);

    if (activeTexels > 32 || !requiredLanesActive)
    {
        if (debugFallback)
            return float4(1,1,1,1);
        return _EvaluateFallbackMethod(fallbackMethod, tex, width, height, uv, rnd01, upperLeftIntCoords, stCoords, floatTexCoords);
       // float4 color = _BBSWaveGatherFilter(tex, width, height, uv, rnd01);
       // return float4(color.xyz, 1);
    }

    uint curLaneIdx = WaveGetLaneIndex();
    float4 texelValue = float4(0.0f, 0.0f, 0.0f, 0.0f);

    if (curLaneIdx <= activeTexels)
    {
        texelValue = tex[_LaneIdxToCoord(curLaneIdx, waveUpperLeftIntCoords, bbWidth)];
    }

    float4 bilinWeights = _BilinearWeights(stCoords);
    float4 filteredColor = float4(0.0f, 0.0f, 0.0f, 0.0f);
    filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(upperLeftIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.x;
    filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(lowerRightIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.y;
    filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(upperLeftIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.z;
    filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(lowerRightIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.w;
    filteredColor.w = activeTexels / 32.0;  // Average number of texel lookups per pixel in this wave. Used for statistics. 
    return filteredColor;
#else
    return 0.f;
#endif
}

// V2 has better handling of failure-cases
float4 _Tex2DWaveMinMaxV2Impl(Texture2D tex, float2 uv, uint width, uint height, float2 rnd01, uint fallbackMethod, bool helperAware, bool debugFallback)
{
#if STF_ALLOW_WAVE_READ
    int2 upperLeftIntCoords;
    float2 floatTexCoords;
    float2 stCoords;
    _ComputeSTCoords(width, height, uv, upperLeftIntCoords, stCoords, floatTexCoords);
    int2 lowerRightIntCoords = upperLeftIntCoords + int2(1, 1);
    _ClampIntCoords(width, height, upperLeftIntCoords, lowerRightIntCoords);
    
    int kMAX_INT = 2147483647;
    int kMIN_INT = -2147483647;

    upperLeftIntCoords = helperAware && _GetIsHelperLane() ? kMAX_INT.xx : upperLeftIntCoords;
    lowerRightIntCoords = helperAware && _GetIsHelperLane() ? kMIN_INT.xx : lowerRightIntCoords;
    
    int2 waveUpperLeftIntCoords = WaveActiveMin(upperLeftIntCoords);
    int2 waveLowerRightIntCoords = WaveActiveMax(lowerRightIntCoords);
    uint bbWidth = uint(waveLowerRightIntCoords.x - waveUpperLeftIntCoords.x) + 1;
    uint bbHeight = uint(waveLowerRightIntCoords.y - waveUpperLeftIntCoords.y) + 1;
    uint activeTexels = bbWidth * bbHeight;

    bool requiredLanesActive = _LanesLowerThanCountActive(activeTexels);

//#if PRINT_NUM_TEXELS_REQUIRED
//    float numTexelsRequired = float(activeTexels) / 32.0;
//#else
    float numTexelsRequired = 1.0;
//#endif

    uint activeLanesBitMask = WaveActiveBallot(true).x;
    if(activeTexels > countbits(activeLanesBitMask))
    {
        if (debugFallback)
            return float4(1, 1, 1, 1);
        return _EvaluateFallbackMethod(fallbackMethod, tex, width, height, uv, rnd01, upperLeftIntCoords, stCoords, floatTexCoords);
    }

    uint curLaneIdx = WaveGetLaneIndex();
    float4 texelValue = 0.0f.xxxx;

    float4 bilinWeights = _bilinearWeights(stCoords);
    float4 filteredColor = 0.0f.xxxx;

    if (_LanesLowerThanCountActive(activeTexels)) // Handle the simple case.
    {
        if (curLaneIdx <= activeTexels)
        {
            texelValue = tex[_LaneIdxToCoord(curLaneIdx, waveUpperLeftIntCoords, bbWidth)];
        }

        filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(upperLeftIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.x;
        filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(lowerRightIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.y;
        filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(upperLeftIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.z;
        filteredColor += WaveReadLaneAt(texelValue, _CoordToLaneIdx(int2(lowerRightIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth)) * bilinWeights.w;
    }
    else
    {
        if (debugFallback)
            return float4(0, 1, 0, 0);

        uint lastLaneNeededIdx = __fns3(activeLanesBitMask, activeTexels - 1);
        if (curLaneIdx <= lastLaneNeededIdx) // This lane needs to do a texture lookup.
        {
            uint remappedLaneIdx = transformLaneNoToActiveLaneNo(curLaneIdx, activeLanesBitMask);
            texelValue = tex[_LaneIdxToCoord(remappedLaneIdx, waveUpperLeftIntCoords, bbWidth)];
        }

        uint4 indices = uint4(
            _CoordToLaneIdx(int2(upperLeftIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth),
            _CoordToLaneIdx(int2(lowerRightIntCoords.x, upperLeftIntCoords.y), waveUpperLeftIntCoords, bbWidth),
            _CoordToLaneIdx(int2(upperLeftIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth),
            _CoordToLaneIdx(int2(lowerRightIntCoords.x, lowerRightIntCoords.y), waveUpperLeftIntCoords, bbWidth));

        uint4 remappedLaneIndices = uint4(__fns3(activeLanesBitMask, indices.x),
                                          __fns3(activeLanesBitMask, indices.y),
                                          __fns3(activeLanesBitMask, indices.z),
                                          __fns3(activeLanesBitMask, indices.w));

        filteredColor += WaveReadLaneAt(texelValue, remappedLaneIndices.x) * bilinWeights.x;
        filteredColor += WaveReadLaneAt(texelValue, remappedLaneIndices.y) * bilinWeights.y;
        filteredColor += WaveReadLaneAt(texelValue, remappedLaneIndices.z) * bilinWeights.z;
        filteredColor += WaveReadLaneAt(texelValue, remappedLaneIndices.w) * bilinWeights.w;
    }
    filteredColor.w = activeTexels / 32.0;  // Average number of texel lookups per pixel in this wave. Used for statistics. 
    return filteredColor;

#else
    return 0.f;
#endif
}

// countbits() does not currently work for 64 bit uints, so doing this instead.
uint _countbits(uint64_t m)
{
    return countbits(uint(m)) + countbits(uint(m >> 32));
}

float4 _bilinearWeights(float2 uv)
{
//    uv.x = uv.x * uv.x * (3.0f - 2.0f * uv.x); // Add these two back in for a smoother interpolation.
//    uv.y = uv.y * uv.y * (3.0f - 2.0f * uv.y);

    const float oneMinusU = 1.0f - uv.x;
    const float oneMinusV = 1.0f - uv.y;
    return float4(oneMinusU * oneMinusV, uv.x * oneMinusV, oneMinusU * uv.y, uv.x * uv.y);
}

uint getNthBit2(uint64_t value, uint N)
{
    uint sumTOT0 = countbits(uint(value & 0xFFFFFFFF));

    uint val;
    uint count;
    uint offset;
#if 1
    if(N < sumTOT0)  // Nth bit is in the bits [0,31]
    {
        uint4 maskedValues0 = uint4(uint(value & 0xFF),
                                    uint((value >> 8) & 0xFF),
                                    uint((value >> 16) & 0xFF),
                                    uint((value >> 24) & 0xFF));
        uint4 counts0 = uint4(countbits(maskedValues0.x),
                              countbits(maskedValues0.y),
                              countbits(maskedValues0.z),
                              countbits(maskedValues0.w));
        uint sumXY0 = counts0.x + counts0.y;
        if (N < sumXY0) // Nth bit is in the bits [0,15]
        {
            if (N < counts0.x) // Nth bit is in the bits [0,7]
            {
                val = maskedValues0.x;
                count = 0;
                offset = 0;
            }
            else // Nth bit is in the bits [8,15]
            {
                val = maskedValues0.y;
                count = counts0.x;
                offset = 8;
            }
        }
        else //  Nth bit is in the bits [16,31]
        {
            if (N < sumXY0 + counts0.z) // Nth bit is in the bits [16,23]
            {
                val = maskedValues0.z;
                count = sumXY0;
                offset = 16;
            }
            else // Nth bit is in the bits [24,31]
            {
                val = maskedValues0.w;
                count = sumXY0 + counts0.z;
                offset = 24;
            }
        }
    }
    else // Nth bit is in the bits [32,63]
    {
        uint4 maskedValues1 = uint4(uint((value >> 32) & 0xFF),
                                    uint((value >> 40) & 0xFF),
                                    uint((value >> 48) & 0xFF),
                                    uint((value >> 56) & 0xFF));
        uint4 counts1 = uint4(countbits(maskedValues1.x),
                              countbits(maskedValues1.y),
                              countbits(maskedValues1.z),
                              countbits(maskedValues1.w));
        uint sumXY1 = counts1.x + counts1.y;
        if (N < sumTOT0 + sumXY1) //  Nth bit is in the bits [32,47]
        {
            if (N < sumTOT0 + counts1.x) //  Nth bit is in the bits [32,39]
            {
                val = maskedValues1.x;
                count = sumTOT0;
                offset = 32;
            }
            else //  Nth bit is in the bits [40,47]
            {
                val = maskedValues1.y;
                count = sumTOT0 + counts1.x;
                offset = 40;
            }
        }
        else //  Nth bit is in the bits [48,63]
        {
            if (N < sumTOT0 + sumXY1 + counts1.z) //  Nth bit is in the bits [48,55]
            {
                val = maskedValues1.z;
                count = sumTOT0 + sumXY1;
                offset = 48;
            }
            else //  Nth bit is in the bits [56,63]
            {
                val = maskedValues1.w;
                count = sumTOT0 + sumXY1 + counts1.z;
                offset = 56;
            }
        }
    }
#else
    uint4 maskedValues0 = uint4(uint(value & 0xFF),
                                    uint((value >> 8) & 0xFF),
                                    uint((value >> 16) & 0xFF),
                                    uint((value >> 24) & 0xFF));
    uint4 counts0 = uint4(countbits(maskedValues0.x),
                              countbits(maskedValues0.y),
                              countbits(maskedValues0.z),
                          countbits(maskedValues0.w));
    uint sumXY0 = counts0.x + counts0.y;
    uint4 maskedValues1 = uint4(uint((value >> 32) & 0xFF),
                                    uint((value >> 40) & 0xFF),
                                    uint((value >> 48) & 0xFF),
                                uint((value >> 56) & 0xFF));
    uint4 counts1 = uint4(countbits(maskedValues1.x),
                              countbits(maskedValues1.y),
                              countbits(maskedValues1.z),
                          countbits(maskedValues1.w));
    uint sumXY1 = counts1.x + counts1.y;

    if (N < counts0.x)
    {
        val = maskedValues0.x;
        count = 0;
        offset = 0;
    }
    else if (N < sumXY0)
    {
        val = maskedValues0.y;
        count = counts0.x;
        offset = 8;
    }
    else if (N < sumXY0 + counts0.z)
    {
        val = maskedValues0.z;
        count = sumXY0;
        offset = 16;
    }
    else if (N < sumTOT0)
    {
        val = maskedValues0.w;
        count = counts0.x + counts0.y + counts0.z;
        offset = 24;
    }
    else if (N < sumTOT0 + counts1.x)
    {
        val = maskedValues1.x;
        count = sumTOT0;
        offset = 32;
    }
    else if (N < sumTOT0 + sumXY1)
    {
        val = maskedValues1.y;
        count = sumTOT0 + counts1.x;
        offset = 40;
    }
    else if (N < sumTOT0 + sumXY1 + counts1.z)
    {
        val = maskedValues1.z;
        count = sumTOT0 + sumXY1;
        offset = 48;
    }
    else
    {
        val = maskedValues1.w;
        count = sumTOT0 + sumXY1 + counts1.z;
        offset = 56;
    }
#endif
#if 1
    uint foundValue = 0;
    [unroll]
    for (int q = 0; q < 8; q++)
    {
        if (q + count == N)
        {
            foundValue = val;
        }
        val &= (val - 1); // Zero out the least significant bit that is also 1.
    }
    return foundValue == 0 ? 0xFFFFFFFF : firstbitlow(foundValue) + offset;
#else
    while (val != 0)
    {
        uint idx = firstbitlow(val);
        if (count == N)
        {
            print("answer = ", offset + idx);
            return offset + idx;
        }
        val = val ^ (1u << idx);
        count++;
    }
    return 0xFFFFFFFF; // Should never happen.
#endif
}


uint2 getDeltaTexCoords(uint laneIdx, uint64_t4 waveMask, uint4 counts, uint2 sumCounts)
{
    uint idx;
    // Do simple binary search on counts.
    if (laneIdx < sumCounts.x) // The answer is in waveMask.xy.
    {
        if (laneIdx < counts.x) // The answer is in waveMask.x.
        {
            // We are looking for a bit=1 whose number is laneIdx in waveMask.x.
            idx = getNthBit2(waveMask.x, laneIdx);
        }
        else // The answer is in waveMask.y.
        {
            idx = getNthBit2(waveMask.y, laneIdx - counts.x) + 64;
        }
    }
    else // The answer is in waveMask.zw.
    {
        if (laneIdx < sumCounts.x + counts.z) // The answer is in waveMask.z.
        {
            idx = getNthBit2(waveMask.z, laneIdx - sumCounts.x) + 128;
        }
        else // The answer is in waveMask.w.
        {
            idx = getNthBit2(waveMask.w, laneIdx - sumCounts.x - counts.z) + 192;
        }
    }
    return uint2(idx & 15, idx >> 4);
}

// uint getLaneIdxFromBitMaskNo(uint bitNo, uint64_t4 waveMask, uint4 counts, uint2 sumCounts)
uint getLaneIdxFromBitMaskNo(uint bitNo, uint64_t4 waveMask, uint16_t4 counts, uint16_t2 sumCounts)
{
    uint idx = bitNo >> 6; // Index into waveMask.
#if 0
    // This was slower on 3090 and 4090!
    uint4 accCounts = uint4(0, counts.x, sumCounts.x, sumCounts.x + counts.z);
    return _countbits((uint64_t(0xFFFFFFFFFFFFFFFF) >> (64 - (bitNo-64*idx) - 1)) & waveMask[idx]) + accCounts[idx] - 1;
#else
    if (idx == 0)
    {
        return _countbits((uint64_t(0xFFFFFFFFFFFFFFFF) >> (64 - bitNo - 1)) & waveMask.x) - 1;
    }
    else if (idx == 1)
    {
        return _countbits((uint64_t(0xFFFFFFFFFFFFFFFF) >> (64 - (bitNo - 64) - 1)) & waveMask.y) + counts.x - 1;
    }
    else if (idx == 2)
    {
        return _countbits((uint64_t(0xFFFFFFFFFFFFFFFF) >> (64 - (bitNo - 128) - 1)) & waveMask.z) + sumCounts.x - 1;
    }
    else // idx == 3
    {
        return _countbits((uint64_t(0xFFFFFFFFFFFFFFFF) >> (64 - (bitNo - 192) - 1)) & waveMask.w) + sumCounts.x + counts.z - 1;
    }
    return 0;
#endif
}

uint __fns3(uint value, uint N)
{
    uint count = countbits(value & 0xFFFF);
#if 1
    uint offset = 0;
    if (N < count)
    {
        count = 0;
    }
    else
    {
        offset = 16;
        value >>= 16;
    }

#if 0
    while (value != 0)
    {
        uint idx = firstbitlow(value);
        if (count == N)
            return idx + offset;
        value = value ^ (1u << idx);
        count++;
    }
    return 0xFFFFFFFF; // Should never happen.
#else
    uint foundValue = 0;
    [unroll]
    for (int q = 0; q < 16; q++)
    {
        if (q + count == N)
        {
            foundValue = value;
        }
        value &= (value - 1); // Zero out the least significant bit that is also 1.
    }
    return foundValue == 0 ? 0xFFFFFFFF : firstbitlow(foundValue) + offset;

#endif
#else
    if (N < count)
    {
        count = 0;
        while (value != 0)
        {
            uint idx = firstbitlow(value);
            if (count == N)
                return idx;
            value = value ^ (1u << idx);
            count++;
        }
        return 0xFFFFFFFF; // Should never happen.
    }
    else
    {
        value >>= 16;
        while (value != 0)
        {
            uint idx = firstbitlow(value);
            if (count == N)
                return idx + 16;
            value = value ^ (1u << idx);
            count++;
        }
        return 0xFFFFFFFF; // Should never happen.
    }
#endif
}

uint transformLaneNoToActiveLaneNo(uint curLaneIdx, uint activeLanesBitMask)
{
    return countbits((0xFFFFFFFFu >> (32u - curLaneIdx - 1u)) & activeLanesBitMask) - 1u;
}

float4 _BL1STFilterFast(Texture2D texture, uint width, uint height, float2 uv, in /*out*/ float2 rnd01, out int2 upperLeftIntCoords, out int2 iCoords, out float2 stCoords, out float2 fCoords)
{
    _ComputeSTCoords(width, height, uv, iCoords, stCoords, fCoords);
    upperLeftIntCoords = iCoords;

    float4 weights = _bilinearWeights(stCoords); // These weights come in the order: [0,0], [1,0], [0,1], [1,1], which
    if (rnd01.x >= weights.x)
    {
        if (rnd01.x < weights.x + weights.y)
        {
            iCoords.x++;
        }
        else if (rnd01.x < 1.0 - weights.w)
        {
            iCoords.y++;
        }
        else
        {
            iCoords.x++;
            iCoords.y++;
        }
    }
    int2 iCoordsClamped = iCoords;
    _ClampIntCoords(width, height, iCoordsClamped);
    return texture[iCoordsClamped];
}


float4 _EvaluateFallbackMethod(uint method, Texture2D texture, uint width, uint height, float2 uv, float2 rnd01, uint2 upperLeftIntCoords, float2 stCoords, float2 floatTexCoords)
{
    float4 color = float4(1,0,1,0);
    if (method == STF_MAGNIFICATION_FALLBACK_METHOD_BL1STFILTER_FAST)
    {
        int2 iCoords;
        color = _BL1STFilterFast(texture, width, height, uv, rnd01, upperLeftIntCoords, iCoords, stCoords, floatTexCoords);
    }
    return float4(color.xyz, 1);
}

// _BL1WaveGlobalMaskFilter
float4 _Tex2DWaveGlobalMaskImpl(Texture2D tex, float2 uv, uint width, uint height, float2 rnd01, uint fallbackMethod, bool excludeHelper, bool debugFallback)
{
#if STF_ALLOW_WAVE_READ
    int2 upperLeftIntCoords;
    float2 floatTexCoords;
    float2 stCoords;
    _ComputeSTCoords(width, height, uv, upperLeftIntCoords, stCoords, floatTexCoords);
    uint activeLanesBitMask = WaveActiveBallot(true).x;

    int2 lowerRightIntCoords = upperLeftIntCoords + int2(1, 1);
    _ClampIntCoords(width, height, upperLeftIntCoords, lowerRightIntCoords);

    int2 waveUpperLeftIntCoords = WaveActiveMin(upperLeftIntCoords);
    int2 waveLowerRightIntCoords = WaveActiveMax(lowerRightIntCoords);
    int2 bbSize = waveLowerRightIntCoords - waveUpperLeftIntCoords + 1;

//#if PRINT_NUM_TEXELS_REQUIRED
//    float numTexelsRequired = 1.1; // Random value over 1.0.
//#else
    float numTexelsRequired = 1.0;
//#endif

    bool fallbackNeeded = bbSize.x > 16 || bbSize.y > 16;

    // Now, we want to put the 2x2 ones, i.e,. 11 into the variable mask.
    //                                         11
    // We shift the value 3 (0b11) in for the first row, and another 3 for the second row.
    // However, if the coords have been clamped, we might need to shift just a 1 (clamped in x)
    // and for clamping in y, we need only to shift one value into mask instead of 2.
    uint64_t horizMask = (lowerRightIntCoords.x - upperLeftIntCoords.x == 1) ? 3 : 1;
    uint64_t4 mask = uint64_t4(0, 0, 0, 0);
    uint2 deltaCoords = upperLeftIntCoords - waveUpperLeftIntCoords;
    uint bitNo = (deltaCoords.y << 4) + deltaCoords.x;
    mask[bitNo >> 6] |= horizMask << (bitNo & 63);
    if (lowerRightIntCoords.y - upperLeftIntCoords.y == 1)
    {
        uint bitNoY1 = bitNo + 16; // deltaCoords.y++
        mask[bitNoY1 >> 6] |= horizMask << (bitNoY1 & 63);
    }

    uint64_t4 waveMask;
    waveMask.x = WaveActiveBitOr(mask.x); // TODO: check if our NVAPI version handles uint64_t4.
    waveMask.y = WaveActiveBitOr(mask.y);
    waveMask.z = WaveActiveBitOr(mask.z);
    waveMask.w = WaveActiveBitOr(mask.w);

    uint16_t4 counts = uint16_t4((uint16_t)_countbits(waveMask.x), (uint16_t)_countbits(waveMask.y), (uint16_t)_countbits(waveMask.z), (uint16_t)_countbits(waveMask.w));

    //print("counts =", counts);
    //print("curLane =", WaveGetLaneIndex());
    //print("waveMask0=", uint(waveMask.x & 0xFFFFFFFF));
    //print("waveMask1=", uint(waveMask.x >> 32) & 0xFFFFFFFF);
    //print("waveMask2=", uint(waveMask.y & 0xFFFFFFFF));
    //print("waveMask3=", uint(waveMask.y >> 32) & 0xFFFFFFFF);

   // bool visualizeModes = false;

    uint curLaneIdx = WaveGetLaneIndex();
    uint16_t2 sumCounts = uint16_t2(counts.x + counts.y, counts.z + counts.w);
    uint16_t activeTexelsNeeded = sumCounts.x + sumCounts.y;
   // if (visualizeModes && !fallbackNeeded)
   // {
   //     return mapToViridisColorMap(activeTexelsNeeded);
   // }

//#if PRINT_NUM_TEXELS_REQUIRED
//    numTexelsRequired = activeTexelsNeeded / 32.0;
//#endif

    if (fallbackNeeded || activeTexelsNeeded > countbits(activeLanesBitMask))
    {
        if (debugFallback)
            return float4(1,1,1,1);

       return _EvaluateFallbackMethod(fallbackMethod, tex, width, height, uv, rnd01, upperLeftIntCoords, stCoords, floatTexCoords);
    }

    int kINT32_MIN = -2147483648;
    float4 curPixelTexelValue = STF_FLT_MAX.xxxx;
    int2 sampledTexelIntCoords = kINT32_MIN.xx; // Invalid coords.
    float4 filteredColor = 0.0f.xxxx;
    float4 bilinWeights = _bilinearWeights(stCoords);

    // The following line is there to handle clamping corectly.
    // In non-clamped cases, lowerRightIntCoords - upperLeftIntCoords = (1,1), but for clamping, we may get (0,1), for example.
    int2 lowerRightOffset = (lowerRightIntCoords - upperLeftIntCoords) * int2(1, 16); //+16 is next line in the 16x16 mask.

    if (_LanesLowerThanCountActive(activeTexelsNeeded))    // Handle the simple case.
    {
        if (curLaneIdx <= activeTexelsNeeded) // This lane needs to do a texture lookup.
        {
            uint2 deltaTexCoords = getDeltaTexCoords(curLaneIdx, waveMask, counts, sumCounts);
            sampledTexelIntCoords = waveUpperLeftIntCoords + deltaTexCoords;
            curPixelTexelValue = tex[sampledTexelIntCoords];
        }

        uint laneIdx0 = getLaneIdxFromBitMaskNo(bitNo, waveMask, counts, sumCounts);
        filteredColor += bilinWeights.x * WaveReadLaneAt(curPixelTexelValue, laneIdx0);
        filteredColor += bilinWeights.y * WaveReadLaneAt(curPixelTexelValue, laneIdx0 + lowerRightOffset.x);
        uint laneIdx1 = getLaneIdxFromBitMaskNo(bitNo + lowerRightOffset.y, waveMask, counts, sumCounts);
        filteredColor += bilinWeights.z * WaveReadLaneAt(curPixelTexelValue, laneIdx1);
        filteredColor += bilinWeights.w * WaveReadLaneAt(curPixelTexelValue, laneIdx1 + lowerRightOffset.x);
    }
    else
    {
        uint lastLaneNeededIdx = __fns3(activeLanesBitMask, activeTexelsNeeded - 1);
        if (curLaneIdx <= lastLaneNeededIdx) // This lane needs to do a texture lookup.
        {
            uint remappedLaneIdx = transformLaneNoToActiveLaneNo(curLaneIdx, activeLanesBitMask);
            uint2 deltaTexCoords = getDeltaTexCoords(remappedLaneIdx, waveMask, counts, sumCounts);
            sampledTexelIntCoords = waveUpperLeftIntCoords + deltaTexCoords;
            curPixelTexelValue = tex[sampledTexelIntCoords];
        }

        // TODO: perhaps we should name these functions better:
        //  * transformLaneNoToActiveLaneNo
        //  * __fns3(activeLanesBitMask, laneIdx0)
        // because they are the bijective function pair.
        // Also, optimizations:
        // * can we do both getLaneIdxFromBitMaskNo() in one call and get some perf?
        // * The four calls to __fns3() can be merged into one call to a more complex function, but should get faster.
        uint laneIdx0 = getLaneIdxFromBitMaskNo(bitNo, waveMask, counts, sumCounts);
        uint laneIdx1 = getLaneIdxFromBitMaskNo(bitNo + lowerRightOffset.y, waveMask, counts, sumCounts);
        uint4 remappedLaneIndices = uint4(__fns3(activeLanesBitMask, laneIdx0),
                                          __fns3(activeLanesBitMask, laneIdx0 + lowerRightOffset.x),
                                          __fns3(activeLanesBitMask, laneIdx1),
                                          __fns3(activeLanesBitMask, laneIdx1 + lowerRightOffset.x));
        filteredColor += bilinWeights.x * WaveReadLaneAt(curPixelTexelValue, remappedLaneIndices.x);
        filteredColor += bilinWeights.y * WaveReadLaneAt(curPixelTexelValue, remappedLaneIndices.y);
        filteredColor += bilinWeights.z * WaveReadLaneAt(curPixelTexelValue, remappedLaneIndices.z);
        filteredColor += bilinWeights.w * WaveReadLaneAt(curPixelTexelValue, remappedLaneIndices.w);
    }

    filteredColor.w = activeTexelsNeeded / 32.0;
    return filteredColor;
#else
    return 0.f;
#endif
}

int _GetNthBit(uint mask, uint N)
{
    for (int i = 0; i < N; i++) {
        mask &= ~(1u << firstbitlow(mask));
    }
    return firstbitlow(mask);
}

int PackInt2(int2 val)
{
    return (val.x << 16) | (0x0000FFFF & val.y);
}

int2 UnpackInt2(int val)
{
    int2 ret;
    ret.x = val >> 16;
    ret.y = 0x0000FFFF & val;
    return ret;
}

STF_MUTATING
float4 _Texture2DLoadImpl(
                            uint mipValueType,
                            Texture2D tex,
                            float2 uv,
                            float2 ddxUV,
                            float2 ddyUV,
                            float mipValue)
{

    uint width;
    uint height;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, numberOfLevels);

    if (STF_MAGNIFICATION_METHOD_MIN_MAX == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveMinMaxImpl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), false  /*helper aware*/, _GetDebugFallback());
    } 
    else if (STF_MAGNIFICATION_METHOD_MIN_MAX_HELPER == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveMinMaxImpl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), true /*helper aware*/, _GetDebugFallback());
    } 
    else if (STF_MAGNIFICATION_METHOD_MIN_MAX_V2 == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveMinMaxV2Impl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), false  /*helper aware*/, _GetDebugFallback());
    } 
    else if (STF_MAGNIFICATION_METHOD_MIN_MAX_V2_HELPER == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveMinMaxV2Impl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), true /*helper aware*/, _GetDebugFallback());
    } 
    else if (STF_MAGNIFICATION_METHOD_MASK == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveGlobalMaskImpl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), false /*helper aware*/, _GetDebugFallback());
    }
    else if (STF_MAGNIFICATION_METHOD_MASK2 == _GetMagMethod())
    {
        bool isBorder = false;
        float2 uvAddr = STF_ApplyAddressingMode2D(uv, uint2(1, 1), m_addressingModes.xy, isBorder);
        return _Tex2DWaveGlobalMaskImpl(tex, uvAddr, width, height, m_u.xy, _GetFallbackMethod(), true /*helper aware*/, _GetDebugFallback());
    }

    float3 samplePos = _GetTexture2DSamplePos(mipValueType, width, height, numberOfLevels, uv, ddxUV, ddyUV, mipValue);
    uint lod = uint(samplePos.z);
    width = width >> lod;
    height = height >> lod;

    bool isBorder = false;
    const float2 pixelIndex = uint2(width, height) * STF_ApplyAddressingMode2D(samplePos.xy, uint2(width, height), m_addressingModes.xy, isBorder);
    const float4 val = tex.Load(uint3((uint)pixelIndex.x, (uint)pixelIndex.y, lod));
    
    if (_GetFilterType() == STF_FILTER_TYPE_POINT)
    {
        return val;
    }

    return _Texture2DMagImpl(val, uv, samplePos.xy, width, height);
}
    
STF_MUTATING
float4 _Texture2DArraySampleImpl(uint           mipValueType,
                                    Texture2DArray tex,
                                    SamplerState   s,
                                    float3         uv,
                                    float3         ddxUV,
                                    float3         ddyUV,
                                    float          mipValue)
{
    uint width;
    uint height;
    uint TextureSlice = uint(uv.z);
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, TextureSlice, numberOfLevels);

    float3 samplePos = _GetTexture2DSamplePos(mipValueType, width, height, numberOfLevels, uv.xy, ddxUV.xy, ddyUV.xy, mipValue);

    // We use SampleLevel with the supplied sampler to make sure
    // we capture the right tiling mode (Wrap, Clamp and Mirror modes)
    return tex.SampleLevel(s, float3(samplePos.xy, uv.z), samplePos.z);
}

STF_MUTATING
float4 _Texture2DArrayLoadImpl(uint mipValueType,
                                    Texture2DArray tex,
                                    float3         uv,
                                    float3         ddxUV,
                                    float3         ddyUV,
                                    float          mipValue)
{
    uint width;
    uint height;
    uint numSlices;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, numSlices, numberOfLevels);

    float3 samplePos = _GetTexture2DSamplePos(mipValueType, width, height, numberOfLevels, uv.xy, ddxUV.xy, ddyUV.xy, mipValue);
    uint lod = uint(samplePos.z);
    width = width >> lod;
    height = height >> lod;

    bool isBorder = false;
    const float2 pixelIndex = uint2(width, height) * STF_ApplyAddressingMode2D(samplePos.xy, uint2(width, height), m_addressingModes.xy, isBorder);
    return tex.Load(uint4(uint(pixelIndex.x), uint(pixelIndex.y), (uint) uv.z, lod));
}

STF_MUTATING
float4 _TextureCubeSampleImpl(uint         mipValueType,
                                TextureCube  tex,
                                SamplerState s,
                                float3       uv,
                                float3       ddxUVW,
                                float3       ddyUVW,
                                float        mipValue)
{
    uint width;
    uint height;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, numberOfLevels);

    float4 samplePos = _GetTextureCubeSamplePos(mipValueType, width, numberOfLevels, uv, ddxUVW, ddyUVW, mipValue);

    // We use SampleLevel with the supplied sampler to make sure
    // we capture the right tiling mode (Wrap, Clamp and Mirror modes)
    return tex.SampleLevel(s, samplePos.xyz, samplePos.w);
}

STF_MUTATING
float4 _Texture3DSampleImpl(uint         mipValueType,
                            Texture3D    tex,
                            SamplerState s,
                            float3       uv,
                            float3       ddxUVW,
                            float3       ddyUVW,
                            float        mipValue)
{
    uint width;
    uint height;
    uint depth;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, depth, numberOfLevels);

    float4 samplePos = _GetTexture3DSamplePos(mipValueType, width, height, depth, numberOfLevels, uv, ddxUVW, ddyUVW, mipValue);

    // We use SampleLevel with the supplied sampler to make sure
    // we capture the right tiling mode (Wrap, Clamp and Mirror modes)
    return tex.SampleLevel(s, samplePos.xyz, samplePos.w);
}

STF_MUTATING
float4 _Texture3DLoadImpl(uint mipValueType,
                            Texture3D    tex,
                            float3       uv,
                            float3       ddxUVW,
                            float3       ddyUVW,
                            float        mipValue)
{
    uint width;
    uint height;
    uint depth;
    uint numberOfLevels;
    tex.GetDimensions(0, width, height, depth, numberOfLevels);

    float4 samplePos = _GetTexture3DSamplePos(mipValueType, width, height, depth, numberOfLevels, uv, ddxUVW, ddyUVW, mipValue);
    uint lod = uint(samplePos.w);
    width = width >> lod;
    height = height >> lod;
    depth = depth >> lod;

    bool isBorder = false;
    const float3 pixelIndex = uint3(width, height, depth) * STF_ApplyAddressingMode3D(samplePos.xyz, uint3(width, height, depth), m_addressingModes.xyz, isBorder);
    return tex.Load(uint4(uint(pixelIndex.x), uint(pixelIndex.y), uint(pixelIndex.z), lod));
}

STF_MUTATING
float4 _Texture2DSample(Texture2D tex, SamplerState s, float2 uv)
{
    return _Texture2DSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddx(uv), ddy(uv), 0.f);
}
    
STF_MUTATING
float4 _Texture2DSampleGrad(Texture2D tex, SamplerState s, float2 uv, float2 ddxUV, float2 ddyUV)
{
    return _Texture2DSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture2DSampleLevel(Texture2D tex, SamplerState s, float2 uv, float mipLevel)
{
    return _Texture2DSampleImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, s, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture2DSampleBias(Texture2D tex, SamplerState s, float2 uv, float mipBias)
{
    return _Texture2DSampleImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, s, uv, 0, 0, mipBias);
}

STF_MUTATING
float4 _Texture2DLoad(Texture2D tex, float2 uv)
{
    return _Texture2DLoadImpl(STF_MIP_VALUE_MODE_NONE, tex,uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture2DLoadGrad(Texture2D tex, float2 uv, float2 ddxUV, float2 ddyUV)
{
    return _Texture2DLoadImpl(STF_MIP_VALUE_MODE_NONE, tex, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture2DLoadLevel(Texture2D tex, float2 uv, float mipLevel)
{
    return _Texture2DLoadImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture2DLoadBias(Texture2D tex, float2 uv, float mipBias)
{
    return _Texture2DLoadImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, uv, 0, 0, mipBias);
}

// Texture2D/Texture2DArray without the Texture objects.
// These functions return float3(x, y, lod) where (x, y) point at texel centers in UV space, lod is integer.
// Note: use floor(f) to convert the sample positions to integer texel coordinates, not round(f).

STF_MUTATING
float3 _Texture2DGetSamplePos(uint   width,
                                uint   height,
                                uint   numberOfLevels,
                                float2 uv)
{
    return _GetTexture2DSamplePos(STF_MIP_VALUE_MODE_NONE, width, height, numberOfLevels, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float3 _Texture2DGetSamplePosGrad(uint   width,
                                    uint   height,
                                    uint   numberOfLevels,
                                    float2 uv,
                                    float2 ddxUV,
                                    float2 ddyUV)
{
    return _GetTexture2DSamplePos(STF_MIP_VALUE_MODE_NONE, width, height, numberOfLevels, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float3 _Texture2DGetSamplePosLevel(uint   width,
                                    uint   height,
                                    uint   numberOfLevels,
                                    float2 uv,
                                    float  mipLevel)
{
    return _GetTexture2DSamplePos(STF_MIP_VALUE_MODE_MIP_LEVEL, width, height, numberOfLevels, uv, 0, 0, mipLevel);
}

STF_MUTATING
float3 _Texture2DGetSamplePosBias(uint   width,
                                    uint   height,
                                    uint   numberOfLevels,
                                    float2 uv,
                                    float  mipBias)
{
    return _GetTexture2DSamplePos(STF_MIP_VALUE_MODE_MIP_BIAS, width, height, numberOfLevels, uv, ddx(uv), ddy(uv), mipBias);
}

// Texture2DArray with the Texture objects.

STF_MUTATING
float4 _Texture2DArraySample(Texture2DArray tex,
                            SamplerState s,
                            float3 uv)
{
    return _Texture2DArraySampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture2DArraySampleGrad(Texture2DArray tex,
                                SamplerState   s,
                                float3         uv,
                                float3         ddxUV,
                                float3         ddyUV)
{
    return _Texture2DArraySampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture2DArraySampleLevel(Texture2DArray tex,
                                    SamplerState   s,
                                    float3         uv,
                                    float          mipLevel)
{
    return _Texture2DArraySampleImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, s, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture2DArraySampleBias(Texture2DArray tex,
                                SamplerState   s,
                                float3         uv,
                                float          mipBias)
{
    return _Texture2DArraySampleImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, s, uv, 0, 0, mipBias);
}

STF_MUTATING
float4 _Texture2DArrayLoad(Texture2DArray tex, float3 uv)
{
    return _Texture2DArrayLoadImpl(STF_MIP_VALUE_MODE_NONE, tex, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture2DArrayLoadGrad(Texture2DArray tex,
                                float3         uv,
                                float3         ddxUV,
                                float3         ddyUV)
{
    return _Texture2DArrayLoadImpl(STF_MIP_VALUE_MODE_NONE, tex, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture2DArrayLoadLevel(Texture2DArray tex,
                                    float3         uv,
                                    float          mipLevel)
{
    return _Texture2DArrayLoadImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture2DArrayLoadBias(Texture2DArray tex,
                                float3         uv,
                                float          mipBias)
{
    return _Texture2DArrayLoadImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, uv, 0, 0, mipBias);
}

// Texture2DArray without the Texture objects.  TODO

// Texture3D with the Texture objects.

STF_MUTATING
float4 _Texture3DSample(Texture3D    tex,
                        SamplerState s,
                        float3       uv)
{
    return _Texture3DSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture3DSampleGrad(Texture3D    tex,
                            SamplerState s,
                            float3       uv,
                            float3       ddxUV,
                            float3       ddyUV)
{
    return _Texture3DSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture3DSampleLevel(Texture3D    tex,
                            SamplerState s,
                            float3       uv,
                            float        mipLevel)
{
    return _Texture3DSampleImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, s, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture3DSampleBias(Texture3D    tex,
                            SamplerState s,
                            float3       uv,
                            float        mipBias)
{
    return _Texture3DSampleImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, s, uv, 0, 0, mipBias);
}

STF_MUTATING
float4 _Texture3DLoad(Texture3D tex, float3 uv)
{
    return _Texture3DLoadImpl(STF_MIP_VALUE_MODE_NONE, tex, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture3DLoadGrad(Texture3D tex, float3 uv, float3 ddxUV, float3 ddyUV)
{
    return _Texture3DLoadImpl(STF_MIP_VALUE_MODE_NONE, tex, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture3DLoadLevel(Texture3D tex, float3 uv, float mipLevel)
{
    return _Texture3DLoadImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture3DLoadBias(Texture3D tex, float3 uv, float mipBias)
{
    return _Texture3DLoadImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, uv, 0, 0, mipBias);
}

// Texture3D without the Texture objects.
// These functions return float4(x, y, z, lod) where (x, y, z) point at texel centers in UV space, lod is integer.
STF_MUTATING
float4 _Texture3DGetSamplePos(uint   width,
                                uint   height,
                                uint   depth,
                                uint   numberOfLevels,
                                float3 uv)
{
    return _GetTexture3DSamplePos(STF_MIP_VALUE_MODE_NONE, width, height, depth, numberOfLevels, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _Texture3DGetSamplePosGrad(uint   width,
                                    uint   height,
                                    uint   depth,
                                    uint   numberOfLevels,
                                    float3 uv,
                                    float3 ddxUV,
                                    float3 ddyUV)
{
    return _GetTexture3DSamplePos(STF_MIP_VALUE_MODE_NONE, width, height, depth, numberOfLevels, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _Texture3DGetSamplePosLevel(uint   width,
                                    uint   height,
                                    uint   depth,
                                    uint   numberOfLevels,
                                    float3 uv,
                                    float  mipLevel)
{
    return _GetTexture3DSamplePos(STF_MIP_VALUE_MODE_MIP_LEVEL, width, height, depth, numberOfLevels, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _Texture3DGetSamplePosBias(uint   width,
                                    uint   height,
                                    uint   depth,
                                    uint   numberOfLevels,
                                    float3 uv,
                                    float  mipBias)
{
    return _GetTexture3DSamplePos(STF_MIP_VALUE_MODE_MIP_BIAS, width, height, depth, numberOfLevels, uv, 0, 0, mipBias);
}

// TextureCube with the Texture objects.
STF_MUTATING
float4 _TextureCubeSample(TextureCube  tex,
                            SamplerState s,
                            float3       uv)
{
    return _TextureCubeSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddx(uv), ddy(uv), 0.f);
}

STF_MUTATING
float4 _TextureCubeSampleGrad(TextureCube  tex,
                                SamplerState s,
                                float3       uv,
                                float3       ddxUV,
                                float3       ddyUV)
{
    return _TextureCubeSampleImpl(STF_MIP_VALUE_MODE_NONE, tex, s, uv, ddxUV, ddyUV, 0.f);
}

STF_MUTATING
float4 _TextureCubeSampleLevel(TextureCube  tex,
                                SamplerState s,
                                float3       uv,
                                float        mipLevel)
{
    return _TextureCubeSampleImpl(STF_MIP_VALUE_MODE_MIP_LEVEL, tex, s, uv, 0, 0, mipLevel);
}

STF_MUTATING
float4 _TextureCubeSampleBias(TextureCube  tex,
                                SamplerState s,
                                float3       uv,
                                float        mipBias)
{
    return _TextureCubeSampleImpl(STF_MIP_VALUE_MODE_MIP_BIAS, tex, s, uv, 0, 0, mipBias);
}

static STF_SamplerStateImpl _Create(float4 u)
{
    STF_SamplerStateImpl p;
    p.m_filterType       = STF_FILTER_TYPE_LINEAR;
    p.m_frameIndex       = 0;
    p.m_anisoMethod      = STF_ANISO_LOD_METHOD_DEFAULT;
    p.m_magMethod        = STF_MAGNIFICATION_METHOD_2x2_QUAD;
    p.m_fallbackMethod   = STF_MAGNIFICATION_FALLBACK_METHOD_BL1STFILTER_FAST;
    p.m_addressingModes  = uint3(STF_ADDRESS_MODE_WRAP, STF_ADDRESS_MODE_WRAP, STF_ADDRESS_MODE_WRAP);
    p.m_sigma            = 0.7;
    p.m_u                = u;
    p.m_debugFallback     = false;
    p.m_userData         = 0;
    return p;
}

uint  m_filterType;      // STF_FILTER_TYPE_*
uint  m_frameIndex;
uint  m_anisoMethod;     // STF_ANISO_LOD_METHOD_*
uint  m_magMethod;       // STF_MAGNIFICATION_METHOD_*
uint  m_fallbackMethod;  // STF_MAGNIFICATION_FALLBACK_METHOD_*
uint3 m_addressingModes; // STF_ADDRESS_MODE_*
float m_sigma;           // used for Gaussian kernel
float4 m_u;              // uniform random number(s)
bool m_reseedOnSample;
bool m_debugFallback;     // return debug color to visualize mag 3.0 failures.
uint4 m_userData;        // User data, can be used to store some application specific auxilary data in the sampler object

};

#endif // #ifndef __STF_SAMPLER_STATE_IMPL_HLSLI__