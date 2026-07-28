using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    public sealed class NoInterpAnimationCurveParameter : VolumeParameter<AnimationCurve>
    {
        public NoInterpAnimationCurveParameter(AnimationCurve value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(AnimationCurve from, AnimationCurve to, float t)
        {
            value = t > 0f ? to : from;
        }
    }
    [Serializable]
    [VolumeComponentMenu("Post-processing/Exposure")]
    public sealed partial class AutoExposure : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Enables automatic exposure metering.")]
        public BoolParameter enabled = new(false);

        protected override void OnEnable()
        {
            EnsureUnrealParameters();
            EnsureHDRPParameters();
            MigrateSharedHDRPSettingsIfNeeded();
            MigrateLegacyUnrealMeteringMaskIfNeeded();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyHistogramLogRangeFields();

            base.OnEnable();
        }

        private void OnValidate()
        {
            EnsureUnrealParameters();
            EnsureHDRPParameters();
            MigrateSharedHDRPSettingsIfNeeded();
            MigrateLegacyUnrealMeteringMaskIfNeeded();
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyHistogramLogRangeFields();
        }

        public bool IsActive()
        {
            return IsActive(AutoExposureImplementationUtility.ResolveImplementation(
                VividRenderPipelineAsset.GetActiveAsset()));
        }

        internal bool IsActive(AutoExposureImplementationPath implementation)
        {
            return implementation == AutoExposureImplementationPath.HDRP
                ? IsHDRPActive()
                : IsUnrealActive();
        }

        internal bool IsUnrealActive()
        {
            if (!enabled.value)
                return false;

            if (mode.value == AutoExposureMode.Manual)
                return true;

            var minWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(minEV100.value);
            var maxWhitePointLuminance = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(maxEV100.value);

            return maxWhitePointLuminance >= minWhitePointLuminance
                && speedUp.value > 0f
                && speedDown.value > 0f;
        }

    }
}
