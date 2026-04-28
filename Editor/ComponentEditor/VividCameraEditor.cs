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
        private static readonly GUIContent s_AntialiasingLabel = EditorGUIUtility.TrTextContent("Anti-Aliasing");
        private static readonly GUIContent s_TAALabel = EditorGUIUtility.TrTextContent("Temporal Anti-Aliasing");
        private static readonly GUIContent s_TAAJitterSpreadLabel = EditorGUIUtility.TrTextContent("Jitter Spread");
        private static readonly GUIContent s_TAASampleCountLabel = EditorGUIUtility.TrTextContent("Sample Count");
        private static readonly GUIContent s_TAABaseBlendFactorLabel = EditorGUIUtility.TrTextContent("Base Blend");
        private static readonly GUIContent s_TAAMotionWeightDecayLabel = EditorGUIUtility.TrTextContent("Motion Decay");
        private static readonly GUIContent s_TAAAntiFlickerIntensityLabel = EditorGUIUtility.TrTextContent("Anti-Flicker");
#if !DLSS_PLUGIN_INTEGRATE
        private const int DlssAntialiasingModeValue = 4;
        private const string DlssDisabledWarning = "DLSS is not enabled. Define DLSS_PLUGIN_INTEGRATE to expose DLSS camera options.";
#endif
#if DLSS_PLUGIN_INTEGRATE
        private static readonly GUIContent s_DLSSLabel = EditorGUIUtility.TrTextContent("Deep Learning Super Sampling");
        private static readonly GUIContent s_DLSSQualityLabel = EditorGUIUtility.TrTextContent("Quality");
#endif
        private static readonly GUIContent s_FSR3Label = EditorGUIUtility.TrTextContent("FidelityFX Super Resolution 3");
        private static readonly GUIContent s_FSR3QualityLabel = EditorGUIUtility.TrTextContent("Quality");
        private static readonly GUIContent s_FSR3SharpeningLabel = EditorGUIUtility.TrTextContent("Sharpening");
        private static readonly GUIContent s_FSR3SharpnessLabel = EditorGUIUtility.TrTextContent("Sharpness");

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
                EditorGUILayout.PropertyField(m_SerializedCamera.antialiasing, s_AntialiasingLabel);

#if !DLSS_PLUGIN_INTEGRATE
                if (ShouldShowDlssDisabledWarning())
                    EditorGUILayout.HelpBox(DlssDisabledWarning, MessageType.Warning);
#endif

                if (ShouldShowTAASettings())
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(s_TAALabel, EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaJitterSpread, s_TAAJitterSpreadLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaSampleCount, s_TAASampleCountLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaBaseBlendFactor, s_TAABaseBlendFactorLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaMotionWeightDecay, s_TAAMotionWeightDecayLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.taaAntiFlickerIntensity, s_TAAAntiFlickerIntensityLabel);
                }

#if DLSS_PLUGIN_INTEGRATE
                if (ShouldShowDLSSSettings())
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(s_DLSSLabel, EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.dlssQuality, s_DLSSQualityLabel);
                }
#endif

                if (ShouldShowFSR3Settings())
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(s_FSR3Label, EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.fsr3Quality, s_FSR3QualityLabel);
                    EditorGUILayout.PropertyField(m_SerializedCamera.fsr3EnableSharpening, s_FSR3SharpeningLabel);
                    using (new EditorGUI.DisabledScope(
                               m_SerializedCamera.fsr3EnableSharpening != null
                               && !m_SerializedCamera.fsr3EnableSharpening.hasMultipleDifferentValues
                               && !m_SerializedCamera.fsr3EnableSharpening.boolValue))
                    {
                        EditorGUILayout.PropertyField(m_SerializedCamera.fsr3Sharpness, s_FSR3SharpnessLabel);
                    }
                }
            }
        }

        private bool ShouldDisableClearDepthField()
        {
            if (m_SerializedCamera.renderType.hasMultipleDifferentValues)
                return false;

            return (VividCameraRenderType)m_SerializedCamera.renderType.enumValueIndex == VividCameraRenderType.Base;
        }

        private bool ShouldShowTAASettings()
        {
            if (m_SerializedCamera.antialiasing == null)
                return false;

            return m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                || m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.TemporalAntiAliasing;
        }

#if !DLSS_PLUGIN_INTEGRATE
        private bool ShouldShowDlssDisabledWarning()
        {
            return m_SerializedCamera.antialiasing != null
                && !m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                && m_SerializedCamera.antialiasing.intValue == DlssAntialiasingModeValue;
        }
#endif

#if DLSS_PLUGIN_INTEGRATE
        private bool ShouldShowDLSSSettings()
        {
            if (m_SerializedCamera.antialiasing == null)
                return false;

            return m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                || m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.DeepLearningSuperSampling;
        }
#endif

        private bool ShouldShowFSR3Settings()
        {
            if (m_SerializedCamera.antialiasing == null)
                return false;

            return m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                || m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.FidelityFXSuperResolution3;
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
