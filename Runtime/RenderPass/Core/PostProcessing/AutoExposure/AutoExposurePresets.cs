using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum AutoExposureCommonPreset
    {
        HistogramBalanced,
        HistogramInteriorExterior,
        HistogramLowLightCurve,
        ManualNeutral,
        ManualCurveBias,
        ManualPhysicalCamera,
    }

    public readonly struct AutoExposurePresetDefinition
    {
        private readonly Func<AnimationCurve> m_CurveFactory;

        internal AutoExposurePresetDefinition(
            AutoExposureCommonPreset id,
            string name,
            string description,
            AutoExposureMode mode,
            Vector2 percent,
            float minEV100,
            float maxEV100,
            Vector2 histogramLogRangeEV100,
            float speedUp,
            float speedDown,
            float manualEV100,
            bool applyPhysicalCameraExposure,
            float exposureCompensation,
            Func<AnimationCurve> curveFactory)
        {
            Id = id;
            Name = name;
            Description = description;
            Mode = mode;
            Percent = percent;
            MinEV100 = minEV100;
            MaxEV100 = maxEV100;
            HistogramLogRangeEV100 = histogramLogRangeEV100;
            SpeedUp = speedUp;
            SpeedDown = speedDown;
            ManualEV100 = manualEV100;
            ApplyPhysicalCameraExposure = applyPhysicalCameraExposure;
            ExposureCompensation = exposureCompensation;
            m_CurveFactory = curveFactory ?? AutoExposureCommonPresets.CreateNeutralCurve;
        }

        public AutoExposureCommonPreset Id { get; }

        public string Name { get; }

        public string Description { get; }

        public AutoExposureMode Mode { get; }

        public Vector2 Percent { get; }

        public float MinEV100 { get; }

        public float MaxEV100 { get; }

        public Vector2 HistogramLogRangeEV100 { get; }

        public float SpeedUp { get; }

        public float SpeedDown { get; }

        public float ManualEV100 { get; }

        public bool ApplyPhysicalCameraExposure { get; }

        public float ExposureCompensation { get; }

        public AnimationCurve CreateExposureCompensationCurve()
        {
            return m_CurveFactory();
        }

        public AutoExposure CreateVolumeComponent()
        {
            var component = ScriptableObject.CreateInstance<AutoExposure>();
            ApplyTo(component);
            return component;
        }

        public void ApplyTo(AutoExposure component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            component.enabled.overrideState = true;
            component.enabled.value = true;
            component.exposureMode.overrideState = true;
            component.exposureMode.value = ResolveExposureMode();
            component.mode.overrideState = true;
            component.mode.value = Mode;
            component.percent.overrideState = true;
            component.percent.value = Percent;
            component.minEV100.overrideState = true;
            component.minEV100.value = MinEV100;
            component.maxEV100.overrideState = true;
            component.maxEV100.value = MaxEV100;
            component.speedUp.overrideState = true;
            component.speedUp.value = SpeedUp;
            component.speedDown.overrideState = true;
            component.speedDown.value = SpeedDown;
            component.manualEV100.overrideState = true;
            component.manualEV100.value = ManualEV100;
            component.applyPhysicalCameraExposure.overrideState = true;
            component.applyPhysicalCameraExposure.value = ApplyPhysicalCameraExposure;
            component.exposureCompensation.overrideState = true;
            component.exposureCompensation.value = ExposureCompensation;
            component.exposureCompensationCurve.overrideState = true;
            component.exposureCompensationCurve.value = CreateExposureCompensationCurve();
            component.histogramLogRange.overrideState = true;
            component.histogramLogRange.value = HistogramLogRangeEV100;
        }

        public override string ToString()
        {
            return Name;
        }

        private AutoExposureExposureMode ResolveExposureMode()
        {
            if (Mode == AutoExposureMode.Manual)
                return ApplyPhysicalCameraExposure ? AutoExposureExposureMode.UsePhysicalCamera : AutoExposureExposureMode.Fixed;

            return AutoExposureExposureMode.AutomaticHistogram;
        }
    }

    public static class AutoExposureCommonPresets
    {
        public const float VolumeSafeMinEV100 = -5f;
        public const float VolumeSafeMaxEV100 = 15f;

        public static IEnumerable<AutoExposurePresetDefinition> All
        {
            get
            {
                foreach (var preset in Histogram)
                    yield return preset;

                foreach (var preset in Manual)
                    yield return preset;
            }
        }

        public static IEnumerable<AutoExposurePresetDefinition> Histogram
        {
            get
            {
                yield return Get(AutoExposureCommonPreset.HistogramBalanced);
                yield return Get(AutoExposureCommonPreset.HistogramInteriorExterior);
                yield return Get(AutoExposureCommonPreset.HistogramLowLightCurve);
            }
        }

        public static IEnumerable<AutoExposurePresetDefinition> Manual
        {
            get
            {
                yield return Get(AutoExposureCommonPreset.ManualNeutral);
                yield return Get(AutoExposureCommonPreset.ManualCurveBias);
                yield return Get(AutoExposureCommonPreset.ManualPhysicalCamera);
            }
        }

        public static AutoExposurePresetDefinition Get(AutoExposureCommonPreset preset)
        {
            switch (preset)
            {
                case AutoExposureCommonPreset.HistogramBalanced:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Histogram Balanced",
                        "UE-style 10-90 histogram baseline with a +1 EV HDRI-friendly bias.",
                        AutoExposureMode.Histogram,
                        new Vector2(10f, 90f),
                        VolumeSafeMinEV100,
                        1f,
                        new Vector2(-10f, 6f),
                        3f,
                        1f,
                        0f,
                        false,
                        1f,
                        CreateNeutralCurve);

                case AutoExposureCommonPreset.HistogramInteriorExterior:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Histogram Interior -> Exterior",
                        "Wide-range histogram preset with a +1 EV lift for outdoor transition readability.",
                        AutoExposureMode.Histogram,
                        new Vector2(70f, 95f),
                        VolumeSafeMinEV100,
                        4f,
                        new Vector2(-12f, 8f),
                        5f,
                        1.5f,
                        0f,
                        false,
                        1f,
                        CreateNeutralCurve);

                case AutoExposureCommonPreset.HistogramLowLightCurve:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Histogram Low Light Curve",
                        "Low-light histogram preset with curve-driven bias for night-scene stress tests.",
                        AutoExposureMode.Histogram,
                        new Vector2(85f, 98f),
                        VolumeSafeMinEV100,
                        0f,
                        new Vector2(-14f, 4f),
                        2.5f,
                        0.75f,
                        0f,
                        false,
                        0.5f,
                        () => CreateLinearCurve(-10f, 1f, 4f, -0.5f));

                case AutoExposureCommonPreset.ManualNeutral:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Manual Neutral",
                        "Manual EV100 baseline with no compensation for fixed-lighting comparisons.",
                        AutoExposureMode.Manual,
                        new Vector2(80f, 95f),
                        -5.058894f,
                        1f,
                        new Vector2(-10f, 6f),
                        3f,
                        1f,
                        0f,
                        false,
                        0f,
                        CreateNeutralCurve);

                case AutoExposureCommonPreset.ManualCurveBias:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Manual Curve Bias",
                        "Manual EV100 preset with baked compensation and a non-flat compensation curve.",
                        AutoExposureMode.Manual,
                        new Vector2(80f, 95f),
                        -5.058894f,
                        1f,
                        new Vector2(-10f, 6f),
                        3f,
                        1f,
                        -2.5f,
                        false,
                        0.5f,
                        () => CreateLinearCurve(-8f, 0.75f, 8f, -0.25f));

                case AutoExposureCommonPreset.ManualPhysicalCamera:
                    return new AutoExposurePresetDefinition(
                        preset,
                        "Manual Physical Camera",
                        "Manual preset that resolves EV100 from aperture, shutter speed, and ISO.",
                        AutoExposureMode.Manual,
                        new Vector2(80f, 95f),
                        -5.058894f,
                        1f,
                        new Vector2(-10f, 6f),
                        3f,
                        1f,
                        -3f,
                        true,
                        0f,
                        CreateNeutralCurve);

                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        internal static AnimationCurve CreateNeutralCurve()
        {
            return CreateConstantCurve(0f);
        }

        private static AnimationCurve CreateConstantCurve(float value)
        {
            var curve = AnimationCurve.Constant(-16f, 16f, value);
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }

        private static AnimationCurve CreateLinearCurve(float minEV100, float minValue, float maxEV100, float maxValue)
        {
            var curve = AnimationCurve.Linear(minEV100, minValue, maxEV100, maxValue);
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }
    }
}
