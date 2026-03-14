using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividLightDataTests
    {
        [Test]
        public void UpdateDirectionalLights_CollectsEnabledDirectionalLights_WhenLightsAreProvided()
        {
            var sunObject = new GameObject("Sun Directional Light");
            var fillObject = new GameObject("Fill Directional Light");
            var disabledDirectionalObject = new GameObject("Disabled Directional Light");
            var pointLightObject = new GameObject("Point Light");

            var sunLight = sunObject.AddComponent<Light>();
            var fillLight = fillObject.AddComponent<Light>();
            var disabledDirectionalLight = disabledDirectionalObject.AddComponent<Light>();
            var pointLight = pointLightObject.AddComponent<Light>();

            sunLight.type = LightType.Directional;
            sunLight.color = Color.white;
            sunLight.intensity = 2.0f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.7f;
            sunObject.transform.forward = Vector3.forward;

            fillLight.type = LightType.Directional;
            fillLight.color = Color.red;
            fillLight.intensity = 0.5f;
            fillObject.transform.forward = Vector3.right;

            disabledDirectionalLight.type = LightType.Directional;
            disabledDirectionalLight.enabled = false;

            pointLight.type = LightType.Point;

            var lightData = new VividLightData();

            try
            {
                lightData.UpdateDirectionalLights(new[] { pointLight, sunLight, disabledDirectionalLight, fillLight }, sunLight);

                Assert.That(lightData.directionalLightCount, Is.EqualTo(2));
                Assert.That(lightData.hasDirectionalLights, Is.True);
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(sunLight.GetEntityId()));
                AssertDirectionalLight(lightData.directionalLights[0], -sunObject.transform.forward, new Vector3(2.0f, 2.0f, 2.0f), 0.7f, (uint)sunLight.renderingLayerMask);
                AssertDirectionalLight(lightData.directionalLights[1], -fillObject.transform.forward, new Vector3(0.5f, 0.0f, 0.0f), 0.0f, (uint)fillLight.renderingLayerMask);
            }
            finally
            {
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(fillObject);
                Object.DestroyImmediate(disabledDirectionalObject);
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void UpdateDirectionalLights_SelectsBrightestDirectional_WhenSunLightIsUnavailable()
        {
            var keyLightObject = new GameObject("Key Directional Light");
            var fillLightObject = new GameObject("Fill Directional Light");
            var hiddenSunObject = new GameObject("Sun Directional Light");

            var keyLight = keyLightObject.AddComponent<Light>();
            var fillLight = fillLightObject.AddComponent<Light>();
            var hiddenSun = hiddenSunObject.AddComponent<Light>();

            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.5f;

            fillLight.type = LightType.Directional;
            fillLight.color = Color.blue;
            fillLight.intensity = 0.5f;

            hiddenSun.type = LightType.Directional;
            hiddenSun.enabled = false;

            var lightData = new VividLightData();

            try
            {
                lightData.UpdateDirectionalLights(new[] { fillLight, hiddenSun, keyLight }, hiddenSun);

                Assert.That(lightData.directionalLightCount, Is.EqualTo(2));
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(1));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(keyLight.GetEntityId()));
            }
            finally
            {
                Object.DestroyImmediate(keyLightObject);
                Object.DestroyImmediate(fillLightObject);
                Object.DestroyImmediate(hiddenSunObject);
            }
        }

        [Test]
        public void UpdatePunctualLights_CollectsEnabledPointAndSpotLights_WhenLightsAreProvided()
        {
            var pointObject = new GameObject("Point Light");
            var spotObject = new GameObject("Spot Light");
            var directionalObject = new GameObject("Directional Light");
            var disabledSpotObject = new GameObject("Disabled Spot Light");

            var pointLight = pointObject.AddComponent<Light>();
            var spotLight = spotObject.AddComponent<Light>();
            var directionalLight = directionalObject.AddComponent<Light>();
            var disabledSpotLight = disabledSpotObject.AddComponent<Light>();

            pointLight.type = LightType.Point;
            pointLight.color = Color.cyan;
            pointLight.intensity = 3.0f;
            pointLight.range = 8.0f;

            spotLight.type = LightType.Spot;
            spotLight.color = Color.yellow;
            spotLight.intensity = 2.0f;
            spotLight.range = 12.0f;
            spotLight.innerSpotAngle = 30.0f;
            spotLight.spotAngle = 50.0f;
            spotObject.transform.forward = Vector3.forward;

            directionalLight.type = LightType.Directional;

            disabledSpotLight.type = LightType.Spot;
            disabledSpotLight.enabled = false;

            var lightData = new VividLightData();

            try
            {
                lightData.UpdatePunctualLights(new[] { directionalLight, pointLight, disabledSpotLight, spotLight });

                Assert.That(lightData.punctualLightCount, Is.EqualTo(2));
                Assert.That(lightData.hasPunctualLights, Is.True);
                AssertPunctualLight(
                    lightData.punctualLights[0],
                    pointObject.transform.position,
                    new Vector3(0.0f, 3.0f, 3.0f),
                    pointLight.range,
                    0u,
                    pointObject.transform.forward);
                AssertPunctualLight(
                    lightData.punctualLights[1],
                    spotObject.transform.position,
                    new Vector3(2.0f, 2.0f, 0.0f),
                    spotLight.range,
                    1u,
                    spotObject.transform.forward);
                Assert.That(lightData.punctualLights[0].angleOffset, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(lightData.punctualLights[0].inverseRangeSquared, Is.EqualTo(1.0f / (pointLight.range * pointLight.range)).Within(0.0001f));
                Assert.That(lightData.punctualLights[1].angleScale, Is.GreaterThan(0.0f));
                Assert.That(lightData.punctualLights[1].angleOffset, Is.LessThan(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(pointObject);
                Object.DestroyImmediate(spotObject);
                Object.DestroyImmediate(directionalObject);
                Object.DestroyImmediate(disabledSpotObject);
            }
        }

        [Test]
        public void FindMainLightIndex_ReturnsSunLight_WhenVisibleDirectionalSunExists()
        {
            var sunObject = new GameObject("Sun Light Test");
            var sunLight = sunObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;

            try
            {
                var visibleLights = new List<VividLightData.VisibleLightDescriptor>
                {
                    new(EntityId.FromULong(1), LightType.Directional, new Color(0.4f, 0.4f, 0.4f)),
                    new(sunLight.GetEntityId(), LightType.Directional, Color.black),
                    new(EntityId.FromULong(3), LightType.Point, Color.white)
                };

                var mainLightIndex = VividLightData.FindMainLightIndex(visibleLights, sunLight.GetEntityId());

                Assert.That(mainLightIndex, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void FindMainLightIndex_ReturnsBrightestDirectional_WhenSunIsNotVisible()
        {
            var visibleLights = new List<VividLightData.VisibleLightDescriptor>
            {
                new(EntityId.FromULong(1), LightType.Point, new Color(2.0f, 2.0f, 2.0f)),
                new(EntityId.FromULong(2), LightType.Directional, new Color(0.5f, 0.4f, 0.3f)),
                new(EntityId.FromULong(3), LightType.Directional, new Color(0.8f, 0.7f, 0.6f))
            };

            var mainLightIndex = VividLightData.FindMainLightIndex(visibleLights, EntityId.FromULong(99));

            Assert.That(mainLightIndex, Is.EqualTo(2));
        }

        [Test]
        public void FindMainLightIndex_ReturnsNegativeOne_WhenNoDirectionalLightsAreVisible()
        {
            var visibleLights = new List<VividLightData.VisibleLightDescriptor>
            {
                new(EntityId.FromULong(1), LightType.Point, Color.white),
                new(EntityId.FromULong(2), LightType.Spot, Color.gray)
            };

            var mainLightIndex = VividLightData.FindMainLightIndex(visibleLights, EntityId.None);

            Assert.That(mainLightIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Reset_ClearsCachedLightState_WhenLightDataWasInitialized()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(0, Allocator.Temp);
            var visibleReflectionProbes = new NativeArray<VisibleReflectionProbe>(0, Allocator.Temp);

            try
            {
                lightData.visibleLights = visibleLights;
                lightData.visibleReflectionProbes = visibleReflectionProbes;
                lightData.mainLightIndex = 2;
                lightData.mainLightEntityId = EntityId.FromULong(42);
                lightData.punctualLightCount = 3;

                lightData.Reset();

                Assert.That(lightData.visibleLights.IsCreated, Is.False);
                Assert.That(lightData.visibleReflectionProbes.IsCreated, Is.False);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.punctualLightCount, Is.Zero);
            }
            finally
            {
                visibleLights.Dispose();
                visibleReflectionProbes.Dispose();
            }
        }

        private static void AssertDirectionalLight(
            VividLightData.DirectionalLightData actual,
            Vector3 expectedDirection,
            Vector3 expectedColor,
            float expectedShadowStrength,
            uint expectedRenderingLayerMask)
        {
            Assert.That(actual.directionWS.x, Is.EqualTo(expectedDirection.x).Within(0.0001f));
            Assert.That(actual.directionWS.y, Is.EqualTo(expectedDirection.y).Within(0.0001f));
            Assert.That(actual.directionWS.z, Is.EqualTo(expectedDirection.z).Within(0.0001f));
            Assert.That(actual.color.x, Is.EqualTo(expectedColor.x).Within(0.0001f));
            Assert.That(actual.color.y, Is.EqualTo(expectedColor.y).Within(0.0001f));
            Assert.That(actual.color.z, Is.EqualTo(expectedColor.z).Within(0.0001f));
            Assert.That(actual.shadowStrength, Is.EqualTo(expectedShadowStrength).Within(0.0001f));
            Assert.That(actual.renderingLayerMask, Is.EqualTo(expectedRenderingLayerMask));
        }

        private static void AssertPunctualLight(
            VividLightData.PunctualLightData actual,
            Vector3 expectedPosition,
            Vector3 expectedColor,
            float expectedRange,
            uint expectedType,
            Vector3 expectedDirection)
        {
            Assert.That(actual.positionWS.x, Is.EqualTo(expectedPosition.x).Within(0.0001f));
            Assert.That(actual.positionWS.y, Is.EqualTo(expectedPosition.y).Within(0.0001f));
            Assert.That(actual.positionWS.z, Is.EqualTo(expectedPosition.z).Within(0.0001f));
            Assert.That(actual.color.x, Is.EqualTo(expectedColor.x).Within(0.0001f));
            Assert.That(actual.color.y, Is.EqualTo(expectedColor.y).Within(0.0001f));
            Assert.That(actual.color.z, Is.EqualTo(expectedColor.z).Within(0.0001f));
            Assert.That(actual.range, Is.EqualTo(expectedRange).Within(0.0001f));
            Assert.That(actual.lightType, Is.EqualTo(expectedType));
            Assert.That(actual.directionWS.x, Is.EqualTo(expectedDirection.x).Within(0.0001f));
            Assert.That(actual.directionWS.y, Is.EqualTo(expectedDirection.y).Within(0.0001f));
            Assert.That(actual.directionWS.z, Is.EqualTo(expectedDirection.z).Within(0.0001f));
        }
    }
}
