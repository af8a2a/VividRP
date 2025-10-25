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
}