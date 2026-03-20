using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    internal static class RenderGraphTestUtility
    {
        private const string TestGraphFolder = "Assets/Temp/VividRPTests";

        internal static RenderGraphEditorGraph CreateGraph()
        {
            EnsureFolderExists("Assets/Temp");
            EnsureFolderExists(TestGraphFolder);

            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{TestGraphFolder}/RenderGraphTest_{Guid.NewGuid():N}.{RenderGraphEditorGraph.AssetExtension}");
            return GraphDatabase.CreateGraph<RenderGraphEditorGraph>(assetPath);
        }

        internal static void DeleteGraph(Graph graph)
        {
            if (graph == null)
                return;

            var assetPath = GraphDatabase.GetGraphAssetPath(graph);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static void EnsureFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolderExists(parent);

            var folderName = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
