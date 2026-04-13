using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyRendererTests
    {
        [Test]
        public void ResolveSunDirection_ReturnsMainDirectionalLightDirection_WhenContextProvidesOne()
        {
            var expectedDirection = new Vector3(0.25f, -0.5f, 0.75f).normalized;
            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = expectedDirection,
                        color = new Vector3(2.0f, 1.5f, 0.75f)
                    }
                },
                directionalLightCount = 1,
                mainDirectionalLightIndex = 0
            };

            var direction = PhysicallyBasedSkyRenderer.ResolveSunDirection(new SkyRendererContext(new VividCameraData(), lightData));

            Assert.That(Vector3.Distance(direction, expectedDirection), Is.LessThan(1e-6f));
        }

        [Test]
        public void ResolveSunColor_ReturnsMainDirectionalLightColor_WhenContextProvidesOne()
        {
            var expectedColor = new Color(1.5f, 0.75f, 0.25f, 1.0f);
            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = Vector3.down,
                        color = new Vector3(expectedColor.r, expectedColor.g, expectedColor.b)
                    }
                },
                directionalLightCount = 1,
                mainDirectionalLightIndex = 0
            };

            var color = PhysicallyBasedSkyRenderer.ResolveSunColor(new SkyRendererContext(new VividCameraData(), lightData));

            Assert.That(color, Is.EqualTo(expectedColor));
        }

        [Test]
        public void ResolveCameraPosition_AddsPlanetRadiusToWorldHeight_WhenCameraExists()
        {
            var cameraGameObject = new GameObject("PhysicallyBasedSkyCamera");
            var camera = cameraGameObject.AddComponent<Camera>();
            var cameraData = new VividCameraData
            {
                camera = camera
            };

            try
            {
                camera.transform.position = new Vector3(10.0f, 20.0f, 30.0f);

                var cameraPosition = PhysicallyBasedSkyRenderer.ResolveCameraPosition(
                    new SkyRendererContext(cameraData, new VividLightData()),
                    1000.0f);

                Assert.That(cameraPosition, Is.EqualTo(new Vector3(10.0f, 1020.0f, 30.0f)));
            }
            finally
            {
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void Source_UnifiesEnvironmentBakingAndRendererDrivenSkyInjection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));
            var parametersSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyShaderParameters.cs"));

            Assert.That(source, Does.Contain("private const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(source, Does.Contain("m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);"));
            Assert.That(source, Does.Contain("m_SkyBakingPass = m_SkyMaterial.FindPass(\"PhysicallyBasedSkyBaking\");"));
            Assert.That(source, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(source, Does.Contain("public void RenderSky(CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("var skyViewTexture = ResolveSkyViewTexture();"));
            Assert.That(source, Does.Contain("Shader.GetGlobalTexture(DirectionalShadowTextureId)"));
            Assert.That(source, Does.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_SkyMaterial, properties, 0);"));
            Assert.That(source, Does.Contain("TryPrepareLocalSkyPrecomputation("));
            Assert.That(source, Does.Contain("ApplyLocalSkyPrecomputationTextures(properties);"));
            Assert.That(source, Does.Contain("CoreUtils.SetKeyword(m_SkyMaterial, \"LOCAL_SKY\", useLocalSkyPrecomputation);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(m_RenderParameters, m_RenderMaterialParameters, m_RenderContext)"));
            Assert.That(source, Does.Contain("SkyManager.TryGetSkyViewLut(skyViewHash, out skyViewTexture)"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Contain("EnsureLocalSkyPrecomputation("));

            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForSkyBaking("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForAmbientProbe("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildMaterialParameters("));
        }

        [Test]
        public void Source_GuardsRuntimeCubemapBehindSharedSkyBakingCapability()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(source, Does.Contain("private bool CanBakeSky()"));
            Assert.That(source, Does.Contain("return CanBakeSky();"));
            Assert.That(source, Does.Contain("return m_SkyMaterial != null && m_SkyBakingPass >= 0;"));
            Assert.That(source, Does.Contain("private Texture ResolveSkyViewTexture()"));
        }

        [Test]
        public void Source_ReusesRuntimeCubemapBeforeRebuildingAmbientProbe()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(source, Does.Contain("private bool TryCopyRuntimeCubemapToAmbientProbe("));
            Assert.That(source, Does.Contain("cmd.CopyTexture(m_RuntimeSkyCubemap, m_AmbientProbeCubemap);"));
            Assert.That(source, Does.Contain("if (TryCopyRuntimeCubemapToAmbientProbe("));
            Assert.That(source, Does.Contain("|| RebuildAmbientProbeCubemap(volume, context, cmd, generatedCubemapViewSampleCount))"));
            Assert.That(source, Does.Contain("|| m_RuntimeSkyHash != skyHash"));
            Assert.That(source, Does.Contain("|| m_RuntimeSkyViewSampleCount != viewSampleCount)"));
        }

        [Test]
        public void Source_BindsRealtimeLocalSkyTexturesThroughRendererCache()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(source, Does.Contain("private int m_LocalSkyPrecomputationHash;"));
            Assert.That(source, Does.Contain("private bool m_HasLocalSkyPrecomputation;"));
            Assert.That(source, Does.Contain("private bool TryPrepareLocalSkyPrecomputation("));
            Assert.That(source, Does.Contain("&& m_LocalSkyPrecomputationHash == localSkyPrecomputationHash"));
            Assert.That(source, Does.Contain("&& HasLocalSkyPrecomputationTextures()"));
            Assert.That(source, Does.Contain("private void ApplyLocalSkyPrecomputationTextures(MaterialPropertyBlock properties)"));
            Assert.That(source, Does.Contain("properties.SetTexture(GroundIrradianceTextureId, m_GroundIrradianceTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(AirSingleScatteringTextureId, m_AirSingleScatteringTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(AerosolSingleScatteringTextureId, m_AerosolSingleScatteringTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(MultipleScatteringTextureId, m_MultipleScatteringTable);"));
            Assert.That(source, Does.Contain("private bool HasLocalSkyPrecomputationTextures()"));
        }

        [Test]
        public void Source_UsesPlanetRelativeCameraPositionForRealtimeLocalSkyShader()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyBridge.hlsl"));

            Assert.That(shaderSource, Does.Contain("const float3 O = _SkyCameraPositionPS.xyz;"));
            Assert.That(shaderSource, Does.Contain("EvaluatePbrAtmosphere(_SkyCameraPositionPS.xyz, V, tFrag, renderSunDisk, skyColor, skyOpacity);"));
            Assert.That(shaderSource, Does.Not.Contain("const float3 O = _WorldSpaceCameraPos;"));
            Assert.That(shaderSource, Does.Not.Contain("EvaluatePbrAtmosphere(_WorldSpaceCameraPos, V, tFrag, renderSunDisk, skyColor, skyOpacity);"));
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
