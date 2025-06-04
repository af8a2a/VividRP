using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public static class UniversalCameraDataExtension
    {
        public static Matrix4x4 GetPixelCoordToViewDirWSMatrix(this UniversalCameraData cameraData)
        {
            var camera = cameraData.camera;
            var gpuProj = cameraData.GetGPUProjectionMatrix(true);
            var gpuProjAspect = RenderingUtilsExt.ProjectionMatrixAspect(gpuProj);

            var screenSize = new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight, 1.0f / camera.scaledPixelWidth, 1.0f / camera.scaledPixelHeight);

            return RenderingUtilsExt.ComputePixelCoordToWorldSpaceViewDirectionMatrix(cameraData.camera, cameraData.camera.worldToCameraMatrix, gpuProj,
                screenSize, gpuProjAspect);
        }
    }
}