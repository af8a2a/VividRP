using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public class RenderGraphEditorWindow : EditorWindow
    {
        private RenderGraphView m_GraphView;
        private RenderGraphAsset m_Asset;
        private bool m_AutoSave;

        [MenuItem("VividRP/Render Graph Editor")]
        public static void Open()
        {
            GetWindow<RenderGraphEditorWindow>("Render Graph Editor");
        }

        public static void Open(RenderGraphAsset asset)
        {
            var window = GetWindow<RenderGraphEditorWindow>("Render Graph Editor");
            window.LoadAsset(asset);
        }

        private void OnEnable()
        {
            BuildToolbar();
            BuildGraphView();
        }

        private void OnDisable()
        {
            if (m_GraphView != null)
            {
                m_GraphView.SaveToAsset();
                rootVisualElement.Remove(m_GraphView);
            }
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();

            var assetField = new ObjectField("Asset")
            {
                objectType = typeof(RenderGraphAsset),
                value = m_Asset
            };
            assetField.RegisterValueChangedCallback(evt =>
            {
                LoadAsset(evt.newValue as RenderGraphAsset);
            });
            toolbar.Add(assetField);

            var saveButton = new ToolbarButton(() => m_GraphView?.SaveToAsset()) { text = "Save" };
            toolbar.Add(saveButton);

            var autoSaveToggle = new ToolbarToggle { text = "Auto Save", value = m_AutoSave };
            autoSaveToggle.RegisterValueChangedCallback(evt => m_AutoSave = evt.newValue);
            toolbar.Add(autoSaveToggle);

            var validateButton = new ToolbarButton(() => ValidateGraph()) { text = "Validate" };
            toolbar.Add(validateButton);

            rootVisualElement.Add(toolbar);
        }

        private void BuildGraphView()
        {
            m_GraphView = new RenderGraphView(this);
            m_GraphView.StretchToParentSize();
            rootVisualElement.Add(m_GraphView);

            if (m_Asset != null)
                m_GraphView.PopulateFromAsset(m_Asset);
        }

        private void LoadAsset(RenderGraphAsset asset)
        {
            m_Asset = asset;
            if (m_GraphView != null)
                m_GraphView.PopulateFromAsset(m_Asset);
        }

        private void ValidateGraph()
        {
            if (m_Asset == null)
            {
                EditorUtility.DisplayDialog("Validate", "No asset loaded.", "OK");
                return;
            }

            var result = m_Asset.Validate();
            if (result.IsValid)
            {
                EditorUtility.DisplayDialog("Validate", "Graph is a valid DAG.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Validate",
                    string.Join("\n", result.Errors), "OK");
            }
        }
    }
}
