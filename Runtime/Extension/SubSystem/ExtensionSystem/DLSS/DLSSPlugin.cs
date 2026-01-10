//------------------------------------------------------------------------------
// DLSSPlugin.cs - C# P/Invoke Wrapper for Unity DLSS Native Plugin
//------------------------------------------------------------------------------
// Auto-generated wrapper matching DLSSPlugin.h API.
// Supports both DLSS-SR (Super Resolution) and DLSS-RR (Ray Reconstruction).
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DLSS
{
    //--------------------------------------------------------------------------
    // SECTION 1: Enumerations
    //--------------------------------------------------------------------------

    /// <summary>
    /// Result codes returned by plugin functions.
    /// </summary>
    public enum DLSSResult : int
    {
        Success = 0,
        Fail_NotInitialized = -1,
        Fail_FeatureNotSupported = -2,
        Fail_InvalidParameter = -3,
        Fail_OutOfMemory = -4,
        Fail_ContextNotFound = -5,
        Fail_ContextAlreadyExists = -6,
        Fail_DriverOutOfDate = -7,
        Fail_PlatformError = -8,
        Fail_NGXError = -9
    }

    /// <summary>
    /// DLSS operating mode - selects between Super Resolution and Ray Reconstruction.
    /// </summary>
    public enum DLSSMode : int
    {
        Off = 0,
        SuperResolution = 1,    // Standard DLSS-SR (upscaling + AA)
        RayReconstruction = 2   // DLSS-RR (ray tracing denoiser + upscaler)
    }

    /// <summary>
    /// Quality preset - affects resolution scaling factor.
    /// </summary>
    public enum DLSSQuality : int
    {
        MaxPerformance = 0,     // UltraPerformance equivalent
        Balanced = 1,
        MaxQuality = 2,
        UltraPerformance = 3,
        UltraQuality = 4,
        DLAA = 5                // No upscaling, AA only (1:1)
    }

    /// <summary>
    /// Render presets for DLSS-SR.
    /// </summary>
    public enum DLSSSRPreset : int
    {
        Default = 0,
        F = 6,      // Deprecated
        G = 7,      // Reverts to default
        J = 10,     // Less ghosting, more flickering
        K = 11,     // Best quality (transformer-based)
        L = 12,     // Default for Ultra Perf
        M = 13      // Default for Perf
    }

    /// <summary>
    /// Render presets for DLSS-RR (Ray Reconstruction).
    /// </summary>
    public enum DLSSRRPreset : int
    {
        Default = 0,
        D = 4,      // Default transformer model
        E = 5       // Latest transformer (required for DoF guide)
    }

    /// <summary>
    /// Feature flags for context creation.
    /// </summary>
    [Flags]
    public enum DLSSFeatureFlags : uint
    {
        None = 0,
        IsHDR = (1 << 0),               // Input is HDR (pre-tonemapped)
        MVLowRes = (1 << 1),            // Motion vectors are low-res
        MVJittered = (1 << 2),          // Motion vectors include jitter
        DepthInverted = (1 << 3),       // Reversed-Z depth buffer
        AutoExposure = (1 << 6),        // Use auto-exposure
        AlphaUpscaling = (1 << 7)       // Upscale alpha channel
    }

    /// <summary>
    /// Depth type for Ray Reconstruction.
    /// </summary>
    public enum DLSSDepthType : int
    {
        Linear = 0,     // Linear depth buffer
        Hardware = 1    // Hardware Z-buffer
    }

    /// <summary>
    /// Roughness packing mode for Ray Reconstruction.
    /// </summary>
    public enum DLSSRoughnessMode : int
    {
        Unpacked = 0,           // Roughness in separate texture
        PackedInNormalsW = 1    // Roughness in normals.w channel
    }

    /// <summary>
    /// Denoise mode for Ray Reconstruction.
    /// </summary>
    public enum DLSSDenoiseMode : int
    {
        Off = 0,
        DLUnified = 1   // DL-based unified upscaler (required for RR)
    }

    //--------------------------------------------------------------------------
    // SECTION 2: Parameter Structures
    //--------------------------------------------------------------------------

    /// <summary>
    /// Common resolution/dimension parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSDimensions
    {
        public uint width;
        public uint height;

        public DLSSDimensions(uint w, uint h)
        {
            width = w;
            height = h;
        }

        public DLSSDimensions(int w, int h)
        {
            width = (uint)w;
            height = (uint)h;
        }

        public static implicit operator Vector2Int(DLSSDimensions d) => new Vector2Int((int)d.width, (int)d.height);
        public static implicit operator DLSSDimensions(Vector2Int v) => new DLSSDimensions((uint)v.x, (uint)v.y);
    }

    /// <summary>
    /// Coordinates for subrect base (atlas support).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSCoordinates
    {
        public uint x;
        public uint y;

        public DLSSCoordinates(uint x, uint y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>
    /// 4x4 matrix (column-major, matches Unity/D3D convention).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSMatrix4x4
    {
        // Column-major: m[0-3]=col0, m[4-7]=col1, etc.
        public float m00, m10, m20, m30;  // Column 0
        public float m01, m11, m21, m31;  // Column 1
        public float m02, m12, m22, m32;  // Column 2
        public float m03, m13, m23, m33;  // Column 3

        public static implicit operator DLSSMatrix4x4(Matrix4x4 m)
        {
            return new DLSSMatrix4x4
            {
                m00 = m.m00, m10 = m.m10, m20 = m.m20, m30 = m.m30,
                m01 = m.m01, m11 = m.m11, m21 = m.m21, m31 = m.m31,
                m02 = m.m02, m12 = m.m12, m22 = m.m22, m32 = m.m32,
                m03 = m.m03, m13 = m.m13, m23 = m.m23, m33 = m.m33
            };
        }

        public static implicit operator Matrix4x4(DLSSMatrix4x4 m)
        {
            return new Matrix4x4(
                new Vector4(m.m00, m.m10, m.m20, m.m30),
                new Vector4(m.m01, m.m11, m.m21, m.m31),
                new Vector4(m.m02, m.m12, m.m22, m.m32),
                new Vector4(m.m03, m.m13, m.m23, m.m33)
            );
        }
    }

    //--------------------------------------------------------------------------
    // Context Creation Parameters
    //--------------------------------------------------------------------------

    /// <summary>
    /// Parameters for creating a DLSS context.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSContextCreateParams
    {
        // === Required for both SR and RR ===
        public DLSSMode mode;
        public DLSSQuality quality;
        public DLSSDimensions inputResolution;
        public DLSSDimensions outputResolution;
        public uint featureFlags;   // DLSSFeatureFlags bitmask

        // === SR-specific presets (one per quality level) ===
        public DLSSSRPreset presetDLAA;
        public DLSSSRPreset presetQuality;
        public DLSSSRPreset presetBalanced;
        public DLSSSRPreset presetPerformance;
        public DLSSSRPreset presetUltraPerformance;
        public DLSSSRPreset presetUltraQuality;

        // === RR-specific parameters ===
        public DLSSDenoiseMode denoiseMode;
        public DLSSDepthType depthType;
        public DLSSRoughnessMode roughnessMode;
        public DLSSRRPreset presetRR_DLAA;
        public DLSSRRPreset presetRR_Quality;
        public DLSSRRPreset presetRR_Balanced;
        public DLSSRRPreset presetRR_Performance;
        public DLSSRRPreset presetRR_UltraPerformance;
        public DLSSRRPreset presetRR_UltraQuality;

        // === Optional ===
        public byte enableOutputSubrects;

        /// <summary>
        /// Create default SR context parameters.
        /// </summary>
        public static DLSSContextCreateParams CreateSR(
            DLSSQuality quality,
            uint inputWidth, uint inputHeight,
            uint outputWidth, uint outputHeight,
            DLSSFeatureFlags flags = DLSSFeatureFlags.None)
        {
            return new DLSSContextCreateParams
            {
                mode = DLSSMode.SuperResolution,
                quality = quality,
                inputResolution = new DLSSDimensions(inputWidth, inputHeight),
                outputResolution = new DLSSDimensions(outputWidth, outputHeight),
                featureFlags = (uint)flags,
                presetDLAA = DLSSSRPreset.Default,
                presetQuality = DLSSSRPreset.Default,
                presetBalanced = DLSSSRPreset.Default,
                presetPerformance = DLSSSRPreset.Default,
                presetUltraPerformance = DLSSSRPreset.Default,
                presetUltraQuality = DLSSSRPreset.Default
            };
        }

        /// <summary>
        /// Create default RR context parameters.
        /// </summary>
        public static DLSSContextCreateParams CreateRR(
            DLSSQuality quality,
            uint inputWidth, uint inputHeight,
            uint outputWidth, uint outputHeight,
            DLSSFeatureFlags flags = DLSSFeatureFlags.None,
            DLSSDepthType depthType = DLSSDepthType.Hardware,
            DLSSRoughnessMode roughnessMode = DLSSRoughnessMode.Unpacked)
        {
            return new DLSSContextCreateParams
            {
                mode = DLSSMode.RayReconstruction,
                quality = quality,
                inputResolution = new DLSSDimensions(inputWidth, inputHeight),
                outputResolution = new DLSSDimensions(outputWidth, outputHeight),
                featureFlags = (uint)flags,
                denoiseMode = DLSSDenoiseMode.DLUnified,
                depthType = depthType,
                roughnessMode = roughnessMode,
                presetRR_DLAA = DLSSRRPreset.Default,
                presetRR_Quality = DLSSRRPreset.Default,
                presetRR_Balanced = DLSSRRPreset.Default,
                presetRR_Performance = DLSSRRPreset.Default,
                presetRR_UltraPerformance = DLSSRRPreset.Default,
                presetRR_UltraQuality = DLSSRRPreset.Default
            };
        }
    }

    //--------------------------------------------------------------------------
    // Execution Parameters - Common (SR and RR)
    //--------------------------------------------------------------------------

    /// <summary>
    /// Common texture inputs shared by SR and RR.
    /// All textures are native pointers (ID3D12Resource*).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSCommonTextures
    {
        public IntPtr colorInput;       // Required: Noisy/low-res color input
        public IntPtr colorOutput;      // Required: Upscaled output destination
        public IntPtr depth;            // Required: Depth buffer
        public IntPtr motionVectors;    // Required: Motion vectors (2D screen-space)
        public IntPtr exposureTexture;  // Optional: 1x1 exposure scale texture
        public IntPtr biasColorMask;    // Optional: Bias current color mask
        public IntPtr transparencyMask; // Optional: Reserved for future use
    }

    /// <summary>
    /// Common per-frame parameters for both SR and RR.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSCommonParams
    {
        // === Jitter (required) ===
        public float jitterOffsetX;
        public float jitterOffsetY;

        // === Motion vector scaling ===
        public float mvScaleX;
        public float mvScaleY;

        // === Rendering state ===
        public DLSSDimensions renderSubrectDimensions;
        public byte reset;

        // === Exposure (optional) ===
        public float preExposure;
        public float exposureScale;

        // === Indicator/debug (optional) ===
        public byte invertYAxis;
        public byte invertXAxis;

        // === Subrect offsets (for atlas rendering) ===
        public DLSSCoordinates colorSubrectBase;
        public DLSSCoordinates depthSubrectBase;
        public DLSSCoordinates mvSubrectBase;
        public DLSSCoordinates outputSubrectBase;
        public DLSSCoordinates biasColorSubrectBase;

        /// <summary>
        /// Create default common parameters.
        /// </summary>
        public static DLSSCommonParams Create(
            float jitterX, float jitterY,
            float mvScaleX, float mvScaleY,
            uint renderWidth, uint renderHeight,
            bool reset = false)
        {
            return new DLSSCommonParams
            {
                jitterOffsetX = jitterX,
                jitterOffsetY = jitterY,
                mvScaleX = mvScaleX,
                mvScaleY = mvScaleY,
                renderSubrectDimensions = new DLSSDimensions(renderWidth, renderHeight),
                reset = reset ? (byte)1 : (byte)0,
                preExposure = 1.0f,
                exposureScale = 1.0f
            };
        }
    }

    //--------------------------------------------------------------------------
    // Execution Parameters - Ray Reconstruction Specific
    //--------------------------------------------------------------------------

    /// <summary>
    /// GBuffer textures for Ray Reconstruction.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSRRGBufferTextures
    {
        public IntPtr diffuseAlbedo;    // Required: Diffuse albedo
        public IntPtr specularAlbedo;   // Required: Specular albedo
        public IntPtr normals;          // Required: World-space normals
        public IntPtr roughness;        // Optional if packed in normals.w
        public IntPtr emissive;         // Optional: Emissive channel
    }

    /// <summary>
    /// Ray direction and hit distance textures for DLSS-RR.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSRRRayTextures
    {
        // === Separate ray direction and hit distance (recommended) ===
        public IntPtr diffuseRayDirection;
        public IntPtr diffuseHitDistance;
        public IntPtr specularRayDirection;
        public IntPtr specularHitDistance;

        // === Combined direction+distance (alternative) ===
        public IntPtr diffuseRayDirectionHitDistance;
        public IntPtr specularRayDirectionHitDistance;
    }

    /// <summary>
    /// Optional textures for advanced RR features.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSRROptionalTextures
    {
        public IntPtr reflectedAlbedo;

        // Particle handling
        public IntPtr colorBeforeParticles;
        public IntPtr colorAfterParticles;

        // Transparency handling
        public IntPtr colorBeforeTransparency;
        public IntPtr colorAfterTransparency;

        // Fog handling
        public IntPtr colorBeforeFog;
        public IntPtr colorAfterFog;

        // Depth of Field (requires Preset E)
        public IntPtr depthOfFieldGuide;
        public IntPtr colorBeforeDepthOfField;
        public IntPtr colorAfterDepthOfField;

        // Subsurface scattering
        public IntPtr screenSpaceSubsurfaceScatteringGuide;
        public IntPtr colorBeforeScreenSpaceSubsurfaceScattering;
        public IntPtr colorAfterScreenSpaceSubsurfaceScattering;

        // Refraction
        public IntPtr screenSpaceRefractionGuide;
        public IntPtr colorBeforeScreenSpaceRefraction;
        public IntPtr colorAfterScreenSpaceRefraction;

        // Additional inputs
        public IntPtr motionVectorsReflections;
        public IntPtr transparencyLayer;
        public IntPtr transparencyLayerOpacity;
        public IntPtr transparencyLayerMvecs;
        public IntPtr disocclusionMask;

        // Alpha
        public IntPtr alpha;
        public IntPtr outputAlpha;
    }

    /// <summary>
    /// Ray Reconstruction specific parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSRRParams
    {
        public DLSSRRGBufferTextures gbuffer;
        public DLSSRRRayTextures rays;
        public DLSSRROptionalTextures optional;

        // === Matrices (required for RR) ===
        public DLSSMatrix4x4 worldToViewMatrix;
        public DLSSMatrix4x4 viewToClipMatrix;

        // === Timing (optional) ===
        public float frameTimeDeltaMs;
    }

    //--------------------------------------------------------------------------
    // Combined Execute Parameters
    //--------------------------------------------------------------------------

    /// <summary>
    /// Unified execution parameters structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSExecuteParams
    {
        public DLSSMode mode;
        public DLSSCommonTextures textures;
        public DLSSCommonParams common;
        public DLSSRRParams rrParams;   // Only used when mode == RayReconstruction

        /// <summary>
        /// Create SR execution parameters.
        /// </summary>
        public static DLSSExecuteParams CreateSR(
            IntPtr colorInput, IntPtr colorOutput,
            IntPtr depth, IntPtr motionVectors,
            float jitterX, float jitterY,
            float mvScaleX, float mvScaleY,
            uint renderWidth, uint renderHeight,
            bool reset = false,
            IntPtr exposureTexture = default,
            IntPtr biasColorMask = default)
        {
            return new DLSSExecuteParams
            {
                mode = DLSSMode.SuperResolution,
                textures = new DLSSCommonTextures
                {
                    colorInput = colorInput,
                    colorOutput = colorOutput,
                    depth = depth,
                    motionVectors = motionVectors,
                    exposureTexture = exposureTexture,
                    biasColorMask = biasColorMask
                },
                common = DLSSCommonParams.Create(jitterX, jitterY, mvScaleX, mvScaleY, renderWidth, renderHeight, reset)
            };
        }
    }

    //--------------------------------------------------------------------------
    // SECTION 3: Capability/Query Structures
    //--------------------------------------------------------------------------

    /// <summary>
    /// Information about DLSS feature availability.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSCapabilityInfo
    {
        public byte dlssSRAvailable;
        public byte dlssRRAvailable;
        public byte needsDriverUpdate;
        public uint minDriverVersionMajor;
        public uint minDriverVersionMinor;

        public bool IsSRAvailable => dlssSRAvailable != 0;
        public bool IsRRAvailable => dlssRRAvailable != 0;
        public bool NeedsDriverUpdate => needsDriverUpdate != 0;
    }

    /// <summary>
    /// Optimal settings for a given quality mode and output resolution.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSOptimalSettings
    {
        public uint optimalRenderWidth;
        public uint optimalRenderHeight;
        public uint minRenderWidth;
        public uint minRenderHeight;
        public uint maxRenderWidth;
        public uint maxRenderHeight;
        public float sharpness;

        public Vector2Int OptimalRenderSize => new Vector2Int((int)optimalRenderWidth, (int)optimalRenderHeight);
        public Vector2Int MinRenderSize => new Vector2Int((int)minRenderWidth, (int)minRenderHeight);
        public Vector2Int MaxRenderSize => new Vector2Int((int)maxRenderWidth, (int)maxRenderHeight);
    }

    /// <summary>
    /// Memory statistics for DLSS.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DLSSStats
    {
        public ulong vramAllocatedBytes;
        public uint optLevel;
        public byte isDevBranch;

        public bool IsDevBranch => isDevBranch != 0;
        public float VRAMAllocatedMB => vramAllocatedBytes / (1024f * 1024f);
    }

    //--------------------------------------------------------------------------
    // SECTION 4: Native Plugin Interface
    //--------------------------------------------------------------------------

    /// <summary>
    /// DLSS Native Plugin P/Invoke declarations.
    /// </summary>
    public static class DLSSNative
    {
        private const string DLL_NAME = "UnityPlugin";
        private const CallingConvention CALLING_CONVENTION = CallingConvention.StdCall;

        //--- Initialization/Shutdown ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_Initialize(
            ulong appId,
            [MarshalAs(UnmanagedType.LPStr)] string projectId,
            [MarshalAs(UnmanagedType.LPStr)] string engineVersion,
            [MarshalAs(UnmanagedType.LPWStr)] string logPath);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern void DLSS_Shutdown();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern byte DLSS_IsInitialized();

        //--- Capability Queries ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_GetCapabilities(out DLSSCapabilityInfo outInfo);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_GetOptimalSettings(
            DLSSMode mode,
            DLSSQuality quality,
            uint outputWidth,
            uint outputHeight,
            out DLSSOptimalSettings outSettings);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_GetStats(DLSSMode mode, out DLSSStats outStats);

        //--- Context Management ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_CreateContext(
            uint viewId,
            ref DLSSContextCreateParams createParams);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_DestroyContext(uint viewId);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern void DLSS_DestroyAllContexts();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern byte DLSS_HasContext(uint viewId);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_UpdateContext(
            uint viewId,
            ref DLSSContextCreateParams createParams);

        //--- Execution ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_Execute(
            uint viewId,
            ref DLSSExecuteParams executeParams);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern DLSSResult DLSS_ExecuteOnCommandList(
            uint viewId,
            IntPtr commandList,
            ref DLSSExecuteParams executeParams);

        //--- Unity Render Event Callback ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern IntPtr DLSS_GetRenderEventFunc();

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern void DLSS_SetCurrentView(uint viewId);

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern void DLSS_SetExecuteParams(ref DLSSExecuteParams executeParams);

        //--- Debug/Utility ---

        [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION)]
        public static extern int DLSS_GetLastNGXError();


        /// <summary>
        /// Render event ID for DLSS: 'DLSS' = 0x444C5353
        /// </summary>
        public const int DLSS_RENDER_EVENT_ID = 0x444C5353;
    }

    //--------------------------------------------------------------------------
    // SECTION 5: High-Level Managed Wrapper
    //--------------------------------------------------------------------------

    /// <summary>
    /// High-level managed wrapper for DLSS operations.
    /// Provides a more Unity-friendly API with automatic error handling.
    /// </summary>
    public static class DLSSManager
    {
        private static bool s_Initialized = false;

        /// <summary>
        /// Initialize DLSS with default settings.
        /// </summary>
        public static bool Initialize(ulong appId = 0, string projectId = null, string engineVersion = null, string logPath = null)
        {
            if (s_Initialized)
                return true;

            var result = DLSSNative.DLSS_Initialize(appId, projectId, engineVersion, logPath);
            s_Initialized = (result == DLSSResult.Success);

            if (!s_Initialized)
            {
                Debug.LogError($"[DLSS] Failed to initialize: {result.ToString()}");
            }

            return s_Initialized;
        }

        /// <summary>
        /// Shutdown DLSS and release all resources.
        /// </summary>
        public static void Shutdown()
        {
            if (!s_Initialized)
                return;

            DLSSNative.DLSS_Shutdown();
            s_Initialized = false;
        }

        /// <summary>
        /// Check if DLSS is initialized.
        /// </summary>
        public static bool IsInitialized => s_Initialized && DLSSNative.DLSS_IsInitialized() != 0;

        /// <summary>
        /// Get DLSS capabilities.
        /// </summary>
        public static bool TryGetCapabilities(out DLSSCapabilityInfo info)
        {
            var result = DLSSNative.DLSS_GetCapabilities(out info);
            if (result != DLSSResult.Success)
            {
                Debug.LogWarning($"[DLSS] Failed to get capabilities: {result.ToString()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get optimal render settings for a given output resolution.
        /// </summary>
        public static bool TryGetOptimalSettings(
            DLSSMode mode,
            DLSSQuality quality,
            uint outputWidth,
            uint outputHeight,
            out DLSSOptimalSettings settings)
        {
            var result = DLSSNative.DLSS_GetOptimalSettings(mode, quality, outputWidth, outputHeight, out settings);
            if (result != DLSSResult.Success)
            {
                Debug.LogWarning($"[DLSS] Failed to get optimal settings: {result.ToString()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Create a DLSS context for SR.
        /// </summary>
        public static bool CreateSRContext(
            uint viewId,
            DLSSQuality quality,
            uint inputWidth, uint inputHeight,
            uint outputWidth, uint outputHeight,
            DLSSFeatureFlags flags = DLSSFeatureFlags.None)
        {
            var createParams = DLSSContextCreateParams.CreateSR(
                quality, inputWidth, inputHeight, outputWidth, outputHeight, flags);

            var result = DLSSNative.DLSS_CreateContext(viewId, ref createParams);
            if (result != DLSSResult.Success)
            {
                Debug.LogError($"[DLSS] Failed to create SR context for view {viewId}: {result.ToString()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Create a DLSS context for RR.
        /// </summary>
        public static bool CreateRRContext(
            uint viewId,
            DLSSQuality quality,
            uint inputWidth, uint inputHeight,
            uint outputWidth, uint outputHeight,
            DLSSFeatureFlags flags = DLSSFeatureFlags.None,
            DLSSDepthType depthType = DLSSDepthType.Hardware,
            DLSSRoughnessMode roughnessMode = DLSSRoughnessMode.Unpacked)
        {
            var createParams = DLSSContextCreateParams.CreateRR(
                quality, inputWidth, inputHeight, outputWidth, outputHeight, flags, depthType, roughnessMode);

            var result = DLSSNative.DLSS_CreateContext(viewId, ref createParams);
            if (result != DLSSResult.Success)
            {
                Debug.LogError($"[DLSS] Failed to create RR context for view {viewId}: {result.ToString()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Destroy a DLSS context.
        /// </summary>
        public static void DestroyContext(uint viewId)
        {
            DLSSNative.DLSS_DestroyContext(viewId);
        }

        /// <summary>
        /// Destroy all DLSS contexts.
        /// </summary>
        public static void DestroyAllContexts()
        {
            DLSSNative.DLSS_DestroyAllContexts();
        }

        /// <summary>
        /// Check if a context exists.
        /// </summary>
        public static bool HasContext(uint viewId)
        {
            return DLSSNative.DLSS_HasContext(viewId) != 0;
        }

        /// <summary>
        /// Execute DLSS for a view.
        /// </summary>
        public static bool Execute(uint viewId, ref DLSSExecuteParams executeParams)
        {
            var result = DLSSNative.DLSS_Execute(viewId, ref executeParams);
            if (result != DLSSResult.Success)
            {
                Debug.LogError($"[DLSS] Failed to execute for view {viewId}: {result.ToString()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get the last NGX error code.
        /// </summary>
        public static int GetLastNGXError()
        {
            return DLSSNative.DLSS_GetLastNGXError();
        }
    }
}

#endif
