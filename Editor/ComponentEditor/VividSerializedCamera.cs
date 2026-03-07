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
        public SerializedProperty dithering { get; }
        public SerializedProperty stopNaNs { get; }
        public SerializedProperty allowDynamicResolution { get; }
        public SerializedProperty volumeLayerMask { get; }
        public SerializedProperty clearDepth { get; }
        public SerializedProperty antialiasing { get; }

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

            camerasAdditionalData = CoreEditorUtils.GetAdditionalData<VividAdditionalCameraData>(
                serializedObject.targetObjects,
                VividAdditionalCameraDataEditorUtility.Initialize);

            serializedAdditionalDataObject = new SerializedObject(camerasAdditionalData);

            renderType = serializedAdditionalDataObject.FindProperty("m_RenderType");
            clearDepth = serializedAdditionalDataObject.FindProperty("m_ClearDepth");
            volumeLayerMask = serializedAdditionalDataObject.FindProperty("m_VolumeLayerMask");
            stopNaNs = serializedAdditionalDataObject.FindProperty("m_StopNaNs");
            dithering = serializedAdditionalDataObject.FindProperty("m_Dithering");
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
