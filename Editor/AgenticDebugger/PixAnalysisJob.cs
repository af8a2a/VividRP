using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace VividRP.AgenticDebugger
{
    // No Unity calls. One immutable request/result pair per external process.
    internal sealed class PixAnalysisRequest
    {
        internal readonly string Action, SessionId, CapturePath, ExpectedPass, BoundaryMode, PixInstall, Analyzer;
        internal readonly string Marker, CaptureHash, Experiment;
        internal readonly int QueueId, EventIndex, Offset, Count, CounterId, OccupancyType, OccupancyStage, Timeout;

        internal static bool IsAction(string action) => action == "events" || action == "event" || action == "pipeline" ||
            action == "resources" || action == "accessed_resources" || action == "timing" || action == "counters" ||
            action == "occupancy" || action == "drpix";

        internal PixAnalysisRequest(string action, string session, string capture, string pass, string mode,
            string pix, string analyzer, int queue, int eventIndex, int offset, int count, string marker,
            string hash, int counter, int occupancyType, int occupancyStage, string experiment, float timeout)
        {
            if (!IsAction(action)) throw new ArgumentException("Unsupported PIX analysis action.");
            if (float.IsNaN(timeout) || timeout < 1 || timeout > 300 || offset < 0 || count < 1 || count > 256 ||
                queue < -1 || eventIndex < -1 || counter < -1 || occupancyType < -1 || occupancyStage < -1)
                throw new ArgumentException("timeout: 1..300, offset >= 0, count: 1..256, indices >= -1.");
            if (eventIndex >= 0 && queue < 0) throw new ArgumentException("PIX event_index requires queue_id from events.");
            if ((action == "event" || action == "pipeline" || action == "timing" || action == "accessed_resources" ||
                counter >= 0 || !string.IsNullOrEmpty(experiment)) && eventIndex < 0)
                throw new ArgumentException("This analysis requires queue_id and event_index.");
            if ((occupancyType >= 0) != (occupancyStage >= 0)) throw new ArgumentException("Supply both occupancy_type and occupancy_stage.");
            if (!string.IsNullOrEmpty(experiment) && !Guid.TryParse(experiment, out _)) throw new ArgumentException("experiment must be a GUID from drpix.");
            Action = action; SessionId = session; CapturePath = Path.GetFullPath(capture); ExpectedPass = pass;
            BoundaryMode = mode; PixInstall = pix; Analyzer = analyzer; QueueId = queue; EventIndex = eventIndex;
            if (!string.IsNullOrEmpty(hash))
            {
                if (hash.Length != 64) throw new ArgumentException("captureHash must be a SHA256 digest.");
                foreach (char c in hash) if (!Uri.IsHexDigit(c)) throw new ArgumentException("captureHash must be hexadecimal.");
            }
            Offset = offset; Count = count; Marker = marker; CaptureHash = hash?.ToLowerInvariant(); CounterId = counter;
            OccupancyType = occupancyType; OccupancyStage = occupancyStage; Experiment = experiment;
            Timeout = (int)Math.Ceiling(timeout);
        }

        internal string Arguments(string requestId, string output)
        {
            var b = new StringBuilder(Action).Append(' ').Append(Quote(PixInstall)).Append(' ').Append(Quote(CapturePath))
                .Append(' ').Append(Quote(SessionId)).Append(' ').Append(Quote(ExpectedPass)).Append(' ').Append(Quote(output))
                .Append(' ').Append(BoundaryMode).Append(" --request_id ").Append(Quote(requestId))
                .Append(" --timeout_seconds ").Append(Timeout).Append(" --offset ").Append(Offset).Append(" --count ").Append(Count);
            if (QueueId >= 0) b.Append(" --queue ").Append(QueueId);
            if (EventIndex >= 0) b.Append(" --event ").Append(EventIndex);
            if (CounterId >= 0) b.Append(" --counter ").Append(CounterId);
            if (OccupancyType >= 0) b.Append(" --type ").Append(OccupancyType).Append(" --stage ").Append(OccupancyStage);
            if (!string.IsNullOrEmpty(Marker)) b.Append(" --marker ").Append(Quote(Marker));
            if (!string.IsNullOrEmpty(CaptureHash)) b.Append(" --capture_hash ").Append(Quote(CaptureHash));
            if (!string.IsNullOrEmpty(Experiment)) b.Append(" --experiment ").Append(Quote(Experiment));
            return b.ToString();
        }

        internal static string Quote(string value)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { ++slashes; continue; }
                output.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); output.Append(c); slashes = 0;
            }
            output.Append('\\', slashes * 2); return output.Append('"').ToString();
        }
    }

    internal sealed class PixAnalysisJob : IDisposable
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal readonly string Output;
        internal readonly PixAnalysisRequest Request;
        internal string State { get; private set; } = "analyzing";
        internal string Code { get; private set; } = "pending";
        internal string Message { get; private set; }
        internal JObject Result { get; private set; }
        internal bool Terminal => State == "ready" || State == "failed";
        private readonly Process process;
        private readonly double deadline;
        private readonly StringBuilder diagnostics = new StringBuilder();
        private bool stopping, disposed;
        private static readonly DataReceivedEventHandler DrainOutput = (_, __) => { };

        internal PixAnalysisJob(PixAnalysisRequest request, string output, double now)
        {
            Request = request;
            Output = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? request.CapturePath + ".analysis." + Id + ".json" : output);
            if (File.Exists(Output)) throw new IOException("Analysis output already exists; use a new file.");
            if (!string.Equals(Path.GetExtension(Output), ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Analysis output must end in .json.");
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            // Native watchdog owns the real deadline. A small grace interval lets
            // it write failure evidence; the Editor can still terminate its child.
            deadline = now + request.Timeout + 5;
            process = new Process { StartInfo = new ProcessStartInfo(request.Analyzer)
            {
                Arguments = request.Arguments(Id, Output), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            } };
            process.OutputDataReceived += DrainOutput;
            process.ErrorDataReceived += OnError;
            try
            {
                if (!process.Start()) throw new IOException("Could not start vivid-pix-analyzer.");
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
            }
            catch { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } process.Dispose(); throw; }
        }
        private void OnError(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (diagnostics)
            {
                int remaining = 16384 - diagnostics.Length;
                if (remaining > 0) diagnostics.Append(args.Data, 0, Math.Min(args.Data.Length, remaining)).AppendLine();
            }
        }
        internal void Tick(double now)
        {
            if (Terminal || disposed) return;
            if (!process.HasExited)
            {
                if (now >= deadline && !stopping) Cancel("analysis_timeout", "Analysis exceeded its process deadline.");
                return;
            }
            if (stopping) { State = "failed"; return; }
            try
            {
                if (!File.Exists(Output)) throw new IOException("Analyzer exited without JSON evidence (exit " + process.ExitCode + ").");
                if (new FileInfo(Output).Length > 8 * 1024 * 1024) throw new IOException("Analysis result exceeded 8 MiB.");
                Result = JObject.Parse(File.ReadAllText(Output));
                if (!Matches(Result, Request, Id)) throw new IOException("Analyzer response identity/schema did not match this request.");
                bool accepted = process.ExitCode == 0 && (bool?)Result["success"] == true && (string)Result["code"] == "ok";
                State = accepted ? "ready" : "failed";
                Code = accepted ? "ok" : (string)Result["code"] == "ok" ? "analysis_process_failed" : (string)Result["code"];
            }
            catch (Exception e) { State = "failed"; Code = "invalid_analysis_result"; Message = e.Message; }
        }
        internal static bool Matches(JObject result, PixAnalysisRequest request, string id)
        {
            if ((int?)result["schemaVersion"] != 3 || (string)result["requestId"] != id || (string)result["sessionId"] != request.SessionId ||
                (string)result["action"] != request.Action || (string)result["expectedPass"] != request.ExpectedPass ||
                (string)result["boundaryMode"] != request.BoundaryMode ||
                !string.Equals((string)result["capturePath"], request.CapturePath, StringComparison.OrdinalIgnoreCase) ||
                ((int?)result["queueId"] ?? -1) != request.QueueId || ((int?)result["eventIndex"] ?? -1) != request.EventIndex)
                return false;
            if ((bool?)result["success"] != true) return !string.IsNullOrEmpty((string)result["code"]);
            string hash = (string)result["captureHash"];
            if (hash == null || hash.Length != 64 || result["data"] == null || result["data"].Type == JTokenType.Null) return false;
            for (int i = 0; i < hash.Length; ++i) if (!Uri.IsHexDigit(hash[i])) return false;
            return string.IsNullOrEmpty(request.CaptureHash) || string.Equals(hash, request.CaptureHash, StringComparison.OrdinalIgnoreCase);
        }
        internal void Cancel(string code = "cancelled", string message = "Analysis cancelled by owner.")
        {
            if (Terminal || disposed) return;
            if (!process.HasExited) process.Kill();
            Code = code; Message = message; stopping = true;
            State = process.HasExited ? "failed" : "cancelling";
        }
        internal JObject Snapshot()
        {
            string errors; lock (diagnostics) errors = diagnostics.ToString();
            return new JObject
            {
                ["success"] = State == "ready", ["backend"] = "pix", ["schemaVersion"] = 1,
                ["sessionId"] = Request.SessionId, ["analysisId"] = Id, ["state"] = State, ["code"] = Code,
                ["captureState"] = "ready", ["action"] = Request.Action, ["resultPath"] = Output,
                ["message"] = Message, ["diagnostics"] = errors, ["result"] = Result?.DeepClone()
            };
        }
        // Disposing detaches; it does not kill the external process. On Editor
        // reload/exit it can finish independently, bounded by its own watchdog.
        public void Dispose()
        {
            if (disposed) return;
            process.OutputDataReceived -= DrainOutput; process.ErrorDataReceived -= OnError;
            process.Dispose(); disposed = true;
        }
    }
}
