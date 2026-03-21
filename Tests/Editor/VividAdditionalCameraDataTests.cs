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
        public void MatrixAccessors_ReturnAttachedCameraMatrices_WhenNoRuntimeOverridesExist()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(3.0f, 4.0f, -5.0f);
            camera.transform.rotation = Quaternion.Euler(10.0f, 20.0f, 30.0f);

            var additionalData = camera.GetVividAdditionalCameraData();

            AssertMatrixAreEqual(camera.worldToCameraMatrix, additionalData.viewMatrix);
            AssertMatrixAreEqual(camera.worldToCameraMatrix.inverse, additionalData.inverseViewMatrix);
            AssertMatrixAreEqual(camera.projectionMatrix, additionalData.projectionMatrix);
            AssertMatrixAreEqual(camera.nonJitteredProjectionMatrix, additionalData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(Matrix4x4.identity, additionalData.jitterMatrix);
            Assert.That(additionalData.jitter, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void MatrixAccessors_ReadCameraJitter_WhenCameraProvidesJitteredProjection()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var additionalData = camera.GetVividAdditionalCameraData();
            var nonJitteredProjectionMatrix = Matrix4x4.Perspective(60.0f, 1.7f, 0.1f, 1000.0f);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.125f, -0.25f, 0.0f));
            var jitteredProjectionMatrix = jitterMatrix * nonJitteredProjectionMatrix;

            camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            camera.projectionMatrix = jitteredProjectionMatrix;

            AssertMatrixAreEqual(nonJitteredProjectionMatrix, additionalData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(jitteredProjectionMatrix, additionalData.projectionMatrix);
            AssertMatrixAreEqual(jitterMatrix, additionalData.jitterMatrix);
            Assert.That(additionalData.jitter.x, Is.EqualTo(jitterMatrix.m03).Within(0.00001f));
            Assert.That(additionalData.jitter.y, Is.EqualTo(jitterMatrix.m13).Within(0.00001f));
        }

        [Test]
        public void MatrixAccessors_ReturnStoredMatrices_WhenRuntimeOverridesExist()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var additionalData = camera.GetVividAdditionalCameraData();
            var viewMatrix = Matrix4x4.Translate(new Vector3(1.0f, 2.0f, 3.0f));
            var projectionMatrix = Matrix4x4.Perspective(60.0f, 1.7f, 0.1f, 1000.0f);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.125f, -0.25f, 0.0f));
            var jitter = new Vector2(0.125f, -0.25f);
            var jitteredProjectionMatrix = jitterMatrix * projectionMatrix;
            var gpuProjectionMatrix = GL.GetGPUProjectionMatrix(jitteredProjectionMatrix, false);

            additionalData.SetViewProjectionAndJitterMatrix(viewMatrix, projectionMatrix, jitterMatrix, jitter);

            AssertMatrixAreEqual(viewMatrix, additionalData.viewMatrix);
            AssertMatrixAreEqual(viewMatrix.inverse, additionalData.inverseViewMatrix);
            AssertMatrixAreEqual(projectionMatrix, additionalData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(jitteredProjectionMatrix, additionalData.projectionMatrix);
            AssertMatrixAreEqual(jitterMatrix, additionalData.jitterMatrix);
            Assert.That(additionalData.jitter, Is.EqualTo(jitter));
            AssertMatrixAreEqual(jitteredProjectionMatrix * viewMatrix, additionalData.viewProjectionMatrix);
            AssertMatrixAreEqual(gpuProjectionMatrix, additionalData.GetGPUProjectionMatrix(false));
            AssertMatrixAreEqual(GL.GetGPUProjectionMatrix(projectionMatrix, false), additionalData.GetGPUProjectionMatrixNoJitter(false));
            AssertMatrixAreEqual(gpuProjectionMatrix * viewMatrix, additionalData.GetGPUViewProjectionMatrix(false));
        }

        [Test]
        public void UpdateCameraMatrices_RefreshesMatricesAndClearsJitter()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var additionalData = camera.GetVividAdditionalCameraData();

            additionalData.SetViewProjectionAndJitterMatrix(
                Matrix4x4.identity,
                Matrix4x4.Ortho(-1.0f, 1.0f, -1.0f, 1.0f, 0.1f, 100.0f),
                Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.0f)),
                new Vector2(0.5f, 0.5f));

            camera.transform.position = new Vector3(-2.0f, 1.0f, -6.0f);
            camera.transform.rotation = Quaternion.Euler(15.0f, 30.0f, 0.0f);

            additionalData.UpdateCameraMatrices(true);

            AssertMatrixAreEqual(camera.worldToCameraMatrix, additionalData.viewMatrix);
            AssertMatrixAreEqual(camera.projectionMatrix, additionalData.projectionMatrix);
            AssertMatrixAreEqual(camera.nonJitteredProjectionMatrix, additionalData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(GL.GetGPUProjectionMatrix(camera.projectionMatrix, true), additionalData.gpuProjectionMatrix);
            AssertMatrixAreEqual(Matrix4x4.identity, additionalData.jitterMatrix);
            Assert.That(additionalData.jitter, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void VividSerializedCamera_ExposesAdditionalCameraProperties_WhenCameraIsWrapped()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var serializedCamera = new VividSerializedCamera(new SerializedObject(camera));

            Assert.That(serializedCamera.camerasAdditionalData, Has.Length.EqualTo(1));
            Assert.That(serializedCamera.nearClippingPlane, Is.Not.Null);
            Assert.That(serializedCamera.farClippingPlane, Is.Not.Null);
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

        private static void AssertMatrixAreEqual(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.00001f));
                }
            }
        }
    }

    public class VividCameraDataTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(VividAdditionalCameraDataEditorUtility).TypeHandle);
            m_GameObject = new GameObject("Vivid Camera Data Test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void MatrixAccessors_ReturnCameraMatrices_WhenAdditionalDataIsMissing()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(1.0f, -2.0f, -8.0f);
            camera.transform.rotation = Quaternion.Euler(12.0f, 34.0f, 0.0f);

            var nonJitteredProjectionMatrix = Matrix4x4.Perspective(55.0f, 1.5f, 0.3f, 500.0f);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.0625f, -0.125f, 0.0f));
            var jitteredProjectionMatrix = jitterMatrix * nonJitteredProjectionMatrix;
            camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            camera.projectionMatrix = jitteredProjectionMatrix;

            var cameraData = new VividCameraData
            {
                camera = camera,
            };

            AssertMatrixAreEqual(camera.worldToCameraMatrix, cameraData.viewMatrix);
            AssertMatrixAreEqual(jitteredProjectionMatrix, cameraData.projectionMatrix);
            AssertMatrixAreEqual(nonJitteredProjectionMatrix, cameraData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(jitterMatrix, cameraData.jitterMatrix);
            Assert.That(cameraData.jitter.x, Is.EqualTo(jitterMatrix.m03).Within(0.00001f));
            Assert.That(cameraData.jitter.y, Is.EqualTo(jitterMatrix.m13).Within(0.00001f));
            AssertMatrixAreEqual(GL.GetGPUProjectionMatrix(jitteredProjectionMatrix, false), cameraData.gpuProjectionMatrix);
        }

        [Test]
        public void MatrixAccessors_ForwardToAdditionalCameraData_WhenAvailable()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var additionalData = camera.GetVividAdditionalCameraData();
            var viewMatrix = Matrix4x4.Translate(new Vector3(2.0f, 3.0f, 4.0f));
            var projectionMatrix = Matrix4x4.Perspective(40.0f, 1.2f, 0.1f, 200.0f);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(-0.25f, 0.125f, 0.0f));
            var jitter = new Vector2(-0.25f, 0.125f);
            var jitteredProjectionMatrix = jitterMatrix * projectionMatrix;
            var gpuProjectionMatrix = GL.GetGPUProjectionMatrix(jitteredProjectionMatrix, false);

            additionalData.SetViewProjectionAndJitterMatrix(viewMatrix, projectionMatrix, jitterMatrix, jitter);

            var cameraData = new VividCameraData
            {
                camera = camera,
                additionalData = additionalData,
            };

            AssertMatrixAreEqual(viewMatrix, cameraData.viewMatrix);
            AssertMatrixAreEqual(projectionMatrix, cameraData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(jitteredProjectionMatrix, cameraData.projectionMatrix);
            AssertMatrixAreEqual(jitterMatrix, cameraData.jitterMatrix);
            Assert.That(cameraData.jitter, Is.EqualTo(jitter));
            AssertMatrixAreEqual(jitteredProjectionMatrix * viewMatrix, cameraData.viewProjectionMatrix);
            AssertMatrixAreEqual(gpuProjectionMatrix, cameraData.GetGPUProjectionMatrix(false));
        }

        [Test]
        public void ExplicitRenderIntoTextureGpuProjection_UsesRequestedConvention_WhenAdditionalDataIsMissing()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var nonJitteredProjectionMatrix = Matrix4x4.Perspective(47.0f, 1.6f, 0.2f, 300.0f);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.03125f, -0.0625f, 0.0f));
            var jitteredProjectionMatrix = jitterMatrix * nonJitteredProjectionMatrix;
            camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            camera.projectionMatrix = jitteredProjectionMatrix;

            var cameraData = new VividCameraData
            {
                camera = camera,
            };

            AssertMatrixAreEqual(
                GL.GetGPUProjectionMatrix(jitteredProjectionMatrix, true),
                cameraData.GetGPUProjectionMatrix(true));
            AssertMatrixAreEqual(
                GL.GetGPUProjectionMatrix(nonJitteredProjectionMatrix, true),
                cameraData.GetGPUProjectionMatrixNoJitter(true));
        }

        private static void AssertMatrixAreEqual(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.00001f));
                }
            }
        }
    }
}
