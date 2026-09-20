using System;
using System.Collections.Generic;

namespace VividRP.AgenticDebugger
{
    // Plain-data seam also used by offline tests. No Editor, graphics or UI dependencies.
    internal interface IGcFrameSource
    {
        int FirstFrame { get; }
        int LastFrame { get; }
        string FrameToken(int frame);
        int ThreadCount(int frame);
        IGcThreadData OpenThread(int frame, int thread);
    }

    internal interface IGcThreadData : IDisposable
    {
        bool Valid { get; }
        string Id { get; }
        string Name { get; }
        string Group { get; }
        int SampleCount { get; }
        bool HasGcMarker { get; }
        bool IsAllocation(int sample);
        long? AllocationBytes(int sample);
        GcStack Stack(int sample);
    }

    internal sealed class GcStack
    {
        public string state;
        public List<GcStackEntry> entries = new List<GcStackEntry>();
    }

    internal sealed class GcStackEntry
    {
        public string address, method, file;
        public long line;
    }

    internal sealed class GcAllocation
    {
        public int profilerFrameIndex, threadIndex, sampleIndex;
        public string frameToken, threadId, threadName, threadGroup, sizeState;
        public long? bytes;
        public GcStack callstack;
    }

    internal sealed class GcThreadSummary
    {
        public int threadIndex, sampleCount, scannedSamples, observedAllocations, missingSizeCount;
        public string threadId, name, group;
        public long knownBytes;
        public bool sampleCoverageComplete;
        public List<string> issues = new List<string>();
    }

    internal sealed class GcFrameSummary
    {
        public int profilerFrameIndex, displayFrame, threadCount, scannedSamples, observedAllocations, missingSizeCount;
        public string frameToken, dataState, allocationState;
        public long knownBytes;
        public long? totalBytes;
        public int? allocationCount;
        public List<string> issues = new List<string>();
        public List<GcThreadSummary> threads;
        public List<GcAllocation> allocations;
        public int allocationOffset, nextAllocationOffset = -1;
    }

    internal sealed class GcQueryException : Exception
    {
        internal readonly string Code;
        internal GcQueryException(string code, string message) : base(message) { Code = code; }
    }

    internal static class GcFrameAnalyzer
    {
        internal static GcFrameSummary Scan(IGcFrameSource source, int frame, bool details,
            int offset, int count, ref int sampleBudget)
        {
            var result = new GcFrameSummary { profilerFrameIndex = frame, displayFrame = frame + 1,
                allocationOffset = offset, threads = details ? new List<GcThreadSummary>() : null,
                allocations = details ? new List<GcAllocation>() : null };
            if (frame < source.FirstFrame || frame > source.LastFrame || source.FirstFrame < 0)
                return Missing(result, "frame_unavailable");
            result.frameToken = source.FrameToken(frame);
            if (result.frameToken == null) return Missing(result, "frame_not_ready");
            result.threadCount = source.ThreadCount(frame);
            if (result.threadCount <= 0) return Missing(result, "no_thread_data");
            bool coverage = true;
            int readableThreads = 0;
            for (int t = 0; t < result.threadCount; t++)
            {
                var thread = new GcThreadSummary { threadIndex = t };
                result.threads?.Add(thread);
                try
                {
                    using var raw = source.OpenThread(frame, t);
                    if (!raw.Valid) { thread.issues.Add("thread_unavailable"); continue; }
                    thread.threadId = raw.Id;
                    thread.name = raw.Name;
                    thread.group = raw.Group;
                    thread.sampleCount = raw.SampleCount;
                    if (raw.SampleCount <= 0) { thread.issues.Add("no_samples"); continue; }
                    if (!raw.HasGcMarker) { thread.issues.Add("gc_marker_unavailable"); continue; }
                    readableThreads++;
                    for (int s = 0; s < raw.SampleCount; s++)
                    {
                        if (sampleBudget == 0) { thread.issues.Add("sample_budget_exhausted"); break; }
                        sampleBudget--;
                        thread.scannedSamples++;
                        if (!raw.IsAllocation(s)) continue;
                        long? bytes = raw.AllocationBytes(s);
                        if (!bytes.HasValue || bytes.Value < 0) { bytes = null; thread.missingSizeCount++; }
                        else thread.knownBytes += bytes.Value;
                        int ordinal = result.observedAllocations + thread.observedAllocations++;
                        if (details && ordinal >= offset && result.allocations.Count < count)
                            result.allocations.Add(Allocation(frame, result.frameToken, t, s, raw, bytes));
                    }
                    thread.sampleCoverageComplete = thread.scannedSamples == thread.sampleCount;
                    if (thread.missingSizeCount > 0) thread.issues.Add("allocation_size_unavailable");
                }
                catch (Exception exception)
                {
                    thread.sampleCoverageComplete = false;
                    thread.issues.Add("thread_read_failed: " + exception.Message);
                }
                finally
                {
                    coverage &= thread.sampleCoverageComplete;
                    result.knownBytes += thread.knownBytes;
                    result.observedAllocations += thread.observedAllocations;
                    result.missingSizeCount += thread.missingSizeCount;
                    result.scannedSamples += thread.scannedSamples;
                    foreach (string issue in thread.issues)
                        if (!result.issues.Contains(issue)) result.issues.Add(issue);
                }
            }
            // A loaded trace or circular buffer may have changed while scanning.
            if (frame < source.FirstFrame || frame > source.LastFrame || result.frameToken != source.FrameToken(frame))
                return Missing(result, "frame_changed_during_read");
            result.dataState = readableThreads == 0 ? "missing" : coverage && result.missingSizeCount == 0 ? "complete" : "partial";
            result.totalBytes = result.dataState == "complete" ? result.knownBytes : (long?)null;
            result.allocationCount = coverage ? result.observedAllocations : (int?)null;
            result.allocationState = result.observedAllocations > 0 ? "nonzero" : result.dataState == "complete" ? "zero" : "unknown";
            if (details && (long)offset + result.allocations.Count < result.observedAllocations)
                result.nextAllocationOffset = offset + result.allocations.Count;
            return result;
        }

