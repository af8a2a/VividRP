using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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

    internal sealed class VividVirtualTextureAssetProducer : IVTPageProducer, IVTPageRequestRetirement, IDisposable
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
            private readonly VividVirtualTextureTilePayload m_Payload;
            private readonly int m_ExpectedPixelCount;
            private readonly VTLayerDesc[] m_Layers;

            internal Finalizer(
                in VividVirtualTextureTilePayload payload,
                int expectedPixelCount,
                VTLayerDesc[] layers)
            {
                m_Payload = payload;
                m_ExpectedPixelCount = expectedPixelCount;
                m_Layers = layers != null && layers.Length > 0
                    ? layers
                    : new[]
                    {
                        new VTLayerDesc(
                            VTLayerSemantic.BaseColor,
                            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                            false,
                            new Color32(0, 0, 0, 255)),
                    };
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
            }

            private void FillFallback(int layerIndex, Color32[] scratchPixels, int pixelCount)
            {
                Color32 fallbackColor = m_Layers[layerIndex].FallbackColor;
                for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                    scratchPixels[pixelIndex] = fallbackColor;
            }
        }

        private static Func<string, int, int, CancellationToken, Task<byte[]>> s_StreamReadHandler = ReadRangeAsync;
        private static Func<string, int, int, byte[]> s_SynchronousStreamReadHandler = ReadRange;

        private readonly VividVirtualTextureAsset m_Asset;
        private readonly VividVirtualTextureBuiltData m_BuiltData;
        private readonly Dictionary<TileKey, StreamTileTask> m_StreamTasks = new();
        private readonly HashSet<TileKey> m_LiveStreamTaskKeys = new();
        private readonly List<TileKey> m_RetiredStreamTaskKeys = new();
        private readonly string m_ResolvedStreamDataPath;

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
            if (!m_BuiltData.Matches(desc)
                || !m_BuiltData.TryGetTilePayloadLocation(request.PageCoord, out VividVirtualTextureTilePayloadLocation location))
            {
                return VTPageRequestStatus.Invalid;
            }

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
            return new Finalizer(payload, pixelCount, CopyLayers(desc.StackDesc));
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
        }

        public void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            RemoveStreamTask(new TileKey(request.PageCoord));
        }

        public void RetireRequests(IReadOnlyList<VTRequest> liveRequests)
        {
            if (m_StreamTasks.Count == 0)
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

            for (int keyIndex = 0; keyIndex < m_RetiredStreamTaskKeys.Count; keyIndex++)
                RemoveStreamTask(m_RetiredStreamTaskKeys[keyIndex]);

            m_RetiredStreamTaskKeys.Clear();
        }

        public void Dispose()
        {
            foreach (StreamTileTask task in m_StreamTasks.Values)
                task.Dispose();

            m_StreamTasks.Clear();
            m_LiveStreamTaskKeys.Clear();
            m_RetiredStreamTaskKeys.Clear();
        }

        internal int PendingStreamTaskCountForTesting => m_StreamTasks.Count;

        internal static void SetStreamReadHandlersForTesting(
            Func<string, int, int, CancellationToken, Task<byte[]>> asyncReadHandler,
            Func<string, int, int, byte[]> synchronousReadHandler = null)
        {
            s_StreamReadHandler = asyncReadHandler ?? ReadRangeAsync;
            s_SynchronousStreamReadHandler = synchronousReadHandler ?? ReadRange;
        }

        internal static void ResetStreamReadHandlersForTesting()
        {
            s_StreamReadHandler = ReadRangeAsync;
            s_SynchronousStreamReadHandler = ReadRange;
            VTVirtualTextureStreamRequestGate.ResetForTesting();
        }

        internal static void SetMaxPendingStreamReadCountForTesting(int maxPendingReadCount)
        {
            VTVirtualTextureStreamRequestGate.SetMaxPendingReadCountForTesting(maxPendingReadCount);
        }

        internal static int GlobalPendingStreamReadCountForTesting =>
            VTVirtualTextureStreamRequestGate.PendingReadCount;

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

        private static VTLayerDesc[] CopyLayers(in VTStackDesc stackDesc)
        {
            var layers = new VTLayerDesc[Mathf.Max(1, stackDesc.LayerCount)];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                layers[layerIndex] = stackDesc.GetLayer(layerIndex);

            return layers;
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
