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
        public void Source_UnifiesRuntimeCubemapAndAmbientProbeOnShaderBakingPath()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyRenderer.cs"));
            var parametersSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyShaderParameters.cs"));

            Assert.That(source, Does.Contain("m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);"));
            Assert.That(source, Does.Contain("m_SkyBakingPass = m_SkyMaterial.FindPass(\"PhysicallyBasedSkyBaking\");"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (MissingTexture)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ResolutionChanged)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (QualityChanged)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildRuntimeCubemap (ParametersChanged)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (MissingTexture)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ResolutionChanged)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.RebuildAmbientProbe (ParametersChanged)"));
            Assert.That(source, Does.Contain("SkySettingsVolume.GetGeneratedCubemapResolution(skySettings)"));
            Assert.That(source, Does.Contain("SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings)"));
            Assert.That(source, Does.Contain("var generatedCubemapResolution = SkySettingsVolume.GetGeneratedCubemapResolution(skySettings);"));
            Assert.That(source, Does.Contain("var generatedCubemapViewSampleCount = SkySettingsVolume.GetGeneratedCubemapViewSampleCount(skySettings);"));
            Assert.That(source, Does.Contain("var runtimeCubemapRebuildReason = ResolveRuntimeCubemapRebuildReason("));
            Assert.That(source, Does.Contain("EnsureRuntimeCubemap(generatedCubemapResolution);"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, GetRuntimeCubemapRebuildSampler(runtimeCubemapRebuildReason)))"));
            Assert.That(source, Does.Contain("generatedCubemapViewSampleCount))"));
            Assert.That(source, Does.Contain("m_RuntimeSkyViewSampleCount = generatedCubemapViewSampleCount;"));
            Assert.That(source, Does.Contain("var ambientProbeRebuildReason = ResolveAmbientProbeCubemapRebuildReason(hash, generatedCubemapResolution);"));
            Assert.That(source, Does.Contain("EnsureAmbientProbeCubemap(generatedCubemapResolution);"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, GetAmbientProbeRebuildSampler(ambientProbeRebuildReason)))"));
            Assert.That(source, Does.Contain("RebuildAmbientProbeCubemap(volume, context, cmd, generatedCubemapViewSampleCount)"));
            Assert.That(source, Does.Contain("skyData.activeSkyType = SkyType.PhysicallyBased;"));
            Assert.That(source, Does.Contain("skyData.specularCubemap = m_RuntimeSkyCubemap;"));
            Assert.That(source, Does.Contain("skyData.exposure = 0.0f;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : m_RuntimeSkyCubemap;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeExposure = 0.0f;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeHash = hash;"));
            Assert.That(source, Does.Contain("return HashCode.Combine("));
            Assert.That(source, Does.Contain("ResolveCameraPosition(context, volume.planetRadius.value)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialBodyHash(context)"));
            Assert.That(source, Does.Contain("private bool CanBakeSky()"));
            Assert.That(source, Does.Contain("return m_SkyMaterial != null && m_SkyBakingPass >= 0;"));
            Assert.That(source, Does.Contain("TryBuildSkyBakingProperties(volume, context, runtimeCubemapViewSampleCount, out var properties)"));
            Assert.That(source, Does.Contain("properties.SetInt(SkyBakingViewSampleCountId, Mathf.Max(viewSampleCount, 1));"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Contain("m_RuntimeSkyCubemap,"));
            Assert.That(source, Does.Contain("m_AmbientProbeCubemap,"));
            Assert.That(source, Does.Not.Contain("m_RuntimeSkyCubemapFaces"));
            Assert.That(source, Does.Not.Contain("cmd.DispatchCompute("));
            Assert.That(source, Does.Not.Contain("cmd.CopyTexture(m_RuntimeSkyCubemapFaces"));
            Assert.That(source, Does.Contain("var skyViewLutHash = hasMaterialParameters"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.ComputeSkyViewLutHash(parameters, materialParameters, context)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.TryGetCachedSkyViewLut(skyViewLutHash, out var skyViewLut)"));
            Assert.That(source, Does.Contain("properties.SetFloat(SkyUseLutId, useSkyViewLut ? 1.0f : 0.0f);"));
            Assert.That(source, Does.Contain("properties.SetTexture(SkyViewLutId, useSkyViewLut ? skyViewLut : Texture2D.blackTexture);"));
            Assert.That(source, Does.Contain("SkyManager.RequestUpdate();"));
            Assert.That(source, Does.Contain("return PipelineResourceManager.Get<VividRPCoreResources>()?.AtmosphereLUTCompute != null;"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildForSkyBaking(volume, context, out var parameters)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(volume, context, out var materialParameters)"));
            Assert.That(source, Does.Contain("private static readonly int CelestialBodyDatasId = Shader.PropertyToID(\"_CelestialBodyDatas\");"));
            Assert.That(source, Does.Contain("private static readonly int DirectionalShadowTextureId = Shader.PropertyToID(\"_DirectionalShadowTexture\");"));
            Assert.That(source, Does.Contain("m_CelestialBodyBuffer.Update(context);"));
            Assert.That(source, Does.Contain("m_SkyMaterial.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);"));
            Assert.That(source, Does.Contain("properties.SetTexture(DirectionalShadowTextureId, Texture2D.whiteTexture);"));
            Assert.That(source, Does.Contain("m_CelestialBodyBuffer.Dispose();"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyMaterialPropertyBinder.Apply(properties, materialParameters, volume);"));
            Assert.That(source, Does.Not.Contain("m_AmbientProbeCubemapFaces"));
            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
            Assert.That(source, Does.Not.Contain("SetPixels("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForSkyBaking("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForAmbientProbe("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildMaterialParameters("));
            Assert.That(parametersSource, Does.Contain("internal static class PhysicallyBasedSkyMaterialPropertyBinder"));
            Assert.That(parametersSource, Does.Contain("properties.SetVector(PlanetCenterRadiusId, parameters.planetCenterRadius);"));
            Assert.That(parametersSource, Does.Contain("Sky baking must stay independent from camera exposure adaptation."));
            Assert.That(parametersSource, Does.Not.Contain("GetPreExposureMultiplier("));
            Assert.That(parametersSource, Does.Not.Contain("GetPostExposureMultiplier("));
            Assert.That(parametersSource, Does.Contain("parameters.skyPlanetParams = new Vector4("));
            Assert.That(parametersSource, Does.Contain("1.0f,"));
            Assert.That(parametersSource, Does.Contain("parameters.skySunColor = ToVector4(exposedSunColor);"));
            Assert.That(parametersSource, Does.Contain("parameters.skyGroundTint = ToVector4(exposedGroundTint);"));
            Assert.That(parametersSource, Does.Contain("internal static class PhysicallyBasedSkyComputeParameterBinder"));
            Assert.That(parametersSource, Does.Contain("commandBuffer.SetComputeMatrixParam(computeShader, PixelCoordToViewDirWSId, skyParameters.pixelCoordToViewDirWS);"));
        }

        [Test]
        public void Source_GuardsRuntimeCubemapBehindSharedSkyBakingCapability()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(source, Does.Contain("private bool CanBakeSky()"));
            Assert.That(source, Does.Contain("return CanBakeSky();"));
            Assert.That(source, Does.Contain("return m_SkyMaterial != null && m_SkyBakingPass >= 0;"));
            Assert.That(source, Does.Contain("private static bool CanUseSkyViewLut()"));
            Assert.That(source, Does.Not.Contain("SkyCubemapKernelName"));
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
