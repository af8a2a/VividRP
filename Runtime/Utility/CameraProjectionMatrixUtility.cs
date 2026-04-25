using UnityEngine;
using System.Reflection;
using System;

namespace VividRP.Runtime
{
    public static class CameraProjectionMatrixUtility
    {
        private const float MatrixTolerance = 0.0001f;
        private static readonly PropertyInfo s_ProjectionMatrixModeProperty =
            typeof(Camera).GetProperty("projectionMatrixMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal enum CameraProjectionStateMode
        {
            Explicit = 0,
            Implicit = 1,
            PhysicalPropertiesBased = 2
        }

        internal readonly struct CameraProjectionState
        {
            internal CameraProjectionState(
                CameraProjectionStateMode mode,
                Matrix4x4 projectionMatrix,
                Matrix4x4 nonJitteredProjectionMatrix)
            {
                Mode = mode;
                ProjectionMatrix = projectionMatrix;
                NonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            }

            internal CameraProjectionStateMode Mode { get; }
            internal Matrix4x4 ProjectionMatrix { get; }
            internal Matrix4x4 NonJitteredProjectionMatrix { get; }
        }

        public static Matrix4x4 GetProjectionMatrix(Camera camera)
        {
            if (camera == null)
            {
                return Matrix4x4.identity;
            }

            if (ResolveEffectiveProjectionStateMode(camera) == CameraProjectionStateMode.Explicit)
            {
                var projectionMatrix = camera.projectionMatrix;
                if (IsProjectionMatrixUsable(projectionMatrix))
                {
                    return projectionMatrix;
                }

                var nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
                if (IsProjectionMatrixUsable(nonJitteredProjectionMatrix))
                {
                    return nonJitteredProjectionMatrix;
                }
            }

            return BuildProjectionMatrix(camera);
        }

        public static Matrix4x4 GetNonJitteredProjectionMatrix(Camera camera)
        {
            if (camera == null)
            {
                return Matrix4x4.identity;
            }

            if (ResolveEffectiveProjectionStateMode(camera) == CameraProjectionStateMode.Explicit)
            {
                var nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
                if (IsProjectionMatrixUsable(nonJitteredProjectionMatrix))
                {
                    return nonJitteredProjectionMatrix;
                }

                var projectionMatrix = camera.projectionMatrix;
                if (IsProjectionMatrixUsable(projectionMatrix))
                {
                    return projectionMatrix;
                }
            }

            return BuildProjectionMatrix(camera);
        }

        public static bool IsProjectionMatrixUsable(Matrix4x4 matrix)
        {
            return MaxAbsElement(matrix) > MatrixTolerance && MaxAbsDiff(matrix, Matrix4x4.identity) > MatrixTolerance;
        }

        public static void SetProjectionMatrices(Camera camera, Matrix4x4 projectionMatrix, Matrix4x4 nonJitteredProjectionMatrix)
        {
            if (camera == null)
            {
                return;
            }

            camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
            camera.projectionMatrix = projectionMatrix;
        }

        internal static CameraProjectionState CaptureProjectionState(Camera camera)
        {
            if (camera == null)
                return default;

            return new CameraProjectionState(
                ResolveEffectiveProjectionStateMode(camera),
                camera.projectionMatrix,
                camera.nonJitteredProjectionMatrix);
        }

        internal static void RestoreProjectionState(Camera camera, in CameraProjectionState state)
        {
            if (camera == null)
                return;

            switch (state.Mode)
            {
                case CameraProjectionStateMode.Explicit:
                    camera.nonJitteredProjectionMatrix = state.NonJitteredProjectionMatrix;
                    camera.projectionMatrix = state.ProjectionMatrix;
                    break;
                case CameraProjectionStateMode.PhysicalPropertiesBased:
                    RestorePhysicalPropertiesBasedProjection(camera);
                    break;
                case CameraProjectionStateMode.Implicit:
                default:
                    RestoreImplicitProjection(camera);
                    break;
            }
        }

        private static Matrix4x4 BuildProjectionMatrix(Camera camera)
        {
            return BuildProjectionMatrix(camera, ResolveAspect(camera));
        }

        private static void RestoreImplicitProjection(Camera camera)
        {
            if (camera == null)
                return;

            camera.ResetProjectionMatrix();
        }

        private static void RestorePhysicalPropertiesBasedProjection(Camera camera)
        {
            if (camera == null)
                return;

            var currentMode = ResolveProjectionStateMode(camera);
            if (camera.usePhysicalProperties && currentMode == CameraProjectionStateMode.PhysicalPropertiesBased)
                return;

            // Unity clears usePhysicalProperties when ResetProjectionMatrix() is called on a camera
            // that currently has an explicit projection. Re-enable the physical mode immediately after.
            camera.ResetProjectionMatrix();
            if (!camera.usePhysicalProperties)
                camera.usePhysicalProperties = true;

            if (ResolveProjectionStateMode(camera) != CameraProjectionStateMode.PhysicalPropertiesBased)
                TrySetProjectionStateMode(camera, CameraProjectionStateMode.PhysicalPropertiesBased);
        }

        private static Matrix4x4 BuildProjectionMatrix(Camera camera, float aspect)
        {
            var nearClip = Mathf.Max(0.0001f, camera.nearClipPlane);
            var farClip = Mathf.Max(nearClip + 0.0001f, camera.farClipPlane);
            aspect = Mathf.Max(aspect, 0.0001f);

            if (camera.orthographic)
            {
                var halfHeight = Mathf.Max(0.0001f, camera.orthographicSize);
                var halfWidth = halfHeight * aspect;
                return Matrix4x4.Ortho(-halfWidth, halfWidth, -halfHeight, halfHeight, nearClip, farClip);
            }

            if (ResolveProjectionStateMode(camera) == CameraProjectionStateMode.PhysicalPropertiesBased || camera.usePhysicalProperties)
            {
                Matrix4x4 projectionMatrix;
                Camera.CalculateProjectionMatrixFromPhysicalProperties(
                    out projectionMatrix,
                    camera.focalLength,
                    camera.sensorSize,
                    camera.lensShift,
                    nearClip,
                    farClip,
                    new Camera.GateFitParameters(camera.gateFit, aspect));
                return projectionMatrix;
            }

            var fieldOfView = Mathf.Clamp(camera.fieldOfView, 0.0001f, 179.0f);
            return Matrix4x4.Perspective(fieldOfView, aspect, nearClip, farClip);
        }

        private static CameraProjectionStateMode ResolveProjectionStateMode(Camera camera)
        {
            if (camera == null)
                return CameraProjectionStateMode.Implicit;

            if (s_ProjectionMatrixModeProperty?.GetValue(camera) is System.Enum modeEnum
                && System.Enum.TryParse(modeEnum.ToString(), out CameraProjectionStateMode mode))
            {
                return mode;
            }

            return camera.usePhysicalProperties
                ? CameraProjectionStateMode.PhysicalPropertiesBased
                : CameraProjectionStateMode.Implicit;
        }

        private static bool TrySetProjectionStateMode(Camera camera, CameraProjectionStateMode mode)
        {
            if (camera == null || s_ProjectionMatrixModeProperty == null || !s_ProjectionMatrixModeProperty.CanWrite)
                return false;

            try
            {
                var projectionModeValue = Enum.Parse(s_ProjectionMatrixModeProperty.PropertyType, mode.ToString(), ignoreCase: false);
                s_ProjectionMatrixModeProperty.SetValue(camera, projectionModeValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CameraProjectionStateMode ResolveEffectiveProjectionStateMode(Camera camera)
        {
            var mode = ResolveProjectionStateMode(camera);
            if (mode != CameraProjectionStateMode.Explicit || camera == null)
                return mode;

            if (HasJitteredProjectionPair(camera))
                return mode;

            if (TryResolveParameterDrivenProjectionMode(camera, camera.nonJitteredProjectionMatrix, out var resolvedMode)
                || TryResolveParameterDrivenProjectionMode(camera, camera.projectionMatrix, out resolvedMode))
                return resolvedMode;

            return mode;
        }

        private static bool HasJitteredProjectionPair(Camera camera)
        {
            if (camera == null)
                return false;

            var projectionMatrix = camera.projectionMatrix;
            var nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
            if (!IsProjectionMatrixUsable(projectionMatrix)
                || !IsProjectionMatrixUsable(nonJitteredProjectionMatrix)
                || MaxAbsDiff(projectionMatrix, nonJitteredProjectionMatrix) <= MatrixTolerance)
            {
                return false;
            }

            return IsJitterMatrix(projectionMatrix * nonJitteredProjectionMatrix.inverse);
        }

        private static bool IsJitterMatrix(Matrix4x4 matrix)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if ((row == 0 || row == 1) && column == 3)
                        continue;

                    if (Mathf.Abs(matrix[row, column] - Matrix4x4.identity[row, column]) > MatrixTolerance)
                        return false;
                }
            }

            return Mathf.Abs(matrix.m03) > MatrixTolerance || Mathf.Abs(matrix.m13) > MatrixTolerance;
        }

        private static bool TryResolveParameterDrivenProjectionMode(
            Camera camera,
            Matrix4x4 projectionMatrix,
            out CameraProjectionStateMode mode)
        {
            mode = CameraProjectionStateMode.Explicit;
            if (camera == null || !IsProjectionMatrixUsable(projectionMatrix))
                return false;

            var currentAspect = ResolveAspect(camera);
            if (ProjectionMatchesCameraParameters(camera, projectionMatrix, currentAspect))
            {
                mode = camera.usePhysicalProperties
                    ? CameraProjectionStateMode.PhysicalPropertiesBased
                    : CameraProjectionStateMode.Implicit;
                return true;
            }

            if (!TryGetProjectionMatrixAspect(projectionMatrix, out var inferredAspect)
                || Mathf.Abs(inferredAspect - currentAspect) <= MatrixTolerance)
            {
                return false;
            }

            if (!ProjectionMatchesCameraParameters(camera, projectionMatrix, inferredAspect))
                return false;

            mode = camera.usePhysicalProperties
                ? CameraProjectionStateMode.PhysicalPropertiesBased
                : CameraProjectionStateMode.Implicit;
            return true;
        }

        private static bool ProjectionMatchesCameraParameters(
            Camera camera,
            Matrix4x4 projectionMatrix,
            float aspect)
        {
            var expectedProjection = BuildProjectionMatrix(camera, aspect);
            return MaxAbsDiff(projectionMatrix, expectedProjection) <= MatrixTolerance;
        }

        private static bool TryGetProjectionMatrixAspect(Matrix4x4 projectionMatrix, out float aspect)
        {
            aspect = 0.0f;

            var m00 = projectionMatrix.m00;
            var m11 = projectionMatrix.m11;
            if (Mathf.Abs(m00) <= MatrixTolerance || Mathf.Abs(m11) <= MatrixTolerance)
                return false;

            aspect = Mathf.Abs(m11 / m00);
            return float.IsFinite(aspect) && aspect > MatrixTolerance;
        }

        private static float ResolveAspect(Camera camera)
        {
            if (camera != null && camera.aspect > 0.0f)
            {
                return camera.aspect;
            }

            var width = camera != null && camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width;
            var height = camera != null && camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height;
            return Mathf.Max(width / (float)Mathf.Max(1, height), 0.0001f);
        }

        private static float MaxAbsElement(Matrix4x4 matrix)
        {
            var maxElement = 0.0f;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    maxElement = Mathf.Max(maxElement, Mathf.Abs(matrix[row, column]));
                }
            }

            return maxElement;
        }

        private static float MaxAbsDiff(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            var maxDiff = 0.0f;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    maxDiff = Mathf.Max(maxDiff, Mathf.Abs(lhs[row, column] - rhs[row, column]));
                }
            }

            return maxDiff;
        }
    }
}
