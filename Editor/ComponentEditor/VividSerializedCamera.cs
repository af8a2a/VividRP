using UnityEditor;
using UnityEditor.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed class VividSerializedCamera : ISerializedCamera
    {
        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }
        public CameraEditor.Settings baseCameraSettings { get; }

        public SerializedProperty projectionMatrixMode { get; }
        public SerializedProperty nearClippingPlane { get; }
        public SerializedProperty farClippingPlane { get; }
        public SerializedProperty dithering { get; }
        public SerializedProperty stopNaNs { get; }
        public SerializedProperty allowDynamicResolution { get; }
        public SerializedProperty volumeLayerMask { get; }
        public SerializedProperty clearDepth { get; }
        public SerializedProperty antialiasing { get; }
        public SerializedProperty enableTAA { get; }
        public SerializedProperty taaJitterSpread { get; }
        public SerializedProperty taaSampleCount { get; }
        public SerializedProperty taaBaseBlendFactor { get; }
        public SerializedProperty taaMotionWeightDecay { get; }
        public SerializedProperty taaAntiFlickerIntensity { get; }
#if DLSS_PLUGIN_INTEGRATE
        public SerializedProperty dlssQuality { get; }
#endif
        public SerializedProperty fsr3Quality { get; }
        public SerializedProperty fsr3EnableSharpening { get; }
        public SerializedProperty fsr3Sharpness { get; }

        internal SerializedProperty renderType { get; }
        internal VividAdditionalCameraData[] camerasAdditionalData { get; }

        public VividSerializedCamera(SerializedObject serializedObject, CameraEditor.Settings settings = null)
        {
            this.serializedObject = serializedObject;
            projectionMatrixMode = serializedObject.FindProperty("m_projectionMatrixMode");
            allowDynamicResolution = serializedObject.FindProperty("m_AllowDynamicResolution");

            if (settings == null)
            {
                baseCameraSettings = new CameraEditor.Settings(serializedObject);
                baseCameraSettings.OnEnable();
            }
            else
            {
                baseCameraSettings = settings;
            }

            nearClippingPlane = baseCameraSettings.nearClippingPlane;
            farClippingPlane = baseCameraSettings.farClippingPlane;

            camerasAdditionalData = CoreEditorUtils.GetAdditionalData<VividAdditionalCameraData>(
                serializedObject.targetObjects,
                VividAdditionalCameraDataEditorUtility.Initialize);

            serializedAdditionalDataObject = new SerializedObject(camerasAdditionalData);

            renderType = serializedAdditionalDataObject.FindProperty("m_RenderType");
            clearDepth = serializedAdditionalDataObject.FindProperty("m_ClearDepth");
            volumeLayerMask = serializedAdditionalDataObject.FindProperty("m_VolumeLayerMask");
            stopNaNs = serializedAdditionalDataObject.FindProperty("m_StopNaNs");
            dithering = serializedAdditionalDataObject.FindProperty("m_Dithering");
            antialiasing = serializedAdditionalDataObject.FindProperty("m_Antialiasing");
            enableTAA = serializedAdditionalDataObject.FindProperty("m_EnableTAA");
            taaJitterSpread = serializedAdditionalDataObject.FindProperty("m_TAAJitterSpread");
            taaSampleCount = serializedAdditionalDataObject.FindProperty("m_TAASampleCount");
            taaBaseBlendFactor = serializedAdditionalDataObject.FindProperty("m_TAABaseBlendFactor");
            taaMotionWeightDecay = serializedAdditionalDataObject.FindProperty("m_TAAMotionWeightDecay");
            taaAntiFlickerIntensity = serializedAdditionalDataObject.FindProperty("m_TAAAntiFlickerIntensity");
#if DLSS_PLUGIN_INTEGRATE
            dlssQuality = serializedAdditionalDataObject.FindProperty("m_DLSSQuality");
#endif
            fsr3Quality = serializedAdditionalDataObject.FindProperty("m_FSR3Quality");
            fsr3EnableSharpening = serializedAdditionalDataObject.FindProperty("m_FSR3EnableSharpening");
            fsr3Sharpness = serializedAdditionalDataObject.FindProperty("m_FSR3Sharpness");
        }

        public void Update()
        {
            baseCameraSettings.Update();
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
        }

        public void Apply()
        {
            baseCameraSettings.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
        }

        public void Refresh()
        {
            Update();
        }
    }
}
