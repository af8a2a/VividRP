using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.AgenticDebugger
{
    public static class PixCaptureBridge
    {
        private static PixCaptureSession session;
        private static PixAnalysisJob analysis;
        private static string sessionPix, sessionAnalyzer, captureHash;
        private static readonly EditorApplication.CallbackFunction AnalysisTickCallback = TickAnalysis;
        private static readonly AssemblyReloadEvents.AssemblyReloadCallback AnalysisReloadCallback = DetachAnalysis;
        private static readonly Action AnalysisQuitCallback = DetachAnalysis;
        internal static bool HasSession => session != null;
        private static readonly EditorApplication.CallbackFunction TickCallback = Tick;
        private static readonly AssemblyReloadEvents.AssemblyReloadCallback ReloadCallback = () => Cancel("Assembly reload");
        private static readonly Action QuitCallback = () => Cancel("Editor quitting");
        private static readonly Action<PlayModeStateChange> PlayCallback = _ => Cancel("Play mode changed");

        public static JObject Execute(string action = "status", string sessionId = null, string path = null,
            string cameraName = null, string expectedPass = null, float timeoutSeconds = 120,
            string pixInstall = null, string analyzerPath = null, int queueId = -1, int eventIndex = -1,
            int offset = 0, int count = 100, string marker = null, string expectedCaptureHash = null,
            int counterId = -1, int occupancyType = -1, int occupancyStage = -1, string experiment = null,
            string analysisId = null)
        {
            try
            {
                switch (action)
                {
                    case "status": return Status();
                    case "capture":
                        if (session != null) return Error("busy", "Release the previous PIX session first.");
                        if (FrameDebuggerBridge.HasActiveCapture) return Error("busy", "Release or disable Frame Debugger before starting PIX.");
                        if (float.IsNaN(timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 300)
                            return Error("invalid_timeout", "timeout_seconds must be between 1 and 300.");
                        if (string.IsNullOrWhiteSpace(expectedPass))
                            return Error("expected_pass_required", "Supply the exact GPU pass marker expected inside the target camera.");
                        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
                            return Error("requires_d3d12", "Launch the Editor with -force-d3d12 under PIX.");
                        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                            return Error("editor_busy", "Wait for compilation/import or play mode transition to finish.");
                        var asset = GraphicsSettings.currentRenderPipeline;
                        if (!asset || asset.GetType().FullName != "VividRP.Runtime.VividRenderPipelineAsset")
                            return Error("requires_vividrp", "Select a VividRP pipeline before capture.");
                        if (!SystemInfo.supportsGraphicsFence)
                            return Error("graphics_fence_required", "Capture requires GPU queue synchronization support.");
                        Camera camera = FindCamera(cameraName);
                        if (!camera) return Error("no_camera", "Supply a unique enabled camera name, or assign MainCamera.");
                        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                            return Error("unsupported_camera", "Capture supports Game or SceneView cameras only.");
                        string id = Guid.NewGuid().ToString("N");
                        string project = Directory.GetParent(Application.dataPath).FullName;
                        string output = Path.GetFullPath(Path.Combine(project, string.IsNullOrWhiteSpace(path)
                            ? "Temp/AgenticDebugger/PIX/" + id + ".wpix" : path));
                        if (!string.Equals(Path.GetExtension(output), ".wpix", StringComparison.OrdinalIgnoreCase))
                            return Error("invalid_path", "Capture path must end in .wpix.");
                        if (File.Exists(output) || File.Exists(output + ".validation.json"))
                            return Error("path_exists", "Capture/evidence files already exist; use a new path.");
                        pixInstall = pixInstall ?? Environment.GetEnvironmentVariable("VIVID_PIX_INSTALL");
                        analyzerPath = analyzerPath ?? Environment.GetEnvironmentVariable("VIVID_PIX_ANALYZER");
                        if (string.IsNullOrWhiteSpace(pixInstall) || !File.Exists(Path.Combine(pixInstall, "pixapi.dll")) ||
                            string.IsNullOrWhiteSpace(analyzerPath) || !File.Exists(analyzerPath))
                            return Error("pix_tools_missing", "Set VIVID_PIX_INSTALL and VIVID_PIX_ANALYZER, or pass pix_install/analyzer_path. See PIX.md.");
                        PixOperations.Check(PixNative.VividPixAvailable(), "PIX unavailable; launch Unity through PIX before D3D12 device creation");
                        var runtimePath = new System.Text.StringBuilder(32768);
                        uint pathLength = PixNative.VividPixRuntimePath(runtimePath, runtimePath.Capacity);
                        if (pathLength == 0 || pathLength >= runtimePath.Capacity ||
                            !string.Equals(Path.GetFullPath(runtimePath.ToString()), Path.GetFullPath(Path.Combine(pixInstall, "WinPixGpuCapturer.dll")), StringComparison.OrdinalIgnoreCase))
                            return Error("pix_version_mismatch", "The injected capturer and selected PIX API must come from the same PIX installation.");
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        var operations = new PixOperations(id, output, expectedPass, camera, Path.GetFullPath(pixInstall), Path.GetFullPath(analyzerPath));
                        sessionPix = Path.GetFullPath(pixInstall); sessionAnalyzer = Path.GetFullPath(analyzerPath); captureHash = null;
                        session = new PixCaptureSession(id, output, expectedPass, EditorApplication.timeSinceStartup + timeoutSeconds, operations, operations.BoundaryMode);
                        Subscribe();
                        session.Start();
                        return Status();
                    case "release":
                        if (session == null || sessionId != session.Id) return Error("session_mismatch", "A matching session_id is required.");
                        if (analysis != null && !analysis.Terminal)
                        {
                            analysis.Cancel(); analysis.Tick(EditorApplication.timeSinceStartup);
                            if (!analysis.Terminal) return analysis.Snapshot();
                        }
                        DetachAnalysis();
                        session.Cancel("Released by owner");
                        if (session.CleanupPending) return Status();
                        Unsubscribe();
                        session = null;
                        sessionPix = sessionAnalyzer = captureHash = null;
                        return Status();
                    case "analysis_status":
                    case "analysis_cancel":
                        if (session == null || session.Id != sessionId || analysis == null || analysis.Id != analysisId)
                            return Error("analysis_mismatch", "Matching session_id and analysis_id are required.");
                        if (action == "analysis_cancel") analysis.Cancel();
                        TickAnalysis(); return analysis.Snapshot();
                    default:
                        if (!PixAnalysisRequest.IsAction(action)) return Error("unsupported_action", "Unknown PIX capture/analysis action.");
                        if (session == null || session.Id != sessionId) return Error("session_mismatch", "A matching session_id is required.");
                        if (session.State != "ready") return Error("capture_not_ready", "Only a validated ready capture can be analyzed.");
                        if (analysis != null && !analysis.Terminal) return Error("analysis_busy", "Poll or cancel the current analysis before starting another.");
                        var request = new PixAnalysisRequest(action, session.Id, session.Path, session.ExpectedPass, session.BoundaryMode,
                            sessionPix, sessionAnalyzer, queueId, eventIndex, offset, count, marker,
                            expectedCaptureHash ?? captureHash, counterId, occupancyType, occupancyStage, experiment, timeoutSeconds);
                        string resultPath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
                        // Construct first so invalid arguments/output never discard a previous result.
                        var next = new PixAnalysisJob(request, resultPath, EditorApplication.timeSinceStartup);
                        DetachAnalysis(); analysis = next;
                        EditorApplication.update += AnalysisTickCallback;
                        AssemblyReloadEvents.beforeAssemblyReload += AnalysisReloadCallback;
                        EditorApplication.quitting += AnalysisQuitCallback;
                        return analysis.Snapshot();
                }
            }
            catch (Exception e) { return Error("pix_capture_error", e.Message); }
        }

        private static Camera FindCamera(string name)
        {
            if (string.IsNullOrEmpty(name)) return Camera.main;
            Camera found = null;
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!camera.isActiveAndEnabled || camera.name != name) continue;
                if (found) throw new ArgumentException("Camera name is ambiguous.");
                found = camera;
            }
            return found;
        }

        private static JObject Status() => session == null
            ? new JObject { ["success"] = true, ["backend"] = "pix", ["state"] = "idle", ["code"] = "idle" }
            : new JObject
            {
                ["success"] = session.State == "ready", ["backend"] = "pix", ["sessionId"] = session.Id,
                ["state"] = session.State, ["code"] = session.Code, ["message"] = session.Message,
                ["capturePath"] = session.Path, ["expectedPass"] = session.ExpectedPass,
                ["boundaryMode"] = session.BoundaryMode,
                ["captureHash"] = captureHash, ["analysisId"] = analysis?.Id,
                ["analysisState"] = analysis?.State, ["analysisResultPath"] = analysis?.Output,
                ["hresult"] = "0x" + unchecked((uint)session.HResult).ToString("X8"),
                ["cleanupPending"] = session.CleanupPending,
                ["validation"] = session.Validation == null ? null : JObject.FromObject(session.Validation)
            };

        private static JObject Error(string code, string message) => new JObject
            { ["success"] = false, ["backend"] = "pix", ["code"] = code, ["message"] = message };

        private static void Subscribe()
        {
            EditorApplication.update += TickCallback;
            AssemblyReloadEvents.beforeAssemblyReload += ReloadCallback;
            EditorApplication.quitting += QuitCallback;
            EditorApplication.playModeStateChanged += PlayCallback;
        }
        private static void Unsubscribe()
        {
            EditorApplication.update -= TickCallback;
            AssemblyReloadEvents.beforeAssemblyReload -= ReloadCallback;
            EditorApplication.quitting -= QuitCallback;
            EditorApplication.playModeStateChanged -= PlayCallback;
        }
        private static void Cancel(string reason) { session?.Cancel(reason); }
        private static void Tick()
        {
            session?.Tick(EditorApplication.timeSinceStartup);
            if (session != null && session.Terminal && !session.CleanupPending) Unsubscribe();
        }
        private static void TickAnalysis()
        {
            analysis?.Tick(EditorApplication.timeSinceStartup);
            if (analysis?.State == "ready") captureHash = (string)analysis.Result["captureHash"];
            if (analysis != null && analysis.Terminal) EditorApplication.update -= AnalysisTickCallback;
        }
        private static void DetachAnalysis()
        {
            EditorApplication.update -= AnalysisTickCallback;
            AssemblyReloadEvents.beforeAssemblyReload -= AnalysisReloadCallback;
            EditorApplication.quitting -= AnalysisQuitCallback;
            analysis?.Dispose(); analysis = null;
        }
    }

    internal static class PixNative
    {
        private const string Dll = "VividPixCapture";
        [DllImport(Dll)] internal static extern int VividPixAvailable();
        [DllImport(Dll)] internal static extern int VividPixPrepare(out ulong token, out int firstEvent);
        [DllImport(Dll)] internal static extern IntPtr VividPixGetRenderEvent();
        [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int VividPixBegin(ulong token, string path);
        [DllImport(Dll)] internal static extern PixNativeState VividPixPoll(ulong token, out int hr);
        [DllImport(Dll)] internal static extern int VividPixCancel(ulong token);
        [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern uint VividPixRuntimePath(System.Text.StringBuilder path, int capacity);
    }

    internal sealed class PixOperations : IPixCaptureOperations, IVividCaptureObserver
    {
        private readonly string id, path, pass, pix, analyzer, beginMarker, endMarker;
        private readonly Camera camera;
        private ulong token;
        private int firstEvent;
        private IntPtr callback;
        private CommandBuffer commands;
        private PixQueueBoundary queues;
        private Process validation;
        private bool renderError;
        private readonly Application.LogCallback logCallback;
        private bool preparePending, armed, captureStarted;
        private int captureFrameIndex;
        private int targetDepth;
        private Exception callbackFailure;
        internal string BoundaryMode { get; }

        internal PixOperations(string id, string path, string pass, Camera camera, string pix, string analyzer)
        {
            this.id = id; this.path = path; this.pass = pass; this.camera = camera; this.pix = pix; this.analyzer = analyzer;
            beginMarker = "VividRP.AgentCapture/" + id + "/FrameBegin";
            endMarker = "VividRP.AgentCapture/" + id + "/FrameEnd";
            logCallback = (_, __, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) renderError = true; };
            BoundaryMode = SystemInfo.supportsAsyncCompute ? "all" : "graphics";
        }

        internal static void Check(int hr, string operation)
        {
            if (hr < 0) throw new COMException(operation + " (0x" + unchecked((uint)hr).ToString("X8") + ")", hr);
        }

        public void Prepare()
        {
            Check(PixNative.VividPixPrepare(out token, out firstEvent), "Prepare capture");
            callback = PixNative.VividPixGetRenderEvent();
            if (callback == IntPtr.Zero) throw new InvalidOperationException("Native render callback missing.");
            commands = new CommandBuffer { name = "VividRP PIX capture boundary" };
            queues = new PixQueueBoundary(id, BoundaryMode == "all");
            if (!VividCaptureHooks.TryAcquire(this)) throw new InvalidOperationException("A pipeline capture observer already owns the camera boundary.");
            preparePending = true;
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public PixNativeState Poll(out int hr)
        {
            if (callbackFailure != null)
            {
                var error = callbackFailure;
                callbackFailure = null;
                throw error;
            }
            if (token == 0) { hr = 0; return PixNativeState.Idle; }
            return PixNative.VividPixPoll(token, out hr);
        }

        public void ArmCamera()
        {
            if (!camera) throw new InvalidOperationException("Capture camera was destroyed.");
            armed = true;
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public void BeginCamera(ScriptableRenderContext context, Camera target, int frameIndex)
        {
            if (target != camera) return;
            ++targetDepth;
            if (targetDepth != 1 || captureStarted || callbackFailure != null) return;
            try
            {
                if (!armed) return;
                // Pipeline-owned boundary, before public camera callbacks, jobs,
                // culling and RenderGraph recording. PIX begins on the main thread.
                Check(PixNative.VividPixBegin(token, path), "PIXBeginCapture");
                captureStarted = true;
                captureFrameIndex = frameIndex;
                Application.logMessageReceived += logCallback;
                commands.BeginSample(beginMarker);
                commands.EndSample(beginMarker);
                queues.Begin(context, commands);
            }
            catch (Exception e) { callbackFailure = e; DetachCamera(); }
        }

        public void EndCamera(ScriptableRenderContext context, Camera target, int frameIndex, VividCaptureOutcome outcome)
        {
            if (target != camera || --targetDepth != 0) return;
            try
            {
                if (outcome != VividCaptureOutcome.Submitted)
                    throw new PixCaptureFailure("target_camera_" + outcome.ToString().ToLowerInvariant(), "VividRP camera result: " + outcome);
                if (preparePending)
                {
                    // Drain after the preparation camera has submitted, not before
                    // it adds another frame of work behind the preparation fence.
                    queues.Join(context, commands, false);
                    commands.IssuePluginEventAndData(callback, firstEvent, new IntPtr(unchecked((long)token)));
                    context.ExecuteCommandBuffer(commands);
                    commands.Clear();
                    context.Submit();
                    preparePending = false;
                    return;
                }
                if (!captureStarted || frameIndex != captureFrameIndex) return;
                DetachCamera();
                if (renderError) throw new PixCaptureFailure("target_camera_render_error", "The target camera logged a rendering error.");
                // End is guaranteed by the pipeline wrapper even when rendering,
                // Submit or a public callback throws. Only a submitted camera can
                // produce the closing markers used by the acceptance gate.
                queues.Join(context, commands, true);
                commands.BeginSample(endMarker);
                commands.EndSample(endMarker);
                commands.IssuePluginEventAndData(callback, firstEvent + 1, new IntPtr(unchecked((long)token)));
                context.ExecuteCommandBuffer(commands);
                context.Submit();
                commands.Clear();
            }
            catch (Exception e) { callbackFailure = e; DetachCamera(); }
        }

        public void CameraSkipped(Camera target, VividCaptureOutcome outcome)
        {
            if (target != camera || callbackFailure != null) return;
            callbackFailure = new PixCaptureFailure("target_camera_" + outcome.ToString().ToLowerInvariant(), "VividRP skipped the target camera: " + outcome);
            DetachCamera();
        }

        private void DetachCamera()
        {
            Application.logMessageReceived -= logCallback;
            VividCaptureHooks.Release(this);
        }

        public void StartValidation()
        {
            // No File.Exists/size/timer acceptance. PIX opens the document and
            // validates ordered session markers and GPU work under the pass.
            var start = new ProcessStartInfo(analyzer)
            {
                UseShellExecute = false, CreateNoWindow = true,
                Arguments = "validate " + Quote(pix) + " " + Quote(path) + " " + Quote(id) + " " + Quote(pass) + " " + Quote(path + ".validation.json") + " " + BoundaryMode
            };
            validation = Process.Start(start) ?? throw new InvalidOperationException("Could not start PIX validator.");
        }

        // Windows CommandLineToArgvW quoting, including quotes/trailing slashes.
        internal static string Quote(string value)
        {
            var output = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') output.Append('\\', slashes * 2 + 1);
                else output.Append('\\', slashes);
                output.Append(c);
                slashes = 0;
            }
            output.Append('\\', slashes * 2);
            return output.Append('"').ToString();
        }

        public bool TryGetValidation(out PixValidationResult result)
        {
            result = null;
            if (!validation.HasExited) return false;
            string evidence = path + ".validation.json";
            if (!File.Exists(evidence)) throw new InvalidOperationException("PIX validator exited without its JSON evidence (exit " + validation.ExitCode + ").");
            result = JsonConvert.DeserializeObject<PixValidationResult>(File.ReadAllText(evidence));
            if (validation.ExitCode != 0 && result != null) result.success = false;
            return true;
        }

        public void Cancel()
        {
            // Release managed resources even when a domain reload prevents future
            // ticks. Native cancellation retains ownership until EndCapture ends.
            try { if (validation != null && !validation.HasExited) validation.Kill(); }
            finally
            {
                try { if (token != 0) Check(PixNative.VividPixCancel(token), "Cancel capture"); }
                finally { Dispose(); }
            }
        }
        public void Dispose()
        {
            DetachCamera();
            commands?.Dispose();
            commands = null;
            queues?.Dispose();
            queues = null;
            validation?.Dispose();
            validation = null;
        }
    }
}
