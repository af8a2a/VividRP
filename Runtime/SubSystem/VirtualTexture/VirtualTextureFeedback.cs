using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VirtualTextureFeedbackBatch
    {
        internal VirtualTextureFeedbackBatch(
            CameraType cameraType,
            ulong[] requests,
            int requestCount,
            int frameIndex,
            int feedbackOverflowCount = 0,
            int fallbackSampleCount = 0)
        {
            CameraType = cameraType;
            Requests = requests ?? Array.Empty<ulong>();
            RequestCount = Mathf.Clamp(requestCount, 0, Requests.Length);
            FrameIndex = frameIndex;
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
        }

        internal CameraType CameraType { get; }

        internal ulong[] Requests { get; }

        internal int RequestCount { get; }

        internal int FrameIndex { get; }

        internal int FeedbackOverflowCount { get; }

        internal int FallbackSampleCount { get; }
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
        private static readonly IComparer<VirtualTextureAggregatedFeedbackRequest> s_RequestComparer = AggregatedRequestComparer.Instance;

        internal struct FaultAccumulator
        {
            public int HitCount;
            public int CameraPriority;
        }

        internal sealed class Scratch
        {
            private readonly Dictionary<ulong, FaultAccumulator> m_FaultAccumulators = new();

            internal Dictionary<ulong, FaultAccumulator> FaultAccumulators => m_FaultAccumulators;
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
            var aggregated = new List<VirtualTextureAggregatedFeedbackRequest>();
            Aggregate(batches, new Scratch(), aggregated);
            return aggregated;
        }

        internal static void Aggregate(
            IReadOnlyList<VirtualTextureFeedbackBatch> batches,
            Scratch scratch,
            List<VirtualTextureAggregatedFeedbackRequest> output)
        {
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            output.Clear();
            Dictionary<ulong, FaultAccumulator> faultAccumulators = scratch.FaultAccumulators;
            faultAccumulators.Clear();

            if (batches == null)
                return;

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
                output.Add(new VirtualTextureAggregatedFeedbackRequest(
                    spaceId,
                    pageCoord,
                    pair.Value.HitCount,
                    pair.Value.CameraPriority));
            }

            output.Sort(s_RequestComparer);
        }

        private static int CompareRequests(
            VirtualTextureAggregatedFeedbackRequest left,
            VirtualTextureAggregatedFeedbackRequest right)
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
        }

        private sealed class AggregatedRequestComparer : IComparer<VirtualTextureAggregatedFeedbackRequest>
        {
            internal static readonly AggregatedRequestComparer Instance = new();

            private AggregatedRequestComparer()
            {
            }

            public int Compare(
                VirtualTextureAggregatedFeedbackRequest left,
                VirtualTextureAggregatedFeedbackRequest right)
            {
                return CompareRequests(left, right);
            }
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

        internal Dictionary<int, VirtualTextureFeedbackBufferState> EnumerateSpaceStates()
        {
            return m_SpaceStates;
        }

        internal bool RemoveSpaceState(int spaceId)
        {
            if (!m_SpaceStates.TryGetValue(spaceId, out VirtualTextureFeedbackBufferState state))
                return false;

            state.Dispose();
            m_SpaceStates.Remove(spaceId);
            return true;
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
        internal Dictionary<Camera, VirtualTextureFeedbackCameraState> EnumerateStates()
        {
            return m_CameraStates;
        }

        internal void RemoveSpaceState(int spaceId)
        {
            foreach (VirtualTextureFeedbackCameraState state in m_CameraStates.Values)
                state.RemoveSpaceState(spaceId);
        }
    }

    internal sealed class VirtualTextureFeedbackBufferState : IDisposable
    {
        private sealed class BufferPairState : IDisposable
        {
            public readonly Action<AsyncGPUReadbackRequest> RequestsReadbackCallback;
            public readonly Action<AsyncGPUReadbackRequest> CounterReadbackCallback;
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
            public int CompletedFallbackSampleCount;

            public BufferPairState()
            {
                RequestsReadbackCallback = HandleRequestsReadback;
                CounterReadbackCallback = HandleCounterReadback;
            }

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
                CompletedFallbackSampleCount = 0;
                ScheduledFrameIndex = -1;
            }

            private void HandleRequestsReadback(AsyncGPUReadbackRequest request)
            {
                RequestReadbackPending = false;
                if (!request.hasError)
                {
                    NativeArray<ulong> data = request.GetData<ulong>();
                    EnsureCompletedRequestCapacity(data.Length);
                    data.CopyTo(CompletedRequests);
                }
                else
                {
                    CompletedRequests = Array.Empty<ulong>();
                    CompletedCount = 0u;
                    CompletedFallbackSampleCount = 0;
                }

                CompleteReadbackIfReady();
            }

            private void HandleCounterReadback(AsyncGPUReadbackRequest request)
            {
                CounterReadbackPending = false;
                if (!request.hasError)
                {
                    NativeArray<uint> data = request.GetData<uint>();
                    CompletedCount = data.Length > 0 ? data[0] : 0u;
                    CompletedFallbackSampleCount = data.Length > 1 ? SaturatingUIntToInt(data[1]) : 0;
                }
                else
                {
                    CompletedCount = 0u;
                    CompletedFallbackSampleCount = 0;
                }

                CompleteReadbackIfReady();
            }

            private void EnsureCompletedRequestCapacity(int capacity)
            {
                if (capacity <= 0)
                {
                    CompletedRequests = Array.Empty<ulong>();
                    return;
                }

                if (CompletedRequests.Length != capacity)
                    CompletedRequests = new ulong[capacity];
            }

            private void CompleteReadbackIfReady()
            {
                if (RequestReadbackPending || CounterReadbackPending)
                    return;

                ReadbackPending = false;
                HasCompletedReadback = true;
            }
        }

        private const int FeedbackCounterElementCount = 2;
        private static readonly uint[] s_ZeroCounterData = { 0u, 0u };

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

                int completedRequestCount = SaturatingUIntToInt(pair.CompletedCount);
                int requestCount = Mathf.Min(pair.CompletedRequests.Length, completedRequestCount);
                int overflowCount = Mathf.Max(0, completedRequestCount - pair.CompletedRequests.Length);
                output.Add(new VirtualTextureFeedbackBatch(
                    pair.LastCameraType,
                    pair.CompletedRequests,
                    requestCount,
                    pair.ScheduledFrameIndex,
                    overflowCount,
                    pair.CompletedFallbackSampleCount));
                lastReadbackFrame = Mathf.Max(lastReadbackFrame, pair.ScheduledFrameIndex);
                pair.HasCompletedReadback = false;
                pair.CompletedCount = 0u;
                pair.CompletedFallbackSampleCount = 0;
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
                pair.CounterBuffer = new ComputeBuffer(FeedbackCounterElementCount, sizeof(uint), ComputeBufferType.Structured);
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

            AsyncGPUReadback.Request(pair.RequestsBuffer, pair.RequestsReadbackCallback);
            AsyncGPUReadback.Request(pair.CounterBuffer, pair.CounterReadbackCallback);
        }

        private static int SaturatingUIntToInt(uint value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
