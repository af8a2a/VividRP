using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
        private readonly Plane[] m_CameraFrustumPlanes = new Plane[6];
        private readonly Vector4[] m_CameraWorldClipPlanes = new Vector4[6];
        private readonly Vector4[] m_ShaderFrustumPlanes = new Vector4[6];

        internal struct ShaderVariables
        {
            public Vector4 worldSpaceCameraPos;
            public Vector4 projectionParams;
            public Vector4 screenParams;
            public Vector4 zBufferParams;
            public Vector4 orthoParams;
            public Vector4 scaleBias;
            public Vector4 scaleBiasRt;
            public Vector4 rtHandleScale;
            public Matrix4x4 cameraProjection;
            public Matrix4x4 cameraInvProjection;
            public Matrix4x4 worldToCamera;
            public Matrix4x4 cameraToWorld;
            public Matrix4x4 glstateMatrixProjection;
            public Matrix4x4 matrixV;
            public Matrix4x4 matrixInvV;
            public Matrix4x4 matrixInvP;
            public Matrix4x4 matrixVP;
            public Matrix4x4 matrixInvVP;
            public Matrix4x4 prevViewProjMatrix;
            public Matrix4x4 nonJitteredViewProjMatrix;
            public Matrix4x4 viewProjMatrix;
            public Matrix4x4 viewMatrix;
            public Matrix4x4 projMatrix;
            public Matrix4x4 invViewProjMatrix;
            public Matrix4x4 invViewMatrix;
            public Matrix4x4 invProjMatrix;
            public Vector4 invProjParam;
            public Vector4 screenSize;
            public Vector2 globalMipBias;
            public Vector4 scaledScreenParams;
            public Vector4[] cameraWorldClipPlanes;
            public Vector4[] frustumPlanes;
        }

        internal ShaderVariables BuildShaderVariables(CameraTemporalData temporalData = null)
        {
            var currentCamera = camera;

            if (currentCamera != null)
                currentCamera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

            var scaledWidth = ResolveScaledDimension(actualWidth, currentCamera != null ? currentCamera.scaledPixelWidth : 0, pixelWidth, Screen.width);
            var scaledHeight = ResolveScaledDimension(actualHeight, currentCamera != null ? currentCamera.scaledPixelHeight : 0, pixelHeight, Screen.height);
            var referenceWidth = ResolveReferenceDimension(pixelWidth, currentCamera != null ? currentCamera.pixelWidth : 0, scaledWidth);
            var referenceHeight = ResolveReferenceDimension(pixelHeight, currentCamera != null ? currentCamera.pixelHeight : 0, scaledHeight);
            var nearClip = currentCamera != null ? Mathf.Max(0.0001f, currentCamera.nearClipPlane) : 0.3f;
            var farClip = currentCamera != null ? Mathf.Max(nearClip + 0.0001f, currentCamera.farClipPlane) : 1000.0f;
            var viewMatrix = GetViewMatrix();
            var invViewMatrix = GetInverseViewMatrix();
            var cameraProjection = GetProjectionMatrix();
            var cameraInvProjection = GetInverseProjectionMatrix();
            var glstateMatrixProjection = GetGPUProjectionMatrix();
            var matrixInvP = glstateMatrixProjection.inverse;
            var matrixVP = glstateMatrixProjection * viewMatrix;
            var matrixInvVP = matrixVP.inverse;
            var projectionFlipSign = glstateMatrixProjection.m11 < 0.0f ? -1.0f : 1.0f;

            // Temporal matrices: use FrameContextSystem data if available, otherwise fallback
            var nonJitteredVP = matrixVP;
            var prevVP = matrixVP;
            if (temporalData != null)
            {
                nonJitteredVP = temporalData.ViewProjection;
                prevVP = temporalData.PreviousViewProjection;
            }

            UpdateFrustumPlanes(currentCamera);

            return new ShaderVariables
            {
                worldSpaceCameraPos = invViewMatrix.GetColumn(3),
                projectionParams = new Vector4(projectionFlipSign, nearClip, farClip, 1.0f / farClip),
                screenParams = CreateScreenParams(referenceWidth, referenceHeight),
                zBufferParams = CreateZBufferParams(nearClip, farClip),
                orthoParams = CreateOrthoParams(currentCamera, referenceWidth, referenceHeight),
                scaleBias = new Vector4(projectionFlipSign, 1.0f, 0.0f, 0.0f),
                scaleBiasRt = new Vector4(projectionFlipSign, 1.0f, 0.0f, 0.0f),
                rtHandleScale = CreateRtHandleScale(),
                cameraProjection = cameraProjection,
                cameraInvProjection = cameraInvProjection,
                worldToCamera = viewMatrix,
                cameraToWorld = invViewMatrix,
                glstateMatrixProjection = glstateMatrixProjection,
                matrixV = viewMatrix,
                matrixInvV = invViewMatrix,
                matrixInvP = matrixInvP,
                matrixVP = matrixVP,
                matrixInvVP = matrixInvVP,
                prevViewProjMatrix = prevVP,
                nonJitteredViewProjMatrix = nonJitteredVP,
                viewProjMatrix = matrixVP,
                viewMatrix = viewMatrix,
                projMatrix = glstateMatrixProjection,
                invViewProjMatrix = matrixInvVP,
                invViewMatrix = invViewMatrix,
                invProjMatrix = matrixInvP,
                invProjParam = CreateInvProjParam(matrixInvP),
                screenSize = new Vector4(scaledWidth, scaledHeight, 1.0f / scaledWidth, 1.0f / scaledHeight),
                globalMipBias = CreateGlobalMipBias(referenceWidth, referenceHeight, scaledWidth, scaledHeight),
                scaledScreenParams = CreateScreenParams(scaledWidth, scaledHeight),
                cameraWorldClipPlanes = m_CameraWorldClipPlanes,
                frustumPlanes = m_ShaderFrustumPlanes,
            };
        }

        private void UpdateFrustumPlanes(Camera currentCamera)
        {
            for (var index = 0; index < m_CameraWorldClipPlanes.Length; index++)
            {
                m_CameraWorldClipPlanes[index] = Vector4.zero;
                m_ShaderFrustumPlanes[index] = Vector4.zero;
            }

            if (currentCamera == null)
                return;

            GeometryUtility.CalculateFrustumPlanes(currentCamera, m_CameraFrustumPlanes);

            for (var index = 0; index < m_CameraFrustumPlanes.Length; index++)
                m_CameraWorldClipPlanes[index] = ConvertPlane(m_CameraFrustumPlanes[index]);

            m_ShaderFrustumPlanes[0] = m_CameraWorldClipPlanes[0];
            m_ShaderFrustumPlanes[1] = m_CameraWorldClipPlanes[1];
            m_ShaderFrustumPlanes[2] = m_CameraWorldClipPlanes[3];
            m_ShaderFrustumPlanes[3] = m_CameraWorldClipPlanes[2];
            m_ShaderFrustumPlanes[4] = m_CameraWorldClipPlanes[4];
            m_ShaderFrustumPlanes[5] = m_CameraWorldClipPlanes[5];
        }

        private static Vector4 ConvertPlane(Plane plane)
        {
            return new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
        }

        private static int ResolveScaledDimension(int preferredDimension, int cameraDimension, int fallbackDimension, int screenDimension)
        {
            if (preferredDimension > 0)
                return preferredDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            if (fallbackDimension > 0)
                return fallbackDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static int ResolveReferenceDimension(int preferredDimension, int cameraDimension, int fallbackDimension)
        {
            if (preferredDimension > 0)
                return preferredDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, fallbackDimension);
        }

        private static Vector4 CreateScreenParams(int width, int height)
        {
            return new Vector4(width, height, 1.0f + (1.0f / width), 1.0f + (1.0f / height));
        }

        private static Vector4 CreateZBufferParams(float nearClip, float farClip)
        {
            var fpn = farClip / nearClip;
            return new Vector4(fpn - 1.0f, 1.0f, (fpn - 1.0f) / farClip, 1.0f / farClip);
        }

        private static Vector4 CreateOrthoParams(Camera currentCamera, int referenceWidth, int referenceHeight)
        {
            if (currentCamera == null || !currentCamera.orthographic)
                return Vector4.zero;

            var orthoSize = currentCamera.orthographicSize;
            var aspect = currentCamera.aspect;
            return new Vector4(orthoSize * aspect * 2.0f, orthoSize * 2.0f, 0.0f, 1.0f);
        }

        private static Vector4 CreateRtHandleScale()
        {
            var rtHandleScale = RTHandles.rtHandleProperties.rtHandleScale;
            return new Vector4(rtHandleScale.x, rtHandleScale.y, rtHandleScale.x, rtHandleScale.y);
        }

        private static Vector2 CreateGlobalMipBias(int referenceWidth, int referenceHeight, int scaledWidth, int scaledHeight)
        {
            var widthRatio = referenceWidth / (float)Mathf.Max(1, scaledWidth);
            var heightRatio = referenceHeight / (float)Mathf.Max(1, scaledHeight);
            var mipScale = Mathf.Max(widthRatio, heightRatio, 0.0001f);
            var mipBias = Mathf.Log(mipScale, 2.0f);
            return new Vector2(mipBias, Mathf.Pow(2.0f, mipBias));
        }

        private static Vector4 CreateInvProjParam(Matrix4x4 invProjectionMatrix)
        {
            return new Vector4(
                invProjectionMatrix.m00,
                invProjectionMatrix.m11,
                invProjectionMatrix.m32,
                invProjectionMatrix.m33);
        }
    }
}
