using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Editor.Tests
{
    public sealed class DecalProjectorTests
    {
        [Test]
        public void TryCreateBoundProxyWorldData_UsesTransformRotationForDecalBounds()
        {
            var owner = new GameObject("Decal Projector Test");

            try
            {
                owner.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
                owner.transform.rotation = Quaternion.Euler(10.0f, 35.0f, 20.0f);

                var projector = owner.AddComponent<DecalProjector>();

                Assert.That(projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.entityId, Is.EqualTo(owner.transform.GetEntityId()));
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.Decal));
                AssertQuaternion(worldData.worldRotation, owner.transform.rotation);
                AssertVector3(worldData.worldCenter, owner.transform.position);
                Assert.That(worldData.worldAabb.size.x, Is.GreaterThan(0.0f));
                Assert.That(worldData.worldAabb.size.y, Is.GreaterThan(0.0f));
                Assert.That(worldData.worldAabb.size.z, Is.GreaterThan(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void Register_InitializesDecalSystem_WhenProjectorAppearsAfterDeinitialize()
        {
            var owner = new GameObject("Decal Register Test");

            try
            {
                DecalSystem.Deinitialize();

                var initializedField = typeof(DecalSystem).GetField(
                    "s_Initialized",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(initializedField, Is.Not.Null);

                var projector = owner.AddComponent<DecalProjector>();

                Assert.That(initializedField.GetValue(null), Is.EqualTo(true));

                DecalSystem.Unregister(projector);
            }
            finally
            {
                DecalSystem.Deinitialize();
                Object.DestroyImmediate(owner);
            }
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        private static void AssertQuaternion(Quaternion actual, Quaternion expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance));
        }
    }
}
