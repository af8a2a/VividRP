using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace VividRP.AgenticDebugger
{
    public static class FrameDebuggerCommands
    {
        [CliCommand("agentic_frame_debugger", "Read Compute/RayTracing Frame Debugger events or manage a bounded local capture. Raster inspection is disabled due to a native render-pass crash.",
            MainThreadRequired = true, Tags = new[] { "agentic-debugger/frame" })]
        public static JObject Execute(
            [CliArg("action", "status | capture | events | select | event | release. Raster events and texture export are disabled.")] string action = "status",
            [CliArg("event_index", "Zero-based event index. -1 reads the current selection.")] int eventIndex = -1,
            [CliArg("offset", "Event list offset.")] int offset = 0,
            [CliArg("count", "Event page size, 1..2000.")] int count = 100,
            [CliArg("events_hash", "Optional eventsHash from a previous response; rejects a changed event list.")] string expectedEventsHash = null,
            [CliArg("session_id", "Capture owner token, required for select/release.")] string sessionId = null,
            [CliArg("path", "New JSON output file for events/event. Relative to project root.")] string path = null,
            [CliArg("timeout_seconds", "Capture lease, 1..300 seconds; restores state automatically on expiry.")] float timeoutSeconds = 60,
            [CliArg("include_shader_properties", "Include potentially large Shader property arrays in event details.")] bool includeShaderProperties = false,
            [CliArg("source", "game_view waits for normal rendering; camera explicitly re-renders a camera in a capture scope (changes temporal history).")] string source = "game_view",
            [CliArg("camera", "Enabled camera name for source=camera; default MainCamera.")] string cameraName = null)
            => FrameDebuggerBridge.Execute(action, eventIndex, offset, count, expectedEventsHash,
                sessionId, path, timeoutSeconds, includeShaderProperties, source, cameraName);
    }
}
