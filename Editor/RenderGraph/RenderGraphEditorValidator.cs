using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphEditorValidator
    {
        internal static void Validate(RenderGraphEditorGraph graph, GraphLogger infos)
        {
            var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToList();
            if (passNodes.Count == 0)
            {
                infos.LogWarning("Add at least one pass node to your Render Graph.", graph);
                return;
            }

            foreach (var passNode in passNodes)
            {
                if (!passNode.TryGetPassScript(out var script) || script == null)
                {
                    infos.LogError("Select a pass script (a class implementing IRenderPass).", passNode);
                    continue;
                }

                var passType = script.GetClass();
                if (passType == null)
                {
                    infos.LogError("Pass script does not reference a valid class.", passNode);
                    continue;
                }

                if (!typeof(IRenderPass).IsAssignableFrom(passType))
                {
                    infos.LogError($"Pass type '{passType.FullName}' must implement {nameof(IRenderPass)}.", passNode);
                    continue;
                }

                ValidateReadWriteBindings(passNode, passType, infos);
            }
        }

        private static void ValidateReadWriteBindings(RenderPassNodeData passNode, System.Type passType, GraphLogger infos)
        {
            var fields = passType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null || !RenderPassPortUtility.CanRead(attr.Access) || !RenderPassPortUtility.CanWrite(attr.Access))
                    continue;

                var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                var inputNode = string.IsNullOrEmpty(inputPortName)
                    ? null
                    : passNode.GetInputPortByName(inputPortName)?.FirstConnectedPort?.GetNode();
                var outputNode = string.IsNullOrEmpty(outputPortName)
                    ? null
                    : passNode.GetOutputPortByName(outputPortName)?.FirstConnectedPort?.GetNode();

                if (inputNode != null && outputNode != null && inputNode != outputNode)
                {
                    infos.LogError(
                        $"Read/write field '{field.Name}' must connect to the same resource node on both input and output ports.",
                        passNode);
                }
            }
        }
    }
}
