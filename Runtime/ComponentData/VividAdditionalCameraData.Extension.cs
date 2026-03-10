using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividAdditionalCameraData
    {
        public Matrix4x4 viewMatrix => GetViewMatrix();
        public Matrix4x4 inverseViewMatrix => GetInverseViewMatrix();
        public Matrix4x4 projectionMatrix => GetProjectionMatrix();
        public Matrix4x4 nonJitteredProjectionMatrix => GetProjectionMatrixNoJitter();
        public Matrix4x4 gpuProjectionMatrix => GetGPUProjectionMatrix();
        public Matrix4x4 gpuProjectionMatrixNoJitter => GetGPUProjectionMatrixNoJitter();
        public Matrix4x4 jitterMatrix => GetJitterMatrix();
        public Vector2 jitter => m_HasStoredMatrixData ? m_Jitter : GetCameraJitter();
        public Matrix4x4 viewProjectionMatrix => GetViewProjectionMatrix();
        public Matrix4x4 gpuViewProjectionMatrix => GetGPUViewProjectionMatrix();

        public Matrix4x4 GetViewMatrix(int viewIndex = 0)
        {
            return m_HasStoredMatrixData ? m_ViewMatrix : GetCameraViewMatrix();
        }

        public Matrix4x4 GetInverseViewMatrix(int viewIndex = 0)
        {
            return GetViewMatrix(viewIndex).inverse;
        }

        public Matrix4x4 GetProjectionMatrixNoJitter(int viewIndex = 0)
        {
            return m_HasStoredMatrixData ? m_ProjectionMatrix : GetCameraProjectionMatrix();
        }

        public Matrix4x4 GetProjectionMatrix(int viewIndex = 0)
        {
            return GetJitterMatrix(viewIndex) * GetProjectionMatrixNoJitter(viewIndex);
        }

        public Matrix4x4 GetInverseProjectionMatrix(int viewIndex = 0)
        {
            return GetProjectionMatrix(viewIndex).inverse;
        }

        public Matrix4x4 GetJitterMatrix(int viewIndex = 0)
        {
            return m_HasStoredMatrixData ? m_JitterMatrix : GetCameraJitterMatrix();
        }

        public Matrix4x4 GetViewProjectionMatrix(int viewIndex = 0)
        {
            return GetProjectionMatrix(viewIndex) * GetViewMatrix(viewIndex);
        }

        public Matrix4x4 GetGPUProjectionMatrix(int viewIndex = 0)
        {
            return GetGPUProjectionMatrix(GetRenderIntoTexture(), viewIndex);
        }

        public Matrix4x4 GetGPUProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), renderIntoTexture);
        }

        public Matrix4x4 GetGPUProjectionMatrixNoJitter(int viewIndex = 0)
        {
            return GetGPUProjectionMatrixNoJitter(GetRenderIntoTexture(), viewIndex);
        }

        public Matrix4x4 GetGPUProjectionMatrixNoJitter(bool renderIntoTexture, int viewIndex = 0)
        {
            return GL.GetGPUProjectionMatrix(GetProjectionMatrixNoJitter(viewIndex), renderIntoTexture);
        }

        public Matrix4x4 GetGPUViewProjectionMatrix(int viewIndex = 0)
        {
            return GetGPUViewProjectionMatrix(GetRenderIntoTexture(), viewIndex);
        }

        public Matrix4x4 GetGPUViewProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            return GetGPUProjectionMatrix(renderIntoTexture, viewIndex) * GetViewMatrix(viewIndex);
        }

        private Matrix4x4 GetCameraViewMatrix()
        {
            var currentCamera = camera;
            return currentCamera != null ? currentCamera.worldToCameraMatrix : Matrix4x4.identity;
        }

        private Matrix4x4 GetCameraProjectionMatrix()
        {
            var currentCamera = camera;
            return currentCamera != null ? currentCamera.nonJitteredProjectionMatrix : Matrix4x4.identity;
        }

        private Matrix4x4 GetCameraJitterMatrix()
        {
            var currentCamera = camera;
            if (currentCamera == null)
                return Matrix4x4.identity;

            var nonJitteredProjectionMatrix = currentCamera.nonJitteredProjectionMatrix;
            return currentCamera.projectionMatrix * nonJitteredProjectionMatrix.inverse;
        }

        private Vector2 GetCameraJitter()
        {
            var cameraJitterMatrix = GetCameraJitterMatrix();
            return new Vector2(cameraJitterMatrix.m03, cameraJitterMatrix.m13);
        }

        private bool GetRenderIntoTexture()
        {
            var currentCamera = camera;
            return m_RenderIntoTexture || (currentCamera != null && currentCamera.targetTexture != null);
        }
        
        
        public Matrix4x4 GetPixelCoordToViewDirWSMatrix()
        {
            var gpuProj = GetGPUProjectionMatrix(true);
            var gpuProjAspect = CoreUtils.ProjectionMatrixAspect(gpuProj);

            var screenSize = new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight, 1.0f / camera.scaledPixelWidth, 1.0f / camera.scaledPixelHeight);

            return CoreUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, camera.worldToCameraMatrix, gpuProj,
                screenSize, gpuProjAspect);
        }

    }
}
