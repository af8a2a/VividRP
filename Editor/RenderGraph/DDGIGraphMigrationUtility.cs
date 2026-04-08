// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Reflection;
// using Unity.GraphToolkit.Editor;
// using UnityEditor;
// using UnityEngine;
// using UnityEngine.Rendering.RenderGraphModule;
// using VividRP.Runtime;
// using RuntimeDDGIProbeBlendPass = VividRP.Runtime.RenderPass.Core.DDGIProbeBlendPass;
// using RuntimeDDGIProbeTracePass = VividRP.Runtime.RenderPass.Core.DDGIProbeTracePass;
// using RuntimeDDGIRTASBuildPass = VividRP.Runtime.RenderPass.Core.DDGIRTASBuildPass;
// using RuntimeDeferredDirectionalLightingPass = VividRP.Runtime.RenderPass.Core.DeferredDirectionalLightingPass;
// using RuntimeDeferredLightingPass = VividRP.Runtime.RenderPass.Core.DeferredLightingPass;
// using NodeDDGIProbeBlendPass = VividRP.Editor.RenderGraph.Generated.DDGIProbeBlendPass;
// using NodeDDGIProbeTracePass = VividRP.Editor.RenderGraph.Generated.DDGIProbeTracePass;
// using NodeDDGIRTASBuildPass = VividRP.Editor.RenderGraph.Generated.DDGIRTASBuildPass;
// using NodeDeferredLightingPass = VividRP.Editor.RenderGraph.Generated.DeferredLightingPass;
//
// namespace VividRP.Editor.RenderGraph
// {
//     public static class DDGIGraphMigrationUtility
//     {
//         private const string DefaultGraphAssetPath = "Assets/Vivid Render Graph.vrdg";
//         private const string LightingSubgraphTitle = "Lighting";
//         private static readonly FieldInfo s_GraphImplementationField = typeof(Graph)
//             .GetField("m_Implementation", BindingFlags.Instance | BindingFlags.NonPublic);
//         private static readonly Type s_GraphImplementationType = Type.GetType(
//             "Unity.GraphToolkit.Editor.Implementation.GraphModelImp, UnityEditor.GraphToolkitModule",
//             throwOnError: false);
//         private static readonly MethodInfo s_CreateNodeModelMethod = s_GraphImplementationType?.GetMethod(
//             "CreateNodeModel",
//             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
//             null,
//             new[] { typeof(Node), typeof(Vector2) },
//             null);
//
//         
//         [MenuItem("Assets/VividRP/Apply DDGI V1 Graph Migration", false)]
//         private static void ApplyToSelectedGraph()
//         {
//             string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
//             if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith($".{RenderGraphEditorGraph.AssetExtension}", StringComparison.OrdinalIgnoreCase))
//             {
//                 Debug.LogWarning("[VividRP] Select a .vrdg Render Graph asset to apply the DDGI v1 migration.");
//                 return;
//             }
//
//             ApplyToGraphAsset(assetPath);
//         }
//
//         [MenuItem("Assets/VividRP/Apply DDGI V1 Graph Migration", true)]
//         private static bool ApplyToSelectedGraphValidation()
//         {
//             string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
//             return !string.IsNullOrEmpty(assetPath)
//                 && assetPath.EndsWith($".{RenderGraphEditorGraph.AssetExtension}", StringComparison.OrdinalIgnoreCase);
//         }
//
//         public static void ApplyDefaultGraphMigrationBatch()
//         {
//             ApplyToGraphAsset(DefaultGraphAssetPath);
//         }
//
//         internal static void ApplyToGraphAsset(string assetPath)
//         {
//             if (string.IsNullOrEmpty(assetPath))
//             {
//                 throw new ArgumentException("Graph asset path must be provided.", nameof(assetPath));
//             }
//
//             RenderGraphEditorGraph rootGraph = GraphDatabase.LoadGraphForImporter<RenderGraphEditorGraph>(assetPath);
//             if (rootGraph == null)
//             {
//                 throw new InvalidOperationException($"Failed to load Render Graph at '{assetPath}'.");
//             }
//
//             RenderGraphSubSystemGraph lightingGraph = FindLightingGraph(rootGraph);
//             if (lightingGraph == null)
//             {
//                 throw new InvalidOperationException($"Failed to find the '{LightingSubgraphTitle}' subgraph in '{assetPath}'.");
//             }
//
//             Undo.RecordObject(rootGraph, "Apply DDGI V1 graph migration");
//             Undo.RecordObject(lightingGraph, "Apply DDGI V1 graph migration");
//
//             RenderPassNodeData sourceDeferredNode = FindDeferredNode(lightingGraph);
//             if (sourceDeferredNode == null)
//             {
//                 throw new InvalidOperationException($"Failed to find a deferred lighting node in '{assetPath}'.");
//             }
//
//             NodeDDGIRTASBuildPass ddgiBuildNode = FindPassNode<NodeDDGIRTASBuildPass>(lightingGraph)
//                 ?? AddPassNode(lightingGraph, new NodeDDGIRTASBuildPass(), new Vector2(2080.0f, 600.0f));
//             NodeDDGIProbeTracePass ddgiTraceNode = FindPassNode<NodeDDGIProbeTracePass>(lightingGraph)
//                 ?? AddPassNode(lightingGraph, new NodeDDGIProbeTracePass(), new Vector2(2330.0f, 600.0f));
//             NodeDDGIProbeBlendPass ddgiBlendNode = FindPassNode<NodeDDGIProbeBlendPass>(lightingGraph)
//                 ?? AddPassNode(lightingGraph, new NodeDDGIProbeBlendPass(), new Vector2(2580.0f, 600.0f));
//             NodeDeferredLightingPass deferredNode = AddPassNode(lightingGraph, new NodeDeferredLightingPass(), new Vector2(2830.0f, 340.0f));
//
//             CopyPassConnections(lightingGraph, sourceDeferredNode, deferredNode);
//
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiBuildNode.GetOutputPortByName("m_DDGIAccelerationStructure"),
//                 ddgiTraceNode.GetInputPortByName("m_DDGIAccelerationStructure"));
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiTraceNode.GetOutputPortByName("m_ProbeRayData"),
//                 ddgiBlendNode.GetInputPortByName("m_ProbeRayData"));
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiTraceNode.GetOutputPortByName("m_ProbeData"),
//                 ddgiBlendNode.GetInputPortByName("m_ProbeData"));
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiBlendNode.GetOutputPortByName("m_ProbeIrradiance"),
//                 deferredNode.GetInputPortByName("m_DDGIProbeIrradiance"));
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiBlendNode.GetOutputPortByName("m_ProbeDistance"),
//                 deferredNode.GetInputPortByName("m_DDGIProbeDistance"));
//             EnsureConnection(
//                 lightingGraph,
//                 ddgiTraceNode.GetOutputPortByName("m_ProbeData"),
//                 deferredNode.GetInputPortByName("m_DDGIProbeData"));
//
//             lightingGraph.RemoveNode(sourceDeferredNode);
//
//             ValidateCompiledOrder(rootGraph, assetPath);
//
//             EditorUtility.SetDirty(lightingGraph);
//             EditorUtility.SetDirty(rootGraph);
//             AssetDatabase.SaveAssetIfDirty(rootGraph);
//             AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
//
//             Debug.Log($"[VividRP] Applied DDGI v1 graph migration to '{assetPath}'.");
//         }
//
//         private static void ValidateCompiledOrder(RenderGraphEditorGraph rootGraph, string assetPath)
//         {
//             RenderGraphCompilationResult result = RenderGraphCompiler.Compile(rootGraph);
//             List<string> orderedPassNames = result.ExecutionOrder
//                 .Select(pass => pass.PassTypeName)
//                 .Where(name => !string.IsNullOrEmpty(name))
//                 .ToList();
//
//             EnsureOrderedPassSequence(
//                 orderedPassNames,
//                 assetPath,
//                 nameof(RuntimeDDGIRTASBuildPass),
//                 nameof(RuntimeDDGIProbeTracePass),
//                 nameof(RuntimeDDGIProbeBlendPass),
//                 nameof(RuntimeDeferredLightingPass));
//         }
//
//         private static void EnsureOrderedPassSequence(
//             IReadOnlyList<string> orderedPassNames,
//             string assetPath,
//             params string[] requiredSequence)
//         {
//             int lastIndex = -1;
//             for (int sequenceIndex = 0; sequenceIndex < requiredSequence.Length; sequenceIndex++)
//             {
//                 string passName = requiredSequence[sequenceIndex];
//                 int index = orderedPassNames.IndexOf(passName);
//                 if (index < 0)
//                 {
//                     throw new InvalidOperationException(
//                         $"The migrated graph '{assetPath}' does not contain the required pass '{passName}'.");
//                 }
//
//                 if (index <= lastIndex)
//                 {
//                     throw new InvalidOperationException(
//                         $"The migrated graph '{assetPath}' did not preserve the required DDGI order: {string.Join(" -> ", requiredSequence)}.");
//                 }
//
//                 lastIndex = index;
//             }
//         }
//
//         private static RenderGraphSubSystemGraph FindLightingGraph(RenderGraphEditorGraph rootGraph)
//         {
//             ISubgraphNode titledSubgraph = rootGraph.GetNodes()
//                 .OfType<ISubgraphNode>()
//                 .FirstOrDefault(node => string.Equals((node as Node)?.Title, LightingSubgraphTitle, StringComparison.Ordinal));
//             if (titledSubgraph?.GetSubgraph() is RenderGraphSubSystemGraph namedGraph)
//             {
//                 return namedGraph;
//             }
//
//             foreach (ISubgraphNode subgraphNode in rootGraph.GetNodes().OfType<ISubgraphNode>())
//             {
//                 if (subgraphNode.GetSubgraph() is not RenderGraphSubSystemGraph candidateGraph)
//                 {
//                     continue;
//                 }
//
//                 if (FindDeferredNode(candidateGraph) != null)
//                 {
//                     return candidateGraph;
//                 }
//             }
//
//             return null;
//         }
//
//         private static RenderPassNodeData FindDeferredNode(Graph graph)
//         {
//             return graph.GetNodes()
//                 .OfType<RenderPassNodeData>()
//                 .FirstOrDefault(node =>
//                 {
//                     Type passType = node.GetPassType();
//                     return passType == typeof(RuntimeDeferredDirectionalLightingPass)
//                         || passType == typeof(RuntimeDeferredLightingPass);
//                 });
//         }
//
//         private static TNode FindPassNode<TNode>(Graph graph)
//             where TNode : RenderPassNodeData
//         {
//             return graph.GetNodes().OfType<TNode>().FirstOrDefault();
//         }
//
//         private static TNode AddPassNode<TNode>(Graph graph, TNode node, Vector2 position)
//             where TNode : Node
//         {
//             if (graph == null)
//             {
//                 throw new ArgumentNullException(nameof(graph));
//             }
//
//             if (node == null)
//             {
//                 throw new ArgumentNullException(nameof(node));
//             }
//
//             object implementation = s_GraphImplementationField?.GetValue(graph);
//             if (implementation == null || s_CreateNodeModelMethod == null)
//             {
//                 throw new InvalidOperationException("Failed to access GraphToolkit internals required to add render pass nodes.");
//             }
//
//             object nodeModel = s_CreateNodeModelMethod.Invoke(implementation, new object[] { node, position });
//             if (nodeModel == null)
//             {
//                 throw new InvalidOperationException($"Failed to create a node model for '{node.GetType().FullName}'.");
//             }
//
//             return node;
//         }
//
//         private static void CopyPassConnections(Graph graph, RenderPassNodeData sourceNode, RenderPassNodeData destinationNode)
//         {
//             if (graph == null || sourceNode == null || destinationNode == null)
//             {
//                 return;
//             }
//
//             Type sourcePassType = sourceNode.GetPassType();
//             if (sourcePassType == null)
//             {
//                 return;
//             }
//
//             foreach (FieldInfo field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(sourcePassType))
//             {
//                 RenderGraphResource attr = field.GetCustomAttribute<RenderGraphResource>();
//                 if (attr == null)
//                 {
//                     continue;
//                 }
//
//                 string inputPortName = sourceNode.GetInputPortName(field, attr);
//                 if (!string.IsNullOrEmpty(inputPortName))
//                 {
//                     IPort sourceInputPort = sourceNode.GetInputPortByName(inputPortName);
//                     IPort destinationInputPort = destinationNode.GetInputPortByName(inputPortName);
//                     if (sourceInputPort?.FirstConnectedPort != null && destinationInputPort != null)
//                     {
//                         EnsureConnection(graph, sourceInputPort.FirstConnectedPort, destinationInputPort);
//                     }
//                 }
//
//                 string outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access, attr.BindingMode);
//                 if (!string.IsNullOrEmpty(outputPortName))
//                 {
//                     IPort sourceOutputPort = sourceNode.GetOutputPortByName(outputPortName);
//                     IPort destinationOutputPort = destinationNode.GetOutputPortByName(outputPortName);
//                     if (sourceOutputPort?.FirstConnectedPort != null && destinationOutputPort != null)
//                     {
//                         EnsureConnection(graph, destinationOutputPort, sourceOutputPort.FirstConnectedPort);
//                     }
//                 }
//             }
//         }
//
//         private static void EnsureConnection(Graph graph, IPort outputPort, IPort inputPort)
//         {
//             if (graph == null || outputPort == null || inputPort == null)
//             {
//                 return;
//             }
//
//             if (ReferenceEquals(inputPort.FirstConnectedPort, outputPort))
//             {
//                 return;
//             }
//
//             if (!graph.Connect(outputPort, inputPort))
//             {
//                 throw new InvalidOperationException(
//                     $"Failed to connect '{outputPort.Title}' to '{inputPort.Title}' while applying the DDGI v1 graph migration.");
//             }
//         }
//     }
// }
