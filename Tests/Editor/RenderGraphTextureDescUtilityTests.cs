using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphTextureDescUtilityTests
    {
        [Test]
        public void HasExplicitSize_ReturnsFalse_WhenDescriptorIsNull()
        {
            Assert.That(RenderGraphTextureDescUtility.HasExplicitSize(null), Is.False);
        }

        [Test]
        public void HasExplicitSize_ReturnsFalse_WhenDescriptorUsesDefaultPlaceholderSize()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 1
            };

            Assert.That(RenderGraphTextureDescUtility.HasExplicitSize(descriptor), Is.False);
        }

        [Test]
        public void HasExplicitSize_ReturnsTrue_WhenOnlyOneDimensionIsOne()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 64
            };

            Assert.That(RenderGraphTextureDescUtility.HasExplicitSize(descriptor), Is.True);
        }

        [Test]
        public void HasExplicitSize_ReturnsTrue_WhenDescriptorHasNonDefaultPositiveSize()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 64,
                Height = 32
            };

            Assert.That(RenderGraphTextureDescUtility.HasExplicitSize(descriptor), Is.True);
        }
    }
}
