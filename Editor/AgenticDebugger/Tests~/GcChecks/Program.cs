using System;
using System.Collections.Generic;
using VividRP.AgenticDebugger;

internal static class Program
{
    private sealed class Thread : IGcThreadData
    {
        internal bool valid = true, marker = true, disposed;
        internal string id = "7", name = "Worker";
        internal bool[] events = { false };
        internal long?[] bytes = { null };
        internal GcStack stack = new GcStack { state = "unavailable" };
        internal int failSample = -1;
        public bool Valid => valid;
        public string Id => id;
        public string Name => name;
        public string Group => "Jobs";
        public int SampleCount => events.Length;
        public bool HasGcMarker => marker;
        public bool IsAllocation(int sample)
        {
            if (sample == failSample) throw new Exception("read failure");
            return events[sample];
        }
        public long? AllocationBytes(int sample) => bytes[sample];
        public GcStack Stack(int sample) => stack;
        public void Dispose() => disposed = true;
    }

    private sealed class Source : IGcFrameSource
    {
        internal int first = 4, last = 4, keys;
        internal bool missingToken, changeToken;
        internal List<Thread> threads = new List<Thread> { new Thread() };
        public int FirstFrame => first;
        public int LastFrame => last;
        public string FrameToken(int frame) => missingToken ? null : changeToken && ++keys > 1 ? "changed" : "frame-a";
        public int ThreadCount(int frame) => threads.Count;
        public IGcThreadData OpenThread(int frame, int thread) => threads[thread];
    }

    private static int checks;
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    private static GcFrameSummary Scan(Source source, int budget = 100, int offset = 0, int limit = 10)
        => GcFrameAnalyzer.Scan(source, 4, true, offset, limit, ref budget);
    private static void Fails(string code, Action action)
    {
        try { action(); }
        catch (GcQueryException e) { Check(code, e.Code == code); return; }
        throw new Exception("Expected " + code);
    }

    private static void Main()
    {
        var source = new Source();
        var result = Scan(source);
        Check("valid zero", result.dataState == "complete" && result.allocationState == "zero" && result.totalBytes == 0 && result.allocationCount == 0);
        Check("frame indices", result.profilerFrameIndex == 4 && result.displayFrame == 5);
        Check("disposed", source.threads[0].disposed);

        source = new Source { first = -1, last = -1 };
        result = Scan(source);
        Check("no recording != zero", result.dataState == "missing" && result.totalBytes == null && result.allocationCount == null && result.allocationState == "unknown");
        source = new Source { missingToken = true };
        Check("unready", Scan(source).totalBytes == null);
        source = new Source(); source.threads.Clear();
        Check("no threads", Scan(source).dataState == "missing");
        source = new Source(); source.threads[0].valid = false;
        Check("invalid thread", Scan(source).totalBytes == null && source.threads[0].disposed);
        source = new Source(); source.threads[0].marker = false;
        Check("missing marker", Scan(source).dataState == "missing");
        source = new Source(); source.threads[0].events = Array.Empty<bool>();
        Check("empty samples", Scan(source).totalBytes == null);

        source = new Source();
        source.threads[0].events = new[] { true, false, true, true };
        source.threads[0].bytes = new long?[] { 32, null, 16, 8 };
        source.threads.Add(new Thread { id = "8", events = new[] { true }, bytes = new long?[] { 64 } });
        result = Scan(source, offset: 1, limit: 2);
        Check("all threads summed", result.totalBytes == 120 && result.allocationCount == 4 && result.allocationState == "nonzero");
        Check("pagination", result.allocations.Count == 2 && result.allocations[0].sampleIndex == 2 && result.nextAllocationOffset == 3);
        Check("same names distinct IDs", result.threads[0].threadId != result.threads[1].threadId);
        var single = GcFrameAnalyzer.ReadAllocation(source, 4, 1, 0, "frame-a");
        Check("allocation identity", single.threadId == "8" && single.bytes == 64);
        Check("missing stack distinct", single.callstack.state == "unavailable" && single.sizeState == "available");
        source.threads[1].stack = new GcStack { state = "partial_symbols", entries = new List<GcStackEntry> { new GcStackEntry { address = "0x1" } } };
        Check("partial symbols", GcFrameAnalyzer.ReadAllocation(source, 4, 1, 0, "frame-a").callstack.state == "partial_symbols");
        Fails("frame_token_required", () => GcFrameAnalyzer.ReadAllocation(source, 4, 0, 0, null));
        Fails("stale_frame", () => GcFrameAnalyzer.ReadAllocation(source, 4, 0, 0, "old"));
        Fails("invalid_thread", () => GcFrameAnalyzer.ReadAllocation(source, 4, 2, 0, "frame-a"));
        Fails("invalid_sample", () => GcFrameAnalyzer.ReadAllocation(source, 4, 0, 99, "frame-a"));
        Fails("not_allocation", () => GcFrameAnalyzer.ReadAllocation(source, 4, 0, 1, "frame-a"));
        Fails("frame_unavailable", () => GcFrameAnalyzer.ReadAllocation(source, 3, 0, 0, "frame-a"));

        source.threads[0].bytes[0] = null;
        result = Scan(source);
        Check("unknown size counted", result.dataState == "partial" && result.totalBytes == null && result.knownBytes == 88 && result.allocationCount == 4 && result.missingSizeCount == 1);
        Check("unknown event preserved", result.allocations[0].sizeState == "missing" && result.allocations[0].bytes == null);
        source.threads[0].bytes[0] = -9;
        Check("negative size unknown", Scan(source).missingSizeCount == 1);
        source.threads[0].bytes[0] = 32;
        result = Scan(source, budget: 2);
        Check("budget partial", result.totalBytes == null && result.allocationCount == null && result.knownBytes == 32 && result.issues.Contains("sample_budget_exhausted"));
        source.threads[1].valid = false;
        result = Scan(source);
        Check("partial thread loss", result.dataState == "partial" && result.totalBytes == null && result.knownBytes == 56);
        source.threads[0].failSample = 2;
        Check("read error incomplete", Scan(source).totalBytes == null);
        source = new Source { changeToken = true };
        result = Scan(source);
        Check("replacement discards stale rows", result.frameToken == null && result.dataState == "missing" && result.allocations.Count == 0);
        source = new Source { changeToken = true };
        source.threads[0].events[0] = true;
        source.threads[0].bytes[0] = 8;
        Fails("stale_frame", () => GcFrameAnalyzer.ReadAllocation(source, 4, 0, 0, "frame-a"));
        source = new Source();
        for (int i = 0; i < 300; i++) source.threads.Add(new Thread());
        result = Scan(source, 1000);
        Check("no 256-thread cap", result.threadCount == 301 && result.dataState == "complete");
        source = new Source();
        result = Scan(source, budget: 0);
        Check("budget zero is unknown", result.allocationState == "unknown" && result.totalBytes == null);
        Console.WriteLine("Passed " + checks + " GC data integrity checks; no Unity native calls.");
    }
}
