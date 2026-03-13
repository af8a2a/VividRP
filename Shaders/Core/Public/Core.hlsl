#ifndef VIVIDRP_CORE_INCLUDED
#define VIVIDRP_CORE_INCLUDED
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#define SLICE_ARRAY_INDEX       0

#define TEXTURE2D_X(textureName)                                        TEXTURE2D(textureName)
#define TYPED_TEXTURE2D_X(type, textureName)                            TYPED_TEXTURE2D(type, textureName)
#define TEXTURE2D_X_PARAM(textureName, samplerName)                     TEXTURE2D_PARAM(textureName, samplerName)
#define TEXTURE2D_X_ARGS(textureName, samplerName)                      TEXTURE2D_ARGS(textureName, samplerName)
#define TEXTURE2D_X_HALF(textureName)                                   TEXTURE2D_HALF(textureName)
#define TEXTURE2D_X_FLOAT(textureName)                                  TEXTURE2D_FLOAT(textureName)

#define FRAMEBUFFER_INPUT_X_HALF(idx)                                   FRAMEBUFFER_INPUT_HALF(idx)
#define FRAMEBUFFER_INPUT_X_FLOAT(idx)                                  FRAMEBUFFER_INPUT_FLOAT(idx)
#define FRAMEBUFFER_INPUT_X_INT(idx)                                    FRAMEBUFFER_INPUT_INT(idx)
#define FRAMEBUFFER_INPUT_X_UINT(idx)                                   FRAMEBUFFER_INPUT_UINT(idx)
#define LOAD_FRAMEBUFFER_X_INPUT(idx, v2fname)                          LOAD_FRAMEBUFFER_INPUT(idx, v2fname)
#define LOAD_FRAMEBUFFER_INPUT_X(idx, v2fname)                          LOAD_FRAMEBUFFER_X_INPUT(idx, v2fname)

#define FRAMEBUFFER_INPUT_X_HALF_MS(idx)                                FRAMEBUFFER_INPUT_HALF_MS(idx)
#define FRAMEBUFFER_INPUT_X_FLOAT_MS(idx)                               FRAMEBUFFER_INPUT_FLOAT_MS(idx)
#define FRAMEBUFFER_INPUT_X_INT_MS(idx)                                 FRAMEBUFFER_INPUT_INT_MS(idx)
#define FRAMEBUFFER_INPUT_X_UINT_MS(idx)                                FRAMEBUFFER_INPUT_UINT_MS(idx)
#define LOAD_FRAMEBUFFER_INPUT_X_MS(idx, sampleIdx, v2fname)            LOAD_FRAMEBUFFER_INPUT_MS(idx, sampleIdx, v2fname)

#define LOAD_TEXTURE2D_X(textureName, unCoord2)                         LOAD_TEXTURE2D(textureName, unCoord2)
#define LOAD_TEXTURE2D_X_MSAA(textureName, unCoord2, sampleIndex)       LOAD_TEXTURE2D_MSAA(textureName, unCoord2, sampleIndex)
#define LOAD_TEXTURE2D_X_LOD(textureName, unCoord2, lod)                LOAD_TEXTURE2D_LOD(textureName, unCoord2, lod)
#define SAMPLE_TEXTURE2D_X(textureName, samplerName, coord2)            SAMPLE_TEXTURE2D(textureName, samplerName, coord2)
#define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod)   SAMPLE_TEXTURE2D_LOD(textureName, samplerName, coord2, lod)
#define GATHER_TEXTURE2D_X(textureName, samplerName, coord2)            GATHER_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_RED_TEXTURE2D_X(textureName, samplerName, coord2)        GATHER_RED_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_GREEN_TEXTURE2D_X(textureName, samplerName, coord2)      GATHER_GREEN_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_BLUE_TEXTURE2D_X(textureName, samplerName, coord2)       GATHER_BLUE_TEXTURE2D(textureName, samplerName, coord2)
#define GATHER_ALPHA_TEXTURE2D_X(textureName, samplerName, coord2)      GATHER_ALPHA_TEXTURE2D(textureName, samplerName, coord2)

