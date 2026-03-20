using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine.Experimental.Rendering;
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

                ValidateAsyncCompute(passNode, passType, infos);
                ValidateReadWriteBindings(passNode, passType, infos);
                ValidateHistoryBindings(passNode, passType, infos);
            }

            ValidateHistoryResourceNodes(graph, infos);
            ValidatePreviewNodes(graph, infos);
        }

        private static void ValidateReadWriteBindings(RenderPassNodeData passNode, System.Type passType, GraphLogger infos)
        {
            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (!RenderPassPortUtility.CanRead(attr.Access) || !RenderPassPortUtility.CanWrite(attr.Access))
                    continue;

                var inputPortName = passNode.GetInputPortName(field, attr);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                var inputNode = string.IsNullOrEmpty(inputPortName)
                    ? null
                    : passNode.GetInputPortByName(inputPortName)?.FirstConnectedPort?.GetNode();
                var outputNode = string.IsNullOrEmpty(outputPortName)
                    ? null
                    : passNode.GetOutputPortByName(outputPortName)?.FirstConnectedPort?.GetNode();

                var inputResourceNode = IsStandaloneResourceNode(inputNode) ? inputNode : null;
                var outputResourceNode = IsStandaloneResourceNode(outputNode) ? outputNode : null;
                if (inputResourceNode != null && outputResourceNode != null && inputResourceNode != outputResourceNode)
                {
                    infos.LogError(
                        $"Read/write field '{field.Name}' must connect to the same resource node on both input and output ports.",
                        passNode);
                }

                if (inputResourceNode != null
                    && outputResourceNode != null
                    && inputResourceNode == outputResourceNode
                    && !ReferenceEquals(passNode.GetInputPortByName(inputPortName)?.FirstConnectedPort, passNode.GetOutputPortByName(outputPortName)?.FirstConnectedPort))
                {
                    infos.LogError(
                        $"Read/write field '{field.Name}' must connect to the same resource output on composite resource nodes.",
                        passNode);
                }
            }
        }

        private static void ValidateAsyncCompute(RenderPassNodeData passNode, System.Type passType, GraphLogger infos)
        {
            var enableAsyncCompute = passNode.GetEnableAsyncCompute();
            if (IsAsyncComputeConfigurationValid(passType, enableAsyncCompute))
                return;

            infos.LogError(
                $"Async Compute can only be enabled on {nameof(ComputePass)} or {nameof(UnsafePass)} types that implement {nameof(IAsyncComputeSupportedPass)}. Disable Async Compute or reselect a supported pass.",
                passNode);
        }

        private static void ValidateHistoryBindings(RenderPassNodeData passNode, System.Type passType, GraphLogger infos)
        {
            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (field.FieldType != typeof(RenderGraphTexture))
                    continue;

                var inputPortName = passNode.GetInputPortName(field, attr);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                var inputConnectedPort = string.IsNullOrEmpty(inputPortName)
                    ? null
                    : passNode.GetInputPortByName(inputPortName)?.FirstConnectedPort;
                var outputConnectedPort = string.IsNullOrEmpty(outputPortName)
                    ? null
                    : passNode.GetOutputPortByName(outputPortName)?.FirstConnectedPort;
                var inputHistoryNode = inputConnectedPort?.GetNode() as HistoryResourceNodeData;
                var outputHistoryNode = outputConnectedPort?.GetNode() as HistoryResourceNodeData;

                if (inputHistoryNode == null && outputHistoryNode == null)
                    continue;

                var canRead = RenderPassPortUtility.CanRead(attr.Access);
                var canWrite = RenderPassPortUtility.CanWrite(attr.Access);

                if (canRead && canWrite)
                {
                    var isValidCurrentHistoryBinding = inputHistoryNode != null
                        && outputConnectedPort == null
                        && inputHistoryNode.IsCurrentOutputPort(inputConnectedPort);

                    if (!isValidCurrentHistoryBinding)
                    {
                        infos.LogError(
                            $"Read/write field '{field.Name}' must connect only its input port to CurrOut on a history node and leave the output port unconnected.",
                            passNode);
                    }

                    continue;
                }

                if (canRead && inputHistoryNode != null && !inputHistoryNode.IsPreviousOutputPort(inputConnectedPort) && !inputHistoryNode.IsCurrentOutputPort(inputConnectedPort))
                {
                    infos.LogError($"Read field '{field.Name}' must connect to PrevOut or CurrOut on a history node.", passNode);
                }

                if (canWrite && (inputHistoryNode != null || outputHistoryNode != null))
                {
                    infos.LogError(
                        $"Write-only field '{field.Name}' cannot bind directly to a history node. Use a ReadWrite field and connect its input port to CurrOut instead.",
                        passNode);
                }
            }
        }

        private static bool IsStandaloneResourceNode(INode node)
        {
            return node is TextureResourceNodeData
                   || node is BufferResourceNodeData
                   || node is RenderListResourceNodeData
                   || node is AccelerationStructureResourceNodeData;
        }


        private static void ValidateHistoryResourceNodes(RenderGraphEditorGraph graph, GraphLogger infos)
        {
            foreach (var historyNode in graph.GetNodes().OfType<HistoryResourceNodeData>())
            {
                var desc = historyNode.GetDescriptor();
                if (desc == null || desc.ColorFormat == GraphicsFormat.None)
                {
                    infos.LogError("History resource requires a valid color format.", historyNode);
                }
            }
        }

        private static void ValidatePreviewNodes(RenderGraphEditorGraph graph, GraphLogger infos)
        {
            var debug = graph.GetNodes().OfType<PreviewNodeData>();
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
                if (sourceNode is TextureResourceNodeData || sourceNode is HistoryResourceNodeData || sourceNode is RenderPassNodeData )
                    continue;

                infos.LogWarning("Preview node only supports texture outputs.", previewNode);
            }
        }

        internal static bool IsAsyncComputeConfigurationValid(System.Type passType, bool enableAsyncCompute)
        {
            return !enableAsyncCompute || RenderGraphPassExecutionUtility.SupportsAsyncCompute(passType);
        }
    }
}
