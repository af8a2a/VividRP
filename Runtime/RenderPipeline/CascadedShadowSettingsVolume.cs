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
        [Tooltip("Base-2 exponent of the finest directional clipmap radius in world units. Coarser levels double in size until Max Distance is covered.")]
        public ClampedIntParameter virtualShadowMapFirstLevel = new(2, -4, 12);
        [Tooltip("Shift intermediate clipmaps toward the non-jittered camera frustum while preserving page alignment and nested coverage. The nearest and farthest levels remain camera-centred.")]
        public BoolParameter virtualShadowMapViewCoverage = new(false);
        [Tooltip("Select receiver levels by screen-space texel density within the existing stable projections. Off preserves P4 coverage selection. Does not resize projections, physical pools or invalidate cached caster depth.")]
        public BoolParameter virtualShadowMapScreenDensity = new(false);
        [Tooltip("Target screen pixels per virtual texel before LOD bias. Smaller requests finer levels, limited by finest-level coverage and page residency. Uses geometric receiver-plane axis footprints, not the normal map.")]
        public ClampedFloatParameter virtualShadowMapTargetTexelPixels = new(1, 0.25f, 8);
        [Tooltip("Receiver quality only: -1 halves the target texel footprint (finer); +1 doubles it (coarser). Does not change First Level, virtual resolution or the page budget. Requires Screen Density.")]
        public ClampedFloatParameter virtualShadowMapResolutionLodBias = new(0, -4, 4);
        [Tooltip("Enable VSM filtering, using two-texel-wide area PCF with up to nine comparisons by default. With SMRT, integrate this footprint at ray origins to preserve contact anti-aliasing. Off keeps the single-point hard-shadow reference; missing filter footprints fall back as a whole to a coarser level.")]
        public BoolParameter virtualShadowMapPCF = new(false);
        [Tooltip("Experimental nine-comparison stratified disk filter with frame-varying samples. Requires VSM PCF; radius is one virtual texel. Intended for comparison with area PCF under temporal anti-aliasing.")]
        public BoolParameter virtualShadowMapStochasticFiltering = new(false);
        [Tooltip("Experimental directional SMRT contact-hardening soft shadows. Uses the light's Angular Diameter (clamped to 10 degrees for SMRT); zero angle preserves the PCF/hard reference. Incomplete footprints retry coarser levels, then the reference filter.")]
        public BoolParameter virtualShadowMapSMRT = new(false);
        [Tooltip("Distribute SMRT samples across TSR jitter cycles to reduce persistent shadow grain. May slightly increase temporal noise. Has no effect without active TSR.")]
        public BoolParameter virtualShadowMapSMRTJointSampling = new(false);
        [Tooltip("Shadow rays per pixel. More rays reduce temporal noise; requires temporal anti-aliasing.")]
        public ClampedIntParameter virtualShadowMapSMRTRayCount = new(4, 4, 8);
        [Tooltip("Depth-cell budget per intermediate clipmap segment. Longer rays continue through coarser levels along the same direction. The coarsest map visits enough cells to finish the configured world length, so total reads can exceed this value. More samples retain fine detail farther from the receiver.")]
        public ClampedIntParameter virtualShadowMapSMRTSamplesPerRay = new(8, 4, 8);
        [Tooltip("Maximum distance in world units over which rays diverge. Fine-to-coarse clipmap continuation preserves this distance independently of texel size and segment budget. Beyond this explicit limit rays continue parallel to the light; incomplete map coverage or residency retries the reference filter.")]
        public ClampedFloatParameter virtualShadowMapSMRTMaxRayLength = new(10, 0.1f, 100);
        [Tooltip("Width of transitions across fractional LOD steps with Screen Density, or selection radii with legacy selection. 0 disables this component of blending.")]
        public ClampedFloatParameter virtualShadowMapTransition = new(0.2f, 0f, 0.5f);
        [Tooltip("Width of the projection-edge transition with Screen Density. When not overridden, follows the LOD transition; smaller values retain fine detail closer to the coverage edge. 0 disables this component of blending.")]
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
