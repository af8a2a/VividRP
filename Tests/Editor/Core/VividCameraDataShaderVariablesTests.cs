using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividCameraDataShaderVariablesTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            m_GameObject = new GameObject("Vivid Camera Shader Variables Test");
            FrameContextSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FrameContextSystem.Clear();
            Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void BuildShaderVariables_ComputesCameraAndScreenGlobals_WhenCameraDataIsAvailable()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 250.0f;
            camera.transform.position = new Vector3(2.0f, 3.0f, -4.0f);
            camera.transform.rotation = Quaternion.Euler(12.0f, 25.0f, 0.0f);

            var nonJitteredProjectionMatrix = Matrix4x4.Perspective(60.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.125f, -0.25f, 0.0f));
            var jitteredProjectionMatrix = jitterMatrix * nonJitteredProjectionMatrix;
            camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            camera.projectionMatrix = jitteredProjectionMatrix;

            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 640,
                actualHeight = 360,
                pixelWidth = 1280,
                pixelHeight = 720,
            };

            var temporalData = FrameContextSystem.GetOrCreate(camera);
            temporalData.Update(cameraData);

            var shaderVariables = cameraData.BuildShaderVariables(temporalData);
            var expectedGpuProjectionMatrix = GL.GetGPUProjectionMatrix(jitteredProjectionMatrix, true);
            var expectedMotionVectorGpuProjectionMatrix = GL.GetGPUProjectionMatrix(nonJitteredProjectionMatrix, true);
            var expectedViewMatrix = camera.worldToCameraMatrix;

            AssertVectorAreEqual(new Vector4(2.0f, 3.0f, -4.0f, 1.0f), shaderVariables.worldSpaceCameraPos);
            AssertVectorAreEqual(new Vector4(1280.0f, 720.0f, 1.0f + (1.0f / 1280.0f), 1.0f + (1.0f / 720.0f)), shaderVariables.screenParams);
            AssertVectorAreEqual(new Vector4(640.0f, 360.0f, 1.0f + (1.0f / 640.0f), 1.0f + (1.0f / 360.0f)), shaderVariables.scaledScreenParams);
            AssertVectorAreEqual(new Vector4(640.0f, 360.0f, 1.0f / 640.0f, 1.0f / 360.0f), shaderVariables.screenSize);
            Assert.That(shaderVariables.globalMipBias.x, Is.EqualTo(1.0f).Within(0.00001f));
            Assert.That(shaderVariables.globalMipBias.y, Is.EqualTo(2.0f).Within(0.00001f));
            Assert.That(shaderVariables.projectionParams.y, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(shaderVariables.projectionParams.z, Is.EqualTo(250.0f).Within(0.00001f));
            Assert.That(shaderVariables.projectionParams.w, Is.EqualTo(1.0f / 250.0f).Within(0.00001f));
            Assert.That(shaderVariables.orthoParams, Is.EqualTo(Vector4.zero));

            AssertMatrixAreEqual(jitteredProjectionMatrix, shaderVariables.cameraProjection);
            AssertMatrixAreEqual(expectedViewMatrix, shaderVariables.worldToCamera);
            AssertMatrixAreEqual(expectedViewMatrix.inverse, shaderVariables.cameraToWorld);
            AssertMatrixAreEqual(expectedGpuProjectionMatrix, shaderVariables.glstateMatrixProjection);
            AssertMatrixAreEqual(expectedGpuProjectionMatrix, shaderVariables.projMatrix);
            AssertMatrixAreEqual(expectedGpuProjectionMatrix * expectedViewMatrix, shaderVariables.viewProjMatrix);
            AssertMatrixAreEqual(expectedGpuProjectionMatrix * expectedViewMatrix, shaderVariables.matrixVP);
            AssertMatrixAreEqual((expectedGpuProjectionMatrix * expectedViewMatrix).inverse, shaderVariables.invViewProjMatrix);
            AssertMatrixAreEqual(expectedMotionVectorGpuProjectionMatrix * expectedViewMatrix, shaderVariables.nonJitteredViewProjMatrix);
            AssertMatrixAreEqual(expectedMotionVectorGpuProjectionMatrix * expectedViewMatrix, shaderVariables.prevViewProjMatrix);

            Assert.That(shaderVariables.cameraWorldClipPlanes, Has.Length.EqualTo(6));
            Assert.That(shaderVariables.frustumPlanes, Has.Length.EqualTo(6));
            AssertVectorAreEqual(shaderVariables.cameraWorldClipPlanes[3], shaderVariables.frustumPlanes[2]);
            AssertVectorAreEqual(shaderVariables.cameraWorldClipPlanes[2], shaderVariables.frustumPlanes[3]);
        }

        [Test]
        public void BuildShaderVariables_ComputesOrthographicParams_WhenCameraIsOrthographic()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.0f;
            camera.aspect = 2.0f;

            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 800,
                actualHeight = 400,
            };

            var shaderVariables = cameraData.BuildShaderVariables();

            AssertVectorAreEqual(new Vector4(16.0f, 8.0f, 0.0f, 1.0f), shaderVariables.orthoParams);
        }

        [Test]
        public void BuildShaderVariables_PersistsPreviousNonJitteredViewProjection_AcrossFrames()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200.0f;
            camera.transform.position = new Vector3(1.0f, 2.0f, -3.0f);
            camera.transform.rotation = Quaternion.Euler(5.0f, 15.0f, 0.0f);

            var firstProjection = Matrix4x4.Perspective(55.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            var firstJitter = Matrix4x4.Translate(new Vector3(0.03125f, -0.0625f, 0.0f));
            camera.nonJitteredProjectionMatrix = firstProjection;
            camera.projectionMatrix = firstJitter * firstProjection;

            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 1280,
                actualHeight = 720,
                pixelWidth = 1280,
                pixelHeight = 720,
                frameIndex = 10,
            };

            var temporalData = FrameContextSystem.GetOrCreate(camera);
            temporalData.Update(cameraData);

            var firstExpectedViewProjection = GL.GetGPUProjectionMatrix(firstProjection, true) * camera.worldToCameraMatrix;
            var firstShaderVariables = cameraData.BuildShaderVariables(temporalData);

            AssertMatrixAreEqual(firstExpectedViewProjection, firstShaderVariables.nonJitteredViewProjMatrix);
            AssertMatrixAreEqual(firstExpectedViewProjection, firstShaderVariables.prevViewProjMatrix);

            camera.transform.position = new Vector3(-2.0f, 1.0f, -6.0f);
            camera.transform.rotation = Quaternion.Euler(9.0f, -20.0f, 0.0f);

            var secondProjection = Matrix4x4.Perspective(47.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            var secondJitter = Matrix4x4.Translate(new Vector3(-0.015625f, 0.03125f, 0.0f));
            camera.nonJitteredProjectionMatrix = secondProjection;
            camera.projectionMatrix = secondJitter * secondProjection;
            cameraData.frameIndex = 11;

            temporalData.Update(cameraData);

            var secondExpectedViewProjection = GL.GetGPUProjectionMatrix(secondProjection, true) * camera.worldToCameraMatrix;
            var secondShaderVariables = cameraData.BuildShaderVariables(temporalData);

            AssertMatrixAreEqual(secondExpectedViewProjection, secondShaderVariables.nonJitteredViewProjMatrix);
            AssertMatrixAreEqual(firstExpectedViewProjection, secondShaderVariables.prevViewProjMatrix);
        }

        [Test]
        public void BuildShaderVariables_EnablesCameraMotionVectorDepthFlags_WhenCameraIsAvailable()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.depthTextureMode = DepthTextureMode.None;

            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 640,
                actualHeight = 360,
            };

            cameraData.BuildShaderVariables();

            Assert.That((camera.depthTextureMode & DepthTextureMode.Depth) != 0, Is.True);
            Assert.That((camera.depthTextureMode & DepthTextureMode.MotionVectors) != 0, Is.True);
        }

        [Test]
        public void BuildShaderVariables_OnlyWritesDepthTextureMode_WhenRequiredFlagsAreMissing()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.ShaderVariables.cs"));

            Assert.That(source, Does.Contain("EnsureRequiredDepthTextureMode(currentCamera);"));
            Assert.That(source, Does.Contain("if ((depthTextureMode & requiredMode) == requiredMode)"));
            Assert.That(source, Does.Contain("m_DepthTextureModeHasRequiredFlags"));
            Assert.That(source, Does.Not.Contain("depthTextureMode |="));
        }

        [Test]
        public void BuildShaderVariables_ResolvesDimensionsWithoutEagerCameraPropertyReads()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.ShaderVariables.cs"));
            var cameraDataSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.cs"));
            var passRecorderSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(source, Does.Contain("scaledWidth = ResolveScaledWidth(currentCamera);"));
            Assert.That(source, Does.Contain("scaledHeight = ResolveScaledHeight(currentCamera);"));
            Assert.That(source, Does.Contain("referenceWidth = ResolveReferenceWidth(currentCamera, scaledWidth);"));
            Assert.That(source, Does.Contain("referenceHeight = ResolveReferenceHeight(currentCamera, scaledHeight);"));
            Assert.That(source, Does.Contain("hasCameraFrameProperties"));
            Assert.That(source, Does.Contain("ResolveRenderIntoTexture(currentCamera)"));
            Assert.That(source, Does.Contain("CreateOrthoParams(currentCamera)"));
            Assert.That(source, Does.Contain("worldSpaceCameraPos = CreateTranslationColumn(invViewMatrix);"));
            Assert.That(source, Does.Contain("var maxRatio = Mathf.Max(widthRatio, heightRatio);"));
            Assert.That(source, Does.Contain("var mipScale = Mathf.Max(maxRatio, 0.0001f);"));
            Assert.That(cameraDataSource, Does.Contain("internal void CacheCameraFrameProperties(Camera value)"));
            Assert.That(passRecorderSource, Does.Contain("cameraData.CacheCameraFrameProperties(camera);"));
            Assert.That(source, Does.Not.Contain("ResolveScaledDimension(actualWidth, currentCamera"));
            Assert.That(source, Does.Not.Contain("ResolveReferenceDimension(pixelWidth, currentCamera"));
            Assert.That(source, Does.Not.Contain("invViewMatrix.GetColumn(3)"));
            Assert.That(source, Does.Not.Contain("Mathf.Max(widthRatio, heightRatio, 0.0001f)"));
        }

        [Test]
        public void SetCamera_CachesCameraNameUntilCameraChanges()
        {
            var firstCamera = m_GameObject.AddComponent<Camera>();
            firstCamera.name = "First Camera";
            var secondCameraObject = new GameObject("Second Camera");
            var cameraData = new VividCameraData();

            try
            {
                var secondCamera = secondCameraObject.AddComponent<Camera>();

                cameraData.SetCamera(firstCamera);

                Assert.That(cameraData.camera, Is.SameAs(firstCamera));
                Assert.That(cameraData.cameraName, Is.EqualTo("First Camera"));

                firstCamera.name = "Renamed First Camera";
                cameraData.SetCamera(firstCamera);

                Assert.That(cameraData.cameraName, Is.EqualTo("First Camera"));

                cameraData.SetCamera(secondCamera);

                Assert.That(cameraData.camera, Is.SameAs(secondCamera));
                Assert.That(cameraData.cameraName, Is.EqualTo("Second Camera"));

                cameraData.Reset();

                Assert.That(cameraData.camera, Is.Null);
                Assert.That(cameraData.cameraName, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(secondCameraObject);
            }
        }

        [Test]
        public void BuildShaderVariables_ExtractsFrustumPlanesMatchingUnityCamera()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.fieldOfView = 53.0f;
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 500.0f;
            camera.transform.position = new Vector3(3.0f, 4.0f, -8.0f);
            camera.transform.rotation = Quaternion.Euler(11.0f, -22.0f, 4.0f);

            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 1280,
                actualHeight = 720,
            };

            var shaderVariables = cameraData.BuildShaderVariables();
            var expectedPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

            for (var index = 0; index < expectedPlanes.Length; index++)
            {
                var expectedPlane = expectedPlanes[index];
                var expected = new Vector4(
                    expectedPlane.normal.x,
                    expectedPlane.normal.y,
                    expectedPlane.normal.z,
                    expectedPlane.distance);

                AssertVectorAreEqual(expected, shaderVariables.cameraWorldClipPlanes[index], 0.0001f);
            }

            AssertVectorAreEqual(shaderVariables.cameraWorldClipPlanes[3], shaderVariables.frustumPlanes[2]);
            AssertVectorAreEqual(shaderVariables.cameraWorldClipPlanes[2], shaderVariables.frustumPlanes[3]);
        }

        [Test]
        public void BuildShaderVariables_ExtractsFrustumPlanesWithoutGeometryUtilityAllocation()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.ShaderVariables.cs"));

            Assert.That(source, Does.Contain("UpdateFrustumPlanes(currentCamera, cameraProjection * viewMatrix);"));
            Assert.That(source, Does.Contain("CullingUtility.ExtractFrustumPlanes(viewProjectionMatrix, m_CameraWorldClipPlanes);"));
            Assert.That(source, Does.Not.Contain("GeometryUtility.CalculateFrustumPlanes"));
            Assert.That(source, Does.Not.Contain("new Plane[6]"));
        }

        [Test]
        public void GetPixelCoordToViewDirWSMatrix_UsesActualRenderSize_ForSkyRays()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.Extension.cs"));

            Assert.That(source, Does.Contain("ResolveViewDirectionDimension(actualWidth"));
            Assert.That(source, Does.Contain("ResolveViewDirectionDimension(actualHeight"));
            Assert.That(source, Does.Contain("new Vector4(width, height, 1.0f / width, 1.0f / height)"));
            Assert.That(source, Does.Not.Contain("new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight"));
        }

        [Test]
        public void FrameContextSystem_UsesExplicitShaderVariablesGlobalConstantBuffer()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));
            var loggerSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "CameraShaderVariablesGlobalComparisonLogger.cs"));
            var autoExposureSystemSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.cs"));

            Assert.That(source, Does.Contain("SubsystemPreRender?.Invoke(frameData, cmd);"));
            Assert.That(source, Does.Not.Contain("AutoExposureRuntimeManager.PrepareFrame(frameData);"));
            Assert.That(source, Does.Not.Contain("BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(source, Does.Not.Contain("VividAutoExposureSystem.Deinitialize();"));
            Assert.That(autoExposureSystemSource, Does.Contain("internal sealed class VividAutoExposureSystem : VividSubsystem<VividAutoExposureSystem>"));
            Assert.That(autoExposureSystemSource, Does.Contain("PrepareFrame(frameData);"));
            Assert.That(autoExposureSystemSource, Does.Contain("BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(autoExposureSystemSource, Does.Contain("FrameContextSystem.SubsystemDispose += OnSubsystemDispose;"));
            Assert.That(source, Does.Contain("skyData = frameData.GetOrCreate<VividSkyData>();"));
            Assert.That(source, Does.Contain("shaderVariablesGlobal = ShaderVariablesGlobal.Create(sv, temporalData, skyData);"));
            Assert.That(source, Does.Contain("cameraData.shaderVariablesGlobal = shaderVariablesGlobal;"));
            Assert.That(source, Does.Contain("cameraData.hasShaderVariablesGlobal = true;"));
            Assert.That(source, Does.Contain("#if VIVIDRP_DEBUG"));
            Assert.That(source, Does.Contain("CameraShaderVariablesGlobalComparisonLogger.CaptureAndCompare(cameraData, shaderVariablesGlobal);"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(cmd, shaderVariablesGlobal, ShaderVariablesGlobal.ConstantBufferShaderId);"));
            Assert.That(source, Does.Not.Contain("GetOrCreate<VividCameraShaderData>"));
            Assert.That(loggerSource, Does.Contain("[Conditional(\"VIVIDRP_DEBUG\")]"));
            Assert.That(source, Does.Not.Contain("cmd.SetGlobalMatrix("));
            Assert.That(source, Does.Not.Contain("cmd.SetGlobalVector("));
        }

        [Test]
        public void CameraShaderVariablesGlobalComparisonLogger_DoesNotReport_WhenSnapshotsMatch()
        {
            var globals = new ShaderVariablesGlobal
            {
                _VividProjectionParams = new Vector4(1.0f, 0.3f, 1000.0f, 0.001f),
                _VividScreenParams = new Vector4(1920.0f, 1080.0f, 1.0f + (1.0f / 1920.0f), 1.0f + (1.0f / 1080.0f)),
                _VividZBufferParams = new Vector4(3332.3333f, 1.0f, 3.3323f, 0.001f),
                _VividWorldToCamera = Matrix4x4.Translate(new Vector3(1.0f, 2.0f, 3.0f)),
                _VividCameraToWorld = Matrix4x4.Translate(new Vector3(-1.0f, -2.0f, -3.0f)),
                _VividGlstateMatrixProjection = Matrix4x4.Perspective(60.0f, 16.0f / 9.0f, 0.3f, 1000.0f),
                _VividViewProjMatrix = Matrix4x4.identity,
                _VividNonJitteredViewProjMatrix = Matrix4x4.identity,
                _VividInvViewProjMatrix = Matrix4x4.identity,
                _VividScreenSize = new Vector4(1920.0f, 1080.0f, 1.0f / 1920.0f, 1.0f / 1080.0f),
                _VividScaledScreenParams = new Vector4(1920.0f, 1080.0f, 1.0f + (1.0f / 1920.0f), 1.0f + (1.0f / 1080.0f)),
            };

            var sceneSnapshot = CreateComparisonSnapshot(CameraType.SceneView, "SceneView", globals);
            var gameSnapshot = CreateComparisonSnapshot(CameraType.Game, "Main Camera", globals);

            var hasDifferences = CameraShaderVariablesGlobalComparisonLogger.TryBuildDifferenceReport(sceneSnapshot, gameSnapshot,
                out var report, out var signature);

            Assert.That(hasDifferences, Is.False);
            Assert.That(report, Is.Null);
            Assert.That(signature, Is.Null);
        }

        [Test]
        public void CameraShaderVariablesGlobalComparisonLogger_ReportsProjectionMismatch_WhenProjectionGlobalsDiffer()
        {
            var sceneGlobals = new ShaderVariablesGlobal
            {
                _VividProjectionParams = new Vector4(-1.0f, 0.3f, 1000.0f, 0.001f),
                _VividGlstateMatrixProjection = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)),
                _VividViewProjMatrix = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)),
                _VividNonJitteredViewProjMatrix = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)),
                _VividInvViewProjMatrix = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)),
                _VividScreenSize = new Vector4(1920.0f, 1080.0f, 1.0f / 1920.0f, 1.0f / 1080.0f),
                _VividScaledScreenParams = new Vector4(1920.0f, 1080.0f, 1.0f + (1.0f / 1920.0f), 1.0f + (1.0f / 1080.0f)),
            };
            var gameGlobals = sceneGlobals;
            gameGlobals._VividProjectionParams = new Vector4(1.0f, 0.3f, 1000.0f, 0.001f);
            gameGlobals._VividGlstateMatrixProjection = Matrix4x4.identity;
            gameGlobals._VividViewProjMatrix = Matrix4x4.identity;
            gameGlobals._VividNonJitteredViewProjMatrix = Matrix4x4.identity;
            gameGlobals._VividInvViewProjMatrix = Matrix4x4.identity;

            var sceneSnapshot = CreateComparisonSnapshot(CameraType.SceneView, "SceneView", sceneGlobals);
            var gameSnapshot = CreateComparisonSnapshot(CameraType.Game, "Main Camera", gameGlobals);

            var hasDifferences = CameraShaderVariablesGlobalComparisonLogger.TryBuildDifferenceReport(sceneSnapshot, gameSnapshot,
                out var report, out var signature);

            Assert.That(hasDifferences, Is.True);
            Assert.That(report, Does.Contain("_VividProjectionParams"));
            Assert.That(report, Does.Contain("_VividGlstateMatrixProjection"));
            Assert.That(report, Does.Contain("Game rawCamera.projectionMatrix"));
            Assert.That(report, Does.Contain("Game effectiveProjectionMatrix"));
            Assert.That(report, Does.Contain("renderIntoTexture=True"));
            Assert.That(report, Does.Contain("renderIntoTexture=False"));
            Assert.That(report, Does.Not.Contain("- pixelRect:"));
            Assert.That(report, Does.Not.Contain("_VividScreenParams:"));
            Assert.That(report, Does.Not.Contain("_VividScreenSize:"));
            Assert.That(report, Does.Not.Contain("_VividScaledScreenParams:"));
            Assert.That(signature, Does.Contain("_VividProjectionParams"));
        }

        [Test]
        public void CameraShaderVariablesGlobalComparisonLogger_DoesNotReport_WhenOnlyViewportMetricsDiffer()
        {
            var sceneGlobals = new ShaderVariablesGlobal
            {
                _VividScreenParams = new Vector4(1565.0f, 727.0f, 1.0f + (1.0f / 1565.0f), 1.0f + (1.0f / 727.0f)),
                _VividScreenSize = new Vector4(1565.0f, 727.0f, 1.0f / 1565.0f, 1.0f / 727.0f),
                _VividScaledScreenParams = new Vector4(1565.0f, 727.0f, 1.0f + (1.0f / 1565.0f), 1.0f + (1.0f / 727.0f)),
            };
            var gameGlobals = new ShaderVariablesGlobal
            {
                _VividScreenParams = new Vector4(1920.0f, 1080.0f, 1.0f + (1.0f / 1920.0f), 1.0f + (1.0f / 1080.0f)),
                _VividScreenSize = new Vector4(1920.0f, 1080.0f, 1.0f / 1920.0f, 1.0f / 1080.0f),
                _VividScaledScreenParams = new Vector4(1920.0f, 1080.0f, 1.0f + (1.0f / 1920.0f), 1.0f + (1.0f / 1080.0f)),
            };

            var sceneSnapshot = CreateComparisonSnapshot(CameraType.SceneView, "SceneView", sceneGlobals);
            sceneSnapshot.pixelRect = new Rect(0.0f, 0.0f, 1565.0f, 727.0f);
            sceneSnapshot.actualWidth = 1565;
            sceneSnapshot.actualHeight = 727;
            sceneSnapshot.pixelWidth = 1565;
            sceneSnapshot.pixelHeight = 727;
            sceneSnapshot.scaledPixelWidth = 1565;
            sceneSnapshot.scaledPixelHeight = 727;
            sceneSnapshot.aspect = 1565.0f / 727.0f;

            var gameSnapshot = CreateComparisonSnapshot(CameraType.Game, "Main Camera", gameGlobals);

            var hasDifferences = CameraShaderVariablesGlobalComparisonLogger.TryBuildDifferenceReport(sceneSnapshot, gameSnapshot,
                out var report, out var signature);

            Assert.That(hasDifferences, Is.False);
            Assert.That(report, Is.Null);
            Assert.That(signature, Is.Null);
        }

        [Test]
        public void UnityInput_RedirectsLegacyAccessors_ToExplicitShaderVariablesGlobalBuffer()
        {
            var unityInputSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "UnityInput.hlsl"));
            var shaderVariablesGlobalSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "ShaderVariablesGlobal.hlsl"));
            var inputSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "Input.hlsl"));

            Assert.That(unityInputSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/ShaderVariablesGlobal.hlsl\""));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("GLOBAL_CBUFFER_START(ShaderVariablesGlobal, b0)"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define unity_MatrixInvVP _VividMatrixInvVP"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define _WorldSpaceCameraPos _VividWorldSpaceCameraPos.xyz"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define _GlobalMipBias _VividGlobalMipBias.xy"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("float4 _VividPlanetCenterRadius;"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("float4 _VividPlanetUpAltitude;"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define _PlanetCenterRadius _VividPlanetCenterRadius"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define _PlanetUpAltitude _VividPlanetUpAltitude"));
            Assert.That(shaderVariablesGlobalSource, Does.Contain("#define _ScaledScreenParams _VividScaledScreenParams"));
            Assert.That(unityInputSource, Does.Contain("#define unity_SHAr _VividSHAr"));
            Assert.That(unityInputSource, Does.Contain("#define unity_SHC _VividSHC"));
            Assert.That(inputSource, Does.Not.Contain("float2 _GlobalMipBias;"));
            Assert.That(inputSource, Does.Not.Contain("float4 _ScaledScreenParams;"));
        }

        [Test]
        public void InitializeContext_UsesRenderToTextureConvention_ForStoredCameraMatrices()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(source, Does.Contain("additionalCameraData.UpdateCameraMatrices(true);"));
            Assert.That(source, Does.Not.Contain("additionalCameraData.UpdateCameraMatrices(camera.targetTexture != null);"));
        }

        [Test]
        public void PrepareFrame_UpdatesFrameContextSystem_BeforePreparingHistoryTargets()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));
            var updateIndex = source.IndexOf("FrameContextSystem.Update(s_FrameData, cmdBuffer);", System.StringComparison.Ordinal);
            var historyIndex = source.IndexOf("PrepareHistoryTargets(graphAsset, cmdBuffer);", System.StringComparison.Ordinal);

            Assert.That(updateIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(historyIndex, Is.GreaterThan(updateIndex));
        }

        [Test]
        public void RecordRenderGraph_PreparesHistoryImports_WithoutRecordingHistoryCopyPasses()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(source, Does.Contain("PreparePendingHistoryTextureImports(renderGraph);"));
            Assert.That(source, Does.Not.Contain("RecordHistoryUpdatePasses(renderGraph, graphAsset);"));
            Assert.That(source, Does.Not.Contain("RecordCodeManagedTextureHistoryUpdatePasses(renderGraph, graphAsset);"));
        }

        private static void AssertMatrixAreEqual(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.00001f));
            }
        }

        private static void AssertVectorAreEqual(Vector4 expected, Vector4 actual, float tolerance = 0.00001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance));
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

        private static CameraShaderVariablesGlobalComparisonLogger.Snapshot CreateComparisonSnapshot(CameraType cameraType,
            string cameraName, ShaderVariablesGlobal globals)
        {
            return new CameraShaderVariablesGlobalComparisonLogger.Snapshot
            {
                cameraName = cameraName,
                cameraType = cameraType,
                captureTime = 1.0f,
                pixelRect = new Rect(0.0f, 0.0f, 1920.0f, 1080.0f),
                actualWidth = 1920,
                actualHeight = 1080,
                pixelWidth = 1920,
                pixelHeight = 1080,
                scaledPixelWidth = 1920,
                scaledPixelHeight = 1080,
                nearClipPlane = 0.3f,
                farClipPlane = 1000.0f,
                fieldOfView = 60.0f,
                aspect = 16.0f / 9.0f,
                orthographic = false,
                orthographicSize = 0.0f,
                hasTargetTexture = false,
                renderIntoTexture = cameraType == CameraType.SceneView,
                hasAdditionalData = cameraType == CameraType.Game,
                rawCameraProjectionMatrix = globals._VividGlstateMatrixProjection,
                rawCameraNonJitteredProjectionMatrix = globals._VividGlstateMatrixProjection,
                effectiveProjectionMatrix = globals._VividGlstateMatrixProjection,
                effectiveNonJitteredProjectionMatrix = globals._VividGlstateMatrixProjection,
                shaderVariablesGlobal = globals,
            };
        }
    }
}
