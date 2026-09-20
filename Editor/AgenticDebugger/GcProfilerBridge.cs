using System;
using Newtonsoft.Json.Linq;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace VividRP.AgenticDebugger
{
    /// <summary>Read-only CPU Profiler queries on the Editor main thread. No recording or rendering callbacks.</summary>
    public static class GcProfilerBridge
    {
        public static JObject Execute(string action = "status", int frameIndex = -1, int count = 30,
            int threadIndex = -1, int sampleIndex = -1, string frameToken = null,
            int offset = 0, int limit = 200, int maxSamples = 1000000)
        {
            try
            {
                if (action != "status" && action != "frames" && action != "frame" && action != "allocation")
                    throw new GcQueryException("invalid_action", "Use status, frames, frame or allocation.");
                if (frameIndex < -1 || count < 1 || count > 120 || offset < 0 || limit < 1 || limit > 2000 || maxSamples < 1 || maxSamples > 10000000)
                    throw new GcQueryException("invalid_arguments", "frame >= -1, count 1..120, offset >= 0, limit 1..2000, max_samples 1..10000000.");
                var source = new GcProfilerSource();
                var result = new JObject { ["success"] = true, ["schemaVersion"] = 1,
                    ["unityVersion"] = Application.unityVersion, ["readerSessionId"] = GcProfilerSource.ReaderSession,
                    ["scope"] = "Recorded CPU Profiler samples across available threads; not live heap size or GC collection pauses." };
                if (action == "status")
                {
                    bool available = source.FirstFrame >= 0 && source.LastFrame >= source.FirstFrame;
                    result["dataState"] = available ? "frames_available" : "missing";
                    result["firstFrameIndex"] = available ? new JValue(source.FirstFrame) : JValue.CreateNull();
                    result["lastFrameIndex"] = available ? new JValue(source.LastFrame) : JValue.CreateNull();
                    result["recording"] = Profiler.enabled;
                    result["profileEditor"] = ProfilerDriver.profileEditor;
                    result["connectedProfiler"] = ProfilerDriver.connectedProfiler;
                    result["target"] = ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler);
                    result["callstackCapability"] = "Per-allocation only; current recording settings cannot prove historical stack availability.";
                    return result;
                }
                int frame = frameIndex == -1 ? source.LastFrame : frameIndex;
                if (action == "allocation")
                    result["allocation"] = JObject.FromObject(GcFrameAnalyzer.ReadAllocation(source, frame, threadIndex, sampleIndex, frameToken));
                else if (action == "frame")
                    result["frame"] = JObject.FromObject(GcFrameAnalyzer.Scan(source, frame, true, offset, limit, ref maxSamples));
                else
                {
                    int start = frameIndex == -1 ? Math.Max(source.FirstFrame, source.LastFrame - count + 1) : frameIndex;
                    var frames = new JArray();
                    if (source.FirstFrame >= 0 && source.LastFrame >= source.FirstFrame)
                    {
                        long end = Math.Min((long)start + count - 1, source.LastFrame);
                        for (int f = start; f <= end; f++)
                            frames.Add(JObject.FromObject(GcFrameAnalyzer.Scan(source, f, false, 0, 0, ref maxSamples)));
                    }
                    result["frames"] = frames;
                    result["dataState"] = frames.Count == 0 ? "missing" : "see_frames";
                }
                return result;
            }
            catch (Exception exception)
            {
                return new JObject { ["success"] = false, ["schemaVersion"] = 1,
                    ["code"] = exception is GcQueryException query ? query.Code : "profiler_read_failed", ["message"] = exception.Message };
            }
        }
    }
}
