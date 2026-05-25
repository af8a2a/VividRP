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

            internal void SetRequest(int index, in VTRequest request)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_Requests[index] = request;
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
                m_RequestCount = 0;
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

        private static IVTUploadFenceFactory s_FenceFactory = GraphicsFenceFactory.Instance;

        private readonly Texture2DArray m_PhysicalCache;
        private readonly UploadBatch[] m_Batches;
        private readonly Color32[] m_ScratchPixels;
        private int m_LastDuplicateUploadCount;
        private int m_LastSkippedUploadCount;

        internal VTUploadScheduler(
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            Texture2DArray physicalCache)
        {
            m_PhysicalCache = physicalCache;
            m_Batches = new[]
            {
                new UploadBatch(spaceName, desc.PhysicalPageSize, desc.MaxUploadsPerFrame, 0),
                new UploadBatch(spaceName, desc.PhysicalPageSize, desc.MaxUploadsPerFrame, 1),
            };
            m_ScratchPixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
        }

        internal bool IsEnabled => m_PhysicalCache != null;

        internal int InFlightBatchCount
        {
            get
            {
                int count = 0;
                for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
                {
                    if (m_Batches[batchIndex].InFlight)
                        count += 1;
                }

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

        internal bool CommitCompletedUploads(IVTUploadRequestCommitter committer)
        {
            if (committer == null)
                throw new ArgumentNullException(nameof(committer));

            bool committedAny = false;
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
            {
                UploadBatch batch = m_Batches[batchIndex];
                if (!batch.InFlight || !batch.IsPassed())
                    continue;

                for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
                {
                    VTRequest request = batch.GetRequest(requestIndex);
                    committedAny |= committer.TryCommitUpload(request);
                }

                batch.Reset();
            }

            return committedAny;
        }

        internal int GetAvailableBatchCapacity()
        {
            UploadBatch batch = FindAvailableBatch();
            return batch?.Capacity ?? 0;
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

        internal bool IsRequestInFlight(in VTRequest request)
        {
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
            {
                if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequest(request))
                    return true;
            }

            return false;
        }

        internal bool ScheduleUploads(IReadOnlyList<VTPageUploadPayload> uploads, CommandBuffer cmd)
        {
            if (uploads == null || uploads.Count == 0)
                return false;

            if (!IsEnabled || cmd == null)
            {
                AddSkippedUploadCount(uploads.Count);
                DisposePayloads(uploads);
                return false;
            }

            UploadBatch batch = FindAvailableBatch();
            if (batch == null)
            {
                AddSkippedUploadCount(uploads.Count);
                DisposePayloads(uploads);
                return false;
            }

            int requestCount = 0;
            try
            {
                for (int uploadIndex = 0; uploadIndex < uploads.Count; uploadIndex++)
                {
                    VTPageUploadPayload payload = uploads[uploadIndex];
                    if (!payload.IsValid)
                    {
                        m_LastSkippedUploadCount += 1;
                        continue;
                    }

                    if (requestCount >= batch.Capacity)
                    {
                        m_LastSkippedUploadCount += 1;
                        continue;
                    }

                    VTPageUploadUtility.WritePayloadToStagingTexture(
                        batch.StagingTexture,
                        requestCount,
                        m_ScratchPixels,
                        payload,
                        cmd);
                    batch.SetRequest(requestCount, payload.Request);
                    requestCount += 1;
                }
            }
            finally
            {
                DisposePayloads(uploads);
            }

            if (requestCount == 0)
                return false;

            batch.SealRequests(requestCount);
            batch.StagingTexture.Apply(false, false);
            for (int uploadIndex = 0; uploadIndex < requestCount; uploadIndex++)
            {
                VTRequest request = batch.GetRequest(uploadIndex);
                cmd.CopyTexture(batch.StagingTexture, uploadIndex, 0, m_PhysicalCache, request.PhysicalPageId, 0);
            }

            batch.Submit(s_FenceFactory.Create(cmd));
            return true;
        }

        public void Dispose()
        {
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
                m_Batches[batchIndex].Dispose();
        }

        private UploadBatch FindAvailableBatch()
        {
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
            {
                UploadBatch batch = m_Batches[batchIndex];
                if (!batch.InFlight)
                    return batch;
            }

            return null;
        }

        private static void DisposePayloads(IReadOnlyList<VTPageUploadPayload> uploads)
        {
            if (uploads == null)
                return;

            for (int uploadIndex = 0; uploadIndex < uploads.Count; uploadIndex++)
                uploads[uploadIndex].Finalizer?.Dispose();
        }
    }
}
