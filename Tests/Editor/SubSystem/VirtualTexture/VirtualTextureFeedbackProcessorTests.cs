using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureFeedbackProcessorTests
    {
        [Test]
        public void EncodeKey_RoundTripsSpaceAndPageCoordinates()
        {
            ulong encoded = VirtualTextureFeedbackProcessor.EncodeKey(
                23,
                new VirtualTexturePageCoord(513, 777, 5));

            VirtualTextureFeedbackProcessor.DecodeKey(
                encoded,
                out int spaceId,
                out VirtualTexturePageCoord pageCoord);

            Assert.That(spaceId, Is.EqualTo(23));
            Assert.That(pageCoord, Is.EqualTo(new VirtualTexturePageCoord(513, 777, 5)));
        }

        [Test]
        public void Aggregate_DeduplicatesRequestsAcrossCameras_AndSortsByPriority()
        {
            ulong mip0High = VirtualTextureFeedbackProcessor.EncodeKey(2, new VirtualTexturePageCoord(1, 0, 0));
            ulong mip0Low = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 0));
            ulong mip1Request = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 1));

            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.SceneView, new[] { mip1Request, mip0Low }, 2, 7),
                new(CameraType.Game, new[] { mip0High, mip0High, mip1Request }, 3, 7),
            };

            List<VirtualTextureAggregatedFeedbackRequest> aggregated = VirtualTextureFeedbackProcessor.Aggregate(batches);

            Assert.That(aggregated.Count, Is.EqualTo(3));

            Assert.That(aggregated[0].SpaceId, Is.EqualTo(1));
            Assert.That(aggregated[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 1)));
            Assert.That(aggregated[0].HitCount, Is.EqualTo(2));
            Assert.That(aggregated[0].CameraPriority, Is.EqualTo(0));

            Assert.That(aggregated[1].SpaceId, Is.EqualTo(2));
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
            Assert.That(aggregated[1].HitCount, Is.EqualTo(2));
            Assert.That(aggregated[1].CameraPriority, Is.EqualTo(0));

            Assert.That(aggregated[2].SpaceId, Is.EqualTo(1));
            Assert.That(aggregated[2].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            Assert.That(aggregated[2].HitCount, Is.EqualTo(1));
            Assert.That(aggregated[2].CameraPriority, Is.EqualTo(1));
        }

        [Test]
        public void FeedbackBatch_PreservesOverflowAndFallbackBreakdown()
        {
            var batch = new VirtualTextureFeedbackBatch(
                CameraType.Game,
                new ulong[4],
                requestCount: 4,
                frameIndex: 7,
                feedbackOverflowCount: 6,
                fallbackSampleCount: 10,
                residentAccessCount: 2,
                faultOverflowCount: 2,
                residentOverflowCount: 4,
                residentFallbackSampleCount: 7,
                weightedResolvedSampleCount: 20);

            Assert.That(batch.FaultOverflowCount, Is.EqualTo(2));
            Assert.That(batch.ResidentOverflowCount, Is.EqualTo(4));
            Assert.That(
                batch.FaultOverflowCount + batch.ResidentOverflowCount,
                Is.EqualTo(batch.FeedbackOverflowCount));
            Assert.That(batch.ResidentFallbackSampleCount, Is.EqualTo(7));
            Assert.That(batch.NonResidentFallbackSampleCount, Is.EqualTo(3));
            Assert.That(batch.WeightedResolvedSampleCount, Is.EqualTo(20));
            Assert.That(batch.RequestsReadbackValid, Is.True);
            Assert.That(batch.CounterReadbackValid, Is.True);
        }

        [Test]
        public void FeedbackBatch_DerivesUnspecifiedOverflowSideFromTotal()
        {
            var faultSpecified = new VirtualTextureFeedbackBatch(
                CameraType.Game,
                System.Array.Empty<ulong>(),
                requestCount: 0,
                frameIndex: 1,
                feedbackOverflowCount: 6,
                faultOverflowCount: 2);
            var residentSpecified = new VirtualTextureFeedbackBatch(
                CameraType.Game,
                System.Array.Empty<ulong>(),
                requestCount: 0,
                frameIndex: 1,
                feedbackOverflowCount: 6,
                residentOverflowCount: 4);

            Assert.That(faultSpecified.FaultOverflowCount, Is.EqualTo(2));
            Assert.That(faultSpecified.ResidentOverflowCount, Is.EqualTo(4));
            Assert.That(residentSpecified.FaultOverflowCount, Is.EqualTo(2));
            Assert.That(residentSpecified.ResidentOverflowCount, Is.EqualTo(4));
        }

        [Test]
        public void FeedbackBatch_PreservesCounterDerivedAcceptedFaultRequestCount_WhenRequestsAreInvalid()
        {
            var batch = new VirtualTextureFeedbackBatch(
                CameraType.Game,
                System.Array.Empty<ulong>(),
                requestCount: 0,
                frameIndex: 1,
                faultOverflowCount: 3,
                requestsReadbackValid: false,
                counterReadbackValid: true,
                acceptedFaultRequestCount: 12);

            Assert.That(batch.RequestCount, Is.Zero);
            Assert.That(batch.ResidentAccessCount, Is.Zero);
            Assert.That(batch.AcceptedFaultRequestCount, Is.EqualTo(12));
        }

        [TestCase(false, 0, 0)]
        [TestCase(true, 2, 2)]
        [TestCase(true, 7, 7)]
        public void CompletedReadbackCounts_DeriveAcceptedFaultsFromCounters(
            bool counterReadbackValid,
            int completedAcceptedFaultRequestCount,
            int expectedAcceptedFaultRequestCount)
        {
            Assert.That(
                VirtualTextureFeedbackBufferState.ResolveCompletedAcceptedFaultRequestCount(
                    counterReadbackValid,
                    completedAcceptedFaultRequestCount),
                Is.EqualTo(expectedAcceptedFaultRequestCount));
        }

        [TestCase(-1, 0, false)]
        [TestCase(4, 5, false)]
        [TestCase(5, 5, true)]
        public void FeedbackHashClear_IsRequiredOnlyWhenPairReusesSameFrameEpoch(
            int previousFrameIndex,
            int frameIndex,
            bool expected)
        {
            Assert.That(
                VirtualTextureFeedbackBufferState.RequiresFeedbackHashClear(
                    previousFrameIndex,
                    frameIndex),
                Is.EqualTo(expected));
        }

        [Test]
        public void NativeAggregator_PreservesGpuCompactedHitCountsAcrossSpaces()
        {
            ulong firstKey = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(2, 3, 0));
            ulong secondKey = VirtualTextureFeedbackProcessor.EncodeKey(
                2,
                new VirtualTexturePageCoord(2, 3, 0));
            using var compacted = new NativeArray<VirtualTextureCompactedFeedbackRequest>(
                new[]
                {
                    new VirtualTextureCompactedFeedbackRequest(firstKey, 7u, 0u),
                    new VirtualTextureCompactedFeedbackRequest(secondKey, 2u, 1u),
                },
                Allocator.TempJob);
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(
                    VirtualTextureViewId.FromCameraType(CameraType.Game),
                    CameraType.Game,
                    compacted,
                    compacted.Length,
                    frameIndex: 12,
                    residentAccessCount: 1,
                    acceptedFaultRequestCount: 9),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(2));
            Assert.That(aggregator.TryGetRequestsForSpace(1, out var firstSpace), Is.True);
            Assert.That(firstSpace.Length, Is.EqualTo(1));
            Assert.That(firstSpace[0].HitCount, Is.EqualTo(7));
            Assert.That(aggregator.TryGetRequestsForSpace(2, out var secondSpace), Is.True);
            Assert.That(secondSpace.Length, Is.EqualTo(1));
            Assert.That(secondSpace[0].HitCount, Is.EqualTo(3));
        }

        [Test]
        public void NativeAggregator_OrsResidentWeightAcrossCompactedBatches()
        {
            ulong key = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(2, 3, 0));
            using var firstCompacted = new NativeArray<VirtualTextureCompactedFeedbackRequest>(
                new[] { new VirtualTextureCompactedFeedbackRequest(key, 2u, 1u) },
                Allocator.TempJob);
            using var secondCompacted = new NativeArray<VirtualTextureCompactedFeedbackRequest>(
                new[] { new VirtualTextureCompactedFeedbackRequest(key, 3u, 1u) },
                Allocator.TempJob);
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(
                    VirtualTextureViewId.FromCameraType(CameraType.Game),
                    CameraType.Game,
                    firstCompacted,
                    1,
                    frameIndex: 12,
                    residentAccessCount: 1,
                    acceptedFaultRequestCount: 2),
                new(
                    VirtualTextureViewId.FromCameraType(CameraType.SceneView),
                    CameraType.SceneView,
                    secondCompacted,
                    1,
                    frameIndex: 12,
                    residentAccessCount: 1,
                    acceptedFaultRequestCount: 3),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(1));
            Assert.That(aggregator.AggregatedRequests[0].HitCount, Is.EqualTo(6));
        }

        [TestCase(true, true, 4, 7, 4)]
        [TestCase(false, true, 4, 7, 0)]
        [TestCase(true, false, 4, 7, 0)]
        [TestCase(false, false, 4, 7, 0)]
        public void CompletedReadbackCounts_RequireBothStagesToBeValid(
            bool requestsReadbackValid,
            bool counterReadbackValid,
            int requestCapacity,
            int completedRequestCount,
            int expectedRequestCount)
        {
            Assert.That(
                VirtualTextureFeedbackBufferState.ResolveCompletedRequestCount(
                    requestsReadbackValid,
                    counterReadbackValid,
                    requestCapacity,
                    completedRequestCount),
                Is.EqualTo(expectedRequestCount));
        }

        [Test]
        public void Aggregate_PrioritizesGameCameraBeforeSceneView_AcrossMipAndHitCount()
        {
            ulong sceneFine = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 0));
            ulong gameCoarse = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 1));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.SceneView, new[] { sceneFine, sceneFine, sceneFine }, 3, 11),
                new(CameraType.Game, new[] { gameCoarse }, 1, 11),
            };

            List<VirtualTextureAggregatedFeedbackRequest> aggregated = VirtualTextureFeedbackProcessor.Aggregate(batches);

            Assert.That(aggregated.Count, Is.EqualTo(2));
            Assert.That(aggregated[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 1)));
            Assert.That(aggregated[0].CameraPriority, Is.EqualTo(0));
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            Assert.That(aggregated[1].CameraPriority, Is.EqualTo(1));
        }

        [Test]
        public void NativeAggregator_ReusesCapacity_WhenFollowingFrameIsEmpty()
        {
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 0));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, new[] { requestKey, requestKey }, 2, 9),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(1));
            Assert.That(aggregator.AggregatedRequests[0].SpaceId, Is.EqualTo(1));
            Assert.That(aggregator.AggregatedRequests[0].HitCount, Is.EqualTo(2));
            Assert.That(aggregator.RequestCapacity, Is.EqualTo(2));
            Assert.That(aggregator.BatchCapacity, Is.EqualTo(1));
            Assert.That(aggregator.LastUsedParallelJobs, Is.False);

            aggregator.Aggregate(
                null,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.AggregatedRequests.Length, Is.Zero);
            Assert.That(aggregator.SpaceRanges.Length, Is.Zero);
            Assert.That(aggregator.RequestCapacity, Is.EqualTo(2));
            Assert.That(aggregator.BatchCapacity, Is.EqualTo(1));
        }

        [Test]
        public void Aggregate_UsesMipWeightedHitCountInsteadOfStrictCoarseFirst()
        {
            ulong fine = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(3, 2, 0));
            ulong coarse = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(0, 0, 2));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, new[] { fine, fine, fine, fine, coarse }, 5, 12),
            };

            List<VirtualTextureAggregatedFeedbackRequest> aggregated =
                VirtualTextureFeedbackProcessor.Aggregate(batches);

            Assert.That(aggregated.Count, Is.EqualTo(2));
            Assert.That(aggregated[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(3, 2, 0)));
            Assert.That(aggregated[0].HitCount, Is.EqualTo(4));
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 2)));
            Assert.That(aggregated[1].HitCount, Is.EqualTo(1));
        }

        [Test]
        public void Aggregate_StillPrioritizesCoarseCoverageWhenItsWeightedScoreIsHigher()
        {
            ulong fine = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(3, 2, 0));
            ulong coarse = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(0, 0, 2));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, new[] { fine, fine, fine, fine, coarse, coarse }, 6, 12),
            };

            List<VirtualTextureAggregatedFeedbackRequest> aggregated =
                VirtualTextureFeedbackProcessor.Aggregate(batches);

            Assert.That(aggregated.Count, Is.EqualTo(2));
            Assert.That(aggregated[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 2)));
            Assert.That(aggregated[0].HitCount, Is.EqualTo(2));
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(3, 2, 0)));
            Assert.That(aggregated[1].HitCount, Is.EqualTo(4));
        }

        [Test]
        public void NativeAggregator_GroupsBySpace_WhilePreservingPriorityOrder()
        {
            ulong spaceOneLow = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(0, 0, 0));
            ulong spaceOneHigh = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(1, 0, 0));
            ulong spaceTwo = VirtualTextureFeedbackProcessor.EncodeKey(
                2,
                new VirtualTexturePageCoord(0, 0, 0));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.SceneView, new[] { spaceOneLow }, 1, 9),
                new(CameraType.Game, new[] { spaceTwo, spaceOneHigh, spaceOneHigh }, 3, 9),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.TryGetRequestsForSpace(1, out var spaceOneRequests), Is.True);
            Assert.That(spaceOneRequests.Length, Is.EqualTo(2));
            Assert.That(spaceOneRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
            Assert.That(spaceOneRequests[0].HitCount, Is.EqualTo(2));
            Assert.That(spaceOneRequests[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            Assert.That(aggregator.TryGetRequestsForSpace(2, out var spaceTwoRequests), Is.True);
            Assert.That(spaceTwoRequests.Length, Is.EqualTo(1));
            Assert.That(aggregator.TryGetRequestsForSpace(3, out _), Is.False);
        }

        [Test]
        public void NativeAggregator_ConsumesNativeReadback_AndPreservesActiveViewPriority()
        {
            ulong sharedRequest = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(3, 2, 0));
            ulong backgroundRequest = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(0, 0, 0));
            using var nativeRequests = new NativeArray<ulong>(
                new[] { sharedRequest, sharedRequest },
                Allocator.TempJob);
            var activeViewId = VirtualTextureViewId.FromCameraType(CameraType.Game);
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(
                    VirtualTextureViewId.FromCameraType(CameraType.SceneView),
                    CameraType.SceneView,
                    new[] { backgroundRequest, backgroundRequest, backgroundRequest },
                    3,
                    20),
                new(activeViewId, CameraType.Game, nativeRequests, nativeRequests.Length, 20),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                activeViewId,
                activeViewId,
                CameraType.Game);

            Assert.That(batches[1].ManagedRequests, Is.Null);
            Assert.That(batches[1].NativeRequests.IsCreated, Is.True);
            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(2));
            Assert.That(aggregator.AggregatedRequests[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(3, 2, 0)));
            Assert.That(aggregator.AggregatedRequests[0].HitCount, Is.EqualTo(2));
            Assert.That(aggregator.AggregatedRequests[0].IsActiveView, Is.True);
            Assert.That(aggregator.AggregatedRequests[0].ViewId, Is.EqualTo(activeViewId));
            Assert.That(aggregator.ActiveViewRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void NativeAggregator_UsesParallelJobs_ForLargeRequestBatches()
        {
            var requests = new ulong[65];
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                requests[requestIndex] = VirtualTextureFeedbackProcessor.EncodeKey(
                    requestIndex < 33 ? 1 : 2,
                    new VirtualTexturePageCoord(requestIndex, 0, 0));
            }

            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, requests, requests.Length, 12),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.LastUsedParallelJobs, Is.True);
            Assert.That(aggregator.RequestCapacity, Is.EqualTo(128));
            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(65));
            Assert.That(aggregator.SpaceRanges.Length, Is.EqualTo(2));

            aggregator.Aggregate(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.LastUsedParallelJobs, Is.True);
            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(65));
            Assert.That(aggregator.SpaceRanges.Length, Is.EqualTo(2));
        }

        [Test]
        public void NativeAggregator_ScheduleDefersParallelCompletionUntilComplete()
        {
            var requests = new ulong[65];
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                requests[requestIndex] = VirtualTextureFeedbackProcessor.EncodeKey(
                    1,
                    new VirtualTexturePageCoord(requestIndex, 0, 0));
            }

            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, requests, requests.Length, 12),
            };
            using var aggregator = new VTFeedbackNativeAggregator();

            aggregator.Schedule(
                batches,
                VirtualTextureViewId.Invalid,
                VirtualTextureViewId.Invalid,
                default);

            Assert.That(aggregator.LastUsedParallelJobs, Is.True);
            Assert.That(aggregator.HasOutstandingJobs, Is.True);

            aggregator.Complete();

            Assert.That(aggregator.HasOutstandingJobs, Is.False);
            Assert.That(aggregator.AggregatedRequests.Length, Is.EqualTo(requests.Length));
            Assert.That(aggregator.SpaceRanges.Length, Is.EqualTo(1));
        }

        [Test]
        public void ShouldScheduleReadback_SkipsStableEmptyStaticView_UntilHeartbeatInterval()
        {
            var signature = new VirtualTextureFeedbackViewSignature(
                Matrix4x4.identity,
                Matrix4x4.Perspective(60f, 1f, 0.1f, 100f),
                actualWidth: 1920,
                actualHeight: 1080,
                pixelWidth: 1920,
                pixelHeight: 1080);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 100 + VirtualTextureFeedbackBufferState.StableReadbackIntervalFrames - 1,
                    signature,
                    signature),
                Is.False);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 100 + VirtualTextureFeedbackBufferState.StableReadbackIntervalFrames,
                    signature,
                    signature),
                Is.True);
        }

        [Test]
        public void ShouldScheduleReadback_RequiresCompleteFeedbackPhaseCoverageBeforeThrottling()
        {
            var signature = new VirtualTextureFeedbackViewSignature(
                Matrix4x4.identity,
                Matrix4x4.Perspective(60f, 1f, 0.1f, 100f),
                actualWidth: 1920,
                actualHeight: 1080,
                pixelWidth: 1920,
                pixelHeight: 1080);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: false,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    signature,
                    signature),
                Is.True);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    signature,
                    signature),
                Is.False);
        }

        [Test]
        public void EmptyFeedbackPhaseCoverage_RequiresEveryJitterPhase()
        {
            const int sampleArea = 4;
            ulong phaseMask = 0ul;

            phaseMask = VirtualTextureFeedbackBufferState.AccumulateEmptyFeedbackPhaseForTesting(
                phaseMask,
                sampleArea,
                frameIndex: 100);
            phaseMask = VirtualTextureFeedbackBufferState.AccumulateEmptyFeedbackPhaseForTesting(
                phaseMask,
                sampleArea,
                frameIndex: 102);

            Assert.That(
                VirtualTextureFeedbackBufferState.HasCompleteFeedbackPhaseCoverageForTesting(
                    phaseMask,
                    sampleArea),
                Is.False,
                "Two empty readbacks can still cover only half of a 2x2 feedback jitter cycle.");

            phaseMask = VirtualTextureFeedbackBufferState.AccumulateEmptyFeedbackPhaseForTesting(
                phaseMask,
                sampleArea,
                frameIndex: 101);
            phaseMask = VirtualTextureFeedbackBufferState.AccumulateEmptyFeedbackPhaseForTesting(
                phaseMask,
                sampleArea,
                frameIndex: 103);

            Assert.That(
                VirtualTextureFeedbackBufferState.HasCompleteFeedbackPhaseCoverageForTesting(
                    phaseMask,
                    sampleArea),
                Is.True);
        }

        [Test]
        public void ShouldScheduleReadback_KeepsFastPath_WhenViewChangesOrUploadWorkIsActive()
        {
            var firstSignature = new VirtualTextureFeedbackViewSignature(
                Matrix4x4.identity,
                Matrix4x4.Perspective(60f, 1f, 0.1f, 100f),
                actualWidth: 1920,
                actualHeight: 1080,
                pixelWidth: 1920,
                pixelHeight: 1080);
            var secondSignature = new VirtualTextureFeedbackViewSignature(
                Matrix4x4.Translate(Vector3.forward),
                Matrix4x4.Perspective(60f, 1f, 0.1f, 100f),
                actualWidth: 1920,
                actualHeight: 1080,
                pixelWidth: 1920,
                pixelHeight: 1080);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    secondSignature,
                    firstSignature),
                Is.True);

            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: true,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    firstSignature,
                    firstSignature),
                Is.True);
        }

        [Test]
        public void ShouldScheduleReadback_KeepsFastPath_WhenAdaptiveMipBiasChanges()
        {
            var stableSignature = new VirtualTextureFeedbackViewSignature(
                Matrix4x4.identity,
                Matrix4x4.Perspective(60f, 1f, 0.1f, 100f),
                actualWidth: 1920,
                actualHeight: 1080,
                pixelWidth: 1920,
                pixelHeight: 1080,
                adaptiveMipBias: 2f);
            VirtualTextureFeedbackViewSignature recoveringSignature =
                stableSignature.WithAdaptiveMipBias(1.875f);

            Assert.That(recoveringSignature, Is.Not.EqualTo(stableSignature));
            Assert.That(
                VirtualTextureFeedbackBufferState.ShouldScheduleReadbackForTesting(
                    forceImmediateReadback: false,
                    hasCompletedReadbackResult: true,
                    lastCompletedReadbackWasEmpty: true,
                    hasCompleteQuiescenceCoverage: true,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    recoveringSignature,
                    stableSignature),
                Is.True);
        }
    }

    public sealed class VirtualTextureFeedbackCompactionTests
    {
        private const string ComputeRelativePath =
            "Tests/Editor/SubSystem/VirtualTexture/VirtualTextureFeedbackCompactionTests.compute";
        private const int CounterCount = 8;
        private const int ResidentAccessCounterIndex = 2;
        private const int FaultOverflowCounterIndex = 3;
        private const int ResidentOverflowCounterIndex = 4;
        private const int AcceptedFaultCounterIndex = 7;

        [Test]
        public void CompactedRecord_HasMatchingGpuStride()
        {
            Assert.That(
                Marshal.SizeOf<VirtualTextureCompactedFeedbackRequest>(),
                Is.EqualTo(VirtualTextureCompactedFeedbackRequest.Stride));
        }

        [Test]
        public void FaultCompaction_PreservesEveryHotKeyHit()
        {
            ulong key = VirtualTextureFeedbackProcessor.EncodeKey(
                7,
                new VirtualTexturePageCoord(3, 5, 0));
            ulong[] keys = Enumerable.Repeat(key, 128).ToArray();

            FeedbackResult result = Dispatch(keys, outputCapacity: 128);

            Assert.That(result.Counters[AcceptedFaultCounterIndex], Is.EqualTo(128));
            Assert.That(result.Counters[FaultOverflowCounterIndex], Is.Zero);
            Assert.That(result.Records.Sum(record => (long)record.FaultHitCount), Is.EqualTo(128));
            Assert.That(result.Records.All(record => record.Key == key), Is.True);
        }

        [Test]
        public void FaultCompaction_KeepsIdenticalCoordinatesDistinctAcrossSpaces()
        {
            ulong firstKey = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(4, 6, 0));
            ulong secondKey = VirtualTextureFeedbackProcessor.EncodeKey(
                2,
                new VirtualTexturePageCoord(4, 6, 0));
            var keys = new ulong[128];
            for (int index = 0; index < keys.Length; index++)
                keys[index] = (index & 1) == 0 ? firstKey : secondKey;

            FeedbackResult result = Dispatch(keys, outputCapacity: 128);
            Dictionary<ulong, long> hitsByKey = result.Records
                .GroupBy(record => record.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(record => (long)record.FaultHitCount));

            Assert.That(hitsByKey.Count, Is.EqualTo(2));
            Assert.That(hitsByKey[firstKey], Is.EqualTo(64));
            Assert.That(hitsByKey[secondKey], Is.EqualTo(64));
        }

        [Test]
        public void FaultCompaction_ClampsOutputAndReportsRejectedHits()
        {
            var keys = new ulong[16];
            for (int index = 0; index < keys.Length; index++)
            {
                keys[index] = VirtualTextureFeedbackProcessor.EncodeKey(
                    index + 1,
                    new VirtualTexturePageCoord(index, 0, 0));
            }

            FeedbackResult result = Dispatch(keys, outputCapacity: 4);

            Assert.That(result.Records.Length, Is.EqualTo(4));
            Assert.That(result.Counters[AcceptedFaultCounterIndex], Is.EqualTo(4));
            Assert.That(result.Counters[FaultOverflowCounterIndex], Is.EqualTo(12));
        }

        [Test]
        public void Compaction_ReportsResidentHitsForFaultOverflowSentinels()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("The active graphics device does not support compute shaders.");

            ComputeShader compute = LoadCompute();
            const int outputCapacity = 1;
            const int hashCapacity = 16;
            ulong acceptedKey = VirtualTextureFeedbackProcessor.EncodeKey(
                1,
                new VirtualTexturePageCoord(0, 0, 0));
            ulong overflowKey = VirtualTextureFeedbackProcessor.EncodeKey(
                2,
                new VirtualTexturePageCoord(1, 0, 0));
            using var acceptedKeyBuffer = new ComputeBuffer(
                1,
                sizeof(ulong),
                ComputeBufferType.Structured);
            using var overflowKeyBuffer = new ComputeBuffer(
                1,
                sizeof(ulong),
                ComputeBufferType.Structured);
            using var outputBuffer = new ComputeBuffer(
                outputCapacity,
                VirtualTextureCompactedFeedbackRequest.Stride,
                ComputeBufferType.Structured);
            using var counterBuffer = new ComputeBuffer(
                CounterCount,
                sizeof(uint),
                ComputeBufferType.Structured);
            using var hashBuffer = new ComputeBuffer(
                hashCapacity,
                sizeof(uint) * 4,
                ComputeBufferType.Structured);
            counterBuffer.SetData(new uint[CounterCount]);
            hashBuffer.SetData(new Vector4[hashCapacity]);

            int faultKernel = compute.FindKernel("WriteFaultFeedback");
            int residentKernel = compute.FindKernel("WriteResidentFeedback");
            acceptedKeyBuffer.SetData(new[] { acceptedKey });
            overflowKeyBuffer.SetData(new[] { overflowKey });
            Bind(
                compute,
                faultKernel,
                acceptedKeyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                keyCount: 1,
                outputCapacity,
                hashCapacity,
                frameIndex: 0);
            compute.Dispatch(faultKernel, 1, 1, 1);

            Bind(
                compute,
                faultKernel,
                overflowKeyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                keyCount: 1,
                outputCapacity,
                hashCapacity,
                frameIndex: 0);
            compute.Dispatch(faultKernel, 1, 1, 1);
            Bind(
                compute,
                residentKernel,
                overflowKeyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                keyCount: 1,
                outputCapacity,
                hashCapacity,
                frameIndex: 0);
            compute.Dispatch(residentKernel, 1, 1, 1);

            var counters = new uint[CounterCount];
            counterBuffer.GetData(counters);
            Assert.That(counters[AcceptedFaultCounterIndex], Is.EqualTo(1));
            Assert.That(counters[FaultOverflowCounterIndex], Is.EqualTo(1));
            Assert.That(counters[ResidentAccessCounterIndex], Is.Zero);
            Assert.That(counters[ResidentOverflowCounterIndex], Is.EqualTo(1));
        }

        [Test]
        public void Compaction_MergesFaultAndResidentStatusForSameKey()
        {
            ulong key = VirtualTextureFeedbackProcessor.EncodeKey(
                3,
                new VirtualTexturePageCoord(2, 1, 0));
            ulong[] keys = Enumerable.Repeat(key, 64).ToArray();

            FeedbackResult result = Dispatch(
                keys,
                outputCapacity: 64,
                dispatchResidentAfterFault: true);

            Assert.That(result.Counters[AcceptedFaultCounterIndex], Is.EqualTo(64));
            Assert.That(result.Counters[ResidentAccessCounterIndex], Is.EqualTo(1));
            Assert.That(result.Records.Sum(record => (long)record.FaultHitCount), Is.EqualTo(64));
            Assert.That(result.Records.Count(record => record.ResidentAccessCount > 0u), Is.EqualTo(1));
        }

        [Test]
        public void Compaction_ReusesHashAcrossFrameEpochs()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("The active graphics device does not support compute shaders.");

            ComputeShader compute = LoadCompute();
            const int outputCapacity = 8;
            const int hashCapacity = 16;
            ulong key = VirtualTextureFeedbackProcessor.EncodeKey(
                3,
                new VirtualTexturePageCoord(2, 1, 0));
            using var keyBuffer = new ComputeBuffer(1, sizeof(ulong), ComputeBufferType.Structured);
            using var outputBuffer = new ComputeBuffer(
                outputCapacity,
                VirtualTextureCompactedFeedbackRequest.Stride,
                ComputeBufferType.Structured);
            using var counterBuffer = new ComputeBuffer(
                CounterCount,
                sizeof(uint),
                ComputeBufferType.Structured);
            using var hashBuffer = new ComputeBuffer(
                hashCapacity,
                sizeof(uint) * 4,
                ComputeBufferType.Structured);
            keyBuffer.SetData(new[] { key });
            hashBuffer.SetData(new Vector4[hashCapacity]);

            FeedbackResult first = DispatchFaultFrame(
                compute,
                keyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                outputCapacity,
                hashCapacity,
                frameIndex: 0);
            FeedbackResult second = DispatchFaultFrame(
                compute,
                keyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                outputCapacity,
                hashCapacity,
                frameIndex: 1);

            Assert.That(first.Records.Length, Is.EqualTo(1));
            Assert.That(first.Records[0].Key, Is.EqualTo(key));
            Assert.That(first.Records[0].FaultHitCount, Is.EqualTo(1));
            Assert.That(second.Records.Length, Is.EqualTo(1));
            Assert.That(second.Records[0].Key, Is.EqualTo(key));
            Assert.That(second.Records[0].FaultHitCount, Is.EqualTo(1));
            Assert.That(second.Counters[AcceptedFaultCounterIndex], Is.EqualTo(1));
        }

        private static FeedbackResult Dispatch(
            ulong[] keys,
            int outputCapacity,
            bool dispatchResidentAfterFault = false)
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("The active graphics device does not support compute shaders.");

            ComputeShader compute = LoadCompute();
            int hashCapacity = Mathf.NextPowerOfTwo(Mathf.Max(outputCapacity * 2, 16));
            using var keyBuffer = new ComputeBuffer(keys.Length, sizeof(ulong), ComputeBufferType.Structured);
            using var outputBuffer = new ComputeBuffer(
                outputCapacity,
                VirtualTextureCompactedFeedbackRequest.Stride,
                ComputeBufferType.Structured);
            using var counterBuffer = new ComputeBuffer(CounterCount, sizeof(uint), ComputeBufferType.Structured);
            using var hashBuffer = new ComputeBuffer(hashCapacity, sizeof(uint) * 4, ComputeBufferType.Structured);
            keyBuffer.SetData(keys);
            counterBuffer.SetData(new uint[CounterCount]);
            hashBuffer.SetData(new Vector4[hashCapacity]);

            int faultKernel = compute.FindKernel("WriteFaultFeedback");
            Bind(
                compute,
                faultKernel,
                keyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                keys.Length,
                outputCapacity,
                hashCapacity,
                frameIndex: 0);
            compute.Dispatch(faultKernel, (keys.Length + 63) / 64, 1, 1);

            if (dispatchResidentAfterFault)
            {
                int residentKernel = compute.FindKernel("WriteResidentFeedback");
                Bind(
                    compute,
                    residentKernel,
                    keyBuffer,
                    outputBuffer,
                    counterBuffer,
                    hashBuffer,
                    keys.Length,
                    outputCapacity,
                    hashCapacity,
                    frameIndex: 0);
                compute.Dispatch(residentKernel, (keys.Length + 63) / 64, 1, 1);
            }

            var counters = new uint[CounterCount];
            counterBuffer.GetData(counters);
            int recordCount = Mathf.Min(outputCapacity, checked((int)counters[0]));
            var records = new VirtualTextureCompactedFeedbackRequest[recordCount];
            if (recordCount > 0)
                outputBuffer.GetData(records, 0, 0, recordCount);
            return new FeedbackResult(counters, records);
        }

        private static FeedbackResult DispatchFaultFrame(
            ComputeShader compute,
            ComputeBuffer keyBuffer,
            ComputeBuffer outputBuffer,
            ComputeBuffer counterBuffer,
            ComputeBuffer hashBuffer,
            int outputCapacity,
            int hashCapacity,
            int frameIndex)
        {
            counterBuffer.SetData(new uint[CounterCount]);
            int faultKernel = compute.FindKernel("WriteFaultFeedback");
            Bind(
                compute,
                faultKernel,
                keyBuffer,
                outputBuffer,
                counterBuffer,
                hashBuffer,
                keyCount: 1,
                outputCapacity,
                hashCapacity,
                frameIndex);
            compute.Dispatch(faultKernel, 1, 1, 1);

            var counters = new uint[CounterCount];
            counterBuffer.GetData(counters);
            int recordCount = Mathf.Min(outputCapacity, checked((int)counters[0]));
            var records = new VirtualTextureCompactedFeedbackRequest[recordCount];
            if (recordCount > 0)
                outputBuffer.GetData(records, 0, 0, recordCount);
            return new FeedbackResult(counters, records);
        }

        private static void Bind(
            ComputeShader compute,
            int kernel,
            ComputeBuffer keyBuffer,
            ComputeBuffer outputBuffer,
            ComputeBuffer counterBuffer,
            ComputeBuffer hashBuffer,
            int keyCount,
            int outputCapacity,
            int hashCapacity,
            int frameIndex)
        {
            compute.SetBuffer(kernel, "_FeedbackTestKeys", keyBuffer);
            compute.SetBuffer(kernel, "_VTFeedbackRequests", outputBuffer);
            compute.SetBuffer(kernel, "_VTFeedbackCounter", counterBuffer);
            compute.SetBuffer(kernel, "_VTFeedbackResidentHash", hashBuffer);
            compute.SetInt("_FeedbackTestCount", keyCount);
            compute.SetInt("_VTFeedbackFrameIndex", frameIndex);
            compute.SetInt("_VTFeedbackRequestCapacity", outputCapacity);
            compute.SetInt("_VTFeedbackResidentHashCapacity", hashCapacity);
        }

        private static ComputeShader LoadCompute()
        {
            string assetPath = VividPackagePathUtility.GetPreferredAssetPath(ComputeRelativePath);
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            Assert.That(compute, Is.Not.Null, $"Missing feedback compaction test shader at {assetPath}.");
            return compute;
        }

        private readonly struct FeedbackResult
        {
            internal FeedbackResult(
                uint[] counters,
                VirtualTextureCompactedFeedbackRequest[] records)
            {
                Counters = counters;
                Records = records;
            }

            internal uint[] Counters { get; }

            internal VirtualTextureCompactedFeedbackRequest[] Records { get; }
        }
    }
}
