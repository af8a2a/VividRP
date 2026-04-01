using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class DDGIVolumeTests
    {
        [Test]
        public void DDGIVolume_ImplementsBoundProxyProvider_WithEmbeddedBoundProxyShape()
        {
            var volumeObject = new GameObject("DDGI Volume Test");
            volumeObject.transform.position = new Vector3(2.0f, 1.0f, 3.0f);
            volumeObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));
                serializedShape.shape.intValue = (int)BoundProxyShapeType.Box;
                serializedShape.center.vector3Value = new Vector3(1.0f, 0.0f, 0.0f);
                serializedShape.size.vector3Value = new Vector3(4.0f, 6.0f, 8.0f);
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                IBoundProxyProvider provider = volume;

                Assert.That(provider.BoundProxyFeature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                Assert.That(provider.IsBoundProxyActive, Is.True);
                Assert.That(provider.BoundProxyTransform, Is.EqualTo(volume.transform));
                Assert.That(provider.BoundProxyShape.shape, Is.EqualTo(BoundProxyShapeType.Box));
                Assert.That(provider.BoundProxyShape.size, Is.EqualTo(new Vector3(4.0f, 6.0f, 8.0f)));
                Assert.That(BoundProxyUtility.TryCreateWorldData(provider, out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                Assert.That(worldData.entityId, Is.EqualTo(volume.transform.GetEntityId()));
                AssertVector3(worldData.worldCenter, new Vector3(2.0f, 1.0f, 2.0f));
                AssertVector3(worldData.worldAabb.size, new Vector3(8.0f, 6.0f, 4.0f));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void DDGIVolume_SerializedBoundProxy_CanBeDrivenBySharedEditorUtility()
        {
            var volumeObject = new GameObject("DDGI Volume Editor Test");
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.center.vector3Value = new Vector3(1.0f, 2.0f, 3.0f);
                serializedShape.radius.floatValue = 4.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                serializedObject.Update();

                Assert.That(
                    BoundProxyEditorUtility.GetPrimaryShapeProperty(serializedShape).propertyPath,
                    Is.EqualTo("m_BoundProxy.radius"));

                BoundProxyShape shape = BoundProxyEditorUtility.GetShapeValue(serializedShape);

                Assert.That(shape.shape, Is.EqualTo(BoundProxyShapeType.Sphere));
                Assert.That(shape.center, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
                Assert.That(shape.radius, Is.EqualTo(4.0f));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void DDGIVolume_CanBeCollectedByBoundProxySceneTracker()
        {
            var volumeObject = new GameObject("DDGI Volume Tracker Test");
            volumeObject.transform.position = new Vector3(0.0f, 0.0f, 8.0f);
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));
                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.radius.floatValue = 3.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                using var tracker = new BoundProxySceneTracker<DDGIVolume>();
                var results = new System.Collections.Generic.List<BoundProxyWorldData>();

                tracker.GetWorldData(results);

                Assert.That(tracker.TrackedProviderCount, Is.GreaterThanOrEqualTo(1));

                bool foundTrackedVolume = false;
                for (int index = 0; index < results.Count; index++)
                {
                    if (results[index].entityId != volume.transform.GetEntityId())
                    {
                        continue;
                    }

                    foundTrackedVolume = true;
                    Assert.That(results[index].feature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                    Assert.That(results[index].shape, Is.EqualTo(BoundProxyShapeType.Sphere));
                    Assert.That(results[index].sphereRadius, Is.EqualTo(3.0f));
                    break;
                }

                Assert.That(foundTrackedVolume, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
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
