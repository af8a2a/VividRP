using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount,
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 100 + VirtualTextureFeedbackBufferState.StableReadbackIntervalFrames,
                    signature,
                    signature),
                Is.True);
        }

        [Test]
        public void ShouldScheduleReadback_RequiresConsecutiveEmptyResultsBeforeThrottling()
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount - 1,
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    signature,
                    signature),
                Is.False);
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount,
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
                    consecutiveEmptyReadbackCount:
                        VirtualTextureFeedbackBufferState.QuiescentEmptyReadbackCount,
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    firstSignature,
                    firstSignature),
                Is.True);
        }
    }
}
