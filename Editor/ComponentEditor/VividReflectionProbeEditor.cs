using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed class VividSerializedReflectionProbe
    {
        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }
        public VividAdditionalReflectionData[] reflectionProbeAdditionalData { get; }

        internal SerializedProperty mode { get; }
        internal SerializedProperty refreshMode { get; }
        internal SerializedProperty timeSlicingMode { get; }
        internal SerializedProperty customTexture { get; }
        internal SerializedProperty clearFlags { get; }
        internal SerializedProperty backgroundColor { get; }
        internal SerializedProperty cullingMask { get; }
        internal SerializedProperty useOcclusionCulling { get; }
        internal SerializedProperty nearClip { get; }
        internal SerializedProperty farClip { get; }
        internal SerializedProperty resolution { get; }
        internal SerializedProperty renderingLayerMask { get; }
        internal SerializedProperty renderDynamicObjects { get; }
        internal SerializedProperty multiplier { get; }
        internal SerializedProperty weight { get; }
        internal SerializedProperty importance { get; }
        internal SerializedProperty fadeDistance { get; }
        internal SerializedProperty rangeCompressionFactor { get; }
        internal SerializedProperty capturePositionOffset { get; }
        internal SerializedProperty influenceBoxSize { get; }
        internal SerializedProperty influenceBoxOffset { get; }
        internal SerializedProperty boxBlendDistancePositive { get; }
        internal SerializedProperty boxBlendDistanceNegative { get; }
        internal SerializedProperty boxBlendNormalDistancePositive { get; }
        internal SerializedProperty boxBlendNormalDistanceNegative { get; }
        internal SerializedProperty boxPerAxisControl { get; }
        internal SerializedProperty boxSideFadePositive { get; }
        internal SerializedProperty boxSideFadeNegative { get; }
        internal SerializedProperty proxyVolumeMode { get; }
        internal SerializedProperty proxyBoxSize { get; }
        internal SerializedProperty proxyBoxOffset { get; }

        public VividSerializedReflectionProbe(SerializedObject serializedObject)
        {
            this.serializedObject = serializedObject;
            mode = FindProperty(serializedObject, "m_Mode");
            refreshMode = FindProperty(serializedObject, "m_RefreshMode");
            timeSlicingMode = FindProperty(serializedObject, "m_TimeSlicingMode");
            customTexture = FindProperty(serializedObject, "m_CustomBakedTexture", "m_CustomTexture");
            clearFlags = FindProperty(serializedObject, "m_ClearFlags");
            backgroundColor = FindProperty(serializedObject, "m_BackGroundColor", "m_BackgroundColor");
            cullingMask = FindProperty(serializedObject, "m_CullingMask");
            useOcclusionCulling = FindProperty(serializedObject, "m_UseOcclusionCulling");
            nearClip = FindProperty(serializedObject, "m_NearClip");
            farClip = FindProperty(serializedObject, "m_FarClip");
            resolution = FindProperty(serializedObject, "m_Resolution");
            renderingLayerMask = FindProperty(serializedObject, "m_RenderingLayerMask");
            renderDynamicObjects = FindProperty(serializedObject, "m_RenderDynamicObjects");

            reflectionProbeAdditionalData = CoreEditorUtils.GetAdditionalData<VividAdditionalReflectionData>(
                serializedObject.targetObjects,
                VividAdditionalReflectionDataEditorUtility.Initialize);

            serializedAdditionalDataObject = new SerializedObject(reflectionProbeAdditionalData);
            multiplier = serializedAdditionalDataObject.FindProperty("m_Multiplier");
            weight = serializedAdditionalDataObject.FindProperty("m_Weight");
            importance = serializedAdditionalDataObject.FindProperty("m_Importance");
            fadeDistance = serializedAdditionalDataObject.FindProperty("m_FadeDistance");
            rangeCompressionFactor = serializedAdditionalDataObject.FindProperty("m_RangeCompressionFactor");
            capturePositionOffset = serializedAdditionalDataObject.FindProperty("m_CapturePositionOffset");
            influenceBoxSize = serializedAdditionalDataObject.FindProperty("m_InfluenceBoxSize");
            influenceBoxOffset = serializedAdditionalDataObject.FindProperty("m_InfluenceBoxOffset");
            boxBlendDistancePositive = serializedAdditionalDataObject.FindProperty("m_BoxBlendDistancePositive");
            boxBlendDistanceNegative = serializedAdditionalDataObject.FindProperty("m_BoxBlendDistanceNegative");
            boxBlendNormalDistancePositive = serializedAdditionalDataObject.FindProperty("m_BoxBlendNormalDistancePositive");
            boxBlendNormalDistanceNegative = serializedAdditionalDataObject.FindProperty("m_BoxBlendNormalDistanceNegative");
            boxPerAxisControl = serializedAdditionalDataObject.FindProperty("m_BoxPerAxisControl");
            boxSideFadePositive = serializedAdditionalDataObject.FindProperty("m_BoxSideFadePositive");
            boxSideFadeNegative = serializedAdditionalDataObject.FindProperty("m_BoxSideFadeNegative");
            proxyVolumeMode = serializedAdditionalDataObject.FindProperty("m_ProxyVolumeMode");
            proxyBoxSize = serializedAdditionalDataObject.FindProperty("m_ProxyBoxSize");
            proxyBoxOffset = serializedAdditionalDataObject.FindProperty("m_ProxyBoxOffset");
        }

        private static SerializedProperty FindProperty(SerializedObject serializedObject, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var property = serializedObject.FindProperty(propertyName);
                if (property != null)
                    return property;
            }

            return null;
        }
    }

    [CustomEditor(typeof(ReflectionProbe))]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    [CanEditMultipleObjects]
    internal sealed class VividReflectionProbeEditor : UnityEditor.Editor
    {
        [System.Flags]
        private enum Expandable
        {
            Projection = 1 << 0,
            Influence = 1 << 1,
            Capture = 1 << 2,
            Render = 1 << 3,
        }

        private const Expandable DefaultExpandedState =
            Expandable.Projection
            | Expandable.Influence
            | Expandable.Capture
            | Expandable.Render;

        private static readonly GUIContent s_TypeLabel = EditorGUIUtility.TrTextContent("Type");
        private static readonly GUIContent s_RealtimeModeLabel = EditorGUIUtility.TrTextContent("Realtime Mode");
        private static readonly GUIContent s_TimeSlicingLabel = EditorGUIUtility.TrTextContent("Time Slicing");
        private static readonly GUIContent s_ProjectionSettingsLabel = EditorGUIUtility.TrTextContent("Projection Settings");
        private static readonly GUIContent s_InfluenceVolumeLabel = EditorGUIUtility.TrTextContent("Influence Volume");
        private static readonly GUIContent s_CaptureSettingsLabel = EditorGUIUtility.TrTextContent("Capture Settings");
        private static readonly GUIContent s_RenderSettingsLabel = EditorGUIUtility.TrTextContent("Render Settings");
        private static readonly GUIContent s_ProxyVolumeLabel = EditorGUIUtility.TrTextContent("Proxy Volume");
        private static readonly GUIContent s_ProxyVolumeModeLabel = EditorGUIUtility.TrTextContent("Proxy Volume Mode");
        private static readonly GUIContent s_UseInfluenceVolumeAsProxyLabel = EditorGUIUtility.TrTextContent("Use Influence Volume As Proxy");
        private static readonly GUIContent s_DistanceBasedRoughnessLabel = EditorGUIUtility.TrTextContent("Distance Based Roughness");
        private static readonly GUIContent s_ShapeLabel = EditorGUIUtility.TrTextContent("Shape");
        private static readonly GUIContent s_BoxSizeLabel = EditorGUIUtility.TrTextContent("Box Size");
        private static readonly GUIContent s_OffsetLabel = EditorGUIUtility.TrTextContent("Offset");
        private static readonly GUIContent s_PerAxisControlLabel = EditorGUIUtility.TrTextContent("Per Axis Control");
        private static readonly GUIContent s_BlendDistanceLabel = EditorGUIUtility.TrTextContent("Blend Distance");
        private static readonly GUIContent s_BlendNormalDistanceLabel = EditorGUIUtility.TrTextContent("Blend Normal Distance");
        private static readonly GUIContent s_FaceFadeLabel = EditorGUIUtility.TrTextContent("Face Fade");
        private static readonly GUIContent s_CapturePositionLabel = EditorGUIUtility.TrTextContent("Capture Position");
        private static readonly GUIContent s_ClearModeLabel = EditorGUIUtility.TrTextContent("Clear Mode");
        private static readonly GUIContent s_BackgroundColorLabel = EditorGUIUtility.TrTextContent("Background Color");
        private static readonly GUIContent s_OcclusionCullingLabel = EditorGUIUtility.TrTextContent("Occlusion Culling");
        private static readonly GUIContent s_CullingMaskLabel = EditorGUIUtility.TrTextContent("Culling Mask");
        private static readonly GUIContent s_ClippingPlanesLabel = EditorGUIUtility.TrTextContent("Clipping Planes");
        private static readonly GUIContent s_NearLabel = EditorGUIUtility.TrTextContent("Near");
        private static readonly GUIContent s_FarLabel = EditorGUIUtility.TrTextContent("Far");
        private static readonly GUIContent s_ResolutionLabel = EditorGUIUtility.TrTextContent("Resolution");
        private static readonly GUIContent s_RangeCompressionFactorLabel = EditorGUIUtility.TrTextContent("Range Compression Factor");
        private static readonly GUIContent s_RenderingLayerMaskLabel = EditorGUIUtility.TrTextContent("Rendering Layer Mask");
        private static readonly GUIContent s_ImportanceLabel = EditorGUIUtility.TrTextContent("Importance");
        private static readonly GUIContent s_MultiplierLabel = EditorGUIUtility.TrTextContent("Multiplier");
        private static readonly GUIContent s_WeightLabel = EditorGUIUtility.TrTextContent("Weight");
        private static readonly GUIContent s_FadeDistanceLabel = EditorGUIUtility.TrTextContent("Fade Distance");
        private static readonly GUIContent s_RenderDynamicObjectsLabel = EditorGUIUtility.TrTextContent("Render Dynamic Objects");
        private static readonly GUIContent s_ExpandAllLabel = EditorGUIUtility.TrTextContent("Expand All");
        private static readonly GUIContent s_CollapseAllLabel = EditorGUIUtility.TrTextContent("Collapse All");

        private static readonly string[] s_BoxShapeOption = { "Box" };
        private static readonly GUIContent[] s_ModeOptions =
        {
            EditorGUIUtility.TrTextContent("Baked"),
            EditorGUIUtility.TrTextContent("Custom"),
            EditorGUIUtility.TrTextContent("Realtime"),
        };
        private static readonly int[] s_ModeValues =
        {
            (int)ReflectionProbeMode.Baked,
            (int)ReflectionProbeMode.Custom,
            (int)ReflectionProbeMode.Realtime,
        };
        private static readonly GUIContent[] s_RefreshModeOptions =
        {
            EditorGUIUtility.TrTextContent("On Awake"),
            EditorGUIUtility.TrTextContent("Every Frame"),
            EditorGUIUtility.TrTextContent("Via Scripting"),
        };
        private static readonly int[] s_RefreshModeValues =
        {
            (int)ReflectionProbeRefreshMode.OnAwake,
            (int)ReflectionProbeRefreshMode.EveryFrame,
            (int)ReflectionProbeRefreshMode.ViaScripting,
        };
        private static readonly GUIContent[] s_TimeSlicingOptions =
        {
            EditorGUIUtility.TrTextContent("All Faces At Once"),
            EditorGUIUtility.TrTextContent("Individual Faces"),
            EditorGUIUtility.TrTextContent("No Time Slicing"),
        };
        private static readonly int[] s_TimeSlicingValues =
        {
            (int)ReflectionProbeTimeSlicingMode.AllFacesAtOnce,
            (int)ReflectionProbeTimeSlicingMode.IndividualFaces,
            (int)ReflectionProbeTimeSlicingMode.NoTimeSlicing,
        };
        private static readonly Color[] s_HandleColors =
        {
            Color.red,
            Color.green,
            Color.blue,
            new Color(0.5f, 0.0f, 0.0f, 1.0f),
            new Color(0.0f, 0.5f, 0.0f, 1.0f),
            new Color(0.0f, 0.0f, 0.5f, 1.0f),
        };

        private static ExpandedState<Expandable, VividReflectionProbeEditor> s_ExpandedState;
        private VividSerializedReflectionProbe m_SerializedReflectionProbe;

        private void OnEnable()
        {
            EnsureExpandedState();
            m_SerializedReflectionProbe = new VividSerializedReflectionProbe(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            m_SerializedReflectionProbe.serializedObject.Update();
            m_SerializedReflectionProbe.serializedAdditionalDataObject.Update();

            DrawPrimarySettings();

            if (DrawReflectionProbeFoldout(Expandable.Projection, s_ProjectionSettingsLabel))
                DrawProjectionSettings();

            if (DrawReflectionProbeFoldout(Expandable.Influence, s_InfluenceVolumeLabel))
                DrawInfluenceVolumeSettings();

            if (DrawReflectionProbeFoldout(Expandable.Capture, s_CaptureSettingsLabel))
                DrawCaptureSettings();

            if (DrawReflectionProbeFoldout(Expandable.Render, s_RenderSettingsLabel))
                DrawRenderSettings();

            var probeChanged = m_SerializedReflectionProbe.serializedObject.ApplyModifiedProperties();
            var additionalDataChanged = m_SerializedReflectionProbe.serializedAdditionalDataObject.ApplyModifiedProperties();

            if (probeChanged || additionalDataChanged)
                SyncAdditionalDataToReflectionProbe();
        }

        private void DrawPrimarySettings()
        {
            DrawIntPopup(m_SerializedReflectionProbe.mode, s_TypeLabel, s_ModeOptions, s_ModeValues);

            if (IsProbeMode(ReflectionProbeMode.Realtime))
            {
                DrawIntPopup(m_SerializedReflectionProbe.refreshMode, s_RealtimeModeLabel, s_RefreshModeOptions, s_RefreshModeValues);
                DrawIntPopup(m_SerializedReflectionProbe.timeSlicingMode, s_TimeSlicingLabel, s_TimeSlicingOptions, s_TimeSlicingValues);
            }
            else if (IsProbeMode(ReflectionProbeMode.Custom))
            {
                DrawProperty(m_SerializedReflectionProbe.customTexture);
            }
        }

        private void DrawProjectionSettings()
        {
            DrawDisabledObjectField(s_ProxyVolumeLabel);
            DrawProperty(m_SerializedReflectionProbe.proxyVolumeMode, s_ProxyVolumeModeLabel);

            var proxyMode = GetProxyVolumeMode();
            using (new EditorGUI.DisabledScope(proxyMode == VividReflectionProbeProxyVolumeMode.Box))
            {
                EditorGUI.BeginChangeCheck();
                var useInfluenceVolume = proxyMode == VividReflectionProbeProxyVolumeMode.InfluenceVolume;
                useInfluenceVolume = EditorGUILayout.Toggle(s_UseInfluenceVolumeAsProxyLabel, useInfluenceVolume);
                if (EditorGUI.EndChangeCheck())
                {
                    m_SerializedReflectionProbe.proxyVolumeMode.intValue = useInfluenceVolume
                        ? (int)VividReflectionProbeProxyVolumeMode.InfluenceVolume
                        : (int)VividReflectionProbeProxyVolumeMode.Infinite;
                    proxyMode = GetProxyVolumeMode();
                }
            }

            if (proxyMode == VividReflectionProbeProxyVolumeMode.InfluenceVolume)
                EditorGUILayout.HelpBox("Influence shape will be used as Projection shape too.", MessageType.Info);
            else if (proxyMode == VividReflectionProbeProxyVolumeMode.Infinite)
                EditorGUILayout.HelpBox("No finite proxy volume is assigned. Reflections use infinite cubemap projection.", MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle(s_DistanceBasedRoughnessLabel, false);

            if (proxyMode != VividReflectionProbeProxyVolumeMode.Box)
                return;

            DrawProperty(m_SerializedReflectionProbe.proxyBoxSize, s_BoxSizeLabel);
            DrawProperty(m_SerializedReflectionProbe.proxyBoxOffset, s_OffsetLabel);
        }

        private void DrawInfluenceVolumeSettings()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Popup(s_ShapeLabel, 0, s_BoxShapeOption);

            DrawProperty(m_SerializedReflectionProbe.influenceBoxSize, s_BoxSizeLabel);
            DrawProperty(m_SerializedReflectionProbe.influenceBoxOffset, s_OffsetLabel);
            DrawProperty(m_SerializedReflectionProbe.boxPerAxisControl, s_PerAxisControlLabel);

            var maxBlendDistance = m_SerializedReflectionProbe.influenceBoxSize.vector3Value * 0.5f;
            DrawBlendDistance(
                s_BlendDistanceLabel,
                m_SerializedReflectionProbe.boxBlendDistancePositive,
                m_SerializedReflectionProbe.boxBlendDistanceNegative,
                maxBlendDistance);
            DrawBlendDistance(
                s_BlendNormalDistanceLabel,
                m_SerializedReflectionProbe.boxBlendNormalDistancePositive,
                m_SerializedReflectionProbe.boxBlendNormalDistanceNegative,
                maxBlendDistance);

            if (!m_SerializedReflectionProbe.boxPerAxisControl.boolValue)
                return;

            CoreEditorUtils.DrawVector6(
                s_FaceFadeLabel,
                m_SerializedReflectionProbe.boxSideFadePositive,
                m_SerializedReflectionProbe.boxSideFadeNegative,
                Vector3.zero,
                Vector3.one,
                s_HandleColors);
        }

        private void DrawCaptureSettings()
        {
            DrawProperty(m_SerializedReflectionProbe.capturePositionOffset, s_CapturePositionLabel);
            DrawProperty(m_SerializedReflectionProbe.clearFlags, s_ClearModeLabel);
            DrawProperty(m_SerializedReflectionProbe.backgroundColor, s_BackgroundColorLabel);
            DrawProperty(m_SerializedReflectionProbe.renderDynamicObjects, s_RenderDynamicObjectsLabel);
            DrawProperty(m_SerializedReflectionProbe.useOcclusionCulling, s_OcclusionCullingLabel);
            DrawProperty(m_SerializedReflectionProbe.cullingMask, s_CullingMaskLabel);
            DrawClippingPlanes();
            DrawProperty(m_SerializedReflectionProbe.resolution, s_ResolutionLabel);
            DrawProperty(m_SerializedReflectionProbe.rangeCompressionFactor, s_RangeCompressionFactorLabel);
        }

        private void DrawRenderSettings()
        {
            DrawProperty(m_SerializedReflectionProbe.renderingLayerMask, s_RenderingLayerMaskLabel);
            DrawProperty(m_SerializedReflectionProbe.importance, s_ImportanceLabel);
            DrawProperty(m_SerializedReflectionProbe.multiplier, s_MultiplierLabel);
            DrawProperty(m_SerializedReflectionProbe.weight, s_WeightLabel);
            DrawProperty(m_SerializedReflectionProbe.fadeDistance, s_FadeDistanceLabel);
        }

        private void SyncAdditionalDataToReflectionProbe()
        {
            foreach (var additionalData in m_SerializedReflectionProbe.reflectionProbeAdditionalData)
            {
                if (additionalData == null || additionalData.reflectionProbe == null)
                    continue;

                Undo.RecordObject(additionalData.reflectionProbe, "Sync Vivid Reflection Probe");
                additionalData.SyncReflectionProbe();
                EditorUtility.SetDirty(additionalData.reflectionProbe);
            }
        }

        private void DrawBlendDistance(
            GUIContent label,
            SerializedProperty blendDistancePositive,
            SerializedProperty blendDistanceNegative,
            Vector3 maxBlendDistance)
        {
            if (m_SerializedReflectionProbe.boxPerAxisControl.boolValue)
            {
                CoreEditorUtils.DrawVector6(
                    label,
                    blendDistancePositive,
                    blendDistanceNegative,
                    Vector3.zero,
                    maxBlendDistance,
                    s_HandleColors);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var distance = EditorGUILayout.FloatField(label, GetUniformDistance(blendDistancePositive, blendDistanceNegative));
            if (!EditorGUI.EndChangeCheck())
                return;

            distance = Mathf.Max(0.0f, distance);
            var clampedDistance = new Vector3(
                Mathf.Min(distance, Mathf.Max(maxBlendDistance.x, 0.0f)),
                Mathf.Min(distance, Mathf.Max(maxBlendDistance.y, 0.0f)),
                Mathf.Min(distance, Mathf.Max(maxBlendDistance.z, 0.0f)));
            blendDistancePositive.vector3Value = clampedDistance;
            blendDistanceNegative.vector3Value = clampedDistance;
        }

        private void DrawClippingPlanes()
        {
            if (m_SerializedReflectionProbe.nearClip == null && m_SerializedReflectionProbe.farClip == null)
                return;

            EditorGUILayout.LabelField(s_ClippingPlanesLabel);
            EditorGUI.indentLevel++;
            DrawProperty(m_SerializedReflectionProbe.nearClip, s_NearLabel);
            DrawProperty(m_SerializedReflectionProbe.farClip, s_FarLabel);
            EditorGUI.indentLevel--;
        }

        private bool IsProbeMode(ReflectionProbeMode probeMode)
        {
            var mode = m_SerializedReflectionProbe.mode;
            return mode != null
                && !mode.hasMultipleDifferentValues
                && mode.intValue == (int)probeMode;
        }

        private VividReflectionProbeProxyVolumeMode GetProxyVolumeMode()
        {
            return (VividReflectionProbeProxyVolumeMode)m_SerializedReflectionProbe.proxyVolumeMode.intValue;
        }

        private static float GetUniformDistance(SerializedProperty positive, SerializedProperty negative)
        {
            var positiveValue = positive.vector3Value;
            var negativeValue = negative.vector3Value;
            return Mathf.Max(
                positiveValue.x,
                positiveValue.y,
                positiveValue.z,
                negativeValue.x,
                negativeValue.y,
                negativeValue.z);
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label = null)
        {
            if (property == null)
                return;

            if (label == null)
                EditorGUILayout.PropertyField(property);
            else
                EditorGUILayout.PropertyField(property, label);
        }

        private static void DrawIntPopup(
            SerializedProperty property,
            GUIContent label,
            GUIContent[] displayedOptions,
            int[] optionValues)
        {
            if (property == null)
                return;

            EditorGUILayout.IntPopup(property, displayedOptions, optionValues, label);
        }

        private static void DrawDisabledObjectField(GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(label, null, typeof(Component), true);
        }

        private static bool DrawReflectionProbeFoldout(Expandable section, GUIContent label)
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

        private static void PopulateExpansionMenu(GenericMenu menu)
        {
            menu.AddItem(s_ExpandAllLabel, false, ExpandAllFoldouts);
            menu.AddItem(s_CollapseAllLabel, false, CollapseAllFoldouts);
        }

        private static void EnsureExpandedState()
        {
            s_ExpandedState ??= new ExpandedState<Expandable, VividReflectionProbeEditor>(DefaultExpandedState, "VividRP");
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

    [CustomEditor(typeof(VividAdditionalReflectionData))]
    [CanEditMultipleObjects]
    internal sealed class VividAdditionalReflectionDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Managed by the Reflection Probe inspector.", MessageType.None);
        }
    }

    [InitializeOnLoad]
    internal static class VividAdditionalReflectionDataEditorUtility
    {
        static VividAdditionalReflectionDataEditorUtility()
        {
            ObjectFactory.componentWasAdded -= OnComponentWasAdded;
            ObjectFactory.componentWasAdded += OnComponentWasAdded;
        }

        internal static void Initialize(VividAdditionalReflectionData additionalData)
        {
            if (additionalData == null)
                return;

            if ((additionalData.hideFlags & HideFlags.HideInInspector) == 0)
            {
                Undo.RecordObject(additionalData, "Hide Vivid Additional Reflection Data");
                additionalData.hideFlags |= HideFlags.HideInInspector;
                EditorUtility.SetDirty(additionalData);
            }

            additionalData.SyncReflectionProbe();
        }

        private static void OnComponentWasAdded(Component component)
        {
            if (component is ReflectionProbe reflectionProbe)
            {
                if (!reflectionProbe.TryGetComponent<VividAdditionalReflectionData>(out var additionalData))
                    additionalData = Undo.AddComponent<VividAdditionalReflectionData>(reflectionProbe.gameObject);

                Initialize(additionalData);
                return;
            }

            if (component is VividAdditionalReflectionData additionalReflectionData)
                Initialize(additionalReflectionData);
        }
    }
}
