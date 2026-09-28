using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace VividRP.AgenticDebugger
{
    public static class GpuDebuggerCommands
    {
        [CliCommand("agentic_gpu_debugger", "VividRP GPU capture: PIX with validated GPU work, or Compute/RayTracing Frame Debugger inspection.", MainThreadRequired = true)]
        public static JObject Execute(
            [CliArg("backend", "pix | frame_debugger")] string backend = "pix",
            [CliArg("action", "status | capture | release | events | event | pipeline | resources | accessed_resources | timing | counters | occupancy | drpix | analysis_status | analysis_cancel; frame_debugger also supports select.")] string action = "status",
            [CliArg("session_id", "Required for every session operation except status/capture.")] string sessionId = null,
            [CliArg("path", "New .wpix capture or Frame Debugger JSON path, relative to project root.")] string path = null,
            [CliArg("camera", "Unique enabled camera name; PIX defaults to MainCamera.")] string cameraName = null,
            [CliArg("expected_pass", "PIX: required exact GPU pass marker containing real GPU work.")] string expectedPass = null,
            [CliArg("timeout_seconds", "Capture/analysis deadline (PIX) or lease (Frame Debugger), 1..300.")] float timeoutSeconds = 120,
            [CliArg("pix_install", "PIX installation, or VIVID_PIX_INSTALL.")] string pixInstall = null,
            [CliArg("analyzer_path", "PIX validator path, or VIVID_PIX_ANALYZER.")] string analyzerPath = null,
            [CliArg("event_index", "PIX queue-local eventIndex from events; Frame Debugger index (-1 uses selection).")] int eventIndex = -1,
            [CliArg("offset", "Result page offset.")] int offset = 0,
            [CliArg("count", "Page size: PIX 1..256, Frame Debugger 1..2000.")] int count = 100,
            [CliArg("events_hash", "Frame Debugger eventsHash, or PIX captureHash from analysis result.")] string expectedEventsHash = null,
            [CliArg("include_shader_properties", "Include Frame Debugger shader properties.")] bool includeShaderProperties = false,
            [CliArg("source", "Frame Debugger: game_view or camera (explicit rerender).")] string source = "game_view",
            [CliArg("queue_id", "PIX queueId from events, required with event_index.")] int queueId = -1,
            [CliArg("marker", "PIX events: exact marker name and its descendants.")] string marker = null,
            [CliArg("counter_id", "PIX counters: -1 lists counters; an ID collects one event value.")] int counterId = -1,
            [CliArg("occupancy_type", "PIX occupancy type index; -1 lists available types/stages.")] int occupancyType = -1,
            [CliArg("occupancy_stage", "PIX occupancy stage index; supply with occupancy_type.")] int occupancyStage = -1,
            [CliArg("experiment", "PIX Dr. PIX GUID; omitted lists experiments, supplied runs one event.")] string experiment = null,
            [CliArg("analysis_id", "Request owner token required for analysis_status/analysis_cancel.")] string analysisId = null)
            => GpuDebuggerBridge.Execute(backend, action, sessionId, path, cameraName, expectedPass, timeoutSeconds,
                pixInstall, analyzerPath, eventIndex, offset, count, expectedEventsHash, includeShaderProperties, source,
                queueId, marker, counterId, occupancyType, occupancyStage, experiment, analysisId);
    }
}
