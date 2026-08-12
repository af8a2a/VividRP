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
            int fallbackSampleCount = 0,
            int residentAccessCount = 0,
            int faultOverflowCount = -1,
            int residentOverflowCount = -1,
            int residentFallbackSampleCount = 0,
            int weightedResolvedSampleCount = 0,
            bool requestsReadbackValid = true,
            bool counterReadbackValid = true,
            int acceptedFaultRequestCount = -1)
            : this(
                VirtualTextureViewId.FromCameraType(cameraType),
                cameraType,
                requests,
                requestCount,
                frameIndex,
                feedbackOverflowCount,
                fallbackSampleCount,
                residentAccessCount,
                faultOverflowCount,
                residentOverflowCount,
                residentFallbackSampleCount,
                weightedResolvedSampleCount,
                requestsReadbackValid,
                counterReadbackValid,
                acceptedFaultRequestCount)
        {
        }

        internal VirtualTextureFeedbackBatch(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            ulong[] requests,
            int requestCount,
            int frameIndex,
            int feedbackOverflowCount = 0,
            int fallbackSampleCount = 0,
            int residentAccessCount = 0,
            int faultOverflowCount = -1,
            int residentOverflowCount = -1,
            int residentFallbackSampleCount = 0,
            int weightedResolvedSampleCount = 0,
            bool requestsReadbackValid = true,
            bool counterReadbackValid = true,
            int acceptedFaultRequestCount = -1)
        {
            ViewId = viewId;
            CameraType = cameraType;
            ManagedRequests = requests ?? Array.Empty<ulong>();
            NativeRequests = default;
            RequestCount = Mathf.Clamp(requestCount, 0, ManagedRequests.Length);
            FrameIndex = frameIndex;
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
            ResidentAccessCount = Mathf.Clamp(residentAccessCount, 0, RequestCount);
            ResolveOverflowBreakdown(
                FeedbackOverflowCount,
                faultOverflowCount,
                residentOverflowCount,
                out int resolvedFaultOverflowCount,
                out int resolvedResidentOverflowCount);
            FaultOverflowCount = resolvedFaultOverflowCount;
            ResidentOverflowCount = resolvedResidentOverflowCount;
            ResidentFallbackSampleCount = Mathf.Clamp(
                residentFallbackSampleCount,
                0,
                FallbackSampleCount);
            WeightedResolvedSampleCount = Mathf.Max(0, weightedResolvedSampleCount);
            RequestsReadbackValid = requestsReadbackValid;
            CounterReadbackValid = counterReadbackValid;
            AcceptedFaultRequestCount = acceptedFaultRequestCount >= 0
                ? Mathf.Max(0, acceptedFaultRequestCount)
                : Mathf.Max(0, RequestCount - ResidentAccessCount);
        }

        internal VirtualTextureFeedbackBatch(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            NativeArray<ulong> requests,
            int requestCount,
            int frameIndex,
            int feedbackOverflowCount = 0,
            int fallbackSampleCount = 0,
            int residentAccessCount = 0,
            int faultOverflowCount = -1,
            int residentOverflowCount = -1,
            int residentFallbackSampleCount = 0,
            int weightedResolvedSampleCount = 0,
            bool requestsReadbackValid = true,
            bool counterReadbackValid = true,
            int acceptedFaultRequestCount = -1)
        {
            ViewId = viewId;
            CameraType = cameraType;
            ManagedRequests = null;
            NativeRequests = requests;
            RequestCount = Mathf.Clamp(requestCount, 0, requests.IsCreated ? requests.Length : 0);
            FrameIndex = frameIndex;
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
            ResidentAccessCount = Mathf.Clamp(residentAccessCount, 0, RequestCount);
            ResolveOverflowBreakdown(
                FeedbackOverflowCount,
                faultOverflowCount,
                residentOverflowCount,
                out int resolvedFaultOverflowCount,
                out int resolvedResidentOverflowCount);
            FaultOverflowCount = resolvedFaultOverflowCount;
            ResidentOverflowCount = resolvedResidentOverflowCount;
            ResidentFallbackSampleCount = Mathf.Clamp(
                residentFallbackSampleCount,
                0,
                FallbackSampleCount);
            WeightedResolvedSampleCount = Mathf.Max(0, weightedResolvedSampleCount);
            RequestsReadbackValid = requestsReadbackValid;
            CounterReadbackValid = counterReadbackValid;
            AcceptedFaultRequestCount = acceptedFaultRequestCount >= 0
                ? Mathf.Max(0, acceptedFaultRequestCount)
                : Mathf.Max(0, RequestCount - ResidentAccessCount);
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

        internal int ResidentAccessCount { get; }

        internal int AcceptedResidentRequestCount => ResidentAccessCount;

        internal int AcceptedFaultRequestCount { get; }

        internal int FaultOverflowCount { get; }

        internal int ResidentOverflowCount { get; }

        internal int ResidentFallbackSampleCount { get; }

        internal int WeightedResolvedSampleCount { get; }

        internal int NonResidentFallbackSampleCount =>
            Mathf.Max(0, FallbackSampleCount - ResidentFallbackSampleCount);

        internal bool RequestsReadbackValid { get; }

        internal bool CounterReadbackValid { get; }

        private static void ResolveOverflowBreakdown(
            int feedbackOverflowCount,
            int faultOverflowCount,
            int residentOverflowCount,
            out int resolvedFaultOverflowCount,
            out int resolvedResidentOverflowCount)
        {
            if (faultOverflowCount < 0 && residentOverflowCount < 0)
            {
                resolvedFaultOverflowCount = feedbackOverflowCount;
                resolvedResidentOverflowCount = 0;
                return;
            }

            if (faultOverflowCount < 0)
            {
                resolvedResidentOverflowCount = Mathf.Clamp(
                    residentOverflowCount,
                    0,
                    feedbackOverflowCount);
                resolvedFaultOverflowCount = feedbackOverflowCount - resolvedResidentOverflowCount;
                return;
            }

            if (residentOverflowCount < 0)
            {
                resolvedFaultOverflowCount = Mathf.Clamp(
                    faultOverflowCount,
                    0,
                    feedbackOverflowCount);
                resolvedResidentOverflowCount = feedbackOverflowCount - resolvedFaultOverflowCount;
                return;
            }

            resolvedFaultOverflowCount = Mathf.Clamp(
                faultOverflowCount,
                0,
                feedbackOverflowCount);
            resolvedResidentOverflowCount = Mathf.Clamp(
                residentOverflowCount,
                0,
                feedbackOverflowCount - resolvedFaultOverflowCount);
        }

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
            int pixelHeight,
            float adaptiveMipBias = 0f)
        {
            ViewMatrix = viewMatrix;
            ProjectionMatrix = projectionMatrix;
            ActualWidth = actualWidth;
            ActualHeight = actualHeight;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            AdaptiveMipBias = Mathf.Max(0f, adaptiveMipBias);
            IsValid = true;
        }

        internal Matrix4x4 ViewMatrix { get; }

        internal Matrix4x4 ProjectionMatrix { get; }

        internal int ActualWidth { get; }

        internal int ActualHeight { get; }

        internal int PixelWidth { get; }

        internal int PixelHeight { get; }

        internal float AdaptiveMipBias { get; }

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

        internal VirtualTextureFeedbackViewSignature WithAdaptiveMipBias(float adaptiveMipBias)
        {
            if (!IsValid)
                return Invalid;

            return new VirtualTextureFeedbackViewSignature(
                ViewMatrix,
                ProjectionMatrix,
                ActualWidth,
                ActualHeight,
                PixelWidth,
                PixelHeight,
                adaptiveMipBias);
        }

        public bool Equals(VirtualTextureFeedbackViewSignature other)
        {
            return IsValid == other.IsValid
                   && ViewMatrix.Equals(other.ViewMatrix)
                   && ProjectionMatrix.Equals(other.ProjectionMatrix)
                   && ActualWidth == other.ActualWidth
                   && ActualHeight == other.ActualHeight
                   && PixelWidth == other.PixelWidth
                   && PixelHeight == other.PixelHeight
                   && AdaptiveMipBias.Equals(other.AdaptiveMipBias);
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
                AdaptiveMipBias,
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
        private struct ResidentFeedbackHashEntry
        {
            public uint KeyLow;
            public uint KeyHigh;
            public uint State;
            public uint Padding;
        }

        private sealed class BufferPairState : IDisposable
        {
            public ComputeBuffer RequestsBuffer;
            public ComputeBuffer CounterBuffer;
            public ComputeBuffer ResidentHashBuffer;
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
            public bool CompletedCounterValid;
            public uint CompletedCount;
            public int CompletedFallbackSampleCount;
            public int CompletedResidentAccessCount;
            public int CompletedFaultOverflowCount;
            public int CompletedResidentOverflowCount;
            public int CompletedResidentFallbackSampleCount;
            public int CompletedWeightedResolvedSampleCount;
            public int FeedbackSampleArea = 1;

            public void Dispose()
            {
                if (ReadbackPending)
                    AsyncGPUReadback.WaitAllRequests();

                RequestsBuffer?.Dispose();
                CounterBuffer?.Dispose();
                ResidentHashBuffer?.Dispose();
                DisposeReadbackData();
                RequestsBuffer = null;
                CounterBuffer = null;
                ResidentHashBuffer = null;
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
                CompletedCounterValid = false;
                CompletedCount = 0u;
                CompletedFallbackSampleCount = 0;
                CompletedResidentAccessCount = 0;
                CompletedFaultOverflowCount = 0;
                CompletedResidentOverflowCount = 0;
                CompletedResidentFallbackSampleCount = 0;
                CompletedWeightedResolvedSampleCount = 0;
                FeedbackSampleArea = 1;
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

                CompleteReadbackIfReady();
            }

            private void CompleteCounterReadback(AsyncGPUReadbackRequest request)
            {
                CounterReadbackPending = false;
                CompletedCounterValid = !request.hasError && CounterReadbackData.IsCreated;
                if (CompletedCounterValid)
                {
                    CompletedCount = CounterReadbackData.Length > 0 ? CounterReadbackData[0] : 0u;
                    CompletedFallbackSampleCount = CounterReadbackData.Length > 1
                        ? SaturatingUIntToInt(CounterReadbackData[1])
                        : 0;
                    CompletedResidentAccessCount = CounterReadbackData.Length > 2
                        ? SaturatingUIntToInt(CounterReadbackData[2])
                        : 0;
                    CompletedFaultOverflowCount = CounterReadbackData.Length > 3
                        ? SaturatingUIntToInt(CounterReadbackData[3])
                        : 0;
                    CompletedResidentOverflowCount = CounterReadbackData.Length > 4
                        ? SaturatingUIntToInt(CounterReadbackData[4])
                        : 0;
                    CompletedResidentFallbackSampleCount = CounterReadbackData.Length > 5
                        ? SaturatingUIntToInt(CounterReadbackData[5])
                        : 0;
                    CompletedWeightedResolvedSampleCount = CounterReadbackData.Length > 6
                        ? SaturatingUIntToInt(CounterReadbackData[6])
                        : 0;
                }
                else
                {
                    CompletedCount = 0u;
                    CompletedFallbackSampleCount = 0;
                    CompletedResidentAccessCount = 0;
                    CompletedFaultOverflowCount = 0;
                    CompletedResidentOverflowCount = 0;
                    CompletedResidentFallbackSampleCount = 0;
                    CompletedWeightedResolvedSampleCount = 0;
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

        // Must match the VT_FEEDBACK_*_COUNTER_INDEX layout in VirtualTexture.hlsl.
        private const int FeedbackCounterElementCount = 7;
        private const int FeedbackBufferCount = 8;
        private const int MaxTrackedFeedbackSampleArea = sizeof(ulong) * 8;
        internal const int StableReadbackIntervalFrames = 30;
        private readonly BufferPairState[] m_BufferPairs =
        {
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
        };
        private readonly int m_SpaceId;
        private NativeArray<uint> m_ZeroCounterData;
        private NativeArray<ResidentFeedbackHashEntry> m_ZeroResidentHashData;
        private int m_RequestCapacity;
        private int m_ResidentHashCapacity;
        private int m_WriteBufferIndex;
        private int m_LastWrittenBufferIndex = -1;
        private bool m_HasCompletedReadbackResult;
        private bool m_LastCompletedReadbackWasEmpty;
        private ulong m_EmptyReadbackPhaseMask;
        private int m_EmptyReadbackSampleArea;
        private int m_QuiescenceInvalidatedFrame = -1;
        private int m_LastScheduledReadbackFrame = -1;
        private VirtualTextureFeedbackViewSignature m_LastCompletedReadbackSignature;
        private string m_ReadbackPendingStatusSpaceName;
        private string m_ReadbackPendingStatusMessage;
        private bool m_IsDisposed;
#if VT_DEBUG
        private bool m_ReadbackStallActive;
        private int m_ReadbackStallStartFrame = -1;
#endif

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
            int cachePageCount,
            int frameIndex,
            bool forceImmediateReadback,
            out ComputeBuffer requestBuffer,
            out ComputeBuffer counterBuffer,
            out ComputeBuffer residentHashBuffer,
            out int residentHashCapacity,
            out string statusMessage)
        {
            requestBuffer = null;
            counterBuffer = null;
            residentHashBuffer = null;
            residentHashCapacity = 0;
            statusMessage = string.Empty;

            if (cmd == null || camera == null)
                return false;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsEnsureCapacityMarker.Auto())
                EnsureCapacity(spaceName, feedbackCapacity, cachePageCount);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsPollMarker.Auto())
                PollReadbacks();

            // Upload completion and page-table publication can make an empty readback stale in
            // the same frame. Only feedback captured after the last active frame may contribute
            // to quiescence coverage.
            if (forceImmediateReadback)
                InvalidateQuiescence(frameIndex, "activity");

            if (m_LastWrittenBufferIndex >= 0)
            {
                BufferPairState readPair = m_BufferPairs[m_LastWrittenBufferIndex];
                if (readPair.WasWritten
                    && !readPair.ReadbackPending
                    && !readPair.HasCompletedReadback)
                {
                    bool shouldScheduleReadback = ShouldScheduleReadback(
                        forceImmediateReadback,
                        m_HasCompletedReadbackResult,
                        m_LastCompletedReadbackWasEmpty,
                        HasCompleteQuiescenceCoverage(),
                        m_LastScheduledReadbackFrame,
                        frameIndex,
                        readPair.LastViewSignature,
                        m_LastCompletedReadbackSignature);
                    if (shouldScheduleReadback)
                    {
                        using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsScheduleReadbackMarker.Auto())
                            ScheduleReadback(readPair, m_RequestCapacity);
                        m_LastScheduledReadbackFrame = frameIndex;
                    }
                    else
                    {
                        // The stable-view heartbeat deliberately discards this sample. Mark it
                        // reusable instead of pinning a ring entry that will never be read back.
                        readPair.WasWritten = false;
                    }
                }
            }

            int writeBufferIndex = FindWritableBufferIndex();
            if (writeBufferIndex < 0)
            {
#if VT_DEBUG
                BeginReadbackStall(frameIndex);
#endif
                statusMessage = GetReadbackPendingStatusMessage(spaceName);
                return false;
            }
#if VT_DEBUG
            EndReadbackStall(frameIndex);
#endif

            BufferPairState writePair = m_BufferPairs[writeBufferIndex];
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureFeedbackPrepareTargetsResetCounterMarker.Auto())
                cmd.SetBufferData(writePair.CounterBuffer, m_ZeroCounterData);
            writePair.WasWritten = true;
            writePair.LastViewId = viewId;
            writePair.LastCameraType = camera.cameraType;
            writePair.LastViewSignature = viewSignature;
            writePair.ScheduledFrameIndex = frameIndex;
            writePair.FeedbackSampleArea = 1;

            requestBuffer = writePair.RequestsBuffer;
            counterBuffer = writePair.CounterBuffer;
            residentHashBuffer = writePair.ResidentHashBuffer;
            residentHashCapacity = m_ResidentHashCapacity;
            m_LastWrittenBufferIndex = writeBufferIndex;
            m_WriteBufferIndex = (writeBufferIndex + 1) % FeedbackBufferCount;
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
                    int requestCapacity = pair.RequestsReadbackData.IsCreated
                        ? pair.RequestsReadbackData.Length
                        : m_RequestCapacity;
                    int requestCount = ResolveCompletedRequestCount(
                        pair.CompletedRequestsValid,
                        pair.CompletedCounterValid,
                        requestCapacity,
                        completedRequestCount);
                    int overflowCount = ResolveCompletedOverflowCount(
                        pair.CompletedCounterValid,
                        requestCapacity,
                        completedRequestCount);
                    int fallbackSampleCount = pair.CompletedCounterValid
                        ? pair.CompletedFallbackSampleCount
                        : 0;
                    int residentAccessCount = pair.CompletedRequestsValid && pair.CompletedCounterValid
                        ? Mathf.Min(requestCount, pair.CompletedResidentAccessCount)
                        : 0;
                    int acceptedFaultRequestCount = ResolveCompletedAcceptedFaultRequestCount(
                        pair.CompletedCounterValid,
                        requestCapacity,
                        completedRequestCount,
                        pair.CompletedResidentAccessCount);
                    int faultOverflowCount = pair.CompletedCounterValid
                        ? pair.CompletedFaultOverflowCount
                        : 0;
                    int residentOverflowCount = pair.CompletedCounterValid
                        ? pair.CompletedResidentOverflowCount
                        : 0;
                    int residentFallbackSampleCount = pair.CompletedCounterValid
                        ? pair.CompletedResidentFallbackSampleCount
                        : 0;
                    int weightedResolvedSampleCount = pair.CompletedCounterValid
                        ? pair.CompletedWeightedResolvedSampleCount
                        : 0;
                    output.Add(new VirtualTextureFeedbackBatch(
                        pair.LastViewId,
                        pair.LastCameraType,
                        pair.RequestsReadbackData,
                        requestCount,
                        pair.ScheduledFrameIndex,
                        overflowCount,
                        fallbackSampleCount,
                        residentAccessCount,
                        faultOverflowCount,
                        residentOverflowCount,
                        residentFallbackSampleCount,
                        weightedResolvedSampleCount,
                        pair.CompletedRequestsValid,
                        pair.CompletedCounterValid,
                        acceptedFaultRequestCount));
                    lastReadbackFrame = Mathf.Max(lastReadbackFrame, pair.ScheduledFrameIndex);
                    bool completedReadbackValid = pair.CompletedRequestsValid
                                                  && pair.CompletedCounterValid;
                    if (completedReadbackValid)
                    {
                        m_HasCompletedReadbackResult = true;
                        bool completedReadbackWasEmpty = requestCount == 0
                                                         && overflowCount == 0
                                                         && fallbackSampleCount == 0;
                        bool sameViewAsPrevious = m_LastCompletedReadbackSignature.IsValid
                                                  && pair.LastViewSignature.Equals(
                                                      m_LastCompletedReadbackSignature);
                        UpdateQuiescenceCoverage(
                            completedReadbackWasEmpty,
                            sameViewAsPrevious,
                            pair.FeedbackSampleArea,
                            pair.ScheduledFrameIndex);
                        m_LastCompletedReadbackWasEmpty = completedReadbackWasEmpty;
                        m_LastCompletedReadbackSignature = pair.LastViewSignature;
                    }
                    else
                    {
                        m_LastCompletedReadbackWasEmpty = false;
                        m_EmptyReadbackPhaseMask = 0ul;
                    }
                    pair.HasCompletedReadback = false;
                    pair.CompletedRequestsValid = false;
                    pair.CompletedCounterValid = false;
                    pair.CompletedCount = 0u;
                    pair.CompletedFallbackSampleCount = 0;
                    pair.CompletedResidentAccessCount = 0;
                    pair.CompletedFaultOverflowCount = 0;
                    pair.CompletedResidentOverflowCount = 0;
                    pair.CompletedResidentFallbackSampleCount = 0;
                    pair.CompletedWeightedResolvedSampleCount = 0;
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

            if (m_ZeroResidentHashData.IsCreated)
            {
                m_ZeroResidentHashData.Dispose();
                m_ZeroResidentHashData = default;
            }

            m_RequestCapacity = 0;
            m_ResidentHashCapacity = 0;
            m_WriteBufferIndex = 0;
            m_LastWrittenBufferIndex = -1;
            m_HasCompletedReadbackResult = false;
            m_LastCompletedReadbackWasEmpty = false;
            ResetQuiescenceCoverage();
            m_QuiescenceInvalidatedFrame = -1;
            m_LastScheduledReadbackFrame = -1;
            m_LastCompletedReadbackSignature = VirtualTextureFeedbackViewSignature.Invalid;
            m_ReadbackPendingStatusSpaceName = null;
            m_ReadbackPendingStatusMessage = null;
