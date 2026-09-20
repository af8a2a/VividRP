using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VividRP.AgenticDebugger
{
    /// <summary>Main-thread Editor API. No render-loop hooks or window activation.</summary>
    public static class FrameDebuggerBridge
    {
        private static FrameDebuggerApi api;
        private static string session, lastState = "idle";
        private static bool changedPause;
        private static double deadline;
        private static int settleTicks;
        private static Camera captureCamera;
        private static string captureSource;
        private static readonly EditorApplication.CallbackFunction TickCallback = Tick;
        private static readonly AssemblyReloadEvents.AssemblyReloadCallback ReloadCallback = Cleanup;
        private static readonly Action QuitCallback = Cleanup;
        private static readonly Action<PlayModeStateChange> PlayModeCallback = OnPlayModeChanged;

        public static JObject Execute(string action = "status", int eventIndex = -1,
            int offset = 0, int count = 100, string expectedEventsHash = null,
            string sessionId = null, string path = null, float timeoutSeconds = 60,
            bool includeShaderProperties = false, string source = "game_view", string cameraName = null)
        {
            try
            {
                api ??= new FrameDebuggerApi();
                switch (action)
                {
                    case "status": return Status();
                    case "export": return Failure("raster_inspection_disabled", "Render-target export is disabled: this Unity version can crash when inspecting native render passes.");
                    case "capture": return Capture(timeoutSeconds, source, cameraName);
                    case "release":
                        RequireOwner(sessionId);
                        Release("released");
                        return Status();
                    case "events":
                        RequireCapture(expectedEventsHash);
                        return Events(offset, count, path);
                    case "select":
                        RequireOwner(sessionId);
                        RequireCapture(expectedEventsHash);
                        RequireIndex(eventIndex);
                        RequireInspectable(eventIndex);
                        api.SetLimit(eventIndex + 1);
                        settleTicks = 4;
                        Repaint();
                        return Status();
                    case "event":
                        RequireCapture(expectedEventsHash);
                        if (eventIndex == -1) eventIndex = api.Limit() - 1;
                        RequireIndex(eventIndex);
                        RequireInspectable(eventIndex);
                        if (api.Limit() != eventIndex + 1)
                            return Failure("selection_required", "Select this event first, then poll event until ready.");
                        if (settleTicks > 0) return Failure("pending", "Waiting for the selected event to render.");
                        int hash = api.EventsHash();
                        object data = api.Data(eventIndex);
                        if (data == null) return Failure("pending", "Unity has not returned matching event data yet.");
                        if (hash != api.EventsHash()) return Failure("stale_capture", "Event list changed while reading details.");
                        var fields = FrameDebuggerApi.DescribeDispatch(data, includeShaderProperties);
                        var detail = Status();
                        detail["eventIndex"] = eventIndex;
                        detail["details"] = fields;
                        SaveJson(detail, path);
                        return detail;
                    default: return Failure("invalid_action", "Use status, capture, events, select, event or release. Texture export is disabled.");
                }
            }
            catch (Exception exception)
            {
                if (exception is TargetInvocationException invocation && invocation.InnerException != null)
                    exception = invocation.InnerException;
                return Failure(exception is MissingMemberException || exception is TypeLoadException
                    ? "unsupported_unity_api" : "request_failed", exception.Message);
            }
        }

        private static JObject Status() => new JObject
        {
            ["success"] = true, ["schemaVersion"] = 1, ["unityVersion"] = Application.unityVersion,
            ["enabled"] = api.Enabled(), ["local"] = api.Local(), ["supported"] = api.Supported(),
            ["eventCount"] = api.Enabled() ? api.Count() : 0,
            ["eventsHash"] = api.Enabled() ? api.EventsHash().ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            ["selectedEventIndex"] = api.Enabled() ? api.Limit() - 1 : -1,
            ["sessionId"] = session, ["state"] = session == null ? lastState : settleTicks > 0 || api.Count() == 0 ? "capturing" : "ready",
            ["source"] = session == null ? null : captureSource,
            ["camera"] = captureCamera ? captureCamera.name : null,
            ["cameraCaptureSupported"] = api.EnterCapture != null,
            ["inspectionPolicy"] = "ComputeDispatch and RayTracingDispatch only; raster/native render-pass inspection and texture export disabled.",
            ["remainingSeconds"] = session == null ? 0 : Math.Max(0, deadline - EditorApplication.timeSinceStartup),
            ["playing"] = EditorApplication.isPlaying, ["paused"] = EditorApplication.isPaused
        };

        private static JObject Capture(float seconds, string source, string cameraName)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 1 || seconds > 300)
                throw new ArgumentOutOfRangeException(nameof(seconds), "timeoutSeconds must be between 1 and 300.");
            if (session != null || api.Enabled())
                return Failure("busy", "A Frame Debugger capture is already active. Existing captures can be read without taking ownership.");
            if (!api.Supported()) return Failure("unsupported", "Local Frame Debugger is unsupported on this graphics backend.");
            Camera camera = null;
            if (source == "camera")
            {
                if (api.EnterCapture == null) return Failure("unsupported_unity_api", "This Unity version does not expose a camera capture scope.");
                camera = Camera.main;
                if (!string.IsNullOrEmpty(cameraName))
                {
                    camera = null;
                    foreach (var candidate in Camera.allCameras)
                    {
                        if (candidate.name != cameraName) continue;
                        if (camera) throw new ArgumentException("Camera name is ambiguous.");
                        camera = candidate;
                    }
                }
                if (!camera) return Failure("no_camera", "Specify an enabled camera by name or assign a MainCamera.");
            }
            else if (source == "game_view")
            {
                var gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
                if (Resources.FindObjectsOfTypeAll(gameView).Length == 0)
                    return Failure("no_game_view", "Open a Game view before capturing, or use source=camera.");
            }
            else throw new ArgumentException("source must be game_view or camera.");
            // The Editor connection is -1; never change the user's Profiler connection.
            session = Guid.NewGuid().ToString("N");
            deadline = EditorApplication.timeSinceStartup + seconds;
            settleTicks = 4;
            captureSource = source;
            captureCamera = camera;
            changedPause = EditorApplication.isPlaying && !EditorApplication.isPaused;
            EditorApplication.update += TickCallback;
            AssemblyReloadEvents.beforeAssemblyReload += ReloadCallback;
            EditorApplication.quitting += QuitCallback;
            EditorApplication.playModeStateChanged += PlayModeCallback;
            try
            {
                if (changedPause) EditorApplication.isPaused = true;
                api.SetEnabled(true, -1);
                if (!api.Local()) throw new InvalidOperationException("Unity did not enable local Frame Debugger capture.");
                Repaint();
                return Status();
            }
            catch
            {
                Release("failed");
                throw;
            }
        }

        private static void RequireCapture(string expectedHash)
        {
            if (!api.Enabled()) throw new InvalidOperationException("Frame Debugger is disabled. Start a capture first.");
            if (!string.IsNullOrEmpty(expectedHash) && expectedHash != api.EventsHash().ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Stale eventsHash. Read the event list again.");
        }

        private static void RequireOwner(string id)
        {
            if (session == null || string.IsNullOrEmpty(id) || id != session)
                throw new InvalidOperationException("A matching sessionId is required. This operation cannot modify a user-owned capture.");
        }

        private static void RequireIndex(int index)
        {
            if (index < 0 || index >= api.Count()) throw new ArgumentOutOfRangeException(nameof(index), "eventIndex is zero-based and must be inside eventCount.");
        }

        private static void RequireInspectable(int index)
        {
            Array events = api.Events();
            if (index >= events.Length || !FrameDebuggerApi.Inspectable(events.GetValue(index)))
                throw new InvalidOperationException("Raster/native render-pass inspection is disabled. Only ComputeDispatch and RayTracingDispatch are allowed.");
        }

        private static JObject Events(int offset, int count, string path)
        {
            if (offset < 0 || count < 1 || count > 2000) throw new ArgumentOutOfRangeException(nameof(offset), "offset >= 0; count must be 1..2000.");
            int hash = api.EventsHash();
            Array events = api.Events();
            var rows = new JArray();
            int end = Math.Min(offset, events.Length);
            while (end < events.Length && rows.Count < count)
            {
                int index = end++;
                object frameEvent = events.GetValue(index);
                if (!FrameDebuggerApi.Inspectable(frameEvent)) continue;
                var row = (JObject)FrameDebuggerApi.Describe(frameEvent);
                row["eventIndex"] = index;
                row["name"] = api.Name(index);
                rows.Add(row);
            }
            if (hash != api.EventsHash()) return Failure("stale_capture", "Event list changed during enumeration.");
            var result = Status();
            result["events"] = rows;
            result["offset"] = offset;
            result["total"] = events.Length;
            result["nextOffset"] = end < events.Length ? end : -1;
            SaveJson(result, path);
            return result;
        }

        private static string OutputPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An output path is required.");
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
            if (!string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output extension must be " + extension);
            if (File.Exists(full)) throw new IOException("Output already exists: " + full);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            return full;
        }

        private static void SaveJson(JObject result, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string output = OutputPath(path, ".json");
            result["path"] = output;
            using (var stream = new FileStream(output, FileMode.CreateNew))
            using (var writer = new StreamWriter(stream)) writer.Write(result.ToString());
        }

        private static JObject Failure(string code, string message) => new JObject
        {
            ["success"] = false, ["schemaVersion"] = 1, ["code"] = code,
            ["message"] = message, ["retryable"] = code == "pending", ["sessionId"] = session
        };

        private static void Repaint()
        {
            api.Repaint();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void Tick()
        {
            if (session == null) return;
            if (!api.Enabled() || !api.Local()) { Release("interrupted"); return; }
            if (EditorApplication.timeSinceStartup >= deadline) { Release("expired"); return; }
            if (settleTicks > 0)
            {
                try
                {
                    if (captureSource == "camera")
                    {
                        if (!captureCamera) { Release("camera_lost"); return; }
                        api.EnterCapture();
                        try { captureCamera.Render(); }
                        finally { api.LeaveCapture(); }
                    }
                    settleTicks--;
                    Repaint();
                }
                catch (Exception exception)
                {
                    Release("failed");
                    Debug.LogException(exception);
                }
            }
        }

        private static void Release(string state)
        {
            if (session == null) return;
            EditorApplication.update -= TickCallback;
            AssemblyReloadEvents.beforeAssemblyReload -= ReloadCallback;
            EditorApplication.quitting -= QuitCallback;
            EditorApplication.playModeStateChanged -= PlayModeCallback;
            session = null;
            captureCamera = null;
            captureSource = null;
            settleTicks = 0;
            lastState = state;
            try
            {
                if (api.Enabled() && api.Local()) api.SetEnabled(false, api.Remote());
            }
            finally
            {
                if (changedPause && EditorApplication.isPlaying && EditorApplication.isPaused) EditorApplication.isPaused = false;
                changedPause = false;
                Repaint();
            }
        }

        private static void Cleanup() => Release("interrupted");
        private static void OnPlayModeChanged(PlayModeStateChange state) => Cleanup();
    }
}
