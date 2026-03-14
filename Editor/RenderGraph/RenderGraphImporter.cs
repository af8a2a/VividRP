using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [ScriptedImporter(4, RenderGraphEditorGraph.AssetExtension)]
    internal sealed class RenderGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var graph = GraphDatabase.LoadGraphForImporter<RenderGraphEditorGraph>(ctx.assetPath);
            if (graph == null)
            {
                Debug.LogError($"Failed to load Render Graph asset: {ctx.assetPath}");
                return;
            }

            var runtimeAsset = ScriptableObject.CreateInstance<VividRP.Runtime.RenderGraphData>();
            runtimeAsset.ImportVersion = DateTime.UtcNow.Ticks;
            RenderGraphCompiler.Compile(graph).ApplyTo(runtimeAsset);

            ctx.AddObjectToAsset("RuntimeAsset", runtimeAsset);
            ctx.SetMainObject(runtimeAsset);
        }

        internal static bool ShouldImportPassFieldBinding(bool hasInputConnection, bool hasBoundResourceNode)
        {
            return RenderGraphCompiler.ShouldImportPassFieldBinding(hasInputConnection, hasBoundResourceNode);
        }

        internal static bool ResolveAsyncComputeSetting(Type passType, bool enableAsyncCompute)
        {
            return RenderGraphCompiler.ResolveAsyncComputeSetting(passType, enableAsyncCompute);
        }
    }

    internal readonly struct RenderGraphCompiledPassInfo
    {
        internal RenderGraphCompiledPassInfo(int executionIndex, string displayName, string passTypeName, bool enableAsyncCompute)
        {
            ExecutionIndex = executionIndex;
            DisplayName = displayName;
            PassTypeName = passTypeName;
            EnableAsyncCompute = enableAsyncCompute;
        }

        internal int ExecutionIndex { get; }
        internal string DisplayName { get; }
        internal string PassTypeName { get; }
        internal bool EnableAsyncCompute { get; }
    }

    internal sealed class RenderGraphCompilationResult
    {
        internal List<RenderGraphTextureDesc> TextureDescriptors { get; } = new List<RenderGraphTextureDesc>();
        internal List<RenderGraphTextureDesc> HistoryTextureDescriptors { get; } = new List<RenderGraphTextureDesc>();
        internal List<RenderGraphBufferDesc> BufferDescriptors { get; } = new List<RenderGraphBufferDesc>();
        internal List<RenderGraphRenderListDesc> RenderListDescriptors { get; } = new List<RenderGraphRenderListDesc>();
        internal List<RenderGraphPassDefinition> Passes { get; } = new List<RenderGraphPassDefinition>();
        internal List<RenderGraphCompiledPassInfo> ExecutionOrder { get; } = new List<RenderGraphCompiledPassInfo>();

        internal void ApplyTo(RenderGraphData runtimeAsset)
        {
            if (runtimeAsset == null)
                return;

            runtimeAsset.TextureDescriptors.Clear();
            runtimeAsset.HistoryTextureDescriptors.Clear();
            runtimeAsset.BufferDescriptors.Clear();
            runtimeAsset.RenderListDescriptors.Clear();
            runtimeAsset.Passes.Clear();

            runtimeAsset.TextureDescriptors.AddRange(TextureDescriptors);
            runtimeAsset.HistoryTextureDescriptors.AddRange(HistoryTextureDescriptors);
            runtimeAsset.BufferDescriptors.AddRange(BufferDescriptors);
            runtimeAsset.RenderListDescriptors.AddRange(RenderListDescriptors);
            runtimeAsset.Passes.AddRange(Passes);
        }
    }

    internal static class RenderGraphCompiler
    {
        internal static RenderGraphCompilationResult Compile(RenderGraphEditorGraph graph)
        {
            var result = new RenderGraphCompilationResult();
            if (graph == null)
                return result;

            var textureNodeToIndex = new Dictionary<TextureResourceNodeData, int>();
            var historyNodeToIndex = new Dictionary<HistoryResourceNodeData, int>();
            var bufferNodeToIndex = new Dictionary<BufferResourceNodeData, int>();
            var renderListNodeToIndex = new Dictionary<RenderListResourceNodeData, int>();
            var texturePortToIndex = new Dictionary<IPort, int>();
            var bufferPortToIndex = new Dictionary<IPort, int>();
            var passNodes = new List<RenderPassNodeData>();
            var passNodeToIndex = new Dictionary<RenderPassNodeData, int>();

            foreach (var passNode in graph.GetNodes().OfType<RenderPassNodeData>())
            {
                var passType = passNode.GetPassType();
                if (passType == null || !typeof(IRenderPass).IsAssignableFrom(passType))
                    continue;

                passNodeToIndex[passNode] = passNodes.Count;
                passNodes.Add(passNode);
            }

            foreach (var node in graph.GetNodes())
            {
                if (node is TextureResourceNodeData textureNode)
                {
                    var index = result.TextureDescriptors.Count;
                    textureNodeToIndex.Add(textureNode, index);
                    result.TextureDescriptors.Add(textureNode.GetDescriptor());
                    AddPortBindingIndex(texturePortToIndex, textureNode.GetOutputPortByName(TextureResourceNodeData.OutputPortName), index);
                }
                else if (node is HistoryResourceNodeData historyNode)
                {
                    var index = result.HistoryTextureDescriptors.Count;
                    historyNodeToIndex.Add(historyNode, index);
                    result.HistoryTextureDescriptors.Add(historyNode.GetDescriptor());
                }
                else if (node is BufferResourceNodeData bufferNode)
                {
                    var index = result.BufferDescriptors.Count;
                    bufferNodeToIndex.Add(bufferNode, index);
                    result.BufferDescriptors.Add(bufferNode.GetDescriptor());
                    AddPortBindingIndex(bufferPortToIndex, bufferNode.GetOutputPortByName(BufferResourceNodeData.OutputPortName), index);
                }
                else if (node is RenderListResourceNodeData renderListNode)
                {
                    var index = result.RenderListDescriptors.Count;
                    renderListNodeToIndex.Add(renderListNode, index);
                    result.RenderListDescriptors.Add(renderListNode.GetDescriptor());
                }
                else if (node is ClassificationResourceNodeData classificationNode)
                {
                    foreach (var (portName, descriptor) in classificationNode.EnumerateBufferDescriptors())
                    {
                        var index = result.BufferDescriptors.Count;
                        result.BufferDescriptors.Add(descriptor);
                        AddPortBindingIndex(bufferPortToIndex, classificationNode.GetOutputPortByName(portName), index);
                    }
                }
            }

            var compiledPassDefinitions = new List<RenderGraphPassDefinition>(passNodes.Count);

            foreach (var passNode in passNodes)
            {
                var passType = passNode.GetPassType();
                if (passType == null)
                    continue;

                var passDefinition = new RenderGraphPassDefinition
                {
                    PassType = $"{passType.FullName}, {passType.Assembly.GetName().Name}",
                    EnableAsyncCompute = ResolveAsyncComputeSetting(passType, passNode.GetEnableAsyncCompute()),
                };

                foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
                {
                    var attr = field.GetCustomAttribute<RenderGraphResource>();
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    var inputPort = string.IsNullOrEmpty(inputPortName) ? null : passNode.GetInputPortByName(inputPortName);
                    var outputPort = string.IsNullOrEmpty(outputPortName) ? null : passNode.GetOutputPortByName(outputPortName);
                    var inputConnectedPort = inputPort?.FirstConnectedPort;
                    var outputConnectedPort = outputPort?.FirstConnectedPort;
                    var connectionKind = GetConnectionKind(inputConnectedPort, outputConnectedPort);
                    var inputResourceNode = GetBindableResourceNode(field.FieldType, inputConnectedPort);
                    var outputResourceNode = GetBindableResourceNode(field.FieldType, outputConnectedPort);

                    if (field.FieldType == typeof(RenderGraphTexture)
                        && TryAddHistoryTextureBinding(
                            passDefinition,
                            field.Name,
                            attr.Access,
                            inputConnectedPort,
                            outputConnectedPort,
                            connectionKind,
                            historyNodeToIndex))
                    {
                        continue;
                    }

                    if (inputResourceNode != null && outputResourceNode != null && inputResourceNode != outputResourceNode)
                    {
                        Debug.LogWarning(
                            $"Render pass field '{field.Name}' on '{passType.FullName}' is connected to different resources on its read/write ports. " +
                            "Skip importing this binding until both ports target the same resource node.");
                        continue;
                    }

                    if (inputResourceNode != null
                        && outputResourceNode != null
                        && inputResourceNode == outputResourceNode
                        && RequiresMatchingStandaloneResourcePorts(inputResourceNode)
                        && !ReferenceEquals(inputConnectedPort, outputConnectedPort))
                    {
                        Debug.LogWarning(
                            $"Render pass field '{field.Name}' on '{passType.FullName}' is connected to different outputs on the same composite resource node. " +
                            "Skip importing this binding until both ports target the same resource output.");
                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphTexture)
                        && TryAddStandaloneResourceBinding(
                            passDefinition,
                            field.Name,
                            RenderGraphResourceKind.Texture,
                            inputConnectedPort,
                            outputConnectedPort,
                            connectionKind,
                            texturePortToIndex))
                    {
                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphBuffer)
                        && TryAddStandaloneResourceBinding(
                            passDefinition,
                            field.Name,
                            RenderGraphResourceKind.Buffer,
                            inputConnectedPort,
                            outputConnectedPort,
                            connectionKind,
                            bufferPortToIndex))
                    {
                        continue;
                    }

                    var resourceNode = inputResourceNode ?? outputResourceNode;
                    if (resourceNode == null)
                    {
                        if (field.FieldType == typeof(RenderGraphTexture)
                            && ShouldImportPassFieldBinding(inputConnectedPort != null, false))
                        {
                            TryAddPassFieldBinding(
                                passDefinition,
                                field.Name,
                                RenderGraphResourceKind.Texture,
                                inputConnectedPort,
                                passNodeToIndex);
                        }
                        else if (field.FieldType == typeof(RenderGraphBuffer)
                                 && ShouldImportPassFieldBinding(inputConnectedPort != null, false))
                        {
                            TryAddPassFieldBinding(
                                passDefinition,
                                field.Name,
                                RenderGraphResourceKind.Buffer,
                                inputConnectedPort,
                                passNodeToIndex);
                        }
                        else if (field.FieldType == typeof(RenderGraphRenderList)
                                 && ShouldImportPassFieldBinding(inputConnectedPort != null, false))
                        {
                            TryAddPassFieldBinding(
                                passDefinition,
                                field.Name,
                                RenderGraphResourceKind.RenderList,
                                inputConnectedPort,
                                passNodeToIndex);
                        }

                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphTexture) && resourceNode is TextureResourceNodeData textureResourceNode)
                    {
                        if (textureNodeToIndex.TryGetValue(textureResourceNode, out var resourceIndex))
                        {
                            passDefinition.ResourceBindings.Add(new RenderGraphPassResourceBinding
                            {
                                FieldName = field.Name,
                                ResourceKind = RenderGraphResourceKind.Texture,
                                ResourceIndex = resourceIndex,
                                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                                ConnectionKind = connectionKind,
                            });
                        }

                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphBuffer) && resourceNode is BufferResourceNodeData bufferResourceNode)
                    {
                        if (bufferNodeToIndex.TryGetValue(bufferResourceNode, out var resourceIndex))
                        {
                            passDefinition.ResourceBindings.Add(new RenderGraphPassResourceBinding
                            {
                                FieldName = field.Name,
                                ResourceKind = RenderGraphResourceKind.Buffer,
                                ResourceIndex = resourceIndex,
                                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                                ConnectionKind = connectionKind,
                            });
                        }

                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphRenderList) && resourceNode is RenderListResourceNodeData renderListResourceNode)
                    {
                        if (renderListNodeToIndex.TryGetValue(renderListResourceNode, out var resourceIndex))
                        {
                            passDefinition.ResourceBindings.Add(new RenderGraphPassResourceBinding
                            {
                                FieldName = field.Name,
                                ResourceKind = RenderGraphResourceKind.RenderList,
                                ResourceIndex = resourceIndex,
                                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                                ConnectionKind = connectionKind,
                            });
                        }
                    }
                }

                compiledPassDefinitions.Add(passDefinition);
            }

            PopulatePreviewTextureFields(graph, compiledPassDefinitions, passNodeToIndex);

            var orderedIndices = RenderGraphPassCompilationUtility.GetOrderedPassIndices(compiledPassDefinitions);
            result.Passes.AddRange(RenderGraphPassCompilationUtility.OrderPassDefinitions(compiledPassDefinitions, orderedIndices));

            for (var compiledIndex = 0; compiledIndex < orderedIndices.Count; compiledIndex++)
            {
                var originalIndex = orderedIndices[compiledIndex];
                if (originalIndex < 0 || originalIndex >= passNodes.Count)
                    continue;

                var passNode = passNodes[originalIndex];
                var passType = passNode.GetPassType();
                var passTypeName = passType?.Name ?? "<Missing Pass>";
                result.ExecutionOrder.Add(new RenderGraphCompiledPassInfo(
                    compiledIndex,
                    GetPassDisplayName(passNode, passTypeName),
                    passTypeName,
                    result.Passes[compiledIndex].EnableAsyncCompute));
            }

            return result;
        }

        internal static bool ShouldImportPassFieldBinding(bool hasInputConnection, bool hasBoundResourceNode)
        {
            return hasInputConnection && !hasBoundResourceNode;
        }

        internal static bool ResolveAsyncComputeSetting(Type passType, bool enableAsyncCompute)
        {
            return enableAsyncCompute && RenderGraphPassExecutionUtility.SupportsAsyncCompute(passType);
        }

        private static string GetPassDisplayName(RenderPassNodeData passNode, string fallbackName)
        {
            var title = passNode?.Title;
            return string.IsNullOrWhiteSpace(title) ? fallbackName : title;
        }

        private static void PopulatePreviewTextureFields(
            RenderGraphEditorGraph graph,
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            IReadOnlyDictionary<RenderPassNodeData, int> passNodeToIndex)
        {
            if (graph == null || passDefinitions == null || passDefinitions.Count == 0)
                return;

            foreach (var previewNode in graph.GetNodes().OfType<PreviewNodeData>())
            {
                var inputPort = previewNode.GetInputPortByName(PreviewNodeData.TextureInputPortName);
                var connectedPort = inputPort?.FirstConnectedPort;
                if (connectedPort?.GetNode() is not RenderPassNodeData sourcePassNode)
                    continue;

                if (!passNodeToIndex.TryGetValue(sourcePassNode, out var sourcePassIndex)
                    || sourcePassIndex < 0
                    || sourcePassIndex >= passDefinitions.Count)
                {
                    continue;
                }

                if (!previewNode.TryGetConnectedPassOutput(out _, out var sourcePreviewKey)
                    || string.IsNullOrEmpty(sourcePreviewKey))
                {
                    continue;
                }

                var previewTextureFields = passDefinitions[sourcePassIndex]?.PreviewTextureFields;
                if (previewTextureFields == null || previewTextureFields.Contains(sourcePreviewKey))
                    continue;

                previewTextureFields.Add(sourcePreviewKey);
            }
        }

        private static bool TryAddHistoryTextureBinding(
            RenderGraphPassDefinition passDef,
            string targetFieldName,
            AccessFlags access,
            IPort inputConnectedPort,
            IPort outputConnectedPort,
            RenderGraphPassBindingConnectionKind connectionKind,
            IReadOnlyDictionary<HistoryResourceNodeData, int> historyNodeToIndex)
        {
            var hasInputHistory = TryGetHistoryBindingReference(
                inputConnectedPort,
                historyNodeToIndex,
                out var inputHistoryIndex,
                out var inputVariant);
            var hasOutputHistory = TryGetHistoryBindingReference(
                outputConnectedPort,
                historyNodeToIndex,
                out var outputHistoryIndex,
                out var outputVariant);

            if (!hasInputHistory && !hasOutputHistory)
                return false;

            var canRead = RenderPassPortUtility.CanRead(access);
            var canWrite = RenderPassPortUtility.CanWrite(access);

            if (canRead && canWrite)
            {
                if (hasInputHistory
                    && outputConnectedPort == null
                    && inputVariant == RenderGraphResourceBindingVariant.HistoryCurrent)
                {
                    passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
                    {
                        FieldName = targetFieldName,
                        ResourceKind = RenderGraphResourceKind.Texture,
                        ResourceIndex = inputHistoryIndex,
                        ResourceBindingVariant = RenderGraphResourceBindingVariant.HistoryCurrent,
                        SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        ConnectionKind = connectionKind,
                    });
                }

                return true;
            }

            if (canRead && hasInputHistory)
            {
                passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
                {
                    FieldName = targetFieldName,
                    ResourceKind = RenderGraphResourceKind.Texture,
                    ResourceIndex = inputHistoryIndex,
                    ResourceBindingVariant = inputVariant,
                    SourceKind = RenderGraphPassBindingSourceKind.Resource,
                    ConnectionKind = connectionKind,
                });
                return true;
            }

            if (canWrite && (hasInputHistory || hasOutputHistory))
                return true;

            return true;
        }

        private static bool TryGetHistoryBindingReference(
            IPort connectedPort,
            IReadOnlyDictionary<HistoryResourceNodeData, int> historyNodeToIndex,
            out int historyIndex,
            out RenderGraphResourceBindingVariant variant)
        {
            historyIndex = -1;
            variant = RenderGraphResourceBindingVariant.Default;

            if (connectedPort?.GetNode() is not HistoryResourceNodeData historyNode)
                return false;

            if (!historyNodeToIndex.TryGetValue(historyNode, out historyIndex))
                return false;

            if (historyNode.IsPreviousOutputPort(connectedPort))
            {
                variant = RenderGraphResourceBindingVariant.HistoryPrevious;
                return true;
            }

            if (historyNode.IsCurrentOutputPort(connectedPort))
            {
                variant = RenderGraphResourceBindingVariant.HistoryCurrent;
                return true;
            }

            historyIndex = -1;
            return false;
        }

        private static bool TryAddStandaloneResourceBinding(
            RenderGraphPassDefinition passDef,
            string targetFieldName,
            RenderGraphResourceKind resourceKind,
            IPort inputConnectedPort,
            IPort outputConnectedPort,
            RenderGraphPassBindingConnectionKind connectionKind,
            IReadOnlyDictionary<IPort, int> portToIndex)
        {
            if (!TryGetStandaloneResourceIndex(inputConnectedPort, outputConnectedPort, portToIndex, out var resourceIndex))
                return false;

            passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
            {
                FieldName = targetFieldName,
                ResourceKind = resourceKind,
                ResourceIndex = resourceIndex,
                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                ConnectionKind = connectionKind,
            });
            return true;
        }

        private static RenderGraphPassBindingConnectionKind GetConnectionKind(IPort inputConnectedPort, IPort outputConnectedPort)
        {
            var connectionKind = RenderGraphPassBindingConnectionKind.None;

            if (inputConnectedPort != null)
                connectionKind |= RenderGraphPassBindingConnectionKind.Input;

            if (outputConnectedPort != null)
                connectionKind |= RenderGraphPassBindingConnectionKind.Output;

            return connectionKind;
        }

        private static bool TryGetStandaloneResourceIndex(
            IPort inputConnectedPort,
            IPort outputConnectedPort,
            IReadOnlyDictionary<IPort, int> portToIndex,
            out int resourceIndex)
        {
            resourceIndex = -1;

            if (inputConnectedPort == null && outputConnectedPort == null)
                return false;

            if (inputConnectedPort != null && outputConnectedPort != null)
            {
                if (!ReferenceEquals(inputConnectedPort, outputConnectedPort))
                    return false;

                return portToIndex.TryGetValue(inputConnectedPort, out resourceIndex);
            }

            var connectedPort = inputConnectedPort ?? outputConnectedPort;
            return connectedPort != null && portToIndex.TryGetValue(connectedPort, out resourceIndex);
        }

        private static void AddPortBindingIndex(IDictionary<IPort, int> portToIndex, IPort port, int resourceIndex)
        {
            if (port == null)
                return;

            portToIndex[port] = resourceIndex;
        }

        private static object GetBindableResourceNode(Type fieldType, IPort connectedPort)
        {
            var connectedNode = connectedPort?.GetNode();
            if (connectedNode == null)
                return null;

            if (fieldType == typeof(RenderGraphTexture) && connectedNode is TextureResourceNodeData textureNode)
                return textureNode;

            if (fieldType == typeof(RenderGraphBuffer) && connectedNode is BufferResourceNodeData bufferNode)
                return bufferNode;

            if (fieldType == typeof(RenderGraphBuffer) && connectedNode is ClassificationResourceNodeData classificationNode)
                return classificationNode;

            if (fieldType == typeof(RenderGraphRenderList) && connectedNode is RenderListResourceNodeData renderListNode)
                return renderListNode;

            return null;
        }

        private static bool RequiresMatchingStandaloneResourcePorts(object node)
        {
            return node is ClassificationResourceNodeData;
        }

        private static void TryAddPassFieldBinding(
            RenderGraphPassDefinition passDef,
            string targetFieldName,
            RenderGraphResourceKind resourceKind,
            IPort connectedPort,
            IReadOnlyDictionary<RenderPassNodeData, int> passNodeToIndex)
        {
            if (connectedPort?.GetNode() is not RenderPassNodeData sourcePassNode)
                return;

            if (!passNodeToIndex.TryGetValue(sourcePassNode, out var sourcePassIndex))
                return;

            var sourcePassType = sourcePassNode.GetPassType();
            var sourceFieldName = GetConnectedOutputFieldName(sourcePassNode, sourcePassType, connectedPort, resourceKind);
            if (string.IsNullOrEmpty(sourceFieldName))
                return;

            passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
            {
                FieldName = targetFieldName,
                ResourceKind = resourceKind,
                SourceKind = RenderGraphPassBindingSourceKind.PassField,
                ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                SourcePassIndex = sourcePassIndex,
                SourceFieldName = sourceFieldName,
            });
        }

        private static string GetConnectedOutputFieldName(
            RenderPassNodeData passNode,
            Type passType,
            IPort connectedPort,
            RenderGraphResourceKind resourceKind)
        {
            if (passNode == null || passType == null || connectedPort == null)
                return null;

            var expectedFieldType = resourceKind switch
            {
                RenderGraphResourceKind.Texture => typeof(RenderGraphTexture),
                RenderGraphResourceKind.Buffer => typeof(RenderGraphBuffer),
                RenderGraphResourceKind.RenderList => typeof(RenderGraphRenderList),
                _ => null,
            };

            if (expectedFieldType == null)
                return null;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                if (field.FieldType != expectedFieldType)
                    continue;

                var attr = field.GetCustomAttribute<RenderGraphResource>();
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                if (string.IsNullOrEmpty(outputPortName))
                    continue;

                var outputPort = passNode.GetOutputPortByName(outputPortName);
                if (ReferenceEquals(outputPort, connectedPort))
                    return field.Name;
            }

            return null;
        }
    }
}
