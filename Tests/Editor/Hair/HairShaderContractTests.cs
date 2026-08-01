using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class HairShaderContractTests
    {
        [Test]
        public void HairShader_ImportsWithoutCompilerMessages()
        {
            Shader shader = Shader.Find("VividRP/Material/Hair");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.passCount, Is.EqualTo(2));

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            Assert.That(
                messages,
                Is.Empty,
                string.Join(
                    "\n",
                    messages.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));
        }

        [Test]
        public void HairShader_DeclaresReferenceAndGBufferRayTracingPasses()
        {
            string source = ReadPackageFile(
                "Shaders",
                "Material",
                "Hair",
                "Hair.shader");

            Assert.That(
                source,
                Does.Contain("Shader \"VividRP/Material/Hair\""));
            Assert.That(
                source,
                Does.Contain("Name \"ReferencedPathtracingDXR\""));
            Assert.That(
                source,
                Does.Contain("Name \"RaytracingGBufferDXR\""));
            Assert.That(
                source,
                Does.Contain("HairReferencedPathtracing.hlsl"));
            Assert.That(
                source,
                Does.Contain("HairRaytracingGBuffer.hlsl"));
        }

        [Test]
        public void HairReferencePass_UsesChiangWithoutSurfaceCosineOrMediumTransition()
        {
            string source = ReadPackageFile(
                "Shaders",
                "Material",
                "ShaderPass",
                "HairReferencedPathtracing.hlsl");

            Assert.That(source, Does.Contain("VividHairEvaluateChiang("));
            Assert.That(source, Does.Contain("VividHairSampleChiang("));
            Assert.That(
                source,
                Does.Contain(
                    "result.nextThroughputWeight = max(throughputWeight, 0.0);"));
            Assert.That(source, Does.Contain("sampledValue / sampledPdf"));
            Assert.That(source, Does.Contain("result.nextLobeClass = 2u;"));
            Assert.That(source, Does.Contain("result.mediumTransition = 0;"));
            Assert.That(source, Does.Contain("result.isStrand = 1u;"));
            Assert.That(source, Does.Not.Contain("NdotL"));
        }

        [Test]
        public void HairGeometry_ReconstructsDotsCenterlineAndTaperedBody()
        {
            string source = ReadPackageFile(
                "Shaders",
                "Material",
                "Hair",
                "HairGeometry.hlsl");

            Assert.That(
                source,
                Does.Contain("VividHairIntersectTaperedSegmentBody("));
            Assert.That(
                source,
                Does.Contain("kVertexAttributeTexCoord1"));
            Assert.That(
                source,
                Does.Contain("segmentStartOS = positionsOS[0]"));
            Assert.That(
                source,
                Does.Contain("segmentEndOS = positionsOS[2]"));
            Assert.That(
                source,
                Does.Contain("geometry.radius = max(length(positionWS - centerlinePositionWS)"));
        }

        [Test]
        public void PathTracer_ReservesIndependentHairDimensionAndRadiusAwareOffset()
        {
            string sampling = ReadPackageFile(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingSampling.hlsl");
            string rayGeneration = ReadPackageFile(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl");

            Assert.That(
                sampling,
                Does.Contain(
                    "kReferencedPathtracingHairBsdfExtraDimensionOffset = 17u"));
            Assert.That(
                rayGeneration,
                Does.Contain("payloadInput.hairBsdfExtraRandom"));
            Assert.That(
                rayGeneration,
                Does.Contain("2.0 * max(strandRadius, 0.0)"));
            Assert.That(
                rayGeneration,
                Does.Contain("payload.isStrand"));
        }

        [Test]
        public void RaytracingGBuffer_AllowsMaterialSuppliedHairAlbedoGuides()
        {
            string common = ReadPackageFile(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "RaytracingGBufferCommon.hlsl");
            string rayGeneration = ReadPackageFile(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "RaytracingGBuffer.rgen.hlsl");
            string hairGBuffer = ReadPackageFile(
                "Shaders",
                "Material",
                "ShaderPass",
                "HairRaytracingGBuffer.hlsl");

            Assert.That(common, Does.Contain("uint materialAlbedoValid;"));
            Assert.That(
                rayGeneration,
                Does.Contain("payload.materialAlbedoValid != 0u"));
            Assert.That(
                hairGBuffer,
                Does.Contain("payload.diffuseAlbedo = baseColor;"));
            Assert.That(
                hairGBuffer,
                Does.Contain(
                    "payload.specularAlbedo = VividHairGetSpecularF0();"));
        }

        [Test]
        public void HairVendorFiles_PreservePerFileMitNoticeAndSourceRecord()
        {
            string chiang = ReadPackageFile(
                "Shaders",
                "Material",
                "Hair",
                "Vendor",
                "RTXCR",
                "HairChiangBSDF.hlsli");
            string notice = ReadPackageFile(
                "Shaders",
                "Material",
                "Hair",
                "Vendor",
                "RTXCR",
                "NOTICE.md");

            Assert.That(
                chiang,
                Does.Contain("Permission is hereby granted, free of charge"));
            Assert.That(
                notice,
                Does.Contain("2bd10cff0824cbd18195339d4dc6987c0fe5a5bc"));
            Assert.That(
                notice,
                Does.Contain("RTXCR geometry and sample glue"));
        }

        private static string ReadPackageFile(params string[] parts)
        {
            string customPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "Custom_URP"));
            if (Directory.Exists(customPath))
                return File.ReadAllText(Path.Combine(
                    customPath,
                    Path.Combine(parts)));

            string vividPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP"));
            if (Directory.Exists(vividPath))
                return File.ReadAllText(Path.Combine(
                    vividPath,
                    Path.Combine(parts)));

            string legacyPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp"));
            return File.ReadAllText(Path.Combine(
                legacyPath,
                Path.Combine(parts)));
        }
    }
}
