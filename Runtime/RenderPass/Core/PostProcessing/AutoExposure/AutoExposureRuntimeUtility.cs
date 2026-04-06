using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct AutoExposureSettingsData
    {
        public bool enabled;
        public AutoExposureExposureMode exposureMode;
        public AutoExposureMode mode;
        public AutoExposureMeteringMode meteringMode;
        public AutoExposureAdaptationMode adaptationMode;
        public float exposureLowPercent;
        public float exposureHighPercent;
        public float minAverageLuminance;
        public float maxAverageLuminance;
        public bool applyPhysicalCameraExposure;
        public float manualEV100;
        public float manualAverageSceneLuminance;
        public float exposureCompensationSettings;
        public float exposureCompensationCurveStops;
        public float exposureCompensationAll;
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
        public Texture exposureCompensationCurveTexture;
        public float exposureCompensationCurveMinEV100;
        public float exposureCompensationCurveInvRange;
        public bool exposureCompensationCurveEnabled;
        public float targetMidGray;
        public Texture curveMapTexture;
        public float curveMapMinEV100;
        public float curveMapMaxEV100;
        public Texture meterMask;

        public static AutoExposureSettingsData CreateDefault()
        {
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBiasFromEV100(-10f, 6f);
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(-10f, 6f);

            return new AutoExposureSettingsData
            {
                enabled = false,
                exposureMode = AutoExposureExposureMode.Automatic,
                mode = AutoExposureMode.Histogram,
                meteringMode = AutoExposureMeteringMode.Average,
                adaptationMode = AutoExposureAdaptationMode.Progressive,
                exposureLowPercent = 0.8f,
                exposureHighPercent = 0.95f,
                minAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                maxAverageLuminance = AutoExposureSettingsResolver.MiddleGrey,
                applyPhysicalCameraExposure = false,
                manualEV100 = 0f,
                manualAverageSceneLuminance = AutoExposureSettingsResolver.MiddleGrey,
                exposureCompensationSettings = 1f,
                exposureCompensationCurveStops = 0f,
                exposureCompensationAll = 1f,
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
                exposureCompensationCurveTexture = null,
                exposureCompensationCurveMinEV100 = AutoExposureCompensationCurveUtility.DefaultCurveMinEV100,
                exposureCompensationCurveInvRange = 1f / AutoExposureCompensationCurveUtility.DefaultCurveRange,
                exposureCompensationCurveEnabled = false,
                targetMidGray = AutoExposureSettingsResolver.MiddleGrey,
                curveMapTexture = null,
                curveMapMinEV100 = AutoExposureCurveMapUtility.DefaultCurveMinEV100,
                curveMapMaxEV100 = AutoExposureCurveMapUtility.DefaultCurveMaxEV100,
                meterMask = null,
            };
        }
    }

    internal sealed class VividExposureData : ContextItem
    {
        public AutoExposureSettingsData settings;
        public AutoExposureImplementationPath implementation;
        public GraphicsBuffer defaultExposureBuffer;
        public GraphicsBuffer previousExposureBuffer;
        public GraphicsBuffer currentExposureBuffer;
        public GraphicsBuffer preExposureBuffer;
        public RenderTexture previousExposureTexture;
        public RenderTexture currentExposureTexture;
        public bool exposureEnabled;
        public bool autoExposureEnabled;
        public bool hasValidHistory;

        public override void Reset()
        {
            settings = AutoExposureSettingsData.CreateDefault();
            implementation = AutoExposureImplementationPath.Unreal;
            defaultExposureBuffer = null;
            previousExposureBuffer = null;
            currentExposureBuffer = null;
            preExposureBuffer = null;
            previousExposureTexture = null;
            currentExposureTexture = null;
            exposureEnabled = false;
            autoExposureEnabled = false;
            hasValidHistory = false;
        }
    }

    internal sealed class AutoExposureHistoryState : CameraRelativeState
    {
        public GraphicsBuffer previousExposureBuffer;
        public GraphicsBuffer currentExposureBuffer;
        public RenderTexture previousExposureTexture;
        public RenderTexture currentExposureTexture;
        public bool hasValidHistory;
        public bool wasEnabledLastFrame;
        public AutoExposureMode lastMode;
        public AutoExposureImplementationPath lastImplementation;

        public void SwapBuffers()
        {
            (previousExposureBuffer, currentExposureBuffer) = (currentExposureBuffer, previousExposureBuffer);
            (previousExposureTexture, currentExposureTexture) = (currentExposureTexture, previousExposureTexture);
        }

        public override void Dispose()
        {
            previousExposureBuffer?.Dispose();
            previousExposureBuffer = null;

            currentExposureBuffer?.Dispose();
            currentExposureBuffer = null;

            if (previousExposureTexture != null)
            {
                previousExposureTexture.Release();
                CoreUtils.Destroy(previousExposureTexture);
                previousExposureTexture = null;
            }

            if (currentExposureTexture != null)
            {
                currentExposureTexture.Release();
                CoreUtils.Destroy(currentExposureTexture);
                currentExposureTexture = null;
            }

            hasValidHistory = false;
            wasEnabledLastFrame = false;
            lastMode = AutoExposureMode.Histogram;
            lastImplementation = AutoExposureImplementationPath.Unreal;
        }
    }

    internal sealed class AutoExposureHistorySystem : CameraRelativeSystem<AutoExposureHistoryState>
    {
    }

    internal static class AutoExposureRuntimeManager
    {
        private const int AutoExposureVectorStride = sizeof(float) * 4;

        private static readonly AutoExposureHistorySystem s_HistorySystem = new();
        private static readonly Vector4[] s_ExposureBufferData = new Vector4[1];

        private static GraphicsBuffer s_DefaultExposureBuffer;

        internal static void PrepareFrame(ContextContainer frameData)
        {
            var exposureData = frameData.GetOrCreate<VividExposureData>();
            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var pipelineAsset = VividRenderPipelineAsset.GetActiveAsset();
            var implementation = AutoExposureImplementationUtility.ResolveImplementation(pipelineAsset);
            var autoExposureCompute = AutoExposureImplementationUtility.ResolveComputeShader(
                resources,
                implementation);
            var hasAutoExposureCompute = implementation == AutoExposureImplementationPath.HDRP
                ? AutoExposureImplementationUtility.SupportsHdrpDispatch(autoExposureCompute)
                : AutoExposureImplementationUtility.SupportsUnrealDispatch(autoExposureCompute);

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

                if (!state.wasEnabledLastFrame
                    || state.lastMode != settings.mode
                    || state.lastImplementation != implementation)
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
                        settings.exposureCompensationAll);
                }

                state.lastMode = settings.mode;
                state.lastImplementation = implementation;
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
            var previousExposureTexture = exposureEnabled && state?.previousExposureTexture != null
                ? state.previousExposureTexture
                : null;
            var currentExposureTexture = exposureEnabled && state?.currentExposureTexture != null
                ? state.currentExposureTexture
                : null;

            exposureData.settings = settings;
            exposureData.implementation = implementation;
            exposureData.defaultExposureBuffer = defaultExposureBuffer;
            exposureData.previousExposureBuffer = previousExposureBuffer;
            exposureData.currentExposureBuffer = currentExposureBuffer;
            exposureData.preExposureBuffer = preExposureBuffer;
            exposureData.previousExposureTexture = previousExposureTexture;
            exposureData.currentExposureTexture = currentExposureTexture;
            exposureData.exposureEnabled = exposureEnabled;
            exposureData.autoExposureEnabled = autoExposureEnabled;
            exposureData.hasValidHistory = hasValidHistory;

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
            AutoExposureCompensationCurveUtility.Dispose();
            AutoExposureCurveMapUtility.Dispose();
        }

        internal static GraphicsBuffer GetOrCreateDefaultExposureBuffer()
        {
            EnsureDefaultExposureBuffer();
            return s_DefaultExposureBuffer;
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
            EnsureAutoExposureTexture(ref state.previousExposureTexture, "VividRP Auto Exposure Previous Texture");
            EnsureAutoExposureTexture(ref state.currentExposureTexture, "VividRP Auto Exposure Current Texture");
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

        private static void EnsureAutoExposureTexture(ref RenderTexture texture, string name)
        {
            if (texture != null
                && texture.IsCreated()
                && texture.width == 1
                && texture.height == 1
                && texture.enableRandomWrite)
            {
                return;
            }

            if (texture != null)
            {
                texture.Release();
                CoreUtils.Destroy(texture);
            }

            texture = new RenderTexture(1, 1, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
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

            settings.exposureMode = autoExposure.ResolveExposureMode();
            settings.mode = AutoExposureExposureModeUtility.ResolveRuntimeMode(settings.exposureMode);
            settings.meteringMode = autoExposure.meteringMode.value;
            settings.adaptationMode = autoExposure.adaptationMode.value;
            settings.applyPhysicalCameraExposure = AutoExposureExposureModeUtility.UsesPhysicalCamera(settings.exposureMode);
            settings.manualEV100 = ResolveManualEV100(
                camera,
                autoExposure.manualEV100.value,
                settings.applyPhysicalCameraExposure);
            settings.targetMidGray = autoExposure.targetMidGray.value;
            settings.exposureCompensationSettings = ResolveExposureCompensation(autoExposure.exposureCompensation.value);
            settings.exposureCompensationCurveStops = settings.mode == AutoExposureMode.Manual
                ? ResolveExposureCompensationCurveStops(
                    autoExposure.exposureCompensationCurve.value,
                    settings.manualEV100)
                : 0f;
            settings.exposureCompensationAll = ResolveExposureCompensationAll(
                settings.exposureCompensationSettings,
                settings.exposureCompensationCurveStops);
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensationAll);
            var curveTextureData = AutoExposureCompensationCurveUtility.Resolve(autoExposure.exposureCompensationCurve.value);
            settings.exposureCompensationCurveTexture = curveTextureData.texture;
            settings.exposureCompensationCurveMinEV100 = curveTextureData.minEV100;
            settings.exposureCompensationCurveInvRange = curveTextureData.invRange;
            settings.exposureCompensationCurveEnabled = curveTextureData.enabled;
            var curveMapTextureData = AutoExposureCurveMapUtility.Resolve(
                autoExposure.curveMap.value,
                autoExposure.minEV100.value,
                autoExposure.maxEV100.value);
            settings.curveMapTexture = curveMapTextureData.texture;
            settings.curveMapMinEV100 = curveMapTextureData.minEV100;
            settings.curveMapMaxEV100 = curveMapTextureData.maxEV100;
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
            var usesProgressiveAdaptation = settings.adaptationMode == AutoExposureAdaptationMode.Progressive;
            var validSpeeds = !usesProgressiveAdaptation
                || (autoExposure.speedUp.value > 0f && autoExposure.speedDown.value > 0f);

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
            settings.forceTarget = !usesProgressiveAdaptation || isFirstFrame || !validRange || !validSpeeds ? 1f : 0f;
            return settings;
        }

        internal static float ResolveExposureCompensation(float compensationStops)
        {
            return Mathf.Pow(2f, compensationStops);
        }

        internal static float ResolveExposureCompensationCurveStops(AnimationCurve curve, float averageSceneEV100)
        {
            if (!HasExposureCompensationCurve(curve))
                return 0f;

            return curve.Evaluate(averageSceneEV100);
        }

        internal static float ResolveExposureCompensationAll(float exposureCompensationSettings, float exposureCompensationCurveStops)
        {
            return exposureCompensationSettings * Mathf.Pow(2f, exposureCompensationCurveStops);
        }

        internal static AutoExposureSettingsData ResolvePhysicalCameraFallback(AutoExposureSettingsData settings, Camera camera)
        {
            if (settings.enabled || camera == null || !camera.usePhysicalProperties)
                return settings;

            settings.enabled = true;
            settings.exposureMode = AutoExposureExposureMode.UsePhysicalCamera;
            settings.mode = AutoExposureMode.Manual;
            settings.meteringMode = AutoExposureMeteringMode.Average;
            settings.adaptationMode = AutoExposureAdaptationMode.Fixed;
            settings.applyPhysicalCameraExposure = true;
            settings.manualEV100 = ResolvePhysicalCameraEV100(camera);
            settings.exposureCompensationSettings = 1f;
            settings.exposureCompensationCurveStops = 0f;
            settings.exposureCompensationAll = 1f;
            settings.manualAverageSceneLuminance = ResolveAverageSceneLuminanceFromEV100(settings.manualEV100);
            settings.fixedExposureScale = ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensationAll);
            settings.targetMidGray = MiddleGrey;
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

        internal static float ResolveAverageSceneEV100FromLuminance(float averageSceneLuminance)
        {
            var luminanceMax = ResolveLuminanceMaxFromLensAttenuation();
            var normalizedLuminance = averageSceneLuminance / Mathf.Max(MiddleGrey * luminanceMax, 1e-6f);
            return Mathf.Log(Mathf.Max(normalizedLuminance, 1e-6f), 2f);
        }

        internal static float ResolveManualExposureScale(float ev100, float exposureCompensation)
        {
            var whitePointLuminance = ResolveWhitePointLuminanceFromEV100(ev100);
            return exposureCompensation / Mathf.Max(whitePointLuminance, 1e-6f);
        }

        internal static bool HasExposureCompensationCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
                return false;

            var curveDomain = ResolveExposureCompensationCurveDomain(curve);
            var keys = curve.keys;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                if (Mathf.Abs(keys[keyIndex].value) > 1e-3f)
                    return true;
            }

            const int sampleCount = 17;

            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var sampleT = sampleCount == 1 ? 0f : sampleIndex / (float)(sampleCount - 1);
                var ev100 = Mathf.Lerp(curveDomain.x, curveDomain.y, sampleT);
                if (Mathf.Abs(curve.Evaluate(ev100)) > 1e-3f)
                    return true;
            }

            return false;
        }

        internal static Vector2 ResolveExposureCompensationCurveDomain(AnimationCurve curve)
        {
            return ResolveCurveDomain(
                curve,
                AutoExposureCompensationCurveUtility.DefaultCurveMinEV100,
                AutoExposureCompensationCurveUtility.DefaultCurveMaxEV100);
        }

        internal static Vector2 ResolveCurveMapDomain(AnimationCurve curve)
        {
            return ResolveCurveDomain(
                curve,
                AutoExposureCurveMapUtility.DefaultCurveMinEV100,
                AutoExposureCurveMapUtility.DefaultCurveMaxEV100);
        }

        private static Vector2 ResolveCurveDomain(
            AnimationCurve curve,
            float defaultMinEV100,
            float defaultMaxEV100)
        {
            if (curve == null || curve.length == 0)
            {
                return new Vector2(
                    defaultMinEV100,
                    defaultMaxEV100);
            }

            var keys = curve.keys;
            var minEV100 = keys[0].time;
            var maxEV100 = keys[0].time;

            for (var keyIndex = 1; keyIndex < keys.Length; keyIndex++)
            {
                minEV100 = Mathf.Min(minEV100, keys[keyIndex].time);
                maxEV100 = Mathf.Max(maxEV100, keys[keyIndex].time);
            }

            if (Mathf.Abs(maxEV100 - minEV100) < 1e-3f)
            {
                minEV100 -= 1f;
                maxEV100 += 1f;
            }

            return new Vector2(minEV100, maxEV100);
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

    internal readonly struct AutoExposureExposureState
    {
        public readonly float currentExposureScale;
        public readonly float targetExposureScale;
        public readonly float averageSceneLuminance;
        public readonly float middleGreyCompensation;

        public AutoExposureExposureState(
            float currentExposureScale,
            float targetExposureScale,
            float averageSceneLuminance,
            float middleGreyCompensation)
        {
            this.currentExposureScale = currentExposureScale;
            this.targetExposureScale = targetExposureScale;
            this.averageSceneLuminance = averageSceneLuminance;
            this.middleGreyCompensation = middleGreyCompensation;
        }

        public Vector4 ToVector4()
        {
            return new Vector4(
                currentExposureScale,
                targetExposureScale,
                averageSceneLuminance,
                middleGreyCompensation);
        }

        public static AutoExposureExposureState FromVector4(Vector4 value)
        {
            return new AutoExposureExposureState(value.x, value.y, value.z, value.w);
        }
    }

    internal static class AutoExposureReferenceSolver
    {
        internal const int HistogramBinCount = 64;

        private const float Epsilon = 1e-4f;

        internal static bool TryResolveAverageSceneLuminance(
            IReadOnlyList<uint> histogram,
            float lowPercent,
            float highPercent,
            float histogramScale,
            float histogramBias,
            out float averageSceneLuminance)
        {
            averageSceneLuminance = AutoExposureSettingsResolver.MiddleGrey;
            if (histogram == null)
                return false;

            var histogramSum = 0f;
            for (var bucketIndex = 0; bucketIndex < HistogramBinCount; bucketIndex++)
                histogramSum += ResolveHistogramBucketValue(histogram, bucketIndex);

            if (histogramSum <= Epsilon)
                return false;

            var minFractionSum = histogramSum * lowPercent;
            var maxFractionSum = histogramSum * highPercent;
            var weightedLogLuminanceSum = 0f;
            var weightedSampleCount = 0f;

            for (var bucketIndex = 0; bucketIndex < HistogramBinCount; bucketIndex++)
            {
                var localValue = ResolveHistogramBucketValue(histogram, bucketIndex);

                var subtractedLow = Mathf.Min(localValue, minFractionSum);
                localValue -= subtractedLow;
                minFractionSum -= subtractedLow;
                maxFractionSum -= subtractedLow;

                localValue = Mathf.Min(localValue, maxFractionSum);
                maxFractionSum -= localValue;

                var histogramPosition = bucketIndex / (float)(HistogramBinCount - 1);
                var logLuminance = ResolveLogLuminanceFromHistogramPosition(histogramPosition, histogramScale, histogramBias);
                weightedLogLuminanceSum += logLuminance * localValue;
                weightedSampleCount += localValue;
            }

            var averageLogLuminance = weightedLogLuminanceSum / Mathf.Max(weightedSampleCount, Epsilon);
            averageSceneLuminance = Mathf.Pow(2f, averageLogLuminance);
            return true;
        }

        internal static AutoExposureExposureState ResolveExposureState(
            IReadOnlyList<uint> histogram,
            in AutoExposureSettingsData settings,
            Vector4 previousExposureState)
        {
            var previousState = AutoExposureExposureState.FromVector4(previousExposureState);
            if (!TryResolveAverageSceneLuminance(
                    histogram,
                    settings.exposureLowPercent,
                    settings.exposureHighPercent,
                    settings.histogramScale,
                    settings.histogramBias,
                    out var averageSceneLuminance))
            {
                return previousState;
            }

            var targetAverageLuminance = Mathf.Clamp(
                averageSceneLuminance,
                settings.minAverageLuminance,
                settings.maxAverageLuminance);
            var targetExposure = targetAverageLuminance / AutoExposureSettingsResolver.MiddleGrey;

            var curveCompensationStops = SampleExposureCompensationCurveStops(averageSceneLuminance, settings);
            var middleGreyExposureCompensation = settings.exposureCompensationSettings * Mathf.Pow(2f, curveCompensationStops);
            var oldExposure = Mathf.Max(previousState.middleGreyCompensation, Epsilon)
                / Mathf.Max(previousState.currentExposureScale, Epsilon);
            var estimatedExposure = ComputeAdaptedExposure(oldExposure, targetExposure, settings);
            var smoothedExposure = Mathf.Clamp(
                estimatedExposure,
                settings.minAverageLuminance / AutoExposureSettingsResolver.MiddleGrey,
                settings.maxAverageLuminance / AutoExposureSettingsResolver.MiddleGrey);

            var smoothedExposureScale = middleGreyExposureCompensation / Mathf.Max(smoothedExposure, Epsilon);
            var targetExposureScale = middleGreyExposureCompensation / Mathf.Max(targetExposure, Epsilon);
            return new AutoExposureExposureState(
                smoothedExposureScale,
                targetExposureScale,
                averageSceneLuminance,
                middleGreyExposureCompensation);
        }

        private static float ResolveHistogramBucketValue(IReadOnlyList<uint> histogram, int bucketIndex)
        {
            if (histogram == null || bucketIndex < 0 || bucketIndex >= histogram.Count)
                return 0f;

            return histogram[bucketIndex];
        }

        private static float ResolveLogLuminanceFromHistogramPosition(float histogramPosition, float histogramScale, float histogramBias)
        {
            return (histogramPosition - histogramBias) / Mathf.Max(histogramScale, Epsilon);
        }

        private static float ComputeAdaptedExposure(float oldExposure, float targetExposure, in AutoExposureSettingsData settings)
        {
            var logTargetExposure = Mathf.Log(Mathf.Max(targetExposure, Epsilon), 2f);
            var logOldExposure = Mathf.Log(Mathf.Max(oldExposure, Epsilon), 2f);
            var logDiff = logTargetExposure - logOldExposure;
            var adaptationSpeed = logDiff > 0f ? settings.exposureSpeedUp : settings.exposureSpeedDown;
            var slopeModifier = logDiff > 0f ? settings.exponentialUpM : settings.exponentialDownM;
            var absLogDiff = Mathf.Abs(logDiff);

            var exponential = ExponentialAdaption(
                logOldExposure,
                logTargetExposure,
                settings.deltaTime,
                adaptationSpeed,
                slopeModifier);
            var linear = LinearAdaption(
                logOldExposure,
                logTargetExposure,
                settings.deltaTime,
                adaptationSpeed);
            var adaptedLogExposure = absLogDiff > settings.startDistance ? linear : exponential;
            var adaptedExposure = Mathf.Pow(2f, adaptedLogExposure);
            return Mathf.Lerp(adaptedExposure, targetExposure, settings.forceTarget);
        }

        private static float ExponentialAdaption(float current, float target, float frameTime, float adaptationSpeed, float slopeModifier)
        {
            var factor = 1f - Mathf.Pow(2f, -frameTime * adaptationSpeed);
            return current + (target - current) * factor * slopeModifier;
        }

        private static float LinearAdaption(float current, float target, float frameTime, float adaptationSpeed)
        {
            var offset = frameTime * adaptationSpeed;
            return current < target
                ? Mathf.Min(target, current + offset)
                : Mathf.Max(target, current - offset);
        }

        private static float SampleExposureCompensationCurveStops(float averageSceneLuminance, in AutoExposureSettingsData settings)
        {
            if (!settings.exposureCompensationCurveEnabled
                || settings.exposureCompensationCurveTexture == null
                || settings.exposureCompensationCurveTexture is not Texture2D curveTexture)
            {
                return 0f;
            }

            var averageSceneEV100 = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(averageSceneLuminance);
            var curveU = Mathf.Clamp01(
                (averageSceneEV100 - settings.exposureCompensationCurveMinEV100)
                * settings.exposureCompensationCurveInvRange);
            return curveTexture.GetPixelBilinear(curveU, 0.5f).r;
        }
    }

    internal readonly struct AutoExposureCompensationCurveTextureData
    {
        public readonly Texture texture;
        public readonly float minEV100;
        public readonly float invRange;
        public readonly bool enabled;

        public AutoExposureCompensationCurveTextureData(Texture texture, float minEV100, float invRange, bool enabled)
        {
            this.texture = texture;
            this.minEV100 = minEV100;
            this.invRange = invRange;
            this.enabled = enabled;
        }
    }

    internal readonly struct AutoExposureCurveMapTextureData
    {
        public readonly Texture texture;
        public readonly float minEV100;
        public readonly float maxEV100;

        public AutoExposureCurveMapTextureData(Texture texture, float minEV100, float maxEV100)
        {
            this.texture = texture;
            this.minEV100 = minEV100;
            this.maxEV100 = maxEV100;
        }
    }

    internal static class AutoExposureCompensationCurveUtility
    {
        private const int CurveSampleCount = 256;

        internal const float DefaultCurveMinEV100 = -16f;
        internal const float DefaultCurveMaxEV100 = 16f;
        internal const float DefaultCurveRange = DefaultCurveMaxEV100 - DefaultCurveMinEV100;

        private static readonly Color[] s_CurveSamples = new Color[CurveSampleCount];

        private static Texture2D s_CurveTexture;
        private static int s_CachedCurveHash;
        private static bool s_HasCachedCurve;
        private static Vector2 s_CachedCurveDomain = new(DefaultCurveMinEV100, DefaultCurveMaxEV100);

        internal static AutoExposureCompensationCurveTextureData Resolve(AnimationCurve curve)
        {
            if (!AutoExposureSettingsResolver.HasExposureCompensationCurve(curve))
            {
                return new AutoExposureCompensationCurveTextureData(
                    Texture2D.blackTexture,
                    DefaultCurveMinEV100,
                    1f / DefaultCurveRange,
                    false);
            }

            EnsureCurveTexture();

            var curveDomain = AutoExposureSettingsResolver.ResolveExposureCompensationCurveDomain(curve);
            var curveHash = ComputeCurveHash(curve, curveDomain);

            if (!s_HasCachedCurve || curveHash != s_CachedCurveHash)
            {
                RebuildCurveTexture(curve, curveDomain);
                s_CachedCurveHash = curveHash;
                s_CachedCurveDomain = curveDomain;
                s_HasCachedCurve = true;
            }

            return new AutoExposureCompensationCurveTextureData(
                s_CurveTexture,
                s_CachedCurveDomain.x,
                1f / Mathf.Max(s_CachedCurveDomain.y - s_CachedCurveDomain.x, 1e-4f),
                true);
        }

        internal static void Dispose()
        {
            CoreUtils.Destroy(s_CurveTexture);
            s_CurveTexture = null;
            s_CachedCurveHash = 0;
            s_HasCachedCurve = false;
            s_CachedCurveDomain = new Vector2(DefaultCurveMinEV100, DefaultCurveMaxEV100);
        }

        private static void EnsureCurveTexture()
        {
            if (s_CurveTexture != null)
                return;

            s_CurveTexture = new Texture2D(CurveSampleCount, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "VividRP Auto Exposure Compensation Curve",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void RebuildCurveTexture(AnimationCurve curve, Vector2 curveDomain)
        {
            for (var sampleIndex = 0; sampleIndex < CurveSampleCount; sampleIndex++)
            {
                var sampleT = sampleIndex / (float)(CurveSampleCount - 1);
                var ev100 = Mathf.Lerp(curveDomain.x, curveDomain.y, sampleT);
                var curveStops = curve.Evaluate(ev100);
                s_CurveSamples[sampleIndex] = new Color(curveStops, 0f, 0f, 0f);
            }

            s_CurveTexture.SetPixels(s_CurveSamples);
            s_CurveTexture.Apply(false, false);
        }

        private static int ComputeCurveHash(AnimationCurve curve, Vector2 curveDomain)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + curveDomain.x.GetHashCode();
                hash = hash * 31 + curveDomain.y.GetHashCode();
                hash = hash * 31 + curve.preWrapMode.GetHashCode();
                hash = hash * 31 + curve.postWrapMode.GetHashCode();

                var keys = curve.keys;
                hash = hash * 31 + keys.Length;

                for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    var key = keys[keyIndex];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + key.weightedMode.GetHashCode();
                }

                return hash;
            }
        }
    }

    internal static class AutoExposureCurveMapUtility
    {
        private const int CurveSampleCount = 256;

        internal const float DefaultCurveMinEV100 = -10f;
        internal const float DefaultCurveMaxEV100 = 10f;

        private static readonly Color[] s_CurveSamples = new Color[CurveSampleCount];

        private static Texture2D s_CurveTexture;
        private static int s_CachedCurveHash;
        private static bool s_HasCachedCurve;
        private static Vector2 s_CachedCurveDomain = new(DefaultCurveMinEV100, DefaultCurveMaxEV100);

        internal static AutoExposureCurveMapTextureData Resolve(
            AnimationCurve curve,
            float clampMinEV100,
            float clampMaxEV100)
        {
            EnsureCurveTexture();

            var curveDomain = AutoExposureSettingsResolver.ResolveCurveMapDomain(curve);
            var resolvedClampMinEV100 = Mathf.Min(clampMinEV100, clampMaxEV100);
            var resolvedClampMaxEV100 = Mathf.Max(resolvedClampMinEV100, clampMaxEV100);
            var curveHash = ComputeCurveHash(
                curve,
                curveDomain,
                resolvedClampMinEV100,
                resolvedClampMaxEV100);

            if (!s_HasCachedCurve || curveHash != s_CachedCurveHash)
            {
                RebuildCurveTexture(
                    curve,
                    curveDomain,
                    resolvedClampMinEV100,
                    resolvedClampMaxEV100);
                s_CachedCurveHash = curveHash;
                s_CachedCurveDomain = curveDomain;
                s_HasCachedCurve = true;
            }

            return new AutoExposureCurveMapTextureData(
                s_CurveTexture,
                s_CachedCurveDomain.x,
                s_CachedCurveDomain.y);
        }

        internal static void Dispose()
        {
            CoreUtils.Destroy(s_CurveTexture);
            s_CurveTexture = null;
            s_CachedCurveHash = 0;
            s_HasCachedCurve = false;
            s_CachedCurveDomain = new Vector2(DefaultCurveMinEV100, DefaultCurveMaxEV100);
        }

        private static void EnsureCurveTexture()
        {
            if (s_CurveTexture != null)
                return;

            s_CurveTexture = new Texture2D(CurveSampleCount, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "VividRP Auto Exposure Curve Map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void RebuildCurveTexture(
            AnimationCurve curve,
            Vector2 curveDomain,
            float resolvedClampMinEV100,
            float resolvedClampMaxEV100)
        {
            for (var sampleIndex = 0; sampleIndex < CurveSampleCount; sampleIndex++)
            {
                var sampleT = sampleIndex / (float)(CurveSampleCount - 1);
                var ev100 = Mathf.Lerp(curveDomain.x, curveDomain.y, sampleT);
                var remappedEV100 = curve == null || curve.length == 0
                    ? ev100
                    : curve.Evaluate(ev100);
                s_CurveSamples[sampleIndex] = new Color(remappedEV100, resolvedClampMinEV100, resolvedClampMaxEV100, 0f);
            }

            s_CurveTexture.SetPixels(s_CurveSamples);
            s_CurveTexture.Apply(false, false);
        }

        private static int ComputeCurveHash(
            AnimationCurve curve,
            Vector2 curveDomain,
            float clampMinEV100,
            float clampMaxEV100)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + curveDomain.x.GetHashCode();
                hash = hash * 31 + curveDomain.y.GetHashCode();
                hash = hash * 31 + clampMinEV100.GetHashCode();
                hash = hash * 31 + clampMaxEV100.GetHashCode();

                if (curve == null || curve.length == 0)
                    return hash;

                hash = hash * 31 + curve.preWrapMode.GetHashCode();
                hash = hash * 31 + curve.postWrapMode.GetHashCode();

                var keys = curve.keys;
                hash = hash * 31 + keys.Length;

                for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    var key = keys[keyIndex];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + key.weightedMode.GetHashCode();
                }

                return hash;
            }
        }
    }
}
