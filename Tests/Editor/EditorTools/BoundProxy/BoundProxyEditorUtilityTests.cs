using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BoundProxyEditorUtilityTests
    {
        [Test]
        public void SerializedBoundProxyShape_BindsExpectedFields_WhenPropertyExists()
        {
            var hostObject = new GameObject("Serialized Bound Proxy Host");
            var host = hostObject.AddComponent<BoundProxyShapeHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var serializedShape = new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));

                Assert.That(serializedShape.root, Is.Not.Null);
                Assert.That(serializedShape.shape, Is.Not.Null);
                Assert.That(serializedShape.center, Is.Not.Null);
                Assert.That(serializedShape.size, Is.Not.Null);
                Assert.That(serializedShape.radius, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void GetPrimaryShapeProperty_ReturnsSizeOrRadius_WhenShapeChanges()
        {
            var hostObject = new GameObject("Primary Shape Property Host");
            var host = hostObject.AddComponent<BoundProxyShapeHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var serializedShape = new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Box;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();
                Assert.That(BoundProxyEditorUtility.GetPrimaryShapeProperty(serializedShape).propertyPath, Is.EqualTo("m_BoundProxy.size"));

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();
                Assert.That(BoundProxyEditorUtility.GetPrimaryShapeProperty(serializedShape).propertyPath, Is.EqualTo("m_BoundProxy.radius"));
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void GetShapeValue_SanitizesNegativeDimensions_WhenReadingSerializedShape()
        {
            var hostObject = new GameObject("Sanitized Shape Host");
            var host = hostObject.AddComponent<BoundProxyShapeHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var serializedShape = new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));
                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.center.vector3Value = new Vector3(1.0f, 2.0f, 3.0f);
                serializedShape.size.vector3Value = new Vector3(-1.0f, -2.0f, 4.0f);
                serializedShape.radius.floatValue = -5.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();

                BoundProxyShape shape = BoundProxyEditorUtility.GetShapeValue(serializedShape);

                Assert.That(shape.shape, Is.EqualTo(BoundProxyShapeType.Sphere));
                Assert.That(shape.center, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
                AssertVector3(shape.size, new Vector3(0.0f, 0.0f, 4.0f));
                Assert.That(shape.radius, Is.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void GetLocalAndWorldBounds_MatchRuntimeSemantics_WhenOwnerRotates()
        {
            var hostObject = new GameObject("Bounds Host");
            var host = hostObject.AddComponent<BoundProxyShapeHost>();
            hostObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            hostObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            hostObject.transform.localScale = new Vector3(6.0f, 7.0f, 8.0f);

            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 0.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f),
            };

            try
            {
                host.BoundProxy = shape;

                Bounds localBounds = BoundProxyEditorUtility.GetLocalBounds(shape);
                Bounds worldBounds = BoundProxyEditorUtility.GetWorldBounds(shape, host.transform);
                Bounds runtimeWorldBounds = host.transform.CalculateWorldAabb(shape);

                Assert.That(localBounds, Is.EqualTo(shape.GetLocalBounds()));
                AssertVector3(worldBounds.center, runtimeWorldBounds.center);
                AssertVector3(worldBounds.size, runtimeWorldBounds.size);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
