using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class CameraDimensionUtilityTests
    {
        [TestCase(1920, 1280, 800, 1920)]
        [TestCase(0, 1280, 800, 1280)]
        [TestCase(-1, 0, 800, 800)]
        [TestCase(0, 0, 0, 1)]
        public void ResolveCameraDimension_ReturnsExpectedValue_WhenFallbackOrderChanges(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            int expected)
        {
            Assert.That(
                CameraDimensionUtility.ResolveCameraDimension(
                    actualCameraDimension,
                    cameraDimension,
                    screenDimension),
                Is.EqualTo(expected));
        }
    }
}
