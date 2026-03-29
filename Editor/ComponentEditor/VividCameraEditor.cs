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
        private static readonly GUIContent s_NearClipPlaneLabel = EditorGUIUtility.TrTextContent("Near Clip Plane");
        private static readonly GUIContent s_FarClipPlaneLabel = EditorGUIUtility.TrTextContent("Far Clip Plane");
        private static readonly GUIContent s_RenderTypeLabel = EditorGUIUtility.TrTextContent("Render Type");
        private static readonly GUIContent s_ClearDepthLabel = EditorGUIUtility.TrTextContent("Clear Depth");
        private static readonly GUIContent s_StopNaNsLabel = EditorGUIUtility.TrTextContent("Stop NaNs");
        private static readonly GUIContent s_DitheringLabel = EditorGUIUtility.TrTextContent("Dithering");
        private static readonly GUIContent s_VolumeLayerMaskLabel = EditorGUIUtility.TrTextContent("Volume Layer Mask");
        private static readonly GUIContent s_TAALabel = EditorGUIUtility.TrTextContent("Temporal Anti-Aliasing");
        private static readonly GUIContent s_EnableTAALabel = EditorGUIUtility.TrTextContent("Enable");
        private static readonly GUIContent s_TAAJitterSpreadLabel = EditorGUIUtility.TrTextContent("Jitter Spread");
        private static readonly GUIContent s_TAASampleCountLabel = EditorGUIUtility.TrTextContent("Sample Count");
        private static readonly GUIContent s_TAABaseBlendFactorLabel = EditorGUIUtility.TrTextContent("Base Blend");
        private static readonly GUIContent s_TAAMotionWeightDecayLabel = EditorGUIUtility.TrTextContent("Motion Decay");
        private static readonly GUIContent s_TAAAntiFlickerIntensityLabel = EditorGUIUtility.TrTextContent("Anti-Flicker");

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
            EditorGUILayout.PropertyField(m_SerializedCamera.nearClippingPlane, s_NearClipPlaneLabel);
            EditorGUILayout.PropertyField(m_SerializedCamera.farClippingPlane, s_FarClipPlaneLabel);
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

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(s_TAALabel, EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.enableTAA, s_EnableTAALabel);

                using (new EditorGUI.DisabledScope(!m_SerializedCamera.enableTAA.hasMultipleDifferentValues
                                                   && !m_SerializedCamera.enableTAA.boolValue))
                {
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaJitterSpread, s_TAAJitterSpreadLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaSampleCount, s_TAASampleCountLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaBaseBlendFactor, s_TAABaseBlendFactorLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaMotionWeightDecay, s_TAAMotionWeightDecayLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaAntiFlickerIntensity, s_TAAAntiFlickerIntensityLabel);
                }
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
