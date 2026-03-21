using System;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    internal static class VividGPUDrivenCullingContextUtility
    {
        public const int DefaultForcedMeshLODNodeDepth = -1;
        public const float DefaultMeshLODErrorThreshold = 50.0f;

        public static void Build(
            Camera camera,
            VividInstancePassMask passMask,
            out VividGPUCullingContext cullingContext,
            out VividGPULODSelectionContext lodSelectionContext
        )
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 gpuViewProjectionMatrix = gpuProjectionMatrix * viewMatrix;
            Matrix4x4 cullingViewProjectionMatrix = camera.projectionMatrix * viewMatrix;
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cullingViewProjectionMatrix);

            Transform cameraTransform = camera.transform;
            Vector3 cameraPosition = cameraTransform.position;

            cullingContext = new VividGPUCullingContext
            {
                ViewProjectionMatrix = ToFloat4x4(gpuViewProjectionMatrix),
                ViewMatrix = ToFloat4x4(viewMatrix),
                CameraPosition = new float4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f),
                CullingSphereLS = float4.zero,
                PassMask = (int) passMask,
                CameraIsPerspective = camera.orthographic ? 0 : 1,
                BaseStartInstance = 0,
                MeshletListBuildJobsOffset = 0,
                MeshletRenderRequestsOffset = 0,
            };
            FillFrustumPlanes(ref cullingContext, frustumPlanes);

            lodSelectionContext = new VividGPULODSelectionContext
            {
                ViewProjectionMatrix = ToFloat4x4(gpuViewProjectionMatrix),
                CameraPosition = new float4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f),
                CameraUp = ToFloat4(cameraTransform.up, 0.0f),
                CameraRight = ToFloat4(cameraTransform.right, 0.0f),
                ScreenSizePixels = new float2(
                    Mathf.Max(1.0f, GetCameraPixelWidth(camera)),
                    Mathf.Max(1.0f, GetCameraPixelHeight(camera))
                ),
            };
        }

        internal static unsafe Vector4 GetFrustumPlane(in VividGPUCullingContext cullingContext, int planeIndex)
        {
            if (planeIndex < 0 || planeIndex >= 6)
            {
                throw new ArgumentOutOfRangeException(nameof(planeIndex));
            }

            fixed (float* frustumPlanes = cullingContext.FrustumPlanes)
            {
                int offset = planeIndex * 4;
                return new Vector4(
                    frustumPlanes[offset + 0],
                    frustumPlanes[offset + 1],
                    frustumPlanes[offset + 2],
                    frustumPlanes[offset + 3]
                );
            }
        }

        private static unsafe void FillFrustumPlanes(ref VividGPUCullingContext cullingContext, Plane[] frustumPlanes)
        {
            fixed (float* destination = cullingContext.FrustumPlanes)
            {
                int planeCount = Mathf.Min(frustumPlanes.Length, 6);
                for (int planeIndex = 0; planeIndex < planeCount; planeIndex++)
                {
                    Plane plane = frustumPlanes[planeIndex];
                    int offset = planeIndex * 4;
                    destination[offset + 0] = plane.normal.x;
                    destination[offset + 1] = plane.normal.y;
                    destination[offset + 2] = plane.normal.z;
                    destination[offset + 3] = plane.distance;
                }

                for (int planeIndex = planeCount; planeIndex < 6; planeIndex++)
                {
                    int offset = planeIndex * 4;
                    destination[offset + 0] = 0.0f;
                    destination[offset + 1] = 0.0f;
                    destination[offset + 2] = 0.0f;
                    destination[offset + 3] = 0.0f;
                }
            }
        }

        private static float GetCameraPixelWidth(Camera camera)
        {
            if (camera.scaledPixelWidth > 0)
            {
                return camera.scaledPixelWidth;
            }

            if (camera.pixelWidth > 0)
            {
                return camera.pixelWidth;
            }

            return camera.pixelRect.width;
        }

        private static float GetCameraPixelHeight(Camera camera)
        {
            if (camera.scaledPixelHeight > 0)
            {
                return camera.scaledPixelHeight;
            }

            if (camera.pixelHeight > 0)
            {
                return camera.pixelHeight;
            }

            return camera.pixelRect.height;
        }

        private static float4 ToFloat4(Vector3 value, float w)
        {
            return new float4(value.x, value.y, value.z, w);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33)
            );
        }
    }
}
