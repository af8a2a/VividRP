using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class NativeBindlessTextureDescriptorAllocatorTests
    {
        [Test]
        public void IsAvailable_RetriesInitialization_WhenHeapAppearsLater()
        {
            var heapCountQueryCount = 0;
            var allocator = new NativeBindlessTextureDescriptorAllocator(
                () => ++heapCountQueryCount == 1 ? 0u : 128u,
                static () => 112u,
                static () => 16u,
                static (texture, index) => true,
                static () => GraphicsDeviceType.Direct3D12,
                static () => Texture2D.whiteTexture);

            Assert.That(allocator.IsAvailable, Is.True);
            Assert.That(allocator.DescriptorHeapCount, Is.EqualTo(128u));
            Assert.That(allocator.DescriptorStartIndex, Is.EqualTo(112u));
            Assert.That(allocator.DescriptorCapacity, Is.EqualTo(16u));
            Assert.That(heapCountQueryCount, Is.EqualTo(2));
            Assert.That(allocator.UnavailableReason, Is.Empty);
        }

        [Test]
        public void TryCreateTextureDescriptor_ReturnsFalse_WhenPluginReportsFailure()
        {
            var allocator = new NativeBindlessTextureDescriptorAllocator(
                static () => 64u,
                static () => 48u,
                static () => 16u,
                static (texture, index) => false,
                static () => GraphicsDeviceType.Direct3D12,
                static () => Texture2D.whiteTexture);
            var texture = new Texture2D(1, 1);

            try
            {
                var created = allocator.TryCreateTextureDescriptor(texture, 7);

                Assert.That(created, Is.False);
                Assert.That(allocator.IsAvailable, Is.True);
                Assert.That(allocator.UnavailableReason, Is.EqualTo("Bindless plugin failed to create a descriptor."));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void IsAvailable_ReturnsFalseWithoutQueryingHeap_WhenGraphicsBackendIsNotDirect3D12()
        {
            var heapCountQueryCount = 0;
            var allocator = new NativeBindlessTextureDescriptorAllocator(
                () =>
                {
                    heapCountQueryCount++;
                    return 64u;
                },
                static () => 48u,
                static () => 16u,
                static (texture, index) => true,
                static () => GraphicsDeviceType.Vulkan,
                static () => Texture2D.whiteTexture);

            Assert.That(allocator.IsAvailable, Is.False);
            Assert.That(allocator.DescriptorHeapCount, Is.EqualTo(0u));
            Assert.That(heapCountQueryCount, Is.EqualTo(0));
            Assert.That(allocator.UnavailableReason, Is.EqualTo("Bindless descriptors require the Direct3D12 graphics backend."));
        }
    }
}
