using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal interface IVTUploadFenceHandle
    {
        bool IsPassed { get; }
    }

    internal interface IVTUploadFenceFactory
    {
        IVTUploadFenceHandle Create(CommandBuffer cmd);
    }

    internal interface IVTUploadRequestCommitter
    {
        bool TryCommitUpload(in VTRequest request);
    }

    internal interface IVTUploadRequestCommitterResolver
    {
        IVTUploadRequestCommitter ResolveCommitter(int spaceId);
    }

    internal static class VTPageUploadUtility
    {
        internal static Texture2DArray CreateStagingTexture(string spaceName, int physicalPageSize, int depth, string suffix)
        {
            var stagingTexture = new Texture2DArray(
                physicalPageSize,
                physicalPageSize,
                Mathf.Max(1, depth),
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"VividVT_{spaceName}_{suffix}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            stagingTexture.Apply(false, false);
            return stagingTexture;
        }

        internal static void WritePayloadToStagingTexture(
            Texture2DArray stagingTexture,
            int slice,
            Color32[] scratchPixels,
            in VTPageUploadPayload payload,
            CommandBuffer cmd)
        {
            if (stagingTexture == null)
                throw new ArgumentNullException(nameof(stagingTexture));
            if (scratchPixels == null)
                throw new ArgumentNullException(nameof(scratchPixels));
            if (!payload.IsValid)
                throw new ArgumentException("Upload payload must include a page finalizer.", nameof(payload));

            payload.Finalizer.FinalizeRender(cmd);
            payload.Finalizer.FinalizeUpload(stagingTexture, slice, scratchPixels);
        }
    }

    internal sealed class VTUploadScheduler : IDisposable
    {
        private readonly struct UploadPoolKey : IEquatable<UploadPoolKey>
        {
            internal UploadPoolKey(in VirtualTextureSpaceDesc desc)
            {
                PhysicalPageSize = desc.PhysicalPageSize;
                GraphicsFormat = desc.GraphicsFormat;
            }

            internal int PhysicalPageSize { get; }

            internal UnityEngine.Experimental.Rendering.GraphicsFormat GraphicsFormat { get; }

            public bool Equals(UploadPoolKey other)
            {
                return PhysicalPageSize == other.PhysicalPageSize
                       && GraphicsFormat == other.GraphicsFormat;
            }

            public override bool Equals(object obj)
            {
                return obj is UploadPoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(PhysicalPageSize, GraphicsFormat);
            }
        }

        private readonly struct QueuedUpload
        {
            internal QueuedUpload(
                UploadPoolKey key,
                Texture2DArray physicalCache,
                in VTPageUploadPayload payload)
            {
                Key = key;
                PhysicalCache = physicalCache;
                Payload = payload;
            }

            internal UploadPoolKey Key { get; }

            internal Texture2DArray PhysicalCache { get; }

            internal VTPageUploadPayload Payload { get; }
        }

        private sealed class GraphicsFenceHandle : IVTUploadFenceHandle
        {
            private readonly GraphicsFence m_Fence;

            internal GraphicsFenceHandle(GraphicsFence fence)
            {
                m_Fence = fence;
            }

            public bool IsPassed => m_Fence.passed;
        }

        private sealed class GraphicsFenceFactory : IVTUploadFenceFactory
        {
            internal static readonly GraphicsFenceFactory Instance = new();

            private GraphicsFenceFactory()
            {
            }

            public IVTUploadFenceHandle Create(CommandBuffer cmd)
            {
                if (cmd == null)
                    throw new ArgumentNullException(nameof(cmd));

                GraphicsFence fence = cmd.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                return new GraphicsFenceHandle(fence);
            }
        }

        private sealed class UploadBatch : IDisposable
        {
            private readonly Texture2DArray m_StagingTexture;
            private readonly VTRequest[] m_Requests;
            private readonly Texture2DArray[] m_PhysicalCaches;

            private int m_RequestCount;
            private IVTUploadFenceHandle m_Fence;

            internal UploadBatch(string spaceName, int physicalPageSize, int capacity, int batchIndex)
            {
                Capacity = Mathf.Max(1, capacity);
                m_StagingTexture = VTPageUploadUtility.CreateStagingTexture(
                    spaceName,
                    physicalPageSize,
                    Capacity,
                    $"UploadBatch{batchIndex}");
                m_Requests = new VTRequest[Capacity];
                m_PhysicalCaches = new Texture2DArray[Capacity];
            }

            internal int Capacity { get; }

            internal Texture2DArray StagingTexture => m_StagingTexture;

            internal bool InFlight => m_Fence != null;

            internal int RequestCount => m_RequestCount;

            internal VTRequest GetRequest(int index)
            {
                if (index < 0 || index >= m_RequestCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return m_Requests[index];
            }

            internal bool HasRequest(in VTRequest request)
            {
                for (int requestIndex = 0; requestIndex < m_RequestCount; requestIndex++)
                {
                    if (IsSameUploadIdentity(m_Requests[requestIndex], request))
                        return true;
                }

                return false;
            }

            internal bool HasRequestForSpace(int spaceId)
            {
                for (int requestIndex = 0; requestIndex < m_RequestCount; requestIndex++)
                {
                    if (m_Requests[requestIndex].SpaceId == spaceId)
                        return true;
                }

                return false;
            }

            internal void SetRequest(int index, in VTRequest request)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_Requests[index] = request;
            }

            internal void SetPhysicalCache(int index, Texture2DArray physicalCache)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_PhysicalCaches[index] = physicalCache;
            }

            internal Texture2DArray GetPhysicalCache(int index)
            {
                if (index < 0 || index >= m_RequestCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return m_PhysicalCaches[index];
            }

            internal void SealRequests(int requestCount)
            {
                m_RequestCount = Mathf.Clamp(requestCount, 0, Capacity);
            }

            internal void Submit(IVTUploadFenceHandle fence)
            {
                m_Fence = fence ?? throw new ArgumentNullException(nameof(fence));
            }

            internal bool IsPassed()
            {
                return m_Fence != null && m_Fence.IsPassed;
            }

            internal void Reset()
            {
                m_Fence = null;
                Array.Clear(m_PhysicalCaches, 0, m_PhysicalCaches.Length);
                m_RequestCount = 0;
            }

            internal int CancelRequestsForSpace(int spaceId)
            {
                int removedCount = 0;
                int writeIndex = 0;
                for (int readIndex = 0; readIndex < m_RequestCount; readIndex++)
                {
                    if (m_Requests[readIndex].SpaceId == spaceId)
                    {
                        m_PhysicalCaches[readIndex] = null;
                        removedCount += 1;
                        continue;
                    }

                    if (writeIndex != readIndex)
                    {
                        m_Requests[writeIndex] = m_Requests[readIndex];
                        m_PhysicalCaches[writeIndex] = m_PhysicalCaches[readIndex];
                    }

                    writeIndex += 1;
                }

                if (writeIndex < m_RequestCount)
                    Array.Clear(m_PhysicalCaches, writeIndex, m_RequestCount - writeIndex);

                m_RequestCount = writeIndex;
                return removedCount;
            }

            public void Dispose()
            {
                Reset();
                if (m_StagingTexture != null)
                    CoreUtils.Destroy(m_StagingTexture);
            }

            private static bool IsSameUploadIdentity(in VTRequest left, in VTRequest right)
            {
                return left.SpaceId == right.SpaceId
                       && left.PageCoord.Equals(right.PageCoord)
                       && left.PhysicalPageId == right.PhysicalPageId
                       && left.Generation == right.Generation;
            }
        }

        private sealed class UploadPool : IDisposable
        {
            private readonly string m_Name;
            private readonly UploadPoolKey m_Key;
            private readonly List<UploadBatch> m_Batches = new();

            internal UploadPool(string name, in UploadPoolKey key, int batchCapacity)
            {
                m_Name = string.IsNullOrWhiteSpace(name) ? "Global" : name;
                m_Key = key;
                BatchCapacity = Mathf.Max(1, batchCapacity);
                for (int batchIndex = 0; batchIndex < 2; batchIndex++)
                    m_Batches.Add(CreateBatch(batchIndex));
            }

            internal int BatchCapacity { get; private set; }

            internal int InFlightBatchCount
            {
                get
                {
                    int count = 0;
                    for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    {
                        if (m_Batches[batchIndex].InFlight)
                            count += 1;
                    }

                    return count;
                }
            }

            internal int AvailableBatchCapacity => FindAvailableBatch()?.Capacity ?? 0;

            internal void EnsureBatchCapacity(int batchCapacity)
            {
                int normalizedCapacity = Mathf.Max(1, batchCapacity);
                if (normalizedCapacity <= BatchCapacity)
                    return;

                BatchCapacity = normalizedCapacity;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (batch.InFlight)
                        continue;

                    batch.Dispose();
                    m_Batches[batchIndex] = CreateBatch(batchIndex);
                }
            }

            internal bool HasInFlightRequest(in VTRequest request)
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequest(request))
                        return true;
                }

                return false;
            }

            internal bool HasInFlightRequestForSpace(int spaceId)
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequestForSpace(spaceId))
                        return true;
                }

                return false;
            }

            internal bool CommitCompletedUploads(IVTUploadRequestCommitterResolver committerResolver)
            {
                bool committedAny = false;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (!batch.InFlight || !batch.IsPassed())
                        continue;

                    for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
                    {
                        VTRequest request = batch.GetRequest(requestIndex);
                        IVTUploadRequestCommitter committer = committerResolver?.ResolveCommitter(request.SpaceId);
                        if (committer != null)
                            committedAny |= committer.TryCommitUpload(request);
                    }

                    batch.Reset();
                }

                return committedAny;
            }

            internal int CancelRequestsForSpace(int spaceId)
            {
                int removedCount = 0;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    removedCount += m_Batches[batchIndex].CancelRequestsForSpace(spaceId);

                return removedCount;
            }

            internal bool ScheduleUploads(
                IReadOnlyList<QueuedUpload> uploads,
                int startIndex,
                int count,
                Color32[] scratchPixels,
                CommandBuffer cmd,
                IVTUploadFenceFactory fenceFactory,
                ref int skippedUploadCount)
            {
                if (uploads == null || count <= 0)
                    return false;

                UploadBatch batch = FindAvailableBatch();
                if (batch == null)
                {
                    skippedUploadCount += count;
                    DisposePayloads(uploads, startIndex, count);
                    return false;
                }

                int requestCount = 0;
                try
                {
                    for (int uploadIndex = 0; uploadIndex < count; uploadIndex++)
                    {
                        QueuedUpload upload = uploads[startIndex + uploadIndex];
                        VTPageUploadPayload payload = upload.Payload;
                        if (!payload.IsValid || upload.PhysicalCache == null)
                        {
                            skippedUploadCount += 1;
                            continue;
                        }

                        if (requestCount >= batch.Capacity)
                        {
                            skippedUploadCount += 1;
                            continue;
                        }

                        VTPageUploadUtility.WritePayloadToStagingTexture(
                            batch.StagingTexture,
                            requestCount,
                            scratchPixels,
                            payload,
                            cmd);
                        batch.SetRequest(requestCount, payload.Request);
                        batch.SetPhysicalCache(requestCount, upload.PhysicalCache);
                        requestCount += 1;
                    }
                }
                finally
                {
                    DisposePayloads(uploads, startIndex, count);
                }

                if (requestCount == 0)
                    return false;

                batch.SealRequests(requestCount);
                batch.StagingTexture.Apply(false, false);
                for (int uploadIndex = 0; uploadIndex < requestCount; uploadIndex++)
                {
                    VTRequest request = batch.GetRequest(uploadIndex);
                    Texture2DArray physicalCache = batch.GetPhysicalCache(uploadIndex);
                    if (physicalCache != null)
                        cmd.CopyTexture(batch.StagingTexture, uploadIndex, 0, physicalCache, request.PhysicalPageId, 0);
                }

                batch.Submit(fenceFactory.Create(cmd));
                return true;
            }

            public void Dispose()
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    m_Batches[batchIndex].Dispose();

                m_Batches.Clear();
            }

            private UploadBatch FindAvailableBatch()
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (!batch.InFlight && batch.Capacity < BatchCapacity)
                    {
                        batch.Dispose();
                        batch = CreateBatch(batchIndex);
                        m_Batches[batchIndex] = batch;
                    }

                    if (!batch.InFlight)
                        return batch;
                }

                return null;
            }

            private UploadBatch CreateBatch(int batchIndex)
            {
                return new UploadBatch(
                    m_Name,
                    m_Key.PhysicalPageSize,
                    BatchCapacity,
                    batchIndex);
            }
        }

        private static IVTUploadFenceFactory s_FenceFactory = GraphicsFenceFactory.Instance;

        private readonly Dictionary<UploadPoolKey, UploadPool> m_Pools = new();
        private readonly Dictionary<UploadPoolKey, int> m_QueuedCountsByKey = new();
        private readonly List<QueuedUpload> m_QueuedUploads = new();
        private Color32[] m_ScratchPixels = Array.Empty<Color32>();
        private int m_MaxUploadBytesPerFrame = int.MaxValue;
        private int m_ReservedUploadBytesThisFrame;
        private int m_LastDuplicateUploadCount;
        private int m_LastSkippedUploadCount;

        internal bool IsEnabled => true;

        internal int MaxUploadBytesPerFrame
        {
            get => m_MaxUploadBytesPerFrame;
            set => m_MaxUploadBytesPerFrame = value <= 0 ? int.MaxValue : value;
        }

        internal int InFlightBatchCount
        {
            get
            {
                int count = 0;
                foreach (UploadPool pool in m_Pools.Values)
                    count += pool.InFlightBatchCount;

                return count;
            }
        }

        internal int LastDuplicateUploadCount => m_LastDuplicateUploadCount;

        internal int LastSkippedUploadCount => m_LastSkippedUploadCount;

        internal static void SetFenceFactoryForTesting(IVTUploadFenceFactory fenceFactory)
        {
            s_FenceFactory = fenceFactory ?? GraphicsFenceFactory.Instance;
        }

        internal static void ResetFenceFactory()
        {
            s_FenceFactory = GraphicsFenceFactory.Instance;
        }

        internal void BeginFrame()
        {
            ResetLastScheduleStats();
            m_ReservedUploadBytesThisFrame = 0;
            if (m_QueuedUploads.Count > 0)
                DisposeQueuedUploads();

            m_QueuedCountsByKey.Clear();
        }

        internal bool CommitCompletedUploads(IVTUploadRequestCommitterResolver committerResolver)
        {
            bool committedAny = false;
            foreach (UploadPool pool in m_Pools.Values)
                committedAny |= pool.CommitCompletedUploads(committerResolver);

            return committedAny;
        }

        internal int GetAvailableBatchCapacity(string spaceName, in VirtualTextureSpaceDesc desc)
        {
            UploadPoolKey key = new(desc);
            UploadPool pool = GetOrCreatePool(spaceName, key, desc.MaxUploadsPerFrame);
            m_QueuedCountsByKey.TryGetValue(key, out int queuedCount);
            return Mathf.Max(0, pool.AvailableBatchCapacity - queuedCount);
        }

        internal void ResetLastScheduleStats()
        {
            m_LastDuplicateUploadCount = 0;
            m_LastSkippedUploadCount = 0;
        }

        internal void AddSkippedUploadCount(int skippedUploadCount)
        {
            m_LastSkippedUploadCount += Mathf.Max(0, skippedUploadCount);
        }

        internal void CountInFlightDuplicates(IReadOnlyList<VTRequest> pendingRequests)
        {
            if (pendingRequests == null)
                return;

            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
            {
                if (IsRequestInFlight(pendingRequests[requestIndex]))
                    m_LastDuplicateUploadCount += 1;
            }
        }

        internal bool HasInFlightUploadForSpace(int spaceId)
        {
            foreach (UploadPool pool in m_Pools.Values)
            {
                if (pool.HasInFlightRequestForSpace(spaceId))
                    return true;
            }

            return false;
        }

        internal bool IsRequestInFlight(in VTRequest request)
        {
            foreach (UploadPool pool in m_Pools.Values)
            {
                if (pool.HasInFlightRequest(request))
                    return true;
            }

            return false;
        }

        internal void CancelUploadsForSpace(int spaceId)
        {
            for (int uploadIndex = m_QueuedUploads.Count - 1; uploadIndex >= 0; uploadIndex--)
            {
                QueuedUpload upload = m_QueuedUploads[uploadIndex];
                if (upload.Payload.Request.SpaceId != spaceId)
                    continue;

                upload.Payload.Finalizer?.Dispose();
                ReleaseUploadReservation(upload.Key);
                m_QueuedUploads.RemoveAt(uploadIndex);
            }

            foreach (UploadPool pool in m_Pools.Values)
                pool.CancelRequestsForSpace(spaceId);
        }

        internal bool TryReserveUpload(string spaceName, in VirtualTextureSpaceDesc desc)
        {
            int uploadByteSize = ComputeUploadByteSize(desc);
            if (m_ReservedUploadBytesThisFrame > m_MaxUploadBytesPerFrame - uploadByteSize)
                return false;

            if (GetAvailableBatchCapacity(spaceName, desc) <= 0)
                return false;

            UploadPoolKey key = new(desc);
            m_QueuedCountsByKey.TryGetValue(key, out int queuedCount);
            m_QueuedCountsByKey[key] = queuedCount + 1;
            m_ReservedUploadBytesThisFrame += uploadByteSize;
            return true;
        }

        internal void ReleaseUploadReservation(in VirtualTextureSpaceDesc desc)
        {
            ReleaseUploadReservation(new UploadPoolKey(desc));
        }

        private void ReleaseUploadReservation(in UploadPoolKey key)
        {
            if (m_QueuedCountsByKey.TryGetValue(key, out int queuedCount))
            {
                if (queuedCount <= 1)
                    m_QueuedCountsByKey.Remove(key);
                else
                    m_QueuedCountsByKey[key] = queuedCount - 1;
            }

            int uploadByteSize = ComputeUploadByteSize(key.PhysicalPageSize);
            m_ReservedUploadBytesThisFrame = Mathf.Max(0, m_ReservedUploadBytesThisFrame - uploadByteSize);
        }

        internal void EnqueueReservedUpload(
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            Texture2DArray physicalCache,
            in VTPageUploadPayload payload)
        {
            UploadPoolKey key = new(desc);
            GetOrCreatePool(spaceName, key, desc.MaxUploadsPerFrame);
            m_QueuedUploads.Add(new QueuedUpload(key, physicalCache, payload));
        }

        internal bool FinalizeUploads(CommandBuffer cmd)
        {
            if (m_QueuedUploads.Count == 0)
                return false;

            if (cmd == null)
            {
                AddSkippedUploadCount(m_QueuedUploads.Count);
                DisposeQueuedUploads();
                return false;
            }

            EnsureScratchPixels(GetMaxQueuedPhysicalPageSize());
            bool scheduledAny = false;
            m_QueuedUploads.Sort(QueuedUploadComparer.Instance);
            int startIndex = 0;
            while (startIndex < m_QueuedUploads.Count)
            {
                UploadPoolKey key = m_QueuedUploads[startIndex].Key;
                int count = 1;
                while (startIndex + count < m_QueuedUploads.Count
                       && m_QueuedUploads[startIndex + count].Key.Equals(key))
                {
                    count += 1;
                }

                if (m_Pools.TryGetValue(key, out UploadPool pool))
                {
                    scheduledAny |= pool.ScheduleUploads(
                        m_QueuedUploads,
                        startIndex,
                        count,
                        m_ScratchPixels,
                        cmd,
                        s_FenceFactory,
                        ref m_LastSkippedUploadCount);
                }
                else
                {
                    m_LastSkippedUploadCount += count;
                    DisposePayloads(m_QueuedUploads, startIndex, count);
                }

                startIndex += count;
            }

            m_QueuedUploads.Clear();
            return scheduledAny;
        }

        public void Dispose()
        {
            DisposeQueuedUploads();
            foreach (UploadPool pool in m_Pools.Values)
                pool.Dispose();

            m_Pools.Clear();
            m_QueuedCountsByKey.Clear();
        }

        private UploadPool GetOrCreatePool(string spaceName, in UploadPoolKey key, int batchCapacity)
        {
            if (m_Pools.TryGetValue(key, out UploadPool pool))
            {
                pool.EnsureBatchCapacity(batchCapacity);
                return pool;
            }

            pool = new UploadPool(spaceName, key, batchCapacity);
            m_Pools.Add(key, pool);
            return pool;
        }

        private void EnsureScratchPixels(int physicalPageSize)
        {
            int pixelCount = Mathf.Max(1, physicalPageSize) * Mathf.Max(1, physicalPageSize);
            if (m_ScratchPixels.Length < pixelCount)
                m_ScratchPixels = new Color32[pixelCount];
        }

        private int GetMaxQueuedPhysicalPageSize()
        {
            int physicalPageSize = 1;
            for (int uploadIndex = 0; uploadIndex < m_QueuedUploads.Count; uploadIndex++)
                physicalPageSize = Mathf.Max(physicalPageSize, m_QueuedUploads[uploadIndex].Key.PhysicalPageSize);

            return physicalPageSize;
        }

        private void DisposeQueuedUploads()
        {
            DisposePayloads(m_QueuedUploads, 0, m_QueuedUploads.Count);
            m_QueuedUploads.Clear();
        }

        private static int ComputeUploadByteSize(in VirtualTextureSpaceDesc desc)
        {
            return ComputeUploadByteSize(desc.PhysicalPageSize);
        }

        private static int ComputeUploadByteSize(int physicalPageSize)
        {
            physicalPageSize = Mathf.Max(1, physicalPageSize);
            return physicalPageSize * physicalPageSize * 4;
        }

        private static void DisposePayloads(IReadOnlyList<QueuedUpload> uploads, int startIndex, int count)
        {
            if (uploads == null)
                return;

            int endIndex = Mathf.Min(uploads.Count, startIndex + count);
            for (int uploadIndex = startIndex; uploadIndex < endIndex; uploadIndex++)
                uploads[uploadIndex].Payload.Finalizer?.Dispose();
        }

        private sealed class QueuedUploadComparer : IComparer<QueuedUpload>
        {
            internal static readonly QueuedUploadComparer Instance = new();

            private QueuedUploadComparer()
            {
            }

            public int Compare(QueuedUpload left, QueuedUpload right)
            {
                int sizeCompare = left.Key.PhysicalPageSize.CompareTo(right.Key.PhysicalPageSize);
                if (sizeCompare != 0)
                    return sizeCompare;

                int formatCompare = left.Key.GraphicsFormat.CompareTo(right.Key.GraphicsFormat);
                if (formatCompare != 0)
                    return formatCompare;

                return left.Payload.Request.SpaceId.CompareTo(right.Payload.Request.SpaceId);
            }
        }
    }
}
