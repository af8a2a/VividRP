using UnityEngine;
using System.Reflection;

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
                case CameraProjectionStateMode.Implicit:
                default:
                    camera.ResetProjectionMatrix();
                    break;
            }
        }

        private static Matrix4x4 BuildProjectionMatrix(Camera camera)
        {
            var nearClip = Mathf.Max(0.0001f, camera.nearClipPlane);
            var farClip = Mathf.Max(nearClip + 0.0001f, camera.farClipPlane);
            var aspect = ResolveAspect(camera);

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

        private static CameraProjectionStateMode ResolveEffectiveProjectionStateMode(Camera camera)
        {
            var mode = ResolveProjectionStateMode(camera);
            if (mode != CameraProjectionStateMode.Explicit || camera == null)
                return mode;

            var expectedProjection = BuildProjectionMatrix(camera);
            if (IsProjectionMatrixUsable(camera.nonJitteredProjectionMatrix)
                && MaxAbsDiff(camera.nonJitteredProjectionMatrix, expectedProjection) <= 0.0001f)
            {
                return camera.usePhysicalProperties
                    ? CameraProjectionStateMode.PhysicalPropertiesBased
                    : CameraProjectionStateMode.Implicit;
            }

            if (IsProjectionMatrixUsable(camera.projectionMatrix)
                && MaxAbsDiff(camera.projectionMatrix, expectedProjection) <= 0.0001f)
            {
                return camera.usePhysicalProperties
                    ? CameraProjectionStateMode.PhysicalPropertiesBased
                    : CameraProjectionStateMode.Implicit;
            }

            return mode;
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
