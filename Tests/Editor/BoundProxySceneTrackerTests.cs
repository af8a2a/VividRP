using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.ObjectDispatching;

namespace VividRP.Editor.Tests
{
    public class BoundProxySceneTrackerTests
    {
        [SetUp]
        [TearDown]
        public void CleanupProviders()
        {
            TestBoundProxyProvider[] providers = Object.FindObjectsByType<TestBoundProxyProvider>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID);
            for (int index = 0; index < providers.Length; index++)
            {
                if (providers[index] != null)
                {
                    Object.DestroyImmediate(providers[index].gameObject);
                }
            }

            ObjectDispatcherService.ProcessUpdates();
        }

        [Test]
        public void GetWorldData_TracksCreatedProvidersWithoutDuplicates_WhenProcessUpdatesRuns()
        {
            var providerObject = new GameObject("Tracked Bound Proxy Provider");

            try
            {
                using var tracker = new BoundProxySceneTracker<TestBoundProxyProvider>();
                var provider = providerObject.AddComponent<TestBoundProxyProvider>();
                provider.SetFeature(BoundProxyFeature.DDGIVolume);
                provider.SetShape(new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Sphere,
                    center = new Vector3(0.0f, 1.0f, 5.0f),
                    radius = 2.0f,
                });

                ObjectDispatcherService.ProcessUpdates();
                ObjectDispatcherService.ProcessUpdates();

                var results = new List<BoundProxyWorldData>();
                tracker.GetWorldData(results);

                Assert.That(tracker.TrackedProviderCount, Is.EqualTo(1));
                Assert.That(results.Count, Is.EqualTo(1));
                Assert.That(results[0].feature, Is.EqualTo(BoundProxyFeature.DDGIVolume));
                Assert.That(results[0].entityId, Is.EqualTo(provider.transform.GetEntityId()));
                Assert.That(tracker.TryGetWorldData(provider.transform.GetEntityId(), out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.sphereRadius, Is.EqualTo(2.0f));
            }
            finally
            {
                Object.DestroyImmediate(providerObject);
                ObjectDispatcherService.ProcessUpdates();
            }
        }

        [Test]
        public void GetWorldData_ReflectsProviderStateChanges_WhenProviderMovesOrDisables()
        {
            var providerObject = new GameObject("Mutable Bound Proxy Provider");
            var provider = providerObject.AddComponent<TestBoundProxyProvider>();
            provider.SetFeature(BoundProxyFeature.LocalVolumetricFog);
            provider.SetShape(new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 0.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f),
            });
            provider.transform.position = new Vector3(3.0f, 0.0f, 0.0f);
            provider.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            EntityId entityId = provider.transform.GetEntityId();

            try
            {
                using var tracker = new BoundProxySceneTracker<TestBoundProxyProvider>();
                var results = new List<BoundProxyWorldData>();

                tracker.GetWorldData(results);

                Assert.That(results.Count, Is.EqualTo(1));
                AssertVector3(results[0].worldCenter, new Vector3(3.0f, 0.0f, -1.0f));
                Assert.That(tracker.TryGetWorldData(entityId, out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.LocalVolumetricFog));

                provider.SetProviderActive(false);
                tracker.GetWorldData(results);

                Assert.That(results, Is.Empty);
                Assert.That(tracker.TryGetWorldData(entityId, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(providerObject);
                ObjectDispatcherService.ProcessUpdates();
            }
        }

        [Test]
        public void ProcessUpdates_RemovesDestroyedProvidersAndClearsEntityMapping()
        {
            var providerObject = new GameObject("Destroyed Bound Proxy Provider");
            var provider = providerObject.AddComponent<TestBoundProxyProvider>();
            provider.SetShape(new BoundProxyShape
            {
                shape = BoundProxyShapeType.Sphere,
                center = new Vector3(0.0f, 0.0f, 5.0f),
                radius = 1.0f,
            });
            EntityId entityId = provider.transform.GetEntityId();

            using var tracker = new BoundProxySceneTracker<TestBoundProxyProvider>();
            Object.DestroyImmediate(providerObject);
            ObjectDispatcherService.ProcessUpdates();

            var results = new List<BoundProxyWorldData>();
            tracker.GetWorldData(results);

            Assert.That(tracker.TrackedProviderCount, Is.Zero);
            Assert.That(results, Is.Empty);
            Assert.That(tracker.TryGetWorldData(entityId, out _), Is.False);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
