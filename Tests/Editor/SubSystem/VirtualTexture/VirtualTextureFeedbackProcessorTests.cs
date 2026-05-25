using System.Collections.Generic;
using NUnit.Framework;
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

            Assert.That(aggregated[0].SpaceId, Is.EqualTo(2));
            Assert.That(aggregated[0].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(1, 0, 0)));
            Assert.That(aggregated[0].HitCount, Is.EqualTo(2));

            Assert.That(aggregated[1].SpaceId, Is.EqualTo(1));
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 1)));
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
        public void Aggregate_WritesIntoReusableOutput_WhenScratchIsProvided()
        {
            ulong requestKey = VirtualTextureFeedbackProcessor.EncodeKey(1, new VirtualTexturePageCoord(0, 0, 0));
            var batches = new List<VirtualTextureFeedbackBatch>
            {
                new(CameraType.Game, new[] { requestKey, requestKey }, 2, 9),
            };
            var scratch = new VirtualTextureFeedbackProcessor.Scratch();
            var output = new List<VirtualTextureAggregatedFeedbackRequest>
            {
                new(99, new VirtualTexturePageCoord(9, 9, 0), 1, 2),
            };

            VirtualTextureFeedbackProcessor.Aggregate(batches, scratch, output);

            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].SpaceId, Is.EqualTo(1));
            Assert.That(output[0].HitCount, Is.EqualTo(2));

            VirtualTextureFeedbackProcessor.Aggregate(null, scratch, output);

            Assert.That(output.Count, Is.EqualTo(0));
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
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 100 + VirtualTextureFeedbackBufferState.StableReadbackIntervalFrames,
                    signature,
                    signature),
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
                    lastScheduledReadbackFrame: 100,
                    frameIndex: 101,
                    firstSignature,
                    firstSignature),
                Is.True);
        }
    }
}
