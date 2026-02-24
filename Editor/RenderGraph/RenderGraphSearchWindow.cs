using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public class RenderGraphSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private RenderGraphView m_GraphView;

        public void Init(RenderGraphView graphView)
        {
            m_GraphView = graphView;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            return new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Node")),
                new SearchTreeGroupEntry(new GUIContent("Pass"), 1),
                new SearchTreeEntry(new GUIContent("Raster Pass")) { level = 2, userData = typeof(RasterPassNodeData) },
                new SearchTreeEntry(new GUIContent("Compute Pass")) { level = 2, userData = typeof(ComputePassNodeData) },
                new SearchTreeEntry(new GUIContent("Unsafe Pass")) { level = 2, userData = typeof(UnsafePassNodeData) },
                new SearchTreeGroupEntry(new GUIContent("Resource"), 1),
                new SearchTreeEntry(new GUIContent("Texture")) { level = 2, userData = typeof(TextureNodeData) },
                new SearchTreeEntry(new GUIContent("Buffer")) { level = 2, userData = typeof(BufferNodeData) },
            };
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            var type = (System.Type)entry.userData;
            var nodeData = (RenderGraphNodeData)System.Activator.CreateInstance(type);

            var worldMousePos = m_GraphView.ChangeCoordinatesTo(
                m_GraphView.contentViewContainer,
                context.screenMousePosition - m_GraphView.parent.worldBound.position);

            m_GraphView.AddNodeToGraph(nodeData, worldMousePos);
            return true;
        }
    }
}
