/***************************************************************************
 # Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
 #
 # NVIDIA CORPORATION and its licensors retain all intellectual property
 # and proprietary rights in and to this software, related documentation
 # and any modifications thereto.  Any use, reproduction, disclosure or
 # distribution of this software and related documentation without an express
 # license agreement from NVIDIA CORPORATION is strictly prohibited.
 **************************************************************************/

#include "STFDefinitions.h"

#ifndef __STF_MACROS_HLSLI__
#define __STF_MACROS_HLSLI__

// Slang
#if __SLANG_COMPILER__
    #define STF_MUTATING [mutating]
#else
    #define STF_MUTATING
#endif // #if __SLANG_COMPILER__

#ifndef STF_SHADER_STAGE
    #if defined(__SHADER_TARGET_STAGE) // I.e dxc - we can derive STF_SHADER_STAGE automatically
        #if __SHADER_TARGET_STAGE == __SHADER_STAGE_PIXEL
            #define STF_SHADER_STAGE STF_SHADER_STAGE_PIXEL
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_VERTEX
            #define STF_SHADER_STAGE STF_SHADER_STAGE_VERTEX
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_GEOMETRY
            #define STF_SHADER_STAGE STF_SHADER_STAGE_GEOMETRY
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_HULL
            #define STF_SHADER_STAGE STF_SHADER_STAGE_HULL
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_DOMAIN
            #define STF_SHADER_STAGE STF_SHADER_STAGE_DOMAIN
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_COMPUTE
            #define STF_SHADER_STAGE STF_SHADER_STAGE_COMPUTE
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_AMPLIFICATION
            #define STF_SHADER_STAGE STF_SHADER_STAGE_AMPLIFICATION
        #elif __SHADER_TARGET_STAGE == __SHADER_STAGE_MESH
            #define STF_SHADER_STAGE STF_SHADER_STAGE_MESH
        #elif  __SHADER_TARGET_STAGE == __SHADER_STAGE_LIBRARY
            #define STF_SHADER_STAGE STF_SHADER_STAGE_LIBRARY
        #else
            #error "unknown value of __SHADER_TARGET_STAGE"
        #endif
    #endif
#endif // #ifndef STF_SHADER_STAGE

#if !defined(STF_SHADER_STAGE)
    #error "STF_SHADER_STAGE must be defined"
#elif (STF_SHADER_STAGE < 0 || STF_SHADER_STAGE > STF_SHADER_STAGE_LIBRARY)
    #error "Invalid value of STF_SHADER_STAGE"
#endif

#ifndef STF_SHADER_MODEL_MAJOR
    #if defined(__SHADER_TARGET_MAJOR) // I.e dxc - we can derive STF_SHADER_MODEL_MAJOR automatically
        #define STF_SHADER_MODEL_MAJOR __SHADER_TARGET_MAJOR
    #else
        #error "STF_SHADER_MODEL_MAJOR must be defined"
    #endif
#endif // #ifndef STF_SHADER_MODEL_MAJOR

#ifndef STF_SHADER_MODEL_MINOR
    #if defined(__SHADER_TARGET_MINOR) // I.e dxc - we can derive STF_SHADER_MODEL_MINOR automatically
        #define STF_SHADER_MODEL_MINOR __SHADER_TARGET_MINOR
    #else
        #error "STF_SHADER_MODEL_MINOR must be defined"
    #endif
#endif // #ifndef STF_SHADER_MODEL_MINOR

// STF_ALLOW_WAVE_READ is needed for all magnicifaction methods (except STF_MAGNIFICATION_METHOD_NONE) to work properly.
#ifndef STF_ALLOW_WAVE_READ
    #define STF_ALLOW_WAVE_READ (STF_SHADER_MODEL_MAJOR >= 6)
#endif // #ifndef STF_ALLOW_WAVE_READ

#endif // #ifndef __STF_MACROS_HLSLI__
