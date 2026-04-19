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

        internal static void WritePageToStagingTexture(
            Texture2DArray stagingTexture,
            int slice,
            Color32[] scratchPixels,
            IVTRuntimePageProducer producer,
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (stagingTexture == null)
                throw new ArgumentNullException(nameof(stagingTexture));
            if (scratchPixels == null)
                throw new ArgumentNullException(nameof(scratchPixels));
            if (producer == null)
                throw new ArgumentNullException(nameof(producer));

            producer.WritePage(desc, request, scratchPixels);
            stagingTexture.SetPixels32(scratchPixels, slice, 0);
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
                    if (m_Requests[requestIndex].Equals(request))
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

            internal void Submit(IVTUploadFenceHandle fence, int requestCount)
            {
                m_Fence = fence ?? throw new ArgumentNullException(nameof(fence));
                m_RequestCount = Mathf.Clamp(requestCount, 0, Capacity);
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
        }

        private static IVTUploadFenceFactory s_FenceFactory = GraphicsFenceFactory.Instance;

        private readonly VirtualTextureSpaceDesc m_Desc;
        private readonly IVTRuntimePageProducer m_RuntimeProducer;
        private readonly Texture2DArray m_PhysicalCache;
        private readonly UploadBatch[] m_Batches;
        private readonly Color32[] m_ScratchPixels;

        internal VTUploadScheduler(
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            Texture2DArray physicalCache,
            IVTRuntimePageProducer runtimeProducer)
        {
            m_Desc = desc;
            m_RuntimeProducer = runtimeProducer;
            m_PhysicalCache = physicalCache;
            m_Batches = new[]
            {
                new UploadBatch(spaceName, desc.PhysicalPageSize, desc.MaxUploadsPerFrame, 0),
                new UploadBatch(spaceName, desc.PhysicalPageSize, desc.MaxUploadsPerFrame, 1),
            };
            m_ScratchPixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
        }

        internal bool IsEnabled => m_RuntimeProducer != null && m_PhysicalCache != null;

        internal static void SetFenceFactoryForTesting(IVTUploadFenceFactory fenceFactory)
        {
            s_FenceFactory = fenceFactory ?? GraphicsFenceFactory.Instance;
        }

        internal static void ResetFenceFactory()
        {
            s_FenceFactory = GraphicsFenceFactory.Instance;
        }

        internal bool CommitCompletedUploads(Func<VTRequest, bool> commitRequest)
        {
            if (commitRequest == null)
                throw new ArgumentNullException(nameof(commitRequest));

            bool committedAny = false;
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
            {
                UploadBatch batch = m_Batches[batchIndex];
                if (!batch.InFlight || !batch.IsPassed())
                    continue;

                for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
                    committedAny |= commitRequest(batch.GetRequest(requestIndex));

                batch.Reset();
            }

            return committedAny;
        }

        internal bool SchedulePendingUploads(IReadOnlyList<VTRequest> pendingRequests, CommandBuffer cmd)
        {
            if (!IsEnabled || pendingRequests == null || pendingRequests.Count == 0 || cmd == null)
                return false;

            UploadBatch batch = FindAvailableBatch();
            if (batch == null)
                return false;

            int requestCount = 0;
            for (int requestIndex = 0; requestIndex < pendingRequests.Count && requestCount < batch.Capacity; requestIndex++)
            {
                VTRequest request = pendingRequests[requestIndex];
                if (IsRequestInFlight(request))
                    continue;

                VTPageUploadUtility.WritePageToStagingTexture(
                    batch.StagingTexture,
                    requestCount,
                    m_ScratchPixels,
                    m_RuntimeProducer,
                    m_Desc,
                    request);
                batch.SetRequest(requestCount, request);
                requestCount += 1;
            }

            if (requestCount == 0)
                return false;

            batch.StagingTexture.Apply(false, false);
            for (int uploadIndex = 0; uploadIndex < requestCount; uploadIndex++)
            {
                VTRequest request = batch.GetRequest(uploadIndex);
                cmd.CopyTexture(batch.StagingTexture, uploadIndex, 0, m_PhysicalCache, request.PhysicalPageId, 0);
            }

            batch.Submit(s_FenceFactory.Create(cmd), requestCount);
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

        private bool IsRequestInFlight(in VTRequest request)
        {
            for (int batchIndex = 0; batchIndex < m_Batches.Length; batchIndex++)
            {
                if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequest(request))
                    return true;
            }

            return false;
        }
    }
}
