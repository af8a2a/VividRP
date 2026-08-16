using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VTVirtualTextureStreamRequestGate
    {
        internal const int DefaultMaxPendingReadCount = 64;

        private static int s_MaxPendingReadCount = DefaultMaxPendingReadCount;
        private static int s_PendingReadCount;
        private static int s_LastSaturatedRequestCount;

        internal static int PendingReadCount => s_PendingReadCount;

        internal static int LastSaturatedRequestCount => s_LastSaturatedRequestCount;

        internal static bool TryAcquire()
        {
            if (s_PendingReadCount >= s_MaxPendingReadCount)
            {
                s_LastSaturatedRequestCount += 1;
                return false;
            }

            s_PendingReadCount += 1;
            return true;
        }

        internal static void Release()
        {
            s_PendingReadCount = Mathf.Max(0, s_PendingReadCount - 1);
        }

        internal static void BeginFrame()
        {
            s_LastSaturatedRequestCount = 0;
        }

        internal static void SetMaxPendingReadCountForTesting(int maxPendingReadCount)
        {
            s_MaxPendingReadCount = Mathf.Max(1, maxPendingReadCount);
        }

        internal static void ResetForTesting()
        {
            s_MaxPendingReadCount = DefaultMaxPendingReadCount;
            s_PendingReadCount = 0;
            s_LastSaturatedRequestCount = 0;
        }
    }

    internal sealed class VividVirtualTextureAssetProducer :
        IVTPageProducer,
        IVTPrioritizedPageProducer,
        IVTPageRequestRetirement,
        IDisposable
    {
        private readonly struct TileKey : IEquatable<TileKey>
        {
            internal TileKey(in VirtualTexturePageCoord coord)
            {
                X = coord.X;
                Y = coord.Y;
                Mip = coord.Mip;
            }

            private int X { get; }

            private int Y { get; }

            private int Mip { get; }

            public bool Equals(TileKey other)
            {
                return X == other.X && Y == other.Y && Mip == other.Mip;
            }

            public override bool Equals(object obj)
            {
                return obj is TileKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y, Mip);
            }
        }

        private sealed class StreamTileTask : IVTPageProducerTask, IDisposable
        {
            private readonly CancellationTokenSource m_CancellationTokenSource;
            private bool m_OwnsGlobalReadSlot;
            private bool m_IsDisposed;

            internal StreamTileTask(
                Task<byte[]> task,
                CancellationTokenSource cancellationTokenSource,
                bool ownsGlobalReadSlot)
            {
                Task = task ?? throw new ArgumentNullException(nameof(task));
                m_CancellationTokenSource = cancellationTokenSource;
                m_OwnsGlobalReadSlot = ownsGlobalReadSlot;
            }

            internal Task<byte[]> Task { get; }

            public bool IsCompleted => Task.IsCompleted;

            internal bool IsCompletedSuccessfully => Task.Status == TaskStatus.RanToCompletion;

            internal bool IsCanceledOrFaulted => Task.IsCanceled || Task.IsFaulted;

            internal void Cancel()
            {
                if (!Task.IsCompleted)
                    m_CancellationTokenSource?.Cancel();
            }

            public void Dispose()
            {
                if (m_IsDisposed)
                    return;

                Cancel();
                m_CancellationTokenSource?.Dispose();
                if (m_OwnsGlobalReadSlot)
                {
                    VTVirtualTextureStreamRequestGate.Release();
                    m_OwnsGlobalReadSlot = false;
                }

                m_IsDisposed = true;
            }
        }

        private sealed class Finalizer : IVTMultiLayerPageFinalizer
        {
            private VividVirtualTextureAssetProducer m_Owner;
            private VividVirtualTextureTilePayload m_Payload;
            private int m_ExpectedPixelCount;
            private VTLayerDesc[] m_Layers;
            private VTChunkLease m_Lease;

            internal void Initialize(
                VividVirtualTextureAssetProducer owner,
                in VividVirtualTextureTilePayload payload,
                int expectedPixelCount,
                VTLayerDesc[] layers,
                VTChunkLease lease = null)
            {
                m_Owner = owner ?? throw new ArgumentNullException(nameof(owner));
                m_Payload = payload;
                m_ExpectedPixelCount = expectedPixelCount;
                m_Layers = layers != null && layers.Length > 0
                    ? layers
                    : throw new ArgumentException("VT finalizer layers must be non-empty.", nameof(layers));
                m_Lease = lease;
            }

            public void FinalizeRender(CommandBuffer cmd)
            {
            }

            public int LayerCount => m_Layers.Length;

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                FinalizeUploadLayer(stagingTexture, slice, 0, scratchPixels);
            }

            public void FinalizeUploadLayer(
                Texture2DArray stagingTexture,
                int slice,
                int layerIndex,
                Color32[] scratchPixels)
            {
                if (stagingTexture == null)
                    throw new ArgumentNullException(nameof(stagingTexture));
                if (scratchPixels == null)
                    throw new ArgumentNullException(nameof(scratchPixels));
                if (!m_Payload.IsValid)
                    throw new InvalidOperationException("[VividRP] Invalid virtual texture tile payload.");

                if (layerIndex < 0 || layerIndex >= m_Layers.Length)
                    throw new ArgumentOutOfRangeException(nameof(layerIndex));

                int pixelCount = Mathf.Min(m_ExpectedPixelCount, scratchPixels.Length);
                int layerByteSize = pixelCount * 4;
                int relativeLayerByteOffset = layerIndex * layerByteSize;
                if (m_Payload.ByteSize < relativeLayerByteOffset + layerByteSize)
                {
                    FillFallback(layerIndex, scratchPixels, pixelCount);
                    stagingTexture.SetPixels32(scratchPixels, slice, 0);
                    return;
                }

                byte[] data = m_Payload.Data;
                int byteOffset = m_Payload.ByteOffset + relativeLayerByteOffset;
                for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                {
                    int sourceIndex = byteOffset + pixelIndex * 4;
                    scratchPixels[pixelIndex] = new Color32(
                        data[sourceIndex],
                        data[sourceIndex + 1],
                        data[sourceIndex + 2],
                        data[sourceIndex + 3]);
                }

                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
                m_Lease?.Dispose();
                m_Lease = null;
                VividVirtualTextureAssetProducer owner = m_Owner;
                m_Owner = null;
                m_Payload = default;
                m_ExpectedPixelCount = 0;
                m_Layers = null;
                owner?.ReturnFinalizer(this);
            }

            private void FillFallback(int layerIndex, Color32[] scratchPixels, int pixelCount)
            {
                Color32 fallbackColor = m_Layers[layerIndex].FallbackColor;
                for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                    scratchPixels[pixelIndex] = fallbackColor;
            }
        }

        private sealed class ChunkTileRequest : IVTPageProducerTask, IDisposable
        {
            private VTChunkLease m_Lease;

            internal void Initialize(
                VTChunkLease lease,
                in VividVirtualTextureTilePayloadLocation location)
            {
                m_Lease = lease ?? throw new ArgumentNullException(nameof(lease));
                Location = location;
            }

            internal VividVirtualTextureTilePayloadLocation Location { get; private set; }

            internal VTStreamChunkState State => m_Lease?.State ?? VTStreamChunkState.Failed;

            internal string Error => m_Lease?.Error;

            internal void PromotePriority(in VTRequestPriorityKey priorityKey)
            {
                m_Lease?.PromotePriority(priorityKey);
            }

            public bool IsCompleted => State is VTStreamChunkState.Ready or VTStreamChunkState.Failed;

            internal VTChunkLease DetachLease()
            {
                VTChunkLease lease = m_Lease;
                m_Lease = null;
                return lease;
            }

            internal bool TryGetPayload(out VividVirtualTextureTilePayload payload)
            {
                payload = default;
                return m_Lease != null && m_Lease.TryGetTilePayload(Location, out payload);
            }

            public void Dispose()
            {
                m_Lease?.Dispose();
                m_Lease = null;
                Location = default;
            }
        }

        private sealed class EncodedFinalizer : IVTEncodedPageFinalizer
        {
            private VividVirtualTextureAssetProducer m_Owner;
            private VividVirtualTextureTilePayload m_Payload;
            private VTLayerDesc[] m_Layers;
            private int m_PhysicalPageSize;
            private VTChunkLease m_Lease;

            internal void Initialize(
                VividVirtualTextureAssetProducer owner,
                in VividVirtualTextureTilePayload payload,
                VTLayerDesc[] layers,
                int physicalPageSize,
                VTChunkLease lease)
            {
                m_Owner = owner ?? throw new ArgumentNullException(nameof(owner));
                m_Payload = payload;
                m_Layers = layers ?? throw new ArgumentNullException(nameof(layers));
                m_PhysicalPageSize = physicalPageSize;
                m_Lease = lease ?? throw new ArgumentNullException(nameof(lease));
            }

            public int LayerCount => m_Layers.Length;

            public void FinalizeEncodedUploadLayer(Texture2DArray stagingTexture, int slice, int layerIndex)
            {
                if (stagingTexture == null)
                    throw new ArgumentNullException(nameof(stagingTexture));
                if (!m_Payload.IsValid)
                    throw new InvalidOperationException("[VividRP] Invalid encoded virtual texture tile payload.");
                if (layerIndex < 0 || layerIndex >= m_Layers.Length)
                    throw new ArgumentOutOfRangeException(nameof(layerIndex));

                int relativeOffset = 0;
                for (int index = 0; index < layerIndex; index++)
                    relativeOffset = checked(relativeOffset + GetLayerByteSize(m_Layers[index], m_PhysicalPageSize));
                int layerByteSize = GetLayerByteSize(m_Layers[layerIndex], m_PhysicalPageSize);
                if (relativeOffset > m_Payload.ByteSize - layerByteSize)
                    throw new InvalidOperationException("[VividRP] Encoded VT layer payload is truncated.");

                stagingTexture.SetPixelData(
                    m_Payload.Data,
                    mipLevel: 0,
                    element: slice,
                    sourceDataStartIndex: m_Payload.ByteOffset + relativeOffset);
            }

            public void Dispose()
            {
                m_Lease?.Dispose();
                m_Lease = null;
                VividVirtualTextureAssetProducer owner = m_Owner;
                m_Owner = null;
                m_Payload = default;
                m_Layers = null;
                m_PhysicalPageSize = 0;
                owner?.ReturnFinalizer(this);
            }

            private static int GetLayerByteSize(in VTLayerDesc layer, int physicalPageSize)
            {
                uint blockWidth = Math.Max(
                    1u,
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(layer.GraphicsFormat));
                uint blockHeight = Math.Max(
                    1u,
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(layer.GraphicsFormat));
                uint blockSize = Math.Max(
                    1u,
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(layer.GraphicsFormat));
                long blocksX = (physicalPageSize + blockWidth - 1) / blockWidth;
                long blocksY = (physicalPageSize + blockHeight - 1) / blockHeight;
                return checked((int)(blocksX * blocksY * blockSize));
            }
        }

        private static Func<string, int, int, CancellationToken, Task<byte[]>> s_StreamReadHandler = ReadRangeAsync;
        private static Func<string, int, int, byte[]> s_SynchronousStreamReadHandler = ReadRange;
        private static bool s_UseLegacyStreamReadHandlersForTesting;

        private readonly VividVirtualTextureAsset m_Asset;
        private readonly VividVirtualTextureBuiltData m_BuiltData;
        private readonly Dictionary<TileKey, StreamTileTask> m_StreamTasks = new();
        private readonly Dictionary<TileKey, ChunkTileRequest> m_ChunkRequests = new();
        private readonly Stack<ChunkTileRequest> m_ChunkRequestPool = new();
        private readonly Stack<Finalizer> m_FinalizerPool = new();
        private readonly Stack<EncodedFinalizer> m_EncodedFinalizerPool = new();
        private readonly HashSet<TileKey> m_LiveStreamTaskKeys = new();
        private readonly List<TileKey> m_RetiredStreamTaskKeys = new();
        private readonly string m_ResolvedStreamDataPath;
        private readonly bool m_StorageSupported;
        private readonly bool m_ContainerHeaderValid;
        private VTLayerDesc[] m_CachedLayers;
        private bool m_ChunkFailureWarningLogged;
        private bool m_HasPermanentFailure;
        private bool m_IsDisposed;

        internal VividVirtualTextureAssetProducer(VividVirtualTextureAsset asset)
        {
            m_Asset = asset != null
                ? asset
                : throw new ArgumentNullException(nameof(asset));
            m_BuiltData = asset.BuiltData != null
                ? asset.BuiltData
                : throw new ArgumentException("Virtual texture asset must contain built data.", nameof(asset));
            m_ResolvedStreamDataPath = ResolveStreamDataPath(
                m_BuiltData.StreamDataPath,
                m_BuiltData.RuntimeStreamDataPath);
            m_StorageSupported = IsStorageSupported(m_BuiltData, out string unsupportedStorageReason);
            if (!m_StorageSupported)
            {
                Debug.LogWarning(
                    $"[VividRP] Streamed VT asset '{asset.name}' cannot be registered: {unsupportedStorageReason} "
                    + "Material and page-table fallbacks remain active.",
                    asset);
            }
            m_ContainerHeaderValid = m_BuiltData.ContainerSchemaVersion < VividVirtualTextureBuiltData.CurrentContainerSchemaVersion
                                     || ValidateContainerHeader(m_ResolvedStreamDataPath, m_BuiltData);
            m_HasPermanentFailure = !m_StorageSupported || !m_ContainerHeaderValid;
            if (m_BuiltData.ContainerSchemaVersion >= VividVirtualTextureBuiltData.CurrentContainerSchemaVersion
                && !m_ContainerHeaderValid)
            {
                Debug.LogWarning(
                    $"[VividRP] Streamed VT asset '{asset.name}' has a missing, truncated, or mismatched v2 "
                    + "container header. The existing VT fallback remains active.",
                    asset);
            }

            string producerName = string.IsNullOrWhiteSpace(asset.name)
                ? nameof(VividVirtualTextureAsset)
                : asset.name;
            ProducerDesc = new VTProducerDesc(
                producerName,
                m_BuiltData.PageSize,
                m_BuiltData.BorderSize,
                m_BuiltData.VirtualPageCountX,
                m_BuiltData.VirtualPageCountY,
                m_BuiltData.MipCount,
                Mathf.Max(1, m_BuiltData.LayerCount),
                m_BuiltData.GraphicsFormat,
                m_BuiltData.LayerCount > 0 && m_BuiltData.Layers[0].SRGB,
                m_BuiltData.FallbackColor,
                producerPriority: 0,
                continuousUpdate: false,
                persistentLowestMip: true);
        }

        public string Name => $"{nameof(VividVirtualTextureAssetProducer)}({m_Asset.name})";

        public VTProducerDesc ProducerDesc { get; }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            VTRequestPriorityKey priorityKey = VTRequestPriorityKey.FromRequest(
                request,
                locked: false,
                producerPriority: ProducerDesc.ProducerPriority);
            return RequestPageData(desc, request, priorityKey);
        }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            in VTRequestPriorityKey priorityKey)
        {
            if (m_HasPermanentFailure)
                return VTPageRequestStatus.Invalid;

            if (!m_BuiltData.Matches(desc)
                || !m_BuiltData.TryGetTilePayloadLocation(request.PageCoord, out VividVirtualTextureTilePayloadLocation location))
            {
                m_HasPermanentFailure = true;
                return VTPageRequestStatus.Invalid;
            }

            if (UsesSharedChunkManager)
                return RequestChunkData(request, location, priorityKey);

            if (m_BuiltData.HasInlineRawData)
                return VTPageRequestStatus.Available;

            if (!m_BuiltData.HasStreamData || string.IsNullOrWhiteSpace(m_ResolvedStreamDataPath))
                return VTPageRequestStatus.Invalid;

            TileKey key = new(request.PageCoord);
            if (!TryGetOrStartStreamTask(
                    key,
                    location,
                    synchronous: request.PageCoord.Mip == m_BuiltData.MipCount - 1,
                    out StreamTileTask task))
            {
                return VTPageRequestStatus.Saturated;
            }

            if (task.IsCompletedSuccessfully)
                return VTPageRequestStatus.Available;

            if (task.IsCanceledOrFaulted)
            {
                RemoveStreamTask(key);
                return VTPageRequestStatus.Invalid;
            }

            return VTPageRequestStatus.Pending;
        }

        public IVTPageUploadFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!m_BuiltData.Matches(desc))
            {
                return null;
            }

            if (UsesSharedChunkManager)
                return ProduceEncodedPageData(desc, request);

            VividVirtualTextureTilePayload payload;
            if (m_BuiltData.HasInlineRawData)
            {
                if (!m_BuiltData.TryGetTilePayload(request.PageCoord, out payload))
                    return null;
            }
            else
            {
                TileKey key = new(request.PageCoord);
                if (!m_StreamTasks.TryGetValue(key, out StreamTileTask task) || !task.IsCompletedSuccessfully)
                    return null;

                byte[] data = task.Task.Result;
                RemoveStreamTask(key);
                payload = new VividVirtualTextureTilePayload(data, 0, data.Length);
            }

            int pixelCount = desc.PhysicalPageSize * desc.PhysicalPageSize;
            return RentFinalizer(
                payload,
                pixelCount,
                GetCachedLayers(desc.StackDesc));
        }

        private VTPageRequestStatus RequestChunkData(
            in VTRequest request,
            in VividVirtualTextureTilePayloadLocation location,
            in VTRequestPriorityKey requestPriorityKey)
        {
            if (!m_StorageSupported
                || !m_ContainerHeaderValid
                || !m_BuiltData.HasStreamData
                || string.IsNullOrWhiteSpace(m_ResolvedStreamDataPath))
            {
                m_HasPermanentFailure = true;
                return VTPageRequestStatus.Invalid;
            }

            TileKey key = new(request.PageCoord);
            bool mipTail = request.PageCoord.Mip == m_BuiltData.MipCount - 1
                           || (location.Flags & VividVirtualTextureChunkFlags.MipTail) != 0;
            VTRequestPriorityKey mipTailPriorityKey = VTRequestPriorityKey.FromRequest(
                request,
                locked: false,
                producerPriority: ProducerDesc.ProducerPriority,
                mipTail: mipTail);
            VTRequestPriorityKey priorityKey = VTRequestPriorityUtility.SelectHigher(
                requestPriorityKey,
                mipTailPriorityKey);
            if (!m_ChunkRequests.TryGetValue(key, out ChunkTileRequest chunkRequest))
            {
                VTChunkLease lease = VTStreamChunkManager.Shared.Acquire(
                    m_ResolvedStreamDataPath,
                    m_BuiltData.ContentVersion,
                    location,
                    priorityKey);
                if (lease == null)
                    return VTPageRequestStatus.Saturated;

                chunkRequest = RentChunkRequest(lease, location);
                m_ChunkRequests.Add(key, chunkRequest);
            }
            else
            {
                chunkRequest.PromotePriority(priorityKey);
            }

            if (chunkRequest.State == VTStreamChunkState.Ready)
                return VTPageRequestStatus.Available;
            if (chunkRequest.State == VTStreamChunkState.Failed)
            {
                m_HasPermanentFailure = true;
                if (!m_ChunkFailureWarningLogged)
                {
                    m_ChunkFailureWarningLogged = true;
                    Debug.LogWarning(
                        $"[VividRP] Streamed VT asset '{m_Asset.name}' rejected chunk {location.ChunkIndex}: "
                        + $"{chunkRequest.Error ?? "unknown read or decode failure"}. The existing VT fallback remains active.",
                        m_Asset);
                }

                RemoveChunkRequest(key);
                return VTPageRequestStatus.Invalid;
            }

            return VTPageRequestStatus.Pending;
        }

        private IVTPageUploadFinalizer ProduceEncodedPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            TileKey key = new(request.PageCoord);
            if (!m_ChunkRequests.TryGetValue(key, out ChunkTileRequest chunkRequest)
                || chunkRequest.State != VTStreamChunkState.Ready
                || !chunkRequest.TryGetPayload(out VividVirtualTextureTilePayload payload))
            {
                return null;
            }

            VTChunkLease lease = chunkRequest.DetachLease();
            m_ChunkRequests.Remove(key);
            ReturnChunkRequest(chunkRequest);
            if (m_BuiltData.StorageProfile == VividVirtualTextureStorageProfile.LegacyRGBA32)
            {
                return RentFinalizer(
                    payload,
                    desc.PhysicalPageSize * desc.PhysicalPageSize,
                    GetCachedLayers(desc.StackDesc),
                    lease);
            }

            return RentEncodedFinalizer(
                payload,
                GetCachedLayers(desc.StackDesc),
                desc.PhysicalPageSize,
                lease);
        }

        public void GatherTasks(List<IVTPageProducerTask> tasks)
        {
            if (tasks == null)
                return;

            foreach (StreamTileTask task in m_StreamTasks.Values)
            {
                if (!task.IsCompleted)
                    tasks.Add(task);
            }

            foreach (ChunkTileRequest request in m_ChunkRequests.Values)
            {
                if (!request.IsCompleted)
                    tasks.Add(request);
            }
        }

        public void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            RemoveStreamTask(new TileKey(request.PageCoord));
            RemoveChunkRequest(new TileKey(request.PageCoord));
        }

        public void RetireRequests(IReadOnlyList<VTRequest> liveRequests)
        {
            if (m_StreamTasks.Count == 0 && m_ChunkRequests.Count == 0)
                return;

            m_LiveStreamTaskKeys.Clear();
            if (liveRequests != null)
            {
                for (int requestIndex = 0; requestIndex < liveRequests.Count; requestIndex++)
                    m_LiveStreamTaskKeys.Add(new TileKey(liveRequests[requestIndex].PageCoord));
            }

            m_RetiredStreamTaskKeys.Clear();
            foreach (TileKey key in m_StreamTasks.Keys)
            {
                if (!m_LiveStreamTaskKeys.Contains(key))
                    m_RetiredStreamTaskKeys.Add(key);
            }

            foreach (TileKey key in m_ChunkRequests.Keys)
            {
                if (!m_LiveStreamTaskKeys.Contains(key))
                    m_RetiredStreamTaskKeys.Add(key);
            }

            for (int keyIndex = 0; keyIndex < m_RetiredStreamTaskKeys.Count; keyIndex++)
            {
                RemoveStreamTask(m_RetiredStreamTaskKeys[keyIndex]);
                RemoveChunkRequest(m_RetiredStreamTaskKeys[keyIndex]);
            }

            m_RetiredStreamTaskKeys.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            foreach (StreamTileTask task in m_StreamTasks.Values)
                task.Dispose();

            foreach (ChunkTileRequest request in m_ChunkRequests.Values)
                request.Dispose();

            m_StreamTasks.Clear();
            m_ChunkRequests.Clear();
            m_LiveStreamTaskKeys.Clear();
            m_RetiredStreamTaskKeys.Clear();
            m_ChunkRequestPool.Clear();
            m_FinalizerPool.Clear();
            m_EncodedFinalizerPool.Clear();
        }

        internal int PendingStreamTaskCountForTesting => m_StreamTasks.Count + m_ChunkRequests.Count;

        internal bool HasPermanentFailure => m_HasPermanentFailure;

        internal static void SetStreamReadHandlersForTesting(
            Func<string, int, int, CancellationToken, Task<byte[]>> asyncReadHandler,
            Func<string, int, int, byte[]> synchronousReadHandler = null)
        {
            s_StreamReadHandler = asyncReadHandler ?? ReadRangeAsync;
            s_SynchronousStreamReadHandler = synchronousReadHandler ?? ReadRange;
            s_UseLegacyStreamReadHandlersForTesting = true;
        }

        internal static void ResetStreamReadHandlersForTesting()
        {
            s_StreamReadHandler = ReadRangeAsync;
            s_SynchronousStreamReadHandler = ReadRange;
            s_UseLegacyStreamReadHandlersForTesting = false;
            VTVirtualTextureStreamRequestGate.ResetForTesting();
        }

        internal static void SetMaxPendingStreamReadCountForTesting(int maxPendingReadCount)
        {
            VTVirtualTextureStreamRequestGate.SetMaxPendingReadCountForTesting(maxPendingReadCount);
        }

        internal static int GlobalPendingStreamReadCountForTesting =>
            VTVirtualTextureStreamRequestGate.PendingReadCount;

        private bool UsesSharedChunkManager =>
            m_BuiltData.ContainerSchemaVersion >= VividVirtualTextureBuiltData.CurrentContainerSchemaVersion
            || (!m_BuiltData.HasInlineRawData && !s_UseLegacyStreamReadHandlersForTesting);

        private bool TryGetOrStartStreamTask(
            in TileKey key,
            in VividVirtualTextureTilePayloadLocation location,
            bool synchronous,
            out StreamTileTask task)
        {
            if (m_StreamTasks.TryGetValue(key, out task))
                return true;

            if (!synchronous && !VTVirtualTextureStreamRequestGate.TryAcquire())
            {
                task = null;
                return false;
            }

            try
            {
                task = synchronous
                    ? CreateCompletedStreamTask(location)
                    : CreateAsyncStreamTask(location);
            }
            catch
            {
                if (!synchronous)
                    VTVirtualTextureStreamRequestGate.Release();
                throw;
            }

            m_StreamTasks.Add(key, task);
            return true;
        }

        private StreamTileTask CreateCompletedStreamTask(in VividVirtualTextureTilePayloadLocation location)
        {
            try
            {
                byte[] data = s_SynchronousStreamReadHandler(
                    m_ResolvedStreamDataPath,
                    location.ByteOffset,
                    location.ByteSize);
                return new StreamTileTask(Task.FromResult(data), null, ownsGlobalReadSlot: false);
            }
            catch (Exception exception)
            {
                return new StreamTileTask(
                    Task.FromException<byte[]>(exception),
                    null,
                    ownsGlobalReadSlot: false);
            }
        }

        private StreamTileTask CreateAsyncStreamTask(in VividVirtualTextureTilePayloadLocation location)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            try
            {
                Task<byte[]> task = s_StreamReadHandler(
                    m_ResolvedStreamDataPath,
                    location.ByteOffset,
                    location.ByteSize,
                    cancellationTokenSource.Token);
                return new StreamTileTask(task, cancellationTokenSource, ownsGlobalReadSlot: true);
            }
            catch
            {
                cancellationTokenSource.Dispose();
                throw;
            }
        }

        private void RemoveStreamTask(in TileKey key)
        {
            if (!m_StreamTasks.TryGetValue(key, out StreamTileTask task))
                return;

            task.Dispose();
            m_StreamTasks.Remove(key);
        }

        private void RemoveChunkRequest(in TileKey key)
        {
            if (!m_ChunkRequests.TryGetValue(key, out ChunkTileRequest request))
                return;

            m_ChunkRequests.Remove(key);
            ReturnChunkRequest(request);
        }

        private ChunkTileRequest RentChunkRequest(
            VTChunkLease lease,
            in VividVirtualTextureTilePayloadLocation location)
        {
            ChunkTileRequest request = m_ChunkRequestPool.Count > 0
                ? m_ChunkRequestPool.Pop()
                : new ChunkTileRequest();
            request.Initialize(lease, location);
            return request;
        }

        private void ReturnChunkRequest(ChunkTileRequest request)
        {
            if (request == null)
                return;

            request.Dispose();
            if (!m_IsDisposed)
                m_ChunkRequestPool.Push(request);
        }

        private Finalizer RentFinalizer(
            in VividVirtualTextureTilePayload payload,
            int expectedPixelCount,
            VTLayerDesc[] layers,
            VTChunkLease lease = null)
        {
            Finalizer finalizer = m_FinalizerPool.Count > 0
                ? m_FinalizerPool.Pop()
                : new Finalizer();
            finalizer.Initialize(this, payload, expectedPixelCount, layers, lease);
            return finalizer;
        }

        private EncodedFinalizer RentEncodedFinalizer(
            in VividVirtualTextureTilePayload payload,
            VTLayerDesc[] layers,
            int physicalPageSize,
            VTChunkLease lease)
        {
            EncodedFinalizer finalizer = m_EncodedFinalizerPool.Count > 0
                ? m_EncodedFinalizerPool.Pop()
                : new EncodedFinalizer();
            finalizer.Initialize(this, payload, layers, physicalPageSize, lease);
            return finalizer;
        }

        private void ReturnFinalizer(Finalizer finalizer)
        {
            if (!m_IsDisposed && finalizer != null)
                m_FinalizerPool.Push(finalizer);
        }

        private void ReturnFinalizer(EncodedFinalizer finalizer)
        {
            if (!m_IsDisposed && finalizer != null)
                m_EncodedFinalizerPool.Push(finalizer);
        }

        private static bool IsStorageSupported(
            VividVirtualTextureBuiltData builtData,
            out string unsupportedReason)
        {
            if (builtData == null
                || builtData.StorageProfile == VividVirtualTextureStorageProfile.LegacyRGBA32)
            {
                unsupportedReason = null;
                return true;
            }

            CopyTextureSupport requiredCopySupport =
                CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes;
            if ((SystemInfo.copyTextureSupport & requiredCopySupport) != requiredCopySupport)
            {
                unsupportedReason =
                    "the active graphics device cannot CopyTexture compressed Texture2DArray slices into the 2D VT atlas";
                return false;
            }

            for (int layerIndex = 0; layerIndex < builtData.LayerCount; layerIndex++)
            {
                GraphicsFormat storageFormat = GraphicsFormatUtility.GetLinearFormat(builtData.Layers[layerIndex].Format);
                if (!SystemInfo.IsFormatSupported(storageFormat, GraphicsFormatUsage.Sample))
                {
                    unsupportedReason = $"physical layer {layerIndex} uses unsupported sample format {storageFormat}";
                    return false;
                }
            }

            unsupportedReason = null;
            return true;
        }

        private static bool ValidateContainerHeader(string path, VividVirtualTextureBuiltData builtData)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                var header = new byte[32];
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length != builtData.StreamDataByteSize64 || stream.Read(header, 0, header.Length) != header.Length)
                    return false;

                return header[0] == (byte)'V'
                       && header[1] == (byte)'I'
                       && header[2] == (byte)'V'
                       && header[3] == (byte)'I'
                       && header[4] == (byte)'D'
                       && header[5] == (byte)'V'
                       && header[6] == (byte)'T'
                       && header[7] == (byte)'2'
                       && BitConverter.ToInt32(header, 8) == builtData.ContainerSchemaVersion
                       && BitConverter.ToUInt32(header, 12) == builtData.ContentVersion
                       && BitConverter.ToInt32(header, 16) == builtData.ChunkCount;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ResolveStreamDataPath(
            string streamDataPath,
            string runtimeStreamDataPath)
        {
            if (!Application.isEditor && !string.IsNullOrWhiteSpace(runtimeStreamDataPath))
            {
                return Path.GetFullPath(Path.Combine(
                    Application.streamingAssetsPath,
                    runtimeStreamDataPath.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (string.IsNullOrWhiteSpace(streamDataPath))
                return string.Empty;

            string normalizedPath = streamDataPath.Replace('\\', '/');
            if (Path.IsPathRooted(normalizedPath))
                return Path.GetFullPath(normalizedPath);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));
        }

        private VTLayerDesc[] GetCachedLayers(in VTStackDesc stackDesc)
        {
            if (m_CachedLayers != null)
                return m_CachedLayers;

            var layers = new VTLayerDesc[Mathf.Max(1, stackDesc.LayerCount)];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                layers[layerIndex] = stackDesc.GetLayer(layerIndex);

            m_CachedLayers = layers;
            return m_CachedLayers;
        }

        private static byte[] ReadRange(string path, int byteOffset, int byteSize)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Stream data path must be non-empty.", nameof(path));
            if (byteOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            if (byteSize < 0)
                throw new ArgumentOutOfRangeException(nameof(byteSize));

            byte[] data = new byte[byteSize];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Seek(byteOffset, SeekOrigin.Begin);
            int readBytes = 0;
            while (readBytes < byteSize)
            {
                int count = stream.Read(data, readBytes, byteSize - readBytes);
                if (count == 0)
                    throw new EndOfStreamException($"Unexpected end of VT stream data '{path}'.");

                readBytes += count;
            }

            return data;
        }

        private static async Task<byte[]> ReadRangeAsync(
            string path,
            int byteOffset,
            int byteSize,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Stream data path must be non-empty.", nameof(path));
            if (byteOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            if (byteSize < 0)
                throw new ArgumentOutOfRangeException(nameof(byteSize));

            byte[] data = new byte[byteSize];
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Seek(byteOffset, SeekOrigin.Begin);
            int readBytes = 0;
            while (readBytes < byteSize)
            {
                int count = await stream.ReadAsync(
                    data,
                    readBytes,
                    byteSize - readBytes,
                    cancellationToken);
                if (count == 0)
                    throw new EndOfStreamException($"Unexpected end of VT stream data '{path}'.");

                readBytes += count;
            }

            return data;
        }
    }
}
