using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingComputeTests
    {
        [Test]
        public void DeferredDirectionalLightingCompute_DeclaresExpectedKernelsAndClassificationInputs()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel ClearDeferredLit"));
            Assert.That(source, Does.Contain("#pragma kernel DeferredLit"));
            Assert.That(source, Does.Contain("_GBuffer0"));
            Assert.That(source, Does.Contain("_GBuffer1"));
            Assert.That(source, Does.Contain("_GBuffer2"));
            Assert.That(source, Does.Contain("_GBuffer3"));
            Assert.That(source, Does.Contain("_DepthTexture"));
            Assert.That(source, Does.Contain("_DirectionalShadowTexture"));
            Assert.That(source, Does.Contain("_GTAOTexture"));
            Assert.That(source, Does.Contain("_ScreenSpaceReflectionTexture"));
            Assert.That(source, Does.Contain("_ScreenSpaceReflectionEnabled"));
            Assert.That(source, Does.Contain("_MaterialPixelIndices"));
            Assert.That(source, Does.Not.Contain("_MaterialDispatchArgs"));
            Assert.That(source, Does.Contain("_LightingTexture"));
            Assert.That(source, Does.Contain("#define CLASSIFY_TILE_SIZE 8"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(source, Does.Contain("_DirectionalLightCount"));
            Assert.That(source, Does.Not.Contain("_PunctualLightCount"));
            Assert.That(source, Does.Not.Contain("_AreaLightCount"));
            Assert.That(source, Does.Not.Contain("HasPunctualLights()"));
            Assert.That(source, Does.Not.Contain("HasAreaLights()"));
            Assert.That(source, Does.Contain("VividLightingLoop::Create"));
            Assert.That(source, Does.Contain("VividLightingLoop::GetPunctualLightCount"));
            Assert.That(source, Does.Contain("VividLightingLoop::LoadPunctualLight"));
            Assert.That(source, Does.Contain("VividLightingLoop::GetAreaLightCount"));
            Assert.That(source, Does.Contain("VividLightingLoop::LoadAreaLight"));
            Assert.That(source, Does.Not.Contain("for (uint areaLightIndex = 0; areaLightIndex < _AreaLightCount; areaLightIndex++)"));
            Assert.That(source, Does.Contain("EvaluateDeferredLitLighting"));
            Assert.That(source, Does.Contain("EvaluateDeferredLitLightLoop"));
            Assert.That(source, Does.Contain("GetVividPreLightData"));
            Assert.That(source, Does.Contain("VividPreLightData preLightData"));
            Assert.That(source, Does.Contain("VividAggregateLighting aggregateLighting"));
            Assert.That(source, Does.Contain("AccumulateIndirectLighting("));
            Assert.That(source, Does.Contain("AccumulateDirectLighting("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Env("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Directional("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Punctual("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Area("));
            Assert.That(source, Does.Contain("PostEvaluateBSDF("));
            Assert.That(source, Does.Contain("VividLightLoopOutput lightLoopOutput"));
            Assert.That(source, Does.Contain("SampleDirectionalShadow"));
            Assert.That(source, Does.Contain("EvaluateDeferredDirectionalShadowAttenuation("));
            Assert.That(source, Does.Contain("shadowAttenuation = EvaluateDeferredDirectionalShadowAttenuation("));
            Assert.That(source, Does.Contain("lerp(1.0, directionalShadow, saturate(directionalLight.shadowStrength))"));
            Assert.That(source, Does.Contain("return CombineVividLightLoopOutput(lightLoopOutput);"));
            Assert.That(source, Does.Contain("ComputeWorldSpacePosition"));
            Assert.That(source, Does.Contain("surfaceData.ambientOcclusion *= saturate(SampleGTAO(pixelCoord));"));
            Assert.That(source, Does.Contain("float4 screenSpaceReflection = LoadScreenSpaceReflection(pixelCoord);"));
            Assert.That(source, Does.Contain("return float4(max(reflection.rgb, 0.0), saturate(reflection.a));"));
            Assert.That(source, Does.Contain("indirectSpecularLighting = FinalizeVividSpecularLighting("));
            Assert.That(source, Does.Contain("float reflectionWeight = screenSpaceReflection.a;"));
            Assert.That(source, Does.Contain("float3 screenSpaceReflectionFGD;"));
            Assert.That(source, Does.Contain("screenSpaceReflectionFGD = preLightData.specularFGD;"));
            Assert.That(source, Does.Contain("lighting = max(lighting - preExposedIndirectSpecular * reflectionWeight, 0.0)"));
            Assert.That(source, Does.Contain("+ screenSpaceReflection.rgb * screenSpaceReflectionFGD * reflectionWeight;"));
            Assert.That(source, Does.Not.Contain("+ screenSpaceReflection.rgb * reflectionWeight;"));
            Assert.That(source, Does.Not.Contain("tileCount ="));
            Assert.That(source, Does.Contain("uint tileListIndex = groupId.x;"));
            Assert.That(source, Does.Contain("UnpackTileCoord"));
            Assert.That(source, Does.Contain("tileCoord * CLASSIFY_TILE_SIZE"));
            Assert.That(source, Does.Contain("float3 emissive = VividApplyPreExposure(max(_GBuffer3.Load(int3(dispatchThreadId.xy, 0)).rgb, 0.0));"));
            Assert.That(source, Does.Contain("_LightingTexture[dispatchThreadId.xy] = float4(emissive, 1.0);"));
            Assert.That(source, Does.Contain("float3 indirectSpecularLighting;"));
            Assert.That(source, Does.Contain("float3 lightingNoPreExposure = EvaluateDeferredLitLighting("));
            Assert.That(source, Does.Contain("float3 lighting = VividApplyPreExposure(lightingNoPreExposure);"));
            Assert.That(source, Does.Contain("uint punctualLightCount = VividLightingLoop::GetPunctualLightCount(lightLoop);"));
            Assert.That(source, Does.Contain("uint areaLightCount = VividLightingLoop::GetAreaLightCount(lightLoop);"));
            Assert.That(source, Does.Not.Contain("_LightingTexture[dispatchThreadId.xy] = float4(0.0, 0.0, 0.0, 1.0);"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Material", "DeferredLit.compute");

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
