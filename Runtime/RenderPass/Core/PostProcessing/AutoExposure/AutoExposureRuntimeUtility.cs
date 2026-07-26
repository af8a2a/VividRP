using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        public bool histogramUseCurveRemapping;
        public bool centerAroundExposureTarget;
        public Vector2 proceduralCenter;
        public Vector2 proceduralRadii;
        public float proceduralSoftness;
        public float maskMinIntensity;
        public float maskMaxIntensity;

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
                histogramUseCurveRemapping = false,
                centerAroundExposureTarget = false,
                proceduralCenter = new Vector2(0.5f, 0.5f),
                proceduralRadii = new Vector2(0.5f, 0.5f),
                proceduralSoftness = 1f,
                maskMinIntensity = -30f,
                maskMaxIntensity = 30f,
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
        public GraphicsBuffer frameExposureBuffer;
        public GraphicsBuffer preExposureBuffer;
        public GraphicsBuffer histogramBuffer;
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
            frameExposureBuffer = null;
            preExposureBuffer = null;
            histogramBuffer = null;
            previousExposureTexture = null;
            currentExposureTexture = null;
            exposureEnabled = false;
            autoExposureEnabled = false;
            hasValidHistory = false;
        }
    }

    internal sealed class AutoExposureHistoryState : CameraRelativeState
    {
        public CameraHistoryBuffer exposureBufferHistory;
        public CameraHistoryTexture exposureTextureHistory;
        public bool hasValidHistory;
        public bool wasEnabledLastFrame;
        public AutoExposureMode lastMode;
        public AutoExposureImplementationPath lastImplementation;

        public override void Dispose()
        {
            exposureBufferHistory = null;
            exposureTextureHistory = null;
            hasValidHistory = false;
            wasEnabledLastFrame = false;
            lastMode = AutoExposureMode.Histogram;
            lastImplementation = AutoExposureImplementationPath.Unreal;
        }
    }

    internal sealed class AutoExposureHistorySystem : CameraRelativeSystem<AutoExposureHistoryState>
    {
    }

    internal sealed class VividAutoExposureSystem : VividSubsystem<VividAutoExposureSystem>
    {
        private const int AutoExposureVectorStride = sizeof(float) * 4;

        internal static readonly int PreExposureBufferId = Shader.PropertyToID("_VividAutoExposurePreExposureBuffer");

        private static readonly AutoExposureHistorySystem s_HistorySystem = new();
        private static readonly Vector4[] s_ExposureBufferData = new Vector4[1];

        private static GraphicsBuffer s_DefaultExposureBuffer;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemAutoExposureMarker.Auto())
            {
                if (frameData == null || cmd == null)
                    return;

                PrepareFrame(frameData);
                BindFrameGlobals(cmd, frameData.Get<VividExposureData>());
            }
        }

        public new static void Deinitialize()
        {
            VividSubsystem<VividAutoExposureSystem>.Deinitialize();

#if UNITY_EDITOR
            // Keep the FrameContext callback wired in editor so the next preview render lazily
            // rebuilds exposure resources after Clear() releases the previous frame state.
            EnsurePreRenderSubscribed();
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
#endif
        }

        private static void OnSubsystemDispose()
        {
            Deinitialize();
        }

        internal static void PrepareFrame(ContextContainer frameData)
        {
            if (!IsInitialized)
                Initialize();

            if (frameData == null)
                return;

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

            EnsureDefaultExposureBuffer();
            s_HistorySystem.PurgeDestroyedCameras();

            var settings = postProcessingAllowed
                ? AutoExposureSettingsResolver.Resolve(
                    camera,
                    temporalData != null && temporalData.isFirstFrame,
                    implementation)
                : AutoExposureSettingsData.CreateDefault();
            settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(settings, camera);
            var hasAutoExposureCompute = AutoExposureImplementationUtility.SupportsDispatch(
                autoExposureCompute,
                implementation,
                settings.exposureMode);

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
                PrepareAutoExposureHistory(state, camera);

                if (!state.wasEnabledLastFrame
                    || state.lastMode != settings.mode
                    || state.lastImplementation != implementation)
                {
                    state.hasValidHistory = false;
                    settings.forceTarget = 1f;
                }

                if (settings.mode == AutoExposureMode.Manual
                    && state.exposureBufferHistory?.GetCurrent() != null)
                {
                    WriteExposureBuffer(
                        state.exposureBufferHistory.GetCurrent(),
                        settings.fixedExposureScale,
                        settings.manualAverageSceneLuminance,
                        settings.exposureCompensationAll);
                    state.exposureBufferHistory.MarkWritten();
                    state.hasValidHistory = true;
                    state.wasEnabledLastFrame = true;
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
            var bufferHistory = exposureEnabled ? state?.exposureBufferHistory : null;
            var textureHistory = exposureEnabled ? state?.exposureTextureHistory : null;
            var hasValidBufferHistory = bufferHistory?.IsValid() == true;
            var hasValidTextureHistory = textureHistory?.IsValid() == true;
            var hasValidHistory = exposureEnabled
                && state != null
                && state.hasValidHistory
                && hasValidBufferHistory
                && (implementation != AutoExposureImplementationPath.HDRP || hasValidTextureHistory);
            var previousExposureBuffer = exposureEnabled && bufferHistory?.GetPrevious() != null
                ? bufferHistory.GetPrevious()
                : defaultExposureBuffer;
            var currentExposureBuffer = exposureEnabled && bufferHistory?.GetCurrent() != null
                ? bufferHistory.GetCurrent()
                : defaultExposureBuffer;
            var frameExposureBuffer = exposureEnabled
                ? settings.mode == AutoExposureMode.Manual
                    ? currentExposureBuffer ?? previousExposureBuffer ?? defaultExposureBuffer
                    : hasValidHistory
                        ? previousExposureBuffer ?? defaultExposureBuffer
                        : defaultExposureBuffer
                : defaultExposureBuffer;
            var preExposureBuffer = exposureEnabled
                && settings.mode == AutoExposureMode.Manual
                && bufferHistory?.GetCurrent() != null
                ? bufferHistory.GetCurrent()
                : hasValidHistory && bufferHistory?.GetPrevious() != null
                    ? bufferHistory.GetPrevious()
                    : defaultExposureBuffer;
            var previousExposureTexture = exposureEnabled && textureHistory?.GetPrevious()?.rt != null
                ? textureHistory.GetPrevious().rt
                : null;
            var currentExposureTexture = exposureEnabled && textureHistory?.GetCurrent()?.rt != null
                ? textureHistory.GetCurrent().rt
                : null;

            exposureData.settings = settings;
            exposureData.implementation = implementation;
            exposureData.defaultExposureBuffer = defaultExposureBuffer;
            exposureData.previousExposureBuffer = previousExposureBuffer;
            exposureData.currentExposureBuffer = currentExposureBuffer;
            exposureData.frameExposureBuffer = frameExposureBuffer;
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

            state.exposureBufferHistory?.MarkWritten();
            if (state.lastImplementation == AutoExposureImplementationPath.HDRP)
                state.exposureTextureHistory?.MarkWritten();
            state.hasValidHistory = true;
            state.wasEnabledLastFrame = true;
        }

        protected override void OnDeinitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
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

        internal static GraphicsBuffer ResolvePreExposureBuffer(VividExposureData exposureData)
        {
            return exposureData?.preExposureBuffer
                ?? exposureData?.defaultExposureBuffer
                ?? GetOrCreateDefaultExposureBuffer();
        }

        private static void BindFrameGlobals(CommandBuffer cmd, VividExposureData exposureData)
        {
            if (cmd == null)
                return;

            var preExposureBuffer = ResolvePreExposureBuffer(exposureData);
            if (preExposureBuffer != null)
                cmd.SetGlobalBuffer(PreExposureBufferId, preExposureBuffer);
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

        private static void PrepareAutoExposureHistory(AutoExposureHistoryState state, Camera camera)
        {
            if (state == null || camera == null)
                return;

            var cameraHistory = camera.GetVividCameraHistory();
            if (!cameraHistory.IsFrameActive)
            {
                state.exposureBufferHistory = null;
                state.exposureTextureHistory = null;
                return;
            }

            state.exposureBufferHistory = cameraHistory.GetOrCreateBuffer(
                CameraHistoryIds.AutoExposureBuffer,
                2,
                new CameraHistoryBufferDescriptor(
                    1,
                    AutoExposureVectorStride,
                    GraphicsBuffer.Target.Structured),
                AllocateAutoExposureBuffer);
            state.exposureTextureHistory = cameraHistory.GetOrCreateTexture(
                CameraHistoryIds.AutoExposureTexture,
                2,
                new CameraHistoryTextureDescriptor(
                    1,
                    1,
                    GraphicsFormat.R32G32_SFloat,
                    filterMode: FilterMode.Point,
                    wrapMode: TextureWrapMode.Clamp,
                    enableRandomWrite: true));
        }

        private static GraphicsBuffer AllocateAutoExposureBuffer(
            in CameraHistoryBufferDescriptor descriptor,
            string resourceName,
            int resourceIndex)
        {
            var buffer = new GraphicsBuffer(
                descriptor.Target,
                descriptor.UsageFlags,
                descriptor.Count,
                descriptor.Stride)
            {
                name = resourceName,
            };
            WriteExposureBuffer(buffer, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f);
            return buffer;
        }

        private static void WriteExposureBuffer(GraphicsBuffer buffer, float exposureScale, float averageSceneLuminance, float middleGreyCompensation)
        {
            if (buffer == null)
                return;

            s_ExposureBufferData[0] = new Vector4(exposureScale, exposureScale, averageSceneLuminance, middleGreyCompensation);
            buffer.SetData(s_ExposureBufferData);
        }
    }

    internal static partial class AutoExposureSettingsResolver
    {
        internal const float MiddleGrey = 0.18f;
        internal const float DefaultStartDistance = 1.5f;
        internal const float DefaultLensAttenuation = 0.78f;

        private const float IsoSaturationSpeedConstant = 0.78f;
        private const float PercentToScale = 0.01f;
        private const float MinSpeed = 0.001f;
        private const float FrameTimeEpsilon = 1f / 60f;

        internal static AutoExposureSettingsData Resolve(
            Camera camera,
            bool isFirstFrame,
            AutoExposureImplementationPath implementation)
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var autoExposure = stack.GetComponent<AutoExposure>();
            if (autoExposure == null)
                return settings;

            return implementation == AutoExposureImplementationPath.HDRP
                ? ResolveHDRP(autoExposure, camera, isFirstFrame)
                : ResolveUnreal(autoExposure, camera, isFirstFrame);
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
            for (var keyIndex = 0; keyIndex < curve.length; keyIndex++)
            {
                if (Mathf.Abs(curve[keyIndex].value) > 1e-3f)
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

            var firstKey = curve[0];
            var minEV100 = firstKey.time;
            var maxEV100 = firstKey.time;

            for (var keyIndex = 1; keyIndex < curve.length; keyIndex++)
            {
                var key = curve[keyIndex];
                minEV100 = Mathf.Min(minEV100, key.time);
                maxEV100 = Mathf.Max(maxEV100, key.time);
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

                hash = hash * 31 + curve.length;

                for (var keyIndex = 0; keyIndex < curve.length; keyIndex++)
                {
                    var key = curve[keyIndex];
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

                hash = hash * 31 + curve.length;

                for (var keyIndex = 0; keyIndex < curve.length; keyIndex++)
                {
                    var key = curve[keyIndex];
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
