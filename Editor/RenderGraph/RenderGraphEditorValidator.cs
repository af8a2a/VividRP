using System.Linq;
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
                }
            }
        }
    }
}
