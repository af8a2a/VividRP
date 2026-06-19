using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public partial class VividCameraData : ContextItem
    {
        public Camera camera;
        public VividAdditionalCameraData additionalData;
        public VividCameraRenderType renderType;
        public bool clearDepth;
        public int actualWidth;
        public int actualHeight;
        public int pixelWidth;
        public int pixelHeight;
        public Rect pixelRect;
        internal int frameIndex = -1;
        internal string cameraName;
        internal EntityId cameraEntityId = EntityId.None;
        internal ShaderVariablesGlobal shaderVariablesGlobal;
        internal bool hasShaderVariablesGlobal;
        internal ViewConstants mainViewConstants = ViewConstants.Identity;
        internal FrameMetrics frameMetrics = FrameMetrics.Default;
        internal int scaledPixelWidth;
        internal int scaledPixelHeight;
        internal float nearClipPlane = 0.3f;
        internal float farClipPlane = 1000.0f;
        internal bool isOrthographic;
        internal float orthographicSize;
        internal float aspect = 1.0f;
        internal bool renderIntoTexture;
        internal bool hasCameraFrameProperties;
        internal bool hdrOutputAllowed;
        internal bool hdrOutputActive;
        internal HDROutputUtils.HDRDisplayInformation hdrDisplayInformation = VividHDROutputUtility.DefaultHDRDisplayInformation;
        internal ColorGamut hdrDisplayColorGamut = ColorGamut.sRGB;
        private Camera m_CameraNameSource;
        private Camera m_DepthTextureModeSource;
        private bool m_DepthTextureModeHasRequiredFlags;

        public Matrix4x4 viewMatrix => GetViewMatrix();
        public Matrix4x4 inverseViewMatrix => GetInverseViewMatrix();
        public Matrix4x4 projectionMatrix => GetProjectionMatrix();
        public Matrix4x4 nonJitteredProjectionMatrix => GetProjectionMatrixNoJitter();
        public Matrix4x4 gpuProjectionMatrix => GetGPUProjectionMatrix();
        public Matrix4x4 gpuProjectionMatrixNoJitter => GetGPUProjectionMatrixNoJitter();
        public Matrix4x4 jitterMatrix => GetJitterMatrix();
        public Vector2 jitter => GetJitter();
        public Matrix4x4 viewProjectionMatrix => GetViewProjectionMatrix();
        public Matrix4x4 gpuViewProjectionMatrix => GetGPUViewProjectionMatrix();

        internal void SetCamera(Camera value)
        {
            camera = value;
            if (ReferenceEquals(m_CameraNameSource, value))
                return;

            m_CameraNameSource = value;
            m_DepthTextureModeSource = null;
            m_DepthTextureModeHasRequiredFlags = false;
            hasCameraFrameProperties = false;
            cameraName = value != null ? value.name : null;
            cameraEntityId = value != null ? value.GetEntityId() : EntityId.None;
        }

        internal void CacheCameraFrameProperties(Camera value)
        {
            if (value == null)
            {
                scaledPixelWidth = 0;
                scaledPixelHeight = 0;
                nearClipPlane = 0.3f;
                farClipPlane = 1000.0f;
                isOrthographic = false;
                orthographicSize = 0.0f;
                aspect = 1.0f;
                renderIntoTexture = false;
                hasCameraFrameProperties = false;
                return;
            }

            scaledPixelWidth = value.scaledPixelWidth;
            scaledPixelHeight = value.scaledPixelHeight;
            nearClipPlane = value.nearClipPlane;
            farClipPlane = value.farClipPlane;
            isOrthographic = value.orthographic;
            orthographicSize = value.orthographicSize;
            aspect = value.aspect;
            renderIntoTexture = value.targetTexture != null || value.cameraType == CameraType.SceneView;
            hasCameraFrameProperties = true;
        }

        public Matrix4x4 GetViewMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetViewMatrix(viewIndex);

            return camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
        }

        public Matrix4x4 GetInverseViewMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetInverseViewMatrix(viewIndex);

            return GetViewMatrix(viewIndex).inverse;
        }

        public Matrix4x4 GetProjectionMatrixNoJitter(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetProjectionMatrixNoJitter(viewIndex);

            return CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);
        }

        public Matrix4x4 GetProjectionMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetProjectionMatrix(viewIndex);

            return CameraProjectionMatrixUtility.GetProjectionMatrix(camera);
        }

        public Matrix4x4 GetInverseProjectionMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetInverseProjectionMatrix(viewIndex);

            return GetProjectionMatrix(viewIndex).inverse;
        }

        public Matrix4x4 GetJitterMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetJitterMatrix(viewIndex);

            return GetProjectionMatrix(viewIndex) * GetProjectionMatrixNoJitter(viewIndex).inverse;
        }

        public Vector2 GetJitter(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.jitter;

            var currentJitterMatrix = GetJitterMatrix(viewIndex);
            return new Vector2(currentJitterMatrix.m03, currentJitterMatrix.m13);
        }

        public Matrix4x4 GetViewProjectionMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetViewProjectionMatrix(viewIndex);

            return GetProjectionMatrix(viewIndex) * GetViewMatrix(viewIndex);
        }

        public Matrix4x4 GetGPUProjectionMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUProjectionMatrix(viewIndex);

            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), IsRenderingToTexture());
        }

        public Matrix4x4 GetGPUProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUProjectionMatrix(renderIntoTexture, viewIndex);

            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), renderIntoTexture);
        }

        public Matrix4x4 GetGPUProjectionMatrixNoJitter(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUProjectionMatrixNoJitter(viewIndex);

            return GL.GetGPUProjectionMatrix(GetProjectionMatrixNoJitter(viewIndex), IsRenderingToTexture());
        }

        public Matrix4x4 GetGPUProjectionMatrixNoJitter(bool renderIntoTexture, int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUProjectionMatrixNoJitter(renderIntoTexture, viewIndex);

            return GL.GetGPUProjectionMatrix(GetProjectionMatrixNoJitter(viewIndex), renderIntoTexture);
        }

        // SceneView cameras render into a RenderTexture internally even when camera.targetTexture is null,
        // so they require the same Y-flip as explicit render-to-texture targets.
        private bool IsRenderingToTexture()
        {
            return camera != null && (camera.targetTexture != null || camera.cameraType == CameraType.SceneView);
        }

        public Matrix4x4 GetGPUViewProjectionMatrix(int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUViewProjectionMatrix(viewIndex);

            return GetGPUProjectionMatrix(viewIndex) * GetViewMatrix(viewIndex);
        }

        public Matrix4x4 GetGPUViewProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            if (additionalData != null)
                return additionalData.GetGPUViewProjectionMatrix(renderIntoTexture, viewIndex);

            return GetGPUProjectionMatrix(renderIntoTexture, viewIndex) * GetViewMatrix(viewIndex);
        }

        public override void Reset()
        {
            camera = null;
            additionalData = null;
            renderType = VividCameraRenderType.Base;
            clearDepth = true;
            actualWidth = 0;
            actualHeight = 0;
            pixelWidth = 0;
            pixelHeight = 0;
            pixelRect = default;
            frameIndex = -1;
            cameraName = null;
            cameraEntityId = EntityId.None;
            shaderVariablesGlobal = default;
            hasShaderVariablesGlobal = false;
            mainViewConstants = ViewConstants.Identity;
            frameMetrics = FrameMetrics.Default;
            scaledPixelWidth = 0;
            scaledPixelHeight = 0;
            nearClipPlane = 0.3f;
            farClipPlane = 1000.0f;
            isOrthographic = false;
            orthographicSize = 0.0f;
            aspect = 1.0f;
            renderIntoTexture = false;
            hasCameraFrameProperties = false;
            hdrOutputAllowed = false;
            hdrOutputActive = false;
            hdrDisplayInformation = VividHDROutputUtility.DefaultHDRDisplayInformation;
            hdrDisplayColorGamut = ColorGamut.sRGB;
            m_CameraNameSource = null;
            m_DepthTextureModeSource = null;
            m_DepthTextureModeHasRequiredFlags = false;
        }

        


    }
}
