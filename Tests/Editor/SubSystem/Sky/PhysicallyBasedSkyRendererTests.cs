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
        public void ResolveSunDirectionAndColor_DoNotAllocate_WhenFrameLightDataIsValid()
        {
            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = Vector3.down,
                        color = new Vector3(2.0f, 1.5f, 0.75f)
                    }
                },
                directionalLightCount = 1,
                mainDirectionalLightIndex = 0
            };
            var context = new SkyRendererContext(
                new VividCameraData(),
                lightData);

            PhysicallyBasedSkyRenderer.ResolveSunDirection(context);
            PhysicallyBasedSkyRenderer.ResolveSunColor(context);

            var allocatedBefore =
                global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
            {
                PhysicallyBasedSkyRenderer.ResolveSunDirection(context);
                PhysicallyBasedSkyRenderer.ResolveSunColor(context);
            }
            var allocatedBytes =
                global::System.GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void ResolveSunColor_UsesSceneDirectionalLight_WhenFrameLightDataIsNotReady()
        {
            var previousSun = RenderSettings.sun;
            var sunGameObject = new GameObject("PbrSkyFallbackSun");
            var sun = sunGameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.75f, 0.5f, 0.25f, 1.0f);
            sun.intensity = 1000.0f;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, -1.0f, 0.3f).normalized);

            try
            {
                RenderSettings.sun = null;

                var context = new SkyRendererContext(new VividCameraData(), new VividLightData());
                var color = PhysicallyBasedSkyRenderer.ResolveSunColor(context);
                var direction = PhysicallyBasedSkyRenderer.ResolveSunDirection(context);
                var expectedColor = VividLightRenderDatabase.EvaluateLightColor(sun);
                var expectedDirection = (-sun.transform.forward).normalized;

                Assert.That(color.r, Is.EqualTo(expectedColor.r).Within(1e-6f));
                Assert.That(color.g, Is.EqualTo(expectedColor.g).Within(1e-6f));
                Assert.That(color.b, Is.EqualTo(expectedColor.b).Within(1e-6f));
                Assert.That(Vector3.Distance(direction, expectedDirection), Is.LessThan(1e-6f));
            }
            finally
            {
                RenderSettings.sun = previousSun;
                Object.DestroyImmediate(sunGameObject);
            }
        }

        [Test]
        public void BuildCelestialBodyData_FallsBackToSceneDirectionalLight_WhenDirectionalFrameDataIsBlack()
        {
            var previousSun = RenderSettings.sun;
            var sunGameObject = new GameObject("PbrSkyFallbackCelestialSun");
            var sun = sunGameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.intensity = 1000.0f;

            try
            {
                RenderSettings.sun = null;

                var lightData = new VividLightData
                {
                    directionalLights = new[]
                    {
                        new VividLightData.DirectionalLightData
                        {
                            directionWS = Vector3.down,
                            color = Vector3.zero
                        }
                    },
                    directionalLightCount = 1,
                    mainDirectionalLightIndex = 0
                };
                var celestialBodies = new PhysicallyBasedSkyCelestialBodyData[PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies];

                PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                    new SkyRendererContext(new VividCameraData(), lightData),
                    celestialBodies,
                    out var celestialLightCount,
                    out var celestialBodyCount,
                    out var celestialLightExposure);

                Assert.That(celestialLightCount, Is.EqualTo(1));
                Assert.That(celestialBodyCount, Is.EqualTo(1));
                Assert.That(celestialLightExposure, Is.GreaterThan(1.0f));
            }
            finally
            {
                RenderSettings.sun = previousSun;
                Object.DestroyImmediate(sunGameObject);
            }
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
    }
}
