using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
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

        [Test]
        public void ResolveMaxExplicitDimension_ReturnsLargestExplicitDescriptorDimension_WhenAvailable()
        {
            var descriptorA = new RenderGraphTextureDesc
            {
                Width = 64,
                Height = 32
            };
            var descriptorB = new RenderGraphTextureDesc
            {
                Width = 128,
                Height = 16
            };

            Assert.That(
                RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                    descriptor => descriptor.Width,
                    0,
                    0,
                    1,
                    descriptorA,
                    descriptorB),
                Is.EqualTo(128));
        }

        [Test]
        public void ResolveMaxExplicitDimension_FallsBackToCameraDimension_WhenNoExplicitDescriptorExists()
        {
            var placeholderDescriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 1
            };

            Assert.That(
                RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                    descriptor => descriptor.Width,
                    0,
                    256,
                    1,
                    placeholderDescriptor),
                Is.EqualTo(256));
        }

        [Test]
        public void ResolveColorFormat_ReturnsDescriptorColorFormat_WhenSpecified()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                ColorFormat = GraphicsFormat.R16G16B16A16_SFloat
            };

            Assert.That(
                RenderGraphTextureDescUtility.ResolveColorFormat(descriptor),
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void ResolveColorFormat_ReturnsFallback_WhenDescriptorHasNoColorFormat()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                ColorFormat = GraphicsFormat.None
            };

            Assert.That(
                RenderGraphTextureDescUtility.ResolveColorFormat(
                    descriptor,
                    GraphicsFormat.R8G8B8A8_SRGB),
                Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
        }
    }
}
