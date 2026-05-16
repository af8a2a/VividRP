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
