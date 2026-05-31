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
        private static readonly MethodInfo s_CreateAreaLightDataMethod =
            typeof(VividLightData).GetMethod("CreateAreaLightData", BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly MethodInfo s_CreateReGIRLightDataMethod =
            typeof(VividLightData).GetMethod("CreateReGIRLightData", BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly MethodInfo s_UpdateVisibleLightDataMethod =
            typeof(VividLightData).GetMethod("UpdateVisibleLightData", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void DecalClusterData_UsesUnsignedBindlessTextureIndicesAndScalarMaterialInputs()
        {
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.baseColorTextureIndex))?.FieldType,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.normalTextureIndex))?.FieldType,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.metallicTextureIndex))?.FieldType,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.roughnessTextureIndex))?.FieldType,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.metallic))?.FieldType,
                Is.EqualTo(typeof(float)));
            Assert.That(
                typeof(VividLightData.DecalClusterData).GetField(nameof(VividLightData.DecalClusterData.roughness))?.FieldType,
                Is.EqualTo(typeof(float)));
            Assert.That(VividLightData.DecalClusterData.Stride % 16, Is.EqualTo(0));
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
        public void UpdateReflectionProbeClusteredCullData_BuildsHdrpEnvBoxBoundsAndVolume_ForReflectionProbe()
        {
            var lightData = new VividLightData
            {
                reflectionProbes = new[]
                {
                    new VividLightData.ReflectionProbeData
                    {
                        positionWS = new Vector3(1.0f, -2.0f, 6.0f),
                        extents = new Vector3(3.0f, 2.0f, 4.0f),
                        rightWS = Vector3.right,
                        upWS = Vector3.up,
                        forwardWS = Vector3.forward,
                    }
                },
                reflectionProbeCount = 1,
            };

            lightData.UpdateReflectionProbeClusteredCullData(Matrix4x4.identity);

            var bound = lightData.reflectionProbeBounds[0];
            var volume = lightData.reflectionProbeVolumeData[0];
            var extents = new Vector3(3.0f, 2.0f, 4.0f);
            var radius = extents.magnitude;

            AssertVector3(bound.center, new Vector3(1.0f, -2.0f, 6.0f));
            AssertVector4(bound.boxAxisX, new Vector4(extents.x, 0.0f, 0.0f, 1.0f));
            AssertVector4(bound.boxAxisY, new Vector4(0.0f, extents.y, 0.0f, radius), 0.001f);
            AssertVector3(bound.boxAxisZ, new Vector3(0.0f, 0.0f, extents.z));
            Assert.That(volume.lightVolume, Is.EqualTo(2u));
            Assert.That(volume.lightCategory, Is.EqualTo(2u));
            Assert.That(volume.featureFlags, Is.EqualTo(32768u));
            Assert.That(volume.radiusSq, Is.EqualTo(radius * radius).Within(0.001f));
            AssertVector3(volume.lightPos, new Vector3(1.0f, -2.0f, 6.0f));
            AssertVector3(volume.lightAxisX, Vector3.right);
            AssertVector3(volume.lightAxisY, Vector3.up);
            AssertVector3(volume.lightAxisZ, Vector3.forward);
            AssertVector3(volume.boxInnerDist, new Vector3(2.99f, 1.99f, 3.99f), 0.001f);
            AssertVector3(volume.boxInvRange, new Vector3(100.0f, 100.0f, 100.0f));
            Assert.That(volume.affectVolumetric, Is.Zero);
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
                lightData.reflectionProbeCount = 1;
                lightData.reGIRLightCount = 1;
                lightData.mainDirectionalLightIndex = 1;
                lightData.mainDirectionalLightEntityId = EntityId.FromULong(84);
                lightData.areaLights = new[] { default(VividLightData.AreaLightData) };
                lightData.reflectionProbes = new[] { default(VividLightData.ReflectionProbeData) };
                lightData.reGIRLights = new[] { default(VividReGIRLightData) };
                lightData.punctualLightBounds = new[] { default(VividLightData.SFiniteLightBound) };
                lightData.punctualLightVolumeData = new[] { default(VividLightData.LightVolumeData) };
                lightData.areaLightBounds = new[] { default(VividLightData.SFiniteLightBound) };
                lightData.areaLightVolumeData = new[] { default(VividLightData.LightVolumeData) };
                lightData.reflectionProbeBounds = new[] { default(VividLightData.SFiniteLightBound) };
                lightData.reflectionProbeVolumeData = new[] { default(VividLightData.LightVolumeData) };

                lightData.Reset();

                Assert.That(lightData.visibleLights.IsCreated, Is.False);
                Assert.That(lightData.visibleReflectionProbes.IsCreated, Is.False);
                Assert.That(lightData.mainLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.directionalLightCount, Is.Zero);
                Assert.That(lightData.punctualLightCount, Is.Zero);
                Assert.That(lightData.areaLightCount, Is.Zero);
                Assert.That(lightData.reflectionProbeCount, Is.Zero);
                Assert.That(lightData.reGIRLightCount, Is.Zero);
                Assert.That(lightData.mainDirectionalLightIndex, Is.EqualTo(-1));
                Assert.That(lightData.mainDirectionalLightEntityId, Is.EqualTo(EntityId.None));
                Assert.That(lightData.areaLights, Is.Empty);
                Assert.That(lightData.reflectionProbes, Is.Empty);
                Assert.That(lightData.reGIRLights, Is.Empty);
                Assert.That(lightData.punctualLightBounds, Is.Empty);
                Assert.That(lightData.punctualLightVolumeData, Is.Empty);
                Assert.That(lightData.areaLightBounds, Is.Empty);
                Assert.That(lightData.areaLightVolumeData, Is.Empty);
                Assert.That(lightData.reflectionProbeBounds, Is.Empty);
                Assert.That(lightData.reflectionProbeVolumeData, Is.Empty);
            }
            finally
            {
                visibleLights.Dispose();
                visibleReflectionProbes.Dispose();
            }
        }

        [Test]
        public void UpdateVisibleLightData_BuildsReGIRLights_WhenCompletedByReGIRPrepare()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(3, Allocator.TempJob);
            var pointLightObject = new GameObject("ReGIR Point Light");
            var spotLightObject = new GameObject("ReGIR Spot Light");
            var areaLightObject = new GameObject("ReGIR Rectangle Light");

            try
            {
                VividLightRenderDatabase.instance.Clear();

                var pointLight = CreateRegisteredLight(
                    pointLightObject,
                    LightType.Point,
                    new Vector3(1.0f, 2.0f, 3.0f),
                    range: 6.0f,
                    color: new Color(2.0f, 1.0f, 0.5f));
                var spotLight = CreateRegisteredLight(
                    spotLightObject,
                    LightType.Spot,
                    new Vector3(4.0f, 5.0f, 6.0f),
                    range: 8.0f,
                    color: new Color(0.5f, 3.0f, 1.0f),
                    spotAngle: 45.0f,
                    innerSpotAngle: 20.0f);
                var areaLight = CreateRegisteredLight(
                    areaLightObject,
                    LightType.Rectangle,
                    new Vector3(7.0f, 8.0f, 9.0f),
                    range: 9.0f,
                    color: Color.white,
                    intensity: 2.0f,
                    areaSize: new Vector2(4.0f, 2.0f));

                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    new Color(2.0f, 1.0f, 0.5f),
                    Matrix4x4.TRS(pointLightObject.transform.position, Quaternion.identity, Vector3.one),
                    range: 6.0f,
                    light: pointLight);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Spot,
                    new Color(0.5f, 3.0f, 1.0f),
                    Matrix4x4.TRS(spotLightObject.transform.position, Quaternion.identity, Vector3.one),
                    range: 8.0f,
                    spotAngle: 45.0f,
                    innerSpotAngle: 20.0f,
                    light: spotLight);
                visibleLights[2] = CreateVisibleLight(
                    LightType.Rectangle,
                    Color.white,
                    Matrix4x4.TRS(areaLightObject.transform.position, areaLightObject.transform.rotation, Vector3.one),
                    light: areaLight);

                InvokeUpdateVisibleLightData(lightData, visibleLights);
                lightData.CompleteReGIRPrepare();
                lightData.CompleteLightGridPrepare();

                Assert.That(lightData.reGIRLightCount, Is.EqualTo(3));
                Assert.That(lightData.punctualLightCount, Is.EqualTo(2));
                Assert.That(lightData.areaLightCount, Is.EqualTo(1));
                Assert.That(lightData.reGIRLights[0].lightType, Is.EqualTo(VividReGIRLightData.TypePoint));
                Assert.That(lightData.reGIRLights[1].lightType, Is.EqualTo(VividReGIRLightData.TypeSpot));
                Assert.That(lightData.reGIRLights[2].lightType, Is.EqualTo(VividReGIRLightData.TypeRectangle));
                Assert.That(lightData.reGIRLights[1].angleScale, Is.GreaterThan(0.0f));
                Assert.That(lightData.reGIRLights[1].angleOffset, Is.LessThan(0.0f));
                Assert.That(lightData.reGIRLights[2].areaSize.x, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(lightData.reGIRLights[2].areaSize.y, Is.EqualTo(2.0f).Within(0.0001f));
            }
            finally
            {
                lightData.ReleaseLightGridNativeResources();
                VividLightRenderDatabase.instance.Clear();
                visibleLights.Dispose();
                UnityEngine.Object.DestroyImmediate(pointLightObject);
                UnityEngine.Object.DestroyImmediate(spotLightObject);
                UnityEngine.Object.DestroyImmediate(areaLightObject);
            }
        }

        [Test]
        public void UpdateVisibleLightData_KeepsReGIRLights_WhenLightGridCompletesFirst()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(1, Allocator.TempJob);
            var lightObject = new GameObject("ReGIR Completion Point Light");

            try
            {
                VividLightRenderDatabase.instance.Clear();

                var light = CreateRegisteredLight(
                    lightObject,
                    LightType.Point,
                    new Vector3(1.0f, 0.0f, 0.0f),
                    range: 4.0f,
                    color: Color.white);

                visibleLights[0] = CreateVisibleLight(
                    LightType.Point,
                    Color.white,
                    Matrix4x4.TRS(lightObject.transform.position, Quaternion.identity, Vector3.one),
                    range: 4.0f,
                    light: light);

                InvokeUpdateVisibleLightData(lightData, visibleLights);
                lightData.CompleteLightGridPrepare();
                lightData.CompleteReGIRPrepare();

                Assert.That(lightData.reGIRLightCount, Is.EqualTo(1));
                Assert.That(lightData.punctualLightCount, Is.EqualTo(1));
                Assert.That(lightData.reGIRLights[0].lightType, Is.EqualTo(VividReGIRLightData.TypePoint));
            }
            finally
            {
                lightData.ReleaseLightGridNativeResources();
                VividLightRenderDatabase.instance.Clear();
                visibleLights.Dispose();
                UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void UpdateVisibleLightData_SkipsDirectionalAndZeroRangeLights_ForReGIR()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(2, Allocator.TempJob);
            var zeroRangeLightObject = new GameObject("ReGIR Zero Range Point Light");

            try
            {
                VividLightRenderDatabase.instance.Clear();

                var zeroRangeLight = CreateRegisteredLight(
                    zeroRangeLightObject,
                    LightType.Point,
                    Vector3.zero,
                    range: 0.0f,
                    color: Color.white);

                visibleLights[0] = CreateVisibleLight(
                    LightType.Directional,
                    Color.white,
                    Matrix4x4.identity);
                visibleLights[1] = CreateVisibleLight(
                    LightType.Point,
                    Color.white,
                    Matrix4x4.identity,
                    range: 0.0f,
                    light: zeroRangeLight);

                InvokeUpdateVisibleLightData(lightData, visibleLights);
                lightData.CompleteReGIRPrepare();

                Assert.That(lightData.reGIRLightCount, Is.Zero);
                Assert.That(lightData.punctualLightCount, Is.Zero);
                Assert.That(lightData.directionalLightCount, Is.EqualTo(1));
            }
            finally
            {
                lightData.ReleaseLightGridNativeResources();
                VividLightRenderDatabase.instance.Clear();
                visibleLights.Dispose();
                UnityEngine.Object.DestroyImmediate(zeroRangeLightObject);
            }
        }

        [Test]
        public void UpdateVisibleLightData_BuildsReGIRLights_FromRegisteredSceneLightsWithoutVisibleLights()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(0, Allocator.TempJob);
            var lightObject = new GameObject("Registered ReGIR Point Light");

            try
            {
                VividLightRenderDatabase.instance.Clear();

                CreateRegisteredLight(
                    lightObject,
                    LightType.Point,
                    new Vector3(2.0f, 0.0f, 0.0f),
                    range: 4.0f,
                    color: Color.white);

                InvokeUpdateVisibleLightData(lightData, visibleLights);
                lightData.CompleteReGIRPrepare();

                Assert.That(lightData.reGIRLightCount, Is.EqualTo(1));
                Assert.That(lightData.punctualLightCount, Is.Zero);
                Assert.That(lightData.areaLightCount, Is.Zero);
                Assert.That(lightData.reGIRLights[0].lightType, Is.EqualTo(VividReGIRLightData.TypePoint));
            }
            finally
            {
                lightData.ReleaseLightGridNativeResources();
                VividLightRenderDatabase.instance.Clear();
                visibleLights.Dispose();
                UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void UpdateVisibleLightData_CullsRegisteredReGIRLightsOutsideCameraBox()
        {
            var lightData = new VividLightData();
            var visibleLights = new NativeArray<VisibleLight>(0, Allocator.TempJob);
            var insideLightObject = new GameObject("Inside ReGIR Point Light");
            var outsideLightObject = new GameObject("Outside ReGIR Point Light");

            try
            {
                VividLightRenderDatabase.instance.Clear();

                var cameraPosition = new Vector3(100.0f, 0.0f, 0.0f);
                var worldToViewMatrix = Matrix4x4.Translate(-cameraPosition);

                CreateRegisteredLight(
                    insideLightObject,
                    LightType.Point,
                    new Vector3(105.0f, 0.0f, 0.0f),
                    range: 2.0f,
                    color: Color.white);
                CreateRegisteredLight(
                    outsideLightObject,
                    LightType.Point,
                    Vector3.zero,
                    range: 2.0f,
                    color: Color.white);

                InvokeUpdateVisibleLightData(lightData, visibleLights, worldToViewMatrix);
                lightData.CompleteReGIRPrepare();

                Assert.That(lightData.reGIRLightCount, Is.EqualTo(1));
                AssertVector3(lightData.reGIRLights[0].positionWS, insideLightObject.transform.position);
            }
            finally
            {
                lightData.ReleaseLightGridNativeResources();
                VividLightRenderDatabase.instance.Clear();
                visibleLights.Dispose();
                UnityEngine.Object.DestroyImmediate(insideLightObject);
                UnityEngine.Object.DestroyImmediate(outsideLightObject);
            }
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
        }

        [Test]
        public void CreateReGIRLightData_PacksAreaLightShape_ForRectangleLight()
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
                renderingLayerMask = 11u,
            };

            Assert.That(s_CreateReGIRLightDataMethod, Is.Not.Null);

            var reGIRLight = (VividReGIRLightData)s_CreateReGIRLightDataMethod.Invoke(null, new object[] { trackedLightData });

            AssertVector3(reGIRLight.positionWS, trackedLightData.positionWS);
            AssertVector3(reGIRLight.color, trackedLightData.color);
            Assert.That(reGIRLight.lightType, Is.EqualTo(VividReGIRLightData.TypeRectangle));
            Assert.That(reGIRLight.range, Is.EqualTo(trackedLightData.range).Within(0.0001f));
            Assert.That(reGIRLight.areaSize.x, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(reGIRLight.areaSize.y, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(reGIRLight.power, Is.EqualTo(36.0f).Within(0.0001f));
            Assert.That(reGIRLight.renderingLayerMask, Is.EqualTo(11u));
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

        private static Light CreateRegisteredLight(
            GameObject lightObject,
            LightType lightType,
            Vector3 position,
            float range,
            Color color,
            float intensity = 1.0f,
            float spotAngle = 30.0f,
            float innerSpotAngle = 30.0f,
            Vector2 areaSize = default)
        {
            lightObject.transform.position = position;

            var light = lightObject.AddComponent<Light>();
            light.type = lightType;
            light.range = range;
            light.color = color;
            light.intensity = intensity;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;

            if (lightType == LightType.Rectangle || lightType == LightType.Tube)
                light.areaSize = areaSize;

            VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());
            return light;
        }

        private static void InvokeUpdateVisibleLightData(
            VividLightData lightData,
            NativeArray<VisibleLight> visibleLights)
        {
            InvokeUpdateVisibleLightData(lightData, visibleLights, Matrix4x4.identity);
        }

        private static void InvokeUpdateVisibleLightData(
            VividLightData lightData,
            NativeArray<VisibleLight> visibleLights,
            Matrix4x4 worldToViewMatrix)
        {
            Assert.That(s_UpdateVisibleLightDataMethod, Is.Not.Null);
            s_UpdateVisibleLightDataMethod.Invoke(
                lightData,
                new object[] { visibleLights, null, worldToViewMatrix });
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
