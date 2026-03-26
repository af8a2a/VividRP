using System;
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
        internal const string AssetExtension = "vrdg";
        private const string DefaultGraphName = "Vivid Render Graph";

        [MenuItem("Assets/Create/VividRP/Render Graph", false)]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<RenderGraphEditorGraph>(DefaultGraphName);
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
