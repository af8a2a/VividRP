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
            // Unreal keeps eye adaptation active for collapsed/invalid ranges
            // and asks the shader to force the target for that frame.
            return enabled.value;
        }

    }
}
