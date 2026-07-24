/*
Copyright (c) 2022, NVIDIA CORPORATION. All rights reserved.

NVIDIA CORPORATION and its licensors retain all intellectual property
and proprietary rights in and to this software, related documentation
and any modifications thereto. Any use, reproduction, disclosure or
distribution of this software and related documentation without an express
license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

// Unity inline sampler spelling: "Nearest" is not recognized; "Point" is equivalent.
#define gNearestClamp gPointClamp

NRD_CONSTANTS_START( REBLUR_HitDistReconstructionConstants )
    // Keep this list in the same order as REBLUR_SHARED_CONSTANTS. Unity 6.6
    // still routes the D3D11 validation variant through FXC, which does not
    // expand the nested multiline macro reliably.
    NRD_CONSTANT( float4x4, gWorldToClip )
    NRD_CONSTANT( float4x4, gViewToClip )
    NRD_CONSTANT( float4x4, gViewToWorld )
    NRD_CONSTANT( float4x4, gWorldToViewPrev )
    NRD_CONSTANT( float4x4, gWorldToClipPrev )
    NRD_CONSTANT( float4x4, gWorldPrevToWorld )
    NRD_CONSTANT( float4, gRotatorPre )
    NRD_CONSTANT( float4, gRotator )
    NRD_CONSTANT( float4, gRotatorPost )
    NRD_CONSTANT( float4, gFrustum )
    NRD_CONSTANT( float4, gFrustumPrev )
    NRD_CONSTANT( float4, gCameraDelta )
    NRD_CONSTANT( float4, gHitDistParams )
    NRD_CONSTANT( float4, gViewVectorWorld )
    NRD_CONSTANT( float4, gViewVectorWorldPrev )
    NRD_CONSTANT( float4, gMvScale )
    NRD_CONSTANT( float2, gAntilagParams )
    NRD_CONSTANT( float2, gResourceSize )
    NRD_CONSTANT( float2, gResourceSizeInv )
    NRD_CONSTANT( float2, gResourceSizeInvPrev )
    NRD_CONSTANT( float2, gRectSize )
    NRD_CONSTANT( float2, gRectSizeInv )
    NRD_CONSTANT( float2, gRectSizePrev )
    NRD_CONSTANT( float2, gResolutionScale )
    NRD_CONSTANT( float2, gResolutionScalePrev )
    NRD_CONSTANT( float2, gRectOffset )
    NRD_CONSTANT( float2, gSpecProbabilityThresholdsForMvModification )
    NRD_CONSTANT( float2, gJitter )
    NRD_CONSTANT( uint2, gPrintfAt )
    NRD_CONSTANT( uint2, gRectOrigin )
    NRD_CONSTANT( int2, gRectSizeMinusOne )
    NRD_CONSTANT( float, gDisocclusionThreshold )
    NRD_CONSTANT( float, gDisocclusionThresholdAlternate )
    NRD_CONSTANT( float, gCameraAttachedReflectionMaterialID )
    NRD_CONSTANT( float, gStrandMaterialID )
    NRD_CONSTANT( float, gStrandThickness )
    NRD_CONSTANT( float, gStabilizationStrength )
    NRD_CONSTANT( float, gDebug )
    NRD_CONSTANT( float, gOrthoMode )
    NRD_CONSTANT( float, gUnproject )
    NRD_CONSTANT( float, gDenoisingRange )
    NRD_CONSTANT( float, gPlaneDistSensitivity )
    NRD_CONSTANT( float, gFramerateScale )
    NRD_CONSTANT( float, gMinBlurRadius )
    NRD_CONSTANT( float, gMaxBlurRadius )
    NRD_CONSTANT( float, gDiffPrepassBlurRadius )
    NRD_CONSTANT( float, gSpecPrepassBlurRadius )
    NRD_CONSTANT( float, gMaxAccumulatedFrameNum )
    NRD_CONSTANT( float, gMaxFastAccumulatedFrameNum )
    NRD_CONSTANT( float, gAntiFirefly )
    NRD_CONSTANT( float, gLobeAngleFraction )
    NRD_CONSTANT( float, gRoughnessFraction )
    NRD_CONSTANT( float, gHistoryFixFrameNum )
    NRD_CONSTANT( float, gHistoryFixBasePixelStride )
    NRD_CONSTANT( float, gHistoryFixAlternatePixelStride )
    NRD_CONSTANT( float, gHistoryFixAlternatePixelStrideMaterialID )
    NRD_CONSTANT( float, gFastHistoryClampingSigmaScale )
    NRD_CONSTANT( float, gMinRectDimMulUnproject )
    NRD_CONSTANT( float, gUsePrepassNotOnlyForSpecularMotionEstimation )
    NRD_CONSTANT( float, gSplitScreen )
    NRD_CONSTANT( float, gSplitScreenPrev )
    NRD_CONSTANT( float, gCheckerboardResolveAccumSpeed )
    NRD_CONSTANT( float, gViewZScale )
    NRD_CONSTANT( float, gFireflySuppressorMinRelativeScale )
    NRD_CONSTANT( float, gMinHitDistanceWeight )
    NRD_CONSTANT( float, gDiffMinMaterial )
    NRD_CONSTANT( float, gSpecMinMaterial )
    NRD_CONSTANT( float, gResponsiveAccumulationInvRoughnessThreshold )
    NRD_CONSTANT( uint, gResponsiveAccumulationMinAccumulatedFrameNum )
    NRD_CONSTANT( uint, gHasHistoryConfidence )
    NRD_CONSTANT( uint, gHasDisocclusionThresholdMix )
    NRD_CONSTANT( uint, gDiffCheckerboard )
    NRD_CONSTANT( uint, gSpecCheckerboard )
    NRD_CONSTANT( uint, gFrameIndex )
    NRD_CONSTANT( uint, gIsRectChanged )
    NRD_CONSTANT( uint, gResetHistory )
    NRD_CONSTANT( uint, gReturnHistoryLengthInsteadOfOcclusion )
NRD_CONSTANTS_END

NRD_SAMPLERS_START
    NRD_SAMPLER( SamplerState, gNearestClamp, s, 0 )
    NRD_SAMPLER( SamplerState, gLinearClamp, s, 1 )
NRD_SAMPLERS_END

NRD_INPUTS_START
    // VividRP only compiles the DIFFUSE_SPECULAR RADIANCE permutation here.
    // Spell nested macro argument types explicitly for Unity's FXC validation path.
    NRD_INPUT( Texture2D, float, gIn_Tiles, t, 0 )
    NRD_INPUT( Texture2D, float4, gIn_Normal_Roughness, t, 1 )
    NRD_INPUT( Texture2D, float, gIn_ViewZ, t, 2 )
    #if( NRD_DIFF && NRD_SPEC )
        NRD_INPUT( Texture2D, float4, gIn_Diff, t, 3 )
        NRD_INPUT( Texture2D, float4, gIn_Spec, t, 4 )
    #elif( NRD_DIFF )
        NRD_INPUT( Texture2D, float4, gIn_Diff, t, 3 )
    #else
        NRD_INPUT( Texture2D, float4, gIn_Spec, t, 3 )
    #endif
NRD_INPUTS_END

NRD_OUTPUTS_START
    #if( NRD_DIFF && NRD_SPEC )
        NRD_OUTPUT( RWTexture2D, float4, gOut_Diff, u, 0 )
        NRD_OUTPUT( RWTexture2D, float4, gOut_Spec, u, 1 )
    #elif( NRD_DIFF )
        NRD_OUTPUT( RWTexture2D, float4, gOut_Diff, u, 0 )
    #else
        NRD_OUTPUT( RWTexture2D, float4, gOut_Spec, u, 0 )
    #endif
NRD_OUTPUTS_END

// Macro magic
#define REBLUR_HitDistReconstructionGroupX 8
#define REBLUR_HitDistReconstructionGroupY 16

#if( MODE_5X5 == 1 )
    #define NRD_USE_BORDER_2
#endif

// Redirection
#undef GROUP_X
#undef GROUP_Y
#define GROUP_X REBLUR_HitDistReconstructionGroupX
#define GROUP_Y REBLUR_HitDistReconstructionGroupY
