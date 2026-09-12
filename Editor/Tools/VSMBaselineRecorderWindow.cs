using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    public sealed class VSMBaselineRecorderWindow : EditorWindow
    {
        private static readonly string[] s_Fields = { "m_Camera", "m_WarmupSeconds", "m_MeasurementSeconds",
            "m_MinSamplesPerCase", "m_MaxSamplesPerCase", "m_RequireFrameTimings", "m_OutputDirectory", "m_Revision", "m_Cases" };
        [SerializeField] private Camera m_Camera;
        [SerializeField, Min(0)] private float m_WarmupSeconds = 5;
        [SerializeField, Min(1)] private float m_MeasurementSeconds = 10;
        [SerializeField, Min(1)] private int m_MinSamplesPerCase = 300;
        [SerializeField, Min(1)] private int m_MaxSamplesPerCase = 30000;
        [SerializeField] private bool m_RequireFrameTimings = true;
        [SerializeField] private string m_OutputDirectory = "Logs/VSMBaselines";
        [SerializeField] private string m_Revision = "";
        [SerializeField] private VSMBaselineCase[] m_Cases = VSMBaselineCase.CreateDefaults();
        [SerializeField] private string m_LastOutput;
        private VSMBaselineSession m_Session;
        private string m_Error = "", m_LastReport = "", m_SaveMessage = "";
        private Vector2 m_Scroll;
        private bool m_ShowSettings = true, m_ShowReport;
        private double m_NextRepaint;

        [MenuItem("Tools/VividRP/VSM Baseline Recorder")]
        private static void Open() => GetWindow<VSMBaselineRecorderWindow>("VSM Baseline");

        [MenuItem("Tools/VividRP/Diagnostics/Run Timing Source Experiment")]
        private static void RunTimingSourceExperiment()
        {
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VSMBaselineRecorderWindow).Assembly).resolvedPath;
            StartTimingSourceExperiment(Camera.main, Path.Combine(package, "Roadmap~/Experiments/TimingSource"), "working_tree_phase0");
        }

        public string OutputPath => m_LastOutput;
        public string Error => m_Error;
        public bool IsRecording => m_Session != null && !m_Session.IsFinished;

        public static VSMBaselineRecorderWindow StartTimingSourceExperiment(Camera camera, string outputDirectory, string revision, int repetitions = 3)
        {
            if (VSMQualityReproduction.IsRunning) throw new InvalidOperationException("Finish quality capture before timing experiments.");
            foreach (var existing in Resources.FindObjectsOfTypeAll<VSMBaselineRecorderWindow>())
                if (existing.IsRecording) throw new InvalidOperationException("A baseline session is already active.");
            var window = CreateInstance<VSMBaselineRecorderWindow>();
            window.titleContent = new GUIContent("VSM Timing A/B");
            window.m_Camera = camera; window.m_OutputDirectory = outputDirectory; window.m_Revision = revision;
            window.m_Cases = VSMBaselineCase.CreateTimingExperiment(repetitions);
            window.ShowUtility();
            window.StartRecording();
            return window;
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
            if (!string.IsNullOrEmpty(m_LastOutput))
            {
                string report = Path.Combine(m_LastOutput, "report.md");
                try { if (File.Exists(report)) m_LastReport = File.ReadAllText(report); }
                catch (Exception exception) { ShowError(exception); }
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
            FinishAndRelease("window_closed_or_assembly_reload", true);
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) FinishAndRelease("play_mode_exited", true);
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) m_Session?.RecordWindowRepaint();
            bool active = m_Session != null && !m_Session.IsFinished;
            EditorGUILayout.HelpBox("Pause and save retains this session. Resume adds a new segment after warmup. " +
                "Finish releases the temporary settings. Manual repaint keeps sampling active; use Refresh progress. " +
                "Keep Game view rendering; closing the window or leaving Play Mode ends the session.", MessageType.Info);
            if (!string.IsNullOrEmpty(m_Error)) EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (active)
            {
                EditorGUILayout.LabelField(m_Session.Phase, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Recorder repaint", m_Session.RepaintMode.ToString());
                if (GUILayout.Button("Refresh progress")) Repaint();
                EditorGUILayout.LabelField($"Case {Math.Min(m_Session.CaseIndex + 1, m_Session.CaseCount)}/{m_Session.CaseCount}: {m_Session.CurrentCaseName}");
                float progress = Mathf.Min((float)(m_Session.MeasuredSeconds / m_MeasurementSeconds),
                    (float)m_Session.SampleCount / m_MinSamplesPerCase);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), Mathf.Clamp01(progress),
                    $"{m_Session.MeasuredSeconds:F1}/{m_MeasurementSeconds:F1} s active | {m_Session.SampleCount}/{m_MinSamplesPerCase} samples minimum");
                EditorGUILayout.LabelField($"Valid GPU frames: {m_Session.GpuSampleCount}/{m_Session.SampleCount} | Resolve: {m_Session.ResolveSampleCount}/{m_Session.SampleCount}");
                if (m_Session.WarmupRemaining > 0 && !m_Session.IsPaused)
                    EditorGUILayout.LabelField($"Warmup remaining: {m_Session.WarmupRemaining:F1} s");
                using (new EditorGUI.DisabledScope(!m_Session.HasCurrentCase))
                {
                    if (m_Session.IsPaused)
                    {
                        if (GUILayout.Button("Resume after warmup")) Execute(m_Session.Resume);
                    }
                    else if (GUILayout.Button("Pause and save — keep session")) Execute(m_Session.PauseAndSave);
                    if (m_Session.CanAllowMissingTiming && GUILayout.Button("Continue with missing frame timings (report as incomplete)"))
                        Execute(m_Session.AllowMissingFrameTimings);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Retry current case (archive attempt)")) Execute(m_Session.RetryCurrentCase);
                        if (GUILayout.Button("Skip current case")) Execute(SkipCurrentCase);
                    }
                }
                if (GUILayout.Button("Finish session and release settings")) FinishAndRelease("user_finished", false);
            }
            else
            {
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || EditorApplication.isPaused))
                    if (GUILayout.Button("Start new recording")) StartRecording();
                if (GUILayout.Button("Load 4K Hard repaint A/B preset (2 cases)"))
                    m_Cases = VSMBaselineCase.CreateRepaintComparison();
                if (GUILayout.Button("Load 4K Hard repaint sweep (3 rounds)"))
                    m_Cases = VSMBaselineCase.CreateTimingExperiment(3);
                if (GUILayout.Button("Load baseline preset (manual repaint)"))
                    m_Cases = VSMBaselineCase.CreateDefaults();
            }

            if (!string.IsNullOrEmpty(m_SaveMessage)) EditorGUILayout.HelpBox(m_SaveMessage, MessageType.Info);
            if (!string.IsNullOrEmpty(m_LastOutput))
            {
                EditorGUILayout.SelectableLabel(m_LastOutput, EditorStyles.wordWrappedLabel, GUILayout.Height(34));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Show output folder")) EditorUtility.RevealInFinder(m_LastOutput);
                    if (GUILayout.Button("Open report") && File.Exists(Path.Combine(m_LastOutput, "report.md")))
                        EditorUtility.OpenWithDefaultApp(Path.Combine(m_LastOutput, "report.md"));
                }
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Session != null)
                for (int i = 0; i < m_Session.CaseCount; i++)
                    EditorGUILayout.LabelField($"Case {i + 1}", m_Session.GetCaseStatus(i));
            m_ShowSettings = EditorGUILayout.Foldout(m_ShowSettings, "Recording settings", true);
            if (m_ShowSettings)
            {
                using (new EditorGUI.DisabledScope(active))
                using (var serialized = new SerializedObject(this))
                {
                    foreach (string field in s_Fields) EditorGUILayout.PropertyField(serialized.FindProperty(field), true);
                    serialized.ApplyModifiedProperties();
                }
                EditorGUILayout.HelpBox("GPU data unavailable at startup waits by default. Per-stage and whole-frame timing include Editor/all-camera work. Missing data is never converted to zero.", MessageType.Info);
            }
            m_ShowReport = EditorGUILayout.Foldout(m_ShowReport, "Last saved report (updated at checkpoints)", true);
            if (m_ShowReport)
                EditorGUILayout.TextArea(m_LastReport, GUILayout.MinHeight(180));
            EditorGUILayout.EndScrollView();
        }

        private void StartRecording()
        {
            try
            {
                if (VividDiagnostics.IsRunning) throw new InvalidOperationException("Finish the active diagnostic before baseline recording.");
                var session = new VSMBaselineSession(m_Camera != null ? m_Camera : Camera.main,
                    m_Cases, m_WarmupSeconds, m_MeasurementSeconds, m_MinSamplesPerCase, m_MaxSamplesPerCase,
                    m_OutputDirectory, m_Revision, m_RequireFrameTimings);
                m_Session?.Dispose();
                m_Session = session; m_LastOutput = session.OutputDirectory;
                m_ShowSettings = false; m_ShowReport = false; m_Error = ""; m_SaveMessage = "";
                m_LastReport = session.ReportText;
            }
            catch (Exception exception) { ShowError(exception); }
        }

        private void SkipCurrentCase()
        {
            if (m_Session.SkipCurrentCase()) FinishAndRelease("all_cases_processed", false);
        }

        private void Execute(Action action)
        {
            try
            {
                m_Error = "";
                action();
                m_LastReport = m_Session.ReportText;
                if (m_Session.LastSaveSucceeded && !m_Session.IsFinished && string.IsNullOrEmpty(m_Error))
                    m_SaveMessage = "Checkpoint saved. Session and collected samples are retained.";
            }
            catch (Exception exception) { HandleSessionError(exception); }
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (m_Session == null || m_Session.IsFinished) return;
            string previousPhase = m_Session.Phase;
            bool neededAttention = m_Session.IsPaused || m_Session.CanAllowMissingTiming;
            try
            {
                if (!EditorApplication.isPlaying) FinishAndRelease("play_mode_exited", true);
                else if (m_Session.Tick()) FinishAndRelease("all_cases_processed", false);
                m_LastReport = m_Session.ReportText;
            }
            catch (Exception exception) { HandleSessionError(exception); }
            if (m_Session.IsFinished) return; // FinishAndRelease already repaints the saved report.
            // Show recovery controls once on a state change, without a paused 4 Hz timer in Manual mode.
            if ((neededAttention || m_Session.IsPaused || m_Session.CanAllowMissingTiming) && previousPhase != m_Session.Phase)
                Repaint();
            if (ShouldRepaint(m_Session.RepaintMode, EditorApplication.timeSinceStartup, ref m_NextRepaint))
                Repaint();
        }

        internal static bool ShouldRepaint(VSMBaselineRepaintMode mode, double now, ref double nextRepaint)
        {
            double interval = VSMBaselineSession.RepaintInterval(mode);
            if (double.IsPositiveInfinity(interval) || now < nextRepaint) return false;
            nextRepaint = now + interval;
            return true;
        }

        private void HandleSessionError(Exception exception)
        {
            ShowError(exception);
            if (m_Session == null || m_Session.IsFinished) return;
            try { m_Session.PauseAfterError(exception); m_LastReport = m_Session.ReportText; }
            catch (Exception saveError) { ShowError(saveError); }
            m_SaveMessage = m_Session.LastSaveSucceeded
                ? "Paused after error. Partial results saved; session retained for retry."
                : "Saving failed. Samples remain in memory; restore output-folder access or disk space before finishing.";
        }

        private void ShowError(Exception exception)
        {
            m_Error = exception.Message;
            Debug.LogException(exception);
        }

        private void FinishAndRelease(string reason, bool forceRelease)
        {
            if (m_Session == null || m_Session.IsFinished) return;
            try
            {
                m_Session.Finish(reason);
                m_LastReport = m_Session.ReportText; m_Error = ""; m_ShowReport = true;
                m_SaveMessage = "Session ended and saved. Review per-case status and missing metrics in report.md.";
                Debug.Log("VSM baseline saved to " + m_LastOutput + ". Reason: " + reason);
            }
            catch (Exception exception)
            {
                HandleSessionError(exception);
                if (forceRelease)
                {
                    m_SaveMessage = "Session released during teardown. Final save failed; inspect the last checkpoint in " + m_LastOutput;
                    Debug.LogError(m_SaveMessage + ". Reason: " + reason);
                }
            }
            finally
            {
                // Closing/reloading/leaving Play Mode cannot keep temporary Unity resources alive.
                if (forceRelease) m_Session.Dispose();
            }
            Repaint();
        }
    }
}
