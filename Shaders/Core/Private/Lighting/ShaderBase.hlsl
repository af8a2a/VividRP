#ifndef __SHADERBASE_H__
#define __SHADERBASE_H__

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"

#ifdef MSAA_ENABLED
    TEXTURE2D_X_MSAA(float, g_depth_tex) : register( t0 );

    float FetchDepthMSAA(uint2 pixCoord, uint sampleIdx)
    {
        float zdpth = LOAD_TEXTURE2D_X_MSAA(g_depth_tex, pixCoord.xy, sampleIdx).x;
    #if UNITY_REVERSED_Z
        zdpth = 1.0 - zdpth;
    #endif
        return zdpth;
    }
#else
    Texture2D g_depth_tex;
    // Max mip-chain of g_depth_tex built on the CPU side (corrected depth, near=0 far=1).
    // Mip level N has one texel per 2^N x 2^N pixel region, storing the maximum corrected depth.
    // Used by the HiZ tile-max-depth path to replace per-pixel depth sampling.
    Texture2D g_depth_tex_hiz : register( t5 );

    float FetchDepth(uint2 pixCoord)
    {
        float zdpth = LOAD_TEXTURE2D_X(g_depth_tex, pixCoord.xy).x;
    #if UNITY_REVERSED_Z
            zdpth = 1.0 - zdpth;
    #endif
        return zdpth;
    }
#endif

#endif
