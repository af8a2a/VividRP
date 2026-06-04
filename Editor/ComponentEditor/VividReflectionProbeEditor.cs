using System;
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

        internal SerializedProperty multiplier { get; }
        internal SerializedProperty weight { get; }
        internal SerializedProperty importance { get; }
        internal SerializedProperty fadeDistance { get; }
        internal SerializedProperty rangeCompressionFactor { get; }
        internal SerializedProperty influenceBoxSize { get; }
        internal SerializedProperty influenceBoxOffset { get; }
        internal SerializedProperty boxBlendDistancePositive { get; }
        internal SerializedProperty boxBlendDistanceNegative { get; }
        internal SerializedProperty boxBlendNormalDistancePositive { get; }
        internal SerializedProperty boxBlendNormalDistanceNegative { get; }
        internal SerializedProperty boxSideFadePositive { get; }
        internal SerializedProperty boxSideFadeNegative { get; }
        internal SerializedProperty proxyVolumeMode { get; }
        internal SerializedProperty proxyBoxSize { get; }
        internal SerializedProperty proxyBoxOffset { get; }

        public VividSerializedReflectionProbe(SerializedObject serializedObject)
        {
            this.serializedObject = serializedObject;
            reflectionProbeAdditionalData = CoreEditorUtils.GetAdditionalData<VividAdditionalReflectionData>(
                serializedObject.targetObjects,
                VividAdditionalReflectionDataEditorUtility.Initialize);

            serializedAdditionalDataObject = new SerializedObject(reflectionProbeAdditionalData);
            multiplier = serializedAdditionalDataObject.FindProperty("m_Multiplier");
            weight = serializedAdditionalDataObject.FindProperty("m_Weight");
            importance = serializedAdditionalDataObject.FindProperty("m_Importance");
            fadeDistance = serializedAdditionalDataObject.FindProperty("m_FadeDistance");
            rangeCompressionFactor = serializedAdditionalDataObject.FindProperty("m_RangeCompressionFactor");
            influenceBoxSize = serializedAdditionalDataObject.FindProperty("m_InfluenceBoxSize");
            influenceBoxOffset = serializedAdditionalDataObject.FindProperty("m_InfluenceBoxOffset");
            boxBlendDistancePositive = serializedAdditionalDataObject.FindProperty("m_BoxBlendDistancePositive");
            boxBlendDistanceNegative = serializedAdditionalDataObject.FindProperty("m_BoxBlendDistanceNegative");
            boxBlendNormalDistancePositive = serializedAdditionalDataObject.FindProperty("m_BoxBlendNormalDistancePositive");
            boxBlendNormalDistanceNegative = serializedAdditionalDataObject.FindProperty("m_BoxBlendNormalDistanceNegative");
            boxSideFadePositive = serializedAdditionalDataObject.FindProperty("m_BoxSideFadePositive");
            boxSideFadeNegative = serializedAdditionalDataObject.FindProperty("m_BoxSideFadeNegative");
            proxyVolumeMode = serializedAdditionalDataObject.FindProperty("m_ProxyVolumeMode");
            proxyBoxSize = serializedAdditionalDataObject.FindProperty("m_ProxyBoxSize");
            proxyBoxOffset = serializedAdditionalDataObject.FindProperty("m_ProxyBoxOffset");
        }
    }

    [CustomEditor(typeof(ReflectionProbe))]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    [CanEditMultipleObjects]
    internal sealed class VividReflectionProbeEditor : UnityEditor.Editor
    {
        private static readonly Type BuiltinReflectionProbeEditorType =
            Type.GetType("UnityEditor.ReflectionProbeEditor, UnityEditor");

        private UnityEditor.Editor m_BuiltinEditor;
        private VividSerializedReflectionProbe m_SerializedReflectionProbe;

        private void OnEnable()
        {
            if (BuiltinReflectionProbeEditorType != null)
                m_BuiltinEditor = CreateEditor(targets, BuiltinReflectionProbeEditorType);

            m_SerializedReflectionProbe = new VividSerializedReflectionProbe(serializedObject);
        }

        private void OnDisable()
        {
            if (m_BuiltinEditor != null)
                DestroyImmediate(m_BuiltinEditor);
        }

        public override void OnInspectorGUI()
        {
            if (m_BuiltinEditor != null)
                m_BuiltinEditor.OnInspectorGUI();
            else
                DrawDefaultInspector();

            DrawVividReflectionProbeProperties();
        }

        private void DrawVividReflectionProbeProperties()
        {
            var serializedAdditionalData = m_SerializedReflectionProbe.serializedAdditionalDataObject;
            serializedAdditionalData.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Vivid Reflection Probe", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.multiplier);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.weight);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.importance);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.fadeDistance);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.rangeCompressionFactor);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Influence Box", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.influenceBoxSize);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.influenceBoxOffset);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxBlendDistancePositive);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxBlendDistanceNegative);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxBlendNormalDistancePositive);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxBlendNormalDistanceNegative);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxSideFadePositive);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.boxSideFadeNegative);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Proxy Box", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.proxyVolumeMode);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.proxyBoxSize);
            EditorGUILayout.PropertyField(m_SerializedReflectionProbe.proxyBoxOffset);

            if (!serializedAdditionalData.ApplyModifiedProperties())
                return;

            foreach (var additionalData in m_SerializedReflectionProbe.reflectionProbeAdditionalData)
            {
                if (additionalData == null || additionalData.reflectionProbe == null)
                    continue;

                Undo.RecordObject(additionalData.reflectionProbe, "Sync Vivid Reflection Probe");
                additionalData.SyncReflectionProbe();
                EditorUtility.SetDirty(additionalData.reflectionProbe);
            }
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
