using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class HaltonJitterTests
    {
        [Test]
        public void Halton_Base2_ReturnsExpectedSequence()
        {
            Assert.That(HaltonJitter.Halton(1, 2), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(HaltonJitter.Halton(2, 2), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(HaltonJitter.Halton(3, 2), Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(HaltonJitter.Halton(4, 2), Is.EqualTo(0.125f).Within(1e-6f));
        }

        [Test]
        public void Halton_Base3_ReturnsExpectedSequence()
        {
            Assert.That(HaltonJitter.Halton(1, 3), Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(HaltonJitter.Halton(2, 3), Is.EqualTo(2f / 3f).Within(1e-6f));
            Assert.That(HaltonJitter.Halton(3, 3), Is.EqualTo(1f / 9f).Within(1e-6f));
        }

        [Test]
        public void Halton_IndexZero_ReturnsZero()
        {
            Assert.That(HaltonJitter.Halton(0, 2), Is.EqualTo(0f));
            Assert.That(HaltonJitter.Halton(0, 3), Is.EqualTo(0f));
        }

        [Test]
        public void Get_ReturnsValuesInExpectedRange()
        {
            for (int i = 0; i < 64; i++)
            {
                var jitter = HaltonJitter.Get(i, 8);
                Assert.That(jitter.x, Is.InRange(-0.5f, 0.5f), $"Frame {i}: x out of range");
                Assert.That(jitter.y, Is.InRange(-0.5f, 0.5f), $"Frame {i}: y out of range");
            }
        }

        [Test]
        public void Get_WrapsAtSampleCount()
        {
            var jitter0 = HaltonJitter.Get(0, 8);
            var jitter8 = HaltonJitter.Get(8, 8);

            Assert.That(jitter8.x, Is.EqualTo(jitter0.x).Within(1e-6f));
            Assert.That(jitter8.y, Is.EqualTo(jitter0.y).Within(1e-6f));
        }

        [Test]
        public void Get_ProducesDistinctSamplesWithinSequence()
        {
            var samples = new Vector2[8];
            for (int i = 0; i < 8; i++)
                samples[i] = HaltonJitter.Get(i, 8);

            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    Assert.That(Vector2.Distance(samples[i], samples[j]), Is.GreaterThan(1e-6f),
                        $"Samples {i} and {j} are identical");
                }
            }
        }

        [Test]
        public void Get_HandlesSampleCountOfOne()
        {
            var jitter = HaltonJitter.Get(0, 1);
            Assert.That(jitter.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(jitter.y, Is.EqualTo(HaltonJitter.Halton(1, 3) - 0.5f).Within(1e-6f));
        }

        [Test]
        public void Get_HandlesZeroSampleCount_ClampsToOne()
        {
            Assert.DoesNotThrow(() => HaltonJitter.Get(0, 0));
        }
    }
}
