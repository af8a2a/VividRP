using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData
    {
        private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
        private static readonly int ProjectionParamsId = Shader.PropertyToID("_ProjectionParams");
        private static readonly int ScreenParamsId = Shader.PropertyToID("_ScreenParams");
        private static readonly int ZBufferParamsId = Shader.PropertyToID("_ZBufferParams");
        private static readonly int OrthoParamsId = Shader.PropertyToID("unity_OrthoParams");
        private static readonly int ScaleBiasId = Shader.PropertyToID("_ScaleBias");
        private static readonly int ScaleBiasRtId = Shader.PropertyToID("_ScaleBiasRt");
        private static readonly int RtHandleScaleId = Shader.PropertyToID("_RTHandleScale");
        private static readonly int CameraWorldClipPlanesId = Shader.PropertyToID("unity_CameraWorldClipPlanes");
        private static readonly int CameraProjectionId = Shader.PropertyToID("unity_CameraProjection");
        private static readonly int CameraInvProjectionId = Shader.PropertyToID("unity_CameraInvProjection");
        private static readonly int WorldToCameraId = Shader.PropertyToID("unity_WorldToCamera");
        private static readonly int CameraToWorldId = Shader.PropertyToID("unity_CameraToWorld");
        private static readonly int GlstateMatrixProjectionId = Shader.PropertyToID("glstate_matrix_projection");
        private static readonly int MatrixVId = Shader.PropertyToID("unity_MatrixV");
        private static readonly int MatrixInvVId = Shader.PropertyToID("unity_MatrixInvV");
        private static readonly int MatrixInvPId = Shader.PropertyToID("unity_MatrixInvP");
        private static readonly int MatrixVPId = Shader.PropertyToID("unity_MatrixVP");
        private static readonly int MatrixInvVPId = Shader.PropertyToID("unity_MatrixInvVP");
        private static readonly int PrevViewProjMatrixId = Shader.PropertyToID("_PrevViewProjMatrix");
        private static readonly int NonJitteredViewProjMatrixId = Shader.PropertyToID("_NonJitteredViewProjMatrix");
        private static readonly int ViewProjMatrixId = Shader.PropertyToID("_ViewProjMatrix");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_ViewMatrix");
        private static readonly int ProjMatrixId = Shader.PropertyToID("_ProjMatrix");
        private static readonly int InvViewProjMatrixId = Shader.PropertyToID("_InvViewProjMatrix");
        private static readonly int InvViewMatrixId = Shader.PropertyToID("_InvViewMatrix");
        private static readonly int InvProjMatrixId = Shader.PropertyToID("_InvProjMatrix");
        private static readonly int InvProjParamId = Shader.PropertyToID("_InvProjParam");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int GlobalMipBiasId = Shader.PropertyToID("_GlobalMipBias");
        private static readonly int ScaledScreenParamsId = Shader.PropertyToID("_ScaledScreenParams");

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

        internal ShaderVariables BuildShaderVariables()
        {
            var currentCamera = camera;

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
            var nonJitteredViewProjMatrix = GetGPUProjectionMatrixNoJitter() * viewMatrix;
            var projectionFlipSign = glstateMatrixProjection.m11 < 0.0f ? -1.0f : 1.0f;

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
                prevViewProjMatrix = nonJitteredViewProjMatrix,
                nonJitteredViewProjMatrix = nonJitteredViewProjMatrix,
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

        internal void UpdateShaderVariables(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));

            var shaderVariables = BuildShaderVariables();

            cmd.SetGlobalVector(WorldSpaceCameraPosId, shaderVariables.worldSpaceCameraPos);
            cmd.SetGlobalVector(ProjectionParamsId, shaderVariables.projectionParams);
            cmd.SetGlobalVector(ScreenParamsId, shaderVariables.screenParams);
            cmd.SetGlobalVector(ZBufferParamsId, shaderVariables.zBufferParams);
            cmd.SetGlobalVector(OrthoParamsId, shaderVariables.orthoParams);
            cmd.SetGlobalVector(ScaleBiasId, shaderVariables.scaleBias);
            cmd.SetGlobalVector(ScaleBiasRtId, shaderVariables.scaleBiasRt);
            cmd.SetGlobalVector(RtHandleScaleId, shaderVariables.rtHandleScale);
            cmd.SetGlobalVector(
                GlobalMipBiasId,
                new Vector4(shaderVariables.globalMipBias.x, shaderVariables.globalMipBias.y, 0.0f, 0.0f));
            cmd.SetGlobalVector(ScaledScreenParamsId, shaderVariables.scaledScreenParams);
            cmd.SetGlobalVector(ScreenSizeId, shaderVariables.screenSize);
            cmd.SetGlobalVector(InvProjParamId, shaderVariables.invProjParam);

            cmd.SetGlobalMatrix(CameraProjectionId, shaderVariables.cameraProjection);
            cmd.SetGlobalMatrix(CameraInvProjectionId, shaderVariables.cameraInvProjection);
            cmd.SetGlobalMatrix(WorldToCameraId, shaderVariables.worldToCamera);
            cmd.SetGlobalMatrix(CameraToWorldId, shaderVariables.cameraToWorld);
            cmd.SetGlobalMatrix(GlstateMatrixProjectionId, shaderVariables.glstateMatrixProjection);
            cmd.SetGlobalMatrix(MatrixVId, shaderVariables.matrixV);
            cmd.SetGlobalMatrix(MatrixInvVId, shaderVariables.matrixInvV);
            cmd.SetGlobalMatrix(MatrixInvPId, shaderVariables.matrixInvP);
            cmd.SetGlobalMatrix(MatrixVPId, shaderVariables.matrixVP);
            cmd.SetGlobalMatrix(MatrixInvVPId, shaderVariables.matrixInvVP);
            // Debug.Log(shaderVariables.matrixInvVP);
            cmd.SetGlobalMatrix(PrevViewProjMatrixId, shaderVariables.prevViewProjMatrix);
            cmd.SetGlobalMatrix(NonJitteredViewProjMatrixId, shaderVariables.nonJitteredViewProjMatrix);
            cmd.SetGlobalMatrix(ViewProjMatrixId, shaderVariables.viewProjMatrix);
            cmd.SetGlobalMatrix(ViewMatrixId, shaderVariables.viewMatrix);
            cmd.SetGlobalMatrix(ProjMatrixId, shaderVariables.projMatrix);
            cmd.SetGlobalMatrix(InvViewProjMatrixId, shaderVariables.invViewProjMatrix);
            cmd.SetGlobalMatrix(InvViewMatrixId, shaderVariables.invViewMatrix);
            cmd.SetGlobalMatrix(InvProjMatrixId, shaderVariables.invProjMatrix);

            cmd.SetGlobalVectorArray(CameraWorldClipPlanesId, shaderVariables.cameraWorldClipPlanes);
            cmd.SetGlobalVectorArray(FrustumPlanesId, shaderVariables.frustumPlanes);
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
            if (SystemInfo.usesReversedZBuffer)
            {
                var x = -1.0f + (farClip / nearClip);
                return new Vector4(x, 1.0f, x / farClip, 1.0f / farClip);
            }

            var y = farClip / nearClip;
            var xNonReversed = 1.0f - y;
            return new Vector4(xNonReversed, y, xNonReversed / farClip, y / farClip);
        }

        private static Vector4 CreateOrthoParams(Camera currentCamera, int width, int height)
        {
            if (currentCamera == null || !currentCamera.orthographic)
                return Vector4.zero;

            var aspect = currentCamera.aspect;
            if (aspect <= 0.0f)
                aspect = width / (float)Mathf.Max(1, height);

            var orthoHeight = currentCamera.orthographicSize * 2.0f;
            var orthoWidth = orthoHeight * aspect;
            return new Vector4(orthoWidth, orthoHeight, 0.0f, 1.0f);
        }

        private static Vector4 CreateRtHandleScale()
        {
            var rtHandleScale = RTHandles.rtHandleProperties.rtHandleScale;
            if (rtHandleScale == Vector4.zero)
                return Vector4.one;

            if (rtHandleScale.x <= 0.0f)
                rtHandleScale.x = 1.0f;
            if (rtHandleScale.y <= 0.0f)
                rtHandleScale.y = 1.0f;
            if (rtHandleScale.z <= 0.0f)
                rtHandleScale.z = rtHandleScale.x;
            if (rtHandleScale.w <= 0.0f)
                rtHandleScale.w = rtHandleScale.y;

            return rtHandleScale;
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
