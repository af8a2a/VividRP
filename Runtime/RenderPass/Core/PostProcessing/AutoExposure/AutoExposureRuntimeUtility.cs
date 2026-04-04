using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct AutoExposureSettingsData
    {
        public bool enabled;
        public AutoExposureMode mode;
        public float exposureLowPercent;
        public float exposureHighPercent;
        public float minAverageLuminance;
        public float maxAverageLuminance;
        public bool applyPhysicalCameraExposure;
        public float manualEV100;
        public float manualAverageSceneLuminance;
        public float exposureCompensation;
        public float fixedExposureScale;
        public float deltaTime;
        public float exposureSpeedUp;
        public float exposureSpeedDown;
        public float histogramScale;
        public float histogramBias;
        public float luminanceMin;
        public float exponentialUpM;
        public float exponentialDownM;
        public float startDistance;
        public float forceTarget;
        public Texture meterMask;

        public static AutoExposureSettingsData CreateDefault()
        {
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBiasFromEV100(-10f, 6f);
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(-10f, 6f);

            return new AutoExposureSettingsData
            {
                enabled = false,
                mode = AutoExposureMode.Histogram,
                exposureLowPercent = 0.8f,
                exposureHighPercent = 0.95f,
                minAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                maxAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                applyPhysicalCameraExposure = false,
                manualEV100 = 0f,
                manualAverageSceneLuminance = AutoExposureSettingsResolver.MiddleGrey,
                exposureCompensation = 1f,
                fixedExposureScale = 1f,
                deltaTime = 1f / 60f,
                exposureSpeedUp = 1f,
                exposureSpeedDown = 1f,
                histogramScale = histogramScaleBias.x,
                histogramBias = histogramScaleBias.y,
                luminanceMin = Mathf.Pow(2f, histogramLogRange.x),
                exponentialUpM = 1f,
                exponentialDownM = 1f,
                startDistance = AutoExposureSettingsResolver.DefaultStartDistance,
                forceTarget = 1f,
                meterMask = null,
            };
        }
    }

    internal sealed class VividExposureData : ContextItem
    {
        public AutoExposureSettingsData settings;
        public GraphicsBuffer defaultExposureBuffer;
        public GraphicsBuffer previousExposureBuffer;
        public GraphicsBuffer currentExposureBuffer;
        public GraphicsBuffer preExposureBuffer;
        public bool exposureEnabled;
        public bool autoExposureEnabled;
        public bool hasValidHistory;

        public override void Reset()
        {
            settings = AutoExposureSettingsData.CreateDefault();
            defaultExposureBuffer = null;
            previousExposureBuffer = null;
            currentExposureBuffer = null;
            preExposureBuffer = null;
            exposureEnabled = false;
            autoExposureEnabled = false;
            hasValidHistory = false;
        }
    }

    internal sealed class AutoExposureHistoryState : CameraRelativeState
    {
        public GraphicsBuffer previousExposureBuffer;
        public GraphicsBuffer currentExposureBuffer;
        public bool hasValidHistory;
        public bool wasEnabledLastFrame;

        public void SwapBuffers()
        {
            (previousExposureBuffer, currentExposureBuffer) = (currentExposureBuffer, previousExposureBuffer);
        }

        public override void Dispose()
        {
            previousExposureBuffer?.Dispose();
            previousExposureBuffer = null;

            currentExposureBuffer?.Dispose();
            currentExposureBuffer = null;

            hasValidHistory = false;
            wasEnabledLastFrame = false;
        }
    }

    internal sealed class AutoExposureHistorySystem : CameraRelativeSystem<AutoExposureHistoryState>
    {
    }

    internal static class AutoExposureRuntimeManager
    {
        private const int AutoExposureVectorStride = sizeof(float) * 4;

        private static readonly int AutoExposurePreExposureBufferId = Shader.PropertyToID("_VividAutoExposurePreExposureBuffer");
        private static readonly AutoExposureHistorySystem s_HistorySystem = new();
        private static readonly Vector4[] s_ExposureBufferData = new Vector4[1];

        private static GraphicsBuffer s_DefaultExposureBuffer;

        internal static void PrepareFrame(ContextContainer frameData, CommandBuffer cmd)
        {
            var exposureData = frameData.GetOrCreate<VividExposureData>();
            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var hasAutoExposureCompute = resources?.AutoExposureCompute != null;

            EnsureDefaultExposureBuffer();
            s_HistorySystem.PurgeDestroyedCameras();

            var settings = postProcessingAllowed
                ? AutoExposureSettingsResolver.Resolve(camera, temporalData != null && temporalData.isFirstFrame)
                : AutoExposureSettingsData.CreateDefault();
            settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(settings, camera);

            var exposureEnabled = postProcessingAllowed
                && settings.enabled
                && camera != null
                && (settings.mode == AutoExposureMode.Manual || hasAutoExposureCompute);
            var autoExposureEnabled = exposureEnabled
                && settings.mode == AutoExposureMode.Histogram
                && hasAutoExposureCompute;

            AutoExposureHistoryState state = null;
            if (exposureEnabled)
            {
                state = s_HistorySystem.GetOrCreateBase(camera);
                EnsureAutoExposureHistoryState(state);

                if (!state.wasEnabledLastFrame)
                {
                    state.hasValidHistory = false;
                    settings.forceTarget = 1f;
                }

                if (settings.mode == AutoExposureMode.Manual && state.currentExposureBuffer != null)
                {
                    WriteExposureBuffer(
                        state.currentExposureBuffer,
                        settings.fixedExposureScale,
                        settings.manualAverageSceneLuminance,
                        settings.exposureCompensation);
                }
            }
            else if (s_HistorySystem.TryGetBase(camera, out state))
            {
                state.hasValidHistory = false;
                state.wasEnabledLastFrame = false;
            }

            var defaultExposureBuffer = s_DefaultExposureBuffer;
            var hasValidHistory = exposureEnabled && state != null && state.hasValidHistory;
            var previousExposureBuffer = exposureEnabled && state?.previousExposureBuffer != null
                ? state.previousExposureBuffer
                : defaultExposureBuffer;
            var currentExposureBuffer = exposureEnabled && state?.currentExposureBuffer != null
                ? state.currentExposureBuffer
                : defaultExposureBuffer;
            var preExposureBuffer = exposureEnabled
                && settings.mode == AutoExposureMode.Manual
                && state?.currentExposureBuffer != null
                ? state.currentExposureBuffer
                : hasValidHistory && state?.previousExposureBuffer != null
                    ? state.previousExposureBuffer
                    : defaultExposureBuffer;

            exposureData.settings = settings;
            exposureData.defaultExposureBuffer = defaultExposureBuffer;
            exposureData.previousExposureBuffer = previousExposureBuffer;
            exposureData.currentExposureBuffer = currentExposureBuffer;
            exposureData.preExposureBuffer = preExposureBuffer;
            exposureData.exposureEnabled = exposureEnabled;
            exposureData.autoExposureEnabled = autoExposureEnabled;
            exposureData.hasValidHistory = hasValidHistory;

            if (cmd != null && preExposureBuffer != null)
                cmd.SetGlobalBuffer(AutoExposurePreExposureBufferId, preExposureBuffer);
        }

        internal static void CommitFrame(Camera camera)
        {
            if (!s_HistorySystem.TryGetBase(camera, out var state) || state == null)
                return;

            state.SwapBuffers();
            state.hasValidHistory = true;
            state.wasEnabledLastFrame = true;
        }

        internal static void Clear()
        {
            s_HistorySystem.Dispose();
            s_DefaultExposureBuffer?.Dispose();
            s_DefaultExposureBuffer = null;
        }

        private static void EnsureDefaultExposureBuffer()
        {
            if (s_DefaultExposureBuffer != null
                && s_DefaultExposureBuffer.count == 1
                && s_DefaultExposureBuffer.stride == AutoExposureVectorStride)
            {
                return;
            }

            s_DefaultExposureBuffer?.Dispose();
            s_DefaultExposureBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                AutoExposureVectorStride);
            s_DefaultExposureBuffer.name = "VividRP Auto Exposure Default";
            WriteExposureBuffer(s_DefaultExposureBuffer, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f);
        }

        private static void EnsureAutoExposureHistoryState(AutoExposureHistoryState state)
        {
            if (state == null)
                return;

            EnsureAutoExposureBuffer(ref state.previousExposureBuffer, "VividRP Auto Exposure Previous");
            EnsureAutoExposureBuffer(ref state.currentExposureBuffer, "VividRP Auto Exposure Current");
        }

        private static void EnsureAutoExposureBuffer(ref GraphicsBuffer buffer, string name)
        {
            if (buffer != null && buffer.count == 1 && buffer.stride == AutoExposureVectorStride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, AutoExposureVectorStride);
            buffer.name = name;
            WriteExposureBuffer(buffer, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f);
        }

        private static void WriteExposureBuffer(GraphicsBuffer buffer, float exposureScale, float averageSceneLuminance, float middleGreyCompensation)
        {
            if (buffer == null)
                return;

            s_ExposureBufferData[0] = new Vector4(exposureScale, exposureScale, averageSceneLuminance, middleGreyCompensation);
            buffer.SetData(s_ExposureBufferData);
        }
    }

    internal static class AutoExposureSettingsResolver
    {
        internal const float MiddleGrey = 0.18f;
        internal const float DefaultStartDistance = 1.5f;
        internal const float DefaultLensAttenuation = 0.78f;

        private const float IsoSaturationSpeedConstant = 0.78f;
        private const float PercentToScale = 0.01f;
        private const float MinSpeed = 0.001f;
        private const float FrameTimeEpsilon = 1f / 60f;

        internal static AutoExposureSettingsData Resolve(Camera camera, bool isFirstFrame)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var autoExposure = stack.GetComponent<AutoExposure>();
            if (autoExposure == null)
                return settings;

            settings.mode = autoExposure.mode.value;
            settings.applyPhysicalCameraExposure = autoExposure.applyPhysicalCameraExposure.value;
            settings.manualEV100 = ResolveManualEV100(
                camera,
                autoExposure.manualEV100.value,
                settings.applyPhysicalCameraExposure);
            settings.exposureCompensation = ResolveExposureCompensation(autoExposure.exposureCompensation.value);
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensation);
            settings.meterMask = autoExposure.meterMask.value;

            if (settings.mode == AutoExposureMode.Manual)
            {
                settings.enabled = autoExposure.enabled.value;
                settings.forceTarget = 1f;
                return settings;
            }

            var exposureHighPercent = Mathf.Clamp(autoExposure.percent.max, 1f, 99f) * PercentToScale;
            var exposureLowPercent = Mathf.Min(
                Mathf.Clamp(autoExposure.percent.min, 1f, 99f) * PercentToScale,
                exposureHighPercent);

            var minWhitePointLuminance = ResolveWhitePointLuminanceFromEV100(autoExposure.minEV100.value);
            var maxWhitePointLuminance = ResolveWhitePointLuminanceFromEV100(autoExposure.maxEV100.value);
            maxWhitePointLuminance = Mathf.Max(minWhitePointLuminance, maxWhitePointLuminance);
            var histogramLogRangeValue = autoExposure.histogramLogRange.value;

            var histogramLogRange = ResolveHistogramLogRangeFromEV100(
                histogramLogRangeValue.x,
                histogramLogRangeValue.y);
            var histogramScaleBias = BuildHistogramScaleBias(histogramLogRange.x, histogramLogRange.y);
            var validRange = autoExposure.minEV100.value < autoExposure.maxEV100.value;
            var validSpeeds = autoExposure.speedUp.value > 0f && autoExposure.speedDown.value > 0f;

            settings.enabled = autoExposure.IsActive();
            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * MiddleGrey;
            settings.deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            settings.exposureSpeedUp = Mathf.Max(autoExposure.speedUp.value, MinSpeed);
            settings.exposureSpeedDown = Mathf.Max(autoExposure.speedDown.value, MinSpeed);
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, histogramLogRange.x);
            settings.exponentialUpM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedUp, DefaultStartDistance);
            settings.exponentialDownM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedDown, DefaultStartDistance);
            settings.startDistance = DefaultStartDistance;
            settings.forceTarget = isFirstFrame || !validRange || !validSpeeds ? 1f : 0f;
            return settings;
        }

        internal static float ResolveExposureCompensation(float compensationStops)
        {
            return Mathf.Pow(2f, compensationStops);
        }

        internal static AutoExposureSettingsData ResolvePhysicalCameraFallback(AutoExposureSettingsData settings, Camera camera)
        {
            if (settings.enabled || camera == null || !camera.usePhysicalProperties)
                return settings;

            settings.enabled = true;
            settings.mode = AutoExposureMode.Manual;
            settings.applyPhysicalCameraExposure = true;
            settings.manualEV100 = ResolvePhysicalCameraEV100(camera);
            settings.exposureCompensation = 1f;
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensation);
            settings.forceTarget = 1f;
            return settings;
        }

        internal static float ResolveManualEV100(Camera camera, float manualEV100, bool applyPhysicalCameraExposure)
        {
            if (!applyPhysicalCameraExposure)
                return manualEV100;

            return ResolvePhysicalCameraEV100(camera);
        }

        internal static float ResolvePhysicalCameraEV100(Camera camera)
        {
            if (camera == null)
                return 0f;

            var aperture = Mathf.Max(camera.aperture, 1e-4f);
            var shutterSpeed = Mathf.Max(camera.shutterSpeed, 1e-6f);
            var iso = Mathf.Max((float)camera.iso, 1f);
            return ColorUtils.ComputeEV100(aperture, shutterSpeed, iso);
        }

        internal static float ResolveLuminanceMaxFromLensAttenuation(float lensAttenuation = DefaultLensAttenuation)
        {
            return IsoSaturationSpeedConstant / Mathf.Max(lensAttenuation, 0.01f);
        }

        internal static float ResolveWhitePointLuminanceFromEV100(float ev100)
        {
            return ResolveLuminanceMaxFromLensAttenuation() * Mathf.Pow(2f, ev100);
        }

        internal static float ResolveLog2LuminanceFromEV100(float ev100)
        {
            return ev100 + Mathf.Log(ResolveLuminanceMaxFromLensAttenuation(), 2f);
        }

        internal static Vector2 ResolveHistogramLogRangeFromEV100(float histogramMinEV100, float histogramMaxEV100)
        {
            var histogramLogMax = ResolveLog2LuminanceFromEV100(histogramMaxEV100);
            var histogramLogMin = Mathf.Min(
                ResolveLog2LuminanceFromEV100(histogramMinEV100),
                histogramLogMax - 1f);
            return new Vector2(histogramLogMin, histogramLogMax);
        }

        internal static float ResolveAverageSceneLuminanceFromEV100(float ev100)
        {
            return ResolveWhitePointLuminanceFromEV100(ev100) * MiddleGrey;
        }

        internal static float ResolveManualExposureScale(float ev100, float exposureCompensation)
        {
            var whitePointLuminance = ResolveWhitePointLuminanceFromEV100(ev100);
            return exposureCompensation / Mathf.Max(whitePointLuminance, 1e-6f);
        }

        internal static Vector2 BuildHistogramScaleBiasFromEV100(float histogramMinEV100, float histogramMaxEV100)
        {
            var histogramLogRange = ResolveHistogramLogRangeFromEV100(histogramMinEV100, histogramMaxEV100);
            return BuildHistogramScaleBias(histogramLogRange.x, histogramLogRange.y);
        }

        internal static Vector2 BuildHistogramScaleBias(float histogramLogMin, float histogramLogMax)
        {
            var resolvedLogMax = Mathf.Max(histogramLogMax, histogramLogMin + 1e-4f);
            var resolvedLogMin = Mathf.Min(histogramLogMin, resolvedLogMax - 1e-4f);
            var histogramDelta = Mathf.Max(resolvedLogMax - resolvedLogMin, 1e-4f);
            var histogramScale = 1f / histogramDelta;
            var histogramBias = -resolvedLogMin * histogramScale;
            return new Vector2(histogramScale, histogramBias);
        }

        internal static float ComputeExponentialTransitionMultiplier(float adaptationSpeed, float startDistance)
        {
            var safeSpeed = Mathf.Max(adaptationSpeed, MinSpeed);
            var startTime = startDistance / safeSpeed;
            var denominator = (1f - Mathf.Pow(2f, -FrameTimeEpsilon * safeSpeed)) * startTime;
            return denominator > 1e-6f ? FrameTimeEpsilon / denominator : 1f;
        }
    }
}
