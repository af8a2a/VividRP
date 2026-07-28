using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static partial class AutoExposureSettingsResolver
    {
        internal static AutoExposureSettingsData ResolveHDRP(
            AutoExposure autoExposure,
            Camera camera,
            bool isFirstFrame)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            settings.implementation = AutoExposureImplementationPath.HDRP;
            settings.hdrpExposureMode = autoExposure.ResolveExposureMode();
            settings.mode = AutoExposureExposureModeUtility.ResolveRuntimeMode(
                settings.hdrpExposureMode);
            settings.hdrpMeteringMode = autoExposure.meteringMode.value;
            settings.hdrpAdaptationMode = autoExposure.adaptationMode.value;
            settings.applyPhysicalCameraExposure =
                AutoExposureExposureModeUtility.UsesPhysicalCamera(settings.hdrpExposureMode);
            settings.manualEV100 = ResolveManualEV100(
                camera,
                autoExposure.fixedExposure.value,
                settings.applyPhysicalCameraExposure);
            settings.targetMidGray = ResolveHDRPTargetMidGray(
                autoExposure.targetMidGray.value);
            settings.exposureCompensationSettings = ResolveExposureCompensation(
                autoExposure.compensation.value);
            settings.exposureCompensationCurveStops = 0f;
            settings.exposureCompensationAll = settings.exposureCompensationSettings;
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(
                settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(
                settings.manualEV100,
                settings.exposureCompensationAll);
            settings.hdrpWeightTextureMask = autoExposure.weightTextureMask.value;
            settings.hdrpHistogramUseCurveRemapping =
                autoExposure.histogramUseCurveRemapping.value;
            settings.hdrpCenterAroundExposureTarget =
                autoExposure.centerAroundExposureTarget.value;
            settings.hdrpProceduralCenter = autoExposure.proceduralCenter.value;
            settings.hdrpProceduralRadii = autoExposure.proceduralRadii.value;
            settings.hdrpProceduralSoftness = Mathf.Max(
                autoExposure.proceduralSoftness.value,
                0.001f);
            settings.hdrpMaskMinIntensity = autoExposure.maskMinIntensity.value;
            settings.hdrpMaskMaxIntensity = autoExposure.maskMaxIntensity.value;

            var usesCurveRemapping = AutoExposureExposureModeUtility.UsesCurveRemapping(
                    settings.hdrpExposureMode)
                || (settings.hdrpExposureMode == AutoExposureExposureMode.AutomaticHistogram
                    && settings.hdrpHistogramUseCurveRemapping);
            if (usesCurveRemapping)
            {
                var curveMapTextureData = AutoExposureCurveMapUtility.Resolve(
                    autoExposure.curveMap.value,
                    autoExposure.limitMin.value,
                    autoExposure.limitMax.value);
                settings.hdrpCurveMapTexture = curveMapTextureData.texture;
                settings.hdrpCurveMapMinEV100 = curveMapTextureData.minEV100;
                settings.hdrpCurveMapMaxEV100 = curveMapTextureData.maxEV100;
            }

            if (settings.mode == AutoExposureMode.Manual)
            {
                settings.enabled = autoExposure.enabled.value;
                settings.forceTarget = 1f;
                return settings;
            }

            var exposureHighPercent = Mathf.Clamp(
                    autoExposure.histogramPercentages.max,
                    0f,
                    100f)
                * PercentToScale;
            var exposureLowPercent = Mathf.Min(
                Mathf.Clamp(autoExposure.histogramPercentages.min, 0f, 100f)
                    * PercentToScale,
                exposureHighPercent);
            var minWhitePointLuminance = ResolveWhitePointLuminanceFromEV100(
                autoExposure.limitMin.value);
            var maxWhitePointLuminance = Mathf.Max(
                minWhitePointLuminance,
                ResolveWhitePointLuminanceFromEV100(autoExposure.limitMax.value));
            var histogramScaleBias = BuildHistogramScaleBiasFromEV100(
                autoExposure.limitMin.value,
                autoExposure.limitMax.value);
            var usesProgressiveAdaptation =
                settings.hdrpAdaptationMode == AutoExposureAdaptationMode.Progressive;
            var validRange = autoExposure.limitMin.value < autoExposure.limitMax.value;
            var validSpeeds = !usesProgressiveAdaptation
                || (autoExposure.adaptationSpeedDarkToLight.value > 0f
                    && autoExposure.adaptationSpeedLightToDark.value > 0f);

            settings.enabled = autoExposure.IsHDRPActive();
            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * MiddleGrey;
            settings.deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            settings.exposureSpeedUp = Mathf.Max(
                autoExposure.adaptationSpeedDarkToLight.value,
                MinSpeed);
            settings.exposureSpeedDown = Mathf.Max(
                autoExposure.adaptationSpeedLightToDark.value,
                MinSpeed);
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, autoExposure.limitMin.value);
            settings.exponentialUpM = ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedUp,
                DefaultStartDistance);
            settings.exponentialDownM = ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedDown,
                DefaultStartDistance);
            settings.startDistance = DefaultStartDistance;
            settings.forceTarget = !usesProgressiveAdaptation
                || isFirstFrame
                || !validRange
                || !validSpeeds
                ? 1f
                : 0f;
            return settings;
        }

        internal static float ResolveHDRPTargetMidGray(TargetMidGray targetMidGray)
        {
            switch (targetMidGray)
            {
                case TargetMidGray.Grey14:
                    return 14f;
                case TargetMidGray.Grey18:
                    return 18f;
                default:
                    return 12.5f;
            }
        }

        internal static void ResolveHDRPProceduralMeteringParameters(
            in AutoExposureSettingsData settings,
            Camera camera,
            int viewportWidth,
            int viewportHeight,
            out Vector4 proceduralMaskParams,
            out Vector4 proceduralMaskParams2)
        {
            var center = settings.hdrpProceduralCenter;
            if (settings.hdrpCenterAroundExposureTarget
                && camera != null
                && camera.TryGetComponent<VividAdditionalCameraData>(out var cameraData)
                && cameraData.exposureTarget != null)
            {
                var viewportPosition = camera.WorldToViewportPoint(
                    cameraData.exposureTarget.transform.position);
                if (viewportPosition.z > 0f)
                {
                    center += new Vector2(
                        viewportPosition.x,
                        1f - viewportPosition.y);
                }
            }

            center.x = Mathf.Clamp01(center.x);
            center.y = Mathf.Clamp01(center.y);
            var radii = new Vector2(
                Mathf.Max(Mathf.Clamp01(settings.hdrpProceduralRadii.x), 1e-4f),
                Mathf.Max(Mathf.Clamp01(settings.hdrpProceduralRadii.y), 1e-4f));
            var width = Mathf.Max(1, viewportWidth);
            var height = Mathf.Max(1, viewportHeight);

            proceduralMaskParams = new Vector4(
                center.x * width,
                center.y * height,
                radii.x * width,
                radii.y * height);
            proceduralMaskParams2 = new Vector4(
                1f / Mathf.Max(settings.hdrpProceduralSoftness, 0.001f),
                LightUnitUtils.Ev100ToNits(settings.hdrpMaskMinIntensity),
                LightUnitUtils.Ev100ToNits(settings.hdrpMaskMaxIntensity),
                0f);
        }
    }
}
