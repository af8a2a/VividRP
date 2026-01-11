//------------------------------------------------------------------------------
// DLSSDef.cs - DLSS Type Definitions for VividRP
//------------------------------------------------------------------------------
// User-facing enums and types for DLSS configuration.
// These map to the low-level NGX types in DLSSExtension.cs
//------------------------------------------------------------------------------

using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Quality presets for user configuration.
    /// Maps to NVSDK_NGX_PerfQuality_Value.
    /// </summary>
    public enum DLSSQuality
    {
        /// <summary>Maximum performance mode - highest upscaling ratio, lowest quality</summary>
        MaxPerformance = 0,
        /// <summary>Balanced mode - good balance between performance and quality</summary>
        Balanced = 1,
        /// <summary>Maximum quality mode - lowest upscaling ratio, highest quality</summary>
        MaxQuality = 2,
        /// <summary>Ultra performance mode - extreme upscaling for very high framerates</summary>
        UltraPerformance = 3,
        /// <summary>Ultra quality mode - minimal upscaling for best quality</summary>
        UltraQuality = 4,
        /// <summary>DLAA mode - no upscaling, only anti-aliasing at native resolution</summary>
        DLAA = 5
    }

    /// <summary>
    /// Volume parameter for DLSSQuality enum.
    /// </summary>
    [Serializable]
    public sealed class DLSSQualityParameter : VolumeParameter<DLSSQuality>
    {
        public DLSSQualityParameter(DLSSQuality value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    /// <summary>
    /// DLSS operating mode.
    /// </summary>
    public enum DLSSMode
    {
        /// <summary>DLSS disabled</summary>
        Off = 0,
        /// <summary>Super Resolution - temporal upscaling</summary>
        SuperResolution = 1,
        /// <summary>Ray Reconstruction - ray tracing denoiser with upscaling</summary>
        RayReconstruction = 2
    }

    /// <summary>
    /// DLSS-SR render presets for fine-tuning temporal behavior.
    /// </summary>
    public enum DLSSSRPreset : uint
    {
        /// <summary>Default preset - auto-selected by DLSS</summary>
        Default = 0,
        /// <summary>Preset A</summary>
        A = 1,
        /// <summary>Preset B</summary>
        B = 2,
        /// <summary>Preset C</summary>
        C = 3,
        /// <summary>Preset D</summary>
        D = 4,
        /// <summary>Preset E</summary>
        E = 5,
        /// <summary>Preset F - good for fast motion</summary>
        F = 6,
        /// <summary>Preset G</summary>
        G = 7,
        /// <summary>Preset H</summary>
        H = 8,
        /// <summary>Preset I</summary>
        I = 9,
        /// <summary>Preset J - reduced ghosting, more flickering</summary>
        J = 10,
        /// <summary>Preset K - transformer-based, best quality</summary>
        K = 11,
        /// <summary>Preset L - default for Ultra Performance</summary>
        L = 12,
        /// <summary>Preset M - default for Performance</summary>
        M = 13
    }

    /// <summary>
    /// DLSS-RR render presets.
    /// </summary>
    public enum DLSSRRPreset : uint
    {
        /// <summary>Default preset</summary>
        Default = 0,
        /// <summary>Preset D</summary>
        D = 4,
        /// <summary>Preset E</summary>
        E = 5
    }

    /// <summary>
    /// DLSS feature creation flags for user configuration.
    /// </summary>
    [Flags]
    public enum DLSSFeatureFlags
    {
        /// <summary>No special flags</summary>
        None = 0,
        /// <summary>Input is HDR (pre-tonemapped)</summary>
        IsHDR = (1 << 0),
        /// <summary>Motion vectors are at render resolution (not display resolution)</summary>
        MVLowRes = (1 << 1),
        /// <summary>Motion vectors already have jitter applied</summary>
        MVJittered = (1 << 2),
        /// <summary>Depth buffer uses reversed-Z (Unity default)</summary>
        DepthInverted = (1 << 3),
        /// <summary>Enable sharpening pass</summary>
        DoSharpening = (1 << 5),
        /// <summary>Enable auto-exposure handling</summary>
        AutoExposure = (1 << 6),
        /// <summary>Enable alpha channel upscaling</summary>
        AlphaUpscaling = (1 << 7)
    }

    /// <summary>
    /// Resolution dimensions for DLSS.
    /// </summary>
    [Serializable]
    public struct DLSSDimensions
    {
        public uint width;
        public uint height;

        public DLSSDimensions(uint width, uint height)
        {
            this.width = width;
            this.height = height;
        }

        public DLSSDimensions(int width, int height)
        {
            this.width = (uint)width;
            this.height = (uint)height;
        }

        public Vector2Int ToVector2Int() => new Vector2Int((int)width, (int)height);
    }

    /// <summary>
    /// DLSS depth buffer type for Ray Reconstruction.
    /// </summary>
    public enum DLSSDepthType
    {
        /// <summary>Linear depth (view-space Z)</summary>
        Linear = 0,
        /// <summary>Hardware depth (projection-space Z)</summary>
        Hardware = 1
    }

    /// <summary>
    /// DLSS roughness packing mode for Ray Reconstruction.
    /// </summary>
    public enum DLSSRoughnessMode
    {
        /// <summary>Roughness in separate texture</summary>
        Unpacked = 0,
        /// <summary>Roughness packed in normals.w channel</summary>
        PackedInNormalsW = 1
    }
}
