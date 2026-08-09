using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BoundProxyShapeTests
    {
        [Test]
        public void DefaultValue_UsesZeroSizedBoxAtOrigin()
        {
            BoundProxyShape shape = default;
            Bounds localBounds = shape.GetLocalBounds();

            Assert.That(shape.shape, Is.EqualTo(BoundProxyShapeType.Box));
            Assert.That(shape.center, Is.EqualTo(Vector3.zero));
            Assert.That(shape.size, Is.EqualTo(Vector3.zero));
            Assert.That(shape.radius, Is.EqualTo(0.0f));
            Assert.That(localBounds.center, Is.EqualTo(Vector3.zero));
            Assert.That(localBounds.size, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GetLocalBounds_ReturnsSphereBounds_WhenShapeIsSphere()
        {
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Sphere,
                center = new Vector3(1.0f, 2.0f, 3.0f),
                radius = 2.5f,
            };

            Bounds localBounds = shape.GetLocalBounds();

            Assert.That(localBounds.center, Is.EqualTo(shape.center));
            Assert.That(localBounds.size, Is.EqualTo(Vector3.one * 5.0f));
        }

        [Test]
        public void JsonUtility_RoundTripsSerializedFields_WhenShapeValuesAreSet()
        {
            var container = new BoundProxyShapeSerializationContainer
            {
                shape = new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Sphere,
                    center = new Vector3(1.0f, -2.0f, 3.0f),
                    size = new Vector3(4.0f, 5.0f, 6.0f),
                    radius = 7.0f,
                }
            };

            string json = JsonUtility.ToJson(container);
            BoundProxyShapeSerializationContainer restored =
                JsonUtility.FromJson<BoundProxyShapeSerializationContainer>(json);

            Assert.That(json, Does.Contain("\"shape\""));
            Assert.That(json, Does.Contain("\"center\""));
            Assert.That(json, Does.Contain("\"size\""));
            Assert.That(json, Does.Contain("\"radius\""));
            Assert.That(restored.shape.shape, Is.EqualTo(BoundProxyShapeType.Sphere));
            Assert.That(restored.shape.center, Is.EqualTo(container.shape.center));
            Assert.That(restored.shape.size, Is.EqualTo(container.shape.size));
            Assert.That(restored.shape.radius, Is.EqualTo(container.shape.radius));
        }

        [Test]
        public void CalculateWorldAabb_IgnoresTransformScale_WhenShapeUsesExplicitSize()
        {
            var owner = new GameObject("BoundProxy Scale Test");
            owner.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            owner.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);

            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 0.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f),
            };

            try
            {
                owner.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
                Bounds unitScaleBounds = owner.transform.CalculateWorldAabb(shape);

                owner.transform.localScale = new Vector3(7.0f, 8.0f, 9.0f);
                Bounds scaledBounds = owner.transform.CalculateWorldAabb(shape);

                AssertVector3(unitScaleBounds.center, new Vector3(3.0f, 4.0f, 4.0f));
                AssertVector3(scaledBounds.center, unitScaleBounds.center);
                AssertVector3(scaledBounds.size, unitScaleBounds.size);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ContainsAndIntersectsAabb_ReturnExpectedResults_ForBoxAndSphere()
        {
            BoundProxyWorldData box = ((Transform)null).CreateWorldData(
                BoundProxyFeature.Decal,
                new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Box,
                    size = new Vector3(4.0f, 4.0f, 4.0f),
                });
            BoundProxyWorldData sphere = ((Transform)null).CreateWorldData(
                BoundProxyFeature.LocalVolumetricFog,
                new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Sphere,
                    center = new Vector3(10.0f, 0.0f, 0.0f),
                    radius = 2.0f,
                });

            Assert.That(box.Contains(new Vector3(1.5f, 0.0f, 0.0f)), Is.True);
            Assert.That(box.Contains(new Vector3(3.0f, 0.0f, 0.0f)), Is.False);
            Assert.That(box.IntersectsAabb(new Bounds(new Vector3(1.0f, 0.0f, 0.0f), Vector3.one)), Is.True);
            Assert.That(box.IntersectsAabb(new Bounds(new Vector3(5.0f, 0.0f, 0.0f), Vector3.one)), Is.False);

            Assert.That(sphere.Contains(new Vector3(11.0f, 0.0f, 0.0f)), Is.True);
            Assert.That(sphere.Contains(new Vector3(13.5f, 0.0f, 0.0f)), Is.False);
            Assert.That(sphere.IntersectsAabb(new Bounds(new Vector3(11.0f, 0.0f, 0.0f), Vector3.one)), Is.True);
            Assert.That(sphere.IntersectsAabb(new Bounds(new Vector3(14.5f, 0.0f, 0.0f), Vector3.one)), Is.False);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
