using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyHdrpPortTests
    {
        [Test]
        public void PhysicallyBasedSkyEntryShader_UsesSingleTopLevelPath()
        {
            var topLevelShaderPath = GetPackageFilePath("Shaders", "Core", "Private", "PhysicallyBasedSky.shader");
            var topLevelShaderDirectory = Path.GetDirectoryName(topLevelShaderPath);
            Assert.That(topLevelShaderDirectory, Is.Not.Null);
            var legacyShaderPath = Path.Combine(
                topLevelShaderDirectory,
                "Sky",
                "PhysicallyBasedSky.shader");
            var shaderSource = File.ReadAllText(topLevelShaderPath);

            Assert.That(File.Exists(topLevelShaderPath), Is.True);
            Assert.That(File.Exists(legacyShaderPath), Is.False);
            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(shaderSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));
        }

        [Test]
        public void RetiredHdrpFullscreenRenderChain_IsRemoved()
        {
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScatteringSky.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GroundIrradiancePrecomputation.compute")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "InScatteredRadiancePrecomputation.compute")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "LightDefinition.cs.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CookieSampling.hlsl")), Is.False);
        }

        [Test]
        public void ActiveHdrpSkySupportFiles_UseLocalIncludes()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyCommon.hlsl"));
            var evaluationSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyEvaluation.hlsl"));
            var ambientProbeConvolutionSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "AmbientProbeConvolution.compute"));
            var skyLutSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "SkyLUTGenerator.compute"));
            var celestialBodySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CelestialBodyData.hlsl"));
            var compatSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "ShaderVariablesCompat.hlsl"));

            Assert.That(commonSource, Does.Contain("Packages/com.af8a2a.vividrp/Shaders/Core/Public/ShaderVariablesGlobal.hlsl"));
            Assert.That(commonSource, Does.Contain("#define _PlanetCenterPosition _PlanetCenterRadius.xyz // camera relative"));
            Assert.That(commonSource, Does.Contain("#define _GroundAlbedo _GroundAlbedo_PlanetRadius.xyz"));
            Assert.That(commonSource, Does.Contain("#define _PlanetUp _PlanetUpAltitude.xyz"));
            Assert.That(commonSource, Does.Contain("#define _CameraAltitude _PlanetUpAltitude.w"));
            Assert.That(commonSource, Does.Contain("#ifndef _PlanetaryRadius"));
            Assert.That(commonSource, Does.Contain("#define _PlanetaryRadius _PlanetCenterRadius.w"));
            Assert.That(commonSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(evaluationSource, Does.Contain("#include \"PhysicallyBasedSkyCommon.hlsl\""));
            Assert.That(evaluationSource, Does.Contain("void EvaluateDistantAtmosphere("));
            Assert.That(evaluationSource, Does.Contain("void EvaluateCameraAtmosphericScattering("));
            Assert.That(evaluationSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(ambientProbeConvolutionSource, Does.Contain("Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"));
            Assert.That(ambientProbeConvolutionSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(celestialBodySource, Does.Contain("struct CelestialBodyData"));
            Assert.That(celestialBodySource, Does.Contain("uint surfaceTextureIndex;"));
            Assert.That(celestialBodySource, Does.Contain("StructuredBuffer<CelestialBodyData> _CelestialBodyDatas;"));
            Assert.That(celestialBodySource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(skyLutSource, Does.Contain("#include \"ShaderVariablesCompat.hlsl\""));
            Assert.That(skyLutSource, Does.Contain("#include \"CelestialBodyData.hlsl\""));
            Assert.That(skyLutSource, Does.Contain("#include \"PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(skyLutSource, Does.Not.Contain("../AtmosphericScattering/AtmosphericScattering.hlsl"));
            Assert.That(skyLutSource, Does.Contain("for (uint i = 0; i < _CelestialLightCount; i++)"));
            Assert.That(skyLutSource, Does.Contain("CelestialBodyData light = _CelestialBodyDatas[i];"));
            Assert.That(skyLutSource, Does.Contain("float3 L = -light.forward.xyz;"));
            Assert.That(skyLutSource, Does.Not.Contain("GetPrimarySunDirection()"));
            Assert.That(skyLutSource, Does.Not.Contain("GetPrimarySunColor()"));
            Assert.That(skyLutSource, Does.Not.Contain("EvaluatePrimarySunShadow(float3 positionPS)"));
            Assert.That(skyLutSource, Does.Not.Contain("_DirectionalLightDatas"));
            Assert.That(skyLutSource, Does.Not.Contain("HDShadowContext"));
            Assert.That(skyLutSource, Does.Not.Contain("EvaluateVolumetricCloudsShadows"));

            Assert.That(compatSource, Does.Contain("float GetCurrentExposureMultiplier()"));
            Assert.That(compatSource, Does.Contain("return VividGetPreExposure();"));
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
