using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct AutoExposureSettingsData
    {
        public bool enabled;
        public float exposureLowPercent;
        public float exposureHighPercent;
        public float minAverageLuminance;
        public float maxAverageLuminance;
        public float exposureCompensation;
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
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBias(-10f, 6f);

            return new AutoExposureSettingsData
            {
                enabled = false,
                exposureLowPercent = 0.8f,
                exposureHighPercent = 0.95f,
                minAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                maxAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                exposureCompensation = 1f,
                deltaTime = 1f / 60f,
                exposureSpeedUp = 1f,
                exposureSpeedDown = 1f,
                histogramScale = histogramScaleBias.x,
                histogramBias = histogramScaleBias.y,
                luminanceMin = Mathf.Pow(2f, -10f),
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
        public bool autoExposureEnabled;
        public bool hasValidHistory;

        public override void Reset()
        {
            settings = AutoExposureSettingsData.CreateDefault();
            defaultExposureBuffer = null;
            previousExposureBuffer = null;
            currentExposureBuffer = null;
            preExposureBuffer = null;
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
                ? AutoExposureSettingsResolver.Resolve(temporalData != null && temporalData.isFirstFrame)
                : AutoExposureSettingsData.CreateDefault();

            var autoExposureEnabled = postProcessingAllowed
                && settings.enabled
                && camera != null
                && hasAutoExposureCompute;

            AutoExposureHistoryState state = null;
            if (autoExposureEnabled)
            {
                state = s_HistorySystem.GetOrCreateBase(camera);
                EnsureAutoExposureHistoryState(state);

                if (!state.wasEnabledLastFrame)
                {
                    state.hasValidHistory = false;
                    settings.forceTarget = 1f;
                }
            }
            else if (s_HistorySystem.TryGetBase(camera, out state))
            {
                state.hasValidHistory = false;
                state.wasEnabledLastFrame = false;
            }

            var defaultExposureBuffer = s_DefaultExposureBuffer;
            var hasValidHistory = autoExposureEnabled && state != null && state.hasValidHistory;
            var previousExposureBuffer = autoExposureEnabled && state?.previousExposureBuffer != null
                ? state.previousExposureBuffer
                : defaultExposureBuffer;
            var currentExposureBuffer = autoExposureEnabled && state?.currentExposureBuffer != null
                ? state.currentExposureBuffer
                : defaultExposureBuffer;
            var preExposureBuffer = hasValidHistory && state?.previousExposureBuffer != null
                ? state.previousExposureBuffer
                : defaultExposureBuffer;

            exposureData.settings = settings;
            exposureData.defaultExposureBuffer = defaultExposureBuffer;
            exposureData.previousExposureBuffer = previousExposureBuffer;
            exposureData.currentExposureBuffer = currentExposureBuffer;
            exposureData.preExposureBuffer = preExposureBuffer;
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
            s_DefaultExposureBuffer.SetData(new[] { new Vector4(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f) });
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
            buffer.SetData(new[] { new Vector4(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f) });
        }
    }

    internal static class AutoExposureSettingsResolver
    {
        internal const float MiddleGrey = 0.18f;
        internal const float DefaultStartDistance = 1.5f;

        private const float PercentToScale = 0.01f;
        private const float MinSpeed = 0.001f;
        private const float FrameTimeEpsilon = 1f / 60f;

        internal static AutoExposureSettingsData Resolve(bool isFirstFrame)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var autoExposure = stack.GetComponent<AutoExposure>();
            if (autoExposure == null)
                return settings;

            var exposureHighPercent = Mathf.Clamp(autoExposure.highPercent.value, 1f, 99f) * PercentToScale;
            var exposureLowPercent = Mathf.Min(
                Mathf.Clamp(autoExposure.lowPercent.value, 1f, 99f) * PercentToScale,
                exposureHighPercent);

            var minWhitePointLuminance = Mathf.Max(0f, autoExposure.minBrightness.value);
            var maxWhitePointLuminance = Mathf.Max(minWhitePointLuminance, autoExposure.maxBrightness.value);

            var histogramScaleBias = BuildHistogramScaleBias(
                autoExposure.histogramLogMin.value,
                autoExposure.histogramLogMax.value);
            var validRange = maxWhitePointLuminance > minWhitePointLuminance;
            var validSpeeds = autoExposure.speedUp.value > 0f && autoExposure.speedDown.value > 0f;

            settings.enabled = autoExposure.IsActive();
            settings.exposureLowPercent = exposureLowPercent;
            settings.exposureHighPercent = exposureHighPercent;
            settings.minAverageLuminance = minWhitePointLuminance * MiddleGrey;
            settings.maxAverageLuminance = maxWhitePointLuminance * MiddleGrey;
            settings.exposureCompensation = ResolveExposureCompensation(autoExposure.exposureCompensation.value);
            settings.deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            settings.exposureSpeedUp = Mathf.Max(autoExposure.speedUp.value, MinSpeed);
            settings.exposureSpeedDown = Mathf.Max(autoExposure.speedDown.value, MinSpeed);
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, Mathf.Min(autoExposure.histogramLogMin.value, autoExposure.histogramLogMax.value - 1e-4f));
            settings.exponentialUpM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedUp, DefaultStartDistance);
            settings.exponentialDownM = ComputeExponentialTransitionMultiplier(settings.exposureSpeedDown, DefaultStartDistance);
            settings.startDistance = DefaultStartDistance;
            settings.forceTarget = isFirstFrame || !validRange || !validSpeeds ? 1f : 0f;
            settings.meterMask = autoExposure.meterMask.value;
            return settings;
        }

        internal static float ResolveExposureCompensation(float compensationStops)
        {
            return Mathf.Pow(2f, compensationStops);
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
