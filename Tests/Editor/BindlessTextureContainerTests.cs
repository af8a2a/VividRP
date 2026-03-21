using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Editor.Tests
{
    public class BindlessTextureContainerTests
    {
        [Test]
        public void TryGetOrCreateIndex_AssignsDescriptorFromHeapEnd_WhenTextureIsNew()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);

            try
            {
                var created = container.TryGetOrCreateIndex(texture, out uint index);

                Assert.That(created, Is.True);
                Assert.That(index, Is.EqualTo(7u));
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(1));
                Assert.That(allocator.DescriptorWrites[0].Index, Is.EqualTo(7u));
                Assert.That(allocator.DescriptorWrites[0].Texture, Is.SameAs(texture));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryGetOrCreateIndex_ReusesExistingDescriptor_WhenTextureIsRequestedAgain()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);

            try
            {
                var firstCreated = container.TryGetOrCreateIndex(texture, out uint firstIndex);
                var secondCreated = container.TryGetOrCreateIndex(texture, out uint secondIndex);

                Assert.That(firstCreated, Is.True);
                Assert.That(secondCreated, Is.True);
                Assert.That(secondIndex, Is.EqualTo(firstIndex));
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryAllocateRange_ReturnsContiguousRangeFromHeapEnd()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(10);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);

            try
            {
                var allocatedRange = container.TryAllocateRange(3, out uint startIndex);
                var created = container.TryGetOrCreateIndex(texture, out uint textureIndex);

                Assert.That(allocatedRange, Is.True);
                Assert.That(startIndex, Is.EqualTo(7u));
                Assert.That(created, Is.True);
                Assert.That(textureIndex, Is.EqualTo(6u));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PreRender_RebindsDestroyedTextureToWhiteTexture_WhenTextureWasTracked()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);
            var instanceId = texture.GetInstanceID();

            try
            {
                var created = container.TryGetOrCreateIndex(texture, out uint index);
                Object.DestroyImmediate(texture);

                container.MarkTextureDestroyed(instanceId);
                container.PreRender();

                Assert.That(created, Is.True);
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(2));
                Assert.That(allocator.DescriptorWrites[1].Index, Is.EqualTo(index));
                Assert.That(allocator.DescriptorWrites[1].Texture, Is.SameAs(Texture2D.whiteTexture));
            }
            catch
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }

                throw;
            }
        }

        [Test]
        public void TryGetOrCreateIndex_ReturnsFalse_WhenAllocatorIsUnavailable()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8)
            {
                IsAvailable = false,
                UnavailableReason = "Missing native plugin.",
            };
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);

            try
            {
                var created = container.TryGetOrCreateIndex(texture, out uint index);

                Assert.That(created, Is.False);
                Assert.That(index, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
                Assert.That(allocator.DescriptorWrites, Is.Empty);
                Assert.That(container.UnavailableReason, Is.EqualTo("Missing native plugin."));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private sealed class FakeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
        {
            public FakeBindlessTextureDescriptorAllocator(uint descriptorHeapCount)
            {
                DescriptorHeapCount = descriptorHeapCount;
            }

            public bool IsAvailable { get; set; } = true;

            public uint DescriptorHeapCount { get; }

            public string UnavailableReason { get; set; } = string.Empty;

            public List<DescriptorWrite> DescriptorWrites { get; } = new();

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                DescriptorWrites.Add(new DescriptorWrite(index, texture));
                return true;
            }
        }

        private readonly struct DescriptorWrite
        {
            public DescriptorWrite(uint index, Texture texture)
            {
                Index = index;
                Texture = texture;
            }

            public uint Index { get; }

            public Texture Texture { get; }
        }
    }
}
