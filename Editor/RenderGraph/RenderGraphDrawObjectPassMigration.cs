using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphDrawObjectPassMigration
    {
        private const string RenderListFieldName = "m_RenderList";
        private const string RenderListDescFieldName = "m_RenderListDesc";

        private static readonly HashSet<string> s_PendingAssetPaths = new();
        private static readonly HashSet<string> s_PersistenceAttempts = new();

        internal static bool Migrate(RenderGraphEditorGraph graph, string assetPath)
        {
            if (graph == null)
                return false;

            return MigrateRecursive(graph, assetPath, new HashSet<RenderGraphEditorGraph>());
        }

        internal static void SchedulePersistence(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)
                || s_PersistenceAttempts.Contains(assetPath)
                || !s_PendingAssetPaths.Add(assetPath))
            {
                return;
            }

            EditorApplication.delayCall += () => PersistMigration(assetPath);
        }

        private static bool MigrateRecursive(
            RenderGraphEditorGraph graph,
            string assetPath,
            ISet<RenderGraphEditorGraph> visited)
        {
            if (graph == null || !visited.Add(graph))
                return false;

            var changed = false;
            if (graph.SchemaVersion < RenderGraphEditorGraph.CurrentSchemaVersion)
            {
                changed |= MigrateLegacyDrawObjectConnections(graph, assetPath);
                graph.SchemaVersion = RenderGraphEditorGraph.CurrentSchemaVersion;
                changed = true;
            }

            foreach (var subgraphNode in graph.GetNodes().OfType<ISubgraphNode>())
            {
                if (subgraphNode.GetSubgraph() is RenderGraphEditorGraph childGraph)
                    changed |= MigrateRecursive(childGraph, assetPath, visited);
            }

            return changed;
        }

        private static bool MigrateLegacyDrawObjectConnections(RenderGraphEditorGraph graph, string assetPath)
        {
            var changed = false;
            var candidateResourceNodes = new HashSet<RenderListResourceNodeData>();
            var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();

            foreach (var passNode in passNodes)
            {
                var passType = passNode.GetPassType();
                if (passType == null || !typeof(DrawObjectPass).IsAssignableFrom(passType))
                    continue;

                var legacyInput = passNode.GetInputPortByName(RenderListFieldName);
                if (legacyInput?.IsConnected != true)
                    continue;

                var connectedOutput = legacyInput.FirstConnectedPort;
                if (connectedOutput?.GetNode() is RenderListResourceNodeData resourceNode)
                {
                    if (!TrySetDescriptor(passNode, resourceNode.GetDescriptor()))
                    {
                        LogMigrationWarning(
                            assetPath,
                            passNode,
                            "could not copy the connected RenderList descriptor; the pass will keep its embedded default descriptor");
                        continue;
                    }

                    graph.Disconnect(connectedOutput, legacyInput);
                    TrySetOverride(passNode, false);
                    candidateResourceNodes.Add(resourceNode);
                    changed = true;
                    continue;
                }

                if (connectedOutput != null)
                {
                    if (TrySetOverride(passNode, true))
                        changed = true;

                    continue;
                }

                LogMigrationWarning(
                    assetPath,
                    passNode,
                    "contains an unresolved legacy RenderList connection; the pass will keep its embedded default descriptor");
            }

            foreach (var resourceNode in candidateResourceNodes)
            {
                if (resourceNode == null || HasAnyConnection(resourceNode))
                    continue;

                graph.RemoveNode(resourceNode);
                changed = true;
            }

            return changed;
        }

        private static bool TrySetDescriptor(RenderPassNodeData passNode, RenderGraphRenderListDesc descriptor)
        {
            var option = passNode?.GetNodeOptionByName(
                RenderGraphPassRenderListDescParameterUtility.GetOptionName(RenderListDescFieldName));
            return option != null
                   && option.TrySetValue(descriptor != null
                       ? descriptor.Clone()
                       : RenderGraphRenderListDesc.CreateOpaque());
        }

        private static bool TrySetOverride(RenderPassNodeData passNode, bool enabled)
        {
            var option = passNode?.GetNodeOptionByName(
                RenderPassPortUtility.GetOverrideOptionName(RenderListFieldName));
            if (option == null || !option.TrySetValue(enabled))
                return false;

            passNode.DefineNode();
            return true;
        }

        private static bool HasAnyConnection(RenderListResourceNodeData resourceNode)
        {
            var input = resourceNode.GetInputPortByName(RenderListResourceNodeData.InputPortName);
            var output = resourceNode.GetOutputPortByName(RenderListResourceNodeData.OutputPortName);
            return input?.IsConnected == true || output?.IsConnected == true;
        }

        private static void PersistMigration(string assetPath)
        {
            s_PendingAssetPaths.Remove(assetPath);
            if (!s_PersistenceAttempts.Add(assetPath))
                return;

            var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(assetPath);
            if (graph == null)
            {
                Debug.LogWarning($"[VividRP] Failed to persist RenderGraph migration for '{assetPath}' because the graph could not be loaded.");
                return;
            }

            Migrate(graph, assetPath);
            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void LogMigrationWarning(
            string assetPath,
            RenderPassNodeData passNode,
            string message)
        {
            var graphPath = string.IsNullOrEmpty(assetPath) ? "<unknown asset>" : assetPath;
            var passName = string.IsNullOrWhiteSpace(passNode?.Title)
                ? passNode?.GetPassType()?.Name ?? nameof(DrawObjectPass)
                : passNode.Title;
            Debug.LogWarning($"[VividRP] RenderGraph migration: '{graphPath}', pass '{passName}' ({passNode?.Guid}) {message}.");
        }
    }
}
