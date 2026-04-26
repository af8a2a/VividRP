using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
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
        public void ResolveCameraPosition_AnchorsPlanetToCamera_WhenRenderingInCameraSpace()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var cameraGameObject = new GameObject("PhysicallyBasedSkyCameraSpaceCamera");
            var camera = cameraGameObject.AddComponent<Camera>();
            var cameraData = new VividCameraData
            {
                camera = camera
            };

            try
            {
                var settings = profile.Add<SkySettingsVolume>(false);
                settings.renderingSpace.value = RenderingSpace.Camera;
                var volume = profile.Add<PhysicallyBasedSkyVolume>(false);
                volume.planetRadius.value = 1000.0f;

                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                camera.transform.position = new Vector3(10.0f, 20.0f, 30.0f);
                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var cameraPosition = PhysicallyBasedSkyRenderer.ResolveCameraPosition(
                    new SkyRendererContext(cameraData, new VividLightData()),
                    volume.planetRadius.value);

                Assert.That(cameraPosition, Is.EqualTo(new Vector3(0.0f, 1001.0f, 0.0f)));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void ResolveCameraPosition_UsesManualPlanetCenter_WhenConfigured()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var cameraGameObject = new GameObject("PhysicallyBasedSkyManualCenterCamera");
            var camera = cameraGameObject.AddComponent<Camera>();
            var cameraData = new VividCameraData
            {
                camera = camera
            };

            try
            {
                var settings = profile.Add<SkySettingsVolume>(false);
                settings.renderingSpace.value = RenderingSpace.World;
                settings.centerMode.value = PlanetMode.Manual;
                settings.planetCenter.value = new Vector3(100.0f, -900.0f, 50.0f);

                var volume = profile.Add<PhysicallyBasedSkyVolume>(false);
                volume.planetRadius.value = 1000.0f;

                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                camera.transform.position = new Vector3(100.0f, 120.0f, 50.0f);
                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var cameraPosition = PhysicallyBasedSkyRenderer.ResolveCameraPosition(
                    new SkyRendererContext(cameraData, new VividLightData()),
                    volume.planetRadius.value);

                Assert.That(cameraPosition, Is.EqualTo(new Vector3(0.0f, 1020.0f, 0.0f)));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraGameObject);
            }
        }

        [Test]
        public void Source_UnifiesEnvironmentBakingAndRendererDrivenSkyInjection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyRenderer.cs"));
            var parametersSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyShaderParameters.cs"));

            Assert.That(source, Does.Contain("private const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(source, Does.Contain("private readonly PhysicallyBasedSkyAtmosphereLutCache m_AtmosphereLutCache = new();"));
            Assert.That(source, Does.Contain("m_AtmosphereLutCache.Build(resources);"));
            Assert.That(source, Does.Contain("m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);"));
            Assert.That(source, Does.Contain("m_SkyBakingPass = m_SkyMaterial.FindPass(\"PhysicallyBasedSkyBaking\");"));
            Assert.That(source, Does.Contain("public void UpdateFrameResources(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("m_AtmosphereLutCache.Update(context, cmd);"));
            Assert.That(source, Does.Contain("ApplyAtmosphereLutHandle(skyData);"));
            Assert.That(source, Does.Contain("public void PrepareSkyRendering("));
            Assert.That(source, Does.Contain("public void RenderSky(CommandBuffer cmd)"));
            Assert.That(source, Does.Contain("ImportSkyViewLutForPass(skyViewLut);"));
            Assert.That(source, Does.Contain("var skyViewTexture = ResolveSkyViewTexture();"));
            Assert.That(source, Does.Contain("Shader.GetGlobalTexture(DirectionalShadowTextureId)"));
            Assert.That(source, Does.Contain("cmd.SetRenderTarget(m_ColorTarget, m_DepthTexture);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(cmd, m_SkyMaterial, properties, 0);"));
            Assert.That(source, Does.Contain("TryPrepareLocalSkyPrecomputation("));
            Assert.That(source, Does.Contain("ApplyLocalSkyPrecomputationTextures(properties);"));
            Assert.That(source, Does.Contain("CoreUtils.SetKeyword(m_SkyMaterial, \"LOCAL_SKY\", useLocalSkyPrecomputation);"));
            Assert.That(source, Does.Contain("&& UsesWorldSpacePrecomputation(m_RenderMaterialParameters)"));
            Assert.That(source, Does.Contain("&& UsesWorldSpacePrecomputation(materialParameters)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyAtmosphereLutCache.ComputeSkyViewLutHash(m_RenderParameters, m_RenderMaterialParameters, m_RenderContext)"));
            Assert.That(source, Does.Contain("m_AtmosphereLutCache.TryGetSkyViewLut(skyViewHash, out skyViewTexture)"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Contain("EnsureLocalSkyPrecomputation("));
            Assert.That(source, Does.Contain("var includeSunInBaking = SkySettingsVolume.GetIncludeSunInBaking(skySettings);"));
            Assert.That(source, Does.Contain("var intensityMultiplier = volume.GetIntensityMultiplier();"));
            Assert.That(source, Does.Contain("SkySettingsVolume.GetIncludeSunInBaking(skySettings),"));
            Assert.That(source, Does.Contain("intensityMultiplier,"));
            Assert.That(source, Does.Contain("materialParameters.renderSunDisk = includeSunInBaking && volume.renderSunDisk.value ? 1 : 0;"));

            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForSkyBaking("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForAmbientProbe("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildMaterialParameters("));
            Assert.That(parametersSource, Does.Contain("private static float ResolveSkyIntensityMultiplier(PhysicallyBasedSkyVolume volume)"));
            Assert.That(parametersSource, Does.Contain("ResolveSkyIntensityMultiplier(volume)"));
            Assert.That(parametersSource, Does.Contain("var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();"));
            Assert.That(parametersSource, Does.Contain("var planet = SkyPlanet.Resolve("));
            Assert.That(parametersSource, Does.Contain("PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData("));
            Assert.That(parametersSource, Does.Contain("out var celestialLightCount"));
            Assert.That(parametersSource, Does.Contain("parameters.celestialLightCount = celestialLightCount;"));
            Assert.That(parametersSource, Does.Contain("parameters.celestialBodyCount = celestialBodyCount;"));
            Assert.That(parametersSource, Does.Contain("parameters.celestialLightExposure = Mathf.Max(celestialLightExposure, 1.0f);"));
            Assert.That(parametersSource, Does.Contain("parameters.intensityMultiplier = volume.GetIntensityMultiplier();"));
            Assert.That(parametersSource, Does.Contain("parameters.renderingSpace = planet.renderingSpace == RenderingSpace.World ? 1 : 0;"));
            Assert.That(parametersSource, Does.Contain("volume.atmosphericScattering.value ? 1.0f : 0.0f"));
            Assert.That(parametersSource, Does.Not.Contain("parameters.celestialLightCount = ResolveCelestialLightCount(context);"));
            Assert.That(parametersSource, Does.Not.Contain("ResolveCelestialLightExposure(context)"));
            Assert.That(parametersSource, Does.Not.Contain("volume.IsHeightFogActive() ? 1.0f : 0.0f"));
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
            Assert.That(source, Does.Contain("private void ApplyAtmosphereLutHandle(VividSkyData skyData)"));
            Assert.That(source, Does.Contain("private bool TryPrepareLocalSkyPrecomputation("));
            Assert.That(source, Does.Contain("&& m_LocalSkyPrecomputationHash == localSkyPrecomputationHash"));
            Assert.That(source, Does.Contain("&& HasLocalSkyPrecomputationTextures()"));
            Assert.That(source, Does.Contain("private void ApplyLocalSkyPrecomputationTextures(MaterialPropertyBlock properties)"));
            Assert.That(source, Does.Contain("properties.SetTexture(GroundIrradianceTextureId, m_GroundIrradianceTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(AirSingleScatteringTextureId, m_AirSingleScatteringTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(AerosolSingleScatteringTextureId, m_AerosolSingleScatteringTable);"));
            Assert.That(source, Does.Contain("properties.SetTexture(MultipleScatteringTextureId, m_MultipleScatteringTable);"));
            Assert.That(source, Does.Contain("private bool HasLocalSkyPrecomputationTextures()"));
            Assert.That(source, Does.Contain("private void ImportSkyViewLutForPass(RenderGraphTexture skyViewLut)"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(skyViewLut, handle);"));
        }

        [Test]
        public void Source_UsesPlanetRelativeCameraPositionForRealtimeLocalSkyShader()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSky.shader"));

            Assert.That(shaderSource, Does.Contain("const float3 O = _PBRSkyCameraPosPS;"));
            Assert.That(shaderSource, Does.Contain("EvaluatePbrAtmosphere(_PBRSkyCameraPosPS, V, tFrag, renderSunDisk, skyColor, skyOpacity);"));
            Assert.That(shaderSource, Does.Not.Contain("const float3 O = _SkyCameraPositionPS.xyz;"));
            Assert.That(shaderSource, Does.Not.Contain("EvaluatePbrAtmosphere(_SkyCameraPositionPS.xyz, V, tFrag, renderSunDisk, skyColor, skyOpacity);"));
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
