using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyHdrpPortTests
    {
        [Test]
        public void CopiedHdrpSkyShaderChain_UsesLocalIncludes()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSky.shader"));
            var renderingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl"));
            var commonSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyCommon.hlsl"));
            var skyScatterSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScatteringSky.hlsl"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/Sky/PbrSky\""));
            Assert.That(shaderSource, Does.Contain("Tags{ \"RenderPipeline\" = \"VividRenderPipeline\" }"));
            Assert.That(shaderSource, Does.Contain("#include \"PhysicallyBasedSkyRendering.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(shaderSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(renderingSource, Does.Contain("#include \"LightDefinition.cs.hlsl\""));
            Assert.That(renderingSource, Does.Contain("#include \"ShaderVariablesCompat.hlsl\""));
            Assert.That(renderingSource, Does.Contain("#include \"../AtmosphericScattering/AtmosphericScatteringSky.hlsl\""));
            Assert.That(renderingSource, Does.Contain("#include \"CookieSampling.hlsl\""));
            Assert.That(renderingSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(commonSource, Does.Contain("Packages/com.af8a2a.vividrp/Shaders/Core/Public/ShaderVariablesGlobal.hlsl"));
            Assert.That(commonSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(skyScatterSource, Does.Contain("void EvaluatePbrAtmosphere("));
            Assert.That(skyScatterSource, Does.Contain("StructuredBuffer<CelestialBodyData> _CelestialBodyDatas;"));
        }

        [Test]
        public void CopiedHdrpPrecomputeFiles_UseLocalPhysicallyBasedSkyIncludes()
        {
            var groundSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GroundIrradiancePrecomputation.compute"));
            var inscatterSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "InScatteredRadiancePrecomputation.compute"));
            var ambientProbeConvolutionSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "AmbientProbeConvolution.compute"));
            var compatSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "ShaderVariablesCompat.hlsl"));
            var lightDefinitionSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "LightDefinition.cs.hlsl"));
            var cookieSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CookieSampling.hlsl"));

            Assert.That(groundSource, Does.Contain("#include \"PhysicallyBasedSkyCommon.hlsl\""));
            Assert.That(groundSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(inscatterSource, Does.Contain("#include \"LightDefinition.cs.hlsl\""));
            Assert.That(inscatterSource, Does.Contain("#include \"ShaderVariablesCompat.hlsl\""));
            Assert.That(inscatterSource, Does.Contain("#include \"PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(inscatterSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(ambientProbeConvolutionSource, Does.Contain("Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"));
            Assert.That(ambientProbeConvolutionSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(compatSource, Does.Contain("float GetCurrentExposureMultiplier()"));
            Assert.That(compatSource, Does.Contain("return VividGetPreExposure();"));

            Assert.That(lightDefinitionSource, Does.Contain("struct CelestialBodyData"));
            Assert.That(cookieSource, Does.Contain("float3 SampleCookie2D(float2 uv, float4 scaleOffset)"));
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
