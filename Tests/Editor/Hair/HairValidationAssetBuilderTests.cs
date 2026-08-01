using NUnit.Framework;
using VividRP.Editor;

namespace VividRP.Editor.Tests
{
    public sealed class HairValidationAssetBuilderTests
    {
        [Test]
        public void CreateValidationSegments_ProducesConnectedTaperedStrands()
        {
            const int strandCount = 3;
            const int segmentsPerStrand = 4;
            var segments = HairValidationAssetBuilder
                .CreateValidationSegments(
                    strandCount,
                    segmentsPerStrand);

            Assert.That(
                segments.Count,
                Is.EqualTo(strandCount * segmentsPerStrand));
            for (var strandIndex = 0;
                 strandIndex < strandCount;
                 strandIndex++)
            {
                var first = segments[strandIndex * segmentsPerStrand];
                var last = segments[
                    strandIndex * segmentsPerStrand
                    + segmentsPerStrand
                    - 1];
                Assert.That(first.Start.UV.x, Is.EqualTo(0.0f));
                Assert.That(last.End.UV.x, Is.EqualTo(1.0f));
                Assert.That(first.Start.Radius, Is.GreaterThan(last.End.Radius));

                for (var segmentIndex = 1;
                     segmentIndex < segmentsPerStrand;
                     segmentIndex++)
                {
                    var previous = segments[
                        strandIndex * segmentsPerStrand
                        + segmentIndex
                        - 1];
                    var current = segments[
                        strandIndex * segmentsPerStrand
                        + segmentIndex];
                    Assert.That(
                        current.Start.Position,
                        Is.EqualTo(previous.End.Position));
                }
            }
        }
    }
}
