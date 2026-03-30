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
        public void Source_UsesRuntimeCubemapUpdateAndClearsCpuShProjection()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyRenderer.cs"));

            Assert.That(source, Does.Contain("EnsureRuntimeCubemap();"));
            Assert.That(source, Does.Contain("RebuildRuntimeCubemap(volume, context);"));
            Assert.That(source, Does.Contain("skyData.activeSkyType = SkyType.PhysicallyBased;"));
            Assert.That(source, Does.Contain("skyData.specularCubemap = m_RuntimeSkyCubemap;"));
            Assert.That(source, Does.Contain("skyData.exposure = volume.exposure.value;"));
            Assert.That(source, Does.Contain("skyData.hasDiffuseSH = false;"));
            Assert.That(source, Does.Contain("skyData.diffuseSH = default;"));
            Assert.That(source, Does.Contain("return HashCode.Combine("));
            Assert.That(source, Does.Contain("ResolveCameraPosition(context, volume.planetRadius.value)"));
            Assert.That(source, Does.Contain("ResolveSunDirection(context)"));
            Assert.That(source, Does.Contain("ResolveSunColor(context)"));
            Assert.That(source, Does.Not.Contain("TryProjectCubemapToSH("));
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
