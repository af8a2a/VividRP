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
        [Tooltip("Base-2 exponent of the finest directional clipmap radius in world units. Coarser levels double in size until Max Distance is covered.")]
        public ClampedIntParameter virtualShadowMapFirstLevel = new(2, -4, 12);
        [Tooltip("Select receiver levels by screen-space texel density within the existing stable projections. Off preserves P4 coverage selection. Does not resize projections, physical pools or invalidate cached caster depth.")]
        public BoolParameter virtualShadowMapScreenDensity = new(false);
        [Tooltip("Target screen pixels per virtual texel before LOD bias. Smaller requests finer levels, limited by finest-level coverage and page residency. Uses geometric receiver-plane axis footprints, not the normal map.")]
        public ClampedFloatParameter virtualShadowMapTargetTexelPixels = new(1, 0.25f, 8);
        [Tooltip("Receiver quality only: -1 halves the target texel footprint (finer); +1 doubles it (coarser). Does not change First Level, virtual resolution or the page budget. Requires Screen Density.")]
        public ClampedFloatParameter virtualShadowMapResolutionLodBias = new(0, -4, 4);
        [Tooltip("Enable normalized 3x3 tent PCF for VSM. Off keeps the single-point hard-shadow reference; missing filter footprints fall back as a whole to a coarser level.")]
        public BoolParameter virtualShadowMapPCF = new(false);
        [Tooltip("Width of transitions to the next available level. Screen Density uses this fraction of a LOD step and the projection coverage border; legacy selection uses the selection radius. 0 disables blending.")]
        public ClampedFloatParameter virtualShadowMapTransition = new(0.2f, 0f, 0.5f);
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
