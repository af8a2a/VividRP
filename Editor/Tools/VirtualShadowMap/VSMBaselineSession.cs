using VividRP.Runtime.VirtualShadowMap;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    internal sealed class VSMBaselineSession : IDisposable
    {
        internal const int TimingSettleFrames = 8;
        internal static readonly string[] StageNames =
        {
            "VSM.LayoutRemap", "VSM.MarkReceiverPages", "VSM.Allocate", "VSM.InvalidateStatic", "VSM.ClearPhysicalPages",
            "VSM.StaticCasterCull", "VSM.DynamicCasterCull", "VSM.PageCull", "VSM.StaticRaster",
            "VSM.DynamicRaster", "VSM.UnityCompatibilityRaster", "VSM.FinalizePages", "VSM.ResetFeedback", "VSM.Resolve",
        };
        private const int StageOffset = 5;
        private static readonly int[] s_RequiredStages = { 0, 1, 2, 4, 9, 11, 13 };
        private static readonly string[] s_MetricNames = BuildMetricNames();
        private enum PauseKind { None, User, Editor, Camera, Timing, Error }
        private PauseKind m_Pause;
        private readonly Camera m_Camera;
        private readonly bool m_PreviousRunInBackground;
        private readonly VSMBaselineCase[] m_Cases;
        private readonly float m_WarmupSeconds, m_MeasurementSeconds;
        private readonly int m_MinSamples;
        private readonly VSMBaselineSamples m_Samples;
        private readonly VSMBaselineSamplingClock m_Clock = new VSMBaselineSamplingClock();
        private readonly double[] m_Values = new double[StageOffset + StageNames.Length];
        private readonly FrameTiming[] m_Timings = new FrameTiming[1];
        private readonly ProfilerRecorder[] m_GpuRecorders = new ProfilerRecorder[StageNames.Length];
        private ProfilerRecorder m_GcRecorder;
        private readonly Action<ScriptableRenderContext, Camera> m_EndCameraCallback;
        private GameObject m_VolumeObject;
        private VolumeProfile m_Profile;
        private CascadedShadowSettingsVolume m_Settings;
        private int m_CaseIndex, m_LastTickFrame = -1, m_CaseStartFrame, m_CollectAfterFrame;
        private int m_LastCameraFrame = -1, m_CameraSerial, m_LastObservedCameraSerial;
        private double m_CaseStartTime, m_LastCameraTime, m_LastGpuTimingTime;
        private ulong m_LastTimingTimestamp;
        private bool m_Measuring, m_PendingEnd, m_EndReady, m_Disposed, m_WaitingForInitialTiming;
        private bool m_RequireFrameTimings;
        private CaseResult m_Result;
        private readonly RunInfo m_Run;
        private Light m_LastMainLight;
        private int m_WindowRepaintCount;
        private double m_LastWindowRepaintTime = double.NaN;
        internal VSMBaselineRepaintMode RepaintMode => HasCurrentCase ? m_Cases[m_CaseIndex].recorderRepaint : VSMBaselineRepaintMode.FourHz;
        internal void RecordWindowRepaint()
        {
            m_WindowRepaintCount++;
            m_LastWindowRepaintTime = EditorApplication.timeSinceStartup;
        }
        internal static double RepaintInterval(VSMBaselineRepaintMode mode) =>
            mode == VSMBaselineRepaintMode.Manual ? double.PositiveInfinity : mode == VSMBaselineRepaintMode.OneHz ? 1 : 0.25;
        internal string OutputDirectory { get; }
        internal string ReportText { get; private set; } = "";
        internal string LastError { get; private set; } = "";
        internal bool LastSaveSucceeded { get; private set; }
        internal bool IsFinished { get; private set; }
        internal bool IsPaused => m_Pause != PauseKind.None;
        internal bool HasCurrentCase => m_CaseIndex < m_Cases.Length;
        internal int CaseIndex => m_CaseIndex;
        internal int CaseCount => m_Cases.Length;
        internal int SampleCount => m_Samples.Count;
        internal int GpuSampleCount => m_Samples.ValidCounts[2];
        internal int ResolveSampleCount => m_Samples.ValidCounts[StageOffset + 12];
        internal double MeasuredSeconds => m_Clock.ElapsedSeconds;
        internal double WarmupRemaining => Math.Max(0, m_WarmupSeconds - (Time.unscaledTimeAsDouble - m_CaseStartTime));
        internal bool CanAllowMissingTiming => !IsFinished && m_RequireFrameTimings && (m_WaitingForInitialTiming || m_Pause == PauseKind.Timing);
        internal string CurrentCaseName => HasCurrentCase ? m_Cases[m_CaseIndex].name : "All cases processed";
        internal string Phase
        {
            get
            {
                if (IsFinished) return m_Run.status;
                switch (m_Pause)
                {
                    case PauseKind.User: return "Paused — samples retained";
                    case PauseKind.Editor: return "Play Mode paused — samples retained";
                    case PauseKind.Camera: return "Waiting for camera — keep Game view rendering";
                    case PauseKind.Timing: return "Waiting for fresh GPU frame timings";
                    case PauseKind.Error: return "Paused after error — samples retained";
                }
                if (m_WaitingForInitialTiming) return "Waiting for first valid GPU frame timing";
                if (!m_Measuring) return "Warming up";
                if (Time.frameCount < m_CollectAfterFrame) return "Settling after snapshot";
                return m_PendingEnd ? "Finishing current case" : "Sampling";
            }
        }
        internal string GetCaseStatus(int index) => m_Run.results[index].status;

        [Serializable]
        private sealed class RunInfo
        {
            public int schemaVersion = 3;
            public string startedUtc, finishedUtc, status, reason, lastError, revision, unityVersion, operatingSystem, gpu, graphicsApi, graphicsDriver, pipelineAsset;
            public int gpuMemoryMB, vSyncCount, targetFrameRate, minSamplesPerCase, maxSamplesPerCase, completedCases, processedCases, comparableCases, currentCase;
            public float warmupSeconds, measurementSeconds, qualityShadowDistance;
            public bool frameTimingEnabled, requireFrameTimings, d3d12DebugStartupFlag;
            public string projectPath;
            public string frameTimingSource = "editor_frames_unattributed";
            public string qualityStatus = "not_validated";
            public VSMBaselineCase[] cases;
            public CaseResult[] results;
            public VSMBaselineSceneSnapshot initialScene;
            public string timingScope = "Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. " +
                "FrameTiming/GPU results are delayed and not synchronized to the observation frame. " +
                "Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.";
        }

        [Serializable]
        private sealed class MetricReport
        {
            public string name, availability, median, p95, min, max;
            public int validSamples;
        }

        [Serializable]
        private sealed class CaseResult
        {
            public VSMBaselineCase settings;
            public string status, reason, mainLightName, mainLightScenePath, pageCounterError;
            public int attempt, observations, selectedCameraFrames, vsmActiveFrames, settingsMismatchFrames, longFramesOver100ms;
            public int effectiveResolution, clipmapLevels, physicalPageCapacity, renderWidth, renderHeight, segments;
            public int duplicateTimings, unavailableTimings, regressedTimings;
            public bool cameraMoved, outputSizeChanged, finalPageCountersAvailable, samplingGoalMet, comparable, timingCoveragePassed;
            public int gpuTimingsBelow1ms, observedWindowRepaints;
            public string frameTimingSource = "editor_frames_unattributed";
            public string residencyStatus = "not_captured";
            public string qualityStatus = "not_validated";
            public double lowGpuTimingMedianIntervalSeconds;
            public double measuredSeconds;
            public VSMBaselineSceneSnapshot start, end;
            public List<VSMBaselineSceneSnapshot> segmentStarts = new List<VSMBaselineSceneSnapshot>();
            public uint[] finalPageCounters = new uint[4];
            public MetricReport[] metrics;
            public List<string> issues = new List<string>();
        }

        [Serializable]
        private sealed class Event
        {
            public string utc, action, reason;
            public int caseIndex, attempt, observations;
            public double measuredSeconds;
        }

        internal VSMBaselineSession(Camera camera, VSMBaselineCase[] cases, float warmupSeconds,
            float measurementSeconds, int minSamples, int maxSamples, string outputDirectory, string revision, bool requireFrameTimings = true)
        {
            if (!EditorApplication.isPlaying || !(RenderPipelineManager.currentPipeline is VividRenderPipeline))
                throw new InvalidOperationException("Start the recorder in VividRP Play Mode.");
            if (VSMQualityReproduction.IsRunning) throw new InvalidOperationException("Finish quality capture before performance recording.");
            if (camera == null || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game)
                throw new ArgumentException("Assign an active Game camera (or tag one MainCamera).");
            if (cases == null || cases.Length == 0 || !float.IsFinite(warmupSeconds) || warmupSeconds < 0
                || !float.IsFinite(measurementSeconds) || measurementSeconds <= 0 || minSamples < 1
                || maxSamples < minSamples || maxSamples > 300000)
                throw new ArgumentException("Specify cases, finite durations and a sample capacity between 1 and 300000.");
            m_Camera = camera; m_WarmupSeconds = warmupSeconds; m_MeasurementSeconds = measurementSeconds;
            m_PreviousRunInBackground = Application.runInBackground;
            m_MinSamples = minSamples; m_RequireFrameTimings = requireFrameTimings;
            m_Cases = new VSMBaselineCase[cases.Length];
            for (int i = 0; i < cases.Length; i++)
            {
                if (cases[i] == null) throw new ArgumentException("Remove empty case entries.");
                cases[i].Validate(); m_Cases[i] = cases[i].Copy();
            }
            m_Samples = new VSMBaselineSamples(maxSamples, m_Values.Length);
            string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            OutputDirectory = Path.Combine(Path.GetFullPath(Path.IsPathRooted(outputDirectory)
                    ? outputDirectory : Path.Combine(projectDirectory, outputDirectory)),
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            m_EndCameraCallback = OnEndCamera;
            m_Run = new RunInfo
            {
                startedUtc = DateTime.UtcNow.ToString("O"), status = "recording", revision = revision,
                unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem,
                gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDriver = SystemInfo.graphicsDeviceVersion, gpuMemoryMB = SystemInfo.graphicsMemorySize,
                pipelineAsset = AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline), qualityShadowDistance = QualitySettings.shadowDistance,
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                warmupSeconds = warmupSeconds, measurementSeconds = measurementSeconds, minSamplesPerCase = minSamples, maxSamplesPerCase = maxSamples,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), cases = m_Cases,
                projectPath = projectDirectory,
                d3d12DebugStartupFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-force-d3d12-debug") >= 0,
                initialScene = VSMBaselineSceneSnapshot.Capture(camera),
                requireFrameTimings = requireFrameTimings, results = new CaseResult[cases.Length],
            };
            for (int i = 0; i < m_Cases.Length; i++)
                m_Run.results[i] = new CaseResult { settings = m_Cases[i], status = "not_started" };
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                CreateOverride();
                StartRecorders();
                Application.runInBackground = true;
                RenderPipelineManager.endCameraRendering += m_EndCameraCallback;
                m_LastCameraTime = EditorApplication.timeSinceStartup;
                BeginCase(); WriteRun();
            }
            catch { Dispose(); throw; }
        }

        private void CreateOverride()
        {
            LayerMask mask = VividVolumeManagerUtility.ResolveVolumeLayerMask(m_Camera,
                m_Camera.GetComponent<VividAdditionalCameraData>());
            int layer = 0;
            while (layer < 32 && (mask.value & (1 << layer)) == 0) layer++;
            if (layer == 32) throw new InvalidOperationException("The selected camera has an empty Volume layer mask.");
            var stack = VolumeManager.instance.CreateStack();
            try
            {
                VolumeManager.instance.Update(stack, m_Camera.transform, mask);
                m_Settings = Object.Instantiate(stack.GetComponent<CascadedShadowSettingsVolume>());
            }
            finally { VolumeManager.instance.DestroyStack(stack); }
            m_Settings.hideFlags = HideFlags.HideAndDontSave;
            m_Settings.SetAllOverridesTo(true);
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.hideFlags = HideFlags.HideAndDontSave;
            m_Profile.components.Add(m_Settings);
            m_VolumeObject = new GameObject("VSM Baseline Temporary Override") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            m_VolumeObject.SetActive(false);
            var volume = m_VolumeObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = float.MaxValue; volume.weight = 1;
            volume.sharedProfile = m_Profile;
            m_VolumeObject.SetActive(true);
        }

        private void StartRecorders()
        {
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.StartImmediately
                | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame;
            for (int i = 0; i < StageNames.Length; i++)
                if (!m_GpuRecorders[i].Valid)
                    m_GpuRecorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, StageNames[i], 1,
                        options | ProfilerRecorderOptions.GpuRecorder);
            if (!m_GcRecorder.Valid)
                m_GcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1, options);
        }

        private void BeginCase()
        {
            m_Cases[m_CaseIndex].Apply(m_Settings);
            m_Samples.Clear(); m_Clock.Reset(); m_LastMainLight = null;
            m_Result = m_Run.results[m_CaseIndex];
            m_LastTimingTimestamp = 0; m_LastGpuTimingTime = EditorApplication.timeSinceStartup;
            BeginWarmup();
            WriteEvent("case_started", "");
        }

        private void BeginWarmup()
        {
            m_Pause = PauseKind.None;
            m_Measuring = m_PendingEnd = m_EndReady = m_WaitingForInitialTiming = false;
            m_CaseStartTime = Time.unscaledTimeAsDouble; m_CaseStartFrame = Time.frameCount;
            m_CollectAfterFrame = int.MaxValue;
            m_LastObservedCameraSerial = m_CameraSerial;
            m_Result.status = "warming_up"; m_Result.reason = "";
            m_Result.samplingGoalMet = m_Result.comparable = m_Result.finalPageCountersAvailable = false;
            m_Result.pageCounterError = "";
            Array.Clear(m_Result.finalPageCounters, 0, m_Result.finalPageCounters.Length);
            m_Run.status = "recording"; m_Run.reason = ""; m_Run.finishedUtc = null;
        }

        internal bool Tick()
        {
            if (IsFinished || m_Disposed) return false;
            if (m_Pause == PauseKind.User || m_Pause == PauseKind.Error) return false;
            if (!HasCurrentCase) return true;
            if (EditorApplication.isPaused) { Pause(PauseKind.Editor, "play_mode_paused"); return false; }
            if (m_EndReady)
            {
                CompleteCase("completed");
                return !HasCurrentCase;
            }
            bool cameraAvailable = m_Camera != null && m_Camera.isActiveAndEnabled
                && RenderPipelineManager.currentPipeline is VividRenderPipeline
                && EditorApplication.timeSinceStartup - m_LastCameraTime < 1;
            if (!cameraAvailable)
            {
                Pause(PauseKind.Camera, "Target camera is not rendering. Open its Game view; this session will resume after warmup.");
                return false;
            }
            int frame = Time.frameCount;
            if (frame == m_LastTickFrame) return false;
            m_LastTickFrame = frame;
            FrameTimingManager.CaptureFrameTimings();
            uint timingCount = FrameTimingManager.GetLatestTimings(1, m_Timings);
            ulong timestamp = timingCount > 0 ? m_Timings[0].frameStartTimestamp : 0;
            bool freshTiming = timestamp != 0 && timestamp > m_LastTimingTimestamp;
            bool freshGpu = freshTiming && PositiveTiming(m_Timings[0].gpuFrameTime) > 0;
            bool regressed = timestamp != 0 && timestamp < m_LastTimingTimestamp;
            if (freshTiming) m_LastTimingTimestamp = timestamp;
            if (freshGpu) m_LastGpuTimingTime = EditorApplication.timeSinceStartup;
            if (m_Pause == PauseKind.Timing && m_RequireFrameTimings && !freshGpu) return false;
            if (IsPaused)
            {
                if (m_CameraSerial == m_LastObservedCameraSerial) return false;
                Resume();
                return false;
            }
            if (m_PendingEnd || m_CameraSerial == m_LastObservedCameraSerial) return false;
            m_LastObservedCameraSerial = m_CameraSerial;
            double now = Time.unscaledTimeAsDouble;
            if (!m_Measuring)
            {
                if (now - m_CaseStartTime < m_WarmupSeconds || frame - m_CaseStartFrame < TimingSettleFrames
                    || m_LastCameraFrame < m_CaseStartFrame) return false;
                if (m_RequireFrameTimings && !freshGpu)
                {
                    if (!m_WaitingForInitialTiming)
                    {
                        m_WaitingForInitialTiming = true;
                        m_Result.status = "waiting_for_timings";
                        m_Result.reason = "Frame Timing Stats is enabled but no fresh GPU timing has arrived, or the API does not provide it.";
                        WriteEvent("waiting_for_timings", m_Result.reason); SaveCheckpoint();
                    }
                    return false;
                }
                var snapshot = VSMBaselineSceneSnapshot.Capture(m_Camera);
                if (m_Result.start == null) m_Result.start = snapshot;
                m_Result.segmentStarts.Add(snapshot);
                StartRecorders();
                m_Clock.BeginSegment();
                m_Result.status = "recording"; m_Measuring = true; m_WaitingForInitialTiming = false;
                m_CollectAfterFrame = frame + TimingSettleFrames;
                WriteEvent("segment_started", "");
                return false;
            }
            if (m_RequireFrameTimings && EditorApplication.timeSinceStartup - m_LastGpuTimingTime > 1)
            {
                Pause(PauseKind.Timing, "No fresh GPU frame timing for one second. Samples retained; resuming requires warmup.");
                return false;
            }
            if (frame < m_CollectAfterFrame) return false;
            for (int i = 0; i < m_Values.Length; i++) m_Values[i] = double.NaN;
            m_Values[0] = Time.unscaledDeltaTime * 1000.0;
            if (freshTiming)
            {
                m_Values[1] = PositiveTiming(m_Timings[0].cpuFrameTime);
                m_Values[2] = PositiveTiming(m_Timings[0].gpuFrameTime);
                m_Values[3] = PositiveTiming(m_Timings[0].cpuRenderThreadFrameTime);
            }
            if (m_GcRecorder.Valid && m_GcRecorder.Count > 0) m_Values[4] = m_GcRecorder.LastValue;
            for (int i = 0; i < m_GpuRecorders.Length; i++)
            {
                var recorder = m_GpuRecorders[i];
                if (!recorder.Valid || recorder.Count == 0) continue;
                var sample = recorder.GetSample(0);
                if (sample.Count > 0 && sample.Value >= 0) m_Values[StageOffset + i] = sample.Value * 1e-6;
            }
            m_Clock.Observe(now);
            m_Samples.Add(frame, now, freshTiming ? timestamp : 0, m_Values, m_Clock.Segment,
                m_LastCameraFrame, m_CameraSerial, m_WindowRepaintCount, EditorApplication.timeSinceStartup - m_LastWindowRepaintTime);
            if (m_Values[0] > 100) m_Result.longFramesOver100ms++;
            if (timestamp == 0) m_Result.unavailableTimings++;
            else if (regressed) m_Result.regressedTimings++;
            else if (!freshTiming) m_Result.duplicateTimings++;
            m_Result.measuredSeconds = m_Clock.ElapsedSeconds;
            bool goal = m_Clock.ElapsedSeconds >= m_MeasurementSeconds && m_Samples.Count >= m_MinSamples
                && (!m_RequireFrameTimings || GpuSampleCount >= m_MinSamples);
            if (goal || m_Samples.Count == m_Samples.Capacity)
            {
                m_Result.samplingGoalMet = goal;
                m_Result.status = goal ? "completed" : "sample_capacity_reached";
                m_PendingEnd = true;
            }
            return false;
        }

        internal void PauseAndSave() => Pause(PauseKind.User, "user_paused_and_saved");

        private void Pause(PauseKind kind, string reason)
        {
            if (IsFinished || m_Pause == kind) return;
            LastSaveSucceeded = false;
            m_Pause = kind; m_PendingEnd = m_EndReady = false;
            m_LastObservedCameraSerial = m_CameraSerial;
            m_Run.status = "paused"; m_Run.reason = reason;
            if (m_Result != null) { m_Result.status = "paused"; m_Result.reason = reason; }
            WriteEvent("paused", reason);
            SaveCheckpoint();
        }

        internal void Resume()
        {
            if (IsFinished || !HasCurrentCase) return;
            if (m_Camera == null || !m_Camera.isActiveAndEnabled || EditorApplication.isPaused)
                return;
            BeginWarmup();
            WriteEvent("resumed", "New segment after warmup; previous samples and active measurement time retained.");
            SaveCheckpoint();
        }

        internal void AllowMissingFrameTimings()
        {
            m_RequireFrameTimings = m_Run.requireFrameTimings = false;
            WriteEvent("allow_missing_frame_timings", "User chose to continue; missing data stays blank and prevents comparable status.");
            Resume();
        }

        internal void PauseAfterError(Exception exception)
        {
            LastSaveSucceeded = false;
            LastError = m_Run.lastError = exception.ToString();
            m_Run.reason = exception.Message;
            if (m_Pause == PauseKind.Error) return;
            Pause(PauseKind.Error, exception.Message);
        }

        internal void RetryCurrentCase()
        {
            if (!HasCurrentCase || IsFinished) return;
            PauseAndSave();
            SaveCheckpoint();
            string prefix = "case_" + m_CaseIndex.ToString("D3");
            string folder = Path.Combine(OutputDirectory, "attempts", prefix + "_attempt_" + m_Result.attempt.ToString("D3"));
            Directory.CreateDirectory(folder);
            foreach (string suffix in new[] { ".json", "_samples.csv", "_summary.csv" })
                File.Copy(Path.Combine(OutputDirectory, prefix + suffix), Path.Combine(folder, prefix + suffix), true);
            int attempt = m_Result.attempt + 1;
            m_Run.results[m_CaseIndex] = new CaseResult { settings = m_Cases[m_CaseIndex], status = "not_started", attempt = attempt };
            BeginCase(); SaveCheckpoint();
        }

        internal bool SkipCurrentCase()
        {
            if (!HasCurrentCase || IsFinished) return !HasCurrentCase;
            m_Pause = PauseKind.User; m_PendingEnd = m_EndReady = false;
            CompleteCase("skipped_by_user");
            return !HasCurrentCase;
        }

        private static double PositiveTiming(double value) => value > 0 && double.IsFinite(value) ? value : double.NaN;

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera || m_Disposed || !(RenderPipelineManager.currentPipeline is VividRenderPipeline)) return;
            var frameData = PassRecorder.GetFrameData();
            if (frameData == null || !frameData.Contains<VividCameraData>() || !frameData.Contains<VividLightData>()) return;
            var cameraData = frameData.Get<VividCameraData>();
            if (cameraData.camera != camera) return;
            m_LastCameraFrame = Time.frameCount; m_LastCameraTime = EditorApplication.timeSinceStartup; m_CameraSerial++;
            if (!m_Measuring || IsPaused || m_EndReady || Time.frameCount < m_CollectAfterFrame || EditorApplication.isPaused) return;
            bool active = VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(
                EntityId.ToULong(camera.GetEntityId()), cameraData.frameIndex);
            m_LastMainLight = frameData.Get<VividLightData>().mainVisibleLight.light;
            m_Result.selectedCameraFrames++;
            if (active) m_Result.vsmActiveFrames++;
            if (!m_Result.settings.Matches(VividVolumeManagerUtility.GetCascadedShadowSettingsVolume()))
                m_Result.settingsMismatchFrames++;
            m_Result.effectiveResolution = VirtualShadowMapPrototypeRuntime.VirtualResolution;
            m_Result.clipmapLevels = VirtualShadowMapPrototypeRuntime.Projections.Count;
            m_Result.physicalPageCapacity = VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity;
            if (m_Result.selectedCameraFrames > 1)
                m_Result.outputSizeChanged |= m_Result.renderWidth != cameraData.actualWidth || m_Result.renderHeight != cameraData.actualHeight;
            m_Result.renderWidth = cameraData.actualWidth; m_Result.renderHeight = cameraData.actualHeight;
            m_Result.cameraMoved |= (camera.transform.position - m_Result.start.camera.position).sqrMagnitude > 1e-8f
                || Quaternion.Angle(camera.transform.rotation, m_Result.start.camera.rotation) > 0.001f;
            m_Result.outputSizeChanged |= camera.pixelWidth != m_Result.start.camera.pixelWidth
                || camera.pixelHeight != m_Result.start.camera.pixelHeight;
            if (!m_PendingEnd) return;
            if (active)
            {
                try
                {
                    VirtualShadowMapPrototypeRuntime.AllocatorCounters.GetData(m_Result.finalPageCounters);
                    m_Result.finalPageCountersAvailable = true;
                }
                catch (Exception exception) { m_Result.pageCounterError = exception.ToString(); }
            }
            m_EndReady = true;
        }

        private void CompleteCase(string status)
        {
            if (status != "completed" || !m_Result.samplingGoalMet) m_Result.samplingGoalMet = false;
            m_Result.status = status == "completed" && !m_Result.samplingGoalMet ? "sample_capacity_reached" : status;
            SaveCurrentCase();
            WriteEvent("case_finished", m_Result.status);
            m_CaseIndex++; m_Result = null;
            if (HasCurrentCase) BeginCase();
            WriteRun();
        }

        private void SaveCheckpoint()
        {
            LastSaveSucceeded = false;
            SaveCurrentCase();
            WriteRun();
            LastSaveSucceeded = true;
        }

        private void SaveCurrentCase()
        {
            if (m_Result == null) return;
            m_Result.observations = m_Samples.Count;
            m_Result.measuredSeconds = m_Clock.ElapsedSeconds; m_Result.segments = m_Clock.Segment;
            m_Result.end = VSMBaselineSceneSnapshot.Capture(m_Camera);
            if (m_LastMainLight != null)
            { m_Result.mainLightName = m_LastMainLight.name; m_Result.mainLightScenePath = m_LastMainLight.gameObject.scene.path; }
            m_Result.issues.Clear();
            if (!m_Result.samplingGoalMet) m_Result.issues.Add("Measurement duration / minimum samples not completed.");
            if (m_Result.selectedCameraFrames == 0 || m_Result.vsmActiveFrames != m_Result.selectedCameraFrames)
                m_Result.issues.Add("Selected-camera VSM was inactive or missing on one or more rendered frames.");
            if (m_Result.settingsMismatchFrames > 0) m_Result.issues.Add("Requested settings did not match effective Volume settings.");
            if (m_Result.cameraMoved || m_Result.outputSizeChanged) m_Result.issues.Add("Camera or output changed during sampling.");
            if (m_Result.longFramesOver100ms > 0) m_Result.issues.Add("Frame intervals over 100 ms retained; investigate before comparing.");
            if (!string.IsNullOrEmpty(m_Result.pageCounterError)) m_Result.issues.Add("Final page counter readback failed; see pageCounterError.");
            m_Result.metrics = new MetricReport[s_MetricNames.Length];
            for (int i = 0; i < s_MetricNames.Length; i++)
            {
                int count = m_Samples.Statistics(i, out double median, out double p95, out double min, out double max);
                bool recorderValid = i == 4 ? m_GcRecorder.Valid : i < StageOffset || m_GpuRecorders[i - StageOffset].Valid;
                string availability = count > 0 ? "available" : recorderValid ? "no_samples_or_not_executed" : "recorder_unavailable";
                m_Result.metrics[i] = new MetricReport { name = s_MetricNames[i], validSamples = count, availability = availability,
                    median = VSMBaselineSamples.Number(median), p95 = VSMBaselineSamples.Number(p95),
                    min = VSMBaselineSamples.Number(min), max = VSMBaselineSamples.Number(max) };
            }
            if (!SufficientCoverage(m_Samples.ValidCounts[1], m_Samples.Count))
                m_Result.issues.Add("Fresh CPU frame timing coverage is below 95%.");
            if (!SufficientCoverage(m_Samples.ValidCounts[2], m_Samples.Count))
                m_Result.issues.Add("Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.");
            for (int i = 0; i < s_RequiredStages.Length; i++)
                if (!SufficientCoverage(m_Samples.ValidCounts[StageOffset + s_RequiredStages[i]], m_Samples.Count))
                    m_Result.issues.Add("Required stage missing or below 95% coverage: " + StageNames[s_RequiredStages[i]]);
            m_Result.timingCoveragePassed = m_Result.samplingGoalMet && m_Result.issues.Count == 0;
            m_Result.gpuTimingsBelow1ms = 0;
            var lowIntervals = new double[m_Samples.Count];
            int intervals = 0, previousSegment = -1;
            double previousLowTime = 0;
            for (int row = 0; row < m_Samples.Count; row++)
            {
                double gpu = m_Samples.Values[row * m_Samples.MetricCount + 2];
                if (!(gpu > 0 && gpu < 1)) continue;
                if (m_Samples.Segments[row] == previousSegment)
                    lowIntervals[intervals++] = m_Samples.Times[row] - previousLowTime;
                previousLowTime = m_Samples.Times[row]; previousSegment = m_Samples.Segments[row];
                m_Result.gpuTimingsBelow1ms++;
            }
            Array.Sort(lowIntervals, 0, intervals);
            m_Result.lowGpuTimingMedianIntervalSeconds = intervals > 0
                ? (lowIntervals[(intervals - 1) / 2] + lowIntervals[intervals / 2]) * 0.5 : 0;
            m_Result.observedWindowRepaints = m_Samples.Count > 1
                ? m_Samples.WindowRepaints[m_Samples.Count - 1] - m_Samples.WindowRepaints[0] : 0;
            m_Result.residencyStatus = !m_Result.finalPageCountersAvailable ? "not_captured"
                : m_Result.finalPageCounters[3] > 0 ? "over_budget_at_last_frame" : "no_overflow_at_last_frame";
            m_Result.issues.Add("Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.");
            if (m_Result.gpuTimingsBelow1ms > 0)
                m_Result.issues.Add("Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.");
            if (m_Result.finalPageCountersAvailable && m_Result.finalPageCounters[3] > 0)
                m_Result.issues.Add("Physical page requests exceed the budget at the last frame; quality is not validated.");
            m_Result.comparable = false;
            if (m_Result.status == "completed" && !m_Result.comparable) m_Result.status = "completed_with_warnings";
            string prefix = Path.Combine(OutputDirectory, "case_" + m_CaseIndex.ToString("D3"));
            File.WriteAllText(prefix + ".json", JsonUtility.ToJson(m_Result, true));
            m_Samples.Write(OutputDirectory, m_CaseIndex, m_Cases[m_CaseIndex], m_Result.status, s_MetricNames);
        }

        internal static bool SufficientCoverage(int valid, int observations) => observations > 0 && (long)valid * 100 >= (long)observations * 95;

        internal void Finish(string reason)
        {
            if (IsFinished) return;
            LastSaveSucceeded = false;
            m_Pause = PauseKind.User; m_PendingEnd = m_EndReady = false;
            if (m_Result != null) { m_Result.status = "partial"; m_Result.reason = reason; SaveCurrentCase(); }
            m_Run.reason = reason; m_Run.finishedUtc = DateTime.UtcNow.ToString("O");
            m_Run.status = HasCurrentCase ? "stopped_partial" : "completed";
            WriteEvent("session_finished", reason);
            WriteRun();
            LastSaveSucceeded = true;
            IsFinished = true;
            Dispose();
        }

        private void WriteEvent(string action, string reason)
        {
            var entry = new Event { utc = DateTime.UtcNow.ToString("O"), action = action, reason = reason,
                caseIndex = m_CaseIndex, attempt = m_Result != null ? m_Result.attempt : 0,
                observations = m_Samples.Count, measuredSeconds = m_Clock.ElapsedSeconds };
            File.AppendAllText(Path.Combine(OutputDirectory, "events.jsonl"), JsonUtility.ToJson(entry) + Environment.NewLine);
        }

        private void WriteRun()
        {
            m_Run.currentCase = m_CaseIndex; m_Run.processedCases = m_CaseIndex;
            m_Run.completedCases = m_Run.comparableCases = 0;
            foreach (var item in m_Run.results)
            {
                if (item.samplingGoalMet) m_Run.completedCases++;
                if (item.comparable) m_Run.comparableCases++;
            }
            if (m_Run.status == "completed" && m_Run.comparableCases != m_Cases.Length) m_Run.status = "completed_with_warnings";
            File.WriteAllText(Path.Combine(OutputDirectory, "run.json"), JsonUtility.ToJson(m_Run, true));
            var report = new StringBuilder();
            report.AppendLine("# VSM baseline report").AppendLine();
            report.Append("Status: ").Append(m_Run.status).Append(" | Reason: ").AppendLine(m_Run.reason);
            report.Append("Cases processed: ").Append(m_Run.processedCases).Append('/').Append(m_Cases.Length)
                .Append(" | Sampling goals met: ").Append(m_Run.completedCases).Append(" | Timing windows comparable: ").AppendLine(m_Run.comparableCases.ToString());
            report.Append("Frame timing source: ").Append(m_Run.frameTimingSource)
                .Append(" | Quality: ").Append(m_Run.qualityStatus).AppendLine();
            report.AppendLine().AppendLine(m_Run.timingScope).AppendLine();
            report.AppendLine("Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.").AppendLine();
            foreach (var item in m_Run.results)
            {
                report.Append("## ").Append(item.settings.name).Append(" — ").AppendLine(item.status);
                report.Append("Attempt: ").Append(item.attempt).Append(" | Observations: ").Append(item.observations)
                    .Append(" | Active seconds: ").Append(VSMBaselineSamples.Number(item.measuredSeconds))
                    .Append(" | Segments: ").AppendLine(item.segments.ToString());
                report.Append("Reason: ").AppendLine(item.reason);
                report.Append("Recorder repaint: ").Append(item.settings.recorderRepaint)
                    .Append(" | Observed repaints: ").Append(item.observedWindowRepaints)
                    .Append(" | GPU <1 ms (retained): ").Append(item.gpuTimingsBelow1ms)
                    .Append(" | Median interval (s, 0=unavailable): ").AppendLine(VSMBaselineSamples.Number(item.lowGpuTimingMedianIntervalSeconds));
                report.Append("Timing coverage passed: ").Append(item.timingCoveragePassed)
                    .Append(" | Source: ").Append(item.frameTimingSource)
                    .Append(" | Quality: ").AppendLine(item.qualityStatus);
                report.Append("Residency: ").Append(item.residencyStatus);
                if (item.finalPageCountersAvailable)
                    report.Append(" | Last-frame resident/requested/new/overflow: ").Append(item.finalPageCounters[0]).Append('/')
                        .Append(item.finalPageCounters[1]).Append('/').Append(item.finalPageCounters[2]).Append('/').Append(item.finalPageCounters[3]);
                report.AppendLine();
                foreach (string issue in item.issues) report.Append("- ").AppendLine(issue);
                if (item.metrics == null) { report.AppendLine("Not sampled.").AppendLine(); continue; }
                report.AppendLine().AppendLine("| Metric | Valid / observations | Median | P95 | Availability |")
                    .AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var metric in item.metrics)
                    report.Append("| ").Append(metric.name).Append(" | ").Append(metric.validSamples).Append('/').Append(item.observations)
                        .Append(" | ").Append(metric.median).Append(" | ").Append(metric.p95).Append(" | ").Append(metric.availability).AppendLine(" |");
                report.AppendLine();
            }
            if (!string.IsNullOrEmpty(m_Run.lastError)) report.AppendLine("## Last error").AppendLine(m_Run.lastError);
            ReportText = report.ToString();
            File.WriteAllText(Path.Combine(OutputDirectory, "report.md"), ReportText);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; IsFinished = true;
            Application.runInBackground = m_PreviousRunInBackground;
            RenderPipelineManager.endCameraRendering -= m_EndCameraCallback;
            for (int i = 0; i < m_GpuRecorders.Length; i++) m_GpuRecorders[i].Dispose();
            m_GcRecorder.Dispose();
            if (m_VolumeObject != null) Object.DestroyImmediate(m_VolumeObject);
            if (m_Profile != null) Object.DestroyImmediate(m_Profile);
            if (m_Settings != null) Object.DestroyImmediate(m_Settings);
        }

        private static string[] BuildMetricNames()
        {
            var names = new string[StageOffset + StageNames.Length];
            names[0] = "frame_interval_ms"; names[1] = "cpu_frame_ms"; names[2] = "gpu_frame_ms";
            names[3] = "cpu_render_thread_ms"; names[4] = "gc_allocated_in_editor_frame_bytes";
            for (int i = 0; i < StageNames.Length; i++) names[StageOffset + i] = StageNames[i] + "_gpu_ms";
            return names;
        }
    }
}
