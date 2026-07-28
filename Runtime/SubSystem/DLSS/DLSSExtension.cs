#if DLSS_PLUGIN_INTEGRATE

//------------------------------------------------------------------------------
// DLSSExtension.cs - Unified DLSS Extension for VividRP ExtensionSystem
//------------------------------------------------------------------------------
// Integrates NVIDIA DLSS (Deep Learning Super Sampling) into VividRP.
// Provides both high-level extension interface and low-level SDK bindings.
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    #region NGX Enums and Types

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
    /// State of an asynchronously created native DLSS feature.
    /// </summary>
    public enum DLSSFeatureStatus : int
    {
        Invalid = -1,
        Pending = 0,
        Ready = 1,
        Failed = 2
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

    #endregion

    /// <summary>
    /// Unified DLSS Extension for VividRP ExtensionSystem.
    /// Handles initialization, capability detection, and provides SDK bindings for NVIDIA DLSS.
    /// </summary>
    public sealed class DLSSExtension
    {
        #region Constants

        private const string DLL_NAME = "UnityDLSS";
        private const CallingConvention CALLING_CONVENTION = CallingConvention.Cdecl;

        public const int DLSS_INVALID_FEATURE_HANDLE = -1;

        // Event IDs for native plugin
        private const int EVENT_ID_CREATE_FEATURE = 0;
        private const int EVENT_ID_EVALUATE_SUPER_RESOLUTION = 1;
        private const int EVENT_ID_DESTROY_FEATURE = 2;
        private const int EVENT_ID_EVALUATE_RAY_RECONSTRUCTION = 3;

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

        // RR-specific parameters
        public const string NVSDK_NGX_Parameter_DLSS_Denoise_Mode = "DLSS.Denoise.Mode";
        public const string NVSDK_NGX_Parameter_DLSS_Depth_Type = "DLSS.Depth.Type";
        public const string NVSDK_NGX_Parameter_DLSS_Roughness_Mode = "DLSS.Roughness.Mode";

        // GBuffer parameters
        public const string NVSDK_NGX_Parameter_DiffuseAlbedo = "DiffuseAlbedo";
        public const string NVSDK_NGX_Parameter_SpecularAlbedo = "SpecularAlbedo";
        public const string NVSDK_NGX_Parameter_Normals = "Normals";
        public const string NVSDK_NGX_Parameter_Roughness = "Roughness";
        public const string NVSDK_NGX_Parameter_Emissive = "Emissive";

        // Ray data parameters
        public const string NVSDK_NGX_Parameter_DiffuseRayDirection = "DiffuseRayDirection";
        public const string NVSDK_NGX_Parameter_DiffuseHitDistance = "DiffuseHitDistance";
        public const string NVSDK_NGX_Parameter_SpecularRayDirection = "SpecularRayDirection";
        public const string NVSDK_NGX_Parameter_SpecularHitDistance = "SpecularHitDistance";
        public const string NVSDK_NGX_Parameter_DiffuseRayDirectionHitDistance = "DiffuseRayDirectionHitDistance";
        public const string NVSDK_NGX_Parameter_SpecularRayDirectionHitDistance = "SpecularRayDirectionHitDistance";

        // Matrix parameters
        public const string NVSDK_NGX_Parameter_WorldToViewMatrix = "WorldToViewMatrix";
        public const string NVSDK_NGX_Parameter_ViewToClipMatrix = "ViewToClipMatrix";

        #endregion

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

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct DLSSMatrix4x4
        {
            public float m00, m01, m02, m03;
            public float m10, m11, m12, m13;
            public float m20, m21, m22, m23;
            public float m30, m31, m32, m33;

            public static DLSSMatrix4x4 FromRowMajor(Matrix4x4 matrix)
            {
                return new DLSSMatrix4x4
                {
                    m00 = matrix.m00,
                    m01 = matrix.m01,
                    m02 = matrix.m02,
                    m03 = matrix.m03,
                    m10 = matrix.m10,
                    m11 = matrix.m11,
                    m12 = matrix.m12,
                    m13 = matrix.m13,
                    m20 = matrix.m20,
                    m21 = matrix.m21,
                    m22 = matrix.m22,
                    m23 = matrix.m23,
                    m30 = matrix.m30,
                    m31 = matrix.m31,
                    m32 = matrix.m32,
                    m33 = matrix.m33
                };
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct DLSSCommonEvaluateParams
        {
            public int handle;
            public IntPtr color;
            public IntPtr output;
            public IntPtr depth;
            public IntPtr motionVectors;
            public float jitterOffsetX;
            public float jitterOffsetY;
            public float motionVectorScaleX;
            public float motionVectorScaleY;
            public int reset;
            public uint renderSubrectWidth;
            public uint renderSubrectHeight;
            public float preExposure;
            public float exposureScale;
            public int invertXAxis;
            public int invertYAxis;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct DLSSSuperResolutionEvaluateParams
        {
            public DLSSCommonEvaluateParams common;
            public IntPtr exposureTexture;
            public IntPtr biasColorMask;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct DLSSRayReconstructionEvaluateParams
        {
            public DLSSCommonEvaluateParams common;
            public IntPtr exposureTexture;
            public IntPtr diffuseAlbedo;
            public IntPtr specularAlbedo;
            public IntPtr normals;
            public IntPtr roughness;
            public IntPtr emissive;
            public IntPtr diffuseRayDirection;
            public IntPtr diffuseHitDistance;
            public IntPtr diffuseRayDirectionHitDistance;
            public IntPtr specularRayDirection;
            public IntPtr specularHitDistance;
            public IntPtr specularRayDirectionHitDistance;
            public DLSSMatrix4x4 worldToView;
            public DLSSMatrix4x4 viewToClip;
            public float frameTimeDeltaMs;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DLSSDestroyFeatureParams
        {
            public int handle;
        }

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_Init_with_ProjectID_D3D12(ref DLSSInitParams initParams);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_Shutdown_D3D12();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_AllocateParameters_D3D12(out IntPtr ppOutParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_GetCapabilityParameters_D3D12(out IntPtr ppOutParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_DestroyParameters_D3D12(IntPtr pInParameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_AllocateFeatureHandle(IntPtr parameters);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern int DLSS_FreeFeatureHandle(int handle);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern DLSSFeatureStatus DLSS_GetFeatureHandleStatus(
            int handle,
            out int createResult);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern IntPtr DLSS_AllocateEventData(uint size);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern void DLSS_FreeEventData(IntPtr data);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern uint DLSS_GetEventDataSize(int eventId);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        private static extern IntPtr DLSS_UnityRenderEventFunc();

        // Parameter setters
        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetULL(IntPtr pParameters, string paramName, ulong value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetF(IntPtr pParameters, string paramName, float value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetD(IntPtr pParameters, string paramName, double value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetUI(IntPtr pParameters, string paramName, uint value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetI(IntPtr pParameters, string paramName, int value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetD3d12Resource(IntPtr pParameters, string paramName, IntPtr value);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern void DLSS_Parameter_SetVoidPointer(IntPtr pParameters, string paramName, IntPtr value);

        // Parameter getters
        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetULL(IntPtr pParameters, string paramName, out ulong pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetF(IntPtr pParameters, string paramName, out float pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetD(IntPtr pParameters, string paramName, out double pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetUI(IntPtr pParameters, string paramName, out uint pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetI(IntPtr pParameters, string paramName, out int pValue);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
        private static extern int DLSS_Parameter_GetVoidPointer(IntPtr pParameters, string paramName, out IntPtr ppValue);

        #endregion

        #region Instance Fields

        private bool m_Initialized = false;
        private bool m_InitializationAttempted = false;
        private bool m_SRSupported = false;
        private bool m_RRSupported = false;

        #endregion

        #region Singleton Instance

        private static DLSSExtension s_Instance;

        /// <summary>
        /// Get the lazily initialized singleton instance.
        /// </summary>
        public static DLSSExtension Instance
        {
            get
            {
                Initialize();
                return s_Instance;
            }
        }

        public static bool IsSuperResolutionSupported =>
            Initialize() && s_Instance.IsSRSupported;

        public static bool IsRayReconstructionSupported =>
            Initialize() && s_Instance.IsRRSupported;

        public static bool Initialize()
        {
            s_Instance ??= new DLSSExtension();
            if (!s_Instance.m_Initialized && !s_Instance.m_InitializationAttempted)
                s_Instance.Init();

            return s_Instance.Support();
        }

        public static void Shutdown()
        {
            if (s_Instance == null)
                return;

            s_Instance.ShutDown();
            s_Instance = null;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Check if DLSS is initialized.
        /// </summary>
        public bool IsInitialized => m_Initialized;

        /// <summary>
        /// Check if DLSS-SR (Super Resolution) is supported.
        /// </summary>
        public bool IsSRSupported => m_SRSupported;

        /// <summary>
        /// Check if DLSS-RR (Ray Reconstruction) is supported.
        /// </summary>
        public bool IsRRSupported => m_RRSupported;

        #endregion

        #region Lifecycle

        public void Init()
        {
#if DLSS_PLUGIN_INTEGRATE
            m_InitializationAttempted = true;
            Debug.Log("[DLSSExtension] Initializing DLSS...");
            Debug.Log($"[DLSSExtension] Graphics Device: {SystemInfo.graphicsDeviceName}");
            Debug.Log($"[DLSSExtension] Graphics Vendor: {SystemInfo.graphicsDeviceVendor}");

            // Check if NVIDIA GPU
            if (!SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia"))
            {
                Debug.Log("[DLSSExtension] Non-NVIDIA GPU detected. DLSS is not available.");
                m_Initialized = false;
                return;
            }

            // Check if D3D12
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Debug.LogWarning("[DLSSExtension] DLSS requires Direct3D12. Current API: " + SystemInfo.graphicsDeviceType);
                m_Initialized = false;
                return;
            }

            try
            {
                // Initialize DLSS native SDK
                var initParams = new DLSSInitParams
                {
                    projectId = "",
                    engineType = NVSDK_NGX_EngineType.NVSDK_NGX_ENGINE_TYPE_UNITY,
                    engineVersion = Application.version,
                    applicationDataPath = "",
                    loggingLevel = NVSDK_NGX_Logging_Level.NVSDK_NGX_LOGGING_LEVEL_OFF
                };

                var result = (NVSDK_NGX_Result)DLSS_Init_with_ProjectID_D3D12(ref initParams);
                m_Initialized = NVSDK_NGX_SUCCEED(result);

                if (!m_Initialized)
                {
                    Debug.LogWarning($"[DLSSExtension] DLSS initialization failed: {result}");
                    return;
                }

                // Query capabilities
                QueryFeatureAvailability();

                Debug.Log($"[DLSSExtension] DLSS-SR Available: {m_SRSupported}");
                Debug.Log($"[DLSSExtension] DLSS-RR Available: {m_RRSupported}");
                Debug.Log("[DLSSExtension] DLSS initialized successfully!");

                // Cache instance
                s_Instance = this;
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"[DLSSExtension] DLSS DLL not found: {e.Message}");
                Debug.LogWarning("[DLSSExtension] Make sure UnityDLSS.dll and nvngx_*.dll are imported as native plugins.");
                m_Initialized = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSSExtension] DLSS initialization error: {e.Message}");
                m_Initialized = false;
            }
#else
            Debug.Log("[DLSSExtension] DLSS plugin not integrated. Define DLSS_PLUGIN_INTEGRATE to enable.");
#endif
        }

        public bool Support()
        {
#if DLSS_PLUGIN_INTEGRATE
            // Consider DLSS supported if either SR or RR is available
            return m_Initialized && (m_SRSupported || m_RRSupported);
#else
            return false;
#endif
        }

        public bool ShutDown()
        {
#if DLSS_PLUGIN_INTEGRATE
            bool wasInitialized = m_Initialized;
            if (wasInitialized)
                DLSS_Shutdown_D3D12();

            m_Initialized = false;
            m_InitializationAttempted = false;
            m_SRSupported = false;
            m_RRSupported = false;
            s_Instance = null;
            if (wasInitialized)
            {
                Debug.Log("[DLSSExtension] DLSS shutdown complete.");
            }
            return wasInitialized;
#else
            return false;
#endif
        }

        #endregion

        #region Static Helper Methods

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

        #endregion

        #region SDK Methods

#if DLSS_PLUGIN_INTEGRATE

        /// <summary>
        /// Queue asynchronous creation of a DLSS feature.
        /// This method always consumes the supplied parameter object. The returned
        /// handle remains Pending until the render event completes.
        /// </summary>
        public int CreateFeature(CommandBuffer cmd, NVSDK_NGX_Feature feature, IntPtr parameters)
        {
            if (!m_Initialized || cmd == null || parameters == IntPtr.Zero)
            {
                Debug.LogError("[DLSSExtension] Cannot create feature: invalid state or arguments");
                if (parameters != IntPtr.Zero)
                {
                    DestroyParameters(parameters);
                }
                return DLSS_INVALID_FEATURE_HANDLE;
            }

            int handle = DLSS_AllocateFeatureHandle(parameters);
            if (handle == DLSS_INVALID_FEATURE_HANDLE)
            {
                Debug.LogError("[DLSSExtension] Failed to allocate feature handle");
                DestroyParameters(parameters);
                return DLSS_INVALID_FEATURE_HANDLE;
            }

            var createParams = new DLSSCreateFeatureParams
            {
                handle = handle,
                feature = feature,
                parameters = parameters
            };

            if (!IssuePluginEvent(
                    cmd,
                    EVENT_ID_CREATE_FEATURE,
                    createParams,
                    "CreateFeature"))
            {
                // The event was never queued, so releasing the proxy is safe and
                // also destroys the parameter object transferred above.
                DLSS_FreeFeatureHandle(handle);
                return DLSS_INVALID_FEATURE_HANDLE;
            }

            return handle;
        }

        /// <summary>
        /// Query whether an asynchronously created feature is pending, ready, or failed.
        /// </summary>
        public DLSSFeatureStatus GetFeatureStatus(
            int handle,
            out NVSDK_NGX_Result createResult)
        {
            var status = DLSS_GetFeatureHandleStatus(handle, out int nativeResult);
            createResult = (NVSDK_NGX_Result)nativeResult;
            return status;
        }

        /// <summary>
        /// Release a proxy handle that never acquired an NGX feature, including
        /// the native-owned parameter object.
        /// </summary>
        public bool ReleaseFeatureHandle(int handle)
        {
            return DLSS_FreeFeatureHandle(handle) == 0;
        }

        /// <summary>
        /// Queue an immutable DLSS Super Resolution evaluation snapshot.
        /// Jitter must already be in input/render pixels and motion-vector scale
        /// must convert the texture values to current-to-previous pixels.
        /// </summary>
        internal bool EvaluateSuperResolutionFeature(
            CommandBuffer cmd,
            int handle,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            float jitterRenderPixelX,
            float jitterRenderPixelY,
            float motionVectorToPixelScaleX,
            float motionVectorToPixelScaleY,
            bool reset,
            uint renderSubrectWidth,
            uint renderSubrectHeight,
            DLSSExposure exposure,
            RenderTexture biasColorMask)
        {
            var evaluateParams = new DLSSSuperResolutionEvaluateParams
            {
                common = CreateCommonEvaluateParams(
                    handle,
                    colorInput,
                    colorOutput,
                    depth,
                    motionVectors,
                    jitterRenderPixelX,
                    jitterRenderPixelY,
                    motionVectorToPixelScaleX,
                    motionVectorToPixelScaleY,
                    reset,
                    renderSubrectWidth,
                    renderSubrectHeight,
                    exposure.PreExposure,
                    exposure.ExposureScale),
                exposureTexture = GetNativeTexturePtr(exposure.ExposureTexture),
                biasColorMask = GetNativeTexturePtr(biasColorMask)
            };

            return IssuePluginEvent(
                cmd,
                EVENT_ID_EVALUATE_SUPER_RESOLUTION,
                evaluateParams,
                "EvaluateSuperResolution");
        }

        /// <summary>
        /// Queue an immutable DLSS Ray Reconstruction evaluation snapshot.
        /// Jitter must already be in input/render pixels and motion-vector scale
        /// must convert the texture values to current-to-previous pixels.
        /// </summary>
        internal bool EvaluateRayReconstructionFeature(
            CommandBuffer cmd,
            int handle,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSExposure exposure,
            RenderTexture diffuseAlbedo,
            RenderTexture specularAlbedo,
            RenderTexture normals,
            RenderTexture roughness,
            RenderTexture emissive,
            RenderTexture diffuseRayDirection,
            RenderTexture diffuseHitDistance,
            RenderTexture diffuseRayDirectionHitDistance,
            RenderTexture specularRayDirection,
            RenderTexture specularHitDistance,
            RenderTexture specularRayDirectionHitDistance,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            float jitterRenderPixelX,
            float jitterRenderPixelY,
            float motionVectorToPixelScaleX,
            float motionVectorToPixelScaleY,
            bool reset,
            uint renderSubrectWidth,
            uint renderSubrectHeight,
            float frameTimeDeltaMs)
        {
            var evaluateParams = new DLSSRayReconstructionEvaluateParams
            {
                common = CreateCommonEvaluateParams(
                    handle,
                    colorInput,
                    colorOutput,
                    depth,
                    motionVectors,
                    jitterRenderPixelX,
                    jitterRenderPixelY,
                    motionVectorToPixelScaleX,
                    motionVectorToPixelScaleY,
                    reset,
                    renderSubrectWidth,
                    renderSubrectHeight,
                    exposure.PreExposure,
                    exposure.ExposureScale),
                exposureTexture = GetNativeTexturePtr(exposure.ExposureTexture),
                diffuseAlbedo = GetNativeTexturePtr(diffuseAlbedo),
                specularAlbedo = GetNativeTexturePtr(specularAlbedo),
                normals = GetNativeTexturePtr(normals),
                roughness = GetNativeTexturePtr(roughness),
                emissive = GetNativeTexturePtr(emissive),
                diffuseRayDirection = GetNativeTexturePtr(diffuseRayDirection),
                diffuseHitDistance = GetNativeTexturePtr(diffuseHitDistance),
                diffuseRayDirectionHitDistance = GetNativeTexturePtr(diffuseRayDirectionHitDistance),
                specularRayDirection = GetNativeTexturePtr(specularRayDirection),
                specularHitDistance = GetNativeTexturePtr(specularHitDistance),
                specularRayDirectionHitDistance = GetNativeTexturePtr(specularRayDirectionHitDistance),
                worldToView = DLSSMatrix4x4.FromRowMajor(worldToView),
                viewToClip = DLSSMatrix4x4.FromRowMajor(viewToClip),
                frameTimeDeltaMs = frameTimeDeltaMs
            };

            return IssuePluginEvent(
                cmd,
                EVENT_ID_EVALUATE_RAY_RECONSTRUCTION,
                evaluateParams,
                "EvaluateRayReconstruction");
        }

        /// <summary>
        /// Destroy a DLSS feature via command buffer.
        /// </summary>
        public bool DestroyFeature(CommandBuffer cmd, int handle)
        {
            if (!m_Initialized)
            {
                Debug.LogError("[DLSSExtension] Cannot destroy feature: not initialized");
                return false;
            }

            var destroyParams = new DLSSDestroyFeatureParams
            {
                handle = handle
            };

            return IssuePluginEvent(
                cmd,
                EVENT_ID_DESTROY_FEATURE,
                destroyParams,
                "DestroyFeature");
        }

        private static DLSSCommonEvaluateParams CreateCommonEvaluateParams(
            int handle,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            float jitterRenderPixelX,
            float jitterRenderPixelY,
            float motionVectorToPixelScaleX,
            float motionVectorToPixelScaleY,
            bool reset,
            uint renderSubrectWidth,
            uint renderSubrectHeight,
            float preExposure,
            float exposureScale)
        {
            return new DLSSCommonEvaluateParams
            {
                handle = handle,
                color = GetNativeTexturePtr(colorInput),
                output = GetNativeTexturePtr(colorOutput),
                depth = GetNativeTexturePtr(depth),
                motionVectors = GetNativeTexturePtr(motionVectors),
                jitterOffsetX = jitterRenderPixelX,
                jitterOffsetY = jitterRenderPixelY,
                motionVectorScaleX = motionVectorToPixelScaleX,
                motionVectorScaleY = motionVectorToPixelScaleY,
                reset = reset ? 1 : 0,
                renderSubrectWidth = renderSubrectWidth,
                renderSubrectHeight = renderSubrectHeight,
                preExposure = preExposure,
                exposureScale = exposureScale,
                invertXAxis = 0,
                invertYAxis = 1
            };
        }

        private static IntPtr GetNativeTexturePtr(RenderTexture texture)
        {
            return texture != null ? texture.GetNativeTexturePtr() : IntPtr.Zero;
        }

        private bool IssuePluginEvent<T>(
            CommandBuffer cmd,
            int eventId,
            T eventData,
            string operation)
            where T : struct
        {
            if (!m_Initialized || cmd == null)
            {
                Debug.LogError($"[DLSSExtension] Cannot queue {operation}: invalid state or command buffer");
                return false;
            }

            IntPtr dataPointer = IntPtr.Zero;

            try
            {
                int dataSize = Marshal.SizeOf(eventData);
                uint nativeDataSize = DLSS_GetEventDataSize(eventId);
                if (nativeDataSize != (uint)dataSize)
                {
                    Debug.LogError(
                        $"[DLSSExtension] {operation} ABI mismatch: " +
                        $"managed={dataSize}, native={nativeDataSize}");
                    return false;
                }

                dataPointer = DLSS_AllocateEventData((uint)dataSize);
                if (dataPointer == IntPtr.Zero)
                {
                    Debug.LogError($"[DLSSExtension] Failed to allocate event data for {operation}");
                    return false;
                }

                Marshal.StructureToPtr(eventData, dataPointer, false);
                cmd.IssuePluginEventAndData(
                    DLSS_UnityRenderEventFunc(),
                    eventId,
                    dataPointer);
                return true;
            }
            catch (Exception exception)
            {
                if (dataPointer != IntPtr.Zero)
                {
                    try
                    {
                        DLSS_FreeEventData(dataPointer);
                    }
                    catch
                    {
                        // The plugin may be missing or ABI-incompatible; preserve
                        // the original queueing error for the caller.
                    }
                }
                Debug.LogError($"[DLSSExtension] Failed to queue {operation}: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Allocate NGX parameters.
        /// </summary>
        public NVSDK_NGX_Result AllocateParameters(out IntPtr ppOutParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_AllocateParameters_D3D12(out ppOutParameters);
            LogResult(result, "AllocateParameters");
            return result;
        }

        /// <summary>
        /// Get capability parameters.
        /// </summary>
        public NVSDK_NGX_Result GetCapabilityParameters(out IntPtr ppOutParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_GetCapabilityParameters_D3D12(out ppOutParameters);
            LogResult(result, "GetCapabilityParameters");
            return result;
        }

        /// <summary>
        /// Destroy NGX parameters.
        /// </summary>
        public NVSDK_NGX_Result DestroyParameters(IntPtr pInParameters)
        {
            var result = (NVSDK_NGX_Result)DLSS_DestroyParameters_D3D12(pInParameters);
            LogResult(result, "DestroyParameters");
            return result;
        }

        #region Parameter Setters

        public void SetParameterUI(IntPtr pParams, string name, uint value)
            => DLSS_Parameter_SetUI(pParams, name, value);

        public void SetParameterI(IntPtr pParams, string name, int value)
            => DLSS_Parameter_SetI(pParams, name, value);

        public void SetParameterF(IntPtr pParams, string name, float value)
            => DLSS_Parameter_SetF(pParams, name, value);

        public void SetParameterD3d12Resource(IntPtr pParams, string name, IntPtr resource)
            => DLSS_Parameter_SetD3d12Resource(pParams, name, resource);

        public void SetParameterRenderTexture(IntPtr pParams, string name, RenderTexture texture)
        {
            IntPtr ptr = texture != null ? texture.GetNativeTexturePtr() : IntPtr.Zero;
            DLSS_Parameter_SetD3d12Resource(pParams, name, ptr);
        }

        public void SetParameterVoidPointer(IntPtr pParams, string name, IntPtr value)
            => DLSS_Parameter_SetVoidPointer(pParams, name, value);

        #endregion

        #region Parameter Getters

        public NVSDK_NGX_Result GetParameterUI(IntPtr pParams, string name, out uint value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetUI(pParams, name, out value);

        public NVSDK_NGX_Result GetParameterI(IntPtr pParams, string name, out int value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetI(pParams, name, out value);

        public NVSDK_NGX_Result GetParameterF(IntPtr pParams, string name, out float value)
            => (NVSDK_NGX_Result)DLSS_Parameter_GetF(pParams, name, out value);

        #endregion

        #region Internal Helpers

        private void QueryFeatureAvailability()
        {
            if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_GetCapabilityParameters_D3D12(out IntPtr capParams)))
            {
                // Query SuperSampling availability
                if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_Parameter_GetI(capParams, NVSDK_NGX_Parameter_SuperSampling_Available, out int ssAvailable)))
                {
                    m_SRSupported = ssAvailable != 0;
                }

                // Query RayReconstruction availability
                if (NVSDK_NGX_SUCCEED((NVSDK_NGX_Result)DLSS_Parameter_GetI(capParams, NVSDK_NGX_Parameter_SuperSamplingDenoising_Available, out int rrAvailable)))
                {
                    m_RRSupported = rrAvailable != 0;
                }

                DLSS_DestroyParameters_D3D12(capParams);
            }
        }

        private void LogResult(NVSDK_NGX_Result result, string functionName)
        {
            if (!NVSDK_NGX_SUCCEED(result))
            {
                string message = $"[DLSSExtension] {functionName} failed: {result}";

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

#endif
        #endregion
    }
}

#endif
