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

                lightData.Reset();

                Assert.That(lightData.visibleLights.IsCreated, Is.False);
                Assert.That(lightData.visibleReflectionProbes.IsCreated, Is.False);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(EntityId.None));
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
    }
}
