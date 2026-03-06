using System;
using System.Collections.Generic;
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

            foreach (var node in graph.GetNodes())
            {
                if (node is not RenderPassNodeData passNode)
                    continue;

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
                    var inputResourceNode = GetConnectedNode(
                        string.IsNullOrEmpty(inputPortName) ? null : passNode.GetInputPortByName(inputPortName));
                    var outputResourceNode = GetConnectedNode(
                        string.IsNullOrEmpty(outputPortName) ? null : passNode.GetOutputPortByName(outputPortName));

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
                            });
                        }
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
                            });
                        }
                    }
                }

                runtimeAsset.Passes.Add(passDef);
            }
        }

        private static INode GetConnectedNode(IPort port)
        {
            if (port == null || !port.IsConnected)
                return null;

            return port.FirstConnectedPort?.GetNode();
        }
    }
}
