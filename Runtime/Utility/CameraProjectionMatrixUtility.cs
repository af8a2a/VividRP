using UnityEngine;

namespace VividRP.Runtime
{
    public static class CameraProjectionMatrixUtility
    {
        private const float MatrixTolerance = 0.0001f;

        public static Matrix4x4 GetProjectionMatrix(Camera camera)
        {
            if (camera == null)
            {
                return Matrix4x4.identity;
            }

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

            return BuildProjectionMatrix(camera);
        }

        public static Matrix4x4 GetNonJitteredProjectionMatrix(Camera camera)
        {
            if (camera == null)
            {
                return Matrix4x4.identity;
            }

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

            var fieldOfView = Mathf.Clamp(camera.fieldOfView, 0.0001f, 179.0f);
            return Matrix4x4.Perspective(fieldOfView, aspect, nearClip, farClip);
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
