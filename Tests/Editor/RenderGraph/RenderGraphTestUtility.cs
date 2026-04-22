using System;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    internal static class RenderGraphTestUtility
    {
        private const string TestGraphFolder = "Assets/Temp/VividRPTests";
        private static readonly FieldInfo s_GraphImplementationField = typeof(Graph)
            .GetField("m_Implementation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type s_GraphImplementationType = Type.GetType(
            "Unity.GraphToolkit.Editor.Implementation.GraphModelImp, UnityEditor.GraphToolkitModule",
            throwOnError: false);
        private static readonly MethodInfo s_CreateNodeModelMethod = s_GraphImplementationType?.GetMethod(
            "CreateNodeModel",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Node), typeof(Vector2) },
            null);

        internal static RenderGraphEditorGraph CreateGraph()
        {
            EnsureFolderExists("Assets/Temp");
            EnsureFolderExists(TestGraphFolder);

            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{TestGraphFolder}/RenderGraphTest_{Guid.NewGuid():N}.{RenderGraphEditorGraph.AssetExtension}");
            return GraphDatabase.CreateGraph<RenderGraphEditorGraph>(assetPath);
        }

        internal static void AddTestNode(Graph graph, Node node, Vector2? position = null)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            if (node == null)
                throw new ArgumentNullException(nameof(node));

            var implementation = s_GraphImplementationField?.GetValue(graph);
            if (implementation == null || s_CreateNodeModelMethod == null)
            {
                throw new InvalidOperationException(
                    "Failed to access GraphToolkit internals required to add a test-only node without menu registration.");
            }

            var nodeModel = s_CreateNodeModelMethod.Invoke(implementation, new object[] { node, position ?? Vector2.zero });
            if (nodeModel == null)
                throw new InvalidOperationException($"Failed to create a node model for '{node.GetType().FullName}'.");
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
