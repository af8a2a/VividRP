using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividCameraRenderType
    {
        Base,
        Overlay,
    }

    public enum VividAntialiasingMode
    {
        [InspectorName("No Anti-aliasing")]
        None,

        [InspectorName("Conservative Morphological Anti-aliasing 2 (CMAA2)")]
        CMAA2,

        [InspectorName("Temporal Anti-aliasing (TAA)")]
        TemporalAntiAliasing,

        [InspectorName("Spatial-Temporal Post-Processing (STP)")]
        SpatialTemporalPostProcessing,

        [InspectorName("Deep Learning Super Sampling (DLSS)")]
        DeepLearningSuperSampling,
    }

    public static class VividCameraExtensions
    {
        public static VividAdditionalCameraData GetVividAdditionalCameraData(this Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            var gameObject = camera.gameObject;
            if (!gameObject.TryGetComponent<VividAdditionalCameraData>(out var cameraData))
                cameraData = gameObject.AddComponent<VividAdditionalCameraData>();

            return cameraData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public partial class VividAdditionalCameraData : MonoBehaviour, IAdditionalData, ISerializationCallbackReceiver
    {
        [SerializeField]
        private VividCameraRenderType m_RenderType = VividCameraRenderType.Base;

        [SerializeField]
        private bool m_ClearDepth = true;

        [SerializeField]
        private LayerMask m_VolumeLayerMask = 1;

        [SerializeField]
        private bool m_StopNaNs;

        [SerializeField]
        private bool m_Dithering;

        [Header("Anti-Aliasing")]
        [SerializeField]
        private VividAntialiasingMode m_Antialiasing = VividAntialiasingMode.None;

        [SerializeField]
        private bool m_EnableTAA;

        [SerializeField, Range(0.2f, 2.0f)]
        private float m_TAAJitterSpread = 1.0f;

        [SerializeField, Range(4, 64)]
        private int m_TAASampleCount = 8;

        [SerializeField, Range(0.0f, 0.99f)]
        private float m_TAABaseBlendFactor = 0.95f;

        [SerializeField, Range(0.5f, 6.0f)]
        private float m_TAAMotionWeightDecay = 3.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_TAAAntiFlickerIntensity = 0.5f;

        [SerializeField]
        private DLSSQuality m_DLSSQuality = DLSSQuality.Balanced;

        private Matrix4x4 m_ViewMatrix = Matrix4x4.identity;
        private Matrix4x4 m_ProjectionMatrix = Matrix4x4.identity;
        private Matrix4x4 m_JitterMatrix = Matrix4x4.identity;
        private Vector2 m_Jitter;
        private bool m_RenderIntoTexture;
        private bool m_HasStoredMatrixData;

        internal void UpdateCameraMatrices(bool renderIntoTexture)
        {
            m_RenderIntoTexture = renderIntoTexture;

            var currentCamera = camera;
            if (currentCamera == null)
            {
                ResetCameraMatrices();
                return;
            }

            var nonJitteredProjectionMatrix = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(currentCamera);
            var projectionMatrix = CameraProjectionMatrixUtility.GetProjectionMatrix(currentCamera);
            var jitterMatrix = projectionMatrix * nonJitteredProjectionMatrix.inverse;
            var jitter = new Vector2(jitterMatrix.m03, jitterMatrix.m13);
            SetViewProjectionAndJitterMatrix(
                currentCamera.worldToCameraMatrix,
                nonJitteredProjectionMatrix,
                jitterMatrix,
                jitter);
        }

        internal void ResetCameraMatrices()
        {
            m_ViewMatrix = Matrix4x4.identity;
            m_ProjectionMatrix = Matrix4x4.identity;
            m_JitterMatrix = Matrix4x4.identity;
            m_Jitter = Vector2.zero;
            m_RenderIntoTexture = false;
            m_HasStoredMatrixData = false;
        }

        internal void SetViewAndProjectionMatrix(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            m_HasStoredMatrixData = true;
            m_ViewMatrix = viewMatrix;
            m_ProjectionMatrix = projectionMatrix;
            m_JitterMatrix = Matrix4x4.identity;
            m_Jitter = Vector2.zero;
        }

        internal void SetViewProjectionAndJitterMatrix(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, Matrix4x4 jitterMatrix)
        {
            SetViewProjectionAndJitterMatrix(viewMatrix, projectionMatrix, jitterMatrix, Vector2.zero);
        }

        internal void SetViewProjectionAndJitterMatrix(
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            Matrix4x4 jitterMatrix,
            Vector2 jitter)
        {
            m_HasStoredMatrixData = true;
            m_ViewMatrix = viewMatrix;
            m_ProjectionMatrix = projectionMatrix;
            m_JitterMatrix = jitterMatrix;
            m_Jitter = jitter;
        }



        private Camera m_Camera;

        internal Camera camera
        {
            get
            {
                if (m_Camera == null)
                    TryGetComponent(out m_Camera);

                return m_Camera;
            }
        }

        public VividCameraRenderType renderType
        {
            get => m_RenderType;
            set => m_RenderType = value;
        }

        public bool clearDepth
        {
            get => m_ClearDepth;
            set => m_ClearDepth = value;
        }

        public LayerMask volumeLayerMask
        {
            get => m_VolumeLayerMask;
            set => m_VolumeLayerMask = value;
        }

        public bool stopNaNs
        {
            get => m_StopNaNs;
            set => m_StopNaNs = value;
        }

        public bool dithering
        {
            get => m_Dithering;
            set => m_Dithering = value;
        }

        public VividAntialiasingMode antialiasing
        {
            get => m_Antialiasing;
            set
            {
                m_Antialiasing = value;
                m_EnableTAA = value == VividAntialiasingMode.TemporalAntiAliasing;
            }
        }

        public bool enableTAA
        {
            get => antialiasing == VividAntialiasingMode.TemporalAntiAliasing;
            set => antialiasing = value
                ? VividAntialiasingMode.TemporalAntiAliasing
                : VividAntialiasingMode.None;
        }

        public bool enableSTP
        {
            get => antialiasing == VividAntialiasingMode.SpatialTemporalPostProcessing;
            set => antialiasing = value
                ? VividAntialiasingMode.SpatialTemporalPostProcessing
                : VividAntialiasingMode.None;
        }

        public bool enableDLSS
        {
            get => antialiasing == VividAntialiasingMode.DeepLearningSuperSampling;
            set => antialiasing = value
                ? VividAntialiasingMode.DeepLearningSuperSampling
                : VividAntialiasingMode.None;
        }

        public bool usesTemporalAntialiasing =>
            antialiasing == VividAntialiasingMode.TemporalAntiAliasing
            || antialiasing == VividAntialiasingMode.SpatialTemporalPostProcessing
            || antialiasing == VividAntialiasingMode.DeepLearningSuperSampling;

        public bool enableCMAA2
        {
            get => antialiasing == VividAntialiasingMode.CMAA2;
            set => antialiasing = value
                ? VividAntialiasingMode.CMAA2
                : VividAntialiasingMode.None;
        }

        public float taaJitterSpread
        {
            get => m_TAAJitterSpread;
            set => m_TAAJitterSpread = Mathf.Clamp(value, 0.2f, 2.0f);
        }

        public int taaSampleCount
        {
            get => m_TAASampleCount;
            set => m_TAASampleCount = Mathf.Clamp(value, 4, 64);
        }

        public float taaBaseBlendFactor
        {
            get => m_TAABaseBlendFactor;
            set => m_TAABaseBlendFactor = Mathf.Clamp(value, 0.0f, 0.99f);
        }

        public float taaMotionWeightDecay
        {
            get => m_TAAMotionWeightDecay;
            set => m_TAAMotionWeightDecay = Mathf.Clamp(value, 0.5f, 6.0f);
        }

        public float taaAntiFlickerIntensity
        {
            get => m_TAAAntiFlickerIntensity;
            set => m_TAAAntiFlickerIntensity = Mathf.Clamp01(value);
        }

        public DLSSQuality dlssQuality
        {
            get => m_DLSSQuality;
            set => m_DLSSQuality = value;
        }

        private void OnValidate()
        {
            SynchronizeLegacyAntialiasing();
            m_Camera = camera;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            SynchronizeLegacyAntialiasing();
        }

        private void SynchronizeLegacyAntialiasing()
        {
            if (m_Antialiasing == VividAntialiasingMode.None && m_EnableTAA)
                m_Antialiasing = VividAntialiasingMode.TemporalAntiAliasing;

            m_EnableTAA = m_Antialiasing == VividAntialiasingMode.TemporalAntiAliasing;
        }
    }
}
