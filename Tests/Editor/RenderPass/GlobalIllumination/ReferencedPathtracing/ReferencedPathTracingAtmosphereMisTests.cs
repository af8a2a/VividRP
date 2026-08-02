using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingAtmosphereMisTests
    {
        [Test]
        public void ShaderContract_ConnectsSunGroundAndAtmosphereWithBidirectionalMis()
        {
            var rayGenerationSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracing.rgen.hlsl"));
            var atmosphereSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingAtmosphere.hlsl"));
            var samplingSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingSampling.hlsl"));

            Assert.That(
                atmosphereSource,
                Does.Contain(
                    "ReferencedPathtracingSampleAtmosphereSun("));
            Assert.That(
                atmosphereSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereSunDiskRadiance("));
            Assert.That(
                atmosphereSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmospherePhasePdf("));
            Assert.That(
                atmosphereSource,
                Does.Contain(
                    "ReferencedPathtracingSampleAtmosphereGround("));
            Assert.That(
                atmosphereSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereGroundDirectWeight("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "previousReferenceSunReachable"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "shadowInterval.hitsGround != 0u"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetLightEstimatorWeight("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetBsdfEstimatorWeight("));
            Assert.That(
                samplingSource,
                Does.Contain(
                    "kReferencedPathtracingAtmosphereSunDimensionOffset = 12u"));
            Assert.That(
                ReferencedPathTracingAtmosphereState.ContractVersion,
                Is.EqualTo(7));
            Assert.That(
                ReferencedPathTracingSamplingContract.Version,
                Is.EqualTo(8));
        }

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

        private static string GetPackageFilePath(
            params string[] relativeParts)
        {
            var packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(
                        ReferencedPathTracingEnvironmentSamplingPass)
                        .Assembly);
            Assert.That(packageInfo, Is.Not.Null);
            return Path.Combine(
                packageInfo.resolvedPath,
                Path.Combine(relativeParts));
        }
    }
}
