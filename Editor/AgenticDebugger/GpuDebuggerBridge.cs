using Newtonsoft.Json.Linq;

namespace VividRP.AgenticDebugger
{
    /// <summary>Common agent entry point; backend-specific evidence remains intact.</summary>
    public static class GpuDebuggerBridge
    {
        public static JObject Execute(string backend = "pix", string action = "status", string sessionId = null,
            string path = null, string cameraName = null, string expectedPass = null, float timeoutSeconds = 120,
            string pixInstall = null, string analyzerPath = null, int eventIndex = -1, int offset = 0,
            int count = 100, string expectedEventsHash = null, bool includeShaderProperties = false,
            string source = "game_view", int queueId = -1, string marker = null, int counterId = -1,
            int occupancyType = -1, int occupancyStage = -1, string experiment = null, string analysisId = null)
        {
            if (backend != "pix" && backend != "frame_debugger")
                return Failure(backend, "invalid_backend", "backend must be pix or frame_debugger.");
            if (action != "status" && action != "capture")
            {
                if (string.IsNullOrEmpty(sessionId))
                    return Failure(backend, "session_mismatch", "A matching session_id is required for session operations.");
                var status = backend == "pix" ? PixCaptureBridge.Execute() : FrameDebuggerBridge.Execute();
                if ((string)status["sessionId"] != sessionId)
                    return Failure(backend, "session_mismatch", "The session does not belong to this backend or has expired.");
            }

            var result = backend == "pix"
                ? PixCaptureBridge.Execute(action, sessionId, path, cameraName, expectedPass, timeoutSeconds, pixInstall, analyzerPath,
                    queueId, eventIndex, offset, count, marker, expectedEventsHash, counterId, occupancyType, occupancyStage, experiment, analysisId)
                : FrameDebuggerBridge.Execute(action, eventIndex, offset, count, expectedEventsHash, sessionId,
                    path, timeoutSeconds, includeShaderProperties, source, cameraName);
            if (action == "status" && !string.IsNullOrEmpty(sessionId) && (string)result["sessionId"] != sessionId)
                return Failure(backend, "session_mismatch", "The session does not belong to this backend or has expired.");
            result["schemaVersion"] = 1;
            result["backend"] = backend;
            string state = (string)result["state"];
            result["ready"] = state == "ready";
            bool pending = state == "preparing" || state == "armed" || state == "capturing" ||
                state == "waiting_for_gpu" || state == "finalizing" || state == "validating" || state == "analyzing" || state == "cancelling" || (string)result["code"] == "pending";
            result["pending"] = pending;
            if (pending || state == "failed" || state == "expired" || state == "interrupted" || state == "camera_lost")
                result["success"] = false;
            if (result["code"] == null)
                result["code"] = pending ? "pending" : state == "ready" ? "ok" : state ?? "request_failed";
            return result;
        }

        private static JObject Failure(string backend, string code, string message) => new JObject
        {
            ["schemaVersion"] = 1, ["success"] = false, ["backend"] = backend,
            ["ready"] = false, ["pending"] = false, ["code"] = code, ["message"] = message
        };
    }
}
