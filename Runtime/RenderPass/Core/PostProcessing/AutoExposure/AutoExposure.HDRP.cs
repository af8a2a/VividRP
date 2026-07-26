using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace VividRP.Runtime
{
    public sealed partial class AutoExposure
    {
        private const int CurrentHDRPSettingsVersion = 1;

        [SerializeField, HideInInspector]
        private int m_HDRPSettingsVersion;

        [Tooltip("Selects the HDRP exposure mode.")]
        public AutoExposureExposureModeParameter exposureMode =
            new(AutoExposureExposureMode.AutomaticHistogram);

        [Tooltip("Selects the HDRP metering pattern.")]
        public AutoExposureMeteringModeParameter meteringMode =
            new(AutoExposureMeteringMode.ProceduralMask);

        [Tooltip("Sets a fixed exposure value in EV100.")]
        public FloatParameter fixedExposure = new(0f);

        [Tooltip("Sets the exposure compensation in EV stops.")]
        public FloatParameter compensation = new(0f);

        [Tooltip("Sets the minimum automatic exposure value in EV100.")]
        public FloatParameter limitMin = new(5f);

        [Tooltip("Sets the maximum automatic exposure value in EV100.")]
        public FloatParameter limitMax = new(13f);

        [Tooltip("Remaps the measured scene EV100 to the desired exposure.")]
        public NoInterpAnimationCurveParameter curveMap =
            new(CreateDefaultHDRPCurveMap());

        [Tooltip("Selects how HDRP adapts exposure between frames.")]
        public AutoExposureAdaptationModeParameter adaptationMode =
            new(AutoExposureAdaptationMode.Progressive);

        [Tooltip("Sets the adaptation speed when the camera moves from dark to light.")]
        public MinFloatParameter adaptationSpeedDarkToLight = new(4f, 0.001f);

        [Tooltip("Sets the adaptation speed when the camera moves from light to dark.")]
        public MinFloatParameter adaptationSpeedLightToDark = new(4f, 0.001f);

        [FormerlySerializedAs("meterMask")]
        [Tooltip("Sets the texture used by Mask Weighted metering.")]
        public Texture2DParameter weightTextureMask = new(null);

        [Tooltip("Sets the lower and upper histogram percentages used to estimate exposure.")]
        public FloatRangeParameter histogramPercentages =
            new(new Vector2(10f, 90f), 0f, 100f);

        [Tooltip("Remaps Automatic Histogram exposure through Curve Map.")]
        public BoolParameter histogramUseCurveRemapping = new(false);

        [AdditionalProperty]
        [Tooltip("Sets the target middle gray used by HDRP exposure.")]
        public EnumParameter<TargetMidGray> targetMidGray = new(TargetMidGray.Grey125);

        [Tooltip("Centers the procedural mask around the camera exposure target.")]
        public BoolParameter centerAroundExposureTarget = new(false);

        [Tooltip("Sets the center or target-relative offset of the procedural mask.")]
        public NoInterpVector2Parameter proceduralCenter = new(new Vector2(0.5f, 0.5f));

        [Tooltip("Sets the horizontal and vertical radius of the procedural mask.")]
        public NoInterpVector2Parameter proceduralRadii = new(new Vector2(0.5f, 0.5f));

        [Tooltip("Sets the falloff softness of the procedural mask.")]
        public MinFloatParameter proceduralSoftness = new(1f, 0.001f);

        [AdditionalProperty]
        [Tooltip("Rejects procedural-mask samples below this EV100 intensity.")]
        public FloatParameter maskMinIntensity = new(-30f);

        [AdditionalProperty]
        [Tooltip("Rejects procedural-mask samples above this EV100 intensity.")]
        public FloatParameter maskMaxIntensity = new(30f);

        public AutoExposureExposureMode ResolveExposureMode()
        {
            return exposureMode?.value ?? AutoExposureExposureMode.AutomaticHistogram;
        }

        internal bool IsHDRPActive()
        {
            if (!enabled.value)
                return false;

            var resolvedMode = ResolveExposureMode();
            if (AutoExposureExposureModeUtility.UsesManualSettings(resolvedMode))
                return true;

            var usesFixedAdaptation = adaptationMode?.value == AutoExposureAdaptationMode.Fixed;
            return limitMax.value >= limitMin.value
                && (usesFixedAdaptation
                    || (adaptationSpeedDarkToLight.value > 0f
                        && adaptationSpeedLightToDark.value > 0f));
        }

        private void EnsureHDRPParameters()
        {
            exposureMode ??= new AutoExposureExposureModeParameter(
                AutoExposureExposureMode.AutomaticHistogram);
            meteringMode ??= new AutoExposureMeteringModeParameter(
                AutoExposureMeteringMode.ProceduralMask);
            fixedExposure ??= new FloatParameter(0f);
            compensation ??= new FloatParameter(0f);
            limitMin ??= new FloatParameter(5f);
            limitMax ??= new FloatParameter(13f);
            curveMap ??= new NoInterpAnimationCurveParameter(CreateDefaultHDRPCurveMap());
            adaptationMode ??= new AutoExposureAdaptationModeParameter(
                AutoExposureAdaptationMode.Progressive);
            adaptationSpeedDarkToLight ??= new MinFloatParameter(4f, 0.001f);
            adaptationSpeedLightToDark ??= new MinFloatParameter(4f, 0.001f);
            weightTextureMask ??= new Texture2DParameter(null);
            histogramPercentages ??= new FloatRangeParameter(
                new Vector2(10f, 90f),
                0f,
                100f);
            histogramUseCurveRemapping ??= new BoolParameter(false);
            targetMidGray ??= new EnumParameter<TargetMidGray>(TargetMidGray.Grey125);
            centerAroundExposureTarget ??= new BoolParameter(false);
            proceduralCenter ??= new NoInterpVector2Parameter(new Vector2(0.5f, 0.5f));
            proceduralRadii ??= new NoInterpVector2Parameter(new Vector2(0.5f, 0.5f));
            proceduralSoftness ??= new MinFloatParameter(1f, 0.001f);
            maskMinIntensity ??= new FloatParameter(-30f);
            maskMaxIntensity ??= new FloatParameter(30f);

            if (curveMap.value == null)
                curveMap.value = CreateDefaultHDRPCurveMap();
        }

        private void MigrateSharedHDRPSettingsIfNeeded()
        {
            if (m_HDRPSettingsVersion >= CurrentHDRPSettingsVersion)
                return;

            CopyOverriddenParameter(percent, histogramPercentages);
            CopyOverriddenParameter(minEV100, limitMin);
            CopyOverriddenParameter(maxEV100, limitMax);
            CopyOverriddenParameter(speedUp, adaptationSpeedDarkToLight);
            CopyOverriddenParameter(speedDown, adaptationSpeedLightToDark);
            CopyOverriddenParameter(manualEV100, fixedExposure);
            CopyOverriddenParameter(exposureCompensation, compensation);
            m_HDRPSettingsVersion = CurrentHDRPSettingsVersion;
        }

        private static void CopyOverriddenParameter<T>(
            VolumeParameter<T> source,
            VolumeParameter<T> destination)
        {
            if (source == null || destination == null || !source.overrideState)
                return;

            destination.overrideState = true;
            destination.value = source.value;
        }

        private static AnimationCurve CreateDefaultHDRPCurveMap()
        {
            var curve = AnimationCurve.Linear(-10f, -10f, 20f, 20f);
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }
    }
}
