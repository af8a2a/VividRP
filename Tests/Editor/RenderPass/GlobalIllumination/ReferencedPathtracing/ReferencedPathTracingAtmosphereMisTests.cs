using System;
using NUnit.Framework;
using UnityEditor;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingAtmosphereMisTests
    {

        [Test]
        public void FiniteSunDisk_RadianceIntegratesToDirectionalIlluminance()
        {
            const double angularDiameterDegrees = 0.54;
            const double illuminance = 120000.0;
            var angularRadius =
                0.5
                * angularDiameterDegrees
                * Math.PI
                / 180.0;
            var solidAngle =
                2.0
                * Math.PI
                * (1.0 - Math.Cos(angularRadius));
            var solidAnglePdf = 1.0 / solidAngle;
            var diskRadiance =
                illuminance * solidAnglePdf;

            Assert.That(solidAngle, Is.GreaterThan(0.0));
            Assert.That(
                diskRadiance * solidAngle,
                Is.EqualTo(illuminance).Within(1e-8));
        }

        [TestCase(0.001, 10.0)]
        [TestCase(0.25, 0.75)]
        [TestCase(10.0, 0.001)]
        public void PowerHeuristic_SunAndPathWeightsAreComplementary(
            double pathPdf,
            double sunPdf)
        {
            var pathWeight =
                PowerHeuristic(pathPdf, sunPdf);
            var sunWeight =
                PowerHeuristic(sunPdf, pathPdf);

            Assert.That(pathWeight, Is.InRange(0.0, 1.0));
            Assert.That(sunWeight, Is.InRange(0.0, 1.0));
            Assert.That(
                pathWeight + sunWeight,
                Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void LambertGround_CosineIntegralReturnsGroundAlbedo()
        {
            const int sampleCount = 65536;
            const double albedo = 0.37;
            var integral = 0.0;
            var cosineStep = 1.0 / sampleCount;
            for (var sampleIndex = 0;
                sampleIndex < sampleCount;
                sampleIndex++)
            {
                var cosineTheta =
                    (sampleIndex + 0.5) * cosineStep;
                integral +=
                    (albedo / Math.PI)
                    * cosineTheta;
            }

            integral *=
                2.0
                * Math.PI
                * cosineStep;
            Assert.That(
                integral,
                Is.EqualTo(albedo).Within(1e-10));
        }

        private static double PowerHeuristic(
            double firstPdf,
            double secondPdf)
        {
            var firstSquared = firstPdf * firstPdf;
            var secondSquared = secondPdf * secondPdf;
            return firstSquared
                / (firstSquared + secondSquared);
        }
    }
}