#if VT_DEBUG
            m_ReadbackStallActive = false;
            m_ReadbackStallStartFrame = -1;
#endif
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

        private void EnsureCapacity(string spaceName, int feedbackCapacity, int cachePageCount)
        {
            int residentHashCapacity = ResolveResidentHashCapacity(cachePageCount);
            EnsureZeroCounterDataCapacity();
            if (m_RequestCapacity == feedbackCapacity
                && m_ResidentHashCapacity == residentHashCapacity
                && HasAllocatedBuffers())
            {
                return;
            }

            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
                m_BufferPairs[bufferIndex].Dispose();

            m_RequestCapacity = feedbackCapacity;
            m_ResidentHashCapacity = residentHashCapacity;
            EnsureZeroResidentHashStateCapacity(residentHashCapacity);
            m_WriteBufferIndex = 0;
            m_LastWrittenBufferIndex = -1;
            m_HasCompletedReadbackResult = false;
            m_LastCompletedReadbackWasEmpty = false;
            ResetQuiescenceCoverage();
            m_QuiescenceInvalidatedFrame = -1;
            m_LastScheduledReadbackFrame = -1;
            m_LastCompletedReadbackSignature = VirtualTextureFeedbackViewSignature.Invalid;
#if VT_DEBUG
            m_ReadbackStallActive = false;
            m_ReadbackStallStartFrame = -1;
#endif
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                pair.RequestsBuffer = new ComputeBuffer(feedbackCapacity, sizeof(ulong), ComputeBufferType.Structured);
                pair.RequestsBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackRequests_{bufferIndex}";
                pair.CounterBuffer = new ComputeBuffer(FeedbackCounterElementCount, sizeof(uint), ComputeBufferType.Structured);
                pair.CounterBuffer.name = $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackCounter_{bufferIndex}";
                pair.ResidentHashBuffer = new ComputeBuffer(
                    residentHashCapacity,
                    sizeof(uint) * 4,
                    ComputeBufferType.Structured);
                pair.ResidentHashBuffer.name =
                    $"VividVT_{spaceName}_Space{m_SpaceId}_FeedbackResidentHash_{bufferIndex}";
                pair.ResidentHashBuffer.SetData(m_ZeroResidentHashData);
                pair.EnsureReadbackCapacity(feedbackCapacity);
            }
        }

        private void EnsureZeroResidentHashStateCapacity(int residentHashCapacity)
        {
            if (m_ZeroResidentHashData.IsCreated
                && m_ZeroResidentHashData.Length == residentHashCapacity)
            {
                return;
            }

            if (m_ZeroResidentHashData.IsCreated)
                m_ZeroResidentHashData.Dispose();

            m_ZeroResidentHashData = new NativeArray<ResidentFeedbackHashEntry>(
                residentHashCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        private void EnsureZeroCounterDataCapacity()
        {
            if (m_ZeroCounterData.IsCreated
                && m_ZeroCounterData.Length == FeedbackCounterElementCount)
            {
                return;
            }

            if (m_ZeroCounterData.IsCreated)
                m_ZeroCounterData.Dispose();

            m_ZeroCounterData = new NativeArray<uint>(
                FeedbackCounterElementCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        internal static int ResolveResidentHashCapacityForTesting(int cachePageCount)
        {
            return ResolveResidentHashCapacity(cachePageCount);
        }

        internal static int ResolveCompletedRequestCount(
            bool requestsReadbackValid,
            bool counterReadbackValid,
            int requestCapacity,
            int completedRequestCount)
        {
            return requestsReadbackValid && counterReadbackValid
                ? Mathf.Min(Mathf.Max(0, requestCapacity), Mathf.Max(0, completedRequestCount))
                : 0;
        }

        internal static int ResolveCompletedOverflowCount(
            bool counterReadbackValid,
            int requestCapacity,
            int completedRequestCount)
        {
            return counterReadbackValid
                ? Mathf.Max(0, completedRequestCount - Mathf.Max(0, requestCapacity))
                : 0;
        }

        internal static int ResolveCompletedAcceptedFaultRequestCount(
            bool counterReadbackValid,
            int requestCapacity,
            int completedRequestCount,
            int completedResidentAccessCount)
        {
            if (!counterReadbackValid)
                return 0;

            int acceptedRequestCount = Mathf.Min(
                Mathf.Max(0, requestCapacity),
                Mathf.Max(0, completedRequestCount));
            int acceptedResidentRequestCount = Mathf.Min(
                acceptedRequestCount,
                Mathf.Max(0, completedResidentAccessCount));
            return acceptedRequestCount - acceptedResidentRequestCount;
        }

        private static int ResolveResidentHashCapacity(int cachePageCount)
        {
            int residentPageCapacity = Mathf.Max(cachePageCount, 1);
            int targetCapacity = checked(residentPageCapacity * 2);
            return Mathf.NextPowerOfTwo(Mathf.Max(targetCapacity, 16));
        }

        internal static bool ShouldScheduleReadbackForTesting(
            bool forceImmediateReadback,
            bool hasCompletedReadbackResult,
            bool lastCompletedReadbackWasEmpty,
            bool hasCompleteQuiescenceCoverage,
            int lastScheduledReadbackFrame,
            int frameIndex,
            VirtualTextureFeedbackViewSignature readbackSignature,
            VirtualTextureFeedbackViewSignature lastCompletedReadbackSignature)
        {
            return ShouldScheduleReadback(
                forceImmediateReadback,
                hasCompletedReadbackResult,
                lastCompletedReadbackWasEmpty,
                hasCompleteQuiescenceCoverage,
                lastScheduledReadbackFrame,
                frameIndex,
                readbackSignature,
                lastCompletedReadbackSignature);
        }

        private static bool ShouldScheduleReadback(
            bool forceImmediateReadback,
            bool hasCompletedReadbackResult,
            bool lastCompletedReadbackWasEmpty,
            bool hasCompleteQuiescenceCoverage,
            int lastScheduledReadbackFrame,
            int frameIndex,
            VirtualTextureFeedbackViewSignature readbackSignature,
            VirtualTextureFeedbackViewSignature lastCompletedReadbackSignature)
        {
            if (forceImmediateReadback)
                return true;

            if (!hasCompletedReadbackResult
                || !lastCompletedReadbackWasEmpty
                || !hasCompleteQuiescenceCoverage)
            {
                return true;
            }

            if (readbackSignature.IsValid && !readbackSignature.Equals(lastCompletedReadbackSignature))
                return true;

            if (lastScheduledReadbackFrame < 0)
                return true;

            return frameIndex - lastScheduledReadbackFrame >= StableReadbackIntervalFrames;
        }

        internal void RegisterFeedbackSampling(int frameIndex, int feedbackSampleRate)
        {
            int sampleArea = VirtualTextureFeedbackBindingUtility.ResolveFeedbackSampleArea(
                feedbackSampleRate);
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                if (!pair.WasWritten || pair.ScheduledFrameIndex != frameIndex)
                    continue;

                // Multiple consumers may write the same feedback target. Tracking the largest
                // cycle is conservative and prevents a sparse consumer from being hidden by a
                // denser one that happens to report an empty frame.
                pair.FeedbackSampleArea = Mathf.Max(pair.FeedbackSampleArea, sampleArea);
            }
        }

        private bool HasAllocatedBuffers()
        {
            for (int bufferIndex = 0; bufferIndex < m_BufferPairs.Length; bufferIndex++)
            {
                BufferPairState pair = m_BufferPairs[bufferIndex];
                if (pair.RequestsBuffer == null
                    || pair.CounterBuffer == null
                    || pair.CounterBuffer.count != FeedbackCounterElementCount
                    || pair.ResidentHashBuffer == null)
                {
                    return false;
                }
            }

            return true;
        }

        internal static ulong AccumulateEmptyFeedbackPhaseForTesting(
            ulong phaseMask,
            int feedbackSampleArea,
            int frameIndex)
        {
            return AccumulateEmptyFeedbackPhase(phaseMask, feedbackSampleArea, frameIndex);
        }

        internal static bool HasCompleteFeedbackPhaseCoverageForTesting(
            ulong phaseMask,
            int feedbackSampleArea)
        {
            return HasCompleteFeedbackPhaseCoverage(phaseMask, feedbackSampleArea);
        }

        private void UpdateQuiescenceCoverage(
            bool completedReadbackWasEmpty,
            bool sameViewAsPrevious,
            int feedbackSampleArea,
            int scheduledFrameIndex)
        {
            if (!completedReadbackWasEmpty)
            {
                InvalidateQuiescence(scheduledFrameIndex, "non-empty-feedback");
                return;
            }

            if (scheduledFrameIndex <= m_QuiescenceInvalidatedFrame)
                return;

            int sampleArea = Mathf.Max(1, feedbackSampleArea);
            if (!sameViewAsPrevious || sampleArea != m_EmptyReadbackSampleArea)
            {
                ResetQuiescenceCoverage();
                m_EmptyReadbackSampleArea = sampleArea;
            }

            bool wasComplete = HasCompleteQuiescenceCoverage();
            m_EmptyReadbackPhaseMask = AccumulateEmptyFeedbackPhase(
                m_EmptyReadbackPhaseMask,
                sampleArea,
                scheduledFrameIndex);
#if VT_DEBUG
            if (!wasComplete && HasCompleteQuiescenceCoverage())
            {
                VTDebugLog.Trace(
                    $"[VividRP][VT_DEBUG][FeedbackQuiescenceEnter] space={m_SpaceId} "
                    + $"feedbackFrame={scheduledFrameIndex} sampleArea={sampleArea} "
                    + $"phaseMask=0x{m_EmptyReadbackPhaseMask:X16}");
            }
#endif
        }

        private void InvalidateQuiescence(int frameIndex, string reason)
        {
            bool wasComplete = HasCompleteQuiescenceCoverage();
            m_QuiescenceInvalidatedFrame = Mathf.Max(m_QuiescenceInvalidatedFrame, frameIndex);
            ResetQuiescenceCoverage();
#if VT_DEBUG
            if (wasComplete)
            {
                VTDebugLog.Trace(
                    $"[VividRP][VT_DEBUG][FeedbackQuiescenceExit] space={m_SpaceId} "
                    + $"frame={frameIndex} reason={reason}");
            }
#endif
        }

        private void ResetQuiescenceCoverage()
        {
            m_EmptyReadbackPhaseMask = 0ul;
            m_EmptyReadbackSampleArea = 0;
        }

        private bool HasCompleteQuiescenceCoverage()
        {
            return HasCompleteFeedbackPhaseCoverage(
                m_EmptyReadbackPhaseMask,
                m_EmptyReadbackSampleArea);
        }

        private static ulong AccumulateEmptyFeedbackPhase(
            ulong phaseMask,
            int feedbackSampleArea,
            int frameIndex)
        {
            if (feedbackSampleArea <= 0 || feedbackSampleArea > MaxTrackedFeedbackSampleArea)
                return 0ul;

            int phase = PositiveModulo(frameIndex, feedbackSampleArea);
            return phaseMask | (1ul << phase);
        }

        private static bool HasCompleteFeedbackPhaseCoverage(
            ulong phaseMask,
            int feedbackSampleArea)
        {
            if (feedbackSampleArea <= 0 || feedbackSampleArea > MaxTrackedFeedbackSampleArea)
                return false;

            ulong completeMask = feedbackSampleArea == MaxTrackedFeedbackSampleArea
                ? ulong.MaxValue
                : (1ul << feedbackSampleArea) - 1ul;
            return (phaseMask & completeMask) == completeMask;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static void ScheduleReadback(BufferPairState pair, int requestCapacity)
        {
            if (pair == null
                || pair.ReadbackPending
                || pair.HasCompletedReadback
                || !pair.WasWritten
                || pair.RequestsBuffer == null
                || pair.CounterBuffer == null)
            {
                return;
            }

            pair.EnsureReadbackCapacity(requestCapacity);
            pair.WasWritten = false;
            pair.ReadbackPending = true;
            pair.RequestReadbackPending = true;
            pair.CounterReadbackPending = true;
            pair.CompletedRequestsValid = false;
            pair.CompletedCounterValid = false;

            pair.RequestsReadbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref pair.RequestsReadbackData,
                pair.RequestsBuffer,
                null);
            pair.CounterReadbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref pair.CounterReadbackData,
                pair.CounterBuffer,
                null);
        }

        private int FindWritableBufferIndex()
        {
            for (int offset = 0; offset < FeedbackBufferCount; offset++)
            {
                int bufferIndex = (m_WriteBufferIndex + offset) % FeedbackBufferCount;
                BufferPairState pair = m_BufferPairs[bufferIndex];
                if (pair.ReadbackPending || pair.HasCompletedReadback || pair.WasWritten)
                    continue;

                return bufferIndex;
            }

            return -1;
        }

#if VT_DEBUG
        private void BeginReadbackStall(int frameIndex)
        {
            if (m_ReadbackStallActive)
                return;

            m_ReadbackStallActive = true;
            m_ReadbackStallStartFrame = frameIndex;
            VTDebugLog.Trace(
                $"[VividRP][VT_DEBUG][FeedbackReadbackStallBegin] space={m_SpaceId} "
                + $"frame={frameIndex} buffers={FeedbackBufferCount}");
        }

        private void EndReadbackStall(int frameIndex)
        {
            if (!m_ReadbackStallActive)
                return;

            int duration = Mathf.Max(0, frameIndex - m_ReadbackStallStartFrame);
            VTDebugLog.Trace(
                $"[VividRP][VT_DEBUG][FeedbackReadbackStallEnd] space={m_SpaceId} "
                + $"frame={frameIndex} durationFrames={duration} buffers={FeedbackBufferCount}");
            m_ReadbackStallActive = false;
            m_ReadbackStallStartFrame = -1;
        }
#endif

        private static int SaturatingUIntToInt(uint value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
