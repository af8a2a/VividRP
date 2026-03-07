using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividAdditionalCameraDataTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(VividAdditionalCameraDataEditorUtility).TypeHandle);
            m_GameObject = new GameObject("Vivid Camera Test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void GetVividAdditionalCameraData_AddsComponent_WhenMissing()
        {
            var camera = m_GameObject.AddComponent<Camera>();

            var additionalData = camera.GetVividAdditionalCameraData();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That(camera.GetComponent<VividAdditionalCameraData>(), Is.SameAs(additionalData));
        }

        [Test]
        public void VividSerializedCamera_ExposesAdditionalCameraProperties_WhenCameraIsWrapped()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var serializedCamera = new VividSerializedCamera(new SerializedObject(camera));

            Assert.That(serializedCamera.camerasAdditionalData, Has.Length.EqualTo(1));
            Assert.That(serializedCamera.renderType, Is.Not.Null);
            Assert.That(serializedCamera.clearDepth, Is.Not.Null);
            Assert.That(serializedCamera.stopNaNs, Is.Not.Null);
            Assert.That(serializedCamera.dithering, Is.Not.Null);
            Assert.That(serializedCamera.volumeLayerMask, Is.Not.Null);
        }

        [Test]
        public void ObjectFactory_AddsAdditionalCameraData_WhenCameraComponentIsCreated()
        {
            ObjectFactory.AddComponent<Camera>(m_GameObject);

            var additionalData = m_GameObject.GetComponent<VividAdditionalCameraData>();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That((additionalData.hideFlags & HideFlags.HideInInspector) != 0, Is.True);
        }
    }
}
