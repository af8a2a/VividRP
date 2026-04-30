using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
        public Matrix4x4 GetPixelCoordToViewDirWSMatrix()
        {
            if (camera == null)
                return Matrix4x4.identity;

            var gpuProj = GetGPUProjectionMatrix();
            var gpuProjAspect = CoreUtils.ProjectionMatrixAspect(gpuProj);
            var width = ResolveViewDirectionDimension(actualWidth, camera.scaledPixelWidth, pixelWidth, Screen.width);
            var height = ResolveViewDirectionDimension(actualHeight, camera.scaledPixelHeight, pixelHeight, Screen.height);

            var screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);

            return CoreUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, camera.worldToCameraMatrix,
                gpuProj,
                screenSize, gpuProjAspect);
        }

        private static int ResolveViewDirectionDimension(
            int actualDimension,
            int scaledDimension,
            int pixelDimension,
            int screenDimension)
        {
            if (actualDimension > 0)
                return actualDimension;

            if (scaledDimension > 0)
                return scaledDimension;

            if (pixelDimension > 0)
                return pixelDimension;

            return Mathf.Max(1, screenDimension);
        }
    }
}
