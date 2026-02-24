using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public class RenderGraphSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private RenderGraphView m_GraphView;
        private EditorWindow m_Window;

        public void Init(RenderGraphView graphView, EditorWindow window)
        {
            m_GraphView = graphView;
            m_Window = window;
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

            // Screen → window-local → graph content container
            var windowMousePos = m_Window.rootVisualElement.ChangeCoordinatesTo(
                m_Window.rootVisualElement.parent,
                context.screenMousePosition - m_Window.position.position);
            var graphMousePos = m_GraphView.contentViewContainer.WorldToLocal(windowMousePos);

            m_GraphView.AddNodeToGraph(nodeData, graphMousePos);
            return true;
        }
    }
}
