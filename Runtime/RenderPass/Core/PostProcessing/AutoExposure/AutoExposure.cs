using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum AutoExposureMode
    {
        Histogram,
        Manual,
    }

    public enum AutoExposureExposureMode
    {
        Automatic,
        AutomaticHistogram,
        CurveMapping,
        Fixed,
        UsePhysicalCamera,
    }

    public enum AutoExposureMeteringMode
    {
        Average,
        Spot,
        CenterWeighted,
        MaskWeighted,
        ProceduralMask,
    }

    public enum AutoExposureAdaptationMode
    {
        Fixed,
        Progressive,
    }

    [Serializable]
    public sealed class AutoExposureModeParameter : VolumeParameter<AutoExposureMode>
    {
        public AutoExposureModeParameter(AutoExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class AutoExposureExposureModeParameter : VolumeParameter<AutoExposureExposureMode>
    {
        public AutoExposureExposureModeParameter(AutoExposureExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class AutoExposureMeteringModeParameter : VolumeParameter<AutoExposureMeteringMode>
    {
        public AutoExposureMeteringModeParameter(AutoExposureMeteringMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class AutoExposureAdaptationModeParameter : VolumeParameter<AutoExposureAdaptationMode>
    {
        public AutoExposureAdaptationModeParameter(AutoExposureAdaptationMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

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
    
    
    /// <summary>
    /// The target grey value used by the exposure system. Note this is equivalent of changing the calibration constant K on the used virtual reflected light meter.
    /// </summary>
    public enum TargetMidGray
    {
        /// <summary>
        /// Mid Grey 12.5% (reflected light meter K set as 12.5)
        /// </summary>
        [InspectorName("Grey 12.5%")]Grey125,

        /// <summary>
        /// Mid Grey 14.0% (reflected light meter K set as 14.0)
        /// </summary>
        [InspectorName("Grey 14.0%")]Grey14,

        /// <summary>
        /// Mid Grey 18.0% (reflected light meter K set as 18.0). Note that this value is outside of the suggested K range by the ISO standard.
        /// </summary>
        [InspectorName("Grey 18.0%")]Grey18
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
            MigrateLegacyHistogramLogRangeIfNeeded();
            SyncLegacyHistogramLogRangeFields();

            base.OnEnable();
        }

        private void OnValidate()
        {
            EnsureUnrealParameters();
            EnsureHDRPParameters();
            MigrateSharedHDRPSettingsIfNeeded();
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

    public static class AutoExposureExposureModeUtility
    {
        public static bool UsesManualSettings(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.Fixed
                || mode == AutoExposureExposureMode.UsePhysicalCamera;
        }

        public static bool UsesPhysicalCamera(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.UsePhysicalCamera;
        }

        public static bool UsesHistogramSettings(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.AutomaticHistogram;
        }

        public static bool UsesCurveRemapping(AutoExposureExposureMode mode)
        {
            return mode == AutoExposureExposureMode.CurveMapping;
        }

        public static AutoExposureMode ResolveRuntimeMode(AutoExposureExposureMode mode)
        {
            return UsesManualSettings(mode)
                ? AutoExposureMode.Manual
                : AutoExposureMode.Histogram;
        }
    }
}
