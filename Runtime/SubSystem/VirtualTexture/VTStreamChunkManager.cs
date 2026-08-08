using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private VTStreamChunkManager m_Manager;
        private VTStreamChunkManager.ChunkEntry m_Entry;

        internal VTChunkLease(VTStreamChunkManager manager, VTStreamChunkManager.ChunkEntry entry)
        {
            Reset(manager, entry);
        }

        internal VTStreamChunkState State => m_Entry?.State ?? VTStreamChunkState.Failed;

        internal string Error => m_Entry?.Error;

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
            internal ChunkKey Key;
            internal VividVirtualTextureTilePayloadLocation Location;
            internal VTStreamChunkState State;
            internal int ReferenceCount;
            internal int RetryCount;
            internal byte[] StoredData;
            internal byte[] DecodedData;
            internal Task<DecodeResult> DecodeTask;
            internal string Error;
            internal LinkedListNode<ChunkEntry> LruNode;
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

        private sealed class ActiveBatch
        {
            internal IVTIOBatch Batch;
            internal readonly List<ChunkEntry> Entries = new();
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

                bool leftHighPriority =
                    (left.Location.Flags & VividVirtualTextureChunkFlags.MipTail) != 0;
                bool rightHighPriority =
                    (right.Location.Flags & VividVirtualTextureChunkFlags.MipTail) != 0;
                int priorityCompare = rightHighPriority.CompareTo(leftHighPriority);
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

        private readonly Dictionary<ChunkKey, ChunkEntry> m_Entries = new();
        private readonly List<ChunkEntry> m_QueuedEntries = new();
        private readonly List<ChunkEntry> m_DecodingEntries = new();
        private readonly List<ActiveBatch> m_ActiveBatches = new();
        private readonly QueuedEntryComparer m_QueuedEntryComparer = new();
        private readonly Stack<ActiveBatch> m_ActiveBatchPool = new();
        private readonly List<ChunkEntry> m_SubmissionEntries = new();
        private readonly List<VTIOReadCommand> m_SubmissionCommands = new(64);
        private readonly Stack<VTChunkLease> m_LeasePool = new();
        private readonly LinkedList<ChunkEntry> m_UnreferencedReadyLru = new();
        private readonly HashSet<string> m_DirectStorageRejectedPaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<object, DecodeResult> m_DecodeWork;
        private SemaphoreSlim m_DecodeSemaphore;
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

        private VTStreamChunkManager()
        {
            m_DecodeSemaphore = new SemaphoreSlim(m_DecodeConcurrency, m_DecodeConcurrency);
            m_IOBackend = CreateBackend(m_BackendMode);
            m_DecodeWork = DecodeEntry;
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
            s_Shared = replacement;
        }

        internal int PendingChunkCount => m_InFlightChunkCount;

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
            int clampedDecodeConcurrency = Mathf.Clamp(decodeConcurrency, 1, 64);
            if (clampedDecodeConcurrency != m_DecodeConcurrency && m_DecodingEntries.Count == 0)
            {
                m_DecodeConcurrency = clampedDecodeConcurrency;
                m_DecodeSemaphore.Dispose();
                m_DecodeSemaphore = new SemaphoreSlim(m_DecodeConcurrency, m_DecodeConcurrency);
            }

            m_DecodedCacheBudget = Math.Max(0, decodedCacheBudgetMiB) * 1024L * 1024L;
            if (backendMode != m_BackendMode)
            {
                m_BackendMode = backendMode;
                m_BackendNeedsReplacement = true;
                TryReplaceBackend();
            }

            TrimCache();
        }

        internal void BeginFrame()
        {
            m_LastIOSaturationCount = 0;
            m_LastDecodeSaturationCount = 0;
            m_LastCacheAllocationFailureCount = 0;
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
            if (m_Disposed || string.IsNullOrWhiteSpace(path) || !location.IsValid)
                return null;

            var key = new ChunkKey(path, contentVersion, location);
            if (m_Entries.TryGetValue(key, out ChunkEntry existing))
            {
                existing.ReferenceCount += 1;
                RemoveFromLru(existing);
                return RentLease(existing);
            }

            if (m_InFlightChunkCount >= m_MaxInFlightChunkCount)
            {
                m_LastIOSaturationCount += 1;
                return null;
            }

            var entry = new ChunkEntry
            {
                Key = key,
                Location = location,
                State = VTStreamChunkState.Queued,
                ReferenceCount = 1,
            };
            m_Entries.Add(key, entry);
            InsertQueuedEntry(entry, highPriority);
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
                m_SubmissionEntries.Sort(m_QueuedEntryComparer);
            }

            int entryIndex = 0;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamSubmitReadsBuildBatchesMarker.Auto())
            {
                while (entryIndex < m_SubmissionEntries.Count)
                {
                    string path = m_SubmissionEntries[entryIndex].Key.Path;
                    bool highPriority =
                        (m_SubmissionEntries[entryIndex].Location.Flags & VividVirtualTextureChunkFlags.MipTail) != 0;
                    ActiveBatch activeBatch = RentActiveBatch();
                    m_SubmissionCommands.Clear();
                    while (entryIndex < m_SubmissionEntries.Count
                           && activeBatch.Entries.Count < 64
                           && string.Equals(
                               m_SubmissionEntries[entryIndex].Key.Path,
                               path,
                               StringComparison.OrdinalIgnoreCase)
                           && ((m_SubmissionEntries[entryIndex].Location.Flags
                                & VividVirtualTextureChunkFlags.MipTail) != 0) == highPriority)
                    {
                        ChunkEntry entry = m_SubmissionEntries[entryIndex++];
                        if (entry.ReferenceCount <= 0 || entry.State != VTStreamChunkState.Queued)
                            continue;

                        activeBatch.Entries.Add(entry);
                        m_SubmissionCommands.Add(new VTIOReadCommand(
                            entry.Location.FileOffset,
                            entry.Location.StoredByteSize,
                            (entry.Location.Flags & VividVirtualTextureChunkFlags.MipTail) != 0));
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

        internal void Release(ChunkEntry entry)
        {
            if (entry == null || entry.ReferenceCount <= 0)
                return;

            entry.ReferenceCount -= 1;
            if (entry.ReferenceCount > 0)
                return;

            if (entry.State == VTStreamChunkState.Ready)
            {
                entry.LruNode = m_UnreferencedReadyLru.AddLast(entry);
                TrimCache();
            }
            else if (entry.State == VTStreamChunkState.Queued)
            {
                RetireInFlightChunk();
                m_Entries.Remove(entry.Key);
                m_QueuedEntries.Remove(entry);
            }
            else if (entry.State == VTStreamChunkState.Failed && entry.Error == null)
            {
                m_Entries.Remove(entry.Key);
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
            m_LeasePool.Clear();

            if (m_DecodingEntries.Count > 0)
            {
                var decodeTasks = new List<Task>(m_DecodingEntries.Count);
                for (int entryIndex = 0; entryIndex < m_DecodingEntries.Count; entryIndex++)
                {
                    Task<DecodeResult> decodeTask = m_DecodingEntries[entryIndex].DecodeTask;
                    if (decodeTask != null)
                        decodeTasks.Add(decodeTask);
                }

                try
                {
                    Task.WaitAll(decodeTasks.ToArray());
                }
                catch (AggregateException)
                {
                    // Decode failures are already represented by the chunk state. Shutdown only
                    // needs to keep the semaphore and native resources alive until tasks retire.
                }
            }

            m_Entries.Clear();
            m_QueuedEntries.Clear();
            m_DecodingEntries.Clear();
            m_UnreferencedReadyLru.Clear();
            m_DirectStorageRejectedPaths.Clear();
            m_DecodeSemaphore.Dispose();
            m_IOBackend?.Dispose();
            m_FallbackIOBackend?.Dispose();
            m_ReadyByteCount = 0;
            m_InFlightChunkCount = 0;
            m_Disposed = true;
        }

        private void PollReadBatches()
        {
            for (int batchIndex = m_ActiveBatches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                ActiveBatch activeBatch = m_ActiveBatches[batchIndex];
                if (!activeBatch.Batch.IsCompleted)
                {
                    bool anyReferenced = false;
                    for (int entryIndex = 0; entryIndex < activeBatch.Entries.Count; entryIndex++)
                        anyReferenced |= activeBatch.Entries[entryIndex].ReferenceCount > 0;
                    if (!anyReferenced)
                        activeBatch.Batch.Cancel();
                    continue;
                }

                for (int entryIndex = 0; entryIndex < activeBatch.Entries.Count; entryIndex++)
                {
                    ChunkEntry entry = activeBatch.Entries[entryIndex];
                    if (!activeBatch.Batch.Failed
                        && activeBatch.Batch.TryGetResult(entryIndex, out byte[] storedData))
                    {
                        StartDecode(entry, storedData);
                    }
                    else
                    {
                        RetryOrFail(entry, activeBatch.Batch.Error ?? "VT chunk read failed.");
                    }
                }

                activeBatch.Batch.Dispose();
                m_ActiveBatches.RemoveAt(batchIndex);
                ReturnActiveBatch(activeBatch);
            }
        }

        private void StartDecode(ChunkEntry entry, byte[] storedData)
        {
            if (entry.ReferenceCount <= 0)
            {
                RetireInFlightChunk();
                m_Entries.Remove(entry.Key);
                return;
            }

            entry.StoredData = storedData;
            entry.State = VTStreamChunkState.Decoding;
            if (m_DecodingEntries.Count >= m_DecodeConcurrency)
                m_LastDecodeSaturationCount += 1;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureStreamStartDecodeMarker.Auto())
            {
                entry.DecodeTask = Task.Factory.StartNew(
                    m_DecodeWork,
                    entry,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            m_DecodingEntries.Add(entry);
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
            m_DecodeSemaphore.Wait();
            try
            {
                IVTStreamCodec codec = VTStreamCodecRegistry.Get(entry.Location.Compression);
                if (codec == null || !codec.IsAvailable)
                    return new DecodeResult(null, $"VT stream codec {entry.Location.Compression} is unavailable.");
                if (!codec.TryDecode(
                        storedData,
                        entry.Location.DecodedByteSize,
                        out byte[] decodedData,
                        out string error))
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
            finally
            {
                m_DecodeSemaphore.Release();
            }
        }

        private void PollDecodeTasks()
        {
            for (int entryIndex = m_DecodingEntries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                ChunkEntry entry = m_DecodingEntries[entryIndex];
                if (!entry.DecodeTask.IsCompleted)
                    continue;

                DecodeResult result;
                if (entry.DecodeTask.IsCompletedSuccessfully)
                    result = entry.DecodeTask.Result;
                else
                    result = new DecodeResult(null, entry.DecodeTask.Exception?.GetBaseException().Message ?? "VT chunk decode failed.");

                entry.DecodeTask = null;
                entry.StoredData = null;
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
                    entry.LruNode = m_UnreferencedReadyLru.AddLast(entry);
                TrimCache();
            }
        }

        private void RetryOrFail(ChunkEntry entry, string error)
        {
            if (entry.ReferenceCount <= 0)
            {
                RetireInFlightChunk();
                m_Entries.Remove(entry.Key);
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
            while (m_ReadyByteCount > m_DecodedCacheBudget && m_UnreferencedReadyLru.First != null)
            {
                ChunkEntry entry = m_UnreferencedReadyLru.First.Value;
                m_UnreferencedReadyLru.RemoveFirst();
                entry.LruNode = null;
                if (entry.DecodedData != null)
                    m_ReadyByteCount -= entry.DecodedData.LongLength;
                entry.DecodedData = null;
                m_Entries.Remove(entry.Key);
            }

            if (m_ReadyByteCount > m_DecodedCacheBudget)
                m_LastCacheAllocationFailureCount += 1;
        }

        private void RemoveFromLru(ChunkEntry entry)
        {
            if (entry.LruNode == null)
                return;

            m_UnreferencedReadyLru.Remove(entry.LruNode);
            entry.LruNode = null;
        }

        private void InsertQueuedEntry(ChunkEntry entry, bool highPriority)
        {
            if (highPriority)
                m_QueuedEntries.Insert(0, entry);
            else
                m_QueuedEntries.Add(entry);
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
            activeBatch.Entries.Clear();
            if (!m_Disposed)
                m_ActiveBatchPool.Push(activeBatch);
        }

        private VTChunkLease RentLease(ChunkEntry entry)
        {
            if (m_LeasePool.Count == 0)
                return new VTChunkLease(this, entry);

            VTChunkLease lease = m_LeasePool.Pop();
            lease.Reset(this, entry);
            return lease;
        }

        internal void ReturnLease(VTChunkLease lease)
        {
            if (!m_Disposed && lease != null)
                m_LeasePool.Push(lease);
        }

        private void RetireInFlightChunk()
        {
            m_InFlightChunkCount = Math.Max(0, m_InFlightChunkCount - 1);
        }

        private void TryReplaceBackend()
        {
            if (!m_BackendNeedsReplacement || m_ActiveBatches.Count != 0)
                return;

            m_IOBackend?.Dispose();
            m_FallbackIOBackend?.Dispose();
            m_FallbackIOBackend = null;
            m_IOBackend = CreateBackend(m_BackendMode);
            m_DirectStorageRejectedPaths.Clear();
            m_BackendNeedsReplacement = false;
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
