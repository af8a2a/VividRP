using System;
using NUnit.Framework;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SdfRadiosityBudgetTests
    {
        [TestCase(37, 0, 16, 128, 37)]
        [TestCase(37, 16, 16, 128, 21)]
        [TestCase(37, 32, 16, 0, 5)]
        [TestCase(37, 37, 16, 128, 0)]
        [TestCase(37, 40, 16, 0, 0)]
        [TestCase(128, 0, 16, 128, 128)]
        [TestCase(129, 0, 16, 128, 16)]
        [TestCase(4096, 4090, 64, 128, 6)]
        [TestCase(4096, 0, 0, 0, 1)]
        [TestCase(0, 0, 16, 128, 0)]
        public void UpdateBudget_RespectsSceneLimitAndRemainingCards(
            int cards, int cursor, int budget, int fullUpdateLimit, int expected)
        {
            Assert.That(SdfDiffuseGatherPass.GetRadiosityUpdateCount(cards, cursor, budget, fullUpdateLimit),
                Is.EqualTo(expected));
        }

        [Test]
        public void UpdateBudget_ChangingBudgetContinuesAnUnfinishedSweep()
        {
            int cursor = SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, 0, 16, 0);
            cursor += SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, cursor, 8, 0);
            cursor += SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, cursor, 8, 128);
            Assert.That(cursor, Is.EqualTo(37));
        }

        [Test]
        public void UpdateBudget_StableCallsDoNotAllocate()
        {
            int total = 0;
            for (int i = 0; i < 256; i++)
                total += SdfDiffuseGatherPass.GetRadiosityUpdateCount(4096, i, 16, 128);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
                total += SdfDiffuseGatherPass.GetRadiosityUpdateCount(4096, i, 16, 128);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);
        }
    }
}
