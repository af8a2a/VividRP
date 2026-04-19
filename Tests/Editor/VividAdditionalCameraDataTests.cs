using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
            PassRecorder.Dispose();
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
        public void UpdateCameraMatrices_FallsBackToCameraParameters_WhenUnityProjectionMatricesAreIdentity()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;

            var additionalData = camera.GetVividAdditionalCameraData();
            camera.nonJitteredProjectionMatrix = Matrix4x4.identity;
            camera.projectionMatrix = Matrix4x4.identity;

            additionalData.UpdateCameraMatrices(false);

            var expectedProjectionMatrix = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            AssertMatrixAreEqual(expectedProjectionMatrix, additionalData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(expectedProjectionMatrix, additionalData.projectionMatrix);
            AssertMatrixAreEqual(GL.GetGPUProjectionMatrix(expectedProjectionMatrix, false), additionalData.gpuProjectionMatrix);
            AssertMatrixAreEqual(Matrix4x4.identity, additionalData.jitterMatrix);
            Assert.That(additionalData.jitter, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void InitializeContext_SynchronizesRawProjectionMatrices_WhenTaaIsDisabledAndUnityReturnsIdentity()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;
            camera.nonJitteredProjectionMatrix = Matrix4x4.identity;
            camera.projectionMatrix = Matrix4x4.identity;

            var additionalData = camera.GetVividAdditionalCameraData();
            additionalData.enableTAA = false;

            PassRecorder.InitializeContext(default, camera, default);

            var expectedProjectionMatrix = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            AssertMatrixAreEqual(expectedProjectionMatrix, camera.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(expectedProjectionMatrix, camera.projectionMatrix);
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

        [Test]
        public void GetPixelCoordToViewDirWSMatrix_Source_UsesCameraRenderTargetConvention()
        {
            var cameraDataExtensionSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.Extension.cs"));
            var additionalCameraDataExtensionSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "ComponentData", "VividAdditionalCameraData.Extension.cs"));
            var shaderVariablesSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.ShaderVariables.cs"));
            var temporalDataSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "CameraTemporalData.cs"));

            Assert.That(cameraDataExtensionSource, Does.Contain("var gpuProj = GetGPUProjectionMatrix();"));
            Assert.That(cameraDataExtensionSource, Does.Not.Contain("var gpuProj = GetGPUProjectionMatrix(true);"));
            Assert.That(additionalCameraDataExtensionSource, Does.Contain("var gpuProj = GetGPUProjectionMatrix();"));
            Assert.That(additionalCameraDataExtensionSource, Does.Not.Contain("var gpuProj = GetGPUProjectionMatrix(true);"));
            Assert.That(shaderVariablesSource, Does.Contain("var glstateMatrixProjection = GetGPUProjectionMatrix();"));
            Assert.That(shaderVariablesSource, Does.Not.Contain("var glstateMatrixProjection = GetGPUProjectionMatrix(true);"));
            Assert.That(temporalDataSource, Does.Contain("var gpuProjNoJitter = cameraData.GetGPUProjectionMatrixNoJitter();"));
            Assert.That(temporalDataSource, Does.Not.Contain("var gpuProjNoJitter = cameraData.GetGPUProjectionMatrixNoJitter(true);"));
        }

        [Test]
        public void MatrixAccessors_FallbackToCameraParameters_WhenUnityProjectionMatricesAreIdentity()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 1.7777778f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 60.0f;
            camera.nonJitteredProjectionMatrix = Matrix4x4.identity;
            camera.projectionMatrix = Matrix4x4.identity;

            var cameraData = new VividCameraData
            {
                camera = camera,
            };

            var expectedProjectionMatrix = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            AssertMatrixAreEqual(expectedProjectionMatrix, cameraData.nonJitteredProjectionMatrix);
            AssertMatrixAreEqual(expectedProjectionMatrix, cameraData.projectionMatrix);
            AssertMatrixAreEqual(GL.GetGPUProjectionMatrix(expectedProjectionMatrix, false), cameraData.gpuProjectionMatrix);
            AssertMatrixAreEqual(Matrix4x4.identity, cameraData.jitterMatrix);
            Assert.That(cameraData.jitter, Is.EqualTo(Vector2.zero));
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

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
    public class LightGridPassTests
    {
        [Test]
        public void Prepare_UsesPrecomputedAreaLights_WithoutScanningScene()
        {
            var cameraObject = new GameObject("Light Grid Camera");
            var visibleAreaLightObject = new GameObject("Visible Rectangle Area Light");
            var sceneOnlyAreaLightObject = new GameObject("Scene-Only Rectangle Area Light");
            var pass = new LightGridPass();

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = Vector3.zero;
                camera.transform.rotation = Quaternion.identity;
                camera.fieldOfView = 60.0f;
                camera.aspect = 1.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100.0f;

                var visibleAreaLight = visibleAreaLightObject.AddComponent<Light>();
                visibleAreaLight.type = LightType.Rectangle;
                visibleAreaLight.range = 12.0f;
                visibleAreaLight.areaSize = new Vector2(4.0f, 2.0f);
                visibleAreaLight.intensity = 3.0f;
                visibleAreaLight.color = Color.white;
                visibleAreaLightObject.transform.position = new Vector3(0.0f, 0.0f, 6.0f);

                var sceneOnlyAreaLight = sceneOnlyAreaLightObject.AddComponent<Light>();
                sceneOnlyAreaLight.type = LightType.Rectangle;
                sceneOnlyAreaLight.range = 12.0f;
                sceneOnlyAreaLight.areaSize = new Vector2(4.0f, 2.0f);
                sceneOnlyAreaLight.intensity = 3.0f;
                sceneOnlyAreaLight.color = Color.white;
                sceneOnlyAreaLightObject.transform.position = new Vector3(0.5f, 0.0f, 8.0f);

                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.camera = camera;
                cameraData.pixelWidth = 256;
                cameraData.pixelHeight = 256;
                cameraData.actualWidth = 256;
                cameraData.actualHeight = 256;

                var lightData = frameData.GetOrCreate<VividLightData>();
                lightData.UpdateAreaLights(new[] { visibleAreaLight });

                pass.Prepare(frameData);

                var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();

                Assert.That(lightData.hasAreaLights, Is.True);
                Assert.That(ContainsAreaLight(lightData, visibleAreaLight), Is.True);
                Assert.That(ContainsAreaLight(lightData, sceneOnlyAreaLight), Is.False);
                Assert.That(clusteredLightingData.areaLightCount, Is.EqualTo(lightData.areaLightCount));
                Assert.That(clusteredLightingData.areaLightCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(clusteredLightingData.areaLights, Is.Not.Null);
                Assert.That(GetPrivateField<int>(pass, "m_AreaLightCount"), Is.EqualTo(lightData.areaLightCount));

                var clusterCount = GetPrivateField<int>(pass, "m_ClusterCount");
                var punctualLightIndexCapacity = GetPrivateField<int>(pass, "m_PunctualLightIndexCapacity");
                var layeredOffsetGraphicsBuffer = GetImportedGraphicsBuffer(clusteredLightingData.layeredOffset);
                var layeredLightListGraphicsBuffer = GetImportedGraphicsBuffer(clusteredLightingData.layeredLightList);
                var packedAreaCell = new uint[1];
                layeredOffsetGraphicsBuffer.GetData(packedAreaCell, 0, clusterCount, 1);
                UnpackLightCell(packedAreaCell[0], out var areaLightListStart, out var areaLightCount);

                Assert.That(areaLightCount, Is.EqualTo(1u));
                Assert.That(areaLightListStart, Is.EqualTo((uint)punctualLightIndexCapacity));

                var areaLightIndices = new uint[1];
                layeredLightListGraphicsBuffer.GetData(areaLightIndices, 0, punctualLightIndexCapacity, 1);
                Assert.That(areaLightIndices[0], Is.EqualTo(0u));
            }
            finally
            {
                pass.Dispose();
                Object.DestroyImmediate(sceneOnlyAreaLightObject);
                Object.DestroyImmediate(visibleAreaLightObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on '{instance.GetType().Name}'.");

            return (T)field.GetValue(instance);
        }

        private static GraphicsBuffer GetImportedGraphicsBuffer(RenderGraphBuffer buffer)
        {
            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);

            var importedGraphicsBuffer = (GraphicsBuffer)importedGraphicsBufferProperty.GetValue(buffer);
            Assert.That(importedGraphicsBuffer, Is.Not.Null);
            return importedGraphicsBuffer;
        }

        private static void UnpackLightCell(uint packedValue, out uint offset, out uint count)
        {
            offset = packedValue & 67108863u;
            count = (packedValue >> 26) & 63u;
        }

        private static bool ContainsAreaLight(VividLightData lightData, Light light)
        {
            if (lightData == null || light == null)
                return false;

            var expectedPosition = light.transform.position;
            var expectedType = light.type == LightType.Tube ? 0u : 1u;

            for (var lightIndex = 0; lightIndex < lightData.areaLightCount; lightIndex++)
            {
                var areaLight = lightData.areaLights[lightIndex];
                if (Mathf.Abs(areaLight.positionWS.x - expectedPosition.x) > 0.0001f
                    || Mathf.Abs(areaLight.positionWS.y - expectedPosition.y) > 0.0001f
                    || Mathf.Abs(areaLight.positionWS.z - expectedPosition.z) > 0.0001f
                    || Mathf.Abs(areaLight.width - light.areaSize.x) > 0.0001f
                    || Mathf.Abs(areaLight.height - light.areaSize.y) > 0.0001f
                    || areaLight.lightType != expectedType)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
