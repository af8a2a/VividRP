using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace VividRP.Runtime
{
    internal enum VTStreamChunkState
    {
        Queued,
        Reading,
        Decoding,
        Ready,
        Failed,
    }

    internal sealed class VTChunkLease : IDisposable
    {
        internal VTChunkLease NextFree;

        internal VTChunkLease()
        {
        }

        private VTStreamChunkManager m_Manager;
        private VTStreamChunkManager.ChunkEntry m_Entry;

        internal VTChunkLease(VTStreamChunkManager manager, VTStreamChunkManager.ChunkEntry entry)
        {
            Reset(manager, entry);
        }

        internal VTStreamChunkState State => m_Entry?.State ?? VTStreamChunkState.Failed;

        internal string Error => m_Entry?.Error;

        internal void PromotePriority(in VTRequestPriorityKey priorityKey)
        {
            if (m_Manager != null && m_Entry != null)
                m_Manager.PromotePriority(m_Entry, priorityKey);
        }

        internal bool TryGetTilePayload(
            in VividVirtualTextureTilePayloadLocation location,
            out VividVirtualTextureTilePayload payload)
        {
            payload = default;
            return m_Manager != null
                   && m_Entry != null
                   && m_Manager.TryGetTilePayload(m_Entry, location, out payload);
        }

        public void Dispose()
        {
            VTStreamChunkManager manager = m_Manager;
            VTStreamChunkManager.ChunkEntry entry = m_Entry;
            m_Manager = null;
            m_Entry = null;
            if (manager == null)
                return;

            manager.Release(entry);
            manager.ReturnLease(this);
        }

        internal void Reset(VTStreamChunkManager manager, VTStreamChunkManager.ChunkEntry entry)
        {
            m_Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }
    }

    internal sealed class VTStreamChunkManager : IDisposable
    {
        internal readonly struct ChunkKey : IEquatable<ChunkKey>
        {
            internal ChunkKey(
                string path,
                uint contentVersion,
                in VividVirtualTextureTilePayloadLocation location)
            {
                Path = path ?? string.Empty;
                ContentVersion = contentVersion;
                ChunkIndex = location.ChunkIndex;
                SyntheticFileOffset = (location.Flags & VividVirtualTextureChunkFlags.LegacySynthetic) != 0
                    ? location.FileOffset
                    : 0;
            }

            internal string Path { get; }

            internal uint ContentVersion { get; }

            internal int ChunkIndex { get; }

            internal long SyntheticFileOffset { get; }

            public bool Equals(ChunkKey other)
            {
                return ContentVersion == other.ContentVersion
                       && ChunkIndex == other.ChunkIndex
                       && SyntheticFileOffset == other.SyntheticFileOffset
                       && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(Path),
                    ContentVersion,
                    ChunkIndex,
                    SyntheticFileOffset);
            }
        }

        internal sealed class ChunkEntry
        {
            internal ChunkEntry NextFree;
            internal int BatchReferenceCount;
            internal bool Retired;
            internal ChunkKey Key;
            internal VividVirtualTextureTilePayloadLocation Location;
            internal VTRequestPriorityKey PriorityKey;
            internal VTStreamChunkState State;
            internal int ReferenceCount;
            internal int RetryCount;
            internal byte[] StoredData;
            internal NativeArray<byte> NativeStoredData;
            internal byte[] DecodedData;
            internal DecodeWorker DecodeWorker;
            internal string Error;
            internal ChunkEntry LruPrevious;
            internal ChunkEntry LruNext;
        }

        internal readonly struct DecodeResult
        {
            internal DecodeResult(byte[] data, string error)
            {
                Data = data;
                Error = error;
            }

            internal byte[] Data { get; }

            internal string Error { get; }

            internal bool Succeeded => Data != null && Error == null;
        }

        // One reusable work slot per thread. The main thread owns assignment/release;
        // the volatile completion flag publishes the result before the slot is reused.
        internal sealed class DecodeWorker : IDisposable
        {
            private readonly Func<object, DecodeResult> m_Work;
            private readonly AutoResetEvent m_Wakeup = new(false);
            private readonly Thread m_Thread;
            private ChunkEntry m_Entry;
            private DecodeResult m_Result;
            private volatile bool m_Completed;
            private volatile bool m_Stop;
            private bool m_Disposed;

            internal DecodeWorker(Func<object, DecodeResult> work)
            {
                m_Work = work;
                m_Thread = new Thread(Run) { IsBackground = true, Name = "VT Chunk Decode" };
                if (ExecutionContext.IsFlowSuppressed())
                    m_Thread.Start();
                else
                {
                    using (ExecutionContext.SuppressFlow())
                        m_Thread.Start();
                }
            }

            internal bool IsIdle => m_Entry == null;
            internal bool IsCompleted => m_Completed;

            internal void Start(ChunkEntry entry)
            {
                m_Entry = entry;
                m_Wakeup.Set();
            }

            internal DecodeResult TakeResult()
            {
                DecodeResult result = m_Result;
                m_Result = default;
                m_Entry = null;
                m_Completed = false;
                return result;
            }

            private void Run()
            {
                while (true)
                {
                    m_Wakeup.WaitOne();
                    ChunkEntry entry = m_Entry;
                    if (entry != null && !m_Completed)
                    {
                        try
                        {
                            m_Result = m_Work(entry);
                        }
                        catch (Exception exception)
                        {
                            m_Result = new DecodeResult(null, exception.GetBaseException().Message);
                        }
                        m_Completed = true;
                    }
                    if (m_Stop)
                        return;
                }
            }

            internal void RequestStop()
            {
                m_Stop = true;
                m_Wakeup.Set();
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                RequestStop();
                m_Thread.Join();
                m_Wakeup.Dispose();
                m_Entry = null;
                m_Result = default;
                m_Disposed = true;
            }
        }

        private sealed class ActiveBatch
        {
            internal IVTIOBatch Batch;
            internal readonly List<ChunkEntry> Entries = new();
        }

        private sealed class ActiveBatchComparer : IComparer<ActiveBatch>
        {
            internal static readonly ActiveBatchComparer Instance = new();

            private ActiveBatchComparer()
            {
            }

            public int Compare(ActiveBatch left, ActiveBatch right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null || left.Entries.Count == 0)
                    return 1;
                if (right == null || right.Entries.Count == 0)
                    return -1;

                ChunkEntry leftEntry = left.Entries[0];
                ChunkEntry rightEntry = right.Entries[0];
                int priorityCompare = VTRequestPriorityUtility.CompareForIO(
                    leftEntry.PriorityKey,
                    rightEntry.PriorityKey);
                if (priorityCompare != 0)
                    return priorityCompare;

                int pathCompare = string.Compare(
                    leftEntry.Key.Path,
                    rightEntry.Key.Path,
                    StringComparison.OrdinalIgnoreCase);
                return pathCompare != 0
                    ? pathCompare
                    : leftEntry.Location.FileOffset.CompareTo(rightEntry.Location.FileOffset);
            }
        }

        private sealed class QueuedEntryComparer : IComparer<ChunkEntry>
        {
            internal QueuedEntryComparer()
            {
            }

            public int Compare(ChunkEntry left, ChunkEntry right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return 1;
                if (right == null)
                    return -1;

                int priorityCompare = VTRequestPriorityUtility.CompareForIO(
                    left.PriorityKey,
                    right.PriorityKey);
                if (priorityCompare != 0)
                    return priorityCompare;

                int pathCompare = string.Compare(
                    left.Key.Path,
                    right.Key.Path,
                    StringComparison.OrdinalIgnoreCase);
                return pathCompare != 0
                    ? pathCompare
                    : left.Location.FileOffset.CompareTo(right.Location.FileOffset);
            }
        }

        private static VTStreamChunkManager s_Shared;
        private static bool s_DirectStorageFallbackWarningLogged;
        private static readonly Comparison<ActiveBatch> s_ActiveBatchComparison = ActiveBatchComparer.Instance.Compare;
        private static readonly Comparison<ChunkEntry> s_QueuedEntryComparison = new QueuedEntryComparer().Compare;

        private readonly Dictionary<ChunkKey, ChunkEntry> m_Entries = new(64);
        private readonly Dictionary<(string Path, uint Version), (int Chunks, int Leases)> m_PreparedAssets = new();
        private int m_PreparedChunkCount;
        private int m_PreparedLeaseCount;
        private int m_PreparedReadByteCapacity;
        private int m_EntryCapacity = 64;
        private int m_LeaseCapacity = 64;
        private readonly List<ChunkEntry> m_QueuedEntries = new(64);
        private readonly List<ChunkEntry> m_PendingDecodeEntries = new(64);
        private readonly List<ChunkEntry> m_DecodingEntries = new(64);
        private readonly List<ActiveBatch> m_ActiveBatches = new(64);
        private readonly List<ActiveBatch> m_RetainedReadBatches = new(64);
        private readonly Stack<ActiveBatch> m_ActiveBatchPool = new(64);
        private readonly List<ChunkEntry> m_SubmissionEntries = new();
        private readonly List<VTIOReadCommand> m_SubmissionCommands = new(64);
        private VTChunkLease m_FreeLease;
        private ChunkEntry m_FreeEntry;
        private ChunkEntry m_ReadyLruFirst;
        private ChunkEntry m_ReadyLruLast;
        private readonly HashSet<string> m_DirectStorageRejectedPaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<object, DecodeResult> m_DecodeWork;
        private readonly List<DecodeWorker> m_DecodeWorkers = new(64);
        private IVTIOBackend m_IOBackend;
        private IVTIOBackend m_FallbackIOBackend;
        private VividVirtualTextureIOBackendMode m_BackendMode = VividVirtualTextureIOBackendMode.Auto;
        private int m_MaxInFlightChunkCount = 64;
        private int m_DecodeConcurrency = Mathf.Clamp(SystemInfo.processorCount / 4, 2, 8);
        private long m_DecodedCacheBudget = 32L * 1024 * 1024;
        private long m_ReadyByteCount;
        private int m_InFlightChunkCount;
        private int m_LastIOSaturationCount;
        private int m_LastDecodeSaturationCount;
        private int m_LastCacheAllocationFailureCount;
        private bool m_BackendNeedsReplacement;
        private bool m_Disposed;

        static VTStreamChunkManager()
        {
            // Mono lazily allocates generic sorting helpers even with cached comparisons.
            Array.Sort(new ChunkEntry[2], s_QueuedEntryComparison);
            Array.Sort(new ActiveBatch[2], s_ActiveBatchComparison);
        }

        internal VTStreamChunkManager(Func<object, DecodeResult> decodeWork = null)
        {
            for (int index = 0; index < m_MaxInFlightChunkCount; index++)
            {
                m_FreeEntry = new ChunkEntry { NextFree = m_FreeEntry };
                m_FreeLease = new VTChunkLease { NextFree = m_FreeLease };
            }
            m_IOBackend = CreateBackend(m_BackendMode);
            m_DecodeWork = decodeWork ?? DecodeEntry;
            EnsureDecodeWorkers();
        }

        internal static VTStreamChunkManager Shared => s_Shared ??= new VTStreamChunkManager();

        internal static long SharedReadyByteCount => s_Shared?.m_ReadyByteCount ?? 0L;

        internal static long SharedDecodedCacheBudget => s_Shared?.m_DecodedCacheBudget ?? 0L;

        internal static void ResetShared()
        {
            VTStreamChunkManager previous = s_Shared;
            s_Shared = null;
            previous?.Dispose();
        }

        internal static void ResetSharedState()
        {
            VTStreamChunkManager previous = s_Shared;
            if (previous == null)
                return;

            VividVirtualTextureIOBackendMode backendMode = previous.m_BackendMode;
            int maxInFlightChunkCount = previous.m_MaxInFlightChunkCount;
            int decodeConcurrency = previous.m_DecodeConcurrency;
            int decodedCacheBudgetMiB = (int)Math.Min(
                int.MaxValue,
                previous.m_DecodedCacheBudget / (1024L * 1024L));

            s_Shared = null;
            previous.Dispose();

            var replacement = new VTStreamChunkManager();
            replacement.Configure(
                backendMode,
                maxInFlightChunkCount,
                decodeConcurrency,
                decodedCacheBudgetMiB);
            foreach (var asset in previous.m_PreparedAssets)
                replacement.PrepareAsset(asset.Key.Path, asset.Key.Version, asset.Value.Chunks, asset.Value.Leases,
                    previous.m_PreparedReadByteCapacity);
            s_Shared = replacement;
        }

        internal int PendingChunkCount => m_InFlightChunkCount;

        internal int PendingDecodeCount => m_PendingDecodeEntries.Count;

        internal int ActiveDecodeCount => m_DecodingEntries.Count;

        internal int LastIOSaturationCount => m_LastIOSaturationCount;

        internal int LastDecodeSaturationCount => m_LastDecodeSaturationCount;

        internal int LastCacheAllocationFailureCount => m_LastCacheAllocationFailureCount;

        internal int LastPressureCount => Mathf.Max(
            m_LastIOSaturationCount,
            Mathf.Max(m_LastDecodeSaturationCount, m_LastCacheAllocationFailureCount));

        internal void Configure(
            VividVirtualTextureIOBackendMode backendMode,
            int maxInFlightChunkCount,
            int decodeConcurrency,
            int decodedCacheBudgetMiB)
        {
            m_MaxInFlightChunkCount = Mathf.Max(1, maxInFlightChunkCount);
            if (m_PendingDecodeEntries.Capacity < m_MaxInFlightChunkCount)
                m_PendingDecodeEntries.Capacity = m_MaxInFlightChunkCount;
            if (m_ActiveBatches.Capacity < m_MaxInFlightChunkCount)
                m_ActiveBatches.Capacity = m_MaxInFlightChunkCount;
            if (m_RetainedReadBatches.Capacity < m_MaxInFlightChunkCount)
                m_RetainedReadBatches.Capacity = m_MaxInFlightChunkCount;
            EnsureEntryCapacity();
            EnsureLeaseCapacity();
            int clampedDecodeConcurrency = Mathf.Clamp(decodeConcurrency, 1, 64);
            m_DecodeConcurrency = clampedDecodeConcurrency;
            EnsureDecodeWorkers();

            m_DecodedCacheBudget = Math.Max(0, decodedCacheBudgetMiB) * 1024L * 1024L;
            if (backendMode != m_BackendMode)
            {
                m_BackendMode = backendMode;
                m_BackendNeedsReplacement = true;
                TryReplaceBackend();
            }

            PrepareReadBuffers();
            TrimCache();
        }

        // Ready chunks retain their entries after leaving the in-flight budget. Reserve
        // those entries when registering assets, plus room for outstanding read batches.
        internal void PrepareAsset(string path, uint contentVersion, int chunkCount, int tileCount = 0, int maxStoredByteSize = 0)
        {
            if (maxStoredByteSize > m_PreparedReadByteCapacity)
            {
                m_PreparedReadByteCapacity = maxStoredByteSize;
                PrepareReadBuffers();
            }
            var key = (path, contentVersion);
            m_PreparedAssets.TryGetValue(key, out var previous);
            int chunks = Math.Max(previous.Chunks, chunkCount);
            // Each tile can retain a lease even when several tiles share one chunk.
            int leases = Math.Max(previous.Leases, Math.Max(chunkCount, tileCount));
            if (chunks == previous.Chunks && leases == previous.Leases)
                return;

            m_PreparedAssets[key] = (chunks, leases);
            m_PreparedChunkCount += chunks - previous.Chunks;
            m_PreparedLeaseCount += leases - previous.Leases;
            EnsureEntryCapacity();
            EnsureLeaseCapacity();
        }

        private void PrepareReadBuffers()
        {
            if (m_IOBackend is VTAsyncReadManagerBackend asyncBackend)
                asyncBackend.Prepare(m_MaxInFlightChunkCount, m_PreparedReadByteCapacity);
            if (m_FallbackIOBackend is VTAsyncReadManagerBackend fallbackBackend)
                fallbackBackend.Prepare(m_MaxInFlightChunkCount, m_PreparedReadByteCapacity);
        }

        private void EnsureLeaseCapacity()
        {
            int capacity = m_PreparedLeaseCount + m_MaxInFlightChunkCount;
            while (m_LeaseCapacity < capacity)
            {
                m_FreeLease = new VTChunkLease { NextFree = m_FreeLease };
                m_LeaseCapacity++;
            }
        }

        private void EnsureEntryCapacity()
        {
            int capacity = m_PreparedChunkCount + m_MaxInFlightChunkCount;
            if (capacity <= m_EntryCapacity)
                return;

            m_Entries.EnsureCapacity(capacity);
            while (m_EntryCapacity < capacity)
            {
                m_FreeEntry = new ChunkEntry { NextFree = m_FreeEntry };
                m_EntryCapacity++;
            }
        }

        internal void SetIOBackendForTesting(IVTIOBackend ioBackend)
        {
            if (ioBackend == null)
                throw new ArgumentNullException(nameof(ioBackend));
            if (m_ActiveBatches.Count > 0 || m_RetainedReadBatches.Count > 0 || m_QueuedEntries.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot replace the VT I/O backend while chunk reads are pending.");
            }

            m_IOBackend?.Dispose();
            m_FallbackIOBackend?.Dispose();
            m_IOBackend = ioBackend;
            m_FallbackIOBackend = null;
            m_BackendNeedsReplacement = false;
            PrepareReadBuffers();
        }

        internal void BeginFrame()
        {
            m_LastIOSaturationCount = 0;
            m_LastDecodeSaturationCount = 0;
            m_LastCacheAllocationFailureCount = 0;
            PollProgress();
        }

        internal void PollProgress()
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamPollReadBatchesMarker.Auto())
                PollReadBatches();
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamReplaceBackendMarker.Auto())
                TryReplaceBackend();
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamPollDecodeTasksMarker.Auto())
                PollDecodeTasks();
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamTrimCacheMarker.Auto())
                TrimCache();
        }

        internal VTChunkLease Acquire(
            string path,
            uint contentVersion,
            in VividVirtualTextureTilePayloadLocation location,
            bool highPriority)
        {
            VTRequestPriorityKey priorityKey =
                VTRequestPriorityKey.FromLegacyIOPriority(highPriority);
            return Acquire(path, contentVersion, location, priorityKey);
        }

        internal VTChunkLease Acquire(
            string path,
            uint contentVersion,
            in VividVirtualTextureTilePayloadLocation location,
            in VTRequestPriorityKey priorityKey)
        {
            if (m_Disposed || string.IsNullOrWhiteSpace(path) || !location.IsValid)
                return null;

            var key = new ChunkKey(path, contentVersion, location);
            if (m_Entries.TryGetValue(key, out ChunkEntry existing))
            {
                PromotePriority(existing, priorityKey);
                existing.ReferenceCount += 1;
                RemoveFromLru(existing);
                return RentLease(existing);
            }

            if (m_InFlightChunkCount >= m_MaxInFlightChunkCount)
            {
                m_LastIOSaturationCount += 1;
                return null;
            }

            ChunkEntry entry = m_FreeEntry;
            if (entry != null)
            {
                m_FreeEntry = entry.NextFree;
                entry.NextFree = null;
            }
            else
            {
                entry = new ChunkEntry();
                m_EntryCapacity++;
            }
            entry.Key = key;
            entry.Location = location;
            entry.PriorityKey = priorityKey;
            entry.State = VTStreamChunkState.Queued;
            entry.ReferenceCount = 1;
            m_Entries.Add(key, entry);
            m_QueuedEntries.Add(entry);
            m_InFlightChunkCount += 1;
            return RentLease(entry);
        }

        internal void SubmitPendingReads()
        {
            if (m_QueuedEntries.Count == 0)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamSubmitReadsSortMarker.Auto())
            {
                m_SubmissionEntries.Clear();
                m_SubmissionEntries.AddRange(m_QueuedEntries);
                m_QueuedEntries.Clear();
                m_SubmissionEntries.Sort(s_QueuedEntryComparison);
            }

            int entryIndex = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamSubmitReadsBuildBatchesMarker.Auto())
            {
                while (entryIndex < m_SubmissionEntries.Count)
                {
                    string path = m_SubmissionEntries[entryIndex].Key.Path;
                    bool highPriority =
                        m_SubmissionEntries[entryIndex].PriorityKey.UsesHighIOPriority;
                    ActiveBatch activeBatch = RentActiveBatch();
                    m_SubmissionCommands.Clear();
                    long batchByteSize = 0;
                    while (entryIndex < m_SubmissionEntries.Count
                           && activeBatch.Entries.Count < 64
                           && string.Equals(
                               m_SubmissionEntries[entryIndex].Key.Path,
                               path,
                               StringComparison.OrdinalIgnoreCase)
                           && m_SubmissionEntries[entryIndex].PriorityKey.UsesHighIOPriority == highPriority)
                    {
                        ChunkEntry entry = m_SubmissionEntries[entryIndex];
                        // Keep normal batches within the storage reserved at asset registration.
                        // A single oversized/unregistered chunk still takes the growth fallback.
                        if (activeBatch.Entries.Count > 0 && m_PreparedReadByteCapacity > 0
                            && batchByteSize + entry.Location.StoredByteSize > m_PreparedReadByteCapacity)
                            break;
                        entryIndex++;
                        if (entry.ReferenceCount <= 0 || entry.State != VTStreamChunkState.Queued)
                            continue;

                        batchByteSize += entry.Location.StoredByteSize;
                        entry.BatchReferenceCount += 1;
                        activeBatch.Entries.Add(entry);
                        m_SubmissionCommands.Add(new VTIOReadCommand(
                            entry.Location.FileOffset,
                            entry.Location.StoredByteSize,
                            entry.PriorityKey.UsesHighIOPriority));
                    }

                    if (activeBatch.Entries.Count == 0)
                    {
                        ReturnActiveBatch(activeBatch);
                        continue;
                    }

                    try
                    {
                        IVTIOBatch ioBatch;
                        using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamSubmitReadsCreateIOMarker.Auto())
                        {
                            if (m_IOBackend is VTDirectStorageBackend
                                && m_DirectStorageRejectedPaths.Contains(path))
                            {
                                m_FallbackIOBackend ??= new VTAsyncReadManagerBackend();
                                PrepareReadBuffers();
                                ioBatch = m_FallbackIOBackend.CreateBatch(path, m_SubmissionCommands);
                            }
                            else
                            {
                                try
                                {
                                    ioBatch = m_IOBackend.CreateBatch(path, m_SubmissionCommands);
                                }
                                catch (Exception exception) when (m_IOBackend is VTDirectStorageBackend)
                                {
                                    m_DirectStorageRejectedPaths.Add(path);
                                    if (m_BackendMode == VividVirtualTextureIOBackendMode.DirectStorage
                                        && !s_DirectStorageFallbackWarningLogged)
                                    {
                                        s_DirectStorageFallbackWarningLogged = true;
                                        Debug.LogWarning(
                                            "[VividRP] DirectStorage could not read the requested VT stream file and is "
                                            + $"falling back to AsyncReadManager: {exception.Message}");
                                    }

                                    m_FallbackIOBackend ??= new VTAsyncReadManagerBackend();
                                    PrepareReadBuffers();
                                    ioBatch = m_FallbackIOBackend.CreateBatch(path, m_SubmissionCommands);
                                }
                            }
                        }
                        activeBatch.Batch = ioBatch;
                        m_ActiveBatches.Add(activeBatch);
                        for (int index = 0; index < activeBatch.Entries.Count; index++)
                            activeBatch.Entries[index].State = VTStreamChunkState.Reading;
                    }
                    catch (Exception exception)
                    {
                        for (int index = 0; index < activeBatch.Entries.Count; index++)
                            RetryOrFail(activeBatch.Entries[index], exception.Message);
                        ReturnActiveBatch(activeBatch);
                    }
                }
            }

            m_SubmissionCommands.Clear();
            m_SubmissionEntries.Clear();
        }

        internal bool TryGetTilePayload(
            ChunkEntry entry,
            in VividVirtualTextureTilePayloadLocation location,
            out VividVirtualTextureTilePayload payload)
        {
            payload = default;
            if (entry == null
                || entry.State != VTStreamChunkState.Ready
                || entry.DecodedData == null
                || entry.Key.ChunkIndex != location.ChunkIndex)
            {
                return false;
            }

            payload = new VividVirtualTextureTilePayload(
                entry.DecodedData,
                location.TileByteOffset,
                location.TileByteSize);
            return payload.IsValid;
        }

        internal void PromotePriority(
            ChunkEntry entry,
            in VTRequestPriorityKey priorityKey)
        {
            if (entry == null)
                return;

            entry.PriorityKey = VTRequestPriorityUtility.SelectHigher(
                entry.PriorityKey,
                priorityKey);
        }

        internal void Release(ChunkEntry entry)
        {
            if (entry == null || entry.ReferenceCount <= 0)
                return;

            entry.ReferenceCount -= 1;
            if (entry.ReferenceCount > 0)
                return;

            if (entry.State == VTStreamChunkState.Ready)
            {
                AddToLru(entry);
                TrimCache();
            }
            else if (entry.State == VTStreamChunkState.Queued)
            {
                RetireInFlightChunk();
                m_QueuedEntries.Remove(entry);
                RetireEntry(entry);
            }
            else if (entry.State == VTStreamChunkState.Decoding
                     && entry.DecodeWorker == null
                     && m_PendingDecodeEntries.Remove(entry))
            {
                entry.StoredData = null;
                entry.NativeStoredData = default;
                RetireInFlightChunk();
                RetireEntry(entry);
            }
            else if (entry.State == VTStreamChunkState.Failed && entry.Error == null)
            {
                RetireEntry(entry);
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            for (int batchIndex = 0; batchIndex < m_ActiveBatches.Count; batchIndex++)
                m_ActiveBatches[batchIndex].Batch.Dispose();
            m_ActiveBatches.Clear();
            m_ActiveBatchPool.Clear();
            m_SubmissionEntries.Clear();
            m_SubmissionCommands.Clear();
            m_FreeLease = null;
            m_FreeEntry = null;

            for (int index = 0; index < m_DecodeWorkers.Count; index++)
                m_DecodeWorkers[index].RequestStop();
            for (int index = 0; index < m_DecodeWorkers.Count; index++)
                m_DecodeWorkers[index].Dispose();
            m_DecodeWorkers.Clear();

            // Workers can still be reading native views until all decode workers stop.
            for (int batchIndex = 0; batchIndex < m_RetainedReadBatches.Count; batchIndex++)
                m_RetainedReadBatches[batchIndex].Batch.Dispose();
            m_RetainedReadBatches.Clear();
            m_Entries.Clear();
            m_QueuedEntries.Clear();
            m_PendingDecodeEntries.Clear();
            m_DecodingEntries.Clear();
            while (m_ReadyLruFirst != null)
                RemoveFromLru(m_ReadyLruFirst);
            m_DirectStorageRejectedPaths.Clear();
            m_IOBackend?.Dispose();
            m_FallbackIOBackend?.Dispose();
            m_ReadyByteCount = 0;
            m_InFlightChunkCount = 0;
            m_Disposed = true;
        }

        private void PollReadBatches()
        {
            // AsyncReadManager submits lazily from IsCompleted, so forward polling preserves
            // the QoS order established when batches were admitted.
            if (m_ActiveBatches.Count > 1)
                m_ActiveBatches.Sort(s_ActiveBatchComparison);
            for (int batchIndex = 0; batchIndex < m_ActiveBatches.Count;)
            {
                ActiveBatch activeBatch = m_ActiveBatches[batchIndex];
                if (!activeBatch.Batch.IsCompleted)
                {
                    bool anyReferenced = false;
                    for (int entryIndex = 0; entryIndex < activeBatch.Entries.Count; entryIndex++)
                        anyReferenced |= activeBatch.Entries[entryIndex].ReferenceCount > 0;
                    if (!anyReferenced)
                        activeBatch.Batch.Cancel();
                    batchIndex += 1;
                    continue;
                }

                for (int entryIndex = 0; entryIndex < activeBatch.Entries.Count; entryIndex++)
                {
                    ChunkEntry entry = activeBatch.Entries[entryIndex];
                    if (entry.ReferenceCount <= 0)
                    {
                        RetireInFlightChunk();
                        RetireEntry(entry);
                        continue;
                    }
                    if (activeBatch.Batch is IVTNativeIOBatch nativeBatch)
                    {
                        if (!nativeBatch.Failed
                            && nativeBatch.TryGetNativeResult(entryIndex, out NativeArray<byte> nativeData))
                        {
                            entry.NativeStoredData = nativeData;
                            QueueDecode(entry, null);
                        }
                        else
                        {
                            RetryOrFail(entry, nativeBatch.Error ?? "VT chunk read failed.");
                        }
                        continue;
                    }
                    if (!activeBatch.Batch.Failed
                        && activeBatch.Batch.TryGetResult(entryIndex, out byte[] storedData))
                    {
                        QueueDecode(entry, storedData);
                    }
                    else
                    {
                        RetryOrFail(entry, activeBatch.Batch.Error ?? "VT chunk read failed.");
                    }
                }

                m_ActiveBatches.RemoveAt(batchIndex);
                if (activeBatch.Batch is IVTNativeIOBatch)
                    m_RetainedReadBatches.Add(activeBatch);
                else
                {
                    activeBatch.Batch.Dispose();
                    ReturnActiveBatch(activeBatch);
                }
            }
            PumpDecodeQueue();
        }

        private void QueueDecode(ChunkEntry entry, byte[] storedData)
        {
            if (entry.ReferenceCount <= 0)
            {
                RetireInFlightChunk();
                RetireEntry(entry);
                return;
            }

            entry.StoredData = storedData;
            entry.State = VTStreamChunkState.Decoding;
            m_PendingDecodeEntries.Add(entry);
        }

        private void EnsureDecodeWorkers()
        {
            while (m_DecodeWorkers.Count < m_DecodeConcurrency)
                m_DecodeWorkers.Add(new DecodeWorker(m_DecodeWork));
        }

        private void PumpDecodeQueue()
        {
            if (m_PendingDecodeEntries.Count == 0)
                return;

            if (m_DecodingEntries.Count >= m_DecodeConcurrency)
            {
                m_LastDecodeSaturationCount = Mathf.Max(
                    m_LastDecodeSaturationCount,
                    m_PendingDecodeEntries.Count);
                return;
            }

            if (m_PendingDecodeEntries.Count > 1)
                m_PendingDecodeEntries.Sort(s_QueuedEntryComparison);

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamStartDecodeMarker.Auto())
            {
                while (m_PendingDecodeEntries.Count > 0
                       && m_DecodingEntries.Count < m_DecodeConcurrency)
                {
                    ChunkEntry entry = m_PendingDecodeEntries[0];
                    m_PendingDecodeEntries.RemoveAt(0);
                    for (int workerIndex = 0; workerIndex < m_DecodeWorkers.Count; workerIndex++)
                    {
                        DecodeWorker worker = m_DecodeWorkers[workerIndex];
                        if (!worker.IsIdle)
                            continue;
                        entry.DecodeWorker = worker;
                        worker.Start(entry);
                        break;
                    }
                    m_DecodingEntries.Add(entry);
                }
            }

            m_LastDecodeSaturationCount = Mathf.Max(
                m_LastDecodeSaturationCount,
                m_PendingDecodeEntries.Count);
        }

        private DecodeResult DecodeEntry(object state)
        {
            if (state is not ChunkEntry entry)
                return new DecodeResult(null, "VT chunk decode received invalid work state.");

            using (RenderPassProfilingUtility.VirtualTextureStreamDecodeMarker.Auto())
                return Decode(entry, entry.StoredData);
        }

        private DecodeResult Decode(ChunkEntry entry, byte[] storedData)
        {
            IVTStreamCodec codec = VTStreamCodecRegistry.Get(entry.Location.Compression);
            if (codec == null || !codec.IsAvailable)
                return new DecodeResult(null, $"VT stream codec {entry.Location.Compression} is unavailable.");
            byte[] decodedData;
            string error;
            bool succeeded = entry.NativeStoredData.IsCreated
                ? VTStreamCodecRegistry.TryDecodeNative(
                    entry.Location.Compression,
                    entry.NativeStoredData,
                    entry.Location.DecodedByteSize,
                    out decodedData,
                    out error)
                : codec.TryDecode(
                    storedData,
                    entry.Location.DecodedByteSize,
                    out decodedData,
                    out error);
            if (!succeeded)
            {
                return new DecodeResult(null, error ?? "VT chunk decode failed.");
            }

            if (decodedData.Length != entry.Location.DecodedByteSize)
                return new DecodeResult(null, "VT chunk decoded size does not match metadata.");
            uint crc = VTDecodedPayloadCRC.Compute(decodedData);
            if (entry.Location.DecodedPayloadCRC != 0 && crc != entry.Location.DecodedPayloadCRC)
            {
                return new DecodeResult(
                    null,
                    $"VT chunk CRC mismatch: expected {entry.Location.DecodedPayloadCRC:x8}, got {crc:x8}.");
            }

            return new DecodeResult(decodedData, null);
        }

        private void PollDecodeTasks()
        {
            for (int entryIndex = m_DecodingEntries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                ChunkEntry entry = m_DecodingEntries[entryIndex];
                if (!entry.DecodeWorker.IsCompleted)
                    continue;

                DecodeResult result = entry.DecodeWorker.TakeResult();
                entry.DecodeWorker = null;
                entry.StoredData = null;
                entry.NativeStoredData = default;
                m_DecodingEntries.RemoveAt(entryIndex);
                RetireInFlightChunk();
                if (!result.Succeeded)
                {
                    entry.State = VTStreamChunkState.Failed;
                    entry.Error = result.Error;
                    continue;
                }

                entry.DecodedData = result.Data;
                entry.State = VTStreamChunkState.Ready;
                m_ReadyByteCount += result.Data.LongLength;
                if (entry.ReferenceCount == 0)
                    AddToLru(entry);
                TrimCache();
            }

            ReleaseCompletedReadBatches();
            PumpDecodeQueue();
        }

        private void ReleaseCompletedReadBatches()
        {
            for (int batchIndex = m_RetainedReadBatches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                ActiveBatch batch = m_RetainedReadBatches[batchIndex];
                bool inUse = false;
                for (int entryIndex = 0; entryIndex < batch.Entries.Count; entryIndex++)
                    inUse |= batch.Entries[entryIndex].NativeStoredData.IsCreated;
                if (inUse)
                    continue;

                batch.Batch.Dispose();
                m_RetainedReadBatches.RemoveAt(batchIndex);
                ReturnActiveBatch(batch);
            }
        }

        private void RetryOrFail(ChunkEntry entry, string error)
        {
            if (entry.ReferenceCount <= 0)
            {
                RetireInFlightChunk();
                RetireEntry(entry);
                return;
            }

            if (entry.RetryCount < 2)
            {
                entry.RetryCount += 1;
                entry.State = VTStreamChunkState.Queued;
                m_QueuedEntries.Add(entry);
                return;
            }

            RetireInFlightChunk();
            entry.State = VTStreamChunkState.Failed;
            entry.Error = error;
        }

        private void TrimCache()
        {
            while (m_ReadyByteCount > m_DecodedCacheBudget && m_ReadyLruFirst != null)
            {
                ChunkEntry entry = m_ReadyLruFirst;
                RemoveFromLru(entry);
                if (entry.DecodedData != null)
                    m_ReadyByteCount -= entry.DecodedData.LongLength;
                entry.DecodedData = null;
                RetireEntry(entry);
            }

            if (m_ReadyByteCount > m_DecodedCacheBudget)
                m_LastCacheAllocationFailureCount += 1;
        }

        private void AddToLru(ChunkEntry entry)
        {
            entry.LruPrevious = m_ReadyLruLast;
            entry.LruNext = null;
            if (m_ReadyLruLast != null)
                m_ReadyLruLast.LruNext = entry;
            else
                m_ReadyLruFirst = entry;
            m_ReadyLruLast = entry;
        }

        private void RemoveFromLru(ChunkEntry entry)
        {
            if (entry.LruPrevious != null)
                entry.LruPrevious.LruNext = entry.LruNext;
            else if (ReferenceEquals(m_ReadyLruFirst, entry))
                m_ReadyLruFirst = entry.LruNext;
            else
                return;

            if (entry.LruNext != null)
                entry.LruNext.LruPrevious = entry.LruPrevious;
            else
                m_ReadyLruLast = entry.LruPrevious;
            entry.LruPrevious = null;
            entry.LruNext = null;
        }

        private ActiveBatch RentActiveBatch()
        {
            ActiveBatch activeBatch = m_ActiveBatchPool.Count > 0
                ? m_ActiveBatchPool.Pop()
                : new ActiveBatch();
            activeBatch.Batch = null;
            activeBatch.Entries.Clear();
            return activeBatch;
        }

        private void ReturnActiveBatch(ActiveBatch activeBatch)
        {
            if (activeBatch == null)
                return;

            activeBatch.Batch = null;
            for (int index = 0; index < activeBatch.Entries.Count; index++)
            {
                ChunkEntry entry = activeBatch.Entries[index];
                entry.BatchReferenceCount -= 1;
                TryRecycleEntry(entry);
            }
            activeBatch.Entries.Clear();
            if (!m_Disposed)
                m_ActiveBatchPool.Push(activeBatch);
        }

        private void RetireEntry(ChunkEntry entry)
        {
            m_Entries.Remove(entry.Key);
            entry.Retired = true;
            TryRecycleEntry(entry);
        }

        private void TryRecycleEntry(ChunkEntry entry)
        {
            // Retained native batches still inspect their entries after decode completion.
            if (m_Disposed || !entry.Retired || entry.BatchReferenceCount != 0
                || entry.ReferenceCount != 0 || entry.DecodeWorker != null)
                return;

            entry.Retired = false;
            entry.Key = default;
            entry.Location = default;
            entry.PriorityKey = default;
            entry.State = default;
            entry.RetryCount = 0;
            entry.StoredData = null;
            entry.NativeStoredData = default;
            entry.DecodedData = null;
            entry.Error = null;
            entry.LruPrevious = null;
            entry.LruNext = null;
            entry.NextFree = m_FreeEntry;
            m_FreeEntry = entry;
        }

        private VTChunkLease RentLease(ChunkEntry entry)
        {
            VTChunkLease lease = m_FreeLease;
            if (lease == null)
            {
                lease = new VTChunkLease();
                m_LeaseCapacity++;
            }
            else
            {
                m_FreeLease = lease.NextFree;
                lease.NextFree = null;
            }
            lease.Reset(this, entry);
            return lease;
        }

        internal void ReturnLease(VTChunkLease lease)
        {
            if (!m_Disposed && lease != null)
            {
                lease.NextFree = m_FreeLease;
                m_FreeLease = lease;
            }
        }

        private void RetireInFlightChunk()
        {
            m_InFlightChunkCount = Math.Max(0, m_InFlightChunkCount - 1);
        }

        private void TryReplaceBackend()
        {
            if (!m_BackendNeedsReplacement || m_ActiveBatches.Count != 0 || m_RetainedReadBatches.Count != 0)
                return;

            m_IOBackend?.Dispose();
            m_FallbackIOBackend?.Dispose();
            m_FallbackIOBackend = null;
            m_IOBackend = CreateBackend(m_BackendMode);
            m_DirectStorageRejectedPaths.Clear();
            m_BackendNeedsReplacement = false;
            PrepareReadBuffers();
        }

        private static IVTIOBackend CreateBackend(VividVirtualTextureIOBackendMode mode)
        {
            if (mode == VividVirtualTextureIOBackendMode.DirectStorage
                || mode == VividVirtualTextureIOBackendMode.Auto)
            {
                var directStorage = new VTDirectStorageBackend();
                if (directStorage.IsAvailable)
                    return directStorage;
                directStorage.Dispose();
                if (mode == VividVirtualTextureIOBackendMode.DirectStorage
                    && !s_DirectStorageFallbackWarningLogged)
                {
                    s_DirectStorageFallbackWarningLogged = true;
                    Debug.LogWarning(
                        "[VividRP] DirectStorage was requested but its native factory is unavailable. "
                        + "Virtual texture streaming is falling back to AsyncReadManager.");
                }
            }

            return new VTAsyncReadManagerBackend();
        }
    }
}
