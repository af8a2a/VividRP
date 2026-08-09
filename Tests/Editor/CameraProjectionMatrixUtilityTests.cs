using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class CameraProjectionMatrixUtilityTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            m_GameObject = new GameObject("CameraProjectionMatrixUtilityTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (m_GameObject != null)
                UnityEngine.Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void RestoreProjectionState_ResetsImplicitProjection_AndAllowsFieldOfViewChanges()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 60.0f;

            var originalState = camera.CaptureProjectionState();
            var originalProjection = camera.GetNonJitteredProjectionMatrix();
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.015625f, -0.03125f, 0.0f));
            camera.SetProjectionMatrices(jitterMatrix * originalProjection, originalProjection);

            camera.fieldOfView = 45.0f;
            camera.RestoreProjectionState(originalState);

            var restoredProjection = camera.GetNonJitteredProjectionMatrix();
            var expectedProjection = Matrix4x4.Perspective(45.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);

            Assert.That(originalState.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
            Assert.That(MaxAbsDiff(restoredProjection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(restoredProjection, originalProjection), Is.GreaterThan(0.01f));
        }

        [Test]
        public void RestoreProjectionState_RestoresExplicitProjectionMatrices()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var explicitProjection = Matrix4x4.Frustum(-0.2f, 0.3f, -0.15f, 0.25f, 0.5f, 100.0f);

            camera.nonJitteredProjectionMatrix = explicitProjection;
            camera.projectionMatrix = explicitProjection;

            var originalState = camera.CaptureProjectionState();
            var temporaryProjection = Matrix4x4.Perspective(70.0f, 1.5f, 0.1f, 50.0f);
            camera.SetProjectionMatrices(temporaryProjection, temporaryProjection);

            camera.RestoreProjectionState(originalState);

            Assert.That(originalState.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Explicit));
            Assert.That(MaxAbsDiff(camera.projectionMatrix, explicitProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(camera.nonJitteredProjectionMatrix, explicitProjection), Is.LessThan(0.0001f));
        }

        [Test]
        public void CaptureProjectionState_TreatsParameterDrivenExplicitProjection_AsImplicit()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 50.0f;

            var expectedProjection = Matrix4x4.Perspective(50.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            camera.nonJitteredProjectionMatrix = expectedProjection;
            camera.projectionMatrix = expectedProjection;

            var state = camera.CaptureProjectionState();

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
        }

        [Test]
        public void CaptureProjectionState_TreatsParameterDrivenExplicitProjectionWithStaleFarClip_AsImplicit()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 20.0f;
            camera.fieldOfView = 50.0f;

            var staleProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            camera.SetProjectionMatrices(staleProjection, staleProjection);

            camera.farClipPlane = 1000.0f;

            var state = camera.CaptureProjectionState();
            var projection = camera.GetNonJitteredProjectionMatrix();
            var expectedProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
            Assert.That(MaxAbsDiff(projection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(projection, staleProjection), Is.GreaterThan(0.001f));
        }

        [Test]
        public void CaptureProjectionState_TreatsParameterDrivenExplicitProjectionWithStaleNearClip_AsImplicit()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            camera.fieldOfView = 50.0f;

            var staleProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            camera.SetProjectionMatrices(staleProjection, staleProjection);

            camera.nearClipPlane = 1.0f;

            var state = camera.CaptureProjectionState();
            var projection = camera.GetNonJitteredProjectionMatrix();
            var expectedProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
            Assert.That(MaxAbsDiff(projection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(projection, staleProjection), Is.GreaterThan(0.001f));
        }

        [Test]
        public void ApplyJitter_NonTemporalModeRestoresProjectionAfterFarClipChange()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            var additionalData = m_GameObject.AddComponent<VividAdditionalCameraData>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 20.0f;
            camera.fieldOfView = 50.0f;
            additionalData.antialiasing = VividAntialiasingMode.CMAA2;

            var staleProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            camera.SetProjectionMatrices(staleProjection, staleProjection);

            camera.farClipPlane = 1000.0f;
            var antialiasingData = new VividAntialiasingData
            {
                effectiveMode = VividAntialiasingMode.CMAA2,
            };

            VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalData, antialiasingData, 0);

            var state = camera.CaptureProjectionState();
            var projection = camera.GetNonJitteredProjectionMatrix();
            var expectedProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
            Assert.That(MaxAbsDiff(projection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(projection, staleProjection), Is.GreaterThan(0.001f));
        }

        [Test]
        public void GetProjectionMatrix_PreservesJitteredExplicitProjectionPair()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 50.0f;

            var nonJitteredProjection = Matrix4x4.Perspective(
                camera.fieldOfView,
                camera.aspect,
                camera.nearClipPlane,
                camera.farClipPlane);
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = 0.015625f;
            jitterMatrix.m13 = -0.03125f;
            var jitteredProjection = jitterMatrix * nonJitteredProjection;

            camera.SetProjectionMatrices(jitteredProjection, nonJitteredProjection);

            var state = camera.CaptureProjectionState();
            var projection = camera.GetProjectionMatrix();
            var nonJittered = camera.GetNonJitteredProjectionMatrix();

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Explicit));
            Assert.That(MaxAbsDiff(projection, jitteredProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(nonJittered, nonJitteredProjection), Is.LessThan(0.0001f));
        }

        [Test]
        public void RestoreProjectionState_ResetsImplicitProjection_WhenExplicitMatrixWasCapturedFromDifferentAspect()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 50.0f;
            camera.aspect = 16.0f / 9.0f;

            var sceneAspectProjection = Matrix4x4.Perspective(50.0f, 1.0f, camera.nearClipPlane, camera.farClipPlane);
            camera.nonJitteredProjectionMatrix = sceneAspectProjection;
            camera.projectionMatrix = sceneAspectProjection;

            var state = camera.CaptureProjectionState();
            camera.RestoreProjectionState(state);

            var restoredProjection = camera.GetNonJitteredProjectionMatrix();
            var expectedProjection = Matrix4x4.Perspective(50.0f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
            Assert.That(MaxAbsDiff(restoredProjection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(restoredProjection, sceneAspectProjection), Is.GreaterThan(0.01f));
        }

        [Test]
        public void RestoreProjectionState_ResetsPhysicalProjection_WhenExplicitMatrixWasCapturedFromDifferentAspect()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.usePhysicalProperties = true;
            camera.focalLength = 50.0f;
            camera.sensorSize = new Vector2(36.0f, 24.0f);
            camera.lensShift = new Vector2(0.1f, -0.05f);
            camera.gateFit = Camera.GateFitMode.Horizontal;
            camera.aspect = 16.0f / 9.0f;

            Camera.CalculateProjectionMatrixFromPhysicalProperties(
                out var sceneAspectProjection,
                camera.focalLength,
                camera.sensorSize,
                camera.lensShift,
                camera.nearClipPlane,
                camera.farClipPlane,
                new Camera.GateFitParameters(camera.gateFit, 1.0f));

            Camera.CalculateProjectionMatrixFromPhysicalProperties(
                out var expectedProjection,
                camera.focalLength,
                camera.sensorSize,
                camera.lensShift,
                camera.nearClipPlane,
                camera.farClipPlane,
                new Camera.GateFitParameters(camera.gateFit, camera.aspect));

            camera.nonJitteredProjectionMatrix = sceneAspectProjection;
            camera.projectionMatrix = sceneAspectProjection;

            var state = camera.CaptureProjectionState();
            camera.RestoreProjectionState(state);

            var restoredProjection = camera.GetNonJitteredProjectionMatrix();

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.PhysicalPropertiesBased));
            Assert.That(MaxAbsDiff(restoredProjection, expectedProjection), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(restoredProjection, sceneAspectProjection), Is.GreaterThan(0.01f));
        }

        [Test]
        public void RestoreProjectionState_KeepsUsePhysicalProperties_WhenCapturedModeIsPhysicalPropertiesBased()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.usePhysicalProperties = true;
            camera.focalLength = 50.0f;
            camera.sensorSize = new Vector2(36.0f, 24.0f);
            camera.gateFit = Camera.GateFitMode.Horizontal;
            camera.aspect = 16.0f / 9.0f;

            Camera.CalculateProjectionMatrixFromPhysicalProperties(
                out var expectedProjection,
                camera.focalLength,
                camera.sensorSize,
                camera.lensShift,
                camera.nearClipPlane,
                camera.farClipPlane,
                new Camera.GateFitParameters(camera.gateFit, camera.aspect));

            var originalState = camera.CaptureProjectionState();
            var temporaryProjection = Matrix4x4.Perspective(70.0f, camera.aspect, 0.1f, 50.0f);
            camera.SetProjectionMatrices(temporaryProjection, temporaryProjection);

            camera.RestoreProjectionState(originalState);

            var restoredState = camera.CaptureProjectionState();
            var restoredProjection = camera.GetNonJitteredProjectionMatrix();

            Assert.That(originalState.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.PhysicalPropertiesBased));
            Assert.That(camera.usePhysicalProperties, Is.True);
            Assert.That(restoredState.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.PhysicalPropertiesBased));
            Assert.That(MaxAbsDiff(restoredProjection, expectedProjection), Is.LessThan(0.0001f));
        }

        [Test]
        public void GetProjectionMatrices_DoNotAllocate_ForParameterDrivenCamera()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 60.0f;

            camera.GetProjectionMatrix();
            camera.GetNonJitteredProjectionMatrix();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
            {
                camera.GetProjectionMatrix();
                camera.GetNonJitteredProjectionMatrix();
            }

            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void RestoreProjectionState_DoesNotAllocate_WhenImplicitProjectionIsAlreadyRestored()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 60.0f;

            var state = camera.CaptureProjectionState();
            camera.RestoreProjectionState(state);

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                camera.RestoreProjectionState(state);

            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }

        private static float MaxAbsDiff(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            var maxDiff = 0.0f;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                    maxDiff = Mathf.Max(maxDiff, Mathf.Abs(lhs[row, column] - rhs[row, column]));
            }

            return maxDiff;
        }
    }
}
