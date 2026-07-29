#if DLSS_PLUGIN_INTEGRATE

//------------------------------------------------------------------------------
// DLSSDef.cs - DLSS Type Definitions for VividRP
//------------------------------------------------------------------------------
// User-facing enums and types for DLSS configuration.
// These map to the low-level NGX types in DLSSExtension.cs
//------------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
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
    /// Per-frame exposure inputs consumed by DLSS-SR and DLSS-RR.
    /// </summary>
    /// <remarks>
    /// PreExposure is the multiplier that has already been applied to the input
    /// color. ExposureScale is NGX's additional exposure-scale parameter.
    /// ExposureTexture, when present, must be a created 1x1 2D texture whose
    /// first channel contains the final exposure scale.
    ///
    /// The AutoExposure feature flag is a feature-creation choice and is not
    /// represented here. When that flag is enabled, the exposure texture may be
    /// omitted while PreExposure must still describe the input color correctly.
    /// </remarks>
    public readonly struct DLSSExposure
    {
        public float PreExposure { get; }
        public float ExposureScale { get; }
        public RenderTexture ExposureTexture { get; }

        /// <summary>
        /// Unit exposure for an input color that has not been pre-exposed.
        /// </summary>
        public static DLSSExposure Identity => new DLSSExposure(1.0f, 1.0f, null);

        public DLSSExposure(
            float preExposure,
            float exposureScale,
            RenderTexture exposureTexture = null)
        {
            if (!IsPositiveFinite(preExposure))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(preExposure),
                    "Pre-exposure must be finite and greater than zero.");
            }

            if (!IsPositiveFinite(exposureScale))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exposureScale),
                    "Exposure scale must be finite and greater than zero.");
            }

            ValidateTextureShape(exposureTexture);

            PreExposure = preExposure;
            ExposureScale = exposureScale;
            ExposureTexture = exposureTexture;
        }

        internal bool TryValidate(out string error)
        {
            if (!IsPositiveFinite(PreExposure))
            {
                error = "Pre-exposure must be finite and greater than zero. " +
                    "Pass DLSSExposure.Identity when the input is not pre-exposed.";
                return false;
            }

            if (!IsPositiveFinite(ExposureScale))
            {
                error = "Exposure scale must be finite and greater than zero.";
                return false;
            }

            if (ExposureTexture != null)
            {
                if (ExposureTexture.width != 1 ||
                    ExposureTexture.height != 1 ||
                    ExposureTexture.volumeDepth != 1 ||
                    ExposureTexture.dimension != TextureDimension.Tex2D)
                {
                    error = "Exposure texture must be a 1x1, single-slice 2D RenderTexture.";
                    return false;
                }

                if (!ExposureTexture.IsCreated())
                {
                    error = "Exposure texture must be created before DLSS evaluation.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0.0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void ValidateTextureShape(RenderTexture texture)
        {
            if (texture == null)
                return;

            if (texture.width != 1 ||
                texture.height != 1 ||
                texture.volumeDepth != 1 ||
                texture.dimension != TextureDimension.Tex2D)
            {
                throw new ArgumentException(
                    "Exposure texture must be a 1x1, single-slice 2D RenderTexture.",
                    nameof(texture));
            }
        }
    }

    /// <summary>
    /// Jitter offset in the input/render pixel space required by NGX.
    /// </summary>
    public readonly struct DLSSJitterOffset
    {
        public Vector2 RenderPixels { get; }

        private DLSSJitterOffset(Vector2 renderPixels)
        {
            RenderPixels = renderPixels;
        }

        /// <summary>
        /// Creates an NGX jitter offset from a value that is already expressed in
        /// input/render pixels.
        /// </summary>
        public static DLSSJitterOffset FromRenderPixels(Vector2 renderPixels)
        {
            return new DLSSJitterOffset(renderPixels);
        }

        /// <summary>
        /// Converts the translation stored in a projection jitter matrix
        /// (m03/m13, in NDC) to NGX input/render pixels.
        ///
        /// The sign change converts the current-frame projection displacement to
        /// the current-to-previous convention required by NGX. The factor of 0.5
        /// converts the full [-1, 1] NDC span to pixels.
        /// </summary>
        public static DLSSJitterOffset FromProjectionNdc(
            Vector2 projectionOffsetNdc,
            int renderWidth,
            int renderHeight)
        {
            ValidateRenderSize(renderWidth, renderHeight);
            return new DLSSJitterOffset(new Vector2(
                -0.5f * projectionOffsetNdc.x * renderWidth,
                -0.5f * projectionOffsetNdc.y * renderHeight));
        }

        public static DLSSJitterOffset FromProjectionNdc(
            Vector2 projectionOffsetNdc,
            Vector2Int renderSize)
        {
            return FromProjectionNdc(projectionOffsetNdc, renderSize.x, renderSize.y);
        }

        private static void ValidateRenderSize(int renderWidth, int renderHeight)
        {
            if (renderWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(renderWidth), "Render width must be positive.");
            if (renderHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(renderHeight), "Render height must be positive.");
        }
    }

    /// <summary>
    /// Unit used by the values stored in a motion-vector texture.
    /// </summary>
    public enum DLSSMotionVectorSpace
    {
        /// <summary>The texture stores offsets measured in input/render pixels.</summary>
        RenderPixels = 0,
        /// <summary>The texture stores offsets measured in normalized [0, 1] UV space.</summary>
        NormalizedUV = 1
    }

    /// <summary>
    /// Direction represented by a motion vector.
    /// </summary>
    public enum DLSSMotionVectorDirection
    {
        /// <summary>
        /// The vector points from the current-frame pixel to its previous-frame
        /// position. This is the direction required by NGX.
        /// </summary>
        CurrentToPrevious = 0,
        /// <summary>
        /// The vector points from the previous-frame position to the current-frame
        /// pixel, so the wrapper must negate it for NGX.
        /// </summary>
        PreviousToCurrent = 1
    }

    /// <summary>
    /// Describes the units and direction encoded in a motion-vector texture.
    /// The DLSS wrappers convert this description to NGX's pixel-space scale.
    /// </summary>
    public readonly struct DLSSMotionVectorEncoding
    {
        public DLSSMotionVectorSpace Space { get; }
        public DLSSMotionVectorDirection Direction { get; }

        public DLSSMotionVectorEncoding(
            DLSSMotionVectorSpace space,
            DLSSMotionVectorDirection direction)
        {
            Space = space;
            Direction = direction;
        }

        /// <summary>
        /// VividRP's regular raster motion vectors:
        /// currentUV - previousUV, stored in normalized UV space.
        /// </summary>
        public static DLSSMotionVectorEncoding VividNormalizedUV =>
            new DLSSMotionVectorEncoding(
                DLSSMotionVectorSpace.NormalizedUV,
                DLSSMotionVectorDirection.PreviousToCurrent);

        /// <summary>
        /// VividRP's ray-tracing GBuffer motion vectors:
        /// (previousUV - currentUV) * renderSize, stored in pixels.
        /// </summary>
        public static DLSSMotionVectorEncoding VividRayTracingPixels =>
            new DLSSMotionVectorEncoding(
                DLSSMotionVectorSpace.RenderPixels,
                DLSSMotionVectorDirection.CurrentToPrevious);

        /// <summary>
        /// Returns the NGX MV.Scale value that converts the described texture
        /// values to current-to-previous input/render pixels.
        /// </summary>
        public Vector2 GetNGXPixelScale(int renderWidth, int renderHeight)
        {
            float scaleX;
            float scaleY;
            switch (Space)
            {
                case DLSSMotionVectorSpace.RenderPixels:
                    scaleX = 1.0f;
                    scaleY = 1.0f;
                    break;
                case DLSSMotionVectorSpace.NormalizedUV:
                    if (renderWidth <= 0)
                        throw new ArgumentOutOfRangeException(nameof(renderWidth), "Render width must be positive.");
                    if (renderHeight <= 0)
                        throw new ArgumentOutOfRangeException(nameof(renderHeight), "Render height must be positive.");
                    scaleX = renderWidth;
                    scaleY = renderHeight;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Space), Space, "Unknown motion-vector space.");
            }

            switch (Direction)
            {
                case DLSSMotionVectorDirection.CurrentToPrevious:
                    return new Vector2(scaleX, scaleY);
                case DLSSMotionVectorDirection.PreviousToCurrent:
                    return new Vector2(-scaleX, -scaleY);
                default:
                    throw new ArgumentOutOfRangeException(nameof(Direction), Direction, "Unknown motion-vector direction.");
            }
        }
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

    /// <summary>
    /// DLSS runtime constants and defaults used by VividRP.
    /// </summary>
    public static class DLSSConstants
    {
        public const float DEFAULT_DRS_SCALE_PERCENT = 66.7f;
        public const ulong CAMERA_STATE_EXPIRATION_FRAMES = 400;
        public const float MOTION_VECTOR_SCALE_SIGN = -1.0f;
    }

    public static class DLSSQualityExtensions
    {
        public static NVSDK_NGX_PerfQuality_Value ToNGXQuality(this DLSSQuality quality)
        {
            switch (quality)
            {
                case DLSSQuality.MaxQuality:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_MaxQuality;
                case DLSSQuality.Balanced:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced;
                case DLSSQuality.MaxPerformance:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_MaxPerf;
                case DLSSQuality.UltraPerformance:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_UltraPerformance;
                case DLSSQuality.UltraQuality:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_UltraQuality;
                case DLSSQuality.DLAA:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_DLAA;
                default:
                    return NVSDK_NGX_PerfQuality_Value.NVSDK_NGX_PerfQuality_Value_Balanced;
            }
        }
    }
}

#endif
