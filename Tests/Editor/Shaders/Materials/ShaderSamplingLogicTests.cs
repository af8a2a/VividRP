using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class ShaderSamplingLogicTests
    {
        [Test]
        public void StandardLitInput_UsesKeywordGuards_ForOptionalTextureSampling()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit", "StandardLitInput.hlsl"));

            Assert.That(source, Does.Contain("#if defined(_ALPHATEST_ON)"));
            Assert.That(source, Does.Contain("#if defined(_OPACITYMAP)"));
            Assert.That(source, Does.Contain("#if defined(_NORMALMAP)"));
            Assert.That(source, Does.Contain("#if defined(_METALLICSPECGLOSSMAP)"));
            Assert.That(source, Does.Contain("#if defined(_ROUGHNESSMAP)"));
            Assert.That(source, Does.Contain("defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)"));
            Assert.That(source, Does.Contain("#if defined(_OCCLUSIONMAP)"));
            Assert.That(source, Does.Contain("#if defined(_EMISSION)"));
            Assert.That(source, Does.Contain("#if defined(_CLEARCOAT)"));
            Assert.That(source, Does.Not.Contain("#if _NORMALMAP"));
        }

        [Test]
        public void StandardLitInput_UsesVirtualTextureBaseColorBranch_WhenKeywordIsEnabled()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit", "StandardLitInput.hlsl"));

            Assert.That(source, Does.Contain("#if defined(_VIRTUAL_TEXTURE_BASE_COLOR)"));
            Assert.That(source, Does.Contain("VirtualTexture/VirtualTexture.hlsl"));
            Assert.That(source, Does.Contain("float4 SampleVirtualTextureBase(float2 uv, float4 positionSS)"));
            Assert.That(source, Does.Contain("float4 SampleVirtualTextureBase(float2 uv)"));
            Assert.That(source, Does.Contain("VTMipRange requestedMips = VTComputeRequestedMipRange(uv);"));
            Assert.That(source, Does.Contain("VTResolvedAddress lowerResolved = VTResolveAddress(uv, requestedMips.lowerMip);"));
            Assert.That(source, Does.Contain("VTWriteAccessFeedback(uv, requestedMips.lowerMip, lowerResolved, positionSS);"));
            Assert.That(source, Does.Not.Contain("if (!lowerResolved.resident)"));
            Assert.That(source, Does.Contain("VTWriteFallbackSample(uv, requestedMips.lowerMip, lowerResolved, positionSS);"));
            Assert.That(source, Does.Contain("return VTSampleBaseColor(uv, lowerResolved, upperResolved, requestedMips.blend);"));
        }

        [Test]
        public void StandardLitPassWrappers_UseSharedVividShaderPasses_WithoutLocalVaryingStructs()
        {
            string[] passFiles =
            {
                "StandardLitDepthOnlyPass.hlsl",
                "StandardLitShadowCasterPass.hlsl",
                "StandardLitGBufferPass.hlsl",
                "StandardLitMetaPass.hlsl",
                "StandardLitMotionVectorPass.hlsl",
            };

            foreach (var passFile in passFiles)
            {
                var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit", passFile));

                Assert.That(source, Does.Contain("StandardLitInput.hlsl"), passFile);
                Assert.That(source, Does.Contain("VividShaderPass"), passFile);
                Assert.That(source, Does.Not.Contain("struct Attributes"), passFile);
                Assert.That(source, Does.Not.Contain("struct Varyings"), passFile);
                Assert.That(source, Does.Not.Contain("Varyings Vert("), passFile);
            }
        }

        [Test]
        public void SimpleDeferredLitPass_UsesLoadBasedSampling_ForGBufferAndDepth()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl"));

            Assert.That(source, Does.Contain("uint2 pixelCoord = GetPixelCoord(input);"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer0, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer1, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer2, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer3, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_GBuffer4, pixelCoord)"));
            Assert.That(source, Does.Contain("LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r"));
            Assert.That(source, Does.Not.Contain("SAMPLE_TEXTURE2D_X(_GBuffer0"));
            Assert.That(source, Does.Not.Contain("SAMPLE_TEXTURE2D_X(_DepthTexture"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            Assert.Fail($"Expected source file under '{packageRoots[0]}' or '{packageRoots[1]}'.");
            return null;
        }
    }
}
