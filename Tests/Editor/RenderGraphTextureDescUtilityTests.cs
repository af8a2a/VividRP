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
            Assert.That(((RenderGraphTextureDesc)null).HasExplicitSize(), Is.False);
        }

        [Test]
        public void HasExplicitSize_ReturnsFalse_WhenDescriptorUsesDefaultPlaceholderSize()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 1
            };

            Assert.That(descriptor.HasExplicitSize(), Is.False);
        }

        [Test]
        public void HasExplicitSize_ReturnsTrue_WhenOnlyOneDimensionIsOne()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 64
            };

            Assert.That(descriptor.HasExplicitSize(), Is.True);
        }

        [Test]
        public void HasExplicitSize_ReturnsTrue_WhenDescriptorHasNonDefaultPositiveSize()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                Width = 64,
                Height = 32
            };

            Assert.That(descriptor.HasExplicitSize(), Is.True);
        }

        [Test]
        public void ResolveMaxExplicitWidth_ReturnsLargestExplicitDescriptorWidth_WhenAvailable()
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
                RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                    0,
                    0,
                    1,
                    descriptorA,
                    descriptorB),
                Is.EqualTo(128));
        }

        [Test]
        public void ResolveMaxExplicitHeight_ReturnsLargestExplicitDescriptorHeight_WhenAvailable()
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
                RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                    0,
                    0,
                    1,
                    descriptorA,
                    descriptorB),
                Is.EqualTo(32));
        }

        [Test]
        public void ResolveMaxExplicitWidthAndHeight_FallsBackToCameraDimensions_WhenNoExplicitDescriptorExists()
        {
            var placeholderDescriptor = new RenderGraphTextureDesc
            {
                Width = 1,
                Height = 1
            };

            Assert.That(
                RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                    0,
                    256,
                    1,
                    placeholderDescriptor),
                Is.EqualTo(256));
            Assert.That(
                RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                    0,
                    128,
                    1,
                    placeholderDescriptor),
                Is.EqualTo(128));
        }

        [Test]
        public void ResolveMaxExplicitWidthAndHeight_DoNotAllocate()
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

            RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(0, 0, 1, descriptorA, descriptorB);
            RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(0, 0, 1, descriptorA, descriptorB);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 32; i++)
            {
                RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(0, 0, 1, descriptorA, descriptorB);
                RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(0, 0, 1, descriptorA, descriptorB);
            }

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void ResolveColorFormat_ReturnsDescriptorColorFormat_WhenSpecified()
        {
            var descriptor = new RenderGraphTextureDesc
            {
                ColorFormat = GraphicsFormat.R16G16B16A16_SFloat
            };

            Assert.That(
                descriptor.ResolveColorFormat(),
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
                descriptor.ResolveColorFormat(
                    GraphicsFormat.R8G8B8A8_SRGB),
                Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
        }
    }
}
