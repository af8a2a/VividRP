using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace VividRP.Editor.RenderGraph
{
    /// <summary>
    /// Graph Toolkit authoring model for VividRP RenderGraph.
    /// </summary>
    [Serializable]
    [Graph(AssetExtension)]
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
    }
}
