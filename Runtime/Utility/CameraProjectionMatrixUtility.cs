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
        private static readonly Func<Camera, int> s_ProjectionMatrixModeGetter = CreateProjectionMatrixModeGetter();
        private static readonly object[] s_ProjectionStateModeValues = CreateProjectionStateModeValues();

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

        internal static void RestoreNoJitterProjection(Camera camera, Matrix4x4 nonJitteredProjectionMatrix)
        {
            if (camera == null)
                return;

            if (TryResolveParameterDrivenProjectionMode(camera, nonJitteredProjectionMatrix, out var mode))
            {
                RestoreParameterDrivenProjection(camera, mode);
                return;
            }

            SetProjectionMatrices(camera, nonJitteredProjectionMatrix, nonJitteredProjectionMatrix);
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
                    if (IsParameterDrivenProjectionAlreadyRestored(camera, CameraProjectionStateMode.PhysicalPropertiesBased))
                        return;

                    RestoreParameterDrivenProjection(camera, CameraProjectionStateMode.PhysicalPropertiesBased);
                    break;
                case CameraProjectionStateMode.Implicit:
                default:
                    if (IsParameterDrivenProjectionAlreadyRestored(camera, CameraProjectionStateMode.Implicit))
                        return;

                    RestoreParameterDrivenProjection(camera, CameraProjectionStateMode.Implicit);
                    break;
            }
        }

        private static void RestoreParameterDrivenProjection(Camera camera, CameraProjectionStateMode mode)
        {
            if (mode == CameraProjectionStateMode.PhysicalPropertiesBased)
            {
                RestorePhysicalPropertiesBasedProjection(camera);
                return;
            }

            RestoreImplicitProjection(camera);
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

        private static bool IsParameterDrivenProjectionAlreadyRestored(Camera camera, CameraProjectionStateMode mode)
        {
            if (camera == null)
                return true;

            if (!TryGetProjectionStateMode(camera, out var currentMode) || currentMode != mode)
                return false;

            var expectedProjection = BuildProjectionMatrix(camera);
            return MaxAbsDiff(camera.projectionMatrix, expectedProjection) <= MatrixTolerance
                && MaxAbsDiff(camera.nonJitteredProjectionMatrix, expectedProjection) <= MatrixTolerance;
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

            if (camera.usePhysicalProperties)
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

            if (TryGetProjectionStateMode(camera, out var mode))
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
                var projectionModeValue = GetProjectionStateModeValue(mode);
                if (projectionModeValue == null)
                    return false;

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
            if (camera == null)
                return CameraProjectionStateMode.Implicit;

            var nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
            var projectionMatrix = camera.projectionMatrix;

            if (HasJitteredProjectionPair(projectionMatrix, nonJitteredProjectionMatrix))
                return CameraProjectionStateMode.Explicit;

            if (TryResolveParameterDrivenProjectionMode(camera, nonJitteredProjectionMatrix, out var resolvedMode)
                || TryResolveParameterDrivenProjectionMode(camera, projectionMatrix, out resolvedMode))
                return resolvedMode;

            return CameraProjectionStateMode.Explicit;
        }

        private static bool HasJitteredProjectionPair(Matrix4x4 projectionMatrix, Matrix4x4 nonJitteredProjectionMatrix)
        {
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

        private static bool TryGetProjectionStateMode(Camera camera, out CameraProjectionStateMode mode)
        {
            mode = default;

            if (s_ProjectionMatrixModeGetter != null)
            {
                var rawMode = s_ProjectionMatrixModeGetter(camera);
                if (rawMode >= (int)CameraProjectionStateMode.Explicit
                    && rawMode <= (int)CameraProjectionStateMode.PhysicalPropertiesBased)
                {
                    mode = (CameraProjectionStateMode)rawMode;
                    return true;
                }
            }

            if (s_ProjectionMatrixModeProperty == null)
                return false;

            try
            {
                var projectionModeValue = s_ProjectionMatrixModeProperty.GetValue(camera);
                if (projectionModeValue == null)
                    return false;

                for (var modeIndex = 0; modeIndex < s_ProjectionStateModeValues.Length; modeIndex++)
                {
                    var cachedModeValue = s_ProjectionStateModeValues[modeIndex];
                    if (cachedModeValue == null || !cachedModeValue.Equals(projectionModeValue))
                        continue;

                    mode = (CameraProjectionStateMode)modeIndex;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static Func<Camera, int> CreateProjectionMatrixModeGetter()
        {
            var getter = s_ProjectionMatrixModeProperty?.GetGetMethod(true);
            if (getter == null)
                return null;

            try
            {
                return (Func<Camera, int>)Delegate.CreateDelegate(typeof(Func<Camera, int>), getter);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (MemberAccessException)
            {
                return null;
            }
        }

        private static object GetProjectionStateModeValue(CameraProjectionStateMode mode)
        {
            var modeIndex = (int)mode;
            if (modeIndex < 0 || modeIndex >= s_ProjectionStateModeValues.Length)
                return null;

            return s_ProjectionStateModeValues[modeIndex];
        }

        private static object[] CreateProjectionStateModeValues()
        {
            if (s_ProjectionMatrixModeProperty == null || !s_ProjectionMatrixModeProperty.PropertyType.IsEnum)
                return Array.Empty<object>();

            var values = new object[3];
            values[(int)CameraProjectionStateMode.Explicit] = TryCreateProjectionStateModeValue(nameof(CameraProjectionStateMode.Explicit));
            values[(int)CameraProjectionStateMode.Implicit] = TryCreateProjectionStateModeValue(nameof(CameraProjectionStateMode.Implicit));
            values[(int)CameraProjectionStateMode.PhysicalPropertiesBased] = TryCreateProjectionStateModeValue(nameof(CameraProjectionStateMode.PhysicalPropertiesBased));
            return values;
        }

        private static object TryCreateProjectionStateModeValue(string name)
        {
            try
            {
                return Enum.Parse(s_ProjectionMatrixModeProperty.PropertyType, name, ignoreCase: false);
            }
            catch
            {
                return null;
            }
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
            if (ProjectionMatchesCameraParameters(camera, projectionMatrix, currentAspect)
                || ProjectionMatchesCameraParametersIgnoringDepthMapping(camera, projectionMatrix, currentAspect))
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

            if (!ProjectionMatchesCameraParameters(camera, projectionMatrix, inferredAspect)
                && !ProjectionMatchesCameraParametersIgnoringDepthMapping(camera, projectionMatrix, inferredAspect))
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

        private static bool ProjectionMatchesCameraParametersIgnoringDepthMapping(
            Camera camera,
            Matrix4x4 projectionMatrix,
            float aspect)
        {
            var expectedProjection = BuildProjectionMatrix(camera, aspect);
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if (row == 2 && (column == 2 || column == 3))
                        continue;

                    if (Mathf.Abs(projectionMatrix[row, column] - expectedProjection[row, column]) > MatrixTolerance)
                        return false;
                }
            }

            return true;
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
