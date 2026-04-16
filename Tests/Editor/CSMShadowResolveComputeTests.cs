using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowResolveComputeTests
    {
        [Test]
        public void CSMShadowResolveCompute_UsesSharedWorldPositionAndNormalDecodingHelpers()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl\""));
            Assert.That(source, Does.Contain("return ComputeWorldSpacePosition(uv, deviceDepth, _CSMInvViewProjMatrix);"));
            Assert.That(source, Does.Contain("float3 normalWS = DecodeVividNormalOct(gbuffer1.xy);"));
            Assert.That(source, Does.Not.Contain("normalize(gbuffer1.xyz * 2.0 - 1.0)"));
        }

        [Test]
        public void CSMShadowResolveCompute_UsesQualityTieredFilteringAndCascadeScaledReceiverBias()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("SamplerComparisonState sampler_CSMShadowAtlas;"));
            Assert.That(source, Does.Contain("int      _CSMShadowQuality;"));
            Assert.That(source, Does.Contain("float    _CSMLightAngularDiameter;"));
            Assert.That(source, Does.Contain("return mul(_CSMViewProjMatrices[cascadeIndex], float4(positionWS, 1.0)).xyz;"));
            Assert.That(source, Does.Contain("float GetCascadeWorldTexelSize(int cascadeIndex)"));
            Assert.That(source, Does.Contain("return (2.0 * cascadeRadius) / max((float)_CSMCascadeResolution, 1.0);"));
            Assert.That(source, Does.Contain("float GetCascadeDepthToWorldScale(int cascadeIndex)"));
            Assert.That(source, Does.Contain("bool IsShadowMapDepthCloser(float sampleDepth, float compareDepth)"));
            Assert.That(source, Does.Contain("float SampleShadowPCFKernel(float2 atlasUV, float depth, int cascadeIndex, int kernelRadius)"));
            Assert.That(source, Does.Contain("float SampleShadowPCF3x3(float2 atlasUV, float depth, int cascadeIndex)"));
            Assert.That(source, Does.Contain("float SampleShadowPCF5x5(float2 atlasUV, float depth, int cascadeIndex)"));
            Assert.That(source, Does.Contain("float SampleShadowPCF7x7(float2 atlasUV, float depth, int cascadeIndex)"));
            Assert.That(source, Does.Contain("float SampleShadowPCSS(float2 atlasUV, float receiverDepth, int cascadeIndex, uint2 pixelCoord)"));
            Assert.That(source, Does.Contain("SampleVogelDiskClumped"));
            Assert.That(source, Does.Contain("SampleVogelDiskUniform"));
            Assert.That(source, Does.Contain("float2 sampleUV = clamp(atlasUV + float2(x, y) * texelSize, cascadeMinUV, cascadeMaxUV);"));
            Assert.That(source, Does.Contain("_CSMShadowAtlas.SampleCmpLevelZero(sampler_CSMShadowAtlas, sampleUV, depth);"));
            Assert.That(source, Does.Contain("_CSMShadowAtlas.Load(int3(AtlasUVToTexelCoord(sampleUV), 0));"));
            Assert.That(source, Does.Contain("if (IsShadowMapDepthCloser(sampleDepth, coordz))"));
            Assert.That(source, Does.Contain("float blockerDistanceWorld = min(abs(averageBlockerDepth - receiverDepth) * depthToWorldScale * 0.9, maxBlockerDistanceWorld);"));
            Assert.That(source, Does.Contain("bool IsWithinShadowMapBounds(float3 shadowCoord)"));
            Assert.That(source, Does.Contain("float receiverNormalBias = _CSMNormalBias * GetCascadeWorldTexelSize(cascadeIndex);"));
            Assert.That(source, Does.Not.Contain("shadowCoord.z = ApplyShadowDepthBias(shadowCoord.z);"));
            Assert.That(source, Does.Contain("if (!IsWithinShadowMapBounds(shadowCoord))"));
            Assert.That(source, Does.Contain("switch (_CSMShadowQuality)"));
            Assert.That(source, Does.Contain("case kCSMShadowQualityLow:"));
            Assert.That(source, Does.Contain("case kCSMShadowQualityMedium:"));
            Assert.That(source, Does.Contain("case kCSMShadowQualityHigh:"));
            Assert.That(source, Does.Contain("case kCSMShadowQualityVeryHigh:"));
            Assert.That(source, Does.Contain("float shadow = EvaluateShadow(atlasUV, shadowCoord.z, cascadeIndex, pixelCoord);"));
            Assert.That(source, Does.Contain("_DirectionalShadowTexture[pixelCoord] = 1.0;"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "CSMShadowResolve.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
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
