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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Entries"), includeChildren: true);
            serializedObject.ApplyModifiedProperties();
        }

        private void UpdateEntryCountLabel()
        {
            var container = target as PipelineResourcesContainer;
            var count = container != null ? container.Entries.Count : 0;
            m_EntryCountLabel.text = $"Entries: {count}";
        }
    }
}
