namespace UnityEngine.Rendering.Universal
{
    
    /// <summary>
    /// TAA quality level.
    /// </summary>
    public enum TAAQualityLevel
    {
        /// <summary>Low quality.</summary>
        Low = 0,

        /// <summary>Medium quality.</summary>
        Medium,

        /// <summary>High quality.</summary>
        High
    }

    /// <summary>
    /// TAA Sharpen mode.
    /// </summary>
    public enum TAASharpenMode
    {
        /// <summary>Low quality.</summary>
        LowQuality = 0,

        /// <summary>Sharpen with a separate pass after TAA.</summary>
        PostSharpen,

        /// <summary>Run a Contrast Adaptive Sharpening pass after TAA.</summary>
        ContrastAdaptiveSharpening
    }


    partial class UniversalAdditionalCameraData
    {
        /// <summary>Strength of the sharpening component of temporal anti-aliasing.</summary>
        [Range(0, 2)] public float taaSharpenStrength = 0.5f;

        /// <summary>Quality of the anti-aliasing when using TAA.</summary>
        public TAAQualityLevel TAAQuality = TAAQualityLevel.Medium;

        /// <summary>How is the sharpening run sharpening.</summary>
        public TAASharpenMode taaSharpenMode = TAASharpenMode.LowQuality;

        /// <summary>How much to reduce the ringing from the TAA post-process sharpening. Note that some ringing might be visually desirable and that any value different than 0 will incur into a small additional cost.</summary>
        [Range(0, 1)] public float taaRingingReduction = 0.0f;

        /// <summary>Strength of the sharpening of the history sampled for TAA.</summary>
        [Range(0, 1)] public float taaHistorySharpening = 0.35f;

        /// <summary>Drive the anti-flicker mechanism. With high values flickering might be reduced, but it can lead to more ghosting or disocclusion artifacts.</summary>
        [Range(0.0f, 1.0f)] public float taaAntiFlicker = 0.5f;

        /// <summary>Larger is this value, more likely history will be rejected when current and reprojected history motion vector differ by a substantial amount.
        /// Larger values can decrease ghosting but will also reintroduce aliasing on the aforementioned cases.</summary>
        [Range(0.0f, 1.0f)] public float taaMotionVectorRejection = 0.0f;

        /// <summary>When enabled, ringing artifacts (dark or strangely saturated edges) caused by history sharpening will be improved. This comes at a potential loss of sharpness upon motion.</summary>
        public bool taaAntiHistoryRinging = false;

        /// <summary> Determines how much the history buffer is blended together with current frame result. Higher values means more history contribution. </summary>
        [Range(0.6f, 0.95f)] public float taaBaseBlendFactor = 0.875f;

        /// <summary> Scale to apply to the jittering applied when TAA is enabled. </summary>
        [Range(0.1f, 1.0f)] public float taaJitterScale = 1.0f;
    }
}