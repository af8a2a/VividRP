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
                Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void RestoreProjectionState_ResetsImplicitProjection_AndAllowsFieldOfViewChanges()
        {
            var camera = m_GameObject.AddComponent<Camera>();
            camera.aspect = 16.0f / 9.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250.0f;
            camera.fieldOfView = 60.0f;

            var originalState = CameraProjectionMatrixUtility.CaptureProjectionState(camera);
            var originalProjection = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);
            var jitterMatrix = Matrix4x4.Translate(new Vector3(0.015625f, -0.03125f, 0.0f));
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * originalProjection, originalProjection);

            camera.fieldOfView = 45.0f;
            CameraProjectionMatrixUtility.RestoreProjectionState(camera, originalState);

            var restoredProjection = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);
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

            var originalState = CameraProjectionMatrixUtility.CaptureProjectionState(camera);
            var temporaryProjection = Matrix4x4.Perspective(70.0f, 1.5f, 0.1f, 50.0f);
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, temporaryProjection, temporaryProjection);

            CameraProjectionMatrixUtility.RestoreProjectionState(camera, originalState);

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

            var state = CameraProjectionMatrixUtility.CaptureProjectionState(camera);

            Assert.That(state.Mode, Is.EqualTo(CameraProjectionMatrixUtility.CameraProjectionStateMode.Implicit));
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
