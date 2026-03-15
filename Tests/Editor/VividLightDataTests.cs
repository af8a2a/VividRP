using System.Collections.Generic;
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
                AssertPunctualLightCullData(
                    lightData.punctualLightCullData[0],
                    pointObject.transform.position,
                    pointObject.transform.forward,
                    pointLight.range,
                    0u,
                    1.0f,
                    0.0f,
                    pointObject.transform.position,
                    pointLight.range);
                GetExpectedSpotCullSphere(
                    lightData.punctualLights[1],
                    out var spotCullCenter,
                    out var spotCullRadius);
                var spotOuterCos = GetSpotOuterCos(lightData.punctualLights[1]);
                AssertPunctualLightCullData(
                    lightData.punctualLightCullData[1],
                    spotObject.transform.position,
                    spotObject.transform.forward,
                    spotLight.range,
                    1u,
                    spotOuterCos,
                    spotLight.range * Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(spotOuterCos * spotOuterCos, 1e-6f) - 1.0f, 0.0f)),
                    spotCullCenter,
                    spotCullRadius);
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
        public void UpdateDirectionalLights_CollectsDirectionalVisibleLights_WhenNativeArrayIsProvided()
        {
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.Temp);
            var lightData = new VividLightData();

            try
            {
                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    Color.green,
                    Matrix4x4.TRS(new Vector3(1.0f, 2.0f, 3.0f), Quaternion.identity, Vector3.one),
                    range: 6.0f);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Directional,
                    new Color(1.5f, 1.0f, 0.5f),
                    Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(Vector3.forward), Vector3.one));
                visibleLights[2] = CreateVisibleLight(
                    LightType.Directional,
                    new Color(0.5f, 0.25f, 0.125f),
                    Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(Vector3.right), Vector3.one));

                lightData.UpdateDirectionalLights(visibleLights, null);

                Assert.That(lightData.directionalLightCount, Is.EqualTo(2));
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(lightData.mainLightIndex, Is.EqualTo(1));
                AssertDirectionalLight(lightData.directionalLights[0], -Vector3.forward, new Vector3(1.5f, 1.0f, 0.5f), 0.0f, 0u);
                AssertDirectionalLight(lightData.directionalLights[1], -Vector3.right, new Vector3(0.5f, 0.25f, 0.125f), 0.0f, 0u);
            }
            finally
            {
                visibleLights.Dispose();
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
                AssertPunctualLight(lightData.punctualLights[0], new Vector3(1.0f, 2.0f, 3.0f), new Vector3(0.25f, 0.5f, 0.75f), 8.0f, 0u, Vector3.forward);
                AssertPunctualLight(lightData.punctualLights[1], new Vector3(-1.0f, 0.5f, 2.0f), new Vector3(0.9f, 0.4f, 0.1f), 12.0f, 1u, Vector3.right);
                Assert.That(lightData.punctualLights[0].renderingLayerMask, Is.Zero);
                Assert.That(lightData.punctualLights[1].angleScale, Is.GreaterThan(0.0f));
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
                GetExpectedSpotCullSphere(
                    lightData.punctualLights[1],
                    out var spotCullCenter,
                    out var spotCullRadius);
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
        public void UpdatePunctualLightScreenSpaceBounds_ComputesPerspectiveViewClipSliceAndTileBounds()
        {
            var cameraObject = new GameObject("Screen Space Bounds Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var lightData = new VividLightData();

            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;

            lightData.punctualLightCullData = new[]
            {
                new VividLightData.PunctualLightCullData
                {
                    positionWS = new Vector3(0.0f, 0.0f, 5.0f),
                    range = 1.0f,
                    directionWS = Vector3.forward,
                    lightType = 0u,
                    cosOuterAngle = 1.0f,
                    radiusAtRange = 0.0f,
                    cullingCenterWS = new Vector3(0.0f, 0.0f, 5.0f),
                    cullingRadius = 1.0f,
                }
            };
            lightData.punctualLightCount = 1;

            try
            {
                var parameters = VividLightData.CreatePunctualLightScreenSpaceBoundsParameters(camera, 320, 180, 32, 24);

                lightData.UpdatePunctualLightScreenSpaceBounds(parameters);

                var bounds = lightData.punctualLightScreenSpaceBounds[0];
                Assert.That(bounds.isValid, Is.EqualTo(1u));
                Assert.That(bounds.viewSpaceAabbMin.x, Is.EqualTo(-1.0f).Within(0.0001f));
                Assert.That(bounds.viewSpaceAabbMin.y, Is.EqualTo(-1.0f).Within(0.0001f));
                Assert.That(bounds.viewSpaceAabbMin.z, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(bounds.viewSpaceAabbMax.x, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(bounds.viewSpaceAabbMax.y, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(bounds.viewSpaceAabbMax.z, Is.EqualTo(6.0f).Within(0.0001f));
                Assert.That(bounds.clipSpaceAabbMin.x, Is.EqualTo(-0.2436f).Within(0.001f));
                Assert.That(bounds.clipSpaceAabbMax.x, Is.EqualTo(0.2436f).Within(0.001f));
                Assert.That(bounds.clipSpaceAabbMin.y, Is.EqualTo(-0.4330f).Within(0.001f));
                Assert.That(bounds.clipSpaceAabbMax.y, Is.EqualTo(0.4330f).Within(0.001f));
                Assert.That(bounds.sliceMin, Is.EqualTo(7));
                Assert.That(bounds.sliceMax, Is.EqualTo(8));
                Assert.That(bounds.tileMinX, Is.EqualTo(3));
                Assert.That(bounds.tileMaxX, Is.EqualTo(6));
                Assert.That(bounds.tileMinY, Is.EqualTo(1));
                Assert.That(bounds.tileMaxY, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void UpdatePunctualLightCoarseCullingData_BuildsSliceRangesAndRecords_FromScreenSpaceBounds()
        {
            var lightData = new VividLightData
            {
                punctualLightScreenSpaceBounds = new[]
                {
                    new VividLightData.PunctualLightScreenSpaceBounds
                    {
                        sliceMin = 0,
                        sliceMax = 1,
                        tileMinX = 1,
                        tileMaxX = 2,
                        tileMinY = 3,
                        tileMaxY = 4,
                        isValid = 1u,
                    },
                    new VividLightData.PunctualLightScreenSpaceBounds
                    {
                        isValid = 0u,
                    },
                    new VividLightData.PunctualLightScreenSpaceBounds
                    {
                        sliceMin = 1,
                        sliceMax = 2,
                        tileMinX = 4,
                        tileMaxX = 5,
                        tileMinY = 6,
                        tileMaxY = 7,
                        isValid = 1u,
                    }
                },
                punctualLightCount = 3,
            };

            lightData.UpdatePunctualLightCoarseCullingData(4);

            Assert.That(lightData.punctualLightCoarseRangeCount, Is.EqualTo(4));
            Assert.That(lightData.punctualLightCoarseRecordCount, Is.EqualTo(4));
            AssertPunctualLightCoarseRange(lightData.punctualLightCoarseRanges[0], 0, 1);
            AssertPunctualLightCoarseRange(lightData.punctualLightCoarseRanges[1], 1, 2);
            AssertPunctualLightCoarseRange(lightData.punctualLightCoarseRanges[2], 3, 1);
            AssertPunctualLightCoarseRange(lightData.punctualLightCoarseRanges[3], 4, 0);
            AssertPunctualLightCoarseRecord(lightData.punctualLightCoarseRecords[0], 0, 1, 2, 3, 4);
            AssertPunctualLightCoarseRecord(lightData.punctualLightCoarseRecords[1], 0, 1, 2, 3, 4);
            AssertPunctualLightCoarseRecord(lightData.punctualLightCoarseRecords[2], 2, 4, 5, 6, 7);
            AssertPunctualLightCoarseRecord(lightData.punctualLightCoarseRecords[3], 2, 4, 5, 6, 7);
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
                lightData.punctualLightScreenSpaceBounds = new[] { default(VividLightData.PunctualLightScreenSpaceBounds) };
                lightData.punctualLightCoarseRanges = new[] { default(VividLightData.PunctualLightCoarseRange) };
                lightData.punctualLightCoarseRecords = new[] { default(VividLightData.PunctualLightCoarseRecord) };
                lightData.punctualLightCoarseRangeCount = 1;
                lightData.punctualLightCoarseRecordCount = 1;

                lightData.Reset();

                Assert.That(lightData.visibleLights.IsCreated, Is.False);
                Assert.That(lightData.visibleReflectionProbes.IsCreated, Is.False);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.punctualLightCount, Is.Zero);
                Assert.That(lightData.punctualLightScreenSpaceBounds, Is.Empty);
                Assert.That(lightData.punctualLightCoarseRanges, Is.Empty);
                Assert.That(lightData.punctualLightCoarseRecords, Is.Empty);
                Assert.That(lightData.punctualLightCoarseRangeCount, Is.Zero);
                Assert.That(lightData.punctualLightCoarseRecordCount, Is.Zero);
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
            Assert.That(actual.positionWS.x, Is.EqualTo(expectedPosition.x).Within(0.0001f));
            Assert.That(actual.positionWS.y, Is.EqualTo(expectedPosition.y).Within(0.0001f));
            Assert.That(actual.positionWS.z, Is.EqualTo(expectedPosition.z).Within(0.0001f));
            Assert.That(actual.directionWS.x, Is.EqualTo(expectedDirection.x).Within(0.0001f));
            Assert.That(actual.directionWS.y, Is.EqualTo(expectedDirection.y).Within(0.0001f));
            Assert.That(actual.directionWS.z, Is.EqualTo(expectedDirection.z).Within(0.0001f));
            Assert.That(actual.range, Is.EqualTo(expectedRange).Within(0.0001f));
            Assert.That(actual.lightType, Is.EqualTo(expectedType));
            Assert.That(actual.cosOuterAngle, Is.EqualTo(expectedCosOuterAngle).Within(0.0001f));
            Assert.That(actual.radiusAtRange, Is.EqualTo(expectedRadiusAtRange).Within(0.0001f));
            Assert.That(actual.cullingCenterWS.x, Is.EqualTo(expectedCenter.x).Within(0.0001f));
            Assert.That(actual.cullingCenterWS.y, Is.EqualTo(expectedCenter.y).Within(0.0001f));
            Assert.That(actual.cullingCenterWS.z, Is.EqualTo(expectedCenter.z).Within(0.0001f));
            Assert.That(actual.cullingRadius, Is.EqualTo(expectedRadius).Within(0.0001f));
        }

        private static void AssertPunctualLightCoarseRange(
            VividLightData.PunctualLightCoarseRange actual,
            int expectedStartIndex,
            int expectedLightCount)
        {
            Assert.That(actual.startIndex, Is.EqualTo(expectedStartIndex));
            Assert.That(actual.lightCount, Is.EqualTo(expectedLightCount));
        }

        private static void AssertPunctualLightCoarseRecord(
            VividLightData.PunctualLightCoarseRecord actual,
            int expectedLightIndex,
            int expectedTileMinX,
            int expectedTileMaxX,
            int expectedTileMinY,
            int expectedTileMaxY)
        {
            Assert.That(actual.lightIndex, Is.EqualTo(expectedLightIndex));
            Assert.That(actual.tileMinX, Is.EqualTo(expectedTileMinX));
            Assert.That(actual.tileMaxX, Is.EqualTo(expectedTileMaxX));
            Assert.That(actual.tileMinY, Is.EqualTo(expectedTileMinY));
            Assert.That(actual.tileMaxY, Is.EqualTo(expectedTileMaxY));
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
            float innerSpotAngle = 30.0f)
        {
            var visibleLight = default(VisibleLight);
            SetVisibleLightField(ref visibleLight, "m_LightType", lightType);
            SetVisibleLightField(ref visibleLight, "m_FinalColor", finalColor);
            SetVisibleLightField(ref visibleLight, "m_LocalToWorldMatrix", localToWorldMatrix);
            SetVisibleLightField(ref visibleLight, "m_Range", range);
            SetVisibleLightField(ref visibleLight, "m_SpotAngle", spotAngle);
            SetVisibleLightField(ref visibleLight, "m_InnerSpotAngle", innerSpotAngle);
            SetVisibleLightField(ref visibleLight, "m_EntityId", EntityId.None);
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
    }
}
