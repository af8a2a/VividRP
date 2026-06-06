using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
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

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesDepthTextureModeMarker.Auto())
            {
                EnsureRequiredDepthTextureMode(currentCamera);
            }

            int scaledWidth;
            int scaledHeight;
            int referenceWidth;
            int referenceHeight;
            float nearClip;
            float farClip;
            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesDimensionsMarker.Auto())
            {
                scaledWidth = ResolveScaledWidth(currentCamera);
                scaledHeight = ResolveScaledHeight(currentCamera);
                referenceWidth = ResolveReferenceWidth(currentCamera, scaledWidth);
                referenceHeight = ResolveReferenceHeight(currentCamera, scaledHeight);
                nearClip = ResolveNearClip(currentCamera);
                farClip = ResolveFarClip(currentCamera, nearClip);
            }

            Matrix4x4 viewMatrix;
            Matrix4x4 invViewMatrix;
            Matrix4x4 cameraProjection;
            Matrix4x4 cameraInvProjection;
            Matrix4x4 glstateMatrixProjection;
            Matrix4x4 matrixInvP;
            Matrix4x4 matrixVP;
            Matrix4x4 matrixInvVP;
            float projectionFlipSign;
            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesMatricesMarker.Auto())
            {
                viewMatrix = GetViewMatrix();
                invViewMatrix = additionalData != null
                    ? additionalData.GetInverseViewMatrix()
                    : viewMatrix.inverse;
                cameraProjection = GetProjectionMatrix();
                cameraInvProjection = additionalData != null
                    ? additionalData.GetInverseProjectionMatrix()
                    : cameraProjection.inverse;
                glstateMatrixProjection = additionalData != null
                    ? additionalData.GetGPUProjectionMatrix()
                    : GL.GetGPUProjectionMatrix(cameraProjection, ResolveRenderIntoTexture(currentCamera));
                matrixInvP = glstateMatrixProjection.inverse;
                matrixVP = glstateMatrixProjection * viewMatrix;
                matrixInvVP = matrixVP.inverse;
                projectionFlipSign = glstateMatrixProjection.m11 < 0.0f ? -1.0f : 1.0f;
            }

            // Temporal matrices: use FrameContextSystem data if available, otherwise fallback
            var nonJitteredVP = matrixVP;
            var prevVP = matrixVP;
            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesTemporalMarker.Auto())
            {
                if (temporalData != null)
                {
                    nonJitteredVP = temporalData.ViewProjection;
                    prevVP = temporalData.PreviousViewProjection;
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesFrustumPlanesMarker.Auto())
            {
                UpdateFrustumPlanes(currentCamera, cameraProjection * viewMatrix);
            }

            Vector4 worldSpaceCameraPos;
            Vector4 projectionParams;
            Vector4 screenParams;
            Vector4 zBufferParams;
            Vector4 orthoParams;
            Vector4 scaleBias;
            Vector4 scaleBiasRt;
            Vector4 rtHandleScale;
            Vector4 invProjParam;
            Vector4 screenSize;
            Vector2 globalMipBias;
            Vector4 scaledScreenParams;
            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackMarker.Auto())
            {
                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackCameraMarker.Auto())
                {
                    worldSpaceCameraPos = CreateTranslationColumn(invViewMatrix);
                    projectionParams = new Vector4(projectionFlipSign, nearClip, farClip, 1.0f / farClip);
                    zBufferParams = CreateZBufferParams(nearClip, farClip);
                    orthoParams = CreateOrthoParams(currentCamera);
                    scaleBias = new Vector4(projectionFlipSign, 1.0f, 0.0f, 0.0f);
                    scaleBiasRt = new Vector4(projectionFlipSign, 1.0f, 0.0f, 0.0f);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackScreenMarker.Auto())
                {
                    screenParams = CreateScreenParams(referenceWidth, referenceHeight);
                    screenSize = new Vector4(scaledWidth, scaledHeight, 1.0f / scaledWidth, 1.0f / scaledHeight);
                    scaledScreenParams = CreateScreenParams(scaledWidth, scaledHeight);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackRtHandleScaleMarker.Auto())
                {
                    rtHandleScale = CreateRtHandleScale();
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackMipBiasMarker.Auto())
                {
                    globalMipBias = CreateGlobalMipBias(referenceWidth, referenceHeight, scaledWidth, scaledHeight);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackMatricesMarker.Auto())
                {
                    invProjParam = CreateInvProjParam(matrixInvP);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackResultMarker.Auto())
                {
                    return new ShaderVariables
                    {
                        worldSpaceCameraPos = worldSpaceCameraPos,
                        projectionParams = projectionParams,
                        screenParams = screenParams,
                        zBufferParams = zBufferParams,
                        orthoParams = orthoParams,
                        scaleBias = scaleBias,
                        scaleBiasRt = scaleBiasRt,
                        rtHandleScale = rtHandleScale,
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
                        invProjParam = invProjParam,
                        screenSize = screenSize,
                        globalMipBias = globalMipBias,
                        scaledScreenParams = scaledScreenParams,
                        cameraWorldClipPlanes = m_CameraWorldClipPlanes,
                        frustumPlanes = m_ShaderFrustumPlanes,
                    };
                }
            }
        }

        private void EnsureRequiredDepthTextureMode(Camera currentCamera)
        {
            if (currentCamera == null)
            {
                m_DepthTextureModeSource = null;
                m_DepthTextureModeHasRequiredFlags = false;
                return;
            }

            if (ReferenceEquals(m_DepthTextureModeSource, currentCamera) && m_DepthTextureModeHasRequiredFlags)
                return;

            const DepthTextureMode requiredMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            m_DepthTextureModeSource = currentCamera;
            var depthTextureMode = currentCamera.depthTextureMode;
            if ((depthTextureMode & requiredMode) == requiredMode)
            {
                m_DepthTextureModeHasRequiredFlags = true;
                return;
            }

            currentCamera.depthTextureMode = depthTextureMode | requiredMode;
            m_DepthTextureModeHasRequiredFlags = true;
        }

        private void UpdateFrustumPlanes(Camera currentCamera, Matrix4x4 viewProjectionMatrix)
        {
            if (currentCamera == null)
            {
                ClearFrustumPlanes();
                return;
            }

            CullingUtility.ExtractFrustumPlanes(viewProjectionMatrix, m_CameraWorldClipPlanes);

            m_ShaderFrustumPlanes[0] = m_CameraWorldClipPlanes[0];
            m_ShaderFrustumPlanes[1] = m_CameraWorldClipPlanes[1];
            m_ShaderFrustumPlanes[2] = m_CameraWorldClipPlanes[3];
            m_ShaderFrustumPlanes[3] = m_CameraWorldClipPlanes[2];
            m_ShaderFrustumPlanes[4] = m_CameraWorldClipPlanes[4];
            m_ShaderFrustumPlanes[5] = m_CameraWorldClipPlanes[5];
        }

        private void ClearFrustumPlanes()
        {
            for (var index = 0; index < m_CameraWorldClipPlanes.Length; index++)
            {
                m_CameraWorldClipPlanes[index] = Vector4.zero;
                m_ShaderFrustumPlanes[index] = Vector4.zero;
            }
        }

        private int ResolveScaledWidth(Camera currentCamera)
        {
            if (actualWidth > 0)
                return actualWidth;

            if (hasCameraFrameProperties && scaledPixelWidth > 0)
                return scaledPixelWidth;

            if (currentCamera != null)
            {
                var cameraWidth = currentCamera.scaledPixelWidth;
                if (cameraWidth > 0)
                    return cameraWidth;
            }

            if (pixelWidth > 0)
                return pixelWidth;

            return Mathf.Max(1, Screen.width);
        }

        private int ResolveScaledHeight(Camera currentCamera)
        {
            if (actualHeight > 0)
                return actualHeight;

            if (hasCameraFrameProperties && scaledPixelHeight > 0)
                return scaledPixelHeight;

            if (currentCamera != null)
            {
                var cameraHeight = currentCamera.scaledPixelHeight;
                if (cameraHeight > 0)
                    return cameraHeight;
            }

            if (pixelHeight > 0)
                return pixelHeight;

            return Mathf.Max(1, Screen.height);
        }

        private int ResolveReferenceWidth(Camera currentCamera, int fallbackWidth)
        {
            if (pixelWidth > 0)
                return pixelWidth;

            if (currentCamera != null)
            {
                var cameraWidth = currentCamera.pixelWidth;
                if (cameraWidth > 0)
                    return cameraWidth;
            }

            return Mathf.Max(1, fallbackWidth);
        }

        private int ResolveReferenceHeight(Camera currentCamera, int fallbackHeight)
        {
            if (pixelHeight > 0)
                return pixelHeight;

            if (currentCamera != null)
            {
                var cameraHeight = currentCamera.pixelHeight;
                if (cameraHeight > 0)
                    return cameraHeight;
            }

            return Mathf.Max(1, fallbackHeight);
        }

        private float ResolveNearClip(Camera currentCamera)
        {
            if (hasCameraFrameProperties)
                return Mathf.Max(0.0001f, nearClipPlane);

            return currentCamera != null ? Mathf.Max(0.0001f, currentCamera.nearClipPlane) : 0.3f;
        }

        private float ResolveFarClip(Camera currentCamera, float nearClip)
        {
            if (hasCameraFrameProperties)
                return Mathf.Max(nearClip + 0.0001f, farClipPlane);

            return currentCamera != null ? Mathf.Max(nearClip + 0.0001f, currentCamera.farClipPlane) : 1000.0f;
        }

        private bool ResolveRenderIntoTexture(Camera currentCamera)
        {
            if (hasCameraFrameProperties)
                return renderIntoTexture;

            return currentCamera != null && (currentCamera.targetTexture != null || currentCamera.cameraType == CameraType.SceneView);
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

        private static Vector4 CreateTranslationColumn(Matrix4x4 matrix)
        {
            return new Vector4(matrix.m03, matrix.m13, matrix.m23, matrix.m33);
        }

        private Vector4 CreateOrthoParams(Camera currentCamera)
        {
            if (hasCameraFrameProperties)
            {
                return isOrthographic
                    ? new Vector4(orthographicSize * aspect * 2.0f, orthographicSize * 2.0f, 0.0f, 1.0f)
                    : Vector4.zero;
            }

            if (currentCamera == null || !currentCamera.orthographic)
                return Vector4.zero;

            var orthoSize = currentCamera.orthographicSize;
            var cameraAspect = currentCamera.aspect;
            return new Vector4(orthoSize * cameraAspect * 2.0f, orthoSize * 2.0f, 0.0f, 1.0f);
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
            var maxRatio = Mathf.Max(widthRatio, heightRatio);
            var mipScale = Mathf.Max(maxRatio, 0.0001f);
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
