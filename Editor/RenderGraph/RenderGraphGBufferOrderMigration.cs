using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    // Native geometry initializes the MRTs; meshlet resolve merges only valid visibility pixels.
    internal static class RenderGraphGBufferOrderMigration
    {
        internal static bool IsHybridGraph(RenderGraphEditorGraph graph)
        {
            var nodes = graph.GetNodes().OfType<RenderPassNodeData>();
            return nodes.Count(n => n.GetPassType() == typeof(GBufferPass)) == 1
                && nodes.Count(n => n.GetPassType() == typeof(VisibilityBufferGBufferResolvePass)) == 1
                && nodes.Any(n => n.GetPassType() == typeof(GBufferPass) && ((INode)n).IsConnected)
                && nodes.Any(n => n.GetPassType() == typeof(VisibilityBufferGBufferResolvePass) && ((INode)n).IsConnected);
        }

        internal static bool Migrate(RenderGraphEditorGraph graph)
        {
            if (!IsHybridGraph(graph))
                return false;
            var nodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
            var gbuffer = nodes.Single(n => n.GetPassType() == typeof(GBufferPass));
            var resolve = nodes.Single(n => n.GetPassType() == typeof(VisibilityBufferGBufferResolvePass));
            // Do not repurpose custom inputs from unrelated producers.
            for (int i = 0; i < 5; i++)
            {
                var field = "m_GBuffer" + i;
                var nativeSource = gbuffer.GetInputPortByName(field + "_In")?.FirstConnectedPort?.GetNode();
                var resolveSource = resolve.GetInputPortByName(field + "_In")?.FirstConnectedPort?.GetNode();
                if ((nativeSource != null && !ReferenceEquals(nativeSource, resolve))
                    || (resolveSource != null && !ReferenceEquals(resolveSource, gbuffer)))
                    return false;
            }

            bool changed = false;
            var consumers = new List<IPort>[5];
            for (int i = 0; i < 5; i++)
            {
                var field = "m_GBuffer" + i;
                consumers[i] = new List<IPort>();
                var output = gbuffer.GetOutputPortByName(field) ?? gbuffer.GetOutputPortByName(field + "_Out");
                output?.GetConnectedPorts(consumers[i]);
                consumers[i].RemoveAll(p => ReferenceEquals(p.GetNode(), resolve));
                var input = gbuffer.GetInputPortByName(field + "_In");
                if (input?.FirstConnectedPort != null)
                {
                    graph.Disconnect(input.FirstConnectedPort, input);
                    changed = true;
                }
                var nativeOverride = gbuffer.GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(field));
                if (nativeOverride != null && nativeOverride.TryGetValue<bool>(out var enabled) && enabled)
                    changed |= nativeOverride.TrySetValue(false);
                var resolveOverride = resolve.GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(field));
                if (resolveOverride != null && resolveOverride.TryGetValue<bool>(out enabled) && !enabled)
                    changed |= resolveOverride.TrySetValue(true);
            }
            gbuffer.DefineNode();
            resolve.DefineNode();
            for (int i = 0; i < 5; i++)
            {
                var field = "m_GBuffer" + i;
                var nativeOutput = gbuffer.GetOutputPortByName(field) ?? gbuffer.GetOutputPortByName(field + "_Out");
                var resolveInput = resolve.GetInputPortByName(field + "_In");
                var resolveOutput = resolve.GetOutputPortByName(field + "_Out");
                changed |= Connect(graph, nativeOutput, resolveInput);
                foreach (var consumer in consumers[i])
                    changed |= Connect(graph, resolveOutput, consumer.GetNode().GetInputPortByName(consumer.Name));
            }
            return changed;
        }

        private static bool Connect(RenderGraphEditorGraph graph, IPort output, IPort input)
        {
            if (output == null || input == null)
                throw new InvalidOperationException("The hybrid GBuffer graph has an incompatible port schema.");
            if (input.FirstConnectedPort?.ID == output.ID)
                return false;
            if (input.FirstConnectedPort != null)
                graph.Disconnect(input.FirstConnectedPort, input);
            if (!graph.Connect(output, input))
                throw new InvalidOperationException("Could not connect the GBuffer/Resolve merge chain.");
            return true;
        }
    }

    public static class VividGBufferOrderUtility
    {
        public static bool MigrateAsset(string assetPath)
        {
            var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(assetPath);
            if (graph == null || !RenderGraphGBufferOrderMigration.Migrate(graph))
                return false;
            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }
    }
}
