using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividAdditionalReflectionDataTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(VividAdditionalReflectionDataEditorUtility).TypeHandle);
            m_GameObject = new GameObject("Vivid Reflection Probe Test");
        }

        [TearDown]
        public void TearDown()
        {
            GameObject.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void GetVividAdditionalReflectionData_AddsComponent_WhenMissing()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();

            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That(reflectionProbe.GetComponent<VividAdditionalReflectionData>(), Is.SameAs(additionalData));
        }

        [Test]
        public void Reset_PullsDefaultBoxSettings_FromReflectionProbe()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            reflectionProbe.size = new Vector3(2.0f, 4.0f, 6.0f);
            reflectionProbe.center = new Vector3(0.5f, -1.0f, 2.0f);
            reflectionProbe.blendDistance = 1.25f;
            reflectionProbe.importance = 9;
            reflectionProbe.boxProjection = true;

            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();

            AssertVector3(additionalData.influenceBoxSize, reflectionProbe.size);
            AssertVector3(additionalData.influenceBoxOffset, reflectionProbe.center);
            AssertVector3(additionalData.capturePositionOffset, Vector3.zero);
            AssertVector3(additionalData.boxBlendDistancePositive, new Vector3(1.0f, 1.25f, 1.25f));
            AssertVector3(additionalData.boxBlendDistanceNegative, new Vector3(1.0f, 1.25f, 1.25f));
            Assert.That(additionalData.importance, Is.EqualTo(reflectionProbe.importance));
            Assert.That(additionalData.proxyVolumeMode, Is.EqualTo(VividReflectionProbeProxyVolumeMode.InfluenceVolume));
        }

        [Test]
        public void SyncReflectionProbe_ClampsAndWritesUnityProbeFields()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();
            additionalData.influenceBoxSize = new Vector3(-2.0f, 0.0f, 8.0f);
            additionalData.influenceBoxOffset = new Vector3(1.0f, 2.0f, 3.0f);
            additionalData.boxBlendDistancePositive = new Vector3(3.0f, 4.0f, 10.0f);
            additionalData.boxBlendDistanceNegative = new Vector3(0.25f, 0.5f, 0.75f);
            additionalData.boxSideFadePositive = new Vector3(-1.0f, 0.5f, 2.0f);
            additionalData.boxSideFadeNegative = new Vector3(0.25f, 1.5f, 1.0f);
            additionalData.capturePositionOffset = new Vector3(0.0f, 1.0f, 0.0f);
            additionalData.boxPerAxisControl = true;
            additionalData.importance = VividAdditionalReflectionData.MaxImportance + 10;
            additionalData.proxyVolumeMode = VividReflectionProbeProxyVolumeMode.Box;
            additionalData.proxyBoxSize = new Vector3(3.0f, 4.0f, 5.0f);
            additionalData.proxyBoxOffset = new Vector3(-1.0f, -2.0f, -3.0f);

            additionalData.SyncReflectionProbe();

            AssertVector3(additionalData.influenceBoxSize, new Vector3(2.0f, VividAdditionalReflectionData.MinBoxSize, 8.0f));
            AssertVector3(reflectionProbe.size, additionalData.influenceBoxSize);
            AssertVector3(reflectionProbe.center, additionalData.influenceBoxOffset);
            Assert.That(reflectionProbe.blendDistance, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(reflectionProbe.importance, Is.EqualTo(VividAdditionalReflectionData.MaxImportance));
            Assert.That(reflectionProbe.boxProjection, Is.True);
            AssertVector3(additionalData.capturePositionOffset, new Vector3(0.0f, 1.0f, 0.0f));
            Assert.That(additionalData.boxPerAxisControl, Is.True);
            AssertVector3(additionalData.boxBlendDistancePositive, new Vector3(1.0f, VividAdditionalReflectionData.MinBoxSize * 0.5f, 4.0f));
            AssertVector3(additionalData.boxSideFadePositive, new Vector3(0.0f, 0.5f, 1.0f));
            AssertVector3(additionalData.boxSideFadeNegative, new Vector3(0.25f, 1.0f, 1.0f));
            AssertVector3(additionalData.GetProxyBoxSize(), new Vector3(3.0f, 4.0f, 5.0f));
            AssertVector3(additionalData.GetProxyBoxOffset(), new Vector3(-1.0f, -2.0f, -3.0f));
        }

        [Test]
        public void SyncReflectionProbe_DisablesBoxProjection_WhenProxyIsInfinite()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();
            additionalData.proxyVolumeMode = VividReflectionProbeProxyVolumeMode.Infinite;

            additionalData.SyncReflectionProbe();

            Assert.That(additionalData.isProjectionInfinite, Is.True);
            Assert.That(reflectionProbe.boxProjection, Is.False);
        }

        [Test]
        public void SyncReflectionProbeIfDirty_SyncsOnlyAfterAdditionalDataChanges()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();

            additionalData.influenceBoxSize = new Vector3(2.0f, 3.0f, 4.0f);
            additionalData.SyncReflectionProbeIfDirty();

            AssertVector3(reflectionProbe.size, new Vector3(2.0f, 3.0f, 4.0f));

            reflectionProbe.size = new Vector3(8.0f, 8.0f, 8.0f);
            additionalData.SyncReflectionProbeIfDirty();

            AssertVector3(reflectionProbe.size, new Vector3(8.0f, 8.0f, 8.0f));

            additionalData.influenceBoxOffset = new Vector3(1.0f, 2.0f, 3.0f);
            additionalData.SyncReflectionProbeIfDirty();

            AssertVector3(reflectionProbe.center, new Vector3(1.0f, 2.0f, 3.0f));
        }

        [Test]
        public void ObjectFactory_AddsAdditionalReflectionData_WhenReflectionProbeComponentIsCreated()
        {
            var reflectionProbe = ObjectFactory.AddComponent<ReflectionProbe>(m_GameObject);

            var additionalData = reflectionProbe.GetComponent<VividAdditionalReflectionData>();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That((additionalData.hideFlags & HideFlags.HideInInspector) != 0, Is.True);
        }

        [Test]
        public void TryGetAdditionalData_UsesEnabledReflectionProbeRegistration()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            var additionalData = reflectionProbe.GetVividAdditionalReflectionData();

            Assert.That(VividAdditionalReflectionData.hasRegisteredData, Is.True);

            Assert.That(
                VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe, out var cachedData),
                Is.True);
            Assert.That(cachedData, Is.SameAs(additionalData));
            Assert.That(
                VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe.GetEntityId(), out cachedData),
                Is.True);
            Assert.That(cachedData, Is.SameAs(additionalData));

            additionalData.enabled = false;

            Assert.That(VividAdditionalReflectionData.hasRegisteredData, Is.False);
            Assert.That(
                VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe, out cachedData),
                Is.False);
            Assert.That(cachedData, Is.Null);
            Assert.That(
                VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe.GetEntityId(), out cachedData),
                Is.False);
            Assert.That(cachedData, Is.Null);

            additionalData.enabled = true;

            Assert.That(VividAdditionalReflectionData.hasRegisteredData, Is.True);
            Assert.That(
                VividAdditionalReflectionData.TryGetAdditionalData(reflectionProbe, out cachedData),
                Is.True);
            Assert.That(cachedData, Is.SameAs(additionalData));
        }

        [Test]
        public void VividSerializedReflectionProbe_ExposesAdditionalReflectionProperties_WhenReflectionProbeIsWrapped()
        {
            var reflectionProbe = m_GameObject.AddComponent<ReflectionProbe>();
            var serializedObject = new SerializedObject(reflectionProbe);
            var serializedReflectionProbe = new VividSerializedReflectionProbe(serializedObject);

            Assert.That(serializedReflectionProbe.reflectionProbeAdditionalData, Has.Length.EqualTo(1));
            Assert.That(serializedReflectionProbe.multiplier, Is.Not.Null);
            Assert.That(serializedReflectionProbe.weight, Is.Not.Null);
            Assert.That(serializedReflectionProbe.importance, Is.Not.Null);
            Assert.That(serializedReflectionProbe.fadeDistance, Is.Not.Null);
            Assert.That(serializedReflectionProbe.rangeCompressionFactor, Is.Not.Null);
            Assert.That(serializedReflectionProbe.capturePositionOffset, Is.Not.Null);
            Assert.That(serializedReflectionProbe.influenceBoxSize, Is.Not.Null);
            Assert.That(serializedReflectionProbe.influenceBoxOffset, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxBlendDistancePositive, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxBlendDistanceNegative, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxBlendNormalDistancePositive, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxBlendNormalDistanceNegative, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxPerAxisControl, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxSideFadePositive, Is.Not.Null);
            Assert.That(serializedReflectionProbe.boxSideFadeNegative, Is.Not.Null);
            Assert.That(serializedReflectionProbe.proxyVolumeMode, Is.Not.Null);
            Assert.That(serializedReflectionProbe.proxyBoxSize, Is.Not.Null);
            Assert.That(serializedReflectionProbe.proxyBoxOffset, Is.Not.Null);
        }

        [Test]
        public void ProxyVolumeMode_DoesNotExposeSphereShape()
        {
            Assert.That(Enum.GetNames(typeof(VividReflectionProbeProxyVolumeMode)), Does.Not.Contain("Sphere"));
        }

        [Test]
        public void VividReflectionProbeEditor_DoesNotWrapBuiltinReflectionProbeEditor()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;

            Assert.That(typeof(VividReflectionProbeEditor).GetField("BuiltinReflectionProbeEditorType", flags), Is.Null);
            Assert.That(typeof(VividReflectionProbeEditor).GetField("m_BuiltinEditor", flags), Is.Null);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
