using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Editor.Tests
{
    public sealed class GPUDrivenVirtualTextureAtlasAllocatorTests
    {
        [Test]
        public void TryAllocate_UsesLowestMortonAddressForAlignedRectangles()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(16, 8);

            Assert.That(allocator.TryAllocate(4, 2, out var first), Is.True);
            Assert.That(allocator.TryAllocate(4, 2, out var second), Is.True);
            Assert.That(allocator.TryAllocate(4, 2, out var third), Is.True);

            Assert.That(first.PageRegion, Is.EqualTo(new RectInt(0, 0, 4, 2)));
            Assert.That(second.PageRegion, Is.EqualTo(new RectInt(0, 2, 4, 2)));
            Assert.That(third.PageRegion, Is.EqualTo(new RectInt(4, 0, 4, 2)));
            Assert.That(first.MaxMip, Is.EqualTo(1));
            Assert.That(second.PageRegion.x % (1 << second.MaxMip), Is.Zero);
            Assert.That(second.PageRegion.y % (1 << second.MaxMip), Is.Zero);
            Assert.That(allocator.AllocatedPageCount, Is.EqualTo(24));
            Assert.That(allocator.AllocationCount, Is.EqualTo(3));
        }

        [Test]
        public void Release_CoalescesBuddiesAndReusesLowestAddress()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(16, 8);
            Assert.That(allocator.TryAllocate(4, 2, out var first), Is.True);
            Assert.That(allocator.TryAllocate(4, 2, out var second), Is.True);
            Assert.That(allocator.TryAllocate(4, 2, out var third), Is.True);

            Assert.That(allocator.Release(second), Is.True);
            Assert.That(allocator.Release(first), Is.True);
            Assert.That(allocator.Release(third), Is.True);
            Assert.That(allocator.AllocatedPageCount, Is.Zero);
            Assert.That(allocator.AllocationCount, Is.Zero);
            Assert.That(allocator.GetLargestFreeSquarePageCount(), Is.EqualTo(8));

            Assert.That(allocator.TryAllocate(4, 2, out var replacement), Is.True);
            Assert.That(replacement.PageRegion, Is.EqualTo(new RectInt(0, 0, 4, 2)));
        }

        [Test]
        public void Release_RejectsDuplicateAndForeignHandlesWithoutChangingCounters()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(16, 8);
            var foreignAllocator = new GPUDrivenVirtualTextureAtlasAllocator(16, 8);
            Assert.That(allocator.TryAllocate(2, 2, out var allocation), Is.True);
            Assert.That(foreignAllocator.TryAllocate(2, 2, out var foreignAllocation), Is.True);

            Assert.That(allocator.Release(foreignAllocation), Is.False);
            Assert.That(allocator.AllocatedPageCount, Is.EqualTo(4));
            Assert.That(allocator.Release(allocation), Is.True);
            Assert.That(allocator.Release(allocation), Is.False);
            Assert.That(allocator.AllocatedPageCount, Is.Zero);
            Assert.That(foreignAllocator.Release(foreignAllocation), Is.True);
        }

        [Test]
        public void TryAllocate_FillsAtlasWithRectanglesAndRecoversFragmentedRegion()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(8, 4);
            var allocations = new GPUDrivenVirtualTextureAtlasAllocator.Allocation[8];
            for (int allocationIndex = 0; allocationIndex < allocations.Length; allocationIndex++)
                Assert.That(allocator.TryAllocate(4, 2, out allocations[allocationIndex]), Is.True);

            Assert.That(allocator.AllocatedPageCount, Is.EqualTo(64));
            Assert.That(allocator.GetLargestFreeSquarePageCount(), Is.Zero);
            Assert.That(allocator.TryAllocate(1, 1, out _), Is.False);

            Assert.That(allocator.Release(allocations[3]), Is.True);
            Assert.That(allocator.CanAllocate(4, 2), Is.True);
            Assert.That(allocator.TryAllocate(4, 2, out var replacement), Is.True);
            Assert.That(replacement.PageRegion, Is.EqualTo(allocations[3].PageRegion));
        }

        [Test]
        public void TryAllocate_SupportsUnboundedPowerOfTwoAspectRatioWithinLimits()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(256, 64);

            Assert.That(allocator.TryAllocate(64, 1, out var first), Is.True);
            Assert.That(allocator.TryAllocate(64, 1, out var second), Is.True);

            Assert.That(first.PageRegion, Is.EqualTo(new RectInt(0, 0, 64, 1)));
            Assert.That(second.PageRegion, Is.EqualTo(new RectInt(0, 1, 64, 1)));
            Assert.That(first.MaxMip, Is.Zero);
            Assert.That(allocator.AllocatedPageCount, Is.EqualTo(128));
        }

        [Test]
        public void TryAllocate_RejectsNonPowerOfTwoOrOversizedDimensions()
        {
            var allocator = new GPUDrivenVirtualTextureAtlasAllocator(16, 8);

            Assert.That(allocator.TryAllocate(3, 2, out _), Is.False);
            Assert.That(allocator.TryAllocate(2, 0, out _), Is.False);
            Assert.That(allocator.TryAllocate(16, 1, out _), Is.False);
            Assert.That(allocator.AllocationCount, Is.Zero);
            Assert.That(allocator.AllocatedPageCount, Is.Zero);
        }
    }
}
