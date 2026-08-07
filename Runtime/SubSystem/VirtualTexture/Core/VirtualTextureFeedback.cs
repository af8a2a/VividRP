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
            : this(
                VirtualTextureViewId.FromCameraType(cameraType),
                cameraType,
                requests,
                requestCount,
                frameIndex,
                feedbackOverflowCount,
                fallbackSampleCount)
        {
        }

        internal VirtualTextureFeedbackBatch(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            ulong[] requests,
            int requestCount,
            int frameIndex,
            int feedbackOverflowCount = 0,
            int fallbackSampleCount = 0)
        {
            ViewId = viewId;
            CameraType = cameraType;
            ManagedRequests = requests ?? Array.Empty<ulong>();
            NativeRequests = default;
            RequestCount = Mathf.Clamp(requestCount, 0, ManagedRequests.Length);
            FrameIndex = frameIndex;
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
        }

        internal VirtualTextureFeedbackBatch(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            NativeArray<ulong> requests,
            int requestCount,
            int frameIndex,
            int feedbackOverflowCount = 0,
            int fallbackSampleCount = 0)
        {
            ViewId = viewId;
            CameraType = cameraType;
            ManagedRequests = null;
            NativeRequests = requests;
            RequestCount = Mathf.Clamp(requestCount, 0, requests.IsCreated ? requests.Length : 0);
            FrameIndex = frameIndex;
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
        }

        internal VirtualTextureViewId ViewId { get; }

        internal CameraType CameraType { get; }

        internal ulong[] ManagedRequests { get; }

        internal NativeArray<ulong> NativeRequests { get; }

        internal int RequestCapacity => NativeRequests.IsCreated
            ? NativeRequests.Length
            : ManagedRequests?.Length ?? 0;

        internal int RequestCount { get; }

        internal int FrameIndex { get; }

        internal int FeedbackOverflowCount { get; }

        internal int FallbackSampleCount { get; }

        internal ulong GetRequest(int requestIndex)
        {
            return NativeRequests.IsCreated
                ? NativeRequests[requestIndex]
                : ManagedRequests[requestIndex];
        }

        internal void CopyRequestsTo(
            NativeArray<ulong> destination,
            int destinationIndex,
            int requestCount)
        {
            int copyCount = Mathf.Clamp(requestCount, 0, RequestCount);
            if (copyCount == 0)
                return;

            if (NativeRequests.IsCreated)
            {
                NativeArray<ulong>.Copy(
                    NativeRequests,
                    0,
                    destination,
                    destinationIndex,
                    copyCount);
                return;
            }

            NativeArray<ulong>.Copy(
                ManagedRequests,
                0,
                destination,
                destinationIndex,
                copyCount);
        }
    }

    internal readonly struct VirtualTextureAggregatedFeedbackRequest
    {
        internal VirtualTextureAggregatedFeedbackRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int hitCount,
            int cameraPriority)
            : this(
                spaceId,
                pageCoord,
                hitCount,
                cameraPriority,
                VirtualTextureViewId.Invalid,
                false)
        {
        }

        internal VirtualTextureAggregatedFeedbackRequest(
            int spaceId,
            VirtualTexturePageCoord pageCoord,
            int hitCount,
            int cameraPriority,
            VirtualTextureViewId viewId,
            bool isActiveView)
        {
            SpaceId = spaceId;
            PageCoord = pageCoord;
            HitCount = hitCount;
            CameraPriority = cameraPriority;
            ViewId = viewId;
            IsActiveView = isActiveView;
        }

        internal int SpaceId { get; }

        internal VirtualTexturePageCoord PageCoord { get; }

        internal int HitCount { get; }

        internal int CameraPriority { get; }

        internal VirtualTextureViewId ViewId { get; }

        internal bool IsActiveView { get; }
    }

    internal readonly struct VirtualTextureFeedbackViewSignature : IEquatable<VirtualTextureFeedbackViewSignature>
    {
        internal static readonly VirtualTextureFeedbackViewSignature Invalid = default;

        internal VirtualTextureFeedbackViewSignature(
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            int actualWidth,
            int actualHeight,
            int pixelWidth,
            int pixelHeight)
        {
            ViewMatrix = viewMatrix;
            ProjectionMatrix = projectionMatrix;
            ActualWidth = actualWidth;
            ActualHeight = actualHeight;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            IsValid = true;
        }

        internal Matrix4x4 ViewMatrix { get; }

        internal Matrix4x4 ProjectionMatrix { get; }

        internal int ActualWidth { get; }

        internal int ActualHeight { get; }

        internal int PixelWidth { get; }

        internal int PixelHeight { get; }

        internal bool IsValid { get; }

        internal static VirtualTextureFeedbackViewSignature FromCameraData(VividCameraData cameraData)
        {
            if (cameraData?.camera == null)
                return Invalid;

            return new VirtualTextureFeedbackViewSignature(
                cameraData.viewMatrix,
                cameraData.nonJitteredProjectionMatrix,
                cameraData.actualWidth,
                cameraData.actualHeight,
                cameraData.pixelWidth,
                cameraData.pixelHeight);
        }

        public bool Equals(VirtualTextureFeedbackViewSignature other)
        {
            return IsValid == other.IsValid
                   && ViewMatrix.Equals(other.ViewMatrix)
                   && ProjectionMatrix.Equals(other.ProjectionMatrix)
                   && ActualWidth == other.ActualWidth
                   && ActualHeight == other.ActualHeight
                   && PixelWidth == other.PixelWidth
                   && PixelHeight == other.PixelHeight;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualTextureFeedbackViewSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                ViewMatrix,
                ProjectionMatrix,
                ActualWidth,
                ActualHeight,
                PixelWidth,
                PixelHeight,
                IsValid);
        }
    }

    internal static class VirtualTextureFeedbackProcessor
    {
        internal const int SpaceIdBitCount = 16;
        internal const int PageCoordBitCount = 20;
        internal const int MaxMipCount = 16;
        internal const int MaxPageCountPerDimension = 1 << PageCoordBitCount;
        private const int SpaceIdMask = (1 << SpaceIdBitCount) - 1;
        private const int PageCoordMask = (1 << PageCoordBitCount) - 1;

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
            using var aggregator = new VTFeedbackNativeAggregator();
            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);
            NativeArray<VirtualTextureAggregatedFeedbackRequest> nativeRequests = aggregator.AggregatedRequests;
            for (int requestIndex = 0; requestIndex < nativeRequests.Length; requestIndex++)
                aggregated.Add(nativeRequests[requestIndex]);

            return aggregated;
        }

        internal static VirtualTextureViewId ResolveFeedbackViewId(
            in VirtualTextureFeedbackBatch batch,
            in VirtualTextureViewId activeViewId)
        {
            return activeViewId.IsValid ? activeViewId : batch.ViewId;
        }

        internal static bool IsActiveViewBatch(
            in VirtualTextureFeedbackBatch batch,
            VirtualTextureViewId activeViewId)
        {
            if (!activeViewId.IsValid && !activeViewId.IsCameraTypeOnly)
                return false;

            return activeViewId.IsValid
                ? batch.ViewId.Equals(activeViewId)
                  || (!batch.ViewId.IsValid && batch.CameraType == activeViewId.CameraType)
                : batch.CameraType == activeViewId.CameraType;
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
            public ComputeBuffer RequestsBuffer;
            public ComputeBuffer CounterBuffer;
            public bool WasWritten;
            public bool ReadbackPending;
            public bool RequestReadbackPending;
            public bool CounterReadbackPending;
            public bool HasCompletedReadback;
            public VirtualTextureViewId LastViewId;
            public CameraType LastCameraType;
            public VirtualTextureFeedbackViewSignature LastViewSignature;
            public int ScheduledFrameIndex = -1;
            public NativeArray<ulong> RequestsReadbackData;
            public NativeArray<uint> CounterReadbackData;
            public AsyncGPUReadbackRequest RequestsReadbackRequest;
            public AsyncGPUReadbackRequest CounterReadbackRequest;
            public bool CompletedRequestsValid;
            public uint CompletedCount;
            public int CompletedFallbackSampleCount;

            public void Dispose()
            {
                if (ReadbackPending)
                    AsyncGPUReadback.WaitAllRequests();

                RequestsBuffer?.Dispose();
                CounterBuffer?.Dispose();
                DisposeReadbackData();
                RequestsBuffer = null;
                CounterBuffer = null;
                WasWritten = false;
                ReadbackPending = false;
                RequestReadbackPending = false;
                CounterReadbackPending = false;
                HasCompletedReadback = false;
                RequestsReadbackRequest = default;
                CounterReadbackRequest = default;
                LastViewId = VirtualTextureViewId.Invalid;
                LastViewSignature = VirtualTextureFeedbackViewSignature.Invalid;
                CompletedRequestsValid = false;
                CompletedCount = 0u;
                CompletedFallbackSampleCount = 0;
                ScheduledFrameIndex = -1;
            }

            public void EnsureReadbackCapacity(int requestCapacity)
            {
                if (RequestsReadbackData.IsCreated
                    && RequestsReadbackData.Length == requestCapacity
                    && CounterReadbackData.IsCreated
                    && CounterReadbackData.Length == FeedbackCounterElementCount)
                {
                    return;
                }

                if (ReadbackPending)
                {
                    AsyncGPUReadback.WaitAllRequests();
                    PollReadback();
                }

                DisposeReadbackData();
                RequestsReadbackData = new NativeArray<ulong>(
                    requestCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                CounterReadbackData = new NativeArray<uint>(
                    FeedbackCounterElementCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            public void PollReadback()
            {
                if (RequestReadbackPending && RequestsReadbackRequest.done)
                    CompleteRequestsReadback(RequestsReadbackRequest);

                if (CounterReadbackPending && CounterReadbackRequest.done)
                    CompleteCounterReadback(CounterReadbackRequest);
            }

            private void CompleteRequestsReadback(AsyncGPUReadbackRequest request)
            {
                RequestReadbackPending = false;
                CompletedRequestsValid = !request.hasError && RequestsReadbackData.IsCreated;
                if (!CompletedRequestsValid)
                {
                    CompletedCount = 0u;
                    CompletedFallbackSampleCount = 0;
                }

                CompleteReadbackIfReady();
            }

            private void CompleteCounterReadback(AsyncGPUReadbackRequest request)
            {
                CounterReadbackPending = false;
                if (!request.hasError && CounterReadbackData.IsCreated)
                {
                    CompletedCount = CounterReadbackData.Length > 0 ? CounterReadbackData[0] : 0u;
                    CompletedFallbackSampleCount = CounterReadbackData.Length > 1
                        ? SaturatingUIntToInt(CounterReadbackData[1])
                        : 0;
                }
                else
                {
                    CompletedCount = 0u;
                    CompletedFallbackSampleCount = 0;
                }

                CompleteReadbackIfReady();
            }

            private void DisposeReadbackData()
            {
                if (RequestsReadbackData.IsCreated)
                {
                    RequestsReadbackData.Dispose();
                    RequestsReadbackData = default;
                }

                if (CounterReadbackData.IsCreated)
                {
                    CounterReadbackData.Dispose();
                    CounterReadbackData = default;
                }
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
        internal const int StableReadbackIntervalFrames = 30;
        private readonly BufferPairState[] m_BufferPairs = { new(), new() };
        private readonly int m_SpaceId;
        private NativeArray<uint> m_ZeroCounterData;
        private int m_RequestCapacity;
        private int m_WriteBufferIndex;
        private bool m_HasCompletedReadbackResult;
        private bool m_LastCompletedReadbackWasEmpty;
        private int m_LastScheduledReadbackFrame = -1;
        private VirtualTextureFeedbackViewSignature m_LastCompletedReadbackSignature;
        private string m_ReadbackPendingStatusSpaceName;
        private string m_ReadbackPendingStatusMessage;
        private bool m_IsDisposed;

        internal VirtualTextureFeedbackBufferState(int spaceId)
        {
            m_SpaceId = spaceId;
            m_ZeroCounterData = new NativeArray<uint>(
                FeedbackCounterElementCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        internal bool TryPrepareForFrame(
            CommandBuffer cmd,
            string spaceName,
            Camera camera,
            VirtualTextureViewId viewId,
            VirtualTextureFeedbackViewSignature viewSignature,
            int feedbackCapacity,
            int frameIndex,
            bool forceImmediateReadback,
            out ComputeBuffer requestBuffer,
            out ComputeBuffer counterBuffer,
            out string statusMessage)
        {
            requestBuffer = null;
            counterBuffer = null;
            statusMessage = string.Empty;

            if (cmd == null || camera == null)
                return false;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsEnsureCapacityMarker.Auto())
                EnsureCapacity(spaceName, feedbackCapacity);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsPollMarker.Auto())
                PollReadbacks();

            int readBufferIndex = 1 - m_WriteBufferIndex;
            BufferPairState readPair = m_BufferPairs[readBufferIndex];
            if (readPair.WasWritten
                && !readPair.ReadbackPending
                && ShouldScheduleReadback(
                    forceImmediateReadback,
                    m_HasCompletedReadbackResult,
                    m_LastCompletedReadbackWasEmpty,
                    m_LastScheduledReadbackFrame,
                    frameIndex,
                    readPair.LastViewSignature,
                    m_LastCompletedReadbackSignature))
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsScheduleReadbackMarker.Auto())
                    ScheduleReadback(readPair, m_RequestCapacity);
                m_LastScheduledReadbackFrame = frameIndex;
            }

            BufferPairState writePair = m_BufferPairs[m_WriteBufferIndex];
            if (writePair.ReadbackPending)
            {
                statusMessage = GetReadbackPendingStatusMessage(spaceName);
                return false;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsResetCounterMarker.Auto())
                cmd.SetBufferData(writePair.CounterBuffer, m_ZeroCounterData);
            writePair.WasWritten = true;
            writePair.LastViewId = viewId;
            writePair.LastCameraType = camera.cameraType;
            writePair.LastViewSignature = viewSignature;
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

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackPollMarker.Auto())
                PollReadbacks();

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackReadbackCollectBatchesMarker.Auto())
            {
                for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
                {
                    BufferPairState pair = m_BufferPairs[bufferIndex];
                    if (!pair.HasCompletedReadback)
                        continue;

                    int completedRequestCount = SaturatingUIntToInt(pair.CompletedCount);
                    int requestCapacity = pair.CompletedRequestsValid && pair.RequestsReadbackData.IsCreated
                        ? pair.RequestsReadbackData.Length
                        : 0;
                    int requestCount = Mathf.Min(requestCapacity, completedRequestCount);
                    int overflowCount = Mathf.Max(0, completedRequestCount - requestCapacity);
                    int fallbackSampleCount = pair.CompletedFallbackSampleCount;
                    output.Add(new VirtualTextureFeedbackBatch(
                        pair.LastViewId,
                        pair.LastCameraType,
                        pair.RequestsReadbackData,
                        requestCount,
                        pair.ScheduledFrameIndex,
                        overflowCount,
                        fallbackSampleCount));
                    lastReadbackFrame = Mathf.Max(lastReadbackFrame, pair.ScheduledFrameIndex);
                    m_HasCompletedReadbackResult = true;
                    m_LastCompletedReadbackWasEmpty = requestCount == 0
                                                      && overflowCount == 0
                                                      && fallbackSampleCount == 0;
                    m_LastCompletedReadbackSignature = pair.LastViewSignature;
                    pair.HasCompletedReadback = false;
                    pair.CompletedRequestsValid = false;
                    pair.CompletedCount = 0u;
                    pair.CompletedFallbackSampleCount = 0;
                }
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
                m_BufferPairs[bufferIndex].Dispose();

            if (m_ZeroCounterData.IsCreated)
            {
                m_ZeroCounterData.Dispose();
                m_ZeroCounterData = default;
            }

            m_RequestCapacity = 0;
            m_HasCompletedReadbackResult = false;
            m_LastCompletedReadbackWasEmpty = false;
            m_LastScheduledReadbackFrame = -1;
            m_LastCompletedReadbackSignature = VirtualTextureFeedbackViewSignature.Invalid;
            m_ReadbackPendingStatusSpaceName = null;
            m_ReadbackPendingStatusMessage = null;
            m_IsDisposed = true;
        }

        private string GetReadbackPendingStatusMessage(string spaceName)
        {
            string resolvedSpaceName = spaceName ?? string.Empty;
            if (m_ReadbackPendingStatusMessage == null
                || !string.Equals(m_ReadbackPendingStatusSpaceName, resolvedSpaceName, StringComparison.Ordinal))
            {
                m_ReadbackPendingStatusSpaceName = resolvedSpaceName;
                m_ReadbackPendingStatusMessage =
                    $"[VividRP] VT feedback buffer is still pending readback for space '{resolvedSpaceName}'.";
            }

            return m_ReadbackPendingStatusMessage;
        }

        private void PollReadbacks()
        {
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                pair.PollReadback();
            }
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
            m_HasCompletedReadbackResult = false;
            m_LastCompletedReadbackWasEmpty = false;
            m_LastScheduledReadbackFrame = -1;
            m_LastCompletedReadbackSignature = VirtualTextureFeedbackViewSignature.Invalid;
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                pair.RequestsBuffer = new ComputeBuffer(feedbackCapacity, sizeof(ulong), ComputeBufferType.Structured);
                pair.RequestsBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackRequests_{bufferIndex}";
                pair.CounterBuffer = new ComputeBuffer(FeedbackCounterElementCount, sizeof(uint), ComputeBufferType.Structured);
                pair.CounterBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackCounter_{bufferIndex}";
                pair.EnsureReadbackCapacity(feedbackCapacity);
            }
        }

        internal static bool ShouldScheduleReadbackForTesting(
            bool forceImmediateReadback,
            bool hasCompletedReadbackResult,
            bool lastCompletedReadbackWasEmpty,
            int lastScheduledReadbackFrame,
            int frameIndex,
            VirtualTextureFeedbackViewSignature readbackSignature,
            VirtualTextureFeedbackViewSignature lastCompletedReadbackSignature)
        {
            return ShouldScheduleReadback(
                forceImmediateReadback,
                hasCompletedReadbackResult,
                lastCompletedReadbackWasEmpty,
                lastScheduledReadbackFrame,
                frameIndex,
                readbackSignature,
                lastCompletedReadbackSignature);
        }

        private static bool ShouldScheduleReadback(
            bool forceImmediateReadback,
            bool hasCompletedReadbackResult,
            bool lastCompletedReadbackWasEmpty,
            int lastScheduledReadbackFrame,
            int frameIndex,
            VirtualTextureFeedbackViewSignature readbackSignature,
            VirtualTextureFeedbackViewSignature lastCompletedReadbackSignature)
        {
            if (forceImmediateReadback)
                return true;

            if (!hasCompletedReadbackResult || !lastCompletedReadbackWasEmpty)
                return true;

            if (readbackSignature.IsValid && !readbackSignature.Equals(lastCompletedReadbackSignature))
                return true;

            if (lastScheduledReadbackFrame < 0)
                return true;

            return frameIndex - lastScheduledReadbackFrame >= StableReadbackIntervalFrames;
        }

        private static void ScheduleReadback(BufferPairState pair, int requestCapacity)
        {
            if (pair == null || pair.ReadbackPending || pair.RequestsBuffer == null || pair.CounterBuffer == null)
                return;

            pair.EnsureReadbackCapacity(requestCapacity);
            pair.ReadbackPending = true;
            pair.RequestReadbackPending = true;
            pair.CounterReadbackPending = true;
            pair.CompletedRequestsValid = false;

            pair.RequestsReadbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref pair.RequestsReadbackData,
                pair.RequestsBuffer,
                null);
            pair.CounterReadbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref pair.CounterReadbackData,
                pair.CounterBuffer,
                null);
        }

        private static int SaturatingUIntToInt(uint value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
