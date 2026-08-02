using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingGlobalFogTests
    {
        [Test]
        public void State_ResolvesPhysicalFogParametersAndSignature()
        {
            var volume =
                ScriptableObject.CreateInstance<VividVolumetricFogVolume>();
            try
            {
                volume.active = true;
                volume.enabled.value = true;
                volume.volumetricFog.value = true;
                volume.meanFreePath.value = 250.0f;
                volume.albedo.value =
                    new Color(0.2f, 0.5f, 0.8f, 1.0f);
                volume.baseHeight.value = 100.0f;
                volume.maximumHeight.value = 600.0f;
                volume.maxFogDistance.value = 3200.0f;
                volume.anisotropy.value = 0.65f;
                volume.directionalLightsOnly.value = true;
                volume.globalLightProbeDimmer.value = 0.3f;

                var state =
                    ReferencedPathTracingGlobalFogState.Resolve(
                        volume);

                Assert.That(state.enabled, Is.True);
                Assert.That(
                    state.extinction,
                    Is.EqualTo(1.0f / 250.0f).Within(1e-7f));
                Assert.That(
                    state.scatteringAlbedo.x,
                    Is.EqualTo(0.2f));
                Assert.That(
                    state.scatteringAlbedo.y,
                    Is.EqualTo(0.5f));
                Assert.That(
                    state.scatteringAlbedo.z,
                    Is.EqualTo(0.8f));
                Assert.That(state.baseHeight, Is.EqualTo(100.0f));
                Assert.That(state.maxDistance, Is.EqualTo(3200.0f));
                Assert.That(state.anisotropy, Is.EqualTo(0.65f));
                Assert.That(state.directionalLightsOnly, Is.True);
                Assert.That(
                    state.globalLightProbeDimmer,
                    Is.EqualTo(0.3f));
                Assert.That(
                    state.reciprocalScaleHeight,
                    Is.EqualTo(
                        1.0f
                        / VividVolumetricUtility
                            .ComputeHeightFogScaleHeight(
                                100.0f,
                                600.0f))
                        .Within(1e-7f));

                var originalSignature = state.signature;
                volume.maxFogDistance.value = 1600.0f;
                var changed =
                    ReferencedPathTracingGlobalFogState.Resolve(
                        volume);
                Assert.That(
                    changed.signature,
                    Is.Not.EqualTo(originalSignature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ShaderContract_SamplesBeforeSurfaceAndKeepsPayloadCompact()
        {
            var rayGenerationSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracing.rgen.hlsl"));
            var fogSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingGlobalFog.hlsl"));
            var commonSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingCommon.hlsl"));

            var fogSampleIndex = rayGenerationSource.IndexOf(
                "ReferencedPathtracingSampleGlobalFog(",
                StringComparison.Ordinal);
            var surfaceTraceIndex = rayGenerationSource.IndexOf(
                "TraceReferencedPathtracingSurface(",
                fogSampleIndex,
                StringComparison.Ordinal);
            Assert.That(fogSampleIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(surfaceTraceIndex, Is.GreaterThan(fogSampleIndex));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "surfaceRay.TMax = min("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingSampleUnifiedNEECandidate("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateGlobalFogTransmittance("));
            Assert.That(
                fogSource,
                Does.Contain(
                    "ReferencedPathtracingIntegrateGlobalFogDensity("));
            Assert.That(
                fogSource,
                Does.Contain(
                    "ReferencedPathtracingInvertGlobalFogDensity("));
            Assert.That(
                fogSource,
                Does.Contain(
                    "ReferencedPathtracingSampleGlobalFogPhase("));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "struct ReferencedPathtracingVisibilityPayload"));
            Assert.That(
                commonSource,
                Does.Contain("uint hit;"));
        }

        [Test]
        public void ExponentialHeightFog_OpticalLengthAndInverseAgree()
        {
            const double baseHeight = 0.0;
            const double scaleHeight = 100.0;
            const double originHeight = -50.0;
            const double verticalDirection = 1.0;
            const double endDistance = 200.0;

            var opticalLength = IntegrateDensity(
                originHeight,
                verticalDirection,
                0.0,
                endDistance,
                baseHeight,
                scaleHeight);
            var expected =
                50.0
                + scaleHeight
                    * (1.0 - Math.Exp(-1.5));
            Assert.That(
                opticalLength,
                Is.EqualTo(expected).Within(1e-9));

            const double expectedDistance = 125.0;
            var targetOpticalLength = IntegrateDensity(
                originHeight,
                verticalDirection,
                0.0,
                expectedDistance,
                baseHeight,
                scaleHeight);
            var reconstructedDistance = InvertDensity(
                originHeight,
                verticalDirection,
                0.0,
                endDistance,
                targetOpticalLength,
                baseHeight,
                scaleHeight);
            Assert.That(
                reconstructedDistance,
                Is.EqualTo(expectedDistance).Within(1e-9));
        }

        private static double IntegrateDensity(
            double originHeight,
            double verticalDirection,
            double startDistance,
            double endDistance,
            double baseHeight,
            double scaleHeight)
        {
            var crossingDistance =
                (baseHeight - originHeight)
                / verticalDirection;
            if (crossingDistance > startDistance
                && crossingDistance < endDistance)
            {
                return IntegrateSingleRegion(
                        originHeight,
                        verticalDirection,
                        startDistance,
                        crossingDistance,
                        baseHeight,
                        scaleHeight)
                    + IntegrateSingleRegion(
                        originHeight,
                        verticalDirection,
                        crossingDistance,
                        endDistance,
                        baseHeight,
                        scaleHeight);
            }

            return IntegrateSingleRegion(
                originHeight,
                verticalDirection,
                startDistance,
                endDistance,
                baseHeight,
                scaleHeight);
        }

        private static double IntegrateSingleRegion(
            double originHeight,
            double verticalDirection,
            double startDistance,
            double endDistance,
            double baseHeight,
            double scaleHeight)
        {
            var distance = endDistance - startDistance;
            var startHeight =
                originHeight + verticalDirection * startDistance;
            var endHeight =
                originHeight + verticalDirection * endDistance;
            if (Math.Max(startHeight, endHeight) <= baseHeight)
                return distance;

            var startDensity = Math.Exp(
                -Math.Max(startHeight - baseHeight, 0.0)
                / scaleHeight);
            var verticalRate =
                verticalDirection / scaleHeight;
            return startDensity
                * (1.0 - Math.Exp(-verticalRate * distance))
                / verticalRate;
        }

        private static double InvertDensity(
            double originHeight,
            double verticalDirection,
            double startDistance,
            double endDistance,
            double targetOpticalLength,
            double baseHeight,
            double scaleHeight)
        {
            var crossingDistance =
                (baseHeight - originHeight)
                / verticalDirection;
            if (crossingDistance > startDistance
                && crossingDistance < endDistance)
            {
                var firstLength = IntegrateSingleRegion(
                    originHeight,
                    verticalDirection,
                    startDistance,
                    crossingDistance,
                    baseHeight,
                    scaleHeight);
                if (targetOpticalLength <= firstLength)
                {
                    return startDistance
                        + targetOpticalLength;
                }

                startDistance = crossingDistance;
                targetOpticalLength -= firstLength;
            }

            var startHeight =
                originHeight + verticalDirection * startDistance;
            if (startHeight <= baseHeight
                && originHeight
                    + verticalDirection * endDistance
                    <= baseHeight)
            {
                return startDistance + targetOpticalLength;
            }

            var startDensity = Math.Exp(
                -Math.Max(startHeight - baseHeight, 0.0)
                / scaleHeight);
            var verticalRate =
                verticalDirection / scaleHeight;
            return startDistance
                - Math.Log(
                    1.0
                    - targetOpticalLength
                        * verticalRate
                        / startDensity)
                    / verticalRate;
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
