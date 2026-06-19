using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
        private readonly Vector4[] m_CameraWorldClipPlanes = new Vector4[6];
        private readonly Vector4[] m_ShaderFrustumPlanes = new Vector4[6];

        internal struct FrameMetrics
        {
            public int scaledWidth;
            public int scaledHeight;
            public int referenceWidth;
            public int referenceHeight;
            public float nearClip;
            public float farClip;
            public bool renderIntoTexture;

            public static FrameMetrics Default => new()
            {
                scaledWidth = 1,
                scaledHeight = 1,
                referenceWidth = 1,
                referenceHeight = 1,
                nearClip = 0.3f,
                farClip = 1000.0f,
                renderIntoTexture = false,
            };
        }

        internal struct ViewConstants
        {
            public Matrix4x4 cameraProjection;
            public Matrix4x4 cameraInvProjection;
            public Matrix4x4 viewMatrix;
            public Matrix4x4 invViewMatrix;
            public Matrix4x4 projMatrix;
            public Matrix4x4 invProjMatrix;
            public Matrix4x4 viewProjMatrix;
            public Matrix4x4 invViewProjMatrix;
            public Matrix4x4 nonJitteredProjMatrix;
            public Matrix4x4 nonJitteredViewProjMatrix;
            public Matrix4x4 prevViewMatrix;
            public Matrix4x4 prevProjMatrix;
            public Matrix4x4 prevViewProjMatrix;
            public Vector4 worldSpaceCameraPos;
            public Vector2 jitter;
            public Vector2 previousJitter;
            public float projectionFlipSign;
            public bool depthRangeZeroToOne;
            public bool reversedZ;

            public static ViewConstants Identity => new()
            {
                cameraProjection = Matrix4x4.identity,
                cameraInvProjection = Matrix4x4.identity,
                viewMatrix = Matrix4x4.identity,
                invViewMatrix = Matrix4x4.identity,
                projMatrix = Matrix4x4.identity,
                invProjMatrix = Matrix4x4.identity,
                viewProjMatrix = Matrix4x4.identity,
                invViewProjMatrix = Matrix4x4.identity,
                nonJitteredProjMatrix = Matrix4x4.identity,
                nonJitteredViewProjMatrix = Matrix4x4.identity,
                prevViewMatrix = Matrix4x4.identity,
                prevProjMatrix = Matrix4x4.identity,
                prevViewProjMatrix = Matrix4x4.identity,
                worldSpaceCameraPos = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                jitter = Vector2.zero,
                previousJitter = Vector2.zero,
                projectionFlipSign = 1.0f,
                depthRangeZeroToOne = true,
                reversedZ = false,
            };
        }

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
            public Matrix4x4 prevViewMatrix;
            public Matrix4x4 prevProjMatrix;
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
            public Vector2 jitter;
            public Vector2 previousJitter;
            public Vector4[] cameraWorldClipPlanes;
            public Vector4[] frustumPlanes;
        }

        internal void UpdateAllViewConstants(CameraTemporalData temporalData = null)
        {
            var currentCamera = camera;

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesDepthTextureModeMarker.Auto())
            {
                EnsureRequiredDepthTextureMode(currentCamera);
            }

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesDimensionsMarker.Auto())
            {
                frameMetrics = ResolveFrameMetrics(currentCamera);
            }

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesMatricesMarker.Auto())
            {
                UpdateViewConstants(frameMetrics, temporalData);
            }

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesFrustumPlanesMarker.Auto())
            {
                UpdateFrustumPlanes(currentCamera, mainViewConstants);
            }
        }

        internal ShaderVariables BuildShaderVariables(CameraTemporalData temporalData = null)
        {
            UpdateAllViewConstants(temporalData);

            var currentCamera = camera;
            var metrics = frameMetrics;
            var viewConstants = mainViewConstants;

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
                    worldSpaceCameraPos = viewConstants.worldSpaceCameraPos;
                    projectionParams = new Vector4(viewConstants.projectionFlipSign, metrics.nearClip, metrics.farClip, 1.0f / metrics.farClip);
                    zBufferParams = CreateZBufferParams(metrics.nearClip, metrics.farClip, viewConstants.reversedZ);
                    orthoParams = CreateOrthoParams(currentCamera);
                    scaleBias = new Vector4(viewConstants.projectionFlipSign, 1.0f, 0.0f, 0.0f);
                    scaleBiasRt = new Vector4(viewConstants.projectionFlipSign, 1.0f, 0.0f, 0.0f);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackScreenMarker.Auto())
                {
                    screenParams = CreateScreenParams(metrics.referenceWidth, metrics.referenceHeight);
                    screenSize = new Vector4(metrics.scaledWidth, metrics.scaledHeight, 1.0f / metrics.scaledWidth, 1.0f / metrics.scaledHeight);
                    scaledScreenParams = CreateScreenParams(metrics.scaledWidth, metrics.scaledHeight);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackRtHandleScaleMarker.Auto())
                {
                    rtHandleScale = CreateRtHandleScale();
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackMipBiasMarker.Auto())
                {
                    globalMipBias = CreateGlobalMipBias(metrics.referenceWidth, metrics.referenceHeight, metrics.scaledWidth, metrics.scaledHeight);
                }

                using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesPackMatricesMarker.Auto())
                {
                    invProjParam = CreateInvProjParam(viewConstants.invProjMatrix);
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
                        cameraProjection = viewConstants.cameraProjection,
                        cameraInvProjection = viewConstants.cameraInvProjection,
                        worldToCamera = viewConstants.viewMatrix,
                        cameraToWorld = viewConstants.invViewMatrix,
                        glstateMatrixProjection = viewConstants.projMatrix,
                        matrixV = viewConstants.viewMatrix,
                        matrixInvV = viewConstants.invViewMatrix,
                        matrixInvP = viewConstants.invProjMatrix,
                        matrixVP = viewConstants.viewProjMatrix,
                        matrixInvVP = viewConstants.invViewProjMatrix,
                        prevViewMatrix = viewConstants.prevViewMatrix,
                        prevProjMatrix = viewConstants.prevProjMatrix,
                        prevViewProjMatrix = viewConstants.prevViewProjMatrix,
                        nonJitteredViewProjMatrix = viewConstants.nonJitteredViewProjMatrix,
                        viewProjMatrix = viewConstants.viewProjMatrix,
                        viewMatrix = viewConstants.viewMatrix,
                        projMatrix = viewConstants.projMatrix,
                        invViewProjMatrix = viewConstants.invViewProjMatrix,
                        invViewMatrix = viewConstants.invViewMatrix,
                        invProjMatrix = viewConstants.invProjMatrix,
                        invProjParam = invProjParam,
                        screenSize = screenSize,
                        globalMipBias = globalMipBias,
                        scaledScreenParams = scaledScreenParams,
                        jitter = viewConstants.jitter,
                        previousJitter = viewConstants.previousJitter,
                        cameraWorldClipPlanes = m_CameraWorldClipPlanes,
                        frustumPlanes = m_ShaderFrustumPlanes,
                    };
                }
            }
        }

        private FrameMetrics ResolveFrameMetrics(Camera currentCamera)
        {
            int scaledWidth;
            int scaledHeight;
            int referenceWidth;
            int referenceHeight;
            float nearClip;
            float farClip;
            bool renderIntoTexture;

            scaledWidth = ResolveScaledWidth(currentCamera);
            scaledHeight = ResolveScaledHeight(currentCamera);
            referenceWidth = ResolveReferenceWidth(currentCamera, scaledWidth);
            referenceHeight = ResolveReferenceHeight(currentCamera, scaledHeight);
            nearClip = ResolveNearClip(currentCamera);
            farClip = ResolveFarClip(currentCamera, nearClip);
            renderIntoTexture = ResolveRenderIntoTexture(currentCamera);

            return new FrameMetrics
            {
                scaledWidth = scaledWidth,
                scaledHeight = scaledHeight,
                referenceWidth = referenceWidth,
                referenceHeight = referenceHeight,
                nearClip = nearClip,
                farClip = farClip,
                renderIntoTexture = renderIntoTexture,
            };
        }

        private void UpdateViewConstants(FrameMetrics metrics, CameraTemporalData temporalData)
        {
            Matrix4x4 viewMatrix;
            Matrix4x4 invViewMatrix;
            Matrix4x4 cameraProjection;
            Matrix4x4 cameraInvProjection;
            Matrix4x4 glstateMatrixProjection;
            Matrix4x4 matrixInvP;
            Matrix4x4 matrixVP;
            Matrix4x4 matrixInvVP;
            Matrix4x4 nonJitteredProjection;
            Matrix4x4 nonJitteredViewProjection;
            Matrix4x4 previousViewMatrix;
            Matrix4x4 previousProjectionMatrix;
            Matrix4x4 previousViewProjection;
            Vector4 worldSpaceCameraPos;
            Vector2 jitter;
            Vector2 previousJitter;
            float projectionFlipSign;
            bool depthRangeZeroToOne;
            bool reversedZ;

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
                : GL.GetGPUProjectionMatrix(cameraProjection, metrics.renderIntoTexture);
            matrixInvP = glstateMatrixProjection.inverse;
            matrixVP = glstateMatrixProjection * viewMatrix;
            matrixInvVP = matrixVP.inverse;
            nonJitteredProjection = ResolveGPUProjectionMatrixNoJitter(metrics.renderIntoTexture);
            nonJitteredViewProjection = nonJitteredProjection * viewMatrix;
            previousViewMatrix = viewMatrix;
            previousProjectionMatrix = nonJitteredProjection;
            previousViewProjection = nonJitteredViewProjection;
            jitter = GetJitter();
            previousJitter = jitter;
            worldSpaceCameraPos = CreateTranslationColumn(invViewMatrix);
            projectionFlipSign = matrixInvP.MultiplyPoint(new Vector3(0.0f, 1.0f, 0.0f)).y < 0.0f ? -1.0f : 1.0f;
            AnalyzeProjectionDepth(glstateMatrixProjection, metrics.nearClip, metrics.farClip, out depthRangeZeroToOne, out reversedZ);

            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesTemporalMarker.Auto())
            {
                if (temporalData != null)
                {
                    nonJitteredProjection = temporalData.ProjectionMatrix;
                    nonJitteredViewProjection = temporalData.ViewProjection;
                    previousViewMatrix = temporalData.PreviousViewMatrix;
                    previousProjectionMatrix = temporalData.PreviousProjectionMatrix;
                    previousViewProjection = temporalData.PreviousViewProjection;
                    jitter = temporalData.Jitter;
                    previousJitter = temporalData.PreviousJitter;
                }
            }

            mainViewConstants = new ViewConstants
            {
                cameraProjection = cameraProjection,
                cameraInvProjection = cameraInvProjection,
                viewMatrix = viewMatrix,
                invViewMatrix = invViewMatrix,
                projMatrix = glstateMatrixProjection,
                invProjMatrix = matrixInvP,
                viewProjMatrix = matrixVP,
                invViewProjMatrix = matrixInvVP,
                nonJitteredProjMatrix = nonJitteredProjection,
                nonJitteredViewProjMatrix = nonJitteredViewProjection,
                prevViewMatrix = previousViewMatrix,
                prevProjMatrix = previousProjectionMatrix,
                prevViewProjMatrix = previousViewProjection,
                worldSpaceCameraPos = worldSpaceCameraPos,
                jitter = jitter,
                previousJitter = previousJitter,
                projectionFlipSign = projectionFlipSign,
                depthRangeZeroToOne = depthRangeZeroToOne,
                reversedZ = reversedZ,
            };
        }

        private Matrix4x4 ResolveGPUProjectionMatrixNoJitter(bool renderIntoTexture)
        {
            if (additionalData != null)
                return additionalData.GetGPUProjectionMatrixNoJitter();

            return GL.GetGPUProjectionMatrix(GetProjectionMatrixNoJitter(), renderIntoTexture);
        }

        internal static void EnsureCameraDepthTextureMode(Camera currentCamera)
        {
            if (currentCamera == null)
                return;

            const DepthTextureMode requiredMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            var depthTextureMode = currentCamera.depthTextureMode;
            if ((depthTextureMode & requiredMode) == requiredMode)
                return;

            currentCamera.depthTextureMode = depthTextureMode | requiredMode;
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

            EnsureCameraDepthTextureMode(currentCamera);
            m_DepthTextureModeHasRequiredFlags = true;
        }

        private void UpdateFrustumPlanes(Camera currentCamera, ViewConstants viewConstants)
        {
            if (currentCamera == null)
            {
                ClearFrustumPlanes();
                return;
            }

            ExtractGpuFrustumPlanes(viewConstants, m_CameraWorldClipPlanes);

            m_ShaderFrustumPlanes[0] = m_CameraWorldClipPlanes[0];
            m_ShaderFrustumPlanes[1] = m_CameraWorldClipPlanes[1];
            m_ShaderFrustumPlanes[2] = m_CameraWorldClipPlanes[3];
            m_ShaderFrustumPlanes[3] = m_CameraWorldClipPlanes[2];
            m_ShaderFrustumPlanes[4] = m_CameraWorldClipPlanes[4];
            m_ShaderFrustumPlanes[5] = m_CameraWorldClipPlanes[5];
        }

        private void ExtractGpuFrustumPlanes(ViewConstants viewConstants, Vector4[] planes)
        {
            var viewProjectionMatrix = viewConstants.viewProjMatrix;

            planes[0] = NormalizePlane(
                viewProjectionMatrix.m30 + viewProjectionMatrix.m00,
                viewProjectionMatrix.m31 + viewProjectionMatrix.m01,
                viewProjectionMatrix.m32 + viewProjectionMatrix.m02,
                viewProjectionMatrix.m33 + viewProjectionMatrix.m03);
            planes[1] = NormalizePlane(
                viewProjectionMatrix.m30 - viewProjectionMatrix.m00,
                viewProjectionMatrix.m31 - viewProjectionMatrix.m01,
                viewProjectionMatrix.m32 - viewProjectionMatrix.m02,
                viewProjectionMatrix.m33 - viewProjectionMatrix.m03);
            planes[2] = NormalizePlane(
                viewProjectionMatrix.m30 + viewProjectionMatrix.m10,
                viewProjectionMatrix.m31 + viewProjectionMatrix.m11,
                viewProjectionMatrix.m32 + viewProjectionMatrix.m12,
                viewProjectionMatrix.m33 + viewProjectionMatrix.m13);
            planes[3] = NormalizePlane(
                viewProjectionMatrix.m30 - viewProjectionMatrix.m10,
                viewProjectionMatrix.m31 - viewProjectionMatrix.m11,
                viewProjectionMatrix.m32 - viewProjectionMatrix.m12,
                viewProjectionMatrix.m33 - viewProjectionMatrix.m13);

            var cameraPositionColumn = viewConstants.invViewMatrix.GetColumn(3);
            var cameraPosition = new Vector3(cameraPositionColumn.x, cameraPositionColumn.y, cameraPositionColumn.z);
            var viewDirectionColumn = -viewConstants.invViewMatrix.GetColumn(2);
            var viewDirection = new Vector3(viewDirectionColumn.x, viewDirectionColumn.y, viewDirectionColumn.z);
            viewDirection.Normalize();
            planes[4] = CreatePlane(viewDirection, cameraPosition, frameMetrics.nearClip);
            planes[5] = CreatePlane(-viewDirection, cameraPosition, -frameMetrics.farClip);
        }

        private static void AnalyzeProjectionDepth(
            Matrix4x4 projectionMatrix,
            float nearClip,
            float farClip,
            out bool depthRangeZeroToOne,
            out bool reversedZ)
        {
            var denominator = farClip * nearClip;
            if (Mathf.Abs(denominator) <= 1e-6f)
            {
                depthRangeZeroToOne = true;
                reversedZ = SystemInfo.usesReversedZBuffer;
                return;
            }

            var scale = projectionMatrix[2, 3] / denominator * (farClip - nearClip);
            depthRangeZeroToOne = Mathf.Abs(scale) < 1.5f;
            reversedZ = scale > 0.0f;
        }

        private static Vector4 NormalizePlane(float x, float y, float z, float distance)
        {
            var length = Mathf.Sqrt(x * x + y * y + z * z);
            if (length <= 1e-6f)
                return Vector4.zero;

            var reciprocalLength = 1.0f / length;
            return new Vector4(
                x * reciprocalLength,
                y * reciprocalLength,
                z * reciprocalLength,
                distance * reciprocalLength);
        }

        private static Vector4 CreatePlane(Vector3 normal, Vector3 point, float distanceOffset)
        {
            normal.Normalize();
            return new Vector4(
                normal.x,
                normal.y,
                normal.z,
                -Vector3.Dot(normal, point) - distanceOffset);
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

        internal static Vector4 CreateZBufferParams(float nearClip, float farClip, bool reversedZ)
        {
            var fpn = farClip / nearClip;
            return reversedZ
                ? new Vector4(fpn - 1.0f, 1.0f, (fpn - 1.0f) / farClip, 1.0f / farClip)
                : new Vector4(1.0f - fpn, fpn, (1.0f - fpn) / farClip, 1.0f / nearClip);
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
