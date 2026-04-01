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
                volume.SendMessage("OnValidate");

                IBoundProxyProvider provider = volume;

                Assert.That(provider.BoundProxyFeature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                Assert.That(provider.IsBoundProxyActive, Is.True);
                Assert.That(provider.BoundProxyTransform, Is.EqualTo(volume.transform));
                Assert.That(provider.BoundProxyShape.shape, Is.EqualTo(BoundProxyShapeType.Box));
                Assert.That(provider.BoundProxyShape.center, Is.EqualTo(Vector3.zero));
                Assert.That(provider.BoundProxyShape.size, Is.EqualTo(new Vector3(4.0f, 6.0f, 8.0f)));
                Assert.That(BoundProxyUtility.TryCreateWorldData(provider, out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                Assert.That(worldData.entityId, Is.EqualTo(volume.transform.GetEntityId()));
                AssertVector3(worldData.worldCenter, new Vector3(2.0f, 1.0f, 3.0f));
                Assert.That(worldData.worldRotation, Is.EqualTo(Quaternion.identity));
                AssertVector3(worldData.worldAabb.size, new Vector3(4.0f, 6.0f, 8.0f));
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
                SerializedProperty blendDistanceProperty = serializedObject.FindProperty("m_BlendDistance");

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.center.vector3Value = new Vector3(1.0f, 2.0f, 3.0f);
                serializedShape.radius.floatValue = 4.0f;
                blendDistanceProperty.floatValue = 1.5f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");
                serializedObject.Update();

                Assert.That(
                    BoundProxyEditorUtility.GetPrimaryShapeProperty(serializedShape).propertyPath,
                    Is.EqualTo("m_BoundProxy.radius"));

                BoundProxyShape shape = BoundProxyEditorUtility.GetShapeValue(serializedShape);

                Assert.That(shape.shape, Is.EqualTo(BoundProxyShapeType.Sphere));
                Assert.That(shape.center, Is.EqualTo(Vector3.zero));
                Assert.That(shape.radius, Is.EqualTo(4.0f));
                Assert.That(blendDistanceProperty.floatValue, Is.EqualTo(1.5f));
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

        [Test]
        public void DDGIVolume_EvaluateBlendFactor_UsesInnerBlendShell_ForBoxShape()
        {
            var volumeObject = new GameObject("DDGI Volume Blend Box Test");
            volumeObject.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));
                SerializedProperty blendDistanceProperty = serializedObject.FindProperty("m_BlendDistance");

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Box;
                serializedShape.size.vector3Value = new Vector3(4.0f, 6.0f, 8.0f);
                blendDistanceProperty.floatValue = 1.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");

                Assert.That(volume.BlendDistance, Is.EqualTo(1.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(1.0f, 2.0f, 3.0f)), Is.EqualTo(1.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(2.5f, 2.0f, 3.0f)), Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(3.0f, 2.0f, 3.0f)), Is.EqualTo(0.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(3.1f, 2.0f, 3.0f)), Is.EqualTo(0.0f));
                Assert.That(BoundProxyUtility.TryCreateWorldData(volume, out BoundProxyWorldData worldData), Is.True);
                AssertVector3(worldData.worldCenter, new Vector3(1.0f, 2.0f, 3.0f));
                AssertVector3(worldData.worldAabb.size, new Vector3(4.0f, 6.0f, 8.0f));
                AssertVector3(volume.BlendInnerLocalBounds.size, new Vector3(2.0f, 4.0f, 6.0f));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void DDGIVolume_EvaluateBlendFactor_UsesInnerBlendShell_ForSphereShape()
        {
            var volumeObject = new GameObject("DDGI Volume Blend Sphere Test");
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));
                SerializedProperty blendDistanceProperty = serializedObject.FindProperty("m_BlendDistance");

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.radius.floatValue = 3.0f;
                blendDistanceProperty.floatValue = 1.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");

                Assert.That(volume.EvaluateBlendFactor(Vector3.zero), Is.EqualTo(1.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(2.5f, 0.0f, 0.0f)), Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(3.0f, 0.0f, 0.0f)), Is.EqualTo(0.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(3.1f, 0.0f, 0.0f)), Is.EqualTo(0.0f));
                Assert.That(BoundProxyUtility.TryCreateWorldData(volume, out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.sphereRadius, Is.EqualTo(3.0f));
                AssertVector3(worldData.worldAabb.size, Vector3.one * 6.0f);
                Assert.That(volume.BlendInnerBoundProxyShape.radius, Is.EqualTo(2.0f));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void DDGIVolume_OnValidate_ClampsNegativeBlendDistance_ToZero()
        {
            var volumeObject = new GameObject("DDGI Volume Blend Clamp Test");
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                SerializedObject serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                SerializedProperty blendDistanceProperty = serializedObject.FindProperty("m_BlendDistance");
                var serializedShape =
                    new SerializedBoundProxyShape(serializedObject.FindProperty("m_BoundProxy"));

                serializedShape.shape.intValue = (int)BoundProxyShapeType.Sphere;
                serializedShape.radius.floatValue = 3.0f;
                blendDistanceProperty.floatValue = -2.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");
                serializedObject.Update();

                Assert.That(volume.BlendDistance, Is.EqualTo(0.0f));
                Assert.That(blendDistanceProperty.floatValue, Is.EqualTo(0.0f));
                Assert.That(volume.EvaluateBlendFactor(new Vector3(3.1f, 0.0f, 0.0f)), Is.EqualTo(0.0f));
                Assert.That(BoundProxyUtility.TryCreateWorldData(volume, out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.sphereRadius, Is.EqualTo(3.0f));
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
