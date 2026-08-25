using System;
using System.IO;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.RenderGraph
{
    /// <summary>
    /// Graph Toolkit authoring model for VividRP RenderGraph.
    /// </summary>
    [Serializable]
    [Graph(AssetExtension, GraphOptions.SupportsSubgraphs)]
    internal class RenderGraphEditorGraph : Graph
    {
        internal const int CurrentSchemaVersion = 3;
        internal const string AssetExtension = "vrdg";
        internal const string StandardGraphTemplateMenuPath = "Assets/Create/VividRP/Standard Render Graph";
        internal const string StandardGraphTemplateRelativePath = "Editor/RenderGraph/Templates/StandardRenderGraph.vrdg.txt";
        private const string StandardGraphTemplateFileName = "StandardRenderGraph.vrdg.txt";
        private const string DefaultGraphName = "Vivid Render Graph";
        private const string DefaultStandardGraphName = "Standard Vivid Render Graph";

        [SerializeField]
        private int m_SchemaVersion;

        internal int SchemaVersion
        {
            get => m_SchemaVersion;
            set => m_SchemaVersion = value;
        }

        [MenuItem("Assets/Create/VividRP/Render Graph", false)]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<RenderGraphEditorGraph>(DefaultGraphName);
        }

        [MenuItem(StandardGraphTemplateMenuPath, false)]
        private static void CreateStandardAssetFile()
        {
            var templateContent = LoadStandardGraphTemplateContent();
            if (string.IsNullOrEmpty(templateContent))
                return;

            ProjectWindowUtil.CreateAssetWithTextContent($"{DefaultStandardGraphName}.{AssetExtension}", templateContent);
        }

        internal static string LoadStandardGraphTemplateContent()
        {
            var candidatePaths = VividPackagePathUtility.GetCandidateAssetPaths(StandardGraphTemplateRelativePath);
            for (var i = 0; i < candidatePaths.Length; i++)
            {
                if (TryReadTemplateContent(candidatePaths[i], out var content))
                    return content;
            }

            var templateGuids = AssetDatabase.FindAssets("StandardRenderGraph");
            for (var i = 0; i < templateGuids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(templateGuids[i]);
                if (!assetPath.EndsWith($"/{StandardGraphTemplateFileName}", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryReadTemplateContent(assetPath, out var content))
                    return content;
            }

            Debug.LogError($"[VividRP] Failed to find RenderGraph template: {StandardGraphTemplateRelativePath}");
            return string.Empty;
        }

        private static bool TryReadTemplateContent(string assetPath, out string content)
        {
            content = string.Empty;
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset != null)
            {
                content = textAsset.text;
                return true;
            }

            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;

            content = File.ReadAllText(fullPath);
            return true;
        }

        public override void OnGraphChanged(GraphLogger infos)
        {
            base.OnGraphChanged(infos);
            RenderGraphEditorValidator.Validate(this, infos);
        }

        [MenuItem("Assets/VividRP/Trim Render Graph", false)]
        private static void TrimSelectedRenderGraph()
        {
            var activeObject = Selection.activeObject;
            var assetPath = AssetDatabase.GetAssetPath(activeObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith($".{AssetExtension}", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[VividRP] Select a .vrdg Render Graph asset to trim.");
                return;
            }

            var graph = GraphDatabase.LoadGraphForImporter<RenderGraphEditorGraph>(assetPath);
            if (graph == null)
            {
                Debug.LogWarning($"[VividRP] Failed to load Render Graph at '{assetPath}'.");
                return;
            }

            var removed = RenderGraphEditorValidator.TrimGraph(graph);
            if (removed > 0)
            {
                // EditorUtility.SetDirty(graph);
                // AssetDatabase.SaveAssetIfDirty(graph);
                AssetDatabase.ImportAsset(assetPath);
                Debug.Log($"[VividRP] Trimmed {removed} node(s) from '{assetPath}'.");
            }
            else
            {
                Debug.Log($"[VividRP] No nodes to trim in '{assetPath}'.");
            }
        }

        [MenuItem("Assets/VividRP/Trim Render Graph", true)]
        private static bool TrimSelectedRenderGraphValidation()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(assetPath)
                && assetPath.EndsWith($".{AssetExtension}", StringComparison.OrdinalIgnoreCase);
        }
    }
}
