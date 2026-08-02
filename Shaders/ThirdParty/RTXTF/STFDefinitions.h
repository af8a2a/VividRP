/***************************************************************************
 # Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
 #
 # NVIDIA CORPORATION and its licensors retain all intellectual property
 # and proprietary rights in and to this software, related documentation
 # and any modifications thereto.  Any use, reproduction, disclosure or
 # distribution of this software and related documentation without an express
 # license agreement from NVIDIA CORPORATION is strictly prohibited.
 **************************************************************************/

#ifndef __STF_DEFINITIONS_H__
#define __STF_DEFINITIONS_H__

#define STF_PI 3.14159265359

// Filter types
#define STF_FILTER_TYPE_POINT               0
#define STF_FILTER_TYPE_LINEAR              1
#define STF_FILTER_TYPE_CUBIC               2
#define STF_FILTER_TYPE_GAUSSIAN            3

// Mip-Level overrides and bias
#define STF_MIP_VALUE_MODE_NONE             0
#define STF_MIP_VALUE_MODE_MIP_LEVEL        1
#define STF_MIP_VALUE_MODE_MIP_BIAS         2

// Magnification strategies
#define STF_MAGNIFICATION_METHOD_NONE                        0
#define STF_MAGNIFICATION_METHOD_2x2_QUAD                    1
#define STF_MAGNIFICATION_METHOD_2x2_FINE                    2
#define STF_MAGNIFICATION_METHOD_2x2_FINE_TEMPORAL           3 // Require FrameIndex to be set on the STFSamplerState
#define STF_MAGNIFICATION_METHOD_3x3_FINE_ALU                4
#define STF_MAGNIFICATION_METHOD_3x3_FINE_LUT                5
#define STF_MAGNIFICATION_METHOD_4x4_FINE                    6
#define STF_MAGNIFICATION_METHOD_MIN_MAX                     7
#define STF_MAGNIFICATION_METHOD_MIN_MAX_HELPER              8
#define STF_MAGNIFICATION_METHOD_MIN_MAX_V2                  9
#define STF_MAGNIFICATION_METHOD_MIN_MAX_V2_HELPER           10
#define STF_MAGNIFICATION_METHOD_MASK                        11
#define STF_MAGNIFICATION_METHOD_MASK2                       12

// When magnification fails we (sometimes) need a matching fallback strategy
#define STF_MAGNIFICATION_FALLBACK_METHOD_BL1STFILTER_FAST   0
#define STF_MAGNIFICATION_FALLBACK_METHOD_DEBUG              1

#define STF_WAVE_READ_SAMPLES_PER_PIXEL     8

// No anisotropy
#define STF_ANISO_LOD_METHOD_NONE           0
// The method described in the STF paper
#define STF_ANISO_LOD_METHOD_DEFAULT        1

// Texture addressing modes - the values match D3D12
#define STF_ADDRESS_MODE_WRAP               1
#define STF_ADDRESS_MODE_MIRROR             2
#define STF_ADDRESS_MODE_CLAMP              3
#define STF_ADDRESS_MODE_BORDER             4
#define STF_ADDRESS_MODE_MIRROR_ONCE        5

// Shader stages
#define STF_SHADER_STAGE_PIXEL              1
#define STF_SHADER_STAGE_VERTEX	            2
#define STF_SHADER_STAGE_GEOMETRY           3
#define STF_SHADER_STAGE_HULL               4
#define STF_SHADER_STAGE_DOMAIN             5
#define STF_SHADER_STAGE_COMPUTE            6
#define STF_SHADER_STAGE_AMPLIFICATION      7
#define STF_SHADER_STAGE_MESH               8
#define STF_SHADER_STAGE_LIBRARY            9

#define STF_FLT_MAX          3.402823466e+38F

#endif // #ifndef __STF_DEFINITIONS_H__
