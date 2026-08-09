using System;
using NUnit.Framework;
using UnityEditor;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingParticipatingMediumTests
    {

        [Test]
        public void RayleighAndHenyeyGreensteinPhase_NormalizeToUnitSphere()
        {
            const int sampleCount = 65536;
            const double anisotropy = 0.8;
            var rayleighIntegral = 0.0;
            var mieIntegral = 0.0;
            var step = 2.0 / sampleCount;
            for (var sampleIndex = 0;
                sampleIndex < sampleCount;
                sampleIndex++)
            {
                var cosineTheta =
                    -1.0 + (sampleIndex + 0.5) * step;
                rayleighIntegral +=
                    EvaluateRayleighPhase(cosineTheta);
                mieIntegral +=
                    EvaluateHenyeyGreensteinPhase(
                        cosineTheta,
                        anisotropy);
            }

            rayleighIntegral *= 2.0 * Math.PI * step;
            mieIntegral *= 2.0 * Math.PI * step;
            Assert.That(
                rayleighIntegral,
                Is.EqualTo(1.0).Within(1e-6));
            Assert.That(
                mieIntegral,
                Is.EqualTo(1.0).Within(1e-5));
        }

        [Test]
        public void HeroChannelWeights_ReconstructRgbNoCollisionAndScatter()
        {
            var transmittance =
                new Spectrum(0.31, 0.57, 0.83);
            var phaseScattering =
                new Spectrum(1.7e-6, 2.9e-6, 4.1e-6);
            var noCollisionEstimate = default(Spectrum);
            var scatterEstimate = default(Spectrum);

            for (var heroChannel = 0;
                heroChannel < 3;
                heroChannel++)
            {
                var heroTransmittance =
                    transmittance[heroChannel];
                var noCollisionWeight =
                    transmittance / heroTransmittance;
                noCollisionEstimate +=
                    noCollisionWeight
                    * heroTransmittance
                    / 3.0;

                var heroPhaseScattering =
                    phaseScattering[heroChannel];
                var scatterProbabilityDensity =
                    heroTransmittance
                    * heroPhaseScattering;
                var scatterWeight =
                    noCollisionWeight
                    * phaseScattering
                    / heroPhaseScattering;
                scatterEstimate +=
                    scatterWeight
                    * scatterProbabilityDensity
                    / 3.0;
            }

            AssertSpectrumEqual(
                noCollisionEstimate,
                transmittance,
                1e-12);
            AssertSpectrumEqual(
                scatterEstimate,
                transmittance * phaseScattering,
                1e-18);
        }

        private static double EvaluateRayleighPhase(
            double cosineTheta)
        {
            return 3.0
                / (16.0 * Math.PI)
                * (1.0 + cosineTheta * cosineTheta);
        }

        private static double EvaluateHenyeyGreensteinPhase(
            double cosineTheta,
            double anisotropy)
        {
            var anisotropySquared =
                anisotropy * anisotropy;
            var denominator =
                1.0
                + anisotropySquared
                - 2.0 * anisotropy * cosineTheta;
            return (1.0 - anisotropySquared)
                / (4.0
                    * Math.PI
                    * denominator
                    * Math.Sqrt(denominator));
        }

        private static void AssertSpectrumEqual(
            Spectrum actual,
            Spectrum expected,
            double tolerance)
        {
            Assert.That(
                actual.x,
                Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(
                actual.y,
                Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(
                actual.z,
                Is.EqualTo(expected.z).Within(tolerance));
        }

        private readonly struct Spectrum
        {
            internal Spectrum(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            internal double x { get; }
            internal double y { get; }
            internal double z { get; }

            internal double this[int index] =>
                index == 0 ? x : (index == 1 ? y : z);

            public static Spectrum operator +(
                Spectrum first,
                Spectrum second)
            {
                return new Spectrum(
                    first.x + second.x,
                    first.y + second.y,
                    first.z + second.z);
            }

            public static Spectrum operator *(
                Spectrum first,
                Spectrum second)
            {
                return new Spectrum(
                    first.x * second.x,
                    first.y * second.y,
                    first.z * second.z);
            }

            public static Spectrum operator *(
                Spectrum value,
                double scalar)
            {
                return new Spectrum(
                    value.x * scalar,
                    value.y * scalar,
                    value.z * scalar);
            }

            public static Spectrum operator /(
                Spectrum value,
                double scalar)
            {
                return new Spectrum(
                    value.x / scalar,
                    value.y / scalar,
                    value.z / scalar);
            }
        }
    }
}
