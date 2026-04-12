using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
        public Matrix4x4 GetPixelCoordToViewDirWSMatrix()
        {
            var gpuProj = GetGPUProjectionMatrix();
            var gpuProjAspect = CoreUtils.ProjectionMatrixAspect(gpuProj);

            var screenSize = new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight,
                1.0f / camera.scaledPixelWidth, 1.0f / camera.scaledPixelHeight);

            return CoreUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, camera.worldToCameraMatrix,
                gpuProj,
                screenSize, gpuProjAspect);
        }
    }
}
