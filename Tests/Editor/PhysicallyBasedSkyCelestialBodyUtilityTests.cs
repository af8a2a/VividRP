using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyCelestialBodyUtilityTests
    {
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
    }
}
