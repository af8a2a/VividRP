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
                var passType = passNode.GetPassType();
                if (passType == null)
                {
                    if (passNode.UsesPassScriptSelection)
                    {
                        infos.LogError("Select a pass script (a class implementing IRenderPass).", passNode);
                    }
                    else
                    {
                        infos.LogError(
                            $"Registered pass type '{passNode.GetRegisteredPassTypeName()}' could not be resolved.",
                            passNode);
                    }

                    continue;
                }

                if (!typeof(IRenderPass).IsAssignableFrom(passType))
                {
                    infos.LogError($"Pass type '{passType.FullName}' must implement {nameof(IRenderPass)}.", passNode);
                    continue;
                }

                if (passType.IsAbstract)
                {
                    infos.LogError($"Pass type '{passType.FullName}' must be a concrete class.", passNode);
                    continue;
                }

                if (passType.GetConstructor(System.Type.EmptyTypes) == null)
                {
                    infos.LogError(
                        $"Pass type '{passType.FullName}' must expose a public parameterless constructor.",
                        passNode);
                    continue;
                }

                ValidateReadWriteBindings(passNode, passType, infos);
            }

            ValidatePreviewNodes(graph, infos);
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

        private static void ValidatePreviewNodes(RenderGraphEditorGraph graph, GraphLogger infos)
        {
            foreach (var previewNode in graph.GetNodes().OfType<PreviewNodeData>())
            {
                previewNode.RefreshPreviewConnectionMetadata();

                var inputPort = previewNode.GetInputPortByName(PreviewNodeData.TextureInputPortName);
                if (inputPort == null || !inputPort.IsConnected)
                {
                    infos.LogWarning("Preview node is not connected to a texture resource.", previewNode);
                    continue;
                }

                var sourceNode = inputPort.FirstConnectedPort?.GetNode();
                if (sourceNode is TextureResourceNodeData || sourceNode is RenderPassNodeData)
                    continue;

                infos.LogWarning("Preview node only supports texture outputs.", previewNode);
            }
        }
    }
}
