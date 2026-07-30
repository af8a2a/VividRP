using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingLocalFogTests
    {
        [Test]
        public void State_BuildsStableAdditiveAnalyticFogRecord()
        {
            var cameraObject =
                new GameObject("Reference Local Fog Test Camera");
            var fogObject =
                new GameObject("Reference Local Fog Test Volume");
            var camera = cameraObject.AddComponent<Camera>();
            var fog =
                fogObject.AddComponent<VividLocalVolumetricFog>();
            try
            {
                fogObject.transform.position =
                    new Vector3(4.0f, 5.0f, 6.0f);
                var parameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                parameters.albedo =
                    new Color(0.2f, 0.4f, 0.8f, 1.0f);
                parameters.meanFreePath = 10.0f;
                parameters.priority = int.MaxValue;
                parameters.anisotropy = 0.45f;
                parameters.blendingMode =
                    VividLocalVolumetricFogBlendingMode.Additive;
                parameters.maskMode =
                    VividLocalVolumetricFogMaskMode.None;
                parameters.falloffMode =
                    VividLocalVolumetricFogFalloffMode.Exponential;
                fog.parameters = parameters;

                var state =
                    ReferencedPathTracingLocalFogState.Resolve(
                        camera,
                        true);
                var record = state.records.First(
                    candidate =>
                        Mathf.Approximately(
                            candidate.scatteringExtinction.w,
                            0.1f)
                        && Mathf.Approximately(
                            candidate.parameters.x,
                            0.45f));

                Assert.That(state.count, Is.GreaterThanOrEqualTo(1));
                Assert.That(
                    VividLocalVolumetricFogEngineData.Stride,
                    Is.EqualTo(160));
                Assert.That(
                    record.scatteringExtinction.x,
                    Is.EqualTo(0.02f).Within(1e-6f));
                Assert.That(
                    record.scatteringExtinction.y,
                    Is.EqualTo(0.04f).Within(1e-6f));
                Assert.That(
                    record.scatteringExtinction.z,
                    Is.EqualTo(0.08f).Within(1e-6f));
                Assert.That(
                    record.parameters.x,
                    Is.EqualTo(0.45f).Within(1e-6f));
                Assert.That(
                    record.parameters.y,
                    Is.EqualTo(
                        (float)VividLocalVolumetricFogBlendingMode
                            .Additive));
                Assert.That(
                    record.textureScaleOffset0,
                    Is.EqualTo(Vector4.zero));
                Assert.That(
                    record.textureScaleOffset1.w,
                    Is.EqualTo(
                        (float)VividLocalVolumetricFogFalloffMode
                            .Exponential));

                var originalSignature = state.signature;
                fogObject.transform.position += Vector3.right;
                var changed =
                    ReferencedPathTracingLocalFogState.Resolve(
                        camera,
                        true);
                Assert.That(
                    changed.signature,
                    Is.Not.EqualTo(originalSignature));
                Assert.That(
                    ReferencedPathTracingLocalFogState.Resolve(
                            camera,
                            false)
                        .count,
                    Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fogObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void State_ReportsDeferredMaskAndBlendModes()
        {
            var cameraObject =
                new GameObject("Reference Local Fog Deferred Camera");
            var maskedObject =
                new GameObject("Reference Local Fog Masked Volume");
            var blendedObject =
                new GameObject("Reference Local Fog Blended Volume");
            var mask = new Texture3D(
                1,
                1,
                1,
                TextureFormat.R8,
                false);
            var camera = cameraObject.AddComponent<Camera>();
            var maskedFog =
                maskedObject.AddComponent<VividLocalVolumetricFog>();
            var blendedFog =
                blendedObject.AddComponent<VividLocalVolumetricFog>();
            try
            {
                var maskedParameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                maskedParameters.priority = int.MaxValue;
                maskedParameters.maskMode =
                    VividLocalVolumetricFogMaskMode.Texture;
                maskedParameters.volumeMask = mask;
                maskedFog.parameters = maskedParameters;

                var blendedParameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                blendedParameters.priority = int.MaxValue - 1;
                blendedParameters.maskMode =
                    VividLocalVolumetricFogMaskMode.None;
                blendedParameters.blendingMode =
                    VividLocalVolumetricFogBlendingMode.Overwrite;
                blendedFog.parameters = blendedParameters;

                var state =
                    ReferencedPathTracingLocalFogState.Resolve(
                        camera,
                        true);
                Assert.That(
                    state.unsupportedMaskCount,
                    Is.GreaterThanOrEqualTo(1));
                Assert.That(
                    state.unsupportedBlendCount,
                    Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(maskedObject);
                UnityEngine.Object.DestroyImmediate(blendedObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        [Test]
        public void ShaderContract_TracksObbsBeforeSurfaceAndAttenuatesVisibility()
        {
            var rayGenerationSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracing.rgen.hlsl"));
            var localFogSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingLocalFog.hlsl"));
            var commonSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingCommon.hlsl"));

            var localFogSampleIndex = rayGenerationSource.IndexOf(
                "ReferencedPathtracingSampleLocalFog(",
                StringComparison.Ordinal);
            var surfaceTraceIndex = rayGenerationSource.IndexOf(
                "TraceReferencedPathtracingSurface(",
                localFogSampleIndex,
                StringComparison.Ordinal);
            Assert.That(
                localFogSampleIndex,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                surfaceTraceIndex,
                Is.GreaterThan(localFogSampleIndex));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateLocalFogTransmittance("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("localFogEventFirst"));
            Assert.That(
                localFogSource,
                Does.Contain(
                    "ReferencedPathtracingIntersectLocalFog("));
            Assert.That(
                localFogSource,
                Does.Contain(
                    "kReferencedPathtracingLocalFogMaximumTrackingStepCount"));
            Assert.That(
                localFogSource,
                Does.Contain(
                    "ReferencedPathtracingSampleHenyeyGreensteinPhase("));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10"));
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
