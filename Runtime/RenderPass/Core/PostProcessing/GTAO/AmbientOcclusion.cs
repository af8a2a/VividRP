using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace VividRP.Runtime
{
    public enum AmbientOcclusionImplementation
    {
        [InspectorName("Intel XeGTAO")]
        GTAO,
        [InspectorName("FidelityFX CACAO")]
        FidelityFXCACAO
    }

    [Serializable]
    public sealed class AmbientOcclusionImplementationParameter : VolumeParameter<AmbientOcclusionImplementation>
    {
        public AmbientOcclusionImplementationParameter(
            AmbientOcclusionImplementation value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [MovedFrom(
        true,
        sourceNamespace: "VividRP.Runtime",
        sourceAssembly: "VividRP.Runtime",
        sourceClassName: "GTAO")]
    [VolumeComponentMenu("Post-processing/Ambient Occlusion")]
    public sealed class AmbientOcclusion : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Whether screen-space ambient occlusion is enabled.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Ambient-occlusion implementation. GTAO preserves the existing path; FidelityFX CACAO is the optional replacement.")]
        public AmbientOcclusionImplementationParameter implementation = new(AmbientOcclusionImplementation.GTAO);

        [Tooltip("Quality level. GTAO uses 0-3; FidelityFX CACAO uses 0-4, where 4 enables adaptive sampling.")]
        public ClampedIntParameter qualityLevel = new(2, 0, 4);

        [Tooltip("GTAO edge-aware denoise passes. 0 keeps the final resolve only.")]
        public ClampedIntParameter denoisePasses = new(1, 0, 3);

        [Tooltip("Ambient occlusion radius in view-space units.")]
        public ClampedFloatParameter radius = new(0.5f, 0.0f, 100.0f);

        [Tooltip("Controls the distance range where occlusion fades out.")]
        public ClampedFloatParameter falloffRange = new(0.615f, 0.0f, 1.0f);

        [Tooltip("Final visibility shaping power.")]
        public ClampedFloatParameter finalValuePower = new(2.2f, 0.5f, 5.0f);

        [Tooltip("Run CACAO at a reduced internal resolution and use its bilateral upsampler.")]
        public BoolParameter cacaoDownsampled = new(false);

        [Tooltip("CACAO linear effect-strength multiplier.")]
        public ClampedFloatParameter cacaoShadowMultiplier = new(1.0f, 0.0f, 5.0f);

        [Tooltip("CACAO effect-strength power.")]
        public ClampedFloatParameter cacaoShadowPower = new(1.5f, 0.5f, 5.0f);

        [Tooltip("CACAO maximum occlusion before filtering.")]
        public ClampedFloatParameter cacaoShadowClamp = new(0.98f, 0.0f, 1.0f);

        [Tooltip("CACAO horizon threshold used to reduce self-occlusion.")]
        public ClampedFloatParameter cacaoHorizonAngleThreshold = new(0.06f, 0.0f, 0.2f);

        [Tooltip("View-space distance where CACAO starts fading out.")]
        public MinFloatParameter cacaoFadeOutFrom = new(50.0f, 0.0f);

        [Tooltip("View-space distance where CACAO is fully faded out.")]
        public MinFloatParameter cacaoFadeOutTo = new(300.0f, 0.001f);

        [Tooltip("Adaptive sample-count limit used by CACAO quality level 4.")]
        public ClampedFloatParameter cacaoAdaptiveQualityLimit = new(0.45f, 0.0f, 1.0f);

        [Tooltip("Number of CACAO edge-sensitive blur passes.")]
        public ClampedIntParameter cacaoBlurPasses = new(2, 0, 8);

        [Tooltip("CACAO edge sharpness. 1 prevents bleeding across depth edges.")]
        public ClampedFloatParameter cacaoSharpness = new(0.98f, 0.0f, 1.0f);

        [Tooltip("Strength of CACAO high-frequency detail occlusion.")]
        public ClampedFloatParameter cacaoDetailShadowStrength = new(0.5f, 0.0f, 5.0f);

        [Tooltip("Gaussian sigma squared used by the CACAO bilateral upsampler.")]
        public MinFloatParameter cacaoBilateralSigmaSquared = new(5.0f, 0.0001f);

        [Tooltip("Depth-similarity sigma used by the CACAO bilateral upsampler.")]
        public MinFloatParameter cacaoBilateralSimilarityDistanceSigma = new(0.01f, 0.0001f);

        public bool IsActive()
        {
            return enabled.value && radius.value > 0.0001f;
        }
    }
}
