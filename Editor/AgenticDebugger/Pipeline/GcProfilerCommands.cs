using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace VividRP.AgenticDebugger
{
    public static class GcProfilerCommands
    {
        [CliCommand("agentic_gc", "Read recorded CPU Profiler GC.Alloc data. Missing data is never reported as zero. Does not record or use Frame Debugger.",
            MainThreadRequired = true, Tags = new[] { "agentic-debugger/gc" })]
        public static JObject Execute(
            [CliArg("action", "status | frames | frame | allocation")] string action = "status",
            [CliArg("frame", "Zero-based Profiler frame index; -1 means latest (or latest count frames). Not Time.frameCount.")] int frameIndex = -1,
            [CliArg("count", "Number of frames for frames, 1..120.")] int count = 30,
            [CliArg("thread", "Thread index from the queried frame, for allocation.")] int threadIndex = -1,
            [CliArg("sample", "Raw sample index from frame.allocations, for allocation.")] int sampleIndex = -1,
            [CliArg("frame_token", "Frame identity returned by frame/frames; required for allocation.")] string frameToken = null,
            [CliArg("offset", "Allocation event offset for frame (thread/sample order).")] int offset = 0,
            [CliArg("limit", "Maximum allocation rows for frame, 1..2000.")] int limit = 200,
            [CliArg("max_samples", "Total sample scan budget for this request, 1..10000000. Exhaustion returns partial data.")] int maxSamples = 1000000)
            => GcProfilerBridge.Execute(action, frameIndex, count, threadIndex, sampleIndex, frameToken, offset, limit, maxSamples);
    }
}
