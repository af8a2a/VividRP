using System;
using System.Linq;
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
            var diffuse = deferred.GetInputPortByName("m_RealtimeDiffuseGI");
            if (diffuse?.FirstConnectedPort?.GetNode() is RenderPassNodeData producer
                && producer.GetPassType() == typeof(SdfDiffuseGatherPass))
                return;
            if (diffuse == null || diffuse.IsConnected || nodes.Any(node => node.GetPassType() == typeof(SdfDiffuseGatherPass)))
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
                || !lighting.Connect(gather.GetOutputPortByName("m_Output"), diffuse))
                throw new InvalidOperationException("Could not connect the SDF diffuse gather pass.");

            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
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
