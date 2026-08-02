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
        public void State_ReportsDeferredBlendMode()
        {
            var cameraObject =
                new GameObject("Reference Local Fog Deferred Camera");
            var blendedObject =
                new GameObject("Reference Local Fog Blended Volume");
            var camera = cameraObject.AddComponent<Camera>();
            var blendedFog =
                blendedObject.AddComponent<VividLocalVolumetricFog>();
            try
            {
                var blendedParameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                blendedParameters.priority = int.MaxValue;
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
                    state.unsupportedBlendCount,
                    Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blendedObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void VolumeMaskContract_ResolvesTextureAndMaterialMasks()
        {
            var fogObject =
                new GameObject("Reference Local Fog Texture Mask");
            var fog =
                fogObject.AddComponent<VividLocalVolumetricFog>();
            var mask = new Texture3D(
                1,
                1,
                1,
                TextureFormat.RGBA32,
                false);
            Material material = null;
            try
            {
                var parameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                parameters.maskMode =
                    VividLocalVolumetricFogMaskMode.Texture;
                parameters.volumeMask = mask;
                fog.parameters = parameters;

                Assert.That(
                    fog.TryGetVolumeMask(
                        out var resolvedMask,
                        out var alphaOnly),
                    Is.True);
                Assert.That(resolvedMask, Is.SameAs(mask));
                Assert.That(alphaOnly, Is.False);

                var shader = Shader.Find(
                    "Hidden/VividRP/LocalVolumetricFogVoxelize");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                material.SetTexture("_Mask", mask);
                material.SetFloat("_AlphaOnlyTexture", 1.0f);
                parameters.maskMode =
                    VividLocalVolumetricFogMaskMode.Material;
                parameters.volumeMask = null;
                parameters.materialMask = material;
                fog.parameters = parameters;

                Assert.That(
                    fog.TryGetVolumeMask(
                        out resolvedMask,
                        out alphaOnly),
                    Is.True);
                Assert.That(resolvedMask, Is.SameAs(mask));
                Assert.That(alphaOnly, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fogObject);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        [Test]
        public void State_AssignsExplicitTextureSlotWithoutBindless()
        {
            var cameraObject =
                new GameObject("Reference Local Fog Slot Camera");
            var fogObject =
                new GameObject("Reference Local Fog Slot Volume");
            var camera = cameraObject.AddComponent<Camera>();
            var fog =
                fogObject.AddComponent<VividLocalVolumetricFog>();
            var mask = new Texture3D(
                1,
                1,
                1,
                TextureFormat.RGBA32,
                false);
            try
            {
                var parameters =
                    VividLocalVolumetricFogArtistParameters
                        .CreateDefault();
                parameters.priority = int.MaxValue;
                parameters.anisotropy = 0.731f;
                parameters.maskMode =
                    VividLocalVolumetricFogMaskMode.Texture;
                parameters.volumeMask = mask;
                fog.parameters = parameters;

                var state =
                    ReferencedPathTracingLocalFogState.Resolve(
                        camera,
                        true);
                var maskSlot = Array.IndexOf(
                    state.maskTextures,
                    mask);
                var record = state.records.First(
                    candidate =>
                        Mathf.Approximately(
                            candidate.parameters.x,
                            0.731f));

                Assert.That(maskSlot, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    record.parameters.w,
                    Is.EqualTo(maskSlot));
                Assert.That(
                    ReferencedPathTracingLocalFogState
                        .MaximumMaskTextureSlotCount,
                    Is.EqualTo(16));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fogObject);
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
            var stateSource = File.ReadAllText(
                GetPackageFilePath(
                    "Runtime",
                    "RenderPass",
                    "Core",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathTracingLocalFogState.cs"));
            var passSource = File.ReadAllText(
                GetPackageFilePath(
                    "Runtime",
                    "RenderPass",
                    "Core",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathTracingPass.cs"));

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
                localFogSource,
                Does.Contain(
                    "ReferencedPathtracingSampleLocalFogMask("));
            Assert.That(
                localFogSource,
                Does.Contain("maskValue.a"));
            Assert.That(
                localFogSource,
                Does.Contain("evaluatedPoint.scatteringAlbedo"));
            Assert.That(
                localFogSource,
                Does.Contain("_ReferencedLocalFogMask0"));
            Assert.That(
                localFogSource,
                Does.Contain("_ReferencedLocalFogMask15"));
            Assert.That(
                localFogSource,
                Does.Not.Contain("ResourceDescriptorHeap"));
            Assert.That(
                localFogSource,
                Does.Not.Contain("GetBindlessTexture3D"));
            Assert.That(
                stateSource,
                Does.Contain("maskTexture.updateCount"));
            Assert.That(
                stateSource,
                Does.Contain(
                    "unsupportedProceduralMaterialCount"));
            Assert.That(
                stateSource,
                Does.Contain("maskSlotOverflowCount"));
            Assert.That(
                stateSource,
                Does.Not.Contain("TryGetOrCreateIndex"));
            Assert.That(
                passSource,
                Does.Contain("LocalFogMaskTextureIds"));
            Assert.That(
                passSource,
                Does.Contain("SetRayTracingTextureParam"));
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
