using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BoundProxyClusterProjectionUtilityTests
    {
        [Test]
        public void CreateParameters_ComputesPerspectiveLayout_WhenCameraIsPerspective()
        {
            var cameraObject = new GameObject("BoundProxy Perspective Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;

            try
            {
                BoundProxyClusterProjectionParameters parameters =
                    BoundProxyClusterProjectionUtility.CreateParameters(camera, 320, 180, 32, 24, 64);

                Assert.That(parameters.screenWidth, Is.EqualTo(320));
                Assert.That(parameters.screenHeight, Is.EqualTo(180));
                Assert.That(parameters.tileSize, Is.EqualTo(32));
                Assert.That(parameters.tileCountX, Is.EqualTo(10));
                Assert.That(parameters.tileCountY, Is.EqualTo(6));
                Assert.That(parameters.bigTileSize, Is.EqualTo(64));
                Assert.That(parameters.bigTileCountX, Is.EqualTo(5));
                Assert.That(parameters.bigTileCountY, Is.EqualTo(3));
                Assert.That(parameters.sliceCount, Is.EqualTo(24));
                Assert.That(parameters.isOrthographic, Is.Zero);
                Assert.That(parameters.tanHalfFovY, Is.EqualTo(Mathf.Tan(Mathf.Deg2Rad * 30.0f)).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateScreenBounds_ComputesExpectedSphereBounds_WhenPerspectiveProxyTouchesScreenEdge()
        {
            var cameraObject = new GameObject("BoundProxy Perspective Edge Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;

            try
            {
                BoundProxyClusterProjectionParameters parameters =
                    BoundProxyClusterProjectionUtility.CreateParameters(camera, 320, 180, 32, 24, 64);
                BoundProxyWorldData worldData = BoundProxyUtility.CreateWorldData(
                    null,
                    BoundProxyFeature.Decal,
                    new BoundProxyShape
                    {
                        shape = BoundProxyShapeType.Sphere,
                        center = new Vector3(5.0f, 0.0f, 6.0f),
                        radius = 3.0f,
                    });

                ClusteredProxyScreenBounds bounds =
                    BoundProxyClusterProjectionUtility.CreateScreenBounds(worldData, parameters);

                Assert.That(bounds.IsValid, Is.True);
                Assert.That(bounds.clipSpaceAabbMin.x, Is.EqualTo(0.2164f).Within(0.001f));
                Assert.That(bounds.tileMinX, Is.EqualTo(5));
                Assert.That(bounds.tileMaxX, Is.EqualTo(9));
                Assert.That(bounds.sliceMin, Is.EqualTo(5));
                Assert.That(bounds.sliceMax, Is.EqualTo(11));
                Assert.That(bounds.bigTileMinX, Is.EqualTo(2));
                Assert.That(bounds.bigTileMaxX, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateScreenBounds_ComputesExpectedBoxBounds_WhenCameraIsOrthographic()
        {
            var cameraObject = new GameObject("BoundProxy Ortho Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100.0f;

            try
            {
                BoundProxyClusterProjectionParameters parameters =
                    BoundProxyClusterProjectionUtility.CreateParameters(camera, 200, 100, 20, 10, 40);
                BoundProxyWorldData worldData = BoundProxyUtility.CreateWorldData(
                    null,
                    BoundProxyFeature.LocalVolumetricFog,
                    new BoundProxyShape
                    {
                        shape = BoundProxyShapeType.Box,
                        center = new Vector3(2.0f, 1.0f, 10.0f),
                        size = new Vector3(4.0f, 2.0f, 4.0f),
                    });

                ClusteredProxyScreenBounds bounds =
                    BoundProxyClusterProjectionUtility.CreateScreenBounds(worldData, parameters);

                Assert.That(bounds.IsValid, Is.True);
                AssertVector2(bounds.clipSpaceAabbMin, new Vector2(0.0f, 0.0f));
                AssertVector2(bounds.clipSpaceAabbMax, new Vector2(0.4f, 0.4f));
                Assert.That(bounds.tileMinX, Is.EqualTo(4));
                Assert.That(bounds.tileMaxX, Is.EqualTo(8));
                Assert.That(bounds.tileMinY, Is.EqualTo(0));
                Assert.That(bounds.tileMaxY, Is.EqualTo(3));
                Assert.That(bounds.sliceMin, Is.EqualTo(0));
                Assert.That(bounds.sliceMax, Is.EqualTo(2));
                Assert.That(bounds.bigTileMinX, Is.EqualTo(2));
                Assert.That(bounds.bigTileMaxX, Is.EqualTo(4));
                Assert.That(bounds.bigTileMinY, Is.EqualTo(0));
                Assert.That(bounds.bigTileMaxY, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateScreenBounds_ReturnsInvalid_WhenProxyIsOffScreen()
        {
            var cameraObject = new GameObject("BoundProxy Offscreen Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;

            try
            {
                BoundProxyClusterProjectionParameters parameters =
                    BoundProxyClusterProjectionUtility.CreateParameters(camera, 320, 180, 32, 24, 64);
                BoundProxyWorldData worldData = BoundProxyUtility.CreateWorldData(
                    null,
                    BoundProxyFeature.DDGIVolume,
                    new BoundProxyShape
                    {
                        shape = BoundProxyShapeType.Sphere,
                        center = new Vector3(100.0f, 0.0f, 10.0f),
                        radius = 1.0f,
                    });

                ClusteredProxyScreenBounds bounds =
                    BoundProxyClusterProjectionUtility.CreateScreenBounds(worldData, parameters);

                Assert.That(bounds.IsValid, Is.False);
                Assert.That(bounds.isValid, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateScreenBounds_IgnoresTransformScale_WhenBoxShapeUsesExplicitSize()
        {
            var cameraObject = new GameObject("BoundProxy Scale Projection Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.0f;

            var firstOwner = new GameObject("BoundProxy Owner A");
            var secondOwner = new GameObject("BoundProxy Owner B");
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(0.5f, 0.0f, 10.0f),
                size = new Vector3(4.0f, 2.0f, 2.0f),
            };

            try
            {
                firstOwner.transform.position = new Vector3(1.0f, 0.0f, 0.0f);
                firstOwner.transform.rotation = Quaternion.Euler(0.0f, 35.0f, 0.0f);
                firstOwner.transform.localScale = Vector3.one;

                secondOwner.transform.position = firstOwner.transform.position;
                secondOwner.transform.rotation = firstOwner.transform.rotation;
                secondOwner.transform.localScale = new Vector3(9.0f, 7.0f, 5.0f);

                BoundProxyClusterProjectionParameters parameters =
                    BoundProxyClusterProjectionUtility.CreateParameters(camera, 200, 100, 20, 10, 40);
                BoundProxyWorldData firstWorldData =
                    BoundProxyUtility.CreateWorldData(firstOwner.transform, BoundProxyFeature.Decal, shape);
                BoundProxyWorldData secondWorldData =
                    BoundProxyUtility.CreateWorldData(secondOwner.transform, BoundProxyFeature.Decal, shape);
                ClusteredProxyScreenBounds firstBounds =
                    BoundProxyClusterProjectionUtility.CreateScreenBounds(firstWorldData, parameters);
                ClusteredProxyScreenBounds secondBounds =
                    BoundProxyClusterProjectionUtility.CreateScreenBounds(secondWorldData, parameters);

                AssertEqualBounds(firstBounds, secondBounds);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(firstOwner);
                Object.DestroyImmediate(secondOwner);
            }
        }

        private static void AssertEqualBounds(ClusteredProxyScreenBounds expected, ClusteredProxyScreenBounds actual)
        {
            Assert.That(actual.isValid, Is.EqualTo(expected.isValid));
            AssertVector3(actual.viewSpaceAabbMin, expected.viewSpaceAabbMin);
            AssertVector3(actual.viewSpaceAabbMax, expected.viewSpaceAabbMax);
            AssertVector2(actual.clipSpaceAabbMin, expected.clipSpaceAabbMin);
            AssertVector2(actual.clipSpaceAabbMax, expected.clipSpaceAabbMax);
            Assert.That(actual.sliceMin, Is.EqualTo(expected.sliceMin));
            Assert.That(actual.sliceMax, Is.EqualTo(expected.sliceMax));
            Assert.That(actual.tileMinX, Is.EqualTo(expected.tileMinX));
            Assert.That(actual.tileMaxX, Is.EqualTo(expected.tileMaxX));
            Assert.That(actual.tileMinY, Is.EqualTo(expected.tileMinY));
            Assert.That(actual.tileMaxY, Is.EqualTo(expected.tileMaxY));
            Assert.That(actual.bigTileMinX, Is.EqualTo(expected.bigTileMinX));
            Assert.That(actual.bigTileMaxX, Is.EqualTo(expected.bigTileMaxX));
            Assert.That(actual.bigTileMinY, Is.EqualTo(expected.bigTileMinY));
            Assert.That(actual.bigTileMaxY, Is.EqualTo(expected.bigTileMaxY));
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
