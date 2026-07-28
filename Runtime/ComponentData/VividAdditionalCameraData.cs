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
        None = 0,

        [InspectorName("Conservative Morphological Anti-aliasing 2 (CMAA2)")]
        CMAA2 = 1,

        [InspectorName("Temporal Anti-aliasing (TAA)")]
        TemporalAntiAliasing = 2,

        [InspectorName("Spatial-Temporal Post-Processing (STP)")]
        SpatialTemporalPostProcessing = 3,

#if DLSS_PLUGIN_INTEGRATE
        [InspectorName("Deep Learning Super Sampling (DLSS)")]
        DeepLearningSuperSampling = 4,
#endif

        [InspectorName("FidelityFX Super Resolution 3 (FSR3)")]
        FidelityFXSuperResolution3 = 5,

        [InspectorName("Temporal Super Resolution (TSR)")]
        TemporalSuperResolution = 6,
    }

    public enum VividFsr3QualityMode
    {
        [InspectorName("Native AA")]
        NativeAA = 0,

        Quality = 1,
        Balanced = 2,
        Performance = 3,

        [InspectorName("Ultra Performance")]
        UltraPerformance = 4,
    }

    public enum VividTsrQualityMode
    {
        [InspectorName("Native AA")]
        NativeAA = 0,

        Quality = 1,
        Balanced = 2,
        Performance = 3,

        [InspectorName("Ultra Performance")]
        UltraPerformance = 4,
    }

    public static class VividCameraExtensions
    {
        public static CameraHistory GetVividCameraHistory(this Camera camera)
        {
            return CameraHistorySystem.GetOrCreate(camera);
        }

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
        private GameObject m_ExposureTarget;

        [SerializeField]
        private bool m_StopNaNs;

        [SerializeField]
        private bool m_Dithering;

        [Header("Anti-Aliasing")]
        [SerializeField]
        private VividAntialiasingMode m_Antialiasing = VividAntialiasingMode.None;

        [SerializeField]
        private bool m_EnableTAA;

        [SerializeField, HideInInspector]
        private bool m_LegacyAntialiasingMigrated;

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

#if DLSS_PLUGIN_INTEGRATE
        [SerializeField]
        private DLSSQuality m_DLSSQuality = DLSSQuality.Balanced;
#endif

        [SerializeField]
        private VividFsr3QualityMode m_FSR3Quality = VividFsr3QualityMode.Balanced;

        [SerializeField]
        private bool m_FSR3EnableSharpening = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_FSR3Sharpness = 0.2f;

        [SerializeField]
        private VividTsrQualityMode m_TSRQuality = VividTsrQualityMode.Balanced;

        [SerializeField]
        private bool m_TSREnableSharpening = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_TSRSharpness = 0.2f;

        [SerializeField, Range(8, 32)]
        private int m_TSRHistorySampleCount = 16;

        private Matrix4x4 m_ViewMatrix = Matrix4x4.identity;
        private Matrix4x4 m_ProjectionMatrix = Matrix4x4.identity;
        private Matrix4x4 m_JitterMatrix = Matrix4x4.identity;
        private Vector2 m_Jitter;
        private Vector2 m_FSR3JitterOffset;
        private int m_FSR3JitterPhaseCount;
        private Vector2 m_TSRJitterOffset;
        private int m_TSRJitterPhaseCount;
        private bool m_RenderIntoTexture;
        private bool m_HasStoredMatrixData;

        [NonSerialized]
        private bool m_ResetPostProcessingHistoryRequested;

        /// <summary>
        /// Requests a one-frame reset of post-processing history for this camera.
        /// Call this when gameplay performs a camera cut or an equivalent discontinuity.
        /// </summary>
        public void ResetPostProcessingHistory()
        {
            m_ResetPostProcessingHistoryRequested = true;
        }

        internal bool ConsumePostProcessingHistoryResetRequest()
        {
            var requested = m_ResetPostProcessingHistoryRequested;
            m_ResetPostProcessingHistoryRequested = false;
            return requested;
        }

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
            ResetFsr3JitterData();
            ResetTsrJitterData();
            m_RenderIntoTexture = false;
            m_HasStoredMatrixData = false;
            ResetPostProcessingHistory();
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

        public GameObject exposureTarget
        {
            get => m_ExposureTarget;
            set => m_ExposureTarget = value;
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

        public bool enableFSR3
        {
            get => antialiasing == VividAntialiasingMode.FidelityFXSuperResolution3;
            set => antialiasing = value
                ? VividAntialiasingMode.FidelityFXSuperResolution3
                : VividAntialiasingMode.None;
        }

        public bool enableTSR
        {
            get => antialiasing == VividAntialiasingMode.TemporalSuperResolution;
            set => antialiasing = value
                ? VividAntialiasingMode.TemporalSuperResolution
                : VividAntialiasingMode.None;
        }

#if DLSS_PLUGIN_INTEGRATE
        public bool enableDLSS
        {
            get => antialiasing == VividAntialiasingMode.DeepLearningSuperSampling;
            set => antialiasing = value
                ? VividAntialiasingMode.DeepLearningSuperSampling
                : VividAntialiasingMode.None;
        }
#endif

        public bool usesTemporalAntialiasing
        {
            get
            {
                if (antialiasing == VividAntialiasingMode.TemporalAntiAliasing
                    || antialiasing == VividAntialiasingMode.SpatialTemporalPostProcessing
                    || antialiasing == VividAntialiasingMode.FidelityFXSuperResolution3
                    || antialiasing == VividAntialiasingMode.TemporalSuperResolution)
                {
                    return true;
                }

#if DLSS_PLUGIN_INTEGRATE
                if (antialiasing == VividAntialiasingMode.DeepLearningSuperSampling)
                    return true;
#endif

                return false;
            }
        }

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

#if DLSS_PLUGIN_INTEGRATE
        public DLSSQuality dlssQuality
        {
            get => m_DLSSQuality;
            set => m_DLSSQuality = value;
        }
#endif

        public VividFsr3QualityMode fsr3Quality
        {
            get => m_FSR3Quality;
            set => m_FSR3Quality = value;
        }

        public bool fsr3EnableSharpening
        {
            get => m_FSR3EnableSharpening;
            set => m_FSR3EnableSharpening = value;
        }

        public float fsr3Sharpness
        {
            get => m_FSR3Sharpness;
            set => m_FSR3Sharpness = Mathf.Clamp01(value);
        }

        public VividTsrQualityMode tsrQuality
        {
            get => m_TSRQuality;
            set => m_TSRQuality = value;
        }

        public bool tsrEnableSharpening
        {
            get => m_TSREnableSharpening;
            set => m_TSREnableSharpening = value;
        }

        public float tsrSharpness
        {
            get => m_TSRSharpness;
            set => m_TSRSharpness = Mathf.Clamp01(value);
        }

        public int tsrHistorySampleCount
        {
            get => m_TSRHistorySampleCount;
            set => m_TSRHistorySampleCount = Mathf.Clamp(value, 8, 32);
        }

        internal Vector2 fsr3JitterOffset => m_FSR3JitterOffset;

        internal int fsr3JitterPhaseCount => m_FSR3JitterPhaseCount;

        internal void SetFsr3JitterData(Vector2 jitterOffset, int phaseCount)
        {
            m_FSR3JitterOffset = jitterOffset;
            m_FSR3JitterPhaseCount = Mathf.Max(1, phaseCount);
        }

        internal void ResetFsr3JitterData()
        {
            m_FSR3JitterOffset = Vector2.zero;
            m_FSR3JitterPhaseCount = 0;
        }

        internal Vector2 tsrJitterOffset => m_TSRJitterOffset;

        internal int tsrJitterPhaseCount => m_TSRJitterPhaseCount;

        internal void SetTsrJitterData(Vector2 jitterOffset, int phaseCount)
        {
            m_TSRJitterOffset = jitterOffset;
            m_TSRJitterPhaseCount = Mathf.Max(1, phaseCount);
        }

        internal void ResetTsrJitterData()
        {
            m_TSRJitterOffset = Vector2.zero;
            m_TSRJitterPhaseCount = 0;
        }

        private void OnValidate()
        {
            ClampTsrSettings();
            SynchronizeLegacyAntialiasing();
            m_Camera = camera;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            ClampTsrSettings();
            SynchronizeLegacyAntialiasing();
        }

        private void ClampTsrSettings()
        {
            m_TSRSharpness = Mathf.Clamp01(m_TSRSharpness);
            m_TSRHistorySampleCount = Mathf.Clamp(m_TSRHistorySampleCount, 8, 32);
        }

        private void SynchronizeLegacyAntialiasing()
        {
#if !DLSS_PLUGIN_INTEGRATE
            if ((int)m_Antialiasing == 4)
                m_Antialiasing = VividAntialiasingMode.None;
#endif

            if (!m_LegacyAntialiasingMigrated
                && m_Antialiasing == VividAntialiasingMode.None
                && m_EnableTAA)
            {
                m_Antialiasing = VividAntialiasingMode.TemporalAntiAliasing;
            }

            m_LegacyAntialiasingMigrated = true;
            m_EnableTAA = m_Antialiasing == VividAntialiasingMode.TemporalAntiAliasing;
        }
    }
}
