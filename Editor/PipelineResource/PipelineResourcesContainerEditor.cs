using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(PipelineResourcesContainer))]
    internal sealed class PipelineResourcesContainerEditor : UnityEditor.Editor
    {
        private const string RecollectButtonName = "vivid-pipeline-resources-recollect-button";
        private const string EntryCountLabelName = "vivid-pipeline-resources-entry-count";
        private const string EntriesInspectorName = "vivid-pipeline-resources-entries";
        private const float ResourceNameColumnWidth = 320f;

        private Label m_EntryCountLabel;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement
            {
                name = "vivid-pipeline-resources-root",
            };

            var helpBox = new HelpBox(
                "Rescan all [PipelineResource] and [ResourcePath] declarations, then rewrite this resource container.",
                HelpBoxMessageType.Info)
            {
                name = "vivid-pipeline-resources-help",
            };
            root.Add(helpBox);

            m_EntryCountLabel = new Label
            {
                name = EntryCountLabelName,
            };
            UpdateEntryCountLabel();
            root.Add(m_EntryCountLabel);

            var recollectButton = new Button(RecollectResources)
            {
                name = RecollectButtonName,
                text = "Recollect Engine Resources",
            };
            root.Add(recollectButton);

            var entriesInspector = new IMGUIContainer(DrawEntriesInspector)
            {
                name = EntriesInspectorName,
            };
            root.Add(entriesInspector);

            return root;
        }

        private void RecollectResources()
        {
            if (target is not PipelineResourcesContainer container)
                return;

            PipelineResourceUpdater.UpdateContainerResources(container, recordUndo: true, logSummary: true);
            serializedObject.Update();
            UpdateEntryCountLabel();
        }

        private void DrawEntriesInspector()
        {
            serializedObject.Update();
            var entriesProperty = serializedObject.FindProperty("m_Entries");
            if (entriesProperty == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Size", entriesProperty.arraySize);
                }

                if (entriesProperty.arraySize > 0)
                {
                    EditorGUILayout.Space(2f);
                    DrawEntryHeader();

                    for (var i = 0; i < entriesProperty.arraySize; i++)
                    {
                        var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                        if (entryProperty == null)
                            continue;

                        DrawEntryRow(entryProperty);

                        if (i < entriesProperty.arraySize - 1)
                            EditorGUILayout.Space(2f);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void UpdateEntryCountLabel()
        {
            var container = target as PipelineResourcesContainer;
            var count = container != null ? container.Entries.Count : 0;
            m_EntryCountLabel.text = $"Entries: {count}";
        }

        private static void DrawEntryHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Resource Name", EditorStyles.boldLabel, GUILayout.Width(ResourceNameColumnWidth));
                EditorGUILayout.LabelField("Resource Object", EditorStyles.boldLabel);
            }
        }

        private static void DrawEntryRow(SerializedProperty entryProperty)
        {
            var resourceNameProperty = entryProperty.FindPropertyRelative("ResourceName");
            var resourceObjectProperty = entryProperty.FindPropertyRelative("ResourceObject");
            if (resourceNameProperty == null || resourceObjectProperty == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(resourceNameProperty.stringValue, GUILayout.Width(ResourceNameColumnWidth));
                }

                EditorGUILayout.PropertyField(
                    resourceObjectProperty,
                    GUIContent.none,
                    GUILayout.MinWidth(120f));
            }
        }
    }
}
