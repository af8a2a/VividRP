using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyCelestialBodyUtilityTests
    {
        [Test]
        public void Source_ResolvesBindlessSurfaceTextureIndices_ForCelestialBodies()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSkyCelestialBodyData.cs"));

            Assert.That(source, Does.Contain("public uint surfaceTextureIndex;"));
            Assert.That(source, Does.Contain("surfaceTextureIndex = ResolveSurfaceTextureIndex(additionalData),"));
            Assert.That(source, Does.Contain("private static uint ResolveSurfaceTextureIndex(VividAdditionalLightData additionalData)"));
            Assert.That(source, Does.Contain("VividGPUDrivenSystem.instance.BindlessTextureContainer.TryGetOrCreateIndex(surfaceTexture, out var"));
            Assert.That(source, Does.Contain("? surfaceTextureIndex"));
            Assert.That(source, Does.Contain("BindlessTextureContainer.InvalidTextureIndex"));
        }

        [Test]
        public void BuildCelestialBodyData_UsesDirectionalLightsForHdrpStyleCelestialBodies()
        {
            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = Vector3.up,
                        color = new Vector3(3.0f, 2.0f, 1.0f)
                    },
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = new Vector3(0.0f, 0.5f, 0.5f).normalized,
                        color = new Vector3(1.0f, 4.0f, 2.0f)
                    }
                },
                directionalLightCount = 2,
                mainDirectionalLightIndex = 0
            };

            var celestialBodies = new PhysicallyBasedSkyCelestialBodyData[PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies];
            var hash = PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                new SkyRendererContext(new VividCameraData(), lightData),
                celestialBodies,
                out var celestialLightCount,
                out var celestialBodyCount,
                out var celestialLightExposure);

            Assert.That(celestialLightCount, Is.EqualTo(2));
            Assert.That(celestialBodyCount, Is.EqualTo(2));
            Assert.That(hash, Is.Not.EqualTo(13));
            Assert.That(Vector3.Distance(-celestialBodies[0].forward, lightData.directionalLights[0].directionWS.normalized), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(-celestialBodies[1].forward, lightData.directionalLights[1].directionWS.normalized), Is.LessThan(1e-6f));
            Assert.That(celestialBodies[0].type, Is.EqualTo(0));
            Assert.That(celestialBodies[0].angularRadius, Is.GreaterThan(0.0f));
            Assert.That(celestialLightExposure, Is.EqualTo(3.0f).Within(1e-6f));
        }

        [Test]
        public void BuildCelestialBodyData_UsesDirectionalLightMetadataForMoonFlareTextureAndShadow()
        {
            var originalSun = RenderSettings.sun;
            var sunObject = new GameObject("Sky Sun");
            var moonObject = new GameObject("Sky Moon");

            try
            {
                var sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = Color.white;
                sun.lightUnit = LightUnit.Lux;
                sun.intensity = 2.0f;
                sun.shadows = LightShadows.Soft;

                var sunAdditionalData = sun.GetVividAdditionalLightData();
                sunAdditionalData.enableRayTracedShadow = true;
                sunAdditionalData.interactsWithSky = true;
                sunAdditionalData.celestialBodyShadingSource = VividAdditionalLightData.CelestialBodyShadingSource.Emission;

                var moon = moonObject.AddComponent<Light>();
                moon.type = LightType.Directional;
                moon.color = Color.white;
                moon.lightUnit = LightUnit.Lux;
                moon.intensity = 0.0f;
                moon.shadows = LightShadows.None;

                var moonAdditionalData = moon.GetVividAdditionalLightData();
                moonAdditionalData.interactsWithSky = true;
                moonAdditionalData.celestialBodyShadingSource = VividAdditionalLightData.CelestialBodyShadingSource.Manual;
                moonAdditionalData.surfaceTint = new Color(0.6f, 0.7f, 0.8f, 1.0f);
                moonAdditionalData.surfaceTexture = Texture2D.whiteTexture;
                moonAdditionalData.distance = 1234.0f;
                moonAdditionalData.earthshine = 2.0f;
                moonAdditionalData.flareSize = 3.0f;
                moonAdditionalData.flareTint = new Color(0.9f, 0.6f, 0.3f, 1.0f);
                moonAdditionalData.flareFalloff = 5.0f;
                moonAdditionalData.flareMultiplier = 0.75f;
                moonAdditionalData.sunColor = new Color(0.25f, 0.5f, 1.0f, 1.0f);
                moonAdditionalData.sunIntensity = 1000.0f;
                moonAdditionalData.moonPhase = 0.25f;
                moonAdditionalData.moonPhaseRotation = 30.0f;

                RenderSettings.sun = sun;

                var lightData = new VividLightData();
                lightData.UpdateDirectionalLights(new[] { sun, moon }, sun);

                var celestialBodies = new PhysicallyBasedSkyCelestialBodyData[PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies];
                var lightHash = PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                    new SkyRendererContext(new VividCameraData(), lightData),
                    celestialBodies,
                    out var celestialLightCount,
                    out var celestialBodyCount,
                    out var celestialLightExposure,
                    out var celestialBodyHash);

                Assert.That(celestialLightCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(celestialBodyCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(lightHash, Is.Not.EqualTo(13));
                Assert.That(celestialBodyHash, Is.Not.EqualTo(lightHash));
                Assert.That(celestialLightExposure, Is.GreaterThanOrEqualTo(1.0f));

                var sunBodyIndex = Array.FindIndex(
                    celestialBodies,
                    body => body.shadowIndex == 0 && body.type == 0);
                Assert.That(sunBodyIndex, Is.GreaterThanOrEqualTo(0));
                var sunBody = celestialBodies[sunBodyIndex];
                Assert.That(sunBody.type, Is.EqualTo(0));
                Assert.That(sunBody.shadowIndex, Is.EqualTo(0));

                var moonBodyIndex = Array.FindIndex(
                    celestialBodies,
                    body => body.type == 1 && Mathf.Abs(body.distanceFromCamera - 1234.0f) < 1e-4f);
                Assert.That(moonBodyIndex, Is.GreaterThanOrEqualTo(0));
                var moonBody = celestialBodies[moonBodyIndex];
                Assert.That(moonBody.type, Is.EqualTo(1));
                Assert.That(moonBody.distanceFromCamera, Is.EqualTo(1234.0f).Within(1e-4f));
                Assert.That(moonBody.earthshine, Is.EqualTo(0.02f).Within(1e-6f));
                Assert.That(moonBody.flareSize, Is.EqualTo(3.0f * Mathf.Deg2Rad).Within(1e-6f));
                Assert.That(moonBody.flareFalloff, Is.EqualTo(5.0f).Within(1e-6f));
                Assert.That(moonBody.surfaceTextureScaleOffset.x, Is.EqualTo(1.0f).Within(1e-6f));
                Assert.That(moonBody.shadowIndex, Is.EqualTo(-1));
                Assert.That(moonBody.sunDirection.sqrMagnitude, Is.GreaterThan(0.99f));
                Assert.That(moonBody.flareColor.magnitude, Is.GreaterThan(0.0f));
                Assert.That(moonBody.surfaceColor.magnitude, Is.GreaterThan(0.0f));
            }
            finally
            {
                RenderSettings.sun = originalSun;
                GameObject.DestroyImmediate(sunObject);
                GameObject.DestroyImmediate(moonObject);
            }
        }

        [Test]
        public void BuildCelestialBodyData_FallsBackToFrameDirectionalLights_WhenSceneDirectionalLightsDoNotProduceSkyLights()
        {
            var sunObject = new GameObject("Sky Sun Filtered");

            try
            {
                var sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = Color.white;
                sun.lightUnit = LightUnit.Lux;
                sun.intensity = 130000.0f;

                var sunAdditionalData = sun.GetVividAdditionalLightData();
                sunAdditionalData.interactsWithSky = false;

                var lightData = new VividLightData
                {
                    directionalLights = new[]
                    {
                        new VividLightData.DirectionalLightData
                        {
                            directionWS = Vector3.down,
                            color = new Vector3(3.0f, 2.0f, 1.0f)
                        }
                    },
                    directionalLightCount = 1,
                    mainDirectionalLightIndex = 0
                };

                var celestialBodies = new PhysicallyBasedSkyCelestialBodyData[PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies];
                var hash = PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                    new SkyRendererContext(new VividCameraData(), lightData),
                    celestialBodies,
                    out var celestialLightCount,
                    out var celestialBodyCount,
                    out var celestialLightExposure);

                Assert.That(celestialLightCount, Is.EqualTo(1));
                Assert.That(celestialBodyCount, Is.EqualTo(1));
                Assert.That(hash, Is.Not.EqualTo(13));
                Assert.That(Vector3.Distance(-celestialBodies[0].forward, Vector3.down), Is.LessThan(1e-6f));
                Assert.That(celestialBodies[0].color, Is.EqualTo(new Vector3(3.0f, 2.0f, 1.0f)));
                Assert.That(celestialLightExposure, Is.EqualTo(1.0f).Within(1e-6f));
            }
            finally
            {
                GameObject.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void Source_FallsBackWhenActualDirectionalLightsDoNotYieldCelestialLights()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyCelestialBodyData.cs"));

            Assert.That(source, Does.Contain("&& celestialLightCount > 0"));
            Assert.That(source, Does.Contain("return celestialLightCount > initialCelestialLightCount"));
            Assert.That(source, Does.Contain("|| celestialBodyCount > initialCelestialBodyCount;"));
            Assert.That(source, Does.Not.Contain("return hasDirectionalLights;"));
        }

        [Test]
        public void Source_SynchronizesAtmosphereLutMaterialParametersWithCelestialBodyBuffer()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyAtmosphereLutCache.cs"));

            Assert.That(source, Does.Contain("m_MaterialParameters.celestialLightCount = m_CelestialBodyBuffer.CelestialLightCount;"));
            Assert.That(source, Does.Contain("m_MaterialParameters.celestialBodyCount = m_CelestialBodyBuffer.CelestialBodyCount;"));
            Assert.That(source, Does.Contain("m_MaterialParameters.celestialLightExposure = Mathf.Max(m_CelestialBodyBuffer.CelestialLightExposure, 1.0f);"));
        }

        [Test]
        public void EvaluateAtmosphericAttenuation_ReturnsAtmosphericTransmittance_WhenSunIsAboveHorizon()
        {
            using var scope = new SkyProfileScope();
            scope.settings.renderingSpace.value = RenderingSpace.Camera;

            var created = PhysicallyBasedSkyAtmosphericAttenuation.TryCreate(
                scope.volume,
                scope.settings,
                Vector3.zero,
                out var context);

            Assert.That(created, Is.True);

            var attenuation = PhysicallyBasedSkyAtmosphericAttenuation.Evaluate(in context, Vector3.up);

            Assert.That(attenuation.x, Is.GreaterThan(0.0f).And.LessThan(1.0f));
            Assert.That(attenuation.y, Is.GreaterThan(0.0f).And.LessThan(1.0f));
            Assert.That(attenuation.z, Is.GreaterThan(0.0f).And.LessThan(1.0f));
            Assert.That(attenuation.x, Is.GreaterThan(attenuation.y));
            Assert.That(attenuation.y, Is.GreaterThan(attenuation.z));
        }

        [Test]
        public void EvaluateAtmosphericAttenuation_ReturnsZero_WhenSunIsBelowHorizon()
        {
            using var scope = new SkyProfileScope();
            scope.settings.renderingSpace.value = RenderingSpace.Camera;

            var created = PhysicallyBasedSkyAtmosphericAttenuation.TryCreate(
                scope.volume,
                scope.settings,
                Vector3.zero,
                out var context);

            Assert.That(created, Is.True);

            var attenuation = PhysicallyBasedSkyAtmosphericAttenuation.Evaluate(in context, Vector3.down);

            Assert.That(attenuation, Is.EqualTo(Vector3.zero));
        }

        private sealed class SkyProfileScope : IDisposable
        {
            internal readonly VolumeProfile profile;
            internal readonly SkySettingsVolume settings;
            internal readonly PhysicallyBasedSkyVolume volume;

            public SkyProfileScope()
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                settings = profile.Add<SkySettingsVolume>(false);
                volume = profile.Add<PhysicallyBasedSkyVolume>(false);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
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
