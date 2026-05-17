using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyHdrpPortTests
    {
        [Test]
        public void PhysicallyBasedSkyEntryShader_UsesHdrpStyleSplitIncludes()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSky.shader");
            var shaderSource = File.ReadAllText(shaderPath);

            Assert.That(File.Exists(shaderPath), Is.True);
            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyRendering.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(shaderSource, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(shaderSource, Does.Contain("float4 RenderSky(Varyings input)"));
            Assert.That(shaderSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));
        }

        [Test]
        public void RetiredHdrpFullscreenRenderChain_KeepsOnlyExpectedLegacyFilesRemoved()
        {
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScatteringSky.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GroundIrradiancePrecomputation.compute")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "InScatteredRadiancePrecomputation.compute")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "LightDefinition.cs.hlsl")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CookieSampling.hlsl")), Is.False);
        }

        [Test]
        public void ActiveHdrpSkySupportFiles_UseLocalIncludes()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyCommon.hlsl"));
            var renderingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyRendering.hlsl"));
            var evaluationSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyEvaluation.hlsl"));
            var bridgeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyBridge.hlsl"));
            var ambientProbeConvolutionSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "AmbientProbeConvolution.compute"));
            var skyLutSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "SkyLUTGenerator.compute"));
            var groundIrradianceSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "GroundIrradiancePrecomputation.compute"));
            var inScatteredSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "InScatteredRadiancePrecomputation.compute"));
            var celestialBodySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "CelestialBodyData.hlsl"));
            var compatSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "ShaderVariablesCompat.hlsl"));

            Assert.That(commonSource, Does.Contain("Packages/com.af8a2a.vividrp/Shaders/Core/Public/ShaderVariablesGlobal.hlsl"));
            Assert.That(commonSource, Does.Contain("#define _PlanetCenterPosition _PlanetCenterRadius.xyz // world space in Vivid"));
            Assert.That(commonSource, Does.Contain("#define _GroundAlbedo _GroundAlbedo_PlanetRadius.xyz"));
            Assert.That(commonSource, Does.Contain("#define _PlanetUp _PlanetUpAltitude.xyz"));
            Assert.That(commonSource, Does.Contain("#define _CameraAltitude _PlanetUpAltitude.w"));
            Assert.That(commonSource, Does.Contain("#ifndef _PlanetaryRadius"));
            Assert.That(commonSource, Does.Contain("#define _PlanetaryRadius _PlanetCenterRadius.w"));
            Assert.That(commonSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(renderingSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl\""));
            Assert.That(renderingSource, Does.Contain("#include \"SkyUtils.hlsl\""));
            Assert.That(renderingSource, Does.Contain("float3 RenderSunDisk(inout float tFrag, float tExit, float3 V)"));
            Assert.That(renderingSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(evaluationSource, Does.Contain("#include \"PhysicallyBasedSkyCommon.hlsl\""));
            Assert.That(evaluationSource, Does.Contain("void EvaluateDistantAtmosphere("));
            Assert.That(evaluationSource, Does.Contain("void EvaluateCameraAtmosphericScattering("));
            Assert.That(evaluationSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(bridgeSource, Does.Contain("float _SkyUseLUT;"));
            Assert.That(bridgeSource, Does.Contain("void EvaluateDistantAtmosphereWithLut("));
            Assert.That(bridgeSource, Does.Not.Contain("EvaluateAtmosphericFallback"));
            Assert.That(bridgeSource, Does.Not.Contain("float3 EvaluateSky("));

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
            Assert.That(skyLutSource, Does.Contain("Texture2D<float> _CSMShadowAtlas;"));
            Assert.That(skyLutSource, Does.Contain("SamplerComparisonState sampler_CSMShadowAtlas;"));
            Assert.That(skyLutSource, Does.Contain("float SampleAtmosphericScatteringShadow(float3 positionWS, float3 normalWS)"));
            Assert.That(skyLutSource, Does.Contain("_CSMShadowAtlas.SampleCmpLevelZero(sampler_CSMShadowAtlas, atlasUV, shadowCoord.z);"));
            Assert.That(skyLutSource, Does.Contain("float shadow = light.shadowIndex >= 0 ? sunShadow : 1.0f;"));
            Assert.That(skyLutSource, Does.Not.Contain("GetPrimarySunDirection()"));
            Assert.That(skyLutSource, Does.Not.Contain("GetPrimarySunColor()"));
            Assert.That(skyLutSource, Does.Not.Contain("EvaluatePrimarySunShadow(float3 positionPS)"));
            Assert.That(skyLutSource, Does.Not.Contain("_DirectionalLightDatas"));
            Assert.That(skyLutSource, Does.Not.Contain("HDShadowContext"));
            Assert.That(skyLutSource, Does.Not.Contain("EvaluateVolumetricCloudsShadows"));

            Assert.That(groundIrradianceSource, Does.Contain("#include \"PhysicallyBasedSkyCommon.hlsl\""));
            Assert.That(groundIrradianceSource, Does.Contain("RW_TEXTURE2D(float3, _GroundIrradianceTable);"));
            Assert.That(groundIrradianceSource, Does.Contain("SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture"));
            Assert.That(groundIrradianceSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

            Assert.That(inScatteredSource, Does.Contain("#include \"ShaderVariablesCompat.hlsl\""));
            Assert.That(inScatteredSource, Does.Contain("#include \"PhysicallyBasedSkyEvaluation.hlsl\""));
            Assert.That(inScatteredSource, Does.Contain("RW_TEXTURE3D(float3, _AirSingleScatteringTable);"));
            Assert.That(inScatteredSource, Does.Contain("RW_TEXTURE3D(float3, _AerosolSingleScatteringTable);"));
            Assert.That(inScatteredSource, Does.Contain("RW_TEXTURE3D(float3, _MultipleScatteringTable);"));
            Assert.That(inScatteredSource, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition"));

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
