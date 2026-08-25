using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphStandardOpaqueMigration
    {
        private static readonly string[] s_GBufferVariableNames =
        {
            "GBuffer0",
            "GBuffer1",
            "GBuffer2",
            "GBuffer3",
            "DiffuseIrradiance",
        };

        private static readonly string[] s_ResolveOutputFieldNames =
        {
            "m_GBuffer0_Out",
            "m_GBuffer1_Out",
            "m_GBuffer2_Out",
            "m_GBuffer3_Out",
            "m_GBuffer4_Out",
        };

        private static readonly string[] s_ResolveOwnedFieldNames =
        {
            "m_GBuffer0",
            "m_GBuffer1",
            "m_GBuffer2",
            "m_GBuffer3",
            "m_GBuffer4",
            "m_LayerAux0",
            "m_LayerAux1",
        };

        private static readonly string[] s_LayerAuxVariableNames =
        {
            "LayerAux0",
            "LayerAux1",
        };

        private static readonly string[] s_LayerAuxResolveOutputFieldNames =
        {
            "m_LayerAux0_Out",
            "m_LayerAux1_Out",
        };

        private static readonly string[] s_LayerAuxDeferredInputFieldNames =
        {
            "m_LayerAux0",
            "m_LayerAux1",
        };

        internal static bool Migrate(RenderGraphEditorGraph graph, string assetPath)
        {
            if (graph == null)
                return false;

            var rootPasses = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
            var gBufferNode = FindPass(rootPasses, typeof(GBufferPass));
            var preDepthNode = FindPass(rootPasses, typeof(PreDepthPass));
            var materialDebugNode = FindPass(rootPasses, typeof(MaterialDebugPass));
            var visibilityNode = FindPass(rootPasses, typeof(VisibilityBufferPass));
            var resolveNode = FindPass(rootPasses, typeof(VisibilityBufferGBufferResolvePass));
            var legacyResolveNodes = rootPasses
                .Where(node => node.GetPassType() == typeof(VisibilityBufferResolvePass))
                .ToArray();

            if (preDepthNode == null || materialDebugNode == null)
                return false;

            if (!TryFindLightingSubSystem(
                    graph,
                    out var lightingSubSystemNode,
                    out var lightingSubSystem,
                    out var classificationNode,
                    out var deferredNode))
            {
                return false;
            }

            var interfaceVariables = new IVariable[s_GBufferVariableNames.Length];
            var interfacePorts = new IPort[s_GBufferVariableNames.Length];
            for (var index = 0; index < interfaceVariables.Length; index++)
            {
                var legacyName = index == interfaceVariables.Length - 1 ? "GBuffer4" : null;
                var variable = FindInputTextureVariable(
                    lightingSubSystem,
                    s_GBufferVariableNames[index],
                    legacyName);
                if (variable == null
                    || !RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(
                        lightingSubSystemNode,
                        variable,
                        out var interfacePort)
                    || interfacePort == null)
                {
                    return false;
                }

                interfaceVariables[index] = variable;
                interfacePorts[index] = interfacePort;
            }

            if (legacyResolveNodes.Any(node => ((INode)node).IsConnected))
                return false;

            var hasLegacyGBuffer = gBufferNode != null;
            var hasConnectedLegacyGBuffer = hasLegacyGBuffer
                                            && ((INode)gBufferNode).IsConnected;
            var matchesRecognizedTopology = hasLegacyGBuffer
                ? hasConnectedLegacyGBuffer
                    ? MatchesLegacyStandardTopology(
                          preDepthNode,
                          gBufferNode,
                          interfacePorts)
                      || MatchesLegacyHybridTopology(
                          visibilityNode,
                          gBufferNode,
                          resolveNode,
                          interfacePorts)
                    : MatchesPendingCleanupTopology(
                        preDepthNode,
                        visibilityNode,
                        resolveNode,
                        interfacePorts)
                : MatchesPendingCleanupTopology(
                    preDepthNode,
                    visibilityNode,
                    resolveNode,
                    interfacePorts);
            if (!matchesRecognizedTopology)
                return false;

            var changed = false;
            classificationNode.DefineNode();
            changed |= ConnectIfUnconnected(
                lightingSubSystem,
                GetFirstVariableOutput(interfaceVariables[1]),
                classificationNode.GetInputPortByName("m_GBuffer1"));

            if (!hasLegacyGBuffer)
            {
                changed |= EnsureDualSlabSidecarConnections(
                    graph,
                    lightingSubSystemNode,
                    lightingSubSystem,
                    resolveNode,
                    deferredNode);
                foreach (var legacyResolveNode in legacyResolveNodes)
                {
                    graph.RemoveNode(legacyResolveNode);
                    changed = true;
                }

                return changed;
            }

            var legacyColorConsumers = CaptureGBufferConsumers(gBufferNode, resolveNode);
            var legacyDepthConsumers = CaptureConsumers(
                gBufferNode.GetOutputPortByName("m_GBufferDepth_Out"));

            var basePosition = gBufferNode.Position;
            if (visibilityNode == null)
            {
                visibilityNode = CreatePassNode(
                    graph,
                    typeof(VisibilityBufferPass),
                    basePosition + new Vector2(-1500.0f, 280.0f));
                changed = true;
            }

            if (resolveNode == null)
            {
                resolveNode = CreatePassNode(
                    graph,
                    typeof(VisibilityBufferGBufferResolvePass),
                    basePosition + new Vector2(460.0f, 160.0f));
                changed = true;
            }

            if (visibilityNode == null || resolveNode == null)
                return changed;

            changed |= EnsureDualSlabSidecarConnections(
                graph,
                lightingSubSystemNode,
                lightingSubSystem,
                resolveNode,
                deferredNode);

            var removeDisconnectedGBuffer = !hasConnectedLegacyGBuffer;
            if (removeDisconnectedGBuffer)
            {
                graph.RemoveNode(gBufferNode);
                changed = true;
            }

            changed |= DisableResolveOutputOverrides(resolveNode);
            deferredNode.DefineNode();
            materialDebugNode.DefineNode();

            var diffuseIrradianceVariable = interfaceVariables[interfaceVariables.Length - 1];
            if (!string.Equals(
                    diffuseIrradianceVariable.Name,
                    "DiffuseIrradiance (R)",
                    StringComparison.Ordinal))
            {
                diffuseIrradianceVariable.Name = "DiffuseIrradiance (R)";
                changed = true;
            }

            changed |= ConnectIfUnconnected(
                graph,
                preDepthNode?.GetOutputPortByName("m_DepthAttachment_Out"),
                visibilityNode.GetInputPortByName("m_Depth_In"));
            changed |= ConnectIfUnconnected(
                graph,
                visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                resolveNode.GetInputPortByName("m_VisibilityBuffer"));
            changed |= ConnectIfUnconnected(
                graph,
                visibilityNode.GetOutputPortByName("m_Attributes0_Out"),
                resolveNode.GetInputPortByName("m_Attributes0"));
            changed |= ConnectIfUnconnected(
                graph,
                visibilityNode.GetOutputPortByName("m_Attributes1_Out"),
                resolveNode.GetInputPortByName("m_Attributes1"));
            changed |= ConnectIfUnconnected(
                graph,
                visibilityNode.GetOutputPortByName("m_Barycentrics_Out"),
                resolveNode.GetInputPortByName("m_Barycentrics"));

            var visibilityDepthOutput = visibilityNode.GetOutputPortByName("m_Depth_Out");
            changed |= ConnectDepthConsumer(
                graph,
                visibilityDepthOutput,
                FindPass(rootPasses, typeof(LightGridPass)),
                "m_DepthTexture");
            changed |= ConnectDepthConsumer(
                graph,
                visibilityDepthOutput,
                FindPass(rootPasses, typeof(HZBGeneratePass)),
                "m_DepthTexture");
            changed |= ConnectDepthConsumer(
                graph,
                visibilityDepthOutput,
                FindPass(rootPasses, typeof(MotionVectorPass)),
                "m_CameraDepthStencilTexture_In");

            var csmShadowResolveNode = FindPass(rootPasses, typeof(CSMShadowResolvePass));
            changed |= ConnectDepthConsumer(
                graph,
                visibilityDepthOutput,
                csmShadowResolveNode,
                "m_DepthTexture");
            changed |= ReconnectConsumers(graph, visibilityDepthOutput, legacyDepthConsumers);

            for (var index = 0; index < s_ResolveOutputFieldNames.Length; index++)
            {
                var resolveOutput = resolveNode.GetOutputPortByName(s_ResolveOutputFieldNames[index]);
                changed |= ConnectReplacing(graph, resolveOutput, interfacePorts[index]);
                changed |= ReconnectConsumers(graph, resolveOutput, legacyColorConsumers[index]);
            }

            changed |= ConnectIfUnconnected(
                graph,
                resolveNode.GetOutputPortByName("m_GBuffer0_Out"),
                materialDebugNode.GetInputPortByName("m_GBuffer0"));
            changed |= ConnectIfUnconnected(
                graph,
                resolveNode.GetOutputPortByName("m_GBuffer1_Out"),
                materialDebugNode.GetInputPortByName("m_GBuffer1"));
            changed |= ConnectIfUnconnected(
                graph,
                resolveNode.GetOutputPortByName("m_GBuffer2_Out"),
                materialDebugNode.GetInputPortByName("m_GBuffer2"));
            changed |= ConnectIfUnconnected(
                graph,
                resolveNode.GetOutputPortByName("m_GBuffer3_Out"),
                materialDebugNode.GetInputPortByName("m_GBuffer3"));
            changed |= ConnectIfUnconnected(
                graph,
                resolveNode.GetOutputPortByName("m_GBuffer4_Out"),
                materialDebugNode.GetInputPortByName("m_GBuffer4"));
            changed |= ConnectIfUnconnected(
                graph,
                visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                materialDebugNode.GetInputPortByName("m_VisibilityBuffer"));

            if (csmShadowResolveNode != null)
            {
                changed |= ConnectIfUnconnected(
                    graph,
                    resolveNode.GetOutputPortByName("m_GBuffer1_Out"),
                    csmShadowResolveNode.GetInputPortByName("m_GBuffer1"));
            }

            var diffuseIrradianceVariableOutput = GetFirstVariableOutput(diffuseIrradianceVariable);
            changed |= ConnectIfUnconnected(
                lightingSubSystem,
                diffuseIrradianceVariableOutput,
                deferredNode.GetInputPortByName("m_GBuffer4"));

            foreach (var legacyResolveNode in legacyResolveNodes)
            {
                graph.RemoveNode(legacyResolveNode);
                changed = true;
            }

            if (!removeDisconnectedGBuffer)
                changed |= DisconnectAllNodePorts(graph, gBufferNode);

            return changed;
        }

        private static bool EnsureDualSlabSidecarConnections(
            RenderGraphEditorGraph graph,
            ISubgraphNode lightingSubSystemNode,
            RenderGraphSubSystemGraph lightingSubSystem,
            RenderPassNodeData resolveNode,
            RenderPassNodeData deferredNode)
        {
            if (graph == null
                || lightingSubSystemNode == null
                || lightingSubSystem == null
                || resolveNode == null
                || deferredNode == null)
            {
                return false;
            }

            var changed = false;
            var refreshInterfacePorts = false;
            var variables = new IVariable[s_LayerAuxVariableNames.Length];
            resolveNode.DefineNode();
            deferredNode.DefineNode();

            // Preserve custom variables. A same-name Local/Output variable is not
            // part of the standard interface and must never be repurposed.
            for (var index = 0; index < s_LayerAuxVariableNames.Length; index++)
            {
                var matchingVariables = lightingSubSystem.GetVariables()
                    .Where(variable =>
                        variable != null
                        && variable.DataType == typeof(RenderGraphTexture)
                        && MatchesVariableName(
                            variable.Name,
                            s_LayerAuxVariableNames[index]))
                    .ToArray();
                if (matchingVariables.Length > 1
                    || (matchingVariables.Length == 1
                        && matchingVariables[0].VariableKind != VariableKind.Input))
                {
                    return false;
                }

                variables[index] = matchingVariables.FirstOrDefault();
            }

            for (var index = 0; index < variables.Length; index++)
            {
                var variable = variables[index];
                if (variable == null)
                {
                    variable = lightingSubSystem.CreateVariable(
                        $"{s_LayerAuxVariableNames[index]} (R)",
                        typeof(RenderGraphTexture),
                        new RenderGraphTexture(),
                        VariableKind.Local);
                    variable.VariableKind = VariableKind.Input;
                    variables[index] = variable;
                    refreshInterfacePorts = true;
                    changed = true;
                }

                if (GetFirstVariableOutput(variable) == null)
                {
                    lightingSubSystem.AddVariableNode(variable, default);
                    changed = true;
                }
            }

            if (refreshInterfacePorts
                && !RenderGraphSubSystemReflectionUtility.TryRefreshPorts(
                    lightingSubSystemNode))
            {
                return changed;
            }

            for (var index = 0; index < variables.Length; index++)
            {
                var variable = variables[index];
                if (RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(
                        lightingSubSystemNode,
                        variable,
                        out var interfacePort))
                {
                    changed |= ConnectIfUnconnected(
                        graph,
                        resolveNode.GetOutputPortByName(
                            s_LayerAuxResolveOutputFieldNames[index]),
                        interfacePort);
                }

                changed |= ConnectIfUnconnected(
                    lightingSubSystem,
                    GetFirstVariableOutput(variable),
                    deferredNode.GetInputPortByName(
                        s_LayerAuxDeferredInputFieldNames[index]));
            }

            return changed;
        }

        private static bool TryFindLightingSubSystem(
            RenderGraphEditorGraph graph,
            out ISubgraphNode subSystemNode,
            out RenderGraphSubSystemGraph subSystem,
            out RenderPassNodeData classificationNode,
            out RenderPassNodeData deferredNode)
        {
            subSystemNode = null;
            subSystem = null;
            classificationNode = null;
            deferredNode = null;

            foreach (var candidateNode in graph.GetNodes().OfType<ISubgraphNode>())
            {
                if (candidateNode.GetSubgraph() is not RenderGraphSubSystemGraph candidateGraph)
                    continue;

                var passNodes = candidateGraph.GetNodes().OfType<RenderPassNodeData>().ToArray();
                var candidateClassificationNode = FindPass(
                    passNodes,
                    typeof(MaterialClassificationPass));
                if (candidateClassificationNode == null)
                    continue;

                var candidateDeferredNode = FindPass(passNodes, typeof(DeferredLightingPass));
                if (candidateDeferredNode == null)
                    continue;

                if (subSystemNode != null)
                    return false;

                subSystemNode = candidateNode;
                subSystem = candidateGraph;
                classificationNode = candidateClassificationNode;
                deferredNode = candidateDeferredNode;
            }

            return subSystemNode != null;
        }

        private static RenderPassNodeData FindPass(
            IEnumerable<RenderPassNodeData> passNodes,
            Type passType)
        {
            return passNodes?.FirstOrDefault(node => node?.GetPassType() == passType);
        }

        private static bool MatchesLegacyStandardTopology(
            RenderPassNodeData preDepthNode,
            RenderPassNodeData gBufferNode,
            IReadOnlyList<IPort> interfacePorts)
        {
            if (!IsConnected(
                    preDepthNode?.GetOutputPortByName("m_DepthAttachment_Out"),
                    gBufferNode?.GetInputPortByName("m_GBufferDepth_In")))
            {
                return false;
            }

            for (var index = 0; index < interfacePorts.Count; index++)
            {
                if (!IsConnected(
                        gBufferNode.GetOutputPortByName($"m_GBuffer{index}"),
                        interfacePorts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesLegacyHybridTopology(
            RenderPassNodeData visibilityNode,
            RenderPassNodeData gBufferNode,
            RenderPassNodeData resolveNode,
            IReadOnlyList<IPort> interfacePorts)
        {
            if (visibilityNode == null || resolveNode == null)
                return false;

            if (!IsConnected(
                    visibilityNode.GetOutputPortByName("m_Depth_Out"),
                    gBufferNode.GetInputPortByName("m_GBufferDepth_In")))
            {
                return false;
            }

            for (var index = 0; index < interfacePorts.Count; index++)
            {
                if (!IsConnected(
                        gBufferNode.GetOutputPortByName($"m_GBuffer{index}"),
                        resolveNode.GetInputPortByName($"m_GBuffer{index}_In"))
                    || !IsConnected(
                        resolveNode.GetOutputPortByName(s_ResolveOutputFieldNames[index]),
                        interfacePorts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesPendingCleanupTopology(
            RenderPassNodeData preDepthNode,
            RenderPassNodeData visibilityNode,
            RenderPassNodeData resolveNode,
            IReadOnlyList<IPort> interfacePorts)
        {
            if (preDepthNode == null || visibilityNode == null || resolveNode == null)
                return false;

            if (!IsConnected(
                    preDepthNode.GetOutputPortByName("m_DepthAttachment_Out"),
                    visibilityNode.GetInputPortByName("m_Depth_In"))
                || !IsConnected(
                    visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                    resolveNode.GetInputPortByName("m_VisibilityBuffer"))
                || !IsConnected(
                    visibilityNode.GetOutputPortByName("m_Attributes0_Out"),
                    resolveNode.GetInputPortByName("m_Attributes0"))
                || !IsConnected(
                    visibilityNode.GetOutputPortByName("m_Attributes1_Out"),
                    resolveNode.GetInputPortByName("m_Attributes1"))
                || !IsConnected(
                    visibilityNode.GetOutputPortByName("m_Barycentrics_Out"),
                    resolveNode.GetInputPortByName("m_Barycentrics")))
            {
                return false;
            }

            for (var index = 0; index < interfacePorts.Count; index++)
            {
                if (!IsConnected(
                        resolveNode.GetOutputPortByName(s_ResolveOutputFieldNames[index]),
                        interfacePorts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsConnected(IPort expectedOutput, IPort inputPort)
        {
            var connectedOutput = inputPort?.FirstConnectedPort;
            return expectedOutput != null
                   && connectedOutput != null
                   && expectedOutput.ID == connectedOutput.ID;
        }

        private static RenderPassNodeData CreatePassNode(
            RenderGraphEditorGraph graph,
            Type passType,
            Vector2 position)
        {
            var nodeType = RenderPassNodeRegistry.GetNodeType(passType);
            if (nodeType == null
                || Activator.CreateInstance(nodeType) is not RenderPassNodeData passNode)
            {
                return null;
            }

            passNode.Position = position;
            graph.AddNode(passNode);
            return passNode;
        }

        private static bool DisableResolveOutputOverrides(RenderPassNodeData resolveNode)
        {
            var changed = false;
            foreach (var fieldName in s_ResolveOwnedFieldNames)
            {
                var option = resolveNode.GetNodeOptionByName(
                    RenderPassPortUtility.GetOverrideOptionName(fieldName));
                if (option == null)
                    continue;

                if (!option.TryGetValue<bool>(out var enabled) || !enabled)
                    continue;

                if (option.TrySetValue(false))
                    changed = true;
            }

            resolveNode.DefineNode();
            return changed;
        }

        private static bool DisconnectInput(Graph graph, IPort inputPort)
        {
            if (graph == null || inputPort?.IsConnected != true)
                return false;

            var outputPort = inputPort.FirstConnectedPort;
            return outputPort != null && graph.Disconnect(outputPort, inputPort);
        }

        private static bool DisconnectAllNodePorts(Graph graph, INode node)
        {
            if (graph == null || node == null)
                return false;

            var changed = false;
            foreach (var inputPort in node.GetInputPorts().ToArray())
                changed |= DisconnectInput(graph, inputPort);

            foreach (var outputPort in node.GetOutputPorts().ToArray())
            {
                var connectedInputs = new List<IPort>();
                outputPort.GetConnectedPorts(connectedInputs);
                foreach (var inputPort in connectedInputs.Where(port => port != null))
                    changed |= graph.Disconnect(outputPort, inputPort);
            }

            return changed;
        }

        private static IVariable FindInputTextureVariable(
            RenderGraphSubSystemGraph graph,
            string name,
            string legacyName = null)
        {
            return graph.GetVariables().FirstOrDefault(variable =>
                variable != null
                && variable.VariableKind == VariableKind.Input
                && variable.DataType == typeof(RenderGraphTexture)
                && (MatchesVariableName(variable.Name, name)
                    || (!string.IsNullOrEmpty(legacyName)
                        && MatchesVariableName(variable.Name, legacyName))));
        }

        private static bool MatchesVariableName(string candidate, string expected)
        {
            return string.Equals(candidate, expected, StringComparison.Ordinal)
                   || string.Equals(candidate, $"{expected} (R)", StringComparison.Ordinal);
        }

        private static IPort GetFirstVariableOutput(IVariable variable)
        {
            if (variable == null)
                return null;

            var variableNodes = new List<IVariableNode>();
            variable.GetNodes(variableNodes);
            foreach (var variableNode in variableNodes)
            {
                if (variableNode?.OutputPortCount > 0)
                    return variableNode.GetOutputPort(0);
            }

            return null;
        }

        private static List<IPort>[] CaptureGBufferConsumers(
            RenderPassNodeData gBufferNode,
            RenderPassNodeData resolveNode)
        {
            var consumers = new List<IPort>[s_ResolveOutputFieldNames.Length];
            for (var index = 0; index < consumers.Length; index++)
            {
                var legacyFieldName = index == consumers.Length - 1
                    ? "m_GBuffer4"
                    : $"m_GBuffer{index}";
                consumers[index] = CaptureConsumers(
                    gBufferNode.GetOutputPortByName(legacyFieldName),
                    resolveNode);
            }

            return consumers;
        }

        private static List<IPort> CaptureConsumers(
            IPort outputPort,
            RenderPassNodeData excludedNode = null)
        {
            var consumers = new List<IPort>();
            if (outputPort == null)
                return consumers;

            outputPort.GetConnectedPorts(consumers);
            if (excludedNode != null)
                consumers.RemoveAll(port => ReferenceEquals(port?.GetNode(), excludedNode));
            return consumers;
        }

        private static bool ConnectDepthConsumer(
            RenderGraphEditorGraph graph,
            IPort depthOutput,
            RenderPassNodeData consumerNode,
            string inputPortName)
        {
            return ConnectIfUnconnected(
                graph,
                depthOutput,
                consumerNode?.GetInputPortByName(inputPortName));
        }

        private static bool ConnectIfUnconnected(
            RenderGraphEditorGraph graph,
            IPort outputPort,
            IPort inputPort)
        {
            if (graph == null
                || outputPort == null
                || inputPort == null
                || inputPort.IsConnected)
            {
                return false;
            }

            return graph.Connect(outputPort, inputPort);
        }

        private static bool ReconnectConsumers(
            RenderGraphEditorGraph graph,
            IPort outputPort,
            IEnumerable<IPort> consumers)
        {
            var changed = false;
            if (graph == null || outputPort == null || consumers == null)
                return false;

            foreach (var capturedInput in consumers)
            {
                if (capturedInput == null)
                    continue;

                var currentInput = capturedInput.GetNode()?.GetInputPortByName(capturedInput.Name);
                changed |= ConnectReplacing(graph, outputPort, currentInput);
            }

            return changed;
        }

        private static bool ConnectReplacing(
            RenderGraphEditorGraph graph,
            IPort outputPort,
            IPort inputPort)
        {
            if (graph == null || outputPort == null || inputPort == null)
                return false;

            var connectedOutput = inputPort.FirstConnectedPort;
            if (connectedOutput != null && connectedOutput.ID == outputPort.ID)
                return false;

            var changed = false;
            while (inputPort.IsConnected)
            {
                connectedOutput = inputPort.FirstConnectedPort;
                if (connectedOutput == null
                    || !graph.Disconnect(connectedOutput, inputPort))
                {
                    return changed;
                }

                changed = true;
            }

            return graph.Connect(outputPort, inputPort) || changed;
        }
    }
}
