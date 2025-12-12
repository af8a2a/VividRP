using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    #region Common

    public enum AccumulationMode : byte
    {
        // Common mode (accumulation continues normally)
        CONTINUE,

        // Discards history and resets accumulation
        RESTART,

        // Like RESTART, but additionally clears resources from potential garbage
        CLEAR_AND_RESTART,

        MAX_NUM
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NRDCommonSettings
    {
        // Matrix as 16 floats (column-major, non-jittered)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] viewToClipMatrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] viewToClipMatrixPrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldToViewMatrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldToViewMatrixPrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] worldPrevToWorldMatrix;

        // motionVectorScale[3]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] motionVectorScale;

        // camera jitter [2]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] cameraJitter;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] cameraJitterPrev;

        // resourceSize / prev / rectSize / rectSizePrev (uint16_t[2])
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] resourceSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] resourceSizePrev;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] rectSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] rectSizePrev;

        // Scalars
        public float viewZScale;
        public float timeDeltaBetweenFrames;
        public float denoisingRange;
        public float disocclusionThreshold;
        public float disocclusionThresholdAlternate;
        public float cameraAttachedReflectionMaterialID;
        public float strandMaterialID;
        public float historyFixAlternatePixelStrideMaterialID;
        public float strandThickness;
        public float splitScreen;

        // printfAt[2] (uint16_t)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] printfAt;

        public float debug;

        // rectOrigin[2] (uint32_t)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] rectOrigin;

        public uint frameIndex;

        // accumulationMode (enum) - ensure underlying type matches C++ (假设 C++ 为 uint32_t)
        public AccumulationMode accumulationMode;

        // booleans - make sure they are 1 byte in native layout
        [MarshalAs(UnmanagedType.I1)] public bool isMotionVectorInWorldSpace;
        [MarshalAs(UnmanagedType.I1)] public bool isHistoryConfidenceAvailable;
        [MarshalAs(UnmanagedType.I1)] public bool isDisocclusionThresholdMixAvailable;
        [MarshalAs(UnmanagedType.I1)] public bool isBaseColorMetalnessAvailable;
        [MarshalAs(UnmanagedType.I1)] public bool enableValidation;

        // Note: if C++ aligns enums/fields differently, Pack = 1 强制紧凑布局.


        // 创建默认初始化的 CommonSettings（确保数组被分配）
        public static NRDCommonSettings Default()
        {
            NRDCommonSettings s = new NRDCommonSettings
            {
                //Must Set in C# side
                viewToClipMatrix = new float[16],
                viewToClipMatrixPrev = new float[16],
                worldToViewMatrix = new float[16],
                worldToViewMatrixPrev = new float[16],
                // default worldPrevToWorldMatrix = identity
                worldPrevToWorldMatrix = new float[16]
                {
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f,
                    0f, 0f, 0f, 1f
                },
                cameraJitter = new float[2],
                cameraJitterPrev = new float[2],
                resourceSize = new ushort[2],
                resourceSizePrev = new ushort[2],
                rectSize = new ushort[2],
                rectSizePrev = new ushort[2],


                //can keep default
                motionVectorScale = new float[3] { 1.0f, 1.0f, 0.0f },
                viewZScale = 1.0f,
                timeDeltaBetweenFrames = 0.0f,
                denoisingRange = 500000.0f,
                disocclusionThreshold = 0.01f,
                disocclusionThresholdAlternate = 0.05f,
                cameraAttachedReflectionMaterialID = 999.0f,
                strandMaterialID = 999.0f,
                historyFixAlternatePixelStrideMaterialID = 999.0f,
                strandThickness = 80e-6f,
                splitScreen = 0.0f,
                printfAt = new ushort[2] { 9999, 9999 },
                debug = 0.0f,
                rectOrigin = new uint[2],
                frameIndex = 0,
                accumulationMode = AccumulationMode.CONTINUE,
                isMotionVectorInWorldSpace = false,
                isHistoryConfidenceAvailable = false,
                isDisocclusionThresholdMixAvailable = false,
                isBaseColorMetalnessAvailable = false,
                enableValidation = false
            };

            return s;
        }
    }


    public enum NRDResult : uint
    {
        SUCCESS,
        FAILURE,
        INVALID_ARGUMENT,
        UNSUPPORTED,
        NON_UNIQUE_IDENTIFIER,

        MAX_NUM
    };

    #endregion

    #region SIGMA

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SigmaSettings
    {
        // Direction to the light source
        // IMPORTANT: it is needed only for directional light sources (sun)
        // rectOrigin[2] (uint32_t)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] lightDirection;

        // (normalized %) - represents maximum allowed deviation from the local tangent plane
        public  float planeDistanceSensitivity;

        // [0; SIGMA_MAX_HISTORY_FRAME_NUM] - maximum number of linearly accumulated frames
        // 0 - disables the stabilization pass
        // Always accumulate in "seconds" not in "frames", use "GetMaxAccumulatedFrameNum" for conversion
        public  uint maxStabilizedFrameNum;

        public static SigmaSettings Default()
        {
            SigmaSettings settings = new SigmaSettings()
            {
                lightDirection = new float[3] { 0, 0, 0 },
                planeDistanceSensitivity = 0.02f,
                maxStabilizedFrameNum = 5
            };
            return settings;
        }
    };


    //In HLSL
    public struct SigmaSharedConstants
    {
        public float4x4 gWorldToView;
        public float4x4 gViewToClip;
        public float4x4 gWorldToClipPrev;
        public float4x4 gWorldToViewPrev;
        public float4 gRotator;
        public float4 gRotatorPost;
        public float4 gViewVectorWorld;
        public float4 gLightDirectionView;
        public float4 gFrustum;
        public float4 gFrustumPrev;
        public float4 gCameraDelta;
        public float4 gMvScale;
        public float2 gResourceSizeInv;
        public float2 gResourceSizeInvPrev;
        public float2 gRectSize;
        public float2 gRectSizeInv;
        public float2 gRectSizePrev;
        public float2 gResolutionScale;
        public float2 gRectOffset;
        public uint2 gPrintfAt;
        public uint2 gRectOrigin;
        public int2 gRectSizeMinusOne;
        public int2 gTilesSizeMinusOne;
        public float gOrthoMode;
        public float gUnproject;
        public float gDenoisingRange;
        public float gPlaneDistSensitivity;
        public float gStabilizationStrength;
        public float gDebug;
        public float gSplitScreen;
        public float gViewZScale;
        public float gMinRectDimMulUnproject;
        public uint gFrameIndex;
        public uint gIsRectChanged;
    };

    #endregion


    #region REBLUR

    public enum CheckerboardMode : byte
    {
        OFF,
        BLACK,
        WHITE,

        MAX_NUM
    }

    public enum HitDistanceReconstructionMode : byte
    {
        // Probabilistic split at primary hit is not used, hence hit distance is always valid (reconstruction is not needed)
        OFF,

        // If hit distance is invalid due to probabilistic sampling, reconstruct using 3x3 neighbors.
        // Probability at primary hit must be clamped to [1/4; 3/4] range to guarantee a sample in this area.
        // White noise must be replaced with Bayer dithering to guarantee a sample in this area (see NRD sample)
        AREA_3X3,

        // If hit distance is invalid due to probabilistic sampling, reconstruct using 5x5 neighbors.
        // Probability at primary hit must be clamped to [1/16; 15/16] range to guarantee a sample in this area.
        // White noise must be replaced with Bayer dithering to guarantee a sample in this area (see NRD sample)
        AREA_5X5,

        MAX_NUM
    }

    // "Normalized hit distance" = saturate( "hit distance" / f ), where:
    // f = ( A + viewZ * B ) * lerp( 1.0, C, exp2( D * roughness ^ 2 ) ), see "NRD.hlsl/REBLUR_FrontEnd_GetNormHitDist"
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HitDistanceParameters
    {
        // (units > 0) - constant value
        public float A;

        // (> 0) - viewZ based linear scale (1 m - 10 cm, 10 m - 1 m, 100 m - 10 m)
        public float B;

        // (>= 1) - roughness based scale, use values > 1 to get bigger hit distance for low roughness
        public float C;

        // (<= 0) - absolute value should be big enough to collapse "exp2( D * roughness ^ 2 )" to "~0" for roughness = 1
        public float D;

        public static HitDistanceParameters Default()
        {
            return new HitDistanceParameters
            {
                A = 3.0f,
                B = 0.1f,
                C = 20.0f,
                D = -25.0f
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ReblurAntilagSettings
    {
        // [1; 5] - delta is reduced by local variance multiplied by this value
        public float luminanceSigmaScale; // can be 3.0 or even less if signal is good

        // [1; 5] - antilag sensitivity (smaller values increase sensitivity)
        public float luminanceSensitivity; // can be 2.0 or even less if signal is good

        public static ReblurAntilagSettings Default()
        {
            return new ReblurAntilagSettings
            {
                luminanceSigmaScale = 4.0f,
                luminanceSensitivity = 3.0f
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ResponsiveAccumulationSettings
    {
        // [0; 1] - if roughness < roughnessThreshold, temporal accumulation becomes responsive and driven by roughness (useful for animated water)
        // maxAccumulatedFrameNum *= smoothstep( 0, 1, max( roughness, 1e-3 ) / max( roughnessThreshold, 1e-3 ) )
        public float roughnessThreshold;

        // [0; historyFixFrameNum] - preserves a few frames in history even for 0-roughness
        // If the signal is clean this value can be reduced to 0 or 1
        public uint minAccumulatedFrameNum;

        public static ResponsiveAccumulationSettings Default()
        {
            return new ResponsiveAccumulationSettings
            {
                roughnessThreshold = 0.0f,
                minAccumulatedFrameNum = 3
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ReblurSettings
    {
        public HitDistanceParameters hitDistanceParameters;
        public ReblurAntilagSettings antilagSettings;
        public ResponsiveAccumulationSettings responsiveAccumulationSettings;

        // [0; REBLUR_MAX_HISTORY_FRAME_NUM] - maximum number of linearly accumulated frames
        // Always accumulate in "seconds" not in "frames", use "GetMaxAccumulatedFrameNum" for conversion
        public uint maxAccumulatedFrameNum;

        // [0; maxAccumulatedFrameNum) - maximum number of linearly accumulated frames for fast history
        // Values ">= maxAccumulatedFrameNum" disable fast history
        // Usually 5x-7x times shorter than the main history (casting more rays, using SHARC or other signal improving techniques help to accumulate less)
        public uint maxFastAccumulatedFrameNum;

        // [0; maxAccumulatedFrameNum] - maximum number of linearly accumulated frames for stabilized radiance
        // "0" disables the stabilization pass
        // Values ">= maxAccumulatedFrameNum"  get clamped to "maxAccumulatedFrameNum"
        public uint maxStabilizedFrameNum;

        // [0; 3] - number of reconstructed frames after history reset (less than "maxFastAccumulatedFrameNum")
        public uint historyFixFrameNum;

        // (> 0) - base stride between pixels in 5x5 history reconstruction kernel
        public uint historyFixBasePixelStride;
        public uint historyFixAlternatePixelStride; // see "historyFixAlternatePixelStrideMaterialID"

        // [1; 3] - standard deviation scale of the color box for clamping slow "main" history to responsive "fast" history
        // REBLUR clamps the spatially processed "main" history to the spatially unprocessed "fast" history. It implies using smaller variance scaling than in RELAX.
        // A bit smaller values (> 1) may be used with clean signals. The implementation will adjust this under the hood if spatial sampling is disabled
        public float fastHistoryClampingSigmaScale; // 2 is old default, 1.5 works well even for dirty signals, 1.1 is a safe value for occlusion denoising

        // (pixels) - pre-accumulation spatial reuse pass blur radius (0 = disabled, must be used in case of badly defined signals and probabilistic sampling)
        public float diffusePrepassBlurRadius;
        public float specularPrepassBlurRadius;

        // (0; 0.2] - bigger values reduce sensitivity to shadows in spatial passes, smaller values are recommended for signals with relatively clean hit distance (like RTXDI/RESTIR)
        public float minHitDistanceWeight;

        // (pixels) - min denoising radius (for converged state)
        public float minBlurRadius;

        // (pixels) - base (max) denoising radius (gets reduced over time)
        public float maxBlurRadius;

        // (normalized %) - base fraction of diffuse or specular lobe angle used to drive normal based rejection
        public float lobeAngleFraction;

        // (normalized %) - base fraction of center roughness used to drive roughness based rejection
        public float roughnessFraction;

        // (normalized %) - represents maximum allowed deviation from the local tangent plane
        public float planeDistanceSensitivity;

        // "IN_MV = lerp(IN_MV, specularMotion, smoothstep(this[0], this[1], specularProbability))"
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] specularProbabilityThresholdsForMvModification;

        // [1; 3] - undesired sporadic outliers suppression to keep output stable (smaller values maximize suppression in exchange of bias)
        public float fireflySuppressorMinRelativeScale;

        // (Optional) material ID comparison: max(m0, minMaterial) == max(m1, minMaterial) (requires "NormalEncoding::R10_G10_B10_A2_UNORM")
        public float minMaterialForDiffuse;
        public float minMaterialForSpecular;

        // If not OFF and used for DIFFUSE_SPECULAR, defines diffuse orientation, specular orientation is the opposite. Used only if "NRD_SUPPORTS_CHECKERBOARD = 1"
        public CheckerboardMode checkerboardMode;

        // Must be used only in case of probabilistic sampling (not checkerboarding), when a pixel can be skipped and have "0" (invalid) hit distance
        public HitDistanceReconstructionMode hitDistanceReconstructionMode;

        // Adds bias in case of badly defined signals, but tries to fight with fireflies
        [MarshalAs(UnmanagedType.I1)]
        public bool enableAntiFirefly;

        // In rare cases, when bright samples are so sparse that any other bright neighbor can't
        // be reached, pre-pass transforms a standalone bright pixel into a standalone bright blob,
        // worsening the situation. Despite that it's a problem of sampling, the denoiser needs to
        // handle it somehow on its side too. Diffuse pre-pass can be just disabled, but for specular
        // it's still needed to find optimal hit distance for tracking. This boolean allow to use
        // specular pre-pass for tracking purposes only (use with care)
        [MarshalAs(UnmanagedType.I1)]
        public bool usePrepassOnlyForSpecularMotionEstimation;

        // Allows to get diffuse or specular history length in ".w" channel of the output instead of denoised ambient/specular occlusion (normalized hit distance).
        // Diffuse history length shows disocclusions, specular history length is more complex and includes accelerations of various kinds caused by specular tracking.
        // History length is measured in frames, it can be in "[0; maxAccumulatedFrameNum]" range
        [MarshalAs(UnmanagedType.I1)]
        public bool returnHistoryLengthInsteadOfOcclusion;

        public static ReblurSettings Default()
        {
            return new ReblurSettings
            {
                hitDistanceParameters = HitDistanceParameters.Default(),
                antilagSettings = ReblurAntilagSettings.Default(),
                responsiveAccumulationSettings = ResponsiveAccumulationSettings.Default(),
                maxAccumulatedFrameNum = 30,
                maxFastAccumulatedFrameNum = 6,
                maxStabilizedFrameNum = 63, // REBLUR_MAX_HISTORY_FRAME_NUM
                historyFixFrameNum = 3,
                historyFixBasePixelStride = 14,
                historyFixAlternatePixelStride = 14,
                fastHistoryClampingSigmaScale = 2.0f,
                diffusePrepassBlurRadius = 30.0f,
                specularPrepassBlurRadius = 50.0f,
                minHitDistanceWeight = 0.1f,
                minBlurRadius = 1.0f,
                maxBlurRadius = 30.0f,
                lobeAngleFraction = 0.15f,
                roughnessFraction = 0.15f,
                planeDistanceSensitivity = 0.02f,
                specularProbabilityThresholdsForMvModification = new float[2] { 0.5f, 0.9f },
                fireflySuppressorMinRelativeScale = 2.0f,
                minMaterialForDiffuse = 4.0f,
                minMaterialForSpecular = 4.0f,
                checkerboardMode = CheckerboardMode.OFF,
                hitDistanceReconstructionMode = HitDistanceReconstructionMode.OFF,
                enableAntiFirefly = false,
                usePrepassOnlyForSpecularMotionEstimation = false,
                returnHistoryLengthInsteadOfOcclusion = false
            };
        }
    }

    //In HLSL
    public struct ReblurSharedConstants
    {
        public float4x4 gWorldToClip;
        public float4x4 gViewToClip;
        public float4x4 gViewToWorld;
        public float4x4 gWorldToViewPrev;
        public float4x4 gWorldToClipPrev;
        public float4x4 gWorldPrevToWorld;
        public float4 gRotatorPre;
        public float4 gRotator;
        public float4 gRotatorPost;
        public float4 gFrustum;
        public float4 gFrustumPrev;
        public float4 gCameraDelta;
        public float4 gHitDistParams;
        public float4 gViewVectorWorld;
        public float4 gViewVectorWorldPrev;
        public float4 gMvScale;
        public float2 gAntilagParams;
        public float2 gResourceSize;
        public float2 gResourceSizeInv;
        public float2 gResourceSizeInvPrev;
        public float2 gRectSize;
        public float2 gRectSizeInv;
        public float2 gRectSizePrev;
        public float2 gResolutionScale;
        public float2 gResolutionScalePrev;
        public float2 gRectOffset;
        public float2 gSpecProbabilityThresholdsForMvModification;
        public float2 gJitter;
        public uint2 gPrintfAt;
        public uint2 gRectOrigin;
        public int2 gRectSizeMinusOne;
        public float gDisocclusionThreshold;
        public float gDisocclusionThresholdAlternate;
        public float gCameraAttachedReflectionMaterialID;
        public float gStrandMaterialID;
        public float gStrandThickness;
        public float gStabilizationStrength;
        public float gDebug;
        public float gOrthoMode;
        public float gUnproject;
        public float gDenoisingRange;
        public float gPlaneDistSensitivity;
        public float gFramerateScale;
        public float gMinBlurRadius;
        public float gMaxBlurRadius;
        public float gDiffPrepassBlurRadius;
        public float gSpecPrepassBlurRadius;
        public float gMaxAccumulatedFrameNum;
        public float gMaxFastAccumulatedFrameNum;
        public float gAntiFirefly;
        public float gLobeAngleFraction;
        public float gRoughnessFraction;
        public float gHistoryFixFrameNum;
        public float gHistoryFixBasePixelStride;
        public float gHistoryFixAlternatePixelStride;
        public float gHistoryFixAlternatePixelStrideMaterialID;
        public float gFastHistoryClampingSigmaScale;
        public float gMinRectDimMulUnproject;
        public float gUsePrepassNotOnlyForSpecularMotionEstimation;
        public float gSplitScreen;
        public float gSplitScreenPrev;
        public float gCheckerboardResolveAccumSpeed;
        public float gViewZScale;
        public float gFireflySuppressorMinRelativeScale;
        public float gMinHitDistanceWeight;
        public float gDiffMinMaterial;
        public float gSpecMinMaterial;
        public float gResponsiveAccumulationInvRoughnessThreshold;
        public uint gResponsiveAccumulationMinAccumulatedFrameNum;
        public uint gHasHistoryConfidence;
        public uint gHasDisocclusionThresholdMix;
        public uint gDiffCheckerboard;
        public uint gSpecCheckerboard;
        public uint gFrameIndex;
        public uint gIsRectChanged;
        public uint gResetHistory;
        public uint gReturnHistoryLengthInsteadOfOcclusion;
    }

    #endregion
}