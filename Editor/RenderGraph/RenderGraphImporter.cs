using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [ScriptedImporter(1, RenderGraphEditorGraph.AssetExtension)]
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

            var runtimeAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            Compile(graph, runtimeAsset);

            ctx.AddObjectToAsset("RuntimeAsset", runtimeAsset);
            ctx.SetMainObject(runtimeAsset);
        }

        private static void Compile(RenderGraphEditorGraph graph, RenderGraphData runtimeAsset)
        {
            runtimeAsset.ImportVersion = DateTime.UtcNow.Ticks;
            runtimeAsset.TextureDescriptors.Clear();
            runtimeAsset.BufferDescriptors.Clear();
            runtimeAsset.Passes.Clear();

            var textureNodeToIndex = new Dictionary<TextureResourceNodeData, int>();
            var bufferNodeToIndex = new Dictionary<BufferResourceNodeData, int>();
            var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToList();
            var passNodeToIndex = new Dictionary<RenderPassNodeData, int>();
            for (var index = 0; index < passNodes.Count; index++)
            {
                passNodeToIndex[passNodes[index]] = index;
            }

            foreach (var node in graph.GetNodes())
            {
                if (node is TextureResourceNodeData textureNode)
                {
                    var index = runtimeAsset.TextureDescriptors.Count;
                    textureNodeToIndex.Add(textureNode, index);
                    runtimeAsset.TextureDescriptors.Add(textureNode.GetDescriptor());
                }
                else if (node is BufferResourceNodeData bufferNode)
                {
                    var index = runtimeAsset.BufferDescriptors.Count;
                    bufferNodeToIndex.Add(bufferNode, index);
                    runtimeAsset.BufferDescriptors.Add(bufferNode.GetDescriptor());
                }
            }

            foreach (var node in passNodes)
            {
                var passNode = node;

                var passType = passNode.GetPassType();
                if (passType == null)
                    continue;

                if (!typeof(IRenderPass).IsAssignableFrom(passType))
                    continue;

                var passDef = new RenderGraphPassDefinition
                {
                    PassType = $"{passType.FullName}, {passType.Assembly.GetName().Name}",
                };

                var fields = passType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var attr = field.GetCustomAttribute<RenderGraphResource>();
                    if (attr == null)
                        continue;

                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    var inputPort = string.IsNullOrEmpty(inputPortName) ? null : passNode.GetInputPortByName(inputPortName);
                    var outputPort = string.IsNullOrEmpty(outputPortName) ? null : passNode.GetOutputPortByName(outputPortName);
                    var inputConnectedPort = inputPort?.FirstConnectedPort;
                    var outputConnectedPort = outputPort?.FirstConnectedPort;
                    var inputResourceNode = inputConnectedPort?.GetNode();
                    var outputResourceNode = outputConnectedPort?.GetNode();

                    if (inputResourceNode != null && outputResourceNode != null && inputResourceNode != outputResourceNode)
                    {
                        Debug.LogWarning(
                            $"Render pass field '{field.Name}' on '{passType.FullName}' is connected to different resources on its read/write ports. " +
                            "Skip importing this binding until both ports target the same resource node.");
                        continue;
                    }

                    var resourceNode = RenderPassPortUtility.ResolveConnectedNode(attr.Access, inputResourceNode, outputResourceNode);
                    if (resourceNode == null)
                        continue;

                    if (field.FieldType == typeof(RenderGraphTexture) && resourceNode is TextureResourceNodeData textureNode)
                    {
                        if (textureNodeToIndex.TryGetValue(textureNode, out var resourceIndex))
                        {
                            passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
                            {
                                FieldName = field.Name,
                                ResourceKind = RenderGraphResourceKind.Texture,
                                ResourceIndex = resourceIndex,
                                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            });
                        }

                        continue;
                    }
                    else if (field.FieldType == typeof(RenderGraphBuffer) && resourceNode is BufferResourceNodeData bufferNode)
                    {
                        if (bufferNodeToIndex.TryGetValue(bufferNode, out var resourceIndex))
                        {
                            passDef.ResourceBindings.Add(new RenderGraphPassResourceBinding
                            {
                                FieldName = field.Name,
                                ResourceKind = RenderGraphResourceKind.Buffer,
                                ResourceIndex = resourceIndex,
                                SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            });
                        }

                        continue;
                    }

                    if (field.FieldType == typeof(RenderGraphTexture) && RenderPassPortUtility.CanRead(attr.Access))
                    {
                        TryAddPassFieldBinding(
                            passDef,
                            field.Name,
                            RenderGraphResourceKind.Texture,
                            inputConnectedPort,
                            passNodeToIndex);
                    }
                    else if (field.FieldType == typeof(RenderGraphBuffer) && RenderPassPortUtility.CanRead(attr.Access))
                    {
                        TryAddPassFieldBinding(
                            passDef,
                            field.Name,
                            RenderGraphResourceKind.Buffer,
                            inputConnectedPort,
                            passNodeToIndex);
                    }
                }

                runtimeAsset.Passes.Add(passDef);
            }
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

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var expectedFieldType = resourceKind == RenderGraphResourceKind.Texture
                ? typeof(RenderGraphTexture)
                : typeof(RenderGraphBuffer);

            foreach (var field in passType.GetFields(flags))
            {
                if (field.FieldType != expectedFieldType)
                    continue;

                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                if (string.IsNullOrEmpty(outputPortName))
                    continue;

                var outputPort = passNode.GetOutputPortByName(outputPortName);
                if (ReferenceEquals(outputPort, connectedPort))
                    return field.Name;
            }

            return null;
        }

        private static INode GetConnectedNode(IPort port)
        {
            if (port == null || !port.IsConnected)
                return null;

            return port.FirstConnectedPort?.GetNode();
        }
    }
}