///
/// Texture Sampling Macro Overrides for Scaling
///
/// When mip bias is supported by the underlying platform, the following section redefines all 2d texturing operations to support a global mip bias feature.
/// This feature is used to improve rendering quality when image scaling is active. It achieves this by adding a bias value to the standard mip lod calculation
/// which allows us to select the mip level based on the final image resolution rather than the current rendering resolution.

#ifdef PLATFORM_SAMPLE_TEXTURE2D_BIAS
#ifdef  SAMPLE_TEXTURE2D
#undef  SAMPLE_TEXTURE2D
#define SAMPLE_TEXTURE2D(textureName, samplerName, coord2) \
            PLATFORM_SAMPLE_TEXTURE2D_BIAS(textureName, samplerName, coord2,  _GlobalMipBias.x)
#endif
#ifdef  SAMPLE_TEXTURE2D_BIAS
#undef  SAMPLE_TEXTURE2D_BIAS
#define SAMPLE_TEXTURE2D_BIAS(textureName, samplerName, coord2, bias) \
            PLATFORM_SAMPLE_TEXTURE2D_BIAS(textureName, samplerName, coord2,  (bias + _GlobalMipBias.x))
#endif
#endif

#ifdef PLATFORM_SAMPLE_TEXTURE2D_GRAD
#ifdef  SAMPLE_TEXTURE2D_GRAD
#undef  SAMPLE_TEXTURE2D_GRAD
#define SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, coord2, dpdx, dpdy) \
            PLATFORM_SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, coord2, (dpdx * _GlobalMipBias.y), (dpdy * _GlobalMipBias.y))
#endif
#endif

#ifdef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS
#ifdef  SAMPLE_TEXTURE2D_ARRAY
#undef  SAMPLE_TEXTURE2D_ARRAY
#define SAMPLE_TEXTURE2D_ARRAY(textureName, samplerName, coord2, index) \
            PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, coord2, index, _GlobalMipBias.x)
#endif
#ifdef  SAMPLE_TEXTURE2D_ARRAY_BIAS
#undef  SAMPLE_TEXTURE2D_ARRAY_BIAS
#define SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, coord2, index, bias) \
            PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, coord2, index, (bias + _GlobalMipBias.x))
#endif
#endif

#ifdef PLATFORM_SAMPLE_TEXTURECUBE_BIAS
#ifdef  SAMPLE_TEXTURECUBE
#undef  SAMPLE_TEXTURECUBE
#define SAMPLE_TEXTURECUBE(textureName, samplerName, coord3) \
            PLATFORM_SAMPLE_TEXTURECUBE_BIAS(textureName, samplerName, coord3, _GlobalMipBias.x)
#endif
#ifdef  SAMPLE_TEXTURECUBE_BIAS
#undef  SAMPLE_TEXTURECUBE_BIAS
#define SAMPLE_TEXTURECUBE_BIAS(textureName, samplerName, coord3, bias) \
            PLATFORM_SAMPLE_TEXTURECUBE_BIAS(textureName, samplerName, coord3, (bias + _GlobalMipBias.x))
#endif
#endif

#ifdef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS

#ifdef  SAMPLE_TEXTURECUBE_ARRAY
#undef  SAMPLE_TEXTURECUBE_ARRAY
#define SAMPLE_TEXTURECUBE_ARRAY(textureName, samplerName, coord3, index)\
            PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS(textureName, samplerName, coord3, index, _GlobalMipBias.x)
#endif

#ifdef  SAMPLE_TEXTURECUBE_ARRAY_BIAS
#undef  SAMPLE_TEXTURECUBE_ARRAY_BIAS
#define SAMPLE_TEXTURECUBE_ARRAY_BIAS(textureName, samplerName, coord3, index, bias)\
            PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS(textureName, samplerName, coord3, index, (bias + _GlobalMipBias.x))
#endif
#endif
#endif
