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
        public void Source_UsesGpuRuntimeCubemapUpdateAndClearsCpuProjectionLoop()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyRenderer.cs"));
            var parametersSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyShaderParameters.cs"));

            Assert.That(source, Does.Contain("m_AtmosphereLutCompute = resources?.AtmosphereLUTCompute;"));
            Assert.That(source, Does.Contain("m_SkyCubemapKernel = m_AtmosphereLutCompute != null"));
            Assert.That(source, Does.Contain("m_SkyMaterial = CoreUtils.CreateEngineMaterial(shader);"));
            Assert.That(source, Does.Contain("m_AmbientProbeBakingPass = m_SkyMaterial.FindPass(\"PhysicallyBasedSkyBaking\");"));
            Assert.That(source, Does.Contain("EnsureRuntimeCubemap();"));
            Assert.That(source, Does.Contain("RebuildRuntimeCubemap(volume, context, cmd);"));
            Assert.That(source, Does.Contain("EnsureAmbientProbeCubemap();"));
            Assert.That(source, Does.Contain("RebuildAmbientProbeCubemap(volume, context, cmd);"));
            Assert.That(source, Does.Contain("skyData.activeSkyType = SkyType.PhysicallyBased;"));
            Assert.That(source, Does.Contain("skyData.specularCubemap = m_RuntimeSkyCubemap;"));
            Assert.That(source, Does.Contain("skyData.exposure = 0.0f;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeCubemap = useBakedAmbientProbe ? m_AmbientProbeCubemap : m_RuntimeSkyCubemap;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeExposure = 0.0f;"));
            Assert.That(source, Does.Contain("skyData.ambientProbeHash = hash;"));
            Assert.That(source, Does.Contain("skyData.hasDiffuseSH = false;"));
            Assert.That(source, Does.Contain("skyData.diffuseSH = default;"));
            Assert.That(source, Does.Contain("return HashCode.Combine("));
            Assert.That(source, Does.Contain("ResolveCameraPosition(context, volume.planetRadius.value)"));
            Assert.That(source, Does.Contain("ResolveSunDirection(context)"));
            Assert.That(source, Does.Contain("ResolveSunColor(context)"));
            Assert.That(source, Does.Contain("m_RuntimeSkyCubemapFaces"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_AtmosphereLutCompute, m_SkyCubemapKernel, SkyCubemapOutputId, m_RuntimeSkyCubemapFaces);"));
            Assert.That(source, Does.Contain("cmd.CopyTexture(m_RuntimeSkyCubemapFaces, face, 0, m_RuntimeSkyCubemap, face, 0);"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute("));
            Assert.That(source, Does.Contain("cmd.GenerateMips(m_RuntimeSkyCubemap);"));
            Assert.That(source, Does.Contain("properties.SetFloat(SkyUseLutId, 0.0f);"));
            Assert.That(source, Does.Contain("properties.SetTexture(SkyViewLutId, Texture2D.blackTexture);"));
            Assert.That(source, Does.Contain("SkyCubemapBakingUtility.RenderSkyToCubemap("));
            Assert.That(source, Does.Not.Contain("m_AmbientProbeCubemapFaces"));
            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
            Assert.That(source, Does.Not.Contain("SetPixels("));
            Assert.That(parametersSource, Does.Contain("internal static bool TryBuildForAmbientProbe("));
            Assert.That(parametersSource, Does.Contain("Ambient probe baking must stay independent from camera exposure adaptation."));
            Assert.That(parametersSource, Does.Not.Contain("GetPreExposureMultiplier("));
            Assert.That(parametersSource, Does.Not.Contain("GetPostExposureMultiplier("));
            Assert.That(parametersSource, Does.Contain("parameters.skyPlanetParams = new Vector4("));
            Assert.That(parametersSource, Does.Contain("1.0f,"));
            Assert.That(parametersSource, Does.Contain("parameters.skySunColor = ToVector4(exposedSunColor);"));
            Assert.That(parametersSource, Does.Contain("parameters.skyGroundTint = ToVector4(exposedGroundTint);"));
        }

        [Test]
        public void AtmosphereLutCompute_DeclaresSkyCubemapKernelForRuntimeSky()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphereLUT.compute"));

            Assert.That(source, Does.Contain("#pragma kernel SkyCubemap"));
            Assert.That(source, Does.Contain("RWTexture2DArray<float4> _SkyCubemapOutput;"));
            Assert.That(source, Does.Contain("SkyOpticalDepth ComputeOpticalDepthToSun("));
            Assert.That(source, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(source, Does.Contain("float3 EvaluateSkyCubemap(float3 directionWS)"));
            Assert.That(source, Does.Contain("void SkyCubemap(uint3 tid : SV_DispatchThreadID)"));
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
