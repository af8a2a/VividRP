using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace VividRP.AgenticDebugger
{
    public static class PixCaptureCommands
    {
        [CliCommand("agentic_pix", "PIX capture at VividRP camera boundaries, with queue joins and mandatory GPU work/session validation.",
            MainThreadRequired = true)]
        public static JObject Execute(
            [CliArg("action", "status | capture | release")] string action = "status",
            [CliArg("session_id", "Owner token required for release.")] string sessionId = null,
            [CliArg("path", "New .wpix file; relative to project root.")] string path = null,
            [CliArg("camera", "Unique enabled camera name; defaults to MainCamera.")] string cameraName = null,
            [CliArg("expected_pass", "Required exact GPU pass marker; must contain actual GPU work.")] string expectedPass = null,
            [CliArg("timeout_seconds", "Absolute capture/validation deadline, 1..300.")] float timeoutSeconds = 120,
            [CliArg("pix_install", "PIX Preview directory, or VIVID_PIX_INSTALL.")] string pixInstall = null,
            [CliArg("analyzer_path", "vivid-pix-analyzer.exe path, or VIVID_PIX_ANALYZER.")] string analyzerPath = null)
            => PixCaptureBridge.Execute(action, sessionId, path, cameraName, expectedPass, timeoutSeconds, pixInstall, analyzerPath);
    }
}
