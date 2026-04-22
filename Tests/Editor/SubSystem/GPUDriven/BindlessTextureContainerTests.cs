using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;

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
        public void PreRender_DoesNotRebindDirtyTexture_WhenNativePointerIsUnchanged()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);

            try
            {
                var created = container.TryGetOrCreateIndex(texture, out _);

                container.MarkTextureDirty(texture);
                container.PreRender();

                Assert.That(created, Is.True);
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PreRender_AssignsNewDescriptor_WhenPotentialDirtyTextureRangeProvidesNewPointer()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var originalTexture = new Texture2D(1, 1);
            var replacementTexture = new Texture2D(1, 1);
            var changedIds = new NativeArray<EntityId>(1, Allocator.Temp);

            try
            {
                var created = container.TryGetOrCreateIndex(originalTexture, out uint index);
                changedIds[0] = originalTexture.GetEntityId();
                var changedTextures = new List<Object> { replacementTexture };

                container.AddPotentialDirtyTextureRange(changedIds, changedTextures);
                container.PreRender();

                Assert.That(created, Is.True);
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(2));
                Assert.That(allocator.DescriptorWrites[1].Index, Is.Not.EqualTo(index));
                Assert.That(allocator.DescriptorWrites[1].Index, Is.EqualTo(6u));
                Assert.That(allocator.DescriptorWrites[1].Texture, Is.SameAs(replacementTexture));
                Assert.That(container.AllocatedDescriptorCount, Is.EqualTo(2u));
                Assert.That(container.TextureBindingRevision, Is.EqualTo(1u));
            }
            finally
            {
                changedIds.Dispose();
                Object.DestroyImmediate(originalTexture);
                Object.DestroyImmediate(replacementTexture);
            }
        }

        [Test]
        public void PreRender_RetiresTrackedDescriptorAndRemovesMapping_WhenTextureWasDestroyed()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var texture = new Texture2D(1, 1);
            EntityId textureId = texture.GetEntityId();

            try
            {
                var created = container.TryGetOrCreateIndex(texture, out uint index);
                Object.DestroyImmediate(texture);

                container.MarkTextureDestroyed(textureId);
                container.PreRender();

                Assert.That(created, Is.True);
                Assert.That(allocator.DescriptorWrites.Count, Is.EqualTo(1));
                Assert.That(container.RegisteredTextureCount, Is.EqualTo(0));
                Assert.That(container.AllocatedDescriptorCount, Is.EqualTo(1u));
                Assert.That(container.TextureBindingRevision, Is.EqualTo(1u));
                Assert.That(container.TryGetExistingIndex(texture, out _), Is.False);

                allocator.CompletedFrameFenceValue = allocator.PendingFrameFenceValue;
                container.PreRender();

                Assert.That(container.AllocatedDescriptorCount, Is.EqualTo(0u));
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
        public void TryGetOrCreateIndex_ReusesRetiredDescriptorAfterFencePasses()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var originalTexture = new Texture2D(1, 1);
            var replacementTexture = new Texture2D(1, 1);
            var unrelatedTexture = new Texture2D(1, 1);
            var changedIds = new NativeArray<EntityId>(1, Allocator.Temp);

            try
            {
                var created = container.TryGetOrCreateIndex(originalTexture, out uint originalIndex);
                changedIds[0] = originalTexture.GetEntityId();
                container.AddPotentialDirtyTextureRange(changedIds, new List<Object> { replacementTexture });
                container.PreRender();

                allocator.CompletedFrameFenceValue = allocator.PendingFrameFenceValue;

                var unrelatedCreated = container.TryGetOrCreateIndex(unrelatedTexture, out uint unrelatedIndex);

                Assert.That(created, Is.True);
                Assert.That(unrelatedCreated, Is.True);
                Assert.That(originalIndex, Is.EqualTo(7u));
                Assert.That(unrelatedIndex, Is.EqualTo(originalIndex));
            }
            finally
            {
                changedIds.Dispose();
                Object.DestroyImmediate(originalTexture);
                Object.DestroyImmediate(replacementTexture);
                Object.DestroyImmediate(unrelatedTexture);
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
                DescriptorCapacity = descriptorHeapCount;
            }

            public bool IsAvailable { get; set; } = true;

            public uint DescriptorHeapCount { get; }
            public uint DescriptorStartIndex { get; }
            public uint DescriptorCapacity { get; }

            public ulong CompletedFrameFenceValue { get; set; }

            public ulong PendingFrameFenceValue { get; set; } = 1ul;

            public string UnavailableReason { get; set; } = string.Empty;

            public uint CreateSRVDescriptorCallCountThisFrame { get; private set; }

            public List<DescriptorWrite> DescriptorWrites { get; } = new();

            public void ResetPerFrameStats()
            {
                CreateSRVDescriptorCallCountThisFrame = 0;
            }

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                CreateSRVDescriptorCallCountThisFrame++;
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
