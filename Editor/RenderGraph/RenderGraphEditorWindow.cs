using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public class RenderGraphEditorWindow : EditorWindow
    {
        private const string LastAssetPathKey = "VividRP.RenderGraphEditor.LastAssetPath";
        private const string AutoSavePrefKey = "VividRP.RenderGraphEditor.AutoSave";

        private RenderGraphView m_GraphView;
        [SerializeField]
        private RenderGraphAsset m_Asset;
        [SerializeField]
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
            RestorePreferences();

            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            BuildToolbar();
            BuildGraphView();
        }

        private void OnDisable()
        {
            if (m_GraphView != null)
            {
                m_GraphView.SaveToAsset();
                m_GraphView.RemoveFromHierarchy();
            }
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;
            toolbar.style.position = Position.Relative;

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
            autoSaveToggle.RegisterValueChangedCallback(evt =>
            {
                m_AutoSave = evt.newValue;
                EditorPrefs.SetBool(AutoSavePrefKey, m_AutoSave);
            });
            toolbar.Add(autoSaveToggle);

            var validateButton = new ToolbarButton(() => ValidateGraph()) { text = "Validate" };
            toolbar.Add(validateButton);

            rootVisualElement.Add(toolbar);
        }

        private void BuildGraphView()
        {
            var graphContainer = new VisualElement();
            graphContainer.style.flexGrow = 1;
            graphContainer.style.minHeight = 0;

            m_GraphView = new RenderGraphView(this);
            m_GraphView.style.flexGrow = 1;
            graphContainer.Add(m_GraphView);
            rootVisualElement.Add(graphContainer);

            if (m_Asset != null)
                m_GraphView.PopulateFromAsset(m_Asset);
        }

        private void LoadAsset(RenderGraphAsset asset)
        {
            m_Asset = asset;
            SaveAssetPreference(asset);
            if (m_GraphView != null)
                m_GraphView.PopulateFromAsset(m_Asset);
        }

        private void RestorePreferences()
        {
            m_AutoSave = EditorPrefs.GetBool(AutoSavePrefKey, m_AutoSave);

            if (m_Asset != null)
                return;

            var path = EditorPrefs.GetString(LastAssetPathKey, string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            m_Asset = AssetDatabase.LoadAssetAtPath<RenderGraphAsset>(path);
            if (m_Asset == null)
                EditorPrefs.DeleteKey(LastAssetPathKey);
        }

        private static void SaveAssetPreference(RenderGraphAsset asset)
        {
            if (asset == null)
            {
                EditorPrefs.DeleteKey(LastAssetPathKey);
                return;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path))
                EditorPrefs.SetString(LastAssetPathKey, path);
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
