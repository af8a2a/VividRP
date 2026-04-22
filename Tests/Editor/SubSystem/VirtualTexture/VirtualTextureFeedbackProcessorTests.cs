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
            Assert.That(aggregated[1].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 0)));
            Assert.That(aggregated[1].HitCount, Is.EqualTo(1));

            Assert.That(aggregated[2].SpaceId, Is.EqualTo(1));
            Assert.That(aggregated[2].PageCoord, Is.EqualTo(new VirtualTexturePageCoord(0, 0, 1)));
            Assert.That(aggregated[2].HitCount, Is.EqualTo(2));
            Assert.That(aggregated[2].CameraPriority, Is.EqualTo(0));
        }
    }
}
