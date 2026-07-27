using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingVolumetricCloudTests
    {
        [Test]
        public void ShaderContract_CloudEventsPrecedeSurfaceAndShareVisibility()
        {
            var rayGenerationSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracing.rgen.hlsl"));
            var cloudSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingCloud.hlsl"));

            var cloudSampleIndex = rayGenerationSource.IndexOf(
                "ReferencedPathtracingSampleCloudMedium(",
                StringComparison.Ordinal);
            var surfaceMissIndex = rayGenerationSource.IndexOf(
                "if (payload.hit == 0u)",
                StringComparison.Ordinal);
            Assert.That(cloudSampleIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(surfaceMissIndex, Is.GreaterThan(cloudSampleIndex));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateCloudTransmittance("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("cloudEventFirst"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateCloudPhasePdf("));
            Assert.That(
                cloudSource,
                Does.Contain(
                    "struct ReferencedPathtracingCloudMaterialSample"));
            Assert.That(
                cloudSource,
                Does.Contain(
                    "ReferencedPathtracingIntersectCloudShell("));
            Assert.That(
                cloudSource,
                Does.Contain(
                    "kReferencedCloudMaximumTrackingSteps = 1024u"));
            Assert.That(
                cloudSource,
                Does.Contain(
                    "REFERENCED_CLOUD_SHADOW_REFERENCE_SAMPLE_COUNT"));
            Assert.That(
                ReferencedPathTracingAtmosphereState.ContractVersion,
                Is.EqualTo(6));
            Assert.That(
                ReferencedPathTracingSamplingContract.Version,
                Is.EqualTo(4));
        }

        [Test]
        public void DisabledClouds_IgnoreCloudParametersAndPreserveA3Signature()
        {
            var settings =
                ScriptableObject.CreateInstance<
                    ReferencedPathTracingSettingsVolume>();
            var skyVolume =
                ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                settings.active = true;
                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
                settings.referenceClouds.value = false;
                skyVolume.airMaximumAltitude.value = 12000.0f;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.PhysicallyBased,
                    skyHash = 71
                };

                var disabled =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        null,
                        settings);
                settings.referenceCloudCoverage.value = 0.9f;
                settings.referenceCloudExtinction.value = 0.005f;
                var disabledChanged =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        null,
                        settings);
                settings.referenceClouds.value = true;
                var enabled =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        null,
                        settings);

                Assert.That(disabled.cloudsActive, Is.False);
                Assert.That(
                    disabledChanged.signature,
                    Is.EqualTo(disabled.signature));
                Assert.That(enabled.cloudsActive, Is.True);
                Assert.That(
                    enabled.signature,
                    Is.Not.EqualTo(disabled.signature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(skyVolume);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void EnergyCompensation_IsExplicitlyMarkedAsBiasedMetadata()
        {
            var settings =
                ScriptableObject.CreateInstance<
                    ReferencedPathTracingSettingsVolume>();
            var skyVolume =
                ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                settings.active = true;
                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
                settings.referenceClouds.value = true;
                settings.referenceCloudMultipleScatteringMode.value =
                    ReferencedPathTracingCloudMultipleScatteringMode
                        .EnergyCompensation;
                skyVolume.airMaximumAltitude.value = 12000.0f;
                var state =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        new VividSkyData
                        {
                            activeSkyType = SkyType.PhysicallyBased
                        },
                        skyVolume,
                        null,
                        null,
                        settings);
                var metadata =
                    ReferencedPathTracingAtmosphereMetadata.Capture(state);

                Assert.That(
                    metadata.cloudShadowUsesDeterministicApproximation,
                    Is.True);
                Assert.That(
                    metadata.cloudMultipleScatteringMode,
                    Is.EqualTo(
                        ReferencedPathTracingCloudMultipleScatteringMode
                            .EnergyCompensation));
                Assert.That(
                    metadata.cloudTransportUsesBiasedApproximation,
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(skyVolume);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void HenyeyGreensteinCloudPhase_NormalizesToUnitSphere()
        {
            const int sampleCount = 65536;
            const double anisotropy = 0.7;
            var integral = 0.0;
            var step = 2.0 / sampleCount;
            for (var sampleIndex = 0;
                sampleIndex < sampleCount;
                sampleIndex++)
            {
                var cosineTheta =
                    -1.0 + (sampleIndex + 0.5) * step;
                integral +=
                    EvaluateHenyeyGreensteinPhase(
                        cosineTheta,
                        anisotropy);
            }

            integral *= 2.0 * Math.PI * step;
            Assert.That(
                integral,
                Is.EqualTo(1.0).Within(1e-5));
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
