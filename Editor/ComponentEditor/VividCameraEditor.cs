using System;
using UnityEditor;
using UnityEditor.Rendering;
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
        [Flags]
        private enum Expandable
        {
            Projection = 1 << 0,
            Environment = 1 << 1,
            Output = 1 << 2,
            Vivid = 1 << 3,
            TAA = 1 << 4,
            DLSS = 1 << 5,
            FSR3 = 1 << 6,
            TSR = 1 << 7,
        }

        private const Expandable DefaultExpandedState =
            Expandable.Projection
            | Expandable.Environment
            | Expandable.Output
            | Expandable.Vivid
            | Expandable.TAA
            | Expandable.DLSS
            | Expandable.FSR3
            | Expandable.TSR;

        private static ExpandedState<Expandable, VividCameraEditor> s_ExpandedState;

        private static readonly GUIContent s_ProjectionLabel = EditorGUIUtility.TrTextContent("Projection");
        private static readonly GUIContent s_EnvironmentLabel = EditorGUIUtility.TrTextContent("Environment");
        private static readonly GUIContent s_OutputLabel = EditorGUIUtility.TrTextContent("Output");
        private static readonly GUIContent s_VividSettingsLabel = EditorGUIUtility.TrTextContent("VividRP");
        private static readonly GUIContent s_ExpandAllLabel = EditorGUIUtility.TrTextContent("Expand All");
        private static readonly GUIContent s_CollapseAllLabel = EditorGUIUtility.TrTextContent("Collapse All");
        private static readonly GUIContent s_RenderTypeLabel = EditorGUIUtility.TrTextContent("Render Type");
        private static readonly GUIContent s_ClearDepthLabel = EditorGUIUtility.TrTextContent("Clear Depth");
        private static readonly GUIContent s_StopNaNsLabel = EditorGUIUtility.TrTextContent("Stop NaNs");
        private static readonly GUIContent s_DitheringLabel = EditorGUIUtility.TrTextContent("Dithering");
        private static readonly GUIContent s_VolumeLayerMaskLabel = EditorGUIUtility.TrTextContent("Volume Layer Mask");
        private static readonly GUIContent s_AntialiasingLabel = EditorGUIUtility.TrTextContent("Anti-Aliasing");
        private const string AntialiasingPassRequiredMessage =
            "Camera anti-aliasing requires an AntialiasingPass node connected in the active RenderGraph.";
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
        private static readonly GUIContent s_TSRLabel = EditorGUIUtility.TrTextContent("Temporal Super Resolution");
        private static readonly GUIContent s_TSRQualityLabel = EditorGUIUtility.TrTextContent("Quality");
        private static readonly GUIContent s_TSRSharpeningLabel = EditorGUIUtility.TrTextContent("Sharpening");
        private static readonly GUIContent s_TSRSharpnessLabel = EditorGUIUtility.TrTextContent("Sharpness");
        private static readonly GUIContent s_TSRHistorySampleCountLabel = EditorGUIUtility.TrTextContent("History Samples");

        private CameraEditor.Settings m_Settings;
        private VividSerializedCamera m_SerializedCamera;
        private static readonly Func<GUIContent, bool, bool, bool> s_DrawSubHeaderFoldout =
            CoreEditorUtils.DrawSubHeaderFoldout;

        private CameraEditor.Settings settings => m_Settings ??= new CameraEditor.Settings(serializedObject);
        private Camera camera => target as Camera;

        private void OnEnable()
        {
            EnsureExpandedState();
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

            DrawRenderTypeInspector();
            DrawBuiltInCameraInspector();
            DrawVividInspector();

            m_SerializedCamera.Apply();
        }

        private void RebuildSerializedState()
        {
            m_SerializedCamera = new VividSerializedCamera(serializedObject, settings);
            m_SerializedCamera.Refresh();
        }

        private void DrawRenderTypeInspector()
        {
            EditorGUILayout.PropertyField(m_SerializedCamera.renderType, s_RenderTypeLabel);
        }

        private void DrawBuiltInCameraInspector()
        {
            if (camera != null)
                CameraEditor.Settings.DrawCameraWarnings(camera);

            if (DrawCameraFoldout(Expandable.Projection, s_ProjectionLabel))
                DrawProjectionInspector();

            if (DrawCameraFoldout(Expandable.Environment, s_EnvironmentLabel))
                DrawEnvironmentInspector();

            if (DrawCameraFoldout(Expandable.Output, s_OutputLabel))
                DrawOutputInspector();
        }

        private void DrawProjectionInspector()
        {
            CameraUI.Drawer_Projection(m_SerializedCamera, this);
            DrawPhysicalCameraInspector();
        }

        private void DrawPhysicalCameraInspector()
        {
            if (!ShouldShowPhysicalCameraSettings())
                return;

            EditorGUILayout.Space(2.0f);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(CameraUI.PhysicalCamera.Styles.cameraBody, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_CameraBody_Sensor(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_CameraBody_ISO(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_CameraBody_ShutterSpeed(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_CameraBody_GateFit(m_SerializedCamera, this);
                }

                EditorGUILayout.LabelField(CameraUI.PhysicalCamera.Styles.lens, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_Lens_FocalLength(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_Lens_Shift(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_Lens_Aperture(m_SerializedCamera, this);
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_FocusDistance(m_SerializedCamera, this);
                }

                EditorGUILayout.LabelField(CameraUI.PhysicalCamera.Styles.apertureShape, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    CameraUI.PhysicalCamera.Drawer_PhysicalCamera_ApertureShape(m_SerializedCamera, this);
                }
            }
        }

        private void DrawEnvironmentInspector()
        {
            settings.DrawClearFlags();
            if (!settings.clearFlags.hasMultipleDifferentValues
                && (CameraClearFlags)settings.clearFlags.intValue == CameraClearFlags.SolidColor)
            {
                settings.DrawBackgroundColor();
            }

            settings.DrawCullingMask();
            settings.DrawOcclusionCulling();
        }

        private void DrawOutputInspector()
        {
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
            if (!DrawCameraFoldout(Expandable.Vivid, s_VividSettingsLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUI.DisabledScope(ShouldDisableClearDepthField()))
                {
                    EditorGUILayout.PropertyField(m_SerializedCamera.clearDepth, s_ClearDepthLabel);
                }

                EditorGUILayout.PropertyField(m_SerializedCamera.stopNaNs, s_StopNaNsLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.dithering, s_DitheringLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.volumeLayerMask, s_VolumeLayerMaskLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.antialiasing, s_AntialiasingLabel);
                if (ShouldShowAntialiasingPassRequiredMessage())
                    EditorGUILayout.HelpBox(AntialiasingPassRequiredMessage, MessageType.Info);

#if !DLSS_PLUGIN_INTEGRATE
                if (ShouldShowDlssDisabledWarning())
                    EditorGUILayout.HelpBox(DlssDisabledWarning, MessageType.Warning);
#endif

                DrawTAAInspector();

#if DLSS_PLUGIN_INTEGRATE
                DrawDLSSInspector();
#endif

                DrawFSR3Inspector();
                DrawTSRInspector();
            }
        }

        private void DrawTAAInspector()
        {
            if (!ShouldShowTAASettings())
                return;

            if (!DrawCameraSubFoldout(Expandable.TAA, s_TAALabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedCamera.taaJitterSpread, s_TAAJitterSpreadLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.taaSampleCount, s_TAASampleCountLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.taaBaseBlendFactor, s_TAABaseBlendFactorLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.taaMotionWeightDecay, s_TAAMotionWeightDecayLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.taaAntiFlickerIntensity, s_TAAAntiFlickerIntensityLabel);
            }
        }

#if DLSS_PLUGIN_INTEGRATE
        private void DrawDLSSInspector()
        {
            if (!ShouldShowDLSSSettings())
                return;

            if (!DrawCameraSubFoldout(Expandable.DLSS, s_DLSSLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedCamera.dlssQuality, s_DLSSQualityLabel);
            }
        }
#endif

        private void DrawFSR3Inspector()
        {
            if (!ShouldShowFSR3Settings())
                return;

            if (!DrawCameraSubFoldout(Expandable.FSR3, s_FSR3Label))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
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

        private void DrawTSRInspector()
        {
            if (!ShouldShowTSRSettings())
                return;

            if (!DrawCameraSubFoldout(Expandable.TSR, s_TSRLabel))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedCamera.tsrQuality, s_TSRQualityLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.tsrHistorySampleCount, s_TSRHistorySampleCountLabel);
                EditorGUILayout.PropertyField(m_SerializedCamera.tsrEnableSharpening, s_TSRSharpeningLabel);
                using (new EditorGUI.DisabledScope(
                           m_SerializedCamera.tsrEnableSharpening != null
                           && !m_SerializedCamera.tsrEnableSharpening.hasMultipleDifferentValues
                           && !m_SerializedCamera.tsrEnableSharpening.boolValue))
                {
                    EditorGUILayout.PropertyField(m_SerializedCamera.tsrSharpness, s_TSRSharpnessLabel);
                }
            }
        }

        private bool ShouldDisableClearDepthField()
        {
            if (m_SerializedCamera.renderType.hasMultipleDifferentValues)
                return false;

            return (VividCameraRenderType)m_SerializedCamera.renderType.enumValueIndex == VividCameraRenderType.Base;
        }

        private bool ShouldShowPhysicalCameraSettings()
        {
            if (settings.orthographic.hasMultipleDifferentValues || settings.orthographic.boolValue)
                return false;

            if (m_SerializedCamera.projectionMatrixMode.hasMultipleDifferentValues)
                return true;

            return m_SerializedCamera.projectionMatrixMode.intValue == (int)CameraUI.ProjectionMatrixMode.PhysicalPropertiesBased;
        }

        private bool ShouldShowTAASettings()
        {
            if (m_SerializedCamera.antialiasing == null)
                return false;

            return m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                || m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.TemporalAntiAliasing;
        }

        private bool ShouldShowAntialiasingPassRequiredMessage()
        {
            return m_SerializedCamera.antialiasing != null
                && !m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                && m_SerializedCamera.antialiasing.intValue != (int)VividAntialiasingMode.None;
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

        private bool ShouldShowTSRSettings()
        {
            if (m_SerializedCamera.antialiasing == null)
                return false;

            return m_SerializedCamera.antialiasing.hasMultipleDifferentValues
                || m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.TemporalSuperResolution;
        }

        private static bool DrawCameraFoldout(Expandable section, GUIContent label)
        {
            EnsureExpandedState();
            CoreEditorUtils.DrawSplitter();
            var wasExpanded = s_ExpandedState[section];
            var isExpanded = CoreEditorUtils.DrawHeaderFoldout(
                label,
                wasExpanded,
                customMenuContextAction: PopulateExpansionMenu);

            if (isExpanded != wasExpanded)
                s_ExpandedState[section] = isExpanded;

            return isExpanded;
        }

        private static bool DrawCameraSubFoldout(Expandable section, GUIContent label)
        {
            EnsureExpandedState();
            var wasExpanded = s_ExpandedState[section];
            var isExpanded = s_DrawSubHeaderFoldout(label, wasExpanded, false);

            if (isExpanded != wasExpanded)
                s_ExpandedState[section] = isExpanded;

            return isExpanded;
        }

        private static void PopulateExpansionMenu(GenericMenu menu)
        {
            menu.AddItem(s_ExpandAllLabel, false, ExpandAllFoldouts);
            menu.AddItem(s_CollapseAllLabel, false, CollapseAllFoldouts);
        }

        private static void EnsureExpandedState()
        {
            s_ExpandedState ??= new ExpandedState<Expandable, VividCameraEditor>(DefaultExpandedState, "VividRP");
        }

        private static void ExpandAllFoldouts()
        {
            EnsureExpandedState();
            s_ExpandedState.ExpandAll();
        }

        private static void CollapseAllFoldouts()
        {
            EnsureExpandedState();
            s_ExpandedState.CollapseAll();
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
