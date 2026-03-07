using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(Camera))]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    [CanEditMultipleObjects]
    public class VividCameraEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_VividSettingsLabel = EditorGUIUtility.TrTextContent("VividRP");
        private static readonly GUIContent s_RenderTypeLabel = EditorGUIUtility.TrTextContent("Render Type");
        private static readonly GUIContent s_ClearDepthLabel = EditorGUIUtility.TrTextContent("Clear Depth");
        private static readonly GUIContent s_StopNaNsLabel = EditorGUIUtility.TrTextContent("Stop NaNs");
        private static readonly GUIContent s_DitheringLabel = EditorGUIUtility.TrTextContent("Dithering");
        private static readonly GUIContent s_VolumeLayerMaskLabel = EditorGUIUtility.TrTextContent("Volume Layer Mask");

        private CameraEditor.Settings m_Settings;
        private VividSerializedCamera m_SerializedCamera;

        private CameraEditor.Settings settings => m_Settings ??= new CameraEditor.Settings(serializedObject);
        private Camera camera => target as Camera;

        private void OnEnable()
        {
            settings.OnEnable();
            RebuildSerializedState();
            Undo.undoRedoPerformed += RebuildSerializedState;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildSerializedState;
        }

        public override void OnInspectorGUI()
        {
            m_SerializedCamera.Update();

            DrawBuiltInCameraInspector();
            EditorGUILayout.Space();
            DrawVividInspector();

            m_SerializedCamera.Apply();
        }

        private void RebuildSerializedState()
        {
            m_SerializedCamera = new VividSerializedCamera(serializedObject, settings);
            m_SerializedCamera.Refresh();
        }

        private void DrawBuiltInCameraInspector()
        {
            if (camera != null)
                CameraEditor.Settings.DrawCameraWarnings(camera);

            settings.DrawProjection();
            EditorGUILayout.Space();

            settings.DrawClearFlags();
            if (!settings.clearFlags.hasMultipleDifferentValues
                && (CameraClearFlags)settings.clearFlags.intValue == CameraClearFlags.SolidColor)
            {
                settings.DrawBackgroundColor();
            }

            settings.DrawCullingMask();
            settings.DrawOcclusionCulling();
            settings.DrawTargetTexture(true);
            settings.DrawHDR();
            settings.DrawMSAA();
            settings.DrawDynamicResolution();
            settings.DrawNormalizedViewPort();
            settings.DrawDepth();
            settings.DrawMultiDisplay();
            settings.DrawTargetEye();
            settings.DrawVR();
        }

        private void DrawVividInspector()
        {
            EditorGUILayout.LabelField(s_VividSettingsLabel, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedCamera.renderType, s_RenderTypeLabel);

                using (new EditorGUI.DisabledScope(ShouldDisableClearDepthField()))
                {
                    EditorGUILayout.PropertyField(m_SerializedCamera.clearDepth, s_ClearDepthLabel);
                }

                EditorGUILayout.PropertyField(m_SerializedCamera.stopNaNs, s_StopNaNsLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.dithering, s_DitheringLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.volumeLayerMask, s_VolumeLayerMaskLabel);
            }
        }

        private bool ShouldDisableClearDepthField()
        {
            if (m_SerializedCamera.renderType.hasMultipleDifferentValues)
                return false;

            return (VividCameraRenderType)m_SerializedCamera.renderType.enumValueIndex == VividCameraRenderType.Base;
        }
    }

    [CustomEditor(typeof(VividAdditionalCameraData))]
    [CanEditMultipleObjects]
    internal sealed class VividAdditionalCameraDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Managed by the Camera inspector.", MessageType.None);
        }
    }

    [InitializeOnLoad]
    internal static class VividAdditionalCameraDataEditorUtility
    {
        static VividAdditionalCameraDataEditorUtility()
        {
            ObjectFactory.componentWasAdded -= OnComponentWasAdded;
            ObjectFactory.componentWasAdded += OnComponentWasAdded;
        }

        internal static void Initialize(VividAdditionalCameraData additionalData)
        {
            if (additionalData == null)
                return;

            if ((additionalData.hideFlags & HideFlags.HideInInspector) != 0)
                return;

            Undo.RecordObject(additionalData, "Hide Vivid Additional Camera Data");
            additionalData.hideFlags |= HideFlags.HideInInspector;
            EditorUtility.SetDirty(additionalData);
        }

        private static void OnComponentWasAdded(Component component)
        {
            if (component is Camera camera)
            {
                if (!camera.TryGetComponent<VividAdditionalCameraData>(out var additionalData))
                {
                    additionalData = Undo.AddComponent<VividAdditionalCameraData>(camera.gameObject);
                    Initialize(additionalData);
                }

                return;
            }

            if (component is VividAdditionalCameraData additionalCameraData)
                Initialize(additionalCameraData);
        }
    }
}
