namespace VividRP.Runtime
{
    internal readonly struct VTDebugStats
    {
        internal VTDebugStats(
            int activeSpaceCount,
            int residentPageCount,
            int freePageCount,
            int pendingUploadCount,
            int evictionCount,
            int faultCount,
            int deduplicatedRequestCount,
            int feedbackOverflowCount,
            int inFlightUploadBatchCount,
            int duplicateUploadCount,
            int skippedUploadCount,
            int fallbackSampleCount,
            int lastReadbackFrame,
            string statusMessage)
        {
            ActiveSpaceCount = activeSpaceCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            PendingUploadCount = pendingUploadCount;
            EvictionCount = evictionCount;
            FaultCount = faultCount;
            DeduplicatedRequestCount = deduplicatedRequestCount;
            FeedbackOverflowCount = feedbackOverflowCount;
            InFlightUploadBatchCount = inFlightUploadBatchCount;
            DuplicateUploadCount = duplicateUploadCount;
            SkippedUploadCount = skippedUploadCount;
            FallbackSampleCount = fallbackSampleCount;
            LastReadbackFrame = lastReadbackFrame;
            StatusMessage = statusMessage;
        }

        internal int ActiveSpaceCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int PendingUploadCount { get; }

        internal int EvictionCount { get; }

        internal int FaultCount { get; }

        internal int DeduplicatedRequestCount { get; }

        internal int FeedbackOverflowCount { get; }

        internal int InFlightUploadBatchCount { get; }

        internal int DuplicateUploadCount { get; }

        internal int SkippedUploadCount { get; }

        internal int FallbackSampleCount { get; }

        internal int LastReadbackFrame { get; }

        internal string StatusMessage { get; }
    }

    internal static class VTDebugStatsRegistry
    {
        private static VTDebugStats s_LastStats;

        internal static VTDebugStats LastStats => s_LastStats;

        internal static void Report(in VTDebugStats stats)
        {
            s_LastStats = stats;
        }

        internal static void Clear()
        {
            s_LastStats = default;
        }
    }
}