        private static GcFrameSummary Missing(GcFrameSummary result, string reason)
        {
            result.dataState = "missing";
            result.allocationState = "unknown";
            result.totalBytes = null;
            result.allocationCount = null;
            result.issues.Add(reason);
            // Do not expose records collected from a replaced frame as queryable allocations.
            if (reason == "frame_changed_during_read")
            {
                result.frameToken = null;
                result.allocations?.Clear();
                result.threads?.Clear();
                result.knownBytes = 0;
                result.observedAllocations = 0;
            }
            return result;
        }

        internal static GcAllocation ReadAllocation(IGcFrameSource source, int frame, int thread, int sample, string token)
        {
            if (string.IsNullOrEmpty(token)) throw new GcQueryException("frame_token_required", "Use frameToken returned by frame/frames.");
            if (frame < source.FirstFrame || frame > source.LastFrame || source.FirstFrame < 0)
                throw new GcQueryException("frame_unavailable", "The requested Profiler frame is no longer available.");
            if (token != source.FrameToken(frame)) throw new GcQueryException("stale_frame", "Frame identity changed; query the frame again.");
            if (thread < 0 || thread >= source.ThreadCount(frame)) throw new GcQueryException("invalid_thread", "Thread index is outside this frame.");
            using var raw = source.OpenThread(frame, thread);
            if (!raw.Valid) throw new GcQueryException("thread_unavailable", "Thread data is not available.");
            if (!raw.HasGcMarker) throw new GcQueryException("gc_marker_unavailable", "GC.Alloc metadata is not available.");
            if (sample < 0 || sample >= raw.SampleCount) throw new GcQueryException("invalid_sample", "Sample index is outside this thread.");
            if (!raw.IsAllocation(sample)) throw new GcQueryException("not_allocation", "The sample is not GC.Alloc.");
            long? bytes = raw.AllocationBytes(sample);
            if (bytes < 0) bytes = null;
            var result = Allocation(frame, token, thread, sample, raw, bytes);
            result.callstack = raw.Stack(sample);
            if (token != source.FrameToken(frame)) throw new GcQueryException("stale_frame", "Frame changed while reading allocation.");
            return result;
        }

        private static GcAllocation Allocation(int frame, string token, int thread, int sample, IGcThreadData raw, long? bytes)
            => new GcAllocation { profilerFrameIndex = frame, frameToken = token, threadIndex = thread,
                sampleIndex = sample, threadId = raw.Id, threadName = raw.Name, threadGroup = raw.Group,
                bytes = bytes, sizeState = bytes.HasValue ? "available" : "missing" };
    }
}
