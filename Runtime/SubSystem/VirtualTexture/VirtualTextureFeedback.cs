using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VirtualTextureFeedbackBatch
    {
        internal VirtualTextureFeedbackBatch(CameraType cameraType, ulong[] requests, int requestCount, int frameIndex)
        {
            CameraType = cameraType;
            Requests = requests ?? Array.Empty<ulong>();
            RequestCount = Mathf.Clamp(requestCount, 0, Requests.Length);
            FrameIndex = frameIndex;
        }

        internal CameraType CameraType { get; }

        internal ulong[] Requests { get; }

        internal int RequestCount { get; }

        internal int FrameIndex { get; }
    }

    internal readonly struct VirtualTextureAggregatedFeedbackRequest
    {
        internal VirtualTextureAggregatedFeedbackRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int hitCount,
            int cameraPriority)
        {
            SpaceId = spaceId;
            PageCoord = pageCoord;
            HitCount = hitCount;
            CameraPriority = cameraPriority;
        }

        internal int SpaceId { get; }

        internal VirtualTexturePageCoord PageCoord { get; }

        internal int HitCount { get; }

        internal int CameraPriority { get; }
    }

    internal static class VirtualTextureFeedbackProcessor
    {
        internal const int SpaceIdBitCount = 16;
        internal const int PageCoordBitCount = 20;
        internal const int MaxMipCount = 16;
        private const int SpaceIdMask = (1 << SpaceIdBitCount) - 1;
        private const int PageCoordMask = (1 << PageCoordBitCount) - 1;

        private struct FaultAccumulator
        {
            public int HitCount;
            public int CameraPriority;
        }

        internal static ulong EncodeKey(int spaceId, in VirtualTexturePageCoord pageCoord)
        {
            if ((uint)spaceId > SpaceIdMask)
                throw new ArgumentOutOfRangeException(nameof(spaceId));
            if ((uint)pageCoord.X > PageCoordMask || (uint)pageCoord.Y > PageCoordMask)
                throw new ArgumentOutOfRangeException(nameof(pageCoord));
            if ((uint)pageCoord.Mip >= MaxMipCount)
                throw new ArgumentOutOfRangeException(nameof(pageCoord));

            uint low = (uint)spaceId | (((uint)pageCoord.X & 0xFFFFu) << 16);
            uint high = (((uint)pageCoord.X >> 16) & 0xFu)
                        | (((uint)pageCoord.Y & PageCoordMask) << 4)
                        | (((uint)pageCoord.Mip & 0xFFu) << 24);
            return ((ulong)high << 32) | low;
        }

        internal static void DecodeKey(ulong key, out int spaceId, out VirtualTexturePageCoord pageCoord)
        {
            uint low = (uint)(key & 0xFFFFFFFFu);
            uint high = (uint)(key >> 32);

            spaceId = (int)(low & SpaceIdMask);
            int x = (int)(((low >> 16) & 0xFFFFu) | ((high & 0xFu) << 16));
            int y = (int)((high >> 4) & PageCoordMask);
            int mip = (int)((high >> 24) & 0xFFu);
            pageCoord = new VirtualTexturePageCoord(x, y, mip);
        }

        internal static int GetCameraPriority(CameraType cameraType)
        {
            return cameraType switch
            {
                CameraType.Game => 0,
                CameraType.SceneView => 1,
                _ => 2,
            };
        }

        internal static List<VirtualTextureAggregatedFeedbackRequest> Aggregate(
            IReadOnlyList<VirtualTextureFeedbackBatch> batches)
        {
            var faultAccumulators = new Dictionary<ulong, FaultAccumulator>();
            var aggregated = new List<VirtualTextureAggregatedFeedbackRequest>();

            if (batches == null)
                return aggregated;

            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                VirtualTextureFeedbackBatch batch = batches[batchIndex];
                int cameraPriority = GetCameraPriority(batch.CameraType);
                for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
                {
                    ulong key = batch.Requests[requestIndex];
                    if (faultAccumulators.TryGetValue(key, out FaultAccumulator accumulator))
                    {
                        accumulator.HitCount += 1;
                        accumulator.CameraPriority = Mathf.Min(accumulator.CameraPriority, cameraPriority);
                        faultAccumulators[key] = accumulator;
                    }
                    else
                    {
                        faultAccumulators[key] = new FaultAccumulator
                        {
                            HitCount = 1,
                            CameraPriority = cameraPriority,
                        };
                    }
                }
            }

            foreach (KeyValuePair<ulong, FaultAccumulator> pair in faultAccumulators)
            {
                DecodeKey(pair.Key, out int spaceId, out VirtualTexturePageCoord pageCoord);
                aggregated.Add(new VirtualTextureAggregatedFeedbackRequest(
                    spaceId,
                    pageCoord,
                    pair.Value.HitCount,
                    pair.Value.CameraPriority));
            }

            aggregated.Sort(static (left, right) =>
            {
                int mipCompare = left.PageCoord.Mip.CompareTo(right.PageCoord.Mip);
                if (mipCompare != 0)
                    return mipCompare;

                int hitCompare = right.HitCount.CompareTo(left.HitCount);
                if (hitCompare != 0)
                    return hitCompare;

                int cameraCompare = left.CameraPriority.CompareTo(right.CameraPriority);
                if (cameraCompare != 0)
                    return cameraCompare;

                int spaceCompare = left.SpaceId.CompareTo(right.SpaceId);
                if (spaceCompare != 0)
                    return spaceCompare;

                int yCompare = left.PageCoord.Y.CompareTo(right.PageCoord.Y);
                if (yCompare != 0)
                    return yCompare;

                return left.PageCoord.X.CompareTo(right.PageCoord.X);
            });

            return aggregated;
        }
    }

    internal sealed class VirtualTextureFeedbackCameraState : CameraRelativeState
    {
        private readonly Dictionary<int, VirtualTextureFeedbackBufferState> m_SpaceStates = new();

        internal VirtualTextureFeedbackBufferState GetOrCreateSpaceState(int spaceId)
        {
            if (m_SpaceStates.TryGetValue(spaceId, out VirtualTextureFeedbackBufferState state))
                return state;

            state = new VirtualTextureFeedbackBufferState(spaceId);
            m_SpaceStates.Add(spaceId, state);
            return state;
        }

        internal IEnumerable<KeyValuePair<int, VirtualTextureFeedbackBufferState>> EnumerateSpaceStates()
        {
            return m_SpaceStates;
        }

        public override void Dispose()
        {
            foreach (VirtualTextureFeedbackBufferState state in m_SpaceStates.Values)
                state.Dispose();

            m_SpaceStates.Clear();
        }
    }

    internal sealed class VirtualTextureFeedbackCameraSystem : CameraRelativeSystem<VirtualTextureFeedbackCameraState>
    {
        internal IEnumerable<KeyValuePair<Camera, VirtualTextureFeedbackCameraState>> EnumerateStates()
        {
            return m_CameraStates;
        }
    }

    internal sealed class VirtualTextureFeedbackBufferState : IDisposable
    {
        private sealed class BufferPairState : IDisposable
        {
            public ComputeBuffer RequestsBuffer;
            public ComputeBuffer CounterBuffer;
            public bool WasWritten;
            public bool ReadbackPending;
            public bool RequestReadbackPending;
            public bool CounterReadbackPending;
            public bool HasCompletedReadback;
            public CameraType LastCameraType;
            public int ScheduledFrameIndex = -1;
            public ulong[] CompletedRequests = Array.Empty<ulong>();
            public uint CompletedCount;

            public void Dispose()
            {
                RequestsBuffer?.Dispose();
                CounterBuffer?.Dispose();
                RequestsBuffer = null;
                CounterBuffer = null;
                WasWritten = false;
                ReadbackPending = false;
                RequestReadbackPending = false;
                CounterReadbackPending = false;
                HasCompletedReadback = false;
                CompletedRequests = Array.Empty<ulong>();
                CompletedCount = 0u;
                ScheduledFrameIndex = -1;
            }
        }

        private static readonly uint[] s_ZeroCounterData = { 0u };

        private readonly BufferPairState[] m_BufferPairs = { new(), new() };
        private readonly int m_SpaceId;
        private int m_RequestCapacity;
        private int m_WriteBufferIndex;
        private bool m_IsDisposed;

        internal VirtualTextureFeedbackBufferState(int spaceId)
        {
            m_SpaceId = spaceId;
        }

        internal bool TryPrepareForFrame(
            CommandBuffer cmd,
            string spaceName,
            Camera camera,
            int feedbackCapacity,
            int frameIndex,
            out ComputeBuffer requestBuffer,
            out ComputeBuffer counterBuffer,
            out string statusMessage)
        {
            requestBuffer = null;
            counterBuffer = null;
            statusMessage = string.Empty;

            if (cmd == null || camera == null)
                return false;

            EnsureCapacity(spaceName, feedbackCapacity);

            int readBufferIndex = 1 - m_WriteBufferIndex;
            BufferPairState readPair = m_BufferPairs[readBufferIndex];
            if (readPair.WasWritten && !readPair.ReadbackPending)
                ScheduleReadback(readPair, camera.cameraType);

            BufferPairState writePair = m_BufferPairs[m_WriteBufferIndex];
            if (writePair.ReadbackPending)
            {
                statusMessage = $"[VividRP] VT feedback buffer is still pending readback for space '{spaceName}'.";
                return false;
            }

            cmd.SetBufferData(writePair.CounterBuffer, s_ZeroCounterData);
            writePair.WasWritten = true;
            writePair.LastCameraType = camera.cameraType;
            writePair.ScheduledFrameIndex = frameIndex;

            requestBuffer = writePair.RequestsBuffer;
            counterBuffer = writePair.CounterBuffer;
            m_WriteBufferIndex = readBufferIndex;
            return true;
        }

        internal void CollectCompletedReadbacks(List<VirtualTextureFeedbackBatch> output, ref int lastReadbackFrame)
        {
            if (output == null)
                return;

            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                if (!pair.HasCompletedReadback)
                    continue;

                int requestCount = Mathf.Min(pair.CompletedRequests.Length, (int)pair.CompletedCount);
                output.Add(new VirtualTextureFeedbackBatch(
                    pair.LastCameraType,
                    pair.CompletedRequests,
                    requestCount,
                    pair.ScheduledFrameIndex));
                lastReadbackFrame = Mathf.Max(lastReadbackFrame, pair.ScheduledFrameIndex);
                pair.HasCompletedReadback = false;
                pair.CompletedRequests = Array.Empty<ulong>();
                pair.CompletedCount = 0u;
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
                m_BufferPairs[bufferIndex].Dispose();

            m_RequestCapacity = 0;
            m_IsDisposed = true;
        }

        private void EnsureCapacity(string spaceName, int feedbackCapacity)
        {
            if (m_RequestCapacity == feedbackCapacity
                && m_BufferPairs[0].RequestsBuffer != null
                && m_BufferPairs[0].CounterBuffer != null
                && m_BufferPairs[1].RequestsBuffer != null
                && m_BufferPairs[1].CounterBuffer != null)
            {
                return;
            }

            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
                m_BufferPairs[bufferIndex].Dispose();

            m_RequestCapacity = feedbackCapacity;
            m_WriteBufferIndex = 0;
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                pair.RequestsBuffer = new ComputeBuffer(feedbackCapacity, sizeof(ulong), ComputeBufferType.Structured);
                pair.RequestsBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackRequests_{bufferIndex}";
                pair.CounterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
                pair.CounterBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackCounter_{bufferIndex}";
            }
        }

        private static void ScheduleReadback(BufferPairState pair, CameraType cameraType)
        {
            if (pair == null || pair.ReadbackPending || pair.RequestsBuffer == null || pair.CounterBuffer == null)
                return;

            pair.ReadbackPending = true;
            pair.RequestReadbackPending = true;
            pair.CounterReadbackPending = true;
            pair.LastCameraType = cameraType;

            AsyncGPUReadback.Request(pair.RequestsBuffer, request => HandleRequestsReadback(pair, request));
            AsyncGPUReadback.Request(pair.CounterBuffer, request => HandleCounterReadback(pair, request));
        }

        private static void HandleRequestsReadback(BufferPairState pair, AsyncGPUReadbackRequest request)
        {
            if (pair == null)
                return;

            pair.RequestReadbackPending = false;
            if (!request.hasError)
            {
                NativeArray<ulong> data = request.GetData<ulong>();
                ulong[] completedRequests = new ulong[data.Length];
                data.CopyTo(completedRequests);
                pair.CompletedRequests = completedRequests;
            }
            else
            {
                pair.CompletedRequests = Array.Empty<ulong>();
            }

            CompleteReadbackIfReady(pair);
        }

        private static void HandleCounterReadback(BufferPairState pair, AsyncGPUReadbackRequest request)
        {
            if (pair == null)
                return;

            pair.CounterReadbackPending = false;
            if (!request.hasError)
            {
                NativeArray<uint> data = request.GetData<uint>();
                pair.CompletedCount = data.Length > 0 ? data[0] : 0u;
            }
            else
            {
                pair.CompletedCount = 0u;
            }

            CompleteReadbackIfReady(pair);
        }

        private static void CompleteReadbackIfReady(BufferPairState pair)
        {
            if (pair.RequestReadbackPending || pair.CounterReadbackPending)
                return;

            pair.ReadbackPending = false;
            pair.HasCompletedReadback = true;
        }
    }
}
