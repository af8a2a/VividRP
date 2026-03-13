using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderPassNodeNavigationUtility
    {
        private const string BackingNodePropertyName = "Node";
        private static readonly BindingFlags InstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool TryOpenPassScript(object nodeModel)
        {
            if (!TryGetRenderPassNode(nodeModel, out var renderPassNode))
                return false;

            if (!renderPassNode.TryGetPassScript(out var script) || script == null)
                return false;

            return AssetDatabase.OpenAsset(script);
        }

        internal static bool TryGetRenderPassNode(object nodeModel, out RenderPassNodeData renderPassNode)
        {
            renderPassNode = null;
            if (nodeModel == null)
                return false;

            var backingNode = GetBackingNode(nodeModel);
            renderPassNode = backingNode as RenderPassNodeData;
            return renderPassNode != null;
        }

        private static Node GetBackingNode(object nodeModel)
        {
            var property = nodeModel.GetType().GetProperty(BackingNodePropertyName, InstanceBindings);
            return property?.GetValue(nodeModel) as Node;
        }
    }
}
