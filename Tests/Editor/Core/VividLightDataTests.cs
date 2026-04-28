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
        private static readonly MethodInfo s_CreateDirectionalLightDataMethod =
            typeof(VividLightData).GetMethod("CreateDirectionalLightData", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo s_CreatePunctualLightDataMethod =
            typeof(VividLightData).GetMethod("CreatePunctualLightData", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo s_CreateAreaLightDataMethod =
            typeof(VividLightData).GetMethod("CreateAreaLightData", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void LightDataStructs_UseExpectedVolumetricStrides()
        {
            Assert.That(VividLightData.DirectionalLightData.Stride, Is.EqualTo(48));
            Assert.That(VividLightData.PunctualLightData.Stride, Is.EqualTo(112));
            Assert.That(VividLightData.AreaLightData.Stride, Is.EqualTo(112));
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
                        affectVolumetric = 1u,
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
            Assert.That(volume.affectVolumetric, Is.EqualTo(1));
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
        public void UpdateAreaLightClusteredCullData_BuildsBoxBoundsAndVolume_ForRectangleLight()
        {
            var lightData = new VividLightData
            {
                areaLights = new[]
                {
                    new VividLightData.AreaLightData
                    {
                        positionWS = new Vector3(1.0f, -2.0f, 6.0f),
                        forwardWS = Vector3.forward,
                        rightWS = Vector3.right,
                        upWS = Vector3.up,
                        width = 4.0f,
                        height = 2.0f,
                        lightType = 1u,
                        range = 8.0f,
                        volumetricDimmer = 1.0f,
                        affectVolumetric = 1u,
                    }
                },
                areaLightCount = 1,
            };

            lightData.UpdateAreaLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.areaLightBounds[0];
            var volume = lightData.areaLightVolumeData[0];
            var extents = new Vector3(6.0f, 5.0f, 4.0f);
            var diagonalRadius = 8.0f + 0.5f * Mathf.Sqrt(20.0f);
            var expectedRadius = Mathf.Sqrt(diagonalRadius * diagonalRadius + 16.0f);

            AssertVector3(bound.center, new Vector3(1.0f, -2.0f, 10.0f));
            AssertVector4(bound.boxAxisX, new Vector4(extents.x, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, extents.y, 0.0f, expectedRadius), 0.001f);
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, extents.z));
            Assert.That(volume.lightVolume, Is.EqualTo(2u));
            Assert.That(volume.lightCategory, Is.EqualTo(1u));
            Assert.That(volume.featureFlags, Is.EqualTo(8192u));
            Assert.That(volume.affectVolumetric, Is.EqualTo(1));
            AssertVector3(volume.lightPos, new Vector3(1.0f, -2.0f, 10.0f));
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
            AssertVector3(volume.boxInvRange, new Vector3(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z));
        }

        [Test]
        public void UpdateAreaLightClusteredCullData_BuildsTubeBoundsAndVolume_ForTubeLight()
        {
            var lightData = new VividLightData
            {
                areaLights = new[]
                {
                    new VividLightData.AreaLightData
                    {
                        positionWS = new Vector3(-3.0f, 1.0f, 5.0f),
                        forwardWS = Vector3.forward,
                        rightWS = Vector3.right,
                        upWS = Vector3.up,
                        width = 6.0f,
                        height = 0.0f,
                        lightType = 0u,
                        range = 4.0f,
                    }
                },
                areaLightCount = 1,
            };

            lightData.UpdateAreaLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.areaLightBounds[0];
            var volume = lightData.areaLightVolumeData[0];
            var extents = new Vector3(7.0f, 4.0f, 4.0f);

            AssertVector3(bound.center, new Vector3(-3.0f, 1.0f, 5.0f));
            AssertVector4(bound.boxAxisX, new Vector4(extents.x, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, extents.y, 0.0f, extents.x));
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, extents.z));
            Assert.That(volume.lightVolume, Is.EqualTo(2u));
            Assert.That(volume.lightCategory, Is.EqualTo(1u));
            Assert.That(volume.featureFlags, Is.EqualTo(8192u));
            AssertVector3(volume.lightPos, new Vector3(-3.0f, 1.0f, 5.0f));
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
            AssertVector3(volume.boxInvRange, new Vector3(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z));
        }

        [Test]
        public void UpdateAreaLightClusteredCullData_KeepsRectangleBoundsConservative_WhenBarnDoorIsActive()
        {
            var lightData = new VividLightData
            {
                areaLights = new[]
                {
                    new VividLightData.AreaLightData
                    {
                        positionWS = new Vector3(1.0f, -2.0f, 6.0f),
                        forwardWS = Vector3.forward,
                        rightWS = Vector3.right,
                        upWS = Vector3.up,
                        width = 4.0f,
                        height = 2.0f,
                        lightType = 1u,
                        range = 8.0f,
                        cosBarnDoorAngle = Mathf.Cos(15.0f * Mathf.Deg2Rad),
                        barnDoorLength = 3.0f,
                    }
                },
                areaLightCount = 1,
            };

            lightData.UpdateAreaLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.areaLightBounds[0];
            var volume = lightData.areaLightVolumeData[0];
            var extents = new Vector3(6.0f, 5.0f, 4.0f);
            var diagonalRadius = 8.0f + 0.5f * Mathf.Sqrt(20.0f);
            var expectedRadius = Mathf.Sqrt(diagonalRadius * diagonalRadius + 16.0f);

            AssertVector3(bound.center, new Vector3(1.0f, -2.0f, 10.0f));
            AssertVector4(bound.boxAxisX, new Vector4(extents.x, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, extents.y, 0.0f, expectedRadius), 0.001f);
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, extents.z));
            AssertVector3(volume.boxInvRange, new Vector3(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z));
        }

        [Test]
        public void UpdateFiniteLightClusteredCullData_BuildsBoxBoundsAndVolume_ForDecal()
        {
            var position = new Vector3(1.0f, -2.0f, 6.0f);
            var size = new Vector3(4.0f, 2.0f, 6.0f);
            var lightData = new VividLightData
            {
                decalClusterData = new[]
                {
                    new VividLightData.DecalClusterData
                    {
                        worldToDecal = Matrix4x4.TRS(position, Quaternion.identity, size).inverse,
                    }
                },
                decalCount = 1,
            };

            lightData.UpdateFiniteLightClusteredCullData(Matrix4x4.identity);

            var bound = lightData.decalBounds[0];
            var volume = lightData.decalVolumeData[0];
            var halfExtents = size * 0.5f;
            var expectedRadius = halfExtents.magnitude;

            AssertVector3(bound.center, position);
            AssertVector4(bound.boxAxisX, new Vector4(halfExtents.x, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, halfExtents.y, 0.0f, expectedRadius), 0.001f);
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, halfExtents.z));
            Assert.That(volume.lightVolume, Is.EqualTo(2u));
            Assert.That(volume.lightCategory, Is.EqualTo(3u));
            Assert.That(volume.featureFlags, Is.EqualTo(524288u));
            AssertVector3(volume.lightPos, position);
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
            AssertVector3(volume.boxInvRange, new Vector3(1.0f / halfExtents.x, 1.0f / halfExtents.y, 1.0f / halfExtents.z));
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
                lightData.areaLightBounds = new[] { default(VividLightData.SFiniteLightBound) };
                lightData.areaLightVolumeData = new[] { default(VividLightData.LightVolumeData) };

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
                Assert.That(lightData.areaLightBounds, Is.Empty);
                Assert.That(lightData.areaLightVolumeData, Is.Empty);
            }
            finally
            {
                visibleLights.Dispose();
                visibleReflectionProbes.Dispose();
            }
        }

        [Test]
        public void CreateDirectionalLightData_PacksVolumetricParameters()
        {
            var trackedLightData = new VividLightRenderData
            {
                forwardWS = Vector3.forward,
                color = new Vector3(1.0f, 2.0f, 3.0f),
                shadowStrength = 0.75f,
                renderingLayerMask = 9u,
                volumetricDimmer = 2.5f,
                volumetricFadeDistance = 300.0f,
                volumetricShadowDimmer = 0.4f,
                flags = VividLightRenderDataFlags.AffectVolumetric,
            };

            Assert.That(s_CreateDirectionalLightDataMethod, Is.Not.Null);

            var directionalLight = (VividLightData.DirectionalLightData)s_CreateDirectionalLightDataMethod.Invoke(null, new object[] { trackedLightData });

            AssertDirectionalLight(
                directionalLight,
                -Vector3.forward,
                trackedLightData.color,
                trackedLightData.shadowStrength,
                trackedLightData.renderingLayerMask);
            Assert.That(directionalLight.volumetricDimmer, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(directionalLight.volumetricFadeDistance, Is.EqualTo(300.0f).Within(0.0001f));
            Assert.That(directionalLight.volumetricShadowDimmer, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(directionalLight.affectVolumetric, Is.EqualTo(1u));
        }

        [Test]
        public void CreatePunctualLightData_PacksVolumetricParameters()
        {
            var trackedLightData = new VividLightRenderData
            {
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 6.0f,
                color = new Vector3(4.0f, 5.0f, 6.0f),
                lightType = LightType.Point,
                forwardWS = Vector3.forward,
                rightWS = Vector3.right,
                upWS = Vector3.up,
                shapeRadius = 0.25f,
                inverseRangeSquared = 1.0f / 36.0f,
                renderingLayerMask = 7u,
                volumetricDimmer = 3.0f,
                volumetricFadeDistance = 500.0f,
                volumetricShadowDimmer = 0.25f,
                flags = VividLightRenderDataFlags.AffectVolumetric,
            };

            Assert.That(s_CreatePunctualLightDataMethod, Is.Not.Null);

            var punctualLight = (VividLightData.PunctualLightData)s_CreatePunctualLightDataMethod.Invoke(null, new object[] { trackedLightData });

            AssertPunctualLight(
                punctualLight,
                trackedLightData.positionWS,
                trackedLightData.color,
                trackedLightData.range,
                0u,
                trackedLightData.forwardWS);
            Assert.That(punctualLight.volumetricDimmer, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(punctualLight.volumetricFadeDistance, Is.EqualTo(500.0f).Within(0.0001f));
            Assert.That(punctualLight.volumetricShadowDimmer, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(punctualLight.affectVolumetric, Is.EqualTo(1u));
            AssertVector3(punctualLight.rightWS, Vector3.right);
            AssertVector3(punctualLight.upWS, Vector3.up);
            Assert.That(punctualLight.shapeRadiusSquared, Is.EqualTo(0.0625f).Within(0.0001f));
        }

        [Test]
        public void CreatePunctualLightData_PacksHdrpSpotConeAxes_ForVolumetricIntersection()
        {
            var trackedLightData = new VividLightRenderData
            {
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 10.0f,
                color = Vector3.one,
                lightType = LightType.Spot,
                forwardWS = Vector3.forward,
                rightWS = Vector3.right,
                upWS = Vector3.up,
                spotAngle = 60.0f,
                innerSpotAngle = 20.0f,
                shapeRadius = 0.5f,
                inverseRangeSquared = 0.01f,
                flags = VividLightRenderDataFlags.AffectVolumetric,
            };

            Assert.That(s_CreatePunctualLightDataMethod, Is.Not.Null);

            var punctualLight = (VividLightData.PunctualLightData)s_CreatePunctualLightDataMethod.Invoke(null, new object[] { trackedLightData });
            var outerHalfAngle = 30.0f * Mathf.Deg2Rad;
            var expectedConeAxisScale = Mathf.Cos(outerHalfAngle) / Mathf.Sin(outerHalfAngle);

            AssertVector3(punctualLight.directionWS, Vector3.forward);
            AssertVector3(punctualLight.rightWS, Vector3.right * expectedConeAxisScale);
            AssertVector3(punctualLight.upWS, Vector3.up * expectedConeAxisScale);
            Assert.That(punctualLight.shapeRadiusSquared, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(punctualLight.lightType, Is.EqualTo(1u));
        }

        [Test]
        public void CreateAreaLightData_PacksBarnDoorParameters_ForRectangleLight()
        {
            var trackedLightData = new VividLightRenderData
            {
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 6.0f,
                color = new Vector3(4.0f, 5.0f, 6.0f),
                lightType = LightType.Rectangle,
                forwardWS = Vector3.forward,
                rightWS = Vector3.right,
                upWS = Vector3.up,
                areaSize = new Vector2(3.0f, 2.0f),
                barnDoorAngle = 45.0f,
                barnDoorLength = 0.35f,
                renderingLayerMask = 11u,
                volumetricDimmer = 4.0f,
                volumetricFadeDistance = 600.0f,
                volumetricShadowDimmer = 0.5f,
                flags = VividLightRenderDataFlags.AffectVolumetric,
            };

            Assert.That(s_CreateAreaLightDataMethod, Is.Not.Null);

            var areaLight = (VividLightData.AreaLightData)s_CreateAreaLightDataMethod.Invoke(null, new object[] { trackedLightData });

            AssertAreaLight(
                areaLight,
                trackedLightData.positionWS,
                trackedLightData.color,
                trackedLightData.range,
                trackedLightData.forwardWS,
                trackedLightData.rightWS,
                trackedLightData.upWS,
                trackedLightData.areaSize.x,
                trackedLightData.areaSize.y,
                1u,
                trackedLightData.renderingLayerMask,
                Mathf.Cos(45.0f * Mathf.Deg2Rad),
                0.35f);
            Assert.That(areaLight.volumetricDimmer, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(areaLight.volumetricFadeDistance, Is.EqualTo(600.0f).Within(0.0001f));
            Assert.That(areaLight.volumetricShadowDimmer, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(areaLight.affectVolumetric, Is.EqualTo(1u));
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
            uint expectedRenderingLayerMask,
            float expectedCosBarnDoorAngle = 0.0f,
            float expectedBarnDoorLength = 0.0f)
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
            Assert.That(actual.cosBarnDoorAngle, Is.EqualTo(expectedCosBarnDoorAngle).Within(0.0001f));
            Assert.That(actual.barnDoorLength, Is.EqualTo(expectedBarnDoorLength).Within(0.0001f));
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
