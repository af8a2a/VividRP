using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class GPUDrivenDecalGBufferShaderTests
    {
        [Test]
        public void StandardAndSimpleLitShaders_DeclareSeparateGPUDrivenDecalGBufferPasses()
        {
            var standardLitSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit.shader"));
            var simpleLitSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleLit.shader"));

            AssertDecalGBufferPass(standardLitSource);
            AssertDecalGBufferPass(simpleLitSource);
        }

        [Test]
        public void StandardLitShader_KeepsDefaultGBufferPassFreeOfBindlessDecalRequirements()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit.shader"));
            var defaultPassStart = source.IndexOf("Name \"VividGBuffer\"", StringComparison.Ordinal);
            var decalPassStart = source.IndexOf("Name \"VividGBufferGPUDrivenDecal\"", StringComparison.Ordinal);

            Assert.That(defaultPassStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(decalPassStart, Is.GreaterThan(defaultPassStart));

            var defaultGBufferPass = source.Substring(defaultPassStart, decalPassStart - defaultPassStart);

            Assert.That(defaultGBufferPass, Does.Not.Contain("VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER"));
            Assert.That(defaultGBufferPass, Does.Not.Contain("Bindless.hlsl"));
        }

        [Test]
        public void MaterialGBufferPasses_ApplyGPUDrivenDecalsOnlyBehindDecalKeyword()
        {
            var standardLitSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "StandardLitGBufferPass.hlsl"));
            var simpleLitSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleLitGBufferPass.hlsl"));

            Assert.That(standardLitSource, Does.Contain("#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)"));
            Assert.That(standardLitSource, Does.Contain("ApplyVividGPUDrivenDecalsToGBufferSurfaceData(surfaceData, input.positionWS"));
            Assert.That(simpleLitSource, Does.Contain("#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)"));
            Assert.That(simpleLitSource, Does.Contain("ApplyVividGPUDrivenDecalsToGBufferSurfaceData(surfaceData, input.positionWS"));
        }

        [Test]
        public void SharedDecalGBufferHlsl_UsesClusteredDecalDataAndBindlessBaseNormalSampling()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "GPUDrivenDecalGBuffer.hlsl"));
            var clusteredLightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "ClusteredLighting.hlsl"));

            Assert.That(source, Does.Contain("StructuredBuffer<VividDecalClusterData> _DecalData;"));
            Assert.That(source, Does.Contain("uint baseColorTextureIndex;"));
            Assert.That(source, Does.Contain("uint normalTextureIndex;"));
            Assert.That(source, Does.Contain("uint metallicTextureIndex;"));
            Assert.That(source, Does.Contain("uint roughnessTextureIndex;"));
            Assert.That(source, Does.Contain("float metallic;"));
            Assert.That(source, Does.Contain("float roughness;"));
            Assert.That(clusteredLightingSource, Does.Contain("_ClusteredDecalGridEnabled"));
            Assert.That(source, Does.Contain("VividClusteredLighting::LoadDecalCell"));
            Assert.That(source, Does.Contain("VividClusteredLighting::LoadLightIndex"));
            Assert.That(source, Does.Contain("GetBindlessTexture2D(NonUniformResourceIndex(decal.baseColorTextureIndex))"));
            Assert.That(source, Does.Contain("GetBindlessTexture2D(NonUniformResourceIndex(decal.normalTextureIndex))"));
            Assert.That(source, Does.Contain("GetBindlessTexture2D(NonUniformResourceIndex(decal.metallicTextureIndex))"));
            Assert.That(source, Does.Contain("GetBindlessTexture2D(NonUniformResourceIndex(decal.roughnessTextureIndex))"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_GRAD(baseColorTexture, sampler_LinearClamp, uv, uvDdx, uvDdy)"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_GRAD(normalTexture, sampler_LinearClamp, uv, uvDdx, uvDdy)"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_GRAD(metallicTexture, sampler_LinearClamp, uv, uvDdx, uvDdy).r"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_GRAD(roughnessTexture, sampler_LinearClamp, uv, uvDdx, uvDdy).r"));
            Assert.That(source, Does.Contain("surfaceData.baseColor = lerp"));
            Assert.That(source, Does.Contain("surfaceData.normalWS = normalize(lerp"));
        }

        [Test]
        public void SharedDecalGBufferHlsl_BlendsMetallicAndRoughnessByDecalOpacity()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "GPUDrivenDecalGBuffer.hlsl"));

            Assert.That(source, Does.Contain("float surfacePerceptualRoughness = sqrt(saturate(surfaceData.linearRoughness));"));
            Assert.That(source, Does.Contain("float decalMetallic = SampleVividDecalMetallic(decal, sampleContext.uv, sampleContext.uvDdx, sampleContext.uvDdy);"));
            Assert.That(source, Does.Contain("float decalPerceptualRoughness = SampleVividDecalPerceptualRoughness(decal, sampleContext.uv, sampleContext.uvDdx, sampleContext.uvDdy);"));
            Assert.That(source, Does.Contain("float blendedPerceptualRoughness = lerp(surfacePerceptualRoughness, decalPerceptualRoughness, decalOpacity);"));
            Assert.That(source, Does.Contain("surfaceData.metallic = lerp(surfaceData.metallic, decalMetallic, decalOpacity);"));
            Assert.That(source, Does.Contain("surfaceData.linearRoughness = blendedPerceptualRoughness * blendedPerceptualRoughness;"));
        }

        [Test]
        public void SharedDecalGBufferHlsl_UsesBaseColorAlphaAsOpacityMask()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "GPUDrivenDecalGBuffer.hlsl"));

            Assert.That(source, Does.Contain("struct VividDecalBaseColorSample"));
            Assert.That(source, Does.Contain("result.color = decal.baseColor.rgb * textureSample.rgb;"));
            Assert.That(source, Does.Contain("result.opacity = saturate(decal.baseColor.a * textureSample.a);"));
            Assert.That(source, Does.Contain("float decalOpacity = volumeFade * baseColor.opacity;"));
            Assert.That(source, Does.Contain("if (decalOpacity <= 0.0)"));
            Assert.That(source, Does.Contain("surfaceData.baseColor = lerp(surfaceData.baseColor, baseColor.color, decalOpacity);"));
            Assert.That(source, Does.Contain("if (decal.normalTextureIndex != VIVID_DECAL_INVALID_TEXTURE_INDEX)"));
            Assert.That(source, Does.Contain("decalOpacity));"));
        }

        [Test]
        public void SharedDecalGBufferHlsl_UsesHdrpProjectedPlaneAndExplicitGradients()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "GPUDrivenDecalGBuffer.hlsl"));
            var fadeStart = source.IndexOf("float ComputeVividDecalVolumeFade", StringComparison.Ordinal);
            var nextFunctionStart = source.IndexOf("VividDecalBaseColorSample SampleVividDecalBaseColor", StringComparison.Ordinal);

            Assert.That(fadeStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextFunctionStart, Is.GreaterThan(fadeStart));

            var fadeFunction = source.Substring(fadeStart, nextFunctionStart - fadeStart);

            Assert.That(source, Does.Contain("float3 positionWSDdx = ddx(positionWS);"));
            Assert.That(source, Does.Contain("float3 positionWSDdy = ddy(positionWS);"));
            Assert.That(source, Does.Contain("sampleContext.uv = positionDS.xz + 0.5;"));
            Assert.That(source, Does.Contain("sampleContext.uvDdx = positionDSDdx.xz;"));
            Assert.That(source, Does.Contain("sampleContext.uvDdy = positionDSDdy.xz;"));
            Assert.That(fadeFunction, Does.Contain("0.5 - abs(positionDS.xz)"));
            Assert.That(fadeFunction, Does.Contain("min(edgeDistance.x, edgeDistance.y)"));
            Assert.That(fadeFunction, Does.Contain("clamp(decal.blendDistance, 0.0, 0.5)"));
            Assert.That(source, Does.Not.Contain("positionDS.xy"));
            Assert.That(fadeFunction, Does.Not.Contain("edgeDistance.z"));
        }

        private static void AssertDecalGBufferPass(string shaderSource)
        {
            Assert.That(shaderSource, Does.Contain("Name \"VividGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER 1"));
            Assert.That(shaderSource, Does.Contain("#include_with_pragmas \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/Bindless.hlsl\""));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
