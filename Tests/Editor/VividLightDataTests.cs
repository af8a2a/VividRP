using System.Reflection;
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
        public void UpdateDirectionalLights_SelectsSunLight_WhenVisibleDirectionalSunExists()
        {
            var sunObject = new GameObject("Sun Directional Light");
            var fillObject = new GameObject("Fill Directional Light");
            var pointObject = new GameObject("Point Light");
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.Temp);
            var lightData = new VividLightData();

            var sunLight = sunObject.AddComponent<Light>();
            var fillLight = fillObject.AddComponent<Light>();
            var pointLight = pointObject.AddComponent<Light>();

            sunLight.type = LightType.Directional;
            sunLight.color = Color.white;
            sunLight.intensity = 2.0f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.7f;
            sunObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            fillLight.type = LightType.Directional;
            fillLight.color = Color.red;
            fillLight.intensity = 0.5f;
            fillObject.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

            pointLight.type = LightType.Point;

            try
            {
                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    Color.white,
                    pointObject.transform.localToWorldMatrix,
                    range: 6.0f,
                    light: pointLight);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Directional,
                    Color.white,
                    sunObject.transform.localToWorldMatrix,
                    light: sunLight);
                visibleLights[2] = CreateVisibleLight(
                    LightType.Directional,
                    Color.red,
                    fillObject.transform.localToWorldMatrix,
                    light: fillLight);

                lightData.UpdateDirectionalLights(visibleLights, sunLight);

                Assert.That(lightData.directionalLightCount, Is.EqualTo(2));
                Assert.That(lightData.hasDirectionalLights, Is.True);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(sunLight.GetEntityId()));
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(sunLight.GetEntityId()));
                AssertDirectionalLight(lightData.directionalLights[0], -sunObject.transform.forward, new Vector3(2.0f, 2.0f, 2.0f), 0.7f, (uint)sunLight.renderingLayerMask);
                AssertDirectionalLight(lightData.directionalLights[1], -fillObject.transform.forward, new Vector3(0.5f, 0.0f, 0.0f), 0.0f, (uint)fillLight.renderingLayerMask);
            }
            finally
            {
                visibleLights.Dispose();
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(fillObject);
                Object.DestroyImmediate(pointObject);
            }
        }

        [Test]
        public void UpdateDirectionalLights_SelectsBrightestDirectional_WhenSunLightIsUnavailable()
        {
            var hiddenSunObject = new GameObject("Hidden Sun Directional Light");
            var fillObject = new GameObject("Fill Directional Light");
            var keyObject = new GameObject("Key Directional Light");
            var pointObject = new GameObject("Point Light");
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.Temp);
            var lightData = new VividLightData();

            var hiddenSun = hiddenSunObject.AddComponent<Light>();
            var fillLight = fillObject.AddComponent<Light>();
            var keyLight = keyObject.AddComponent<Light>();
            var pointLight = pointObject.AddComponent<Light>();

            hiddenSun.type = LightType.Directional;
            hiddenSun.enabled = false;

            fillLight.type = LightType.Directional;
            fillLight.color = Color.blue;
            fillLight.intensity = 0.5f;
            fillObject.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.5f;
            keyObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            pointLight.type = LightType.Point;

            try
            {
                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    Color.white,
                    pointObject.transform.localToWorldMatrix,
                    range: 4.0f,
                    light: pointLight);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Directional,
                    Color.blue,
                    fillObject.transform.localToWorldMatrix,
                    light: fillLight);
                visibleLights[2] = CreateVisibleLight(
                    LightType.Directional,
                    Color.white,
                    keyObject.transform.localToWorldMatrix,
                    light: keyLight);

                lightData.UpdateDirectionalLights(visibleLights, hiddenSun);

                Assert.That(lightData.directionalLightCount, Is.EqualTo(2));
                Assert.That(lightData.mainLightIndex, Is.EqualTo(2));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(keyLight.GetEntityId()));
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(1));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(keyLight.GetEntityId()));
            }
            finally
            {
                visibleLights.Dispose();
                Object.DestroyImmediate(hiddenSunObject);
                Object.DestroyImmediate(fillObject);
                Object.DestroyImmediate(keyObject);
                Object.DestroyImmediate(pointObject);
            }
        }

        [Test]
        public void UpdatePunctualLights_CollectsPointAndSpotVisibleLights_WhenNativeArrayIsProvided()
        {
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.Temp);
            var lightData = new VividLightData();

            try
            {
                visibleLights[0] = CreateVisibleLight(
                    LightType.Directional,
                    Color.white,
                    Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(Vector3.forward), Vector3.one));
                visibleLights[1] = CreateVisibleLight(
                    LightType.Point,
                    new Color(0.25f, 0.5f, 0.75f),
                    Matrix4x4.TRS(new Vector3(1.0f, 2.0f, 3.0f), Quaternion.identity, Vector3.one),
                    range: 8.0f);
                visibleLights[2] = CreateVisibleLight(
                    LightType.Spot,
                    new Color(0.9f, 0.4f, 0.1f),
                    Matrix4x4.TRS(new Vector3(-1.0f, 0.5f, 2.0f), Quaternion.LookRotation(Vector3.right), Vector3.one),
                    range: 12.0f,
                    spotAngle: 50.0f,
                    innerSpotAngle: 30.0f);

                lightData.UpdatePunctualLights(visibleLights);

                Assert.That(lightData.punctualLightCount, Is.EqualTo(2));
                Assert.That(lightData.hasPunctualLights, Is.True);
                AssertPunctualLight(lightData.punctualLights[0], new Vector3(1.0f, 2.0f, 3.0f), new Vector3(0.25f, 0.5f, 0.75f), 8.0f, 0u, Vector3.forward);
                AssertPunctualLight(lightData.punctualLights[1], new Vector3(-1.0f, 0.5f, 2.0f), new Vector3(0.9f, 0.4f, 0.1f), 12.0f, 1u, Vector3.right);
                Assert.That(lightData.punctualLights[0].renderingLayerMask, Is.Zero);
                Assert.That(lightData.punctualLights[0].angleOffset, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(lightData.punctualLights[1].angleScale, Is.GreaterThan(0.0f));
                Assert.That(lightData.punctualLights[1].angleOffset, Is.LessThan(0.0f));
                AssertPunctualLightCullData(
                    lightData.punctualLightCullData[0],
                    new Vector3(1.0f, 2.0f, 3.0f),
                    Vector3.forward,
                    8.0f,
                    0u,
                    1.0f,
                    0.0f,
                    new Vector3(1.0f, 2.0f, 3.0f),
                    8.0f);
                GetExpectedSpotCullSphere(lightData.punctualLights[1], out var spotCullCenter, out var spotCullRadius);
                var spotOuterCos = GetSpotOuterCos(lightData.punctualLights[1]);
                AssertPunctualLightCullData(
                    lightData.punctualLightCullData[1],
                    new Vector3(-1.0f, 0.5f, 2.0f),
                    Vector3.right,
                    12.0f,
                    1u,
                    spotOuterCos,
                    12.0f * Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(spotOuterCos * spotOuterCos, 1e-6f) - 1.0f, 0.0f)),
                    spotCullCenter,
                    spotCullRadius);
            }
            finally
            {
                visibleLights.Dispose();
            }
        }

        [Test]
        public void UpdateAreaLights_CollectsRectangleAndTubeVisibleLights_WhenNativeArrayIsProvided()
        {
            var rectangleObject = new GameObject("Visible Rectangle Area Light");
            var tubeObject = new GameObject("Visible Tube Area Light");
            var pointObject = new GameObject("Visible Point Light");
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.Temp);
            var lightData = new VividLightData();

            var rectangleLight = rectangleObject.AddComponent<Light>();
            var tubeLight = tubeObject.AddComponent<Light>();
            var pointLight = pointObject.AddComponent<Light>();

            rectangleLight.type = LightType.Rectangle;
            rectangleLight.color = Color.white;
            rectangleLight.intensity = 4.0f;
            rectangleLight.range = 10.0f;
            rectangleLight.areaSize = new Vector2(4.0f, 2.0f);
            rectangleObject.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            rectangleObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            tubeLight.type = LightType.Tube;
            tubeLight.color = Color.cyan;
            tubeLight.intensity = 3.0f;
            tubeLight.range = 12.0f;
            tubeLight.areaSize = new Vector2(3.0f, 1.0f);
            tubeObject.transform.position = new Vector3(-2.0f, 1.0f, 0.5f);
            tubeObject.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);

            pointLight.type = LightType.Point;
            pointLight.range = 8.0f;
            pointObject.transform.position = new Vector3(0.5f, -1.0f, 2.5f);

            try
            {
                var expectedRectangleData = VividLightRenderDatabase.instance.UpdateLightData(rectangleLight);
                var expectedTubeData = VividLightRenderDatabase.instance.UpdateLightData(tubeLight);

                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    Color.white,
                    pointObject.transform.localToWorldMatrix,
                    range: pointLight.range,
                    light: pointLight);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Rectangle,
                    Color.white,
                    rectangleObject.transform.localToWorldMatrix,
                    range: rectangleLight.range,
                    light: rectangleLight);
                visibleLights[2] = CreateVisibleLight(
                    LightType.Tube,
                    Color.cyan,
                    tubeObject.transform.localToWorldMatrix,
                    range: tubeLight.range,
                    light: tubeLight);

                lightData.UpdateAreaLights(visibleLights);

                Assert.That(lightData.areaLightCount, Is.EqualTo(2));
                Assert.That(lightData.hasAreaLights, Is.True);
                AssertAreaLight(
                    lightData.areaLights[0],
                    rectangleObject.transform.position,
                    expectedRectangleData.color,
                    rectangleLight.range,
                    rectangleObject.transform.forward,
                    rectangleObject.transform.right,
                    rectangleObject.transform.up,
                    rectangleLight.areaSize.x,
                    rectangleLight.areaSize.y,
                    1u,
                    (uint)rectangleLight.renderingLayerMask);
                AssertAreaLight(
                    lightData.areaLights[1],
                    tubeObject.transform.position,
                    expectedTubeData.color,
                    tubeLight.range,
                    tubeObject.transform.forward,
                    tubeObject.transform.right,
                    tubeObject.transform.up,
                    tubeLight.areaSize.x,
                    0.0f,
                    0u,
                    (uint)tubeLight.renderingLayerMask);
            }
            finally
            {
                visibleLights.Dispose();
                Object.DestroyImmediate(rectangleObject);
                Object.DestroyImmediate(tubeObject);
                Object.DestroyImmediate(pointObject);
            }
        }

        [Test]
        public void UpdatePunctualLightClusteredCullData_BuildsSphereBoundsAndVolume_ForPointLight()
        {
            var lightData = new VividLightData
            {
                punctualLightCullData = new[]
                {
                    new VividLightData.PunctualLightCullData
                    {
                        positionWS = new Vector3(0.0f, 0.0f, 5.0f),
                        range = 3.0f,
                        directionWS = Vector3.forward,
                        lightType = 0u,
                        cosOuterAngle = 1.0f,
                        radiusAtRange = 0.0f,
                        cullingCenterWS = new Vector3(0.0f, 0.0f, 5.0f),
                        cullingRadius = 3.0f,
                    }
                },
                punctualLightCount = 1,
            };

            lightData.UpdatePunctualLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.punctualLightBounds[0];
            var volume = lightData.punctualLightVolumeData[0];

            AssertVector3(bound.center, new Vector3(0.0f, 0.0f, 5.0f));
            AssertVector4(bound.boxAxisX, new Vector4(3.0f, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, 3.0f, 0.0f, 3.0f));
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, 3.0f));
            Assert.That(volume.lightVolume, Is.EqualTo(1u));
            Assert.That(volume.lightCategory, Is.Zero);
            Assert.That(volume.featureFlags, Is.EqualTo(4096u));
            Assert.That(volume.radiusSq, Is.EqualTo(9.0f).Within(0.0001f));
            AssertVector3(volume.lightPos, new Vector3(0.0f, 0.0f, 5.0f));
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
        }

        [Test]
        public void UpdatePunctualLightClusteredCullData_BuildsConeBoundsAndVolume_ForSpotLight()
        {
            var lightData = new VividLightData
            {
                punctualLightCullData = new[]
                {
                    new VividLightData.PunctualLightCullData
                    {
                        positionWS = new Vector3(2.0f, 0.0f, 8.0f),
                        range = 10.0f,
                        directionWS = Vector3.forward,
                        lightType = 1u,
                        cosOuterAngle = 0.5f,
                        radiusAtRange = 6.0f,
                        cullingCenterWS = new Vector3(2.0f, 0.0f, 13.0f),
                        cullingRadius = 5.0f,
                    }
                },
                punctualLightCount = 1,
            };

            lightData.UpdatePunctualLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.punctualLightBounds[0];
            var volume = lightData.punctualLightVolumeData[0];
            var sinOuterAngle = Mathf.Sqrt(0.75f);
            var tanOuterAngle = sinOuterAngle / 0.5f;
            var expectedRadius = sinOuterAngle * 10.0f;

            AssertVector3(bound.center, new Vector3(2.0f, 0.0f, 13.0f));
            AssertVector4(bound.boxAxisX, new Vector4(tanOuterAngle * 10.0f, 0.0f, 0.0f, 0.01f), 0.001f);
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, tanOuterAngle * 10.0f, 0.0f, expectedRadius), 0.001f);
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, 5.0f));
            Assert.That(volume.lightVolume, Is.EqualTo(0u));
            Assert.That(volume.radiusSq, Is.EqualTo(100.0f).Within(0.0001f));
            Assert.That(volume.cotan, Is.EqualTo(0.5f / sinOuterAngle).Within(0.001f));
            AssertVector3(volume.lightPos, new Vector3(2.0f, 0.0f, 8.0f));
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
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
                lightData.directionalLightCount = 2;
                lightData.punctualLightCount = 3;
                lightData.areaLightCount = 1;
                lightData.mainDirectionalLightIndex = 1;
                lightData.mainDirectionalLightEntityId = EntityId.FromULong(84);
                lightData.areaLights = new[] { default(VividLightData.AreaLightData) };
                lightData.punctualLightBounds = new[] { default(VividLightData.SFiniteLightBound) };
                lightData.punctualLightVolumeData = new[] { default(VividLightData.LightVolumeData) };

                lightData.Reset();

                Assert.That(lightData.visibleLights.IsCreated, Is.False);
                Assert.That(lightData.visibleReflectionProbes.IsCreated, Is.False);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.directionalLightCount, Is.Zero);
                Assert.That(lightData.punctualLightCount, Is.Zero);
                Assert.That(lightData.areaLightCount, Is.Zero);
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.areaLights, Is.Empty);
                Assert.That(lightData.punctualLightBounds, Is.Empty);
                Assert.That(lightData.punctualLightVolumeData, Is.Empty);
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
            AssertVector3(actual.directionWS, expectedDirection);
            AssertVector3(actual.color, expectedColor);
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
            AssertVector3(actual.positionWS, expectedPosition);
            AssertVector3(actual.color, expectedColor);
            Assert.That(actual.range, Is.EqualTo(expectedRange).Within(0.0001f));
            Assert.That(actual.lightType, Is.EqualTo(expectedType));
            AssertVector3(actual.directionWS, expectedDirection);
        }

        private static void AssertPunctualLightCullData(
            VividLightData.PunctualLightCullData actual,
            Vector3 expectedPosition,
            Vector3 expectedDirection,
            float expectedRange,
            uint expectedType,
            float expectedCosOuterAngle,
            float expectedRadiusAtRange,
            Vector3 expectedCenter,
            float expectedRadius)
        {
            AssertVector3(actual.positionWS, expectedPosition);
            AssertVector3(actual.directionWS, expectedDirection);
            Assert.That(actual.range, Is.EqualTo(expectedRange).Within(0.0001f));
            Assert.That(actual.lightType, Is.EqualTo(expectedType));
            Assert.That(actual.cosOuterAngle, Is.EqualTo(expectedCosOuterAngle).Within(0.0001f));
            Assert.That(actual.radiusAtRange, Is.EqualTo(expectedRadiusAtRange).Within(0.0001f));
            AssertVector3(actual.cullingCenterWS, expectedCenter);
            Assert.That(actual.cullingRadius, Is.EqualTo(expectedRadius).Within(0.0001f));
        }

        private static void AssertAreaLight(
            VividLightData.AreaLightData actual,
            Vector3 expectedPosition,
            Vector3 expectedColor,
            float expectedRange,
            Vector3 expectedForward,
            Vector3 expectedRight,
            Vector3 expectedUp,
            float expectedWidth,
            float expectedHeight,
            uint expectedType,
            uint expectedRenderingLayerMask)
        {
            AssertVector3(actual.positionWS, expectedPosition);
            AssertVector3(actual.color, expectedColor);
            Assert.That(actual.rangeAttenuationScale, Is.EqualTo(1.0f / (expectedRange * expectedRange)).Within(0.0001f));
            Assert.That(actual.rangeAttenuationBias, Is.EqualTo(1.0f).Within(0.0001f));
            AssertVector3(actual.forwardWS, expectedForward);
            AssertVector3(actual.rightWS, expectedRight);
            AssertVector3(actual.upWS, expectedUp);
            Assert.That(actual.width, Is.EqualTo(expectedWidth).Within(0.0001f));
            Assert.That(actual.height, Is.EqualTo(expectedHeight).Within(0.0001f));
            Assert.That(actual.range, Is.EqualTo(expectedRange).Within(0.0001f));
            Assert.That(actual.lightType, Is.EqualTo(expectedType));
            Assert.That(actual.renderingLayerMask, Is.EqualTo(expectedRenderingLayerMask));
        }

        private static float GetSpotOuterCos(VividLightData.PunctualLightData punctualLight)
        {
            return Mathf.Clamp01(-punctualLight.angleOffset / Mathf.Max(punctualLight.angleScale, 1e-6f));
        }

        private static void GetExpectedSpotCullSphere(
            VividLightData.PunctualLightData punctualLight,
            out Vector3 cullingCenter,
            out float cullingRadius)
        {
            var direction = punctualLight.directionWS.sqrMagnitude > 1e-6f
                ? punctualLight.directionWS.normalized
                : Vector3.forward;
            var outerCos = Mathf.Clamp01(-punctualLight.angleOffset / Mathf.Max(punctualLight.angleScale, 1e-6f));
            var tanOuter = Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(outerCos * outerCos, 1e-6f) - 1.0f, 0.0f));
            float centerDistance;

            if (tanOuter <= 1.0f)
            {
                centerDistance = 0.5f * punctualLight.range * (1.0f + tanOuter * tanOuter);
                cullingRadius = centerDistance;
            }
            else
            {
                centerDistance = punctualLight.range;
                cullingRadius = punctualLight.range * tanOuter;
            }

            cullingCenter = punctualLight.positionWS + direction * centerDistance;
        }

        private static VisibleLight CreateVisibleLight(
            LightType lightType,
            Color finalColor,
            Matrix4x4 localToWorldMatrix,
            float range = 0.0f,
            float spotAngle = 30.0f,
            float innerSpotAngle = 30.0f,
            Light light = null)
        {
            var visibleLight = default(VisibleLight);
            SetVisibleLightField(ref visibleLight, "m_LightType", lightType);
            SetVisibleLightField(ref visibleLight, "m_FinalColor", finalColor);
            SetVisibleLightField(ref visibleLight, "m_LocalToWorldMatrix", localToWorldMatrix);
            SetVisibleLightField(ref visibleLight, "m_Range", range);
            SetVisibleLightField(ref visibleLight, "m_SpotAngle", spotAngle);
            SetVisibleLightField(ref visibleLight, "m_InnerSpotAngle", innerSpotAngle);
            SetVisibleLightField(ref visibleLight, "m_EntityId", EntityId.None);

            if (light != null)
                TrySetVisibleLightField(ref visibleLight, "m_Light", light);

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

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        private static void AssertVector4(Vector4 actual, Vector4 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance));
        }
    }
}
