using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using VividRP.AgenticDebugger;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); ++checks; }
    static string root;
    static PixAnalysisRequest Request(string action = "events", int queue = -1, int eventIndex = -1, int count = 100,
        string hash = null, string behavior = "ok", float timeout = 2) => new(action, "session", Path.Combine(root, "capture.wpix"),
        "Pass", "all", behavior, Environment.ProcessPath, queue, eventIndex, 0, count, "marker \"quoted\" \\", hash, -1, -1, -1, null, timeout);
    static JObject Result(PixAnalysisRequest request, string id) => new()
    {
        ["schemaVersion"] = 3, ["success"] = true, ["code"] = "ok", ["requestId"] = id,
        ["sessionId"] = request.SessionId, ["action"] = request.Action, ["capturePath"] = request.CapturePath,
        ["expectedPass"] = request.ExpectedPass, ["boundaryMode"] = request.BoundaryMode,
        ["queueId"] = request.QueueId < 0 ? null : request.QueueId, ["eventIndex"] = request.EventIndex < 0 ? null : request.EventIndex,
        ["captureHash"] = new string('a', 64), ["data"] = new JObject()
    };
    static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
    static void Wait(PixAnalysisJob job)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!job.Terminal && DateTime.UtcNow < end) { job.Tick(0); Thread.Sleep(10); }
        Check(job.Terminal, "child process completed");
    }
    static int Main(string[] args)
    {
        // Exercise the actual process adapter with a controlled child, no Unity.
        if (args.Length >= 7 && args[0] == "events")
        {
            string behavior = args[1];
            if (behavior == "hang") { Thread.Sleep(30000); return 0; }
            if (behavior == "delayed") Thread.Sleep(300);
            string id = args[Array.IndexOf(args, "--request_id") + 1];
            var r = new JObject { ["schemaVersion"] = 3, ["success"] = true, ["code"] = "ok", ["requestId"] = id,
                ["action"] = args[0], ["sessionId"] = args[3], ["capturePath"] = args[2], ["expectedPass"] = args[4],
                ["boundaryMode"] = args[6], ["queueId"] = null, ["eventIndex"] = null, ["captureHash"] = new string('a', 64),
                ["data"] = new JObject { ["marker"] = args[Array.IndexOf(args, "--marker") + 1] } };
            if (behavior == "stale") r["requestId"] = "old";
            if (behavior != "missing") File.WriteAllText(args[5], r.ToString());
            return behavior == "bad_exit" ? 2 : 0;
        }
        root = Path.Combine(Path.GetTempPath(), "vivid-pix-m2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var request = Request(); var result = Result(request, "id");
        Check(PixAnalysisJob.Matches(result, request, "id"), "valid evidence identity");
        foreach (string field in new[] { "requestId", "sessionId", "action", "capturePath", "expectedPass", "boundaryMode", "captureHash" })
        { var broken = (JObject)result.DeepClone(); broken[field] = "old"; Check(!PixAnalysisJob.Matches(broken, request, "id"), "stale " + field); }
        var selected = Request("pipeline", 19, 6); var wrongQueue = Result(selected, "id"); wrongQueue["queueId"] = 20;
        Check(!PixAnalysisJob.Matches(wrongQueue, selected, "id"), "queue-local event identity");
        Check(!PixAnalysisJob.Matches(result, Request(hash: new string('b', 64)), "id"), "different capture digest");
        Reject(() => Request(count: 257), "bounded result page");
        Reject(() => Request("event", eventIndex: 6), "queue required");
        Reject(() => Request("timing"), "event required");
        Reject(() => Request(timeout: float.NaN), "invalid deadline");
        Reject(() => Request(hash: "short"), "invalid digest");
        foreach (string behavior in new[] { "ok", "stale", "missing", "bad_exit" })
        {
            using var job = new PixAnalysisJob(Request(behavior: behavior), null, 0); Wait(job);
            Check((job.State == "ready") == (behavior == "ok"), "process evidence " + behavior);
            if (behavior == "ok") Check((string)job.Result["data"]["marker"] == request.Marker, "Windows argument quoting survives child process");
        }
        using (var job = new PixAnalysisJob(Request(behavior: "hang"), null, 0))
        { job.Tick(8); Wait(job); Check(job.Code == "analysis_timeout", "bounded process timeout"); }
        using (var job = new PixAnalysisJob(Request(behavior: "hang"), null, 0))
        { job.Cancel(); Wait(job); Check(job.Code == "cancelled", "owner cancellation"); }
        using (var job = new PixAnalysisJob(Request(), Path.Combine(root, "no-overwrite.json"), 0))
        { Wait(job); bool rejected = false; try { using var duplicate = new PixAnalysisJob(Request(), job.Output, 0); } catch (IOException) { rejected = true; } Check(rejected, "preserve existing evidence"); }
        string detachedOutput;
        using (var job = new PixAnalysisJob(Request(behavior: "delayed"), null, 0)) detachedOutput = job.Output;
        var detachedDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(detachedOutput) && DateTime.UtcNow < detachedDeadline) Thread.Sleep(10);
        Check(File.Exists(detachedOutput), "analysis survives owner detachment/reload");
        if (args.Length == 3)
        {
            var real = new PixAnalysisRequest("events", "smoke", args[2], "AsyncSmokePass", "all", args[1], args[0],
                -1, -1, 0, 256, "AsyncSmokePass", null, -1, -1, -1, null, 60);
            using var job = new PixAnalysisJob(real, null, 0); Wait(job);
            Check(job.State == "ready" && (int)job.Result["data"]["total"] > 0, "production analyzer process/schema integration");
        }
        Console.WriteLine(checks + " M2 request/process checks passed. Evidence: " + root);
        return 0;
    }
}
