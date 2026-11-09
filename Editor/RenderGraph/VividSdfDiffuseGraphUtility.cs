using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    public static class VividSdfDiffuseGraphUtility
    {
        public static void AddGatherPass(string graphPath)
        {
            var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(graphPath)
                ?? throw new ArgumentException("Expected a VividRP Render Graph asset.", nameof(graphPath));
            var scopes = graph.GetNodes().OfType<ISubgraphNode>().Select(node => node.GetSubgraph())
                .OfType<RenderGraphEditorGraph>()
                .Where(child => GraphDatabase.GetGraphAssetPath(child) == graphPath)
                .Prepend(graph);
            var lighting = scopes.Single(scope => scope.GetNodes().OfType<RenderPassNodeData>()
                .Any(node => node.GetPassType() == typeof(DeferredLightingPass)));
            var nodes = lighting.GetNodes().OfType<RenderPassNodeData>().ToArray();
            var deferred = nodes.Single(node => node.GetPassType() == typeof(DeferredLightingPass));
            var existingGather = nodes.SingleOrDefault(node => node.GetPassType() == typeof(SdfDiffuseGatherPass));
            var diffuse = deferred.GetInputPortByName("m_RealtimeDiffuseGI");
            if (existingGather != null)
            {
                var previous = diffuse?.FirstConnectedPort;
                if (previous?.GetNode() is { } owner && !ReferenceEquals(owner, existingGather))
                    throw new InvalidOperationException("RealtimeDiffuseGI already has another producer.");
                existingGather.DefineNode();
                var output = existingGather.GetOutputPortByName("m_Output_Out");
                if (diffuse == null || output == null)
                    throw new InvalidOperationException("Could not resolve the SDF diffuse output ports.");
                bool changed = previous?.ID != output.ID;
                if (changed)
                {
                    // A write-only output used to serialize as m_Output.
                    if (previous != null)
                        lighting.Disconnect(previous, diffuse);
                    else if (diffuse.IsConnected && !RemoveLegacyGatherWire(lighting, existingGather, diffuse))
                        throw new InvalidOperationException("Could not remove the obsolete SDF diffuse output connection.");
                    if (!lighting.Connect(output, diffuse))
                        throw new InvalidOperationException("Could not migrate the SDF diffuse output connection.");
                }
                changed |= ConnectOpenPbrInputs(lighting, deferred, existingGather);
                if (changed)
                {
                    GraphDatabase.SaveGraph(graph);
                    AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
                }
                return;
            }
            if (diffuse == null || diffuse.IsConnected)
                throw new InvalidOperationException("RealtimeDiffuseGI already has a producer or the graph needs to be reimported.");

            var depth = deferred.GetInputPortByName("m_DepthTexture")?.FirstConnectedPort;
            var gbuffer0 = deferred.GetInputPortByName("m_GBuffer0")?.FirstConnectedPort;
            var gbuffer1 = deferred.GetInputPortByName("m_GBuffer1")?.FirstConnectedPort;
            if (depth == null || gbuffer0 == null || gbuffer1 == null)
                throw new InvalidOperationException("Connect Deferred depth and GBuffer inputs before adding SDF diffuse GI.");

            var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(SdfDiffuseGatherPass));
            var gather = (RenderPassNodeData)Activator.CreateInstance(nodeType);
            gather.Position = deferred.Position + new Vector2(-300f, -250f);
            lighting.AddNode(gather);
            if (!lighting.Connect(depth, gather.GetInputPortByName("m_Depth"))
                || !lighting.Connect(gbuffer0, gather.GetInputPortByName("m_GBuffer0"))
                || !lighting.Connect(gbuffer1, gather.GetInputPortByName("m_GBuffer1"))
                || !lighting.Connect(gather.GetOutputPortByName("m_Output_Out"), diffuse))
                throw new InvalidOperationException("Could not connect the SDF diffuse gather pass.");

            ConnectOpenPbrInputs(lighting, deferred, gather);
            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool RemoveLegacyGatherWire(RenderGraphEditorGraph graph, RenderPassNodeData gather, IPort input)
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var model = typeof(Graph).GetField("m_Implementation", instance)?.GetValue(graph);
            var gatherModel = typeof(Node).GetMethod("GetImplementation", instance)?.Invoke(gather, null);
            var modelType = model?.GetType();
            var remove = modelType?.GetMethod("RemoveWire", instance);
            var guid = gatherModel?.GetType().GetProperty("Guid", instance)?.GetValue(gatherModel);
            if (remove == null || guid == null
                || modelType.GetMethod("GetWiresForPort", instance)?.Invoke(model, new object[] { input }) is not IEnumerable wires)
                return false;

            // The public Disconnect API requires both ports to resolve.
            var wire = wires.Cast<object>().SingleOrDefault();
            var wireType = wire?.GetType();
            if (wireType?.GetProperty("FromPortId", instance)?.GetValue(wire) is not string port || port != "m_Output"
                || !Equals(wireType.GetProperty("FromNodeGuid", instance)?.GetValue(wire), guid))
                return false;

            remove.Invoke(model, new[] { wire });
            return !input.IsConnected;
        }

        private static bool ConnectOpenPbrInputs(RenderGraphEditorGraph lighting, RenderPassNodeData deferred, RenderPassNodeData gather)
        {
            var parameters = deferred.GetInputPortByName("m_GBuffer4")?.FirstConnectedPort;
            var aux0 = deferred.GetInputPortByName("m_LayerAux0")?.FirstConnectedPort;
            var aux1 = deferred.GetInputPortByName("m_LayerAux1")?.FirstConnectedPort;
            if (parameters == null || aux0 == null || aux1 == null)
                return false;

            bool changed = ConnectMissing("m_OpenPbrParameters", parameters);
            changed |= ConnectMissing("m_OpenPbrAux0", aux0);
            changed |= ConnectMissing("m_OpenPbrAux1", aux1);
            return changed;

            bool ConnectMissing(string name, IPort source)
            {
                var input = gather.GetInputPortByName(name);
                if (input?.IsConnected == true)
                    return false;
                if (input == null || !lighting.Connect(source, input))
                    throw new InvalidOperationException($"Could not connect SDF diffuse input {name}.");
                return true;
            }
        }

        public static void AddScreenTracing(string graphPath)
        {
            var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(graphPath)
                ?? throw new ArgumentException("Expected a VividRP Render Graph asset.", nameof(graphPath));
            var motion = graph.GetNodes().OfType<RenderPassNodeData>()
                .Single(node => node.GetPassType() == typeof(MotionVectorPass))
                .GetOutputPortByName("m_MotionVectorTexture");
            var owner = graph.GetNodes().OfType<ISubgraphNode>().Single(node =>
                node.GetSubgraph() is RenderGraphEditorGraph child
                && GraphDatabase.GetGraphAssetPath(child) == graphPath
                && child.GetNodes().OfType<RenderPassNodeData>().Any(pass => pass.GetPassType() == typeof(SdfDiffuseGatherPass)));
            var lighting = (RenderGraphEditorGraph)owner.GetSubgraph();
            var gather = lighting.GetNodes().OfType<RenderPassNodeData>()
                .Single(node => node.GetPassType() == typeof(SdfDiffuseGatherPass));
            gather.DefineNode();
            var depth = gather.GetInputPortByName("m_Depth").FirstConnectedPort;
            var hzbInput = gather.GetInputPortByName("m_ScreenHzb");
            var motionInput = gather.GetInputPortByName("m_ScreenMotion");
            if (depth == null || motion == null || hzbInput == null || motionInput == null)
                throw new InvalidOperationException("Screen tracing requires final depth and motion vectors.");
            if (hzbInput.IsConnected && motionInput.IsConnected)
                return;
            if (hzbInput.IsConnected || motionInput.IsConnected)
                throw new InvalidOperationException("Screen tracing is already partially connected.");

            var hzb = (RenderPassNodeData)Activator.CreateInstance(RenderPassNodeRegistry.GetNodeType(typeof(HZBGeneratePass)));
            hzb.Position = gather.Position + new Vector2(-280f, 160f);
            lighting.AddNode(hzb);
            var motionVariable = lighting.CreateVariable("GI Motion Vectors (R)", typeof(RenderGraphTexture),
                new RenderGraphTexture(), VariableKind.Local);
            motionVariable.VariableKind = VariableKind.Input;
            lighting.AddVariableNode(motionVariable, gather.Position + new Vector2(-280f, 250f));
            var motionNodes = new System.Collections.Generic.List<IVariableNode>();
            motionVariable.GetNodes(motionNodes);

            if (!RenderGraphSubSystemReflectionUtility.TryRefreshPorts(owner)
                || !RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(owner, motionVariable, out var motionPort)
                || !graph.Connect(motion, motionPort)
                || !lighting.Connect(depth, hzb.GetInputPortByName("m_DepthTexture"))
                || !lighting.Connect(hzb.GetOutputPortByName("m_HzbTexture"), hzbInput)
                || !lighting.Connect(motionNodes.Single().GetOutputPort(0), motionInput))
                throw new InvalidOperationException("Could not connect the screen trace inputs.");

            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
