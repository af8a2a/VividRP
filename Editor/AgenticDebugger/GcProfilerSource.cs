using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace VividRP.AgenticDebugger
{
    internal sealed class GcProfilerSource : IGcFrameSource
    {
        internal static readonly string ReaderSession = Guid.NewGuid().ToString("N");
        public int FirstFrame => ProfilerDriver.firstFrameIndex;
        public int LastFrame => ProfilerDriver.lastFrameIndex;

        public int ThreadCount(int frame)
        {
            using var iterator = new ProfilerFrameDataIterator();
            return iterator.GetThreadCount(frame);
        }

        public string FrameToken(int frame)
        {
            using var raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (!raw.valid) return null;
            return string.Join(":", ReaderSession, ProfilerDriver.connectedProfiler.ToString(CultureInfo.InvariantCulture),
                frame.ToString(CultureInfo.InvariantCulture), raw.frameStartTimeNs.ToString(CultureInfo.InvariantCulture),
                raw.frameTimeNs.ToString(CultureInfo.InvariantCulture), raw.sampleCount.ToString(CultureInfo.InvariantCulture),
                ThreadCount(frame).ToString(CultureInfo.InvariantCulture));
        }

        public IGcThreadData OpenThread(int frame, int thread) => new ThreadData(ProfilerDriver.GetRawFrameDataView(frame, thread));

        private sealed class ThreadData : IGcThreadData
        {
            private readonly RawFrameDataView raw;
            private readonly int marker;
            public ThreadData(RawFrameDataView view)
            {
                raw = view;
                try { marker = raw.valid ? raw.GetMarkerId("GC.Alloc") : FrameDataView.invalidMarkerId; }
                catch { raw.Dispose(); throw; }
            }
            public bool Valid => raw.valid;
            public string Id => raw.threadId.ToString(CultureInfo.InvariantCulture);
            public string Name => raw.threadName;
            public string Group => raw.threadGroupName;
            public int SampleCount => raw.sampleCount;
            public bool HasGcMarker => marker != FrameDataView.invalidMarkerId;
            public bool IsAllocation(int sample) => raw.GetSampleMarkerId(sample) == marker;
            public long? AllocationBytes(int sample) => raw.GetSampleMetadataCount(sample) > 0 ? raw.GetSampleMetadataAsLong(sample, 0) : (long?)null;
            public void Dispose() => raw.Dispose();

            public GcStack Stack(int sample)
            {
                var result = new GcStack { state = "unavailable" };
                var addresses = new List<ulong>();
                raw.GetSampleCallstack(sample, addresses);
                if (addresses.Count == 0) return result;
                result.state = "resolved";
                foreach (ulong address in addresses)
                {
                    var entry = new GcStackEntry { address = "0x" + address.ToString("x", CultureInfo.InvariantCulture) };
                    result.entries.Add(entry);
                    try
                    {
                        var method = raw.ResolveMethodInfo(address);
                        entry.method = method.methodName;
                        entry.file = method.sourceFileName;
                        entry.line = method.sourceFileLine;
                        if (string.IsNullOrEmpty(entry.method)) result.state = "partial_symbols";
                    }
                    catch { result.state = "partial_symbols"; }
                }
                return result;
            }
        }
    }
}
