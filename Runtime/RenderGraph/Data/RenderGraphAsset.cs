using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderGraph.Data
{
    [CreateAssetMenu(menuName = "VividRP/Render Graph")]
    public class RenderGraphAsset : ScriptableObject
    {
        [SerializeReference] public List<RenderGraphNodeData> Nodes = new List<RenderGraphNodeData>();
        public List<RenderGraphEdgeData> Edges = new List<RenderGraphEdgeData>();

        public void AddNode(RenderGraphNodeData node)
        {
            Nodes.Add(node);
        }

        public void RemoveNode(string guid)
        {
            Nodes.RemoveAll(n => n.Guid == guid);
            Edges.RemoveAll(e => e.OutputNodeGuid == guid || e.InputNodeGuid == guid);
        }

        public void AddEdge(RenderGraphEdgeData edge)
        {
            Edges.Add(edge);
        }

        public void RemoveEdge(string outputNodeGuid, string outputPortId, string inputNodeGuid, string inputPortId)
        {
            Edges.RemoveAll(e =>
                e.OutputNodeGuid == outputNodeGuid &&
                e.OutputPortId == outputPortId &&
                e.InputNodeGuid == inputNodeGuid &&
                e.InputPortId == inputPortId);
        }
    }
}
