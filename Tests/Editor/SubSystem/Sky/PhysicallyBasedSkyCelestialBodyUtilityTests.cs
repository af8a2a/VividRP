using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
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
        public void BuildCelestialBodyData_UsesLightColorTemperature_WhenVisibleLightHasStaleTint()
        {
            var sunObject = new GameObject("Visible Sky Sun");
            NativeArray<VisibleLight> visibleLights = default;

            try
            {
                var sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1.0f, 0.9431372f, 0.9f, 1.0f);
                sun.lightUnit = LightUnit.Lux;
                sun.intensity = 130000.0f;
                sun.useColorTemperature = true;
                sun.colorTemperature = 5500.0f;

                var visibleFinalColor = sun.color.linear * Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature) * sun.intensity;
                var expected = Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature) * sun.intensity;
                visibleLights = new NativeArray<VisibleLight>(1, Allocator.Temp);
                visibleLights[0] = CreateVisibleDirectionalLight(sun, visibleFinalColor);

                var lightData = new VividLightData
                {
                    visibleLights = visibleLights
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
                Assert.That(celestialBodies[0].color.x, Is.EqualTo(expected.r).Within(0.1f));
                Assert.That(celestialBodies[0].color.y, Is.EqualTo(expected.g).Within(0.1f));
                Assert.That(celestialBodies[0].color.z, Is.EqualTo(expected.b).Within(0.1f));
                Assert.That(celestialLightExposure, Is.EqualTo(expected.r).Within(0.1f));
            }
            finally
            {
                if (visibleLights.IsCreated)
                    visibleLights.Dispose();

                GameObject.DestroyImmediate(sunObject);
            }
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
            Assert.That(source, Does.Contain("internal bool SkyViewLutRebuiltThisFrame => m_SkyViewLutRebuiltThisFrame;"));
            Assert.That(source, Does.Contain("internal void Update(in SkyRendererContext context, CommandBuffer cmd, bool forceSkyViewRebuild = false)"));
            Assert.That(source, Does.Contain("if (forceSkyViewRebuild)"));
            Assert.That(source, Does.Contain("m_SkyViewLutRebuiltThisFrame = true;"));
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

        private static VisibleLight CreateVisibleDirectionalLight(Light light, Color finalColor)
        {
            var visibleLight = default(VisibleLight);
            SetVisibleLightField(ref visibleLight, "m_LightType", LightType.Directional);
            SetVisibleLightField(ref visibleLight, "m_FinalColor", finalColor);
            SetVisibleLightField(ref visibleLight, "m_LocalToWorldMatrix", light.transform.localToWorldMatrix);
            SetVisibleLightField(ref visibleLight, "m_Range", light.range);
            SetVisibleLightField(ref visibleLight, "m_SpotAngle", light.spotAngle);
            SetVisibleLightField(ref visibleLight, "m_InnerSpotAngle", light.innerSpotAngle);
            TrySetVisibleLightField(ref visibleLight, "m_Light", light);
            TrySetVisibleLightField(ref visibleLight, "m_EntityId", EntityId.None);
            return visibleLight;
        }

        private static void SetVisibleLightField<T>(ref VisibleLight visibleLight, string fieldName, T value)
        {
            var field = typeof(VisibleLight).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Expected VisibleLight to contain field '{fieldName}'.");

            object boxedVisibleLight = visibleLight;
            field.SetValue(boxedVisibleLight, value);
            visibleLight = (VisibleLight)boxedVisibleLight;
        }

        private static void TrySetVisibleLightField<T>(ref VisibleLight visibleLight, string fieldName, T value)
        {
            var field = typeof(VisibleLight).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                return;

            object boxedVisibleLight = visibleLight;
            field.SetValue(boxedVisibleLight, value);
            visibleLight = (VisibleLight)boxedVisibleLight;
        }
    }
}
