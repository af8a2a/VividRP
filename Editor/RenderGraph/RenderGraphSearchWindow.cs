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
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Node")),
                new SearchTreeGroupEntry(new GUIContent("Pass"), 1),
            };

            foreach (var entry in RenderNodeRegistry.GetAllPassTypes())
            {
                tree.Add(new SearchTreeEntry(new GUIContent(entry.DisplayName))
                {
                    level = 2,
                    userData = entry.DataType
                });
            }

            tree.Add(new SearchTreeGroupEntry(new GUIContent("Resource"), 1));

            foreach (var entry in RenderNodeRegistry.GetAllResourceTypes())
            {
                tree.Add(new SearchTreeEntry(new GUIContent(entry.DisplayName))
                {
                    level = 2,
                    userData = entry.DataType
                });
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            var type = (System.Type)entry.userData;
            var nodeData = (RenderGraphNodeData)System.Activator.CreateInstance(type);

            var windowMousePos = m_Window.rootVisualElement.ChangeCoordinatesTo(
                m_Window.rootVisualElement.parent,
                context.screenMousePosition - m_Window.position.position);
            var graphMousePos = m_GraphView.contentViewContainer.WorldToLocal(windowMousePos);

            m_GraphView.AddNodeToGraph(nodeData, graphMousePos);
            return true;
        }
    }
}
