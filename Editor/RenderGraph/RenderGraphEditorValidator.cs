using System.Collections.Generic;
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
            if (graph == null || infos == null)
                return;

            ValidateGraph(graph, new GraphLoggerValidationReporter(infos), summarizeChildSubgraphs: true);
        }

        private static ValidationSummary ValidateGraph(
            RenderGraphEditorGraph graph,
            IRenderGraphValidationReporter reporter,
            bool summarizeChildSubgraphs)
        {
            var summary = new ValidationSummary();
            if (graph == null || reporter == null)
                return summary;

            var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToList();
            var subgraphNodes = graph.GetNodes().OfType<ISubgraphNode>().ToList();
            if (passNodes.Count == 0 && subgraphNodes.Count == 0)
            {
                reporter.LogWarning("Add at least one pass node to your Render Graph.", graph);
                summary.WarningCount++;
            }

            foreach (var passNode in passNodes)
            {
                if (IsCorruptedNode(passNode))
                {
                    reporter.LogError("This pass node is corrupted. Delete it and re-create.", passNode);
                    summary.ErrorCount++;
                    continue;
                }

                var passType = passNode.GetPassType();
                if (passType == null)
                {
                    if (passNode.UsesPassScriptSelection)
                    {
                        reporter.LogError("Select a pass script (a class implementing IRenderPass).", passNode);
                    }
                    else
                    {
                        reporter.LogError(
                            $"Registered pass type '{passNode.GetRegisteredPassTypeName()}' could not be resolved.",
                            passNode);
                    }

                    summary.ErrorCount++;
                    continue;
                }

                if (!typeof(IRenderPass).IsAssignableFrom(passType))
                {
                    reporter.LogError($"Pass type '{passType.FullName}' must implement {nameof(IRenderPass)}.", passNode);
                    summary.ErrorCount++;
                    continue;
                }

                if (passType.IsAbstract)
                {
                    reporter.LogError($"Pass type '{passType.FullName}' must be a concrete class.", passNode);
                    summary.ErrorCount++;
                    continue;
                }

                if (passType.GetConstructor(System.Type.EmptyTypes) == null)
                {
                    reporter.LogError(
                        $"Pass type '{passType.FullName}' must expose a public parameterless constructor.",
                        passNode);
                    summary.ErrorCount++;
                    continue;
                }

                ValidateAsyncCompute(passNode, passType, reporter, ref summary);
                ValidateReadWriteBindings(passNode, passType, reporter, ref summary);
                ValidateHistoryBindings(passNode, passType, reporter, ref summary);
            }

            ValidateHistoryResourceNodes(graph, reporter, ref summary);
            ValidateSubSystemInterfaceVariables(graph, reporter, ref summary);

            ValidatePreviewNodes(graph, reporter, ref summary);
            ValidateSubgraphNodes(graph, reporter, summarizeChildSubgraphs, ref summary);

            return summary;
        }

        private static void ValidateReadWriteBindings(
            RenderPassNodeData passNode,
            System.Type passType,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (!RenderPassPortUtility.CanRead(attr.Access) || !RenderPassPortUtility.CanWrite(attr.Access))
                    continue;

                var inputPortName = passNode.GetInputPortName(field, attr);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access, attr.BindingMode);
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
                    reporter.LogError(
                        $"Read/write field '{field.Name}' must connect to the same resource node on both input and output ports.",
                        passNode);
                    summary.ErrorCount++;
                }

                if (inputResourceNode != null
                    && outputResourceNode != null
                    && inputResourceNode == outputResourceNode
                    && !ReferenceEquals(passNode.GetInputPortByName(inputPortName)?.FirstConnectedPort, passNode.GetOutputPortByName(outputPortName)?.FirstConnectedPort))
                {
                    reporter.LogError(
                        $"Read/write field '{field.Name}' must connect to the same resource output on composite resource nodes.",
                        passNode);
                    summary.ErrorCount++;
                }
            }
        }

        private static void ValidateAsyncCompute(
            RenderPassNodeData passNode,
            System.Type passType,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            var enableAsyncCompute = passNode.GetEnableAsyncCompute();
            if (IsAsyncComputeConfigurationValid(passType, enableAsyncCompute))
                return;

            reporter.LogError(
                $"Async Compute can only be enabled on {nameof(ComputePass)} or {nameof(UnsafePass)} types that implement {nameof(IAsyncComputeSupportedPass)}. Disable Async Compute or reselect a supported pass.",
                passNode);
            summary.ErrorCount++;
        }

        private static void ValidateHistoryBindings(
            RenderPassNodeData passNode,
            System.Type passType,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (field.FieldType != typeof(RenderGraphTexture))
                    continue;

                var inputPortName = passNode.GetInputPortName(field, attr);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access, attr.BindingMode);
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
                        reporter.LogError(
                            $"Read/write field '{field.Name}' must connect only its input port to CurrOut on a history node and leave the output port unconnected.",
                            passNode);
                        summary.ErrorCount++;
                    }

                    continue;
                }

                if (canRead && inputHistoryNode != null && !inputHistoryNode.IsPreviousOutputPort(inputConnectedPort) && !inputHistoryNode.IsCurrentOutputPort(inputConnectedPort))
                {
                    reporter.LogError($"Read field '{field.Name}' must connect to PrevOut or CurrOut on a history node.", passNode);
                    summary.ErrorCount++;
                }

                if (canWrite && (inputHistoryNode != null || outputHistoryNode != null))
                {
                    reporter.LogError(
                        $"Write-only field '{field.Name}' cannot bind directly to a history node. Use a ReadWrite field and connect its input port to CurrOut instead.",
                        passNode);
                    summary.ErrorCount++;
                }
            }
        }

        private static void ValidateSubSystemInterfaceVariables(
            RenderGraphEditorGraph graph,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            if (!RenderGraphSubSystemCompilationUtility.IsSubSystemGraph(graph))
                return;

            foreach (var variable in graph.GetVariables())
            {
                if (variable == null)
                    continue;

                var kind = variable.VariableKind;
                if (kind != VariableKind.Input && kind != VariableKind.Output)
                    continue;

                if (!IsSupportedSubSystemInterfaceType(variable.DataType))
                {
                    reporter.LogError(
                        $"SubSystem interface variable '{variable.Name}' uses unsupported type '{variable.DataType?.FullName ?? "<null>"}'.",
                        variable);
                    summary.ErrorCount++;
                }

                if (kind == VariableKind.Output && GetDistinctConnectedInputCount(variable) > 1)
                {
                    reporter.LogError(
                        $"SubSystem output variable '{variable.Name}' must have a single internal source.",
                        variable);
                    summary.ErrorCount++;
                }
            }
        }

        private static void ValidateSubgraphNodes(
            RenderGraphEditorGraph graph,
            IRenderGraphValidationReporter reporter,
            bool summarizeChildSubgraphs,
            ref ValidationSummary summary)
        {
            var subgraphNodes = graph.GetNodes().OfType<ISubgraphNode>().ToList();
            if (subgraphNodes.Count == 0)
                return;

            if (RenderGraphSubSystemCompilationUtility.IsSubSystemGraph(graph))
            {
                foreach (var subgraphNode in subgraphNodes)
                {
                    reporter.LogError("SubSystem graphs cannot contain nested SubSystems.", subgraphNode);
                    summary.ErrorCount++;
                }

                return;
            }

            foreach (var subgraphNode in subgraphNodes)
            {
                if (subgraphNode.GetSubgraph() is not RenderGraphSubSystemGraph subSystemGraph)
                {
                    reporter.LogError("Only VividRP RenderGraph SubSystem graphs are supported inside RenderGraphEditor.", subgraphNode);
                    summary.ErrorCount++;
                    continue;
                }

                if (!summarizeChildSubgraphs)
                    continue;

                var childReporter = new CollectingValidationReporter();
                var childSummary = ValidateGraph(subSystemGraph, childReporter, summarizeChildSubgraphs: false);
                if (childSummary.ErrorCount > 0)
                {
                    reporter.LogError(
                        $"SubSystem contains {childSummary.ErrorCount} error(s). Open the SubSystem to inspect details.",
                        subgraphNode);
                    summary.ErrorCount++;
                }
                else if (childSummary.WarningCount > 0)
                {
                    reporter.LogWarning(
                        $"SubSystem contains {childSummary.WarningCount} warning(s). Open the SubSystem to inspect details.",
                        subgraphNode);
                    summary.WarningCount++;
                }
            }
        }

        private static bool IsSupportedSubSystemInterfaceType(System.Type type)
        {
            return type == typeof(RenderGraphTexture)
                   || type == typeof(RenderGraphBuffer)
                   || type == typeof(RenderGraphRenderList)
                   || type == typeof(RenderGraphAccelerationStructure);
        }

        private static int GetDistinctConnectedInputCount(IVariable variable)
        {
            if (variable == null)
                return 0;

            var variableNodes = new List<IVariableNode>();
            variable.GetNodes(variableNodes);

            var connectedPorts = new HashSet<IPort>(ReferenceEqualityComparer<IPort>.Instance);
            foreach (var variableNode in variableNodes)
            {
                foreach (var inputPort in variableNode.GetInputPorts())
                {
                    if (inputPort?.IsConnected == true && inputPort.FirstConnectedPort != null)
                        connectedPorts.Add(inputPort.FirstConnectedPort);
                }
            }

            return connectedPorts.Count;
        }

        private static bool IsStandaloneResourceNode(INode node)
        {
            return node is TextureResourceNodeData
                   || node is BufferResourceNodeData
                   || node is RenderListResourceNodeData
                   || node is AccelerationStructureResourceNodeData;
        }

        private static void ValidateHistoryResourceNodes(
            RenderGraphEditorGraph graph,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            foreach (var historyNode in graph.GetNodes().OfType<HistoryResourceNodeData>())
            {
                var desc = historyNode.GetDescriptor();
                if (desc == null || desc.ColorFormat == GraphicsFormat.None)
                {
                    reporter.LogError("History resource requires a valid color format.", historyNode);
                    summary.ErrorCount++;
                }
            }
        }

        private static void ValidatePreviewNodes(
            RenderGraphEditorGraph graph,
            IRenderGraphValidationReporter reporter,
            ref ValidationSummary summary)
        {
            if (graph == null)
                return;

            foreach (var previewNode in graph.GetNodes().OfType<PreviewNodeData>())
            {
                reporter.LogError(
                    "Preview Node has been removed from VividRP RenderGraph. Delete this node and use camera-aware debugging tools instead.",
                    previewNode);
                summary.ErrorCount++;
            }
        }

        internal static bool IsAsyncComputeConfigurationValid(System.Type passType, bool enableAsyncCompute)
        {
            return !enableAsyncCompute || RenderGraphPassExecutionUtility.SupportsAsyncCompute(passType);
        }

        internal static bool IsCorruptedNode(RenderPassNodeData passNode)
        {
            if (passNode == null)
                return true;

            try
            {
                if (!passNode.UsesPassScriptSelection)
                {
                    var typeName = passNode.GetRegisteredPassTypeName();
                    if (string.IsNullOrEmpty(typeName))
                        return true;
                }

                passNode.GetPassType();
                return false;
            }
            catch
            {
                return true;
            }
        }

        internal static int TrimGraph(RenderGraphEditorGraph graph)
        {
            if (graph == null)
                return 0;

            var removed = TrimGraphRecursive(graph);
            return removed;
        }

        private static int TrimGraphRecursive(RenderGraphEditorGraph graph)
        {
            if (graph == null)
                return 0;

            var removed = 0;
            var allNodes = graph.GetNodes().ToList();

            foreach (var passNode in allNodes.OfType<RenderPassNodeData>().ToList())
            {
                if (!IsCorruptedNode(passNode))
                    continue;

                graph.RemoveNode(passNode);
                removed++;
            }

            foreach (var previewNode in graph.GetNodes().OfType<PreviewNodeData>().ToList())
            {
                graph.RemoveNode(previewNode);
                removed++;
            }

            allNodes = graph.GetNodes().ToList();
            foreach (var node in allNodes)
            {
                if (node is RenderPassNodeData)
                    continue;

                if (!IsDisconnectedResourceNode(node as Node))
                    continue;

                graph.RemoveNode(node);
                removed++;
            }

            foreach (var subgraphNode in graph.GetNodes().OfType<ISubgraphNode>())
            {
                if (subgraphNode.GetSubgraph() is RenderGraphEditorGraph childGraph)
                    removed += TrimGraphRecursive(childGraph);
            }

            return removed;
        }

        private static bool IsDisconnectedResourceNode(Node node)
        {
            if (node == null)
                return false;

            string[] portNames;
            switch (node)
            {
                case TextureResourceNodeData:
                    portNames = new[] { TextureResourceNodeData.InputPortName, TextureResourceNodeData.OutputPortName };
                    break;
                case BufferResourceNodeData:
                    portNames = new[] { BufferResourceNodeData.InputPortName, BufferResourceNodeData.OutputPortName };
                    break;
                case RenderListResourceNodeData:
                    portNames = new[] { RenderListResourceNodeData.InputPortName, RenderListResourceNodeData.OutputPortName };
                    break;
                case HistoryResourceNodeData:
                    portNames = new[] { HistoryResourceNodeData.PreviousOutputPortName, HistoryResourceNodeData.CurrentOutputPortName };
                    break;
                case AccelerationStructureResourceNodeData:
                    portNames = new[] { AccelerationStructureResourceNodeData.InputPortName, AccelerationStructureResourceNodeData.OutputPortName };
                    break;
                default:
                    return false;
            }

            foreach (var portName in portNames)
            {
                var port = node.GetInputPortByName(portName) ?? node.GetOutputPortByName(portName);
                if (port != null && port.IsConnected)
                    return false;
            }

            return true;
        }

        private struct ValidationSummary
        {
            internal int ErrorCount;
            internal int WarningCount;
        }

        private interface IRenderGraphValidationReporter
        {
            void LogError(string message, object context);
            void LogWarning(string message, object context);
        }

        private sealed class GraphLoggerValidationReporter : IRenderGraphValidationReporter
        {
            private readonly GraphLogger m_Logger;

            internal GraphLoggerValidationReporter(GraphLogger logger)
            {
                m_Logger = logger;
            }

            public void LogError(string message, object context)
            {
                m_Logger?.LogError(message, context);
            }

            public void LogWarning(string message, object context)
            {
                m_Logger?.LogWarning(message, context);
            }
        }

        private sealed class CollectingValidationReporter : IRenderGraphValidationReporter
        {
            public void LogError(string message, object context)
            {
            }

            public void LogWarning(string message, object context)
            {
            }
        }
    }
}
