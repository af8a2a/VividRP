using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividAdditionalCameraData
    {
        /// <summary>
        /// Returns the camera projection matrix. Might be jittered for temporal features.
        /// </summary>
        /// <param name="viewIndex"> View index in case of stereo rendering. By default <c>viewIndex</c> is set to 0. </param>
        /// <returns> The camera projection matrix. </returns>
        public Matrix4x4 GetProjectionMatrix(int viewIndex = 0)
        {
            return m_JitterMatrix * m_ProjectionMatrix;
        }

        internal Matrix4x4 GetGPUProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), renderIntoTexture);
        }

        /// <summary>
        /// Compute the matrix from screen space (pixel) to world space direction (RHS).
        ///
        /// You can use this matrix on the GPU to compute the direction to look in a cubemap for a specific
        /// screen pixel.
        /// </summary>
        /// <param name="viewConstants"></param>
        /// <param name="resolution">The target texture resolution.</param>
        /// <param name="aspect">
        /// The aspect ratio to use.
        ///
        /// if negative, then the aspect ratio of <paramref name="resolution"/> will be used.
        ///
        /// It is different from the aspect ratio of <paramref name="resolution"/> for anamorphic projections.
        /// </param>
        /// <param name="cameraRelativeRendering">If non-zero, then assume Camera Relative Rendering is enabled.</param>
        /// <returns></returns>
        internal Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector4 resolution, float aspect = -1,
            int cameraRelativeRendering = 1)
        {
            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / camera.gp.projMatrix[1, 1]) * 2;
            }

            Vector2 lensShift = camera.GetGateFittedLensShift();

            return CoreUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution,
                viewConstants.viewMatrix, false, aspect, camera.orthographic);
        }

    }
}
