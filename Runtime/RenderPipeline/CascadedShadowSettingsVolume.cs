using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/Shadows/Cascaded Shadow Maps")]
    public sealed class CascadedShadowSettingsVolume : VolumeComponent
    {
        public const int MinShadowResolution = 512;
        public const int MaxShadowResolution = 4096;
        public const int DefaultShadowResolution = 2048;
        public const int DefaultCascadeCount = 4;
        public const float DefaultMaxShadowDistance = 150f;
        public const float DefaultDepthBias = 1.0f;
        public const float DefaultNormalBias = 1.0f;
        public const float DefaultCascadeBorder = 0.2f;

        public BoolParameter enableCSM = new(false);
        [Tooltip("Experimental directional-light virtual shadow map. Unity Renderer casters require a VSM-compatible ShadowCaster pass; incompatible content and unsupported platforms fail closed to CSM.")]
        public BoolParameter enableVirtualShadowMapPrototype = new(false);
        [Tooltip("Virtual shadow resolution per projection. 0 follows the light's CSM resolution; otherwise rounded up to 128 texels (clipmaps use at least 512). Does not resize the CSM atlas or physical page budget.")]
        public ClampedIntParameter virtualShadowMapResolution = new(0, 0, 16384);
        [Tooltip("Maximum resident physical pages shared by the static and dynamic shadow layers. Higher budgets retain more fine detail and use more GPU memory.")]
        public ClampedIntParameter virtualShadowMapPhysicalPageBudget = new(256, 128, 1024);
        // Retain serialized legacy fields; UE allocation never applies these quotas.
        [HideInInspector]
        public ClampedIntParameter virtualShadowMapPageUpdateBudget = new(0, 0, 1024);
        [HideInInspector]
        public ClampedIntParameter virtualShadowMapRasterVertexBudget = new(0, 0, 16777216);
        [Tooltip("Base-2 exponent of the finest directional clipmap radius in world units. Coarser levels double in size until Max Distance is covered.")]
        public ClampedIntParameter virtualShadowMapFirstLevel = new(2, -4, 12);
        [Tooltip("Shift intermediate clipmaps toward the non-jittered camera frustum while preserving page alignment and nested coverage. The nearest and farthest levels remain camera-centred.")]
        public BoolParameter virtualShadowMapViewCoverage = new(false);
        [HideInInspector, Tooltip("Legacy serialized value; UE distance selection and continuous texel dither replace this control.")]
        public BoolParameter virtualShadowMapScreenDensity = new(false);
        [Tooltip("UE pool-pressure LOD bias: target 85% capacity, fast reduction, recovery after 10 frames below the threshold, maximum +2 levels. Sampling keeps its desired level and falls back to resident parents.")]
        public BoolParameter virtualShadowMapPagePressure = new(true);
        [HideInInspector, Tooltip("Legacy serialized value; UE distance selection and continuous texel dither replace this control.")]
        public ClampedFloatParameter virtualShadowMapTargetTexelPixels = new(1, 0.25f, 8);
        [Tooltip("UE distance-based clipmap LOD bias. -1 requests finer levels; +1 requests coarser levels. Includes horizontal viewport/projection normalization; total bias is clamped to zero to preserve coverage. Does not change resident projection sizes.")]
        public ClampedFloatParameter virtualShadowMapResolutionLodBias = new(0, -4, 4);
        [Tooltip("Enable two-texel-wide area PCF for the hard-shadow reference. SMRT independently uses UE distance-scaled texel dither. Missing PCF footprints fall back as a whole to a coarser level.")]
        public BoolParameter virtualShadowMapPCF = new(false);
        [Tooltip("Experimental nine-comparison stratified disk filter with frame-varying samples. Requires VSM PCF; radius is one virtual texel. Intended for comparison with area PCF under temporal anti-aliasing.")]
        public BoolParameter virtualShadowMapStochasticFiltering = new(false);
        [Tooltip("Experimental directional SMRT contact-hardening soft shadows. Uses sin(half Angular Diameter), as UE's directional SourceRadius; zero angle preserves the PCF/hard reference. Uses UE-style fixed-step single-layer tracing and depth-history gap filling. Missing samples try coarser pages, then are skipped; thin or hidden occluders may be missed.")]
        public BoolParameter virtualShadowMapSMRT = new(false);
        [HideInInspector, Tooltip("Legacy value. UE STBN uses the frame index directly.")]
        public BoolParameter virtualShadowMapSMRTJointSampling = new(false);
        [Tooltip("Accumulate a short, depth/normal-validated shadow history with current-frame clamping. Requires Screen Space Denoise and SMRT; independent of camera anti-aliasing.")]
        public BoolParameter virtualShadowMapSMRTTemporalDenoise = new(true);
        [Tooltip("Use current-frame wave votes to stop after one ray in uniformly lit regions or at least two rays in uniformly shadowed regions. Mixed waves keep the full budget. Invalid ray samples are skipped. Independent of temporal denoising; may change penumbra noise.")]
        public BoolParameter virtualShadowMapSMRTAdaptiveRays = new(true);
        [Tooltip("Maximum rays per shadow estimate. Adaptive Rays can stop uniformly lit/shadowed waves early; disabling it uses the full count.")]
        public ClampedIntParameter virtualShadowMapSMRTRayCount = new(7, 0, 16);
        [Tooltip("UE fixed-step count for the whole ray. Traces from far to near with squared spacing, plus one sample at the receiver (at most N+1 positions). Coarse fallback can probe multiple page tables at each position.")]
        public ClampedIntParameter virtualShadowMapSMRTSamplesPerRay = new(8, 1, 32);
        [HideInInspector, Tooltip("Legacy world-length value. Replaced by SMRT Ray Length Scale times view distance.")]
        public ClampedFloatParameter virtualShadowMapSMRTMaxRayLength = new(10, 0.1f, 100);
        [Tooltip("UE directional ray length = this scale times distance from the view origin.")]
        public MinFloatParameter virtualShadowMapSMRTRayLengthScale = new(1.5f, 0);
        [Tooltip("UE maximum depth-history extrapolation slope. Zero selects the no-slope shader permutation.")]
        public MinFloatParameter virtualShadowMapSMRTExtrapolateMaxSlope = new(5, 0);
        [Tooltip("UE directional texel dither scale. Zero disables ray-origin dither.")]
        public MinFloatParameter virtualShadowMapSMRTTexelDitherScale = new(2, 0);
        [Tooltip("UE zero-based ray index at which all-hit waves may stop. First-ray all-miss exit is always allowed when adaptive rays are enabled.")]
        public ClampedIntParameter virtualShadowMapSMRTAdaptiveRayCount = new(1, 1, 16);
        [HideInInspector, Tooltip("Legacy serialized value; UE distance selection and continuous texel dither replace this control.")]
        public ClampedFloatParameter virtualShadowMapTransition = new(0.2f, 0f, 0.5f);
        [HideInInspector, Tooltip("Legacy serialized value; UE distance selection and continuous texel dither replace this control.")]
        public ClampedFloatParameter virtualShadowMapCoverageTransition = new(0.2f, 0f, 0.5f);
        public ClampedIntParameter cascadeCount = new(DefaultCascadeCount, 1, 4);
        public MinFloatParameter maxShadowDistance = new(DefaultMaxShadowDistance, 0.01f);
        public ClampedFloatParameter cascadeSplit1 = new(0.067f, 0f, 1f);
        public ClampedFloatParameter cascadeSplit2 = new(0.2f, 0f, 1f);
        public ClampedFloatParameter cascadeSplit3 = new(0.467f, 0f, 1f);
        public ClampedFloatParameter cascadeBorder1 = new(DefaultCascadeBorder, 0f, 1f);
        public ClampedFloatParameter cascadeBorder2 = new(DefaultCascadeBorder, 0f, 1f);
        public ClampedFloatParameter cascadeBorder3 = new(DefaultCascadeBorder, 0f, 1f);
        public ClampedFloatParameter cascadeBorder4 = new(DefaultCascadeBorder, 0f, 1f);
        public BoolParameter screenSpaceShadowDenoise = new(false);
        // Legacy serialized fields kept only to avoid breaking existing volume assets.
        [HideInInspector]
        public ClampedIntParameter shadowResolution = new(DefaultShadowResolution, MinShadowResolution, MaxShadowResolution);

        [HideInInspector]
        public ClampedFloatParameter depthBias = new(DefaultDepthBias, 0f, 10f);

        [HideInInspector]
        public ClampedFloatParameter normalBias = new(DefaultNormalBias, 0f, 10f);

        public Vector3 GetCascadeSplitRatios()
        {
            return new Vector3(
                cascadeSplit1.value,
                cascadeSplit2.value,
                cascadeSplit3.value);
        }

        public Vector4 GetCascadeBorderRatios()
        {
            int splitCount = cascadeCount.value;

            return new Vector4(
                InterCascadeToSqRangeBorder(cascadeBorder1.value, 0.0f, splitCount > 1 ? cascadeSplit1.value : 1.0f),
                InterCascadeToSqRangeBorder(cascadeBorder2.value, cascadeSplit1.value, splitCount > 2 ? cascadeSplit2.value : 1.0f),
                InterCascadeToSqRangeBorder(cascadeBorder3.value, cascadeSplit2.value, splitCount > 3 ? cascadeSplit3.value : 1.0f),
                InterCascadeToSqRangeBorder(cascadeBorder4.value, cascadeSplit3.value, 1.0f));
        }

        private static float InterCascadeToSqRangeBorder(float interCascadeBorder, float previousCascadeRelativeRange, float cascadeRelativeRange)
        {
            float rangeBorder = cascadeRelativeRange > 0.0f
                ? (cascadeRelativeRange - previousCascadeRelativeRange) * interCascadeBorder / cascadeRelativeRange
                : 0.0f;

            return 1.0f - (1.0f - rangeBorder) * (1.0f - rangeBorder);
        }

        public bool IsActive()
        {
            return active && enableCSM.value;
        }
    }
}
