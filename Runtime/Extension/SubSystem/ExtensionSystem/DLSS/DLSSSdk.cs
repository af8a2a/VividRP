//------------------------------------------------------------------------------
// DLSSSdk.cs - C# SDK for DLSS Native Plugin
//------------------------------------------------------------------------------
// Low-level bindings and helper methods for DLSS integration.
// Based on UnityDenoiserPlugin pattern - all context management is done in C#.
//------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// NGX Result codes matching NVSDK_NGX_Result.
    /// </summary>
    public enum NVSDK_NGX_Result : int
    {
        NVSDK_NGX_Result_Success = 0x1,
        NVSDK_NGX_Result_Fail = unchecked((int)0xBAD00000),
        NVSDK_NGX_Result_FAIL_FeatureNotSupported = unchecked((int)0xBAD00001),
        NVSDK_NGX_Result_FAIL_PlatformError = unchecked((int)0xBAD00002),
        NVSDK_NGX_Result_FAIL_FeatureAlreadyExists = unchecked((int)0xBAD00003),
        NVSDK_NGX_Result_FAIL_FeatureNotFound = unchecked((int)0xBAD00004),
        NVSDK_NGX_Result_FAIL_InvalidParameter = unchecked((int)0xBAD00005),
        NVSDK_NGX_Result_FAIL_ScratchBufferTooSmall = unchecked((int)0xBAD00006),
        NVSDK_NGX_Result_FAIL_NotInitialized = unchecked((int)0xBAD00007),
        NVSDK_NGX_Result_FAIL_UnsupportedInputFormat = unchecked((int)0xBAD00008),
        NVSDK_NGX_Result_FAIL_RWFlagMissing = unchecked((int)0xBAD00009),
        NVSDK_NGX_Result_FAIL_MissingInput = unchecked((int)0xBAD0000A),
        NVSDK_NGX_Result_FAIL_UnableToInitializeFeature = unchecked((int)0xBAD0000B),
        NVSDK_NGX_Result_FAIL_OutOfDate = unchecked((int)0xBAD0000C),
        NVSDK_NGX_Result_FAIL_OutOfGPUMemory = unchecked((int)0xBAD0000D),
        NVSDK_NGX_Result_FAIL_UnsupportedFormat = unchecked((int)0xBAD0000E),
        NVSDK_NGX_Result_FAIL_UnableToWriteToAppDataPath = unchecked((int)0xBAD0000F),
        NVSDK_NGX_Result_FAIL_UnsupportedParameter = unchecked((int)0xBAD00010),
        NVSDK_NGX_Result_FAIL_Denied = unchecked((int)0xBAD00011),
        NVSDK_NGX_Result_FAIL_NotImplemented = unchecked((int)0xBAD00012)
    }

    /// <summary>
    /// Engine type enumeration matching NVSDK_NGX_EngineType.
    /// </summary>
    public enum NVSDK_NGX_EngineType : int
    {
        NVSDK_NGX_ENGINE_TYPE_CUSTOM = 0,
        NVSDK_NGX_ENGINE_TYPE_UNREAL = 1,
        NVSDK_NGX_ENGINE_TYPE_UNITY = 2,
        NVSDK_NGX_ENGINE_TYPE_OMNIVERSE = 3
    }

    /// <summary>
    /// Logging level matching NVSDK_NGX_Logging_Level.
    /// </summary>
    public enum NVSDK_NGX_Logging_Level : int
    {
        NVSDK_NGX_LOGGING_LEVEL_OFF = 0,
        NVSDK_NGX_LOGGING_LEVEL_ON = 1,
        NVSDK_NGX_LOGGING_LEVEL_VERBOSE = 2
    }

    /// <summary>
    /// NGX Feature types.
    /// </summary>
    public enum NVSDK_NGX_Feature : int
    {
        NVSDK_NGX_Feature_SuperSampling = 1,        // DLSS-SR
        NVSDK_NGX_Feature_RayReconstruction = 13    // DLSS-RR
    }

    /// <summary>
    /// Performance/Quality presets matching NVSDK_NGX_PerfQuality_Value.
    /// </summary>
    public enum NVSDK_NGX_PerfQuality_Value : int
    {
        NVSDK_NGX_PerfQuality_Value_MaxPerf = 0,
        NVSDK_NGX_PerfQuality_Value_Balanced = 1,
        NVSDK_NGX_PerfQuality_Value_MaxQuality = 2,
        NVSDK_NGX_PerfQuality_Value_UltraPerformance = 3,
        NVSDK_NGX_PerfQuality_Value_UltraQuality = 4,
        NVSDK_NGX_PerfQuality_Value_DLAA = 5
    }

    /// <summary>
    /// DLSS Feature creation flags.
    /// </summary>
    [Flags]
    public enum NVSDK_NGX_DLSS_Feature_Flags : int
    {
        None = 0,
        IsHDR = (1 << 0),
        MVLowRes = (1 << 1),
        MVJittered = (1 << 2),
        DepthInverted = (1 << 3),
        Reserved_0 = (1 << 4),
        DoSharpening = (1 << 5),
        AutoExposure = (1 << 6),
        AlphaUpscaling = (1 << 7)
    }

    /// <summary>
    /// Dimensions struct for resolution parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NVSDK_NGX_Dimensions
    {
        public uint Width;
        public uint Height;

        public NVSDK_NGX_Dimensions(uint width, uint height)
        {
            Width = width;
            Height = height;
        }

        public NVSDK_NGX_Dimensions(int width, int height)
        {
            Width = (uint)width;
            Height = (uint)height;
        }
    }

    /// <summary>
    /// DLSS SDK - Low-level bindings and helper methods.
    /// All context management is done in C#, native plugin is stateless.
    /// </summary>
    public static class DLSSSdk
    {
        private const string DLL_NAME = "UnityDLSS";
        private const CallingConvention CALLING_CONVENTION = CallingConvention.Cdecl;

        public const int DLSS_INVALID_FEATURE_HANDLE = -1;

        // Reference counter to track initialization
        private static int s_initializationCount = 0;

        // Cached feature availability
        private static bool s_superSamplingAvailable = false;
        private static bool s_rayReconstructionAvailable = false;

        // Ring buffer for C# to C++ data passing
        private static RingBufferAllocator s_allocator;
        private const int ALLOCATOR_SIZE = 2 * 1024 * 1024; // 2MB

        #region P/Invoke Declarations

        [StructLayout(LayoutKind.Sequential)]
        private struct DLSSInitParams
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string projectId;
            public NVSDK_NGX_EngineType engineType;
            [MarshalAs(UnmanagedType.LPStr)]
            public string engineVersion;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string applicationDataPath;
            public NVSDK_NGX_Logging_Level loggingLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DLSSCreateFeatureParams
        {
            public int handle;
            public NVSDK_NGX_Feature feature;
            public IntPtr parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DLSSEvaluateFeatureParams
        {
            public int handle;
            public IntPtr parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DLSSDestroyFeatureParams
        {
            public int handle;
        }

        // Event IDs
        private const int EVENT_ID_CREATE_FEATURE = 0;
        private const int EVENT_ID_EVALUATE_FEATURE = 1;
        private const int EVENT_ID_DESTROY_FEATURE = 2;

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_Init_with_ProjectID_D3D12(ref DLSSInitParams initParams);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_Shutdown_D3D12();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_AllocateParameters_D3D12_Internal(out IntPtr ppOutParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_GetCapabilityParameters_D3D12_Internal(out IntPtr ppOutParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_DestroyParameters_D3D12_Internal(IntPtr pInParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_AllocateFeatureHandle();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_FreeFeatureHandle(int handle);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern IntPtr DLSS_UnityRenderEventFunc();

        // Parameter setters
        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetULL_Internal(IntPtr pParameters, string paramName, ulong value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetF_Internal(IntPtr pParameters, string paramName, float value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetD_Internal(IntPtr pParameters, string paramName, double value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetUI_Internal(IntPtr pParameters, string paramName, uint value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetI_Internal(IntPtr pParameters, string paramName, int value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetD3d12Resource_Internal(IntPtr pParameters, string paramName, IntPtr value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetVoidPointer_Internal(IntPtr pParameters, string paramName, IntPtr value);

        // Parameter getters
        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetULL_Internal(IntPtr pParameters, string paramName, out ulong pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetF_Internal(IntPtr pParameters, string paramName, out float pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetD_Internal(IntPtr pParameters, string paramName, out double pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetUI_Internal(IntPtr pParameters, string paramName, out uint pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetI_Internal(IntPtr pParameters, string paramName, out int pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetVoidPointer_Internal(IntPtr pParameters, string paramName, out IntPtr ppValue);

        #endregion

        #region NGX Parameter Names

        // Common parameters
        public const string NVSDK_NGX_Parameter_Width = "Width";
        public const string NVSDK_NGX_Parameter_Height = "Height";
        public const string NVSDK_NGX_Parameter_OutWidth = "OutWidth";
        public const string NVSDK_NGX_Parameter_OutHeight = "OutHeight";
        public const string NVSDK_NGX_Parameter_PerfQualityValue = "PerfQualityValue";
        public const string NVSDK_NGX_Parameter_Sharpness = "Sharpness";
        public const string NVSDK_NGX_Parameter_CreationNodeMask = "CreationNodeMask";
        public const string NVSDK_NGX_Parameter_VisibilityNodeMask = "VisibilityNodeMask";

        // DLSS-specific parameters
        public const string NVSDK_NGX_Parameter_DLSS_Feature_Create_Flags = "DLSS.Feature.Create.Flags";
        public const string NVSDK_NGX_Parameter_DLSS_Enable_Output_Subrects = "DLSS.Enable.Output.Subrects";

        // Input textures
        public const string NVSDK_NGX_Parameter_Color = "Color";
        public const string NVSDK_NGX_Parameter_Output = "Output";
        public const string NVSDK_NGX_Parameter_Depth = "Depth";
        public const string NVSDK_NGX_Parameter_MotionVectors = "MotionVectors";
        public const string NVSDK_NGX_Parameter_TransparencyMask = "TransparencyMask";
        public const string NVSDK_NGX_Parameter_ExposureTexture = "ExposureTexture";
        public const string NVSDK_NGX_Parameter_DLSS_Input_Bias_Current_Color_Mask = "DLSS.Input.Bias.Current.Color.Mask";

        // Motion vector parameters
        public const string NVSDK_NGX_Parameter_Jitter_Offset_X = "Jitter.Offset.X";
        public const string NVSDK_NGX_Parameter_Jitter_Offset_Y = "Jitter.Offset.Y";
        public const string NVSDK_NGX_Parameter_MV_Scale_X = "MV.Scale.X";
        public const string NVSDK_NGX_Parameter_MV_Scale_Y = "MV.Scale.Y";
        public const string NVSDK_NGX_Parameter_Reset = "Reset";

        // Subrect parameters
        public const string NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Width = "DLSS.Render.Subrect.Dimensions.Width";
        public const string NVSDK_NGX_Parameter_DLSS_Render_Subrect_Dimensions_Height = "DLSS.Render.Subrect.Dimensions.Height";

        // Exposure parameters
        public const string NVSDK_NGX_Parameter_DLSS_Pre_Exposure = "DLSS.Pre.Exposure";
        public const string NVSDK_NGX_Parameter_DLSS_Exposure_Scale = "DLSS.Exposure.Scale";

        // Indicator parameters
        public const string NVSDK_NGX_Parameter_DLSS_Indicator_Invert_X_Axis = "DLSS.Indicator.Invert.X.Axis";
        public const string NVSDK_NGX_Parameter_DLSS_Indicator_Invert_Y_Axis = "DLSS.Indicator.Invert.Y.Axis";

        // Frame timing parameters
        public const string NVSDK_NGX_Parameter_FrameTimeDeltaInMsec = "FrameTimeDeltaInMsec";

        // Capability parameters
        public const string NVSDK_NGX_Parameter_SuperSampling_Available = "SuperSampling.Available";
        public const string NVSDK_NGX_Parameter_SuperSamplingDenoising_Available = "SuperSamplingDenoising.Available";

        // Optimal settings parameters
        public const string NVSDK_NGX_Parameter_DLSS_Get_Dynamic_Max_Render_Width = "DLSS.Get.Dynamic.Max.Render.Width";
        public const string NVSDK_NGX_Parameter_DLSS_Get_Dynamic_Max_Render_Height = "DLSS.Get.Dynamic.Max.Render.Height";
        public const string NVSDK_NGX_Parameter_DLSS_Get_Dynamic_Min_Render_Width = "DLSS.Get.Dynamic.Min.Render.Width";
        public const string NVSDK_NGX_Parameter_DLSS_Get_Dynamic_Min_Render_Height = "DLSS.Get.Dynamic.Min.Render.Height";

        #endregion

        #region Public API

        /// <summary>
        /// Check if an NGX result indicates success.
        /// </summary>
        public static bool NVSDK_NGX_SUCCEED(NVSDK_NGX_Result result)
        {
            return (int)result > 0;
        }

        /// <summary>
        /// Check if an NGX result indicates failure.
        /// </summary>
        public static bool NVSDK_NGX_FAILED(NVSDK_NGX_Result result)
        {
            return (int)result <= 0;
        }

        /// <summary>
        /// Initialize DLSS SDK. Reference counted - safe to call multiple times.
        /// </summary>
        public static NVSDK_NGX_Result DLSS_Init()
        {
            s_initializationCount++;

            if (s_initializationCount == 1)
            {
                var initParams = new DLSSInitParams
                {
                    projectId = "",
                    engineType = NVSDK_NGX_EngineType.NVSDK_NGX_ENGINE_TYPE_UNITY,
                    engineVersion = Application.version,
                    applicationDataPath = "",
                    loggingLevel = NVSDK_NGX_Logging_Level.NVSDK_NGX_LOGGING_LEVEL_VERBOSE
                };

                var result = (NVSDK_NGX_Result)DLSS_Init_with_ProjectID_D3D12(ref initParams);
                LogDlssResult(result, "DLSS_Init");

                if (NVSDK_NGX_SUCCEED(result))
                {
                    QueryFeatureAvailability();
                }

                return result;
            }

            return NVSDK_NGX_Result.NVSDK_NGX_Result_Success;
        }

        /// <summary>
        /// Shutdown DLSS SDK. Reference counted - only shuts down on last call.
        /// </summary>
        public static NVSDK_NGX_Result DLSS_Shutdown()
        {
            if (s_initializationCount <= 0)
            {
                s_initializationCount = 0;
                return NVSDK_NGX_Result.NVSDK_NGX_Result_Success;
            }

            s_initializationCount--;

            if (s_initializationCount == 0)
            {
                s_superSamplingAvailable = false;
                s_rayReconstructionAvailable = false;

                s_allocator?.Dispose();
                s_allocator = null;

                var result = (NVSDK_NGX_Result)DLSS_Shutdown_D3D12();
                LogDlssResult(result, "DLSS_Shutdown");
                return result;
            }

            return NVSDK_NGX_Result.NVSDK_NGX_Result_Success;
        }

        /// <summary>
        /// Check if DLSS Super Sampling is available.
        /// </summary>
        public static bool DLSS_IsSuperSamplingAvailable() => s_superSamplingAvailable;

        /// <summary>
        /// Check if DLSS Ray Reconstruction is available.
        /// </summary>
        public static bool DLSS_IsRayReconstructionAvailable() => s_rayReconstructionAvailable;

        /// <summary>
        /// Create a DLSS feature via command buffer.
        /// </summary>
        public static int DLSS_CreateFeature(CommandBuffer cmd, NVSDK_NGX_Feature feature, IntPtr parameters)
        {
            int handle = DLSS_AllocateFeatureHandle();
            if (handle == DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSSdk] Failed to allocate feature handle");
                return DLSS_INVALID_FEATURE_HANDLE;
            }

            var createParams = new DLSSCreateFeatureParams
            {
                handle = handle,
                feature = feature,
                parameters = parameters
            };

            IntPtr ptr = Allocator.Allocate(createParams);
            if (ptr == IntPtr.Zero)
            {
                Debug.LogError("[DLSSSdk] Failed to allocate space in ring buffer for CreateFeature");
                DLSS_FreeFeatureHandle(handle);
                return DLSS_INVALID_FEATURE_HANDLE;
            }

            cmd.IssuePluginEventAndData(DLSS_UnityRenderEventFunc(), EVENT_ID_CREATE_FEATURE, ptr);
            return handle;
        }

        /// <summary>
        /// Evaluate (execute) a DLSS feature via command buffer.
        /// </summary>
        public static void DLSS_EvaluateFeature(CommandBuffer cmd, int handle, IntPtr parameters)
        {
            var evalParams = new DLSSEvaluateFeatureParams
            {
                handle = handle,
                parameters = parameters
            };

            IntPtr ptr = Allocator.Allocate(evalParams);
            if (ptr == IntPtr.Zero)
            {
                Debug.LogError("[DLSSSdk] Failed to allocate space in ring buffer for EvaluateFeature");
                return;
            }

            cmd.IssuePluginEventAndData(DLSS_UnityRenderEventFunc(), EVENT_ID_EVALUATE_FEATURE, ptr);
        }

        /// <summary>
        /// Destroy a DLSS feature via command buffer.
        /// </summary>
        public static void DLSS_DestroyFeature(CommandBuffer cmd, int handle)
        {
            var destroyParams = new DLSSDestroyFeatureParams
            {
                handle = handle
            };

            IntPtr ptr = Allocator.Allocate(destroyParams);
            if (ptr == IntPtr.Zero)
            {
                Debug.LogError("[DLSSSdk] Failed to allocate space in ring buffer for DestroyFeature");
                return;
            }

            cmd.IssuePluginEventAndData(DLSS_UnityRenderEventFunc(), EVENT_ID_DESTROY_FEATURE, ptr);
        }

        /// <summary>
        /// Allocate NGX parameters.
        /// </summary>
        public static NVSDK_NGX_Result DLSS_AllocateParameters_D3D12(out IntPtr ppOutParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_AllocateParameters_D3D12_Internal(out ppOutParameters);
            LogDlssResult(result, "DLSS_AllocateParameters_D3D12");
            return result;
        }

        /// <summary>
        /// Get capability parameters.
        /// </summary>
        public static NVSDK_NGX_Result DLSS_GetCapabilityParameters_D3D12(out IntPtr ppOutParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_GetCapabilityParameters_D3D12_Internal(out ppOutParameters);
            LogDlssResult(result, "DLSS_GetCapabilityParameters_D3D12");
            return result;
        }

        /// <summary>
        /// Destroy NGX parameters.
        /// </summary>
        public static NVSDK_NGX_Result DLSS_DestroyParameters_D3D12(IntPtr pInParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_DestroyParameters_D3D12_Internal(pInParameters);
            LogDlssResult(result, "DLSS_DestroyParameters_D3D12");
            return result;
        }

        #endregion

        #region Parameter Helpers

        public static void DLSS_Parameter_SetUI(IntPtr pParams, string name, uint value)
            => DLSS_Parameter_SetUI_Internal(pParams, name, value);

        public static void DLSS_Parameter_SetI(IntPtr pParams, string name, int value)
            => DLSS_Parameter_SetI_Internal(pParams, name, value);

        public static void DLSS_Parameter_SetF(IntPtr pParams, string name, float value)
            => DLSS_Parameter_SetF_Internal(pParams, name, value);

        public static void DLSS_Parameter_SetD3d12Resource(IntPtr pParams, string name, IntPtr resource)
            => DLSS_Parameter_SetD3d12Resource_Internal(pParams, name, resource);

        public static void DLSS_Parameter_SetD3d12RenderTexture(IntPtr pParams, string name, RenderTexture texture)
        {
            IntPtr ptr = texture != null ? texture.GetNativeTexturePtr() : IntPtr.Zero;
            DLSS_Parameter_SetD3d12Resource_Internal(pParams, name, ptr);
        }

        public static void DLSS_Parameter_SetVoidPointer(IntPtr pParams, string name, IntPtr value)
            => DLSS_Parameter_SetVoidPointer_Internal(pParams, name, value);

        /// <summary>
        /// Set a 4x4 matrix parameter. Uploads as 16 floats in row-major order.
        /// </summary>
        public static unsafe void DLSS_Parameter_SetMatrix4x4(IntPtr pParams, string name, Matrix4x4 matrix)
        {
            // NGX expects matrices via void pointer - we need to marshal the data
            // For simplicity, we'll set individual float values using a naming convention
            // NGX typically expects WorldToViewMatrix_00, WorldToViewMatrix_01, etc.
            // However, the common approach is to pass a pointer to the matrix data
            // For now, set as individual named elements (common NGX pattern)
            DLSS_Parameter_SetF(pParams, name + "_00", matrix.m00);
            DLSS_Parameter_SetF(pParams, name + "_01", matrix.m01);
            DLSS_Parameter_SetF(pParams, name + "_02", matrix.m02);
            DLSS_Parameter_SetF(pParams, name + "_03", matrix.m03);
            DLSS_Parameter_SetF(pParams, name + "_10", matrix.m10);
            DLSS_Parameter_SetF(pParams, name + "_11", matrix.m11);
            DLSS_Parameter_SetF(pParams, name + "_12", matrix.m12);
            DLSS_Parameter_SetF(pParams, name + "_13", matrix.m13);
            DLSS_Parameter_SetF(pParams, name + "_20", matrix.m20);
            DLSS_Parameter_SetF(pParams, name + "_21", matrix.m21);
            DLSS_Parameter_SetF(pParams, name + "_22", matrix.m22);
            DLSS_Parameter_SetF(pParams, name + "_23", matrix.m23);
            DLSS_Parameter_SetF(pParams, name + "_30", matrix.m30);
            DLSS_Parameter_SetF(pParams, name + "_31", matrix.m31);
            DLSS_Parameter_SetF(pParams, name + "_32", matrix.m32);
            DLSS_Parameter_SetF(pParams, name + "_33", matrix.m33);
        }

        public static NVSDK_NGX_Result DLSS_Parameter_GetUI(IntPtr pParams, string name, out uint value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetUI_Internal(pParams, name, out value);

        public static NVSDK_NGX_Result DLSS_Parameter_GetI(IntPtr pParams, string name, out int value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetI_Internal(pParams, name, out value);

        public static NVSDK_NGX_Result DLSS_Parameter_GetF(IntPtr pParams, string name, out float value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetF_Internal(pParams, name, out value);

        #endregion

        #region Internal Helpers

        private static RingBufferAllocator Allocator
        {
            get
            {
                if (s_allocator == null)
                {
                    s_allocator = new RingBufferAllocator(ALLOCATOR_SIZE);
                }
                return s_allocator;
            }
        }

        private static void QueryFeatureAvailability()
        {
            if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_GetCapabilityParameters_D3D12_Internal(out IntPtr capParams)))
            {
                // Query SuperSampling availability
                if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_Parameter_GetI_Internal(capParams, NVSDK_NGX_Parameter_SuperSampling_Available, out int ssAvailable)))
                {
                    s_superSamplingAvailable = ssAvailable != 0;
                }

                // Query RayReconstruction availability
                if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_Parameter_GetI_Internal(capParams, NVSDK_NGX_Parameter_SuperSamplingDenoising_Available, out int rrAvailable)))
                {
                    s_rayReconstructionAvailable = rrAvailable != 0;
                }

                DLSS_DestroyParameters_D3D12_Internal(capParams);
            }
        }

        private static void LogDlssResult(NVSDK_NGX_Result result, string functionName)
        {
            if (!NVSDK_NGX_SUCCEED(result))
            {
                string message = $"[DLSS] {functionName} failed: {result}";

                switch (result)
                {
                    case NVSDK_NGX_Result.NVSDK_NGX_Result_FAIL_FeatureNotSupported:
                        message += " - Feature not supported on current hardware";
                        break;
                    case NVSDK_NGX_Result.NVSDK_NGX_Result_FAIL_PlatformError:
                        message += " - Platform error";
                        break;
                    case NVSDK_NGX_Result.NVSDK_NGX_Result_FAIL_InvalidParameter:
                        message += " - Invalid parameter";
                        break;
                    case NVSDK_NGX_Result.NVSDK_NGX_Result_FAIL_NotInitialized:
                        message += " - SDK not initialized";
                        break;
                    case NVSDK_NGX_Result.NVSDK_NGX_Result_FAIL_OutOfGPUMemory:
                        message += " - Out of GPU memory";
                        break;
                }

                Debug.LogError(message);
            }
        }

        #endregion
    }
}
