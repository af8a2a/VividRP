using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Editor.RenderGraph.Nodes;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public class RenderGraphView : GraphView
    {
        private RenderGraphAsset m_Asset;
        private RenderGraphSearchWindow m_SearchWindow;

        public RenderGraphView(RenderGraphEditorWindow window)
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.af8a2a.vividrp/Editor/RenderGraph/Styles/RenderGraphEditor.uss"));

            var minimap = new MiniMap { anchored = true };
            minimap.SetPosition(new Rect(10, 30, 200, 140));
            Add(minimap);

            m_SearchWindow = ScriptableObject.CreateInstance<RenderGraphSearchWindow>();
            m_SearchWindow.Init(this, window);
            nodeCreationRequest = ctx =>
                SearchWindow.Open(new SearchWindowContext(ctx.screenMousePosition), m_SearchWindow);

            graphViewChanged = OnGraphViewChanged;
        }

        public void PopulateFromAsset(RenderGraphAsset asset)
        {
            m_Asset = asset;

            // Clear existing
            DeleteElements(graphElements.ToList());

            LoadNodes();
            LoadEdges();
        }

        private void LoadNodes()
        {
            if (m_Asset.Nodes == null) return;
            foreach (var nodeData in m_Asset.Nodes)
            {
                var view = CreateNodeView(nodeData);
                AddElement(view);
            }
        }

        private void LoadEdges()
        {
            if (m_Asset.Edges == null) return;
            foreach (var edgeData in m_Asset.Edges)
            {
                var outputView = FindNodeView(edgeData.OutputNodeGuid);
                var inputView = FindNodeView(edgeData.InputNodeGuid);
                if (outputView == null || inputView == null) continue;

                var outputPort = outputView.GetPort(edgeData.OutputPortId);
                var inputPort = inputView.GetPort(edgeData.InputPortId);
                if (outputPort == null || inputPort == null) continue;

                var edge = outputPort.ConnectTo(inputPort);
                AddElement(edge);
            }
        }

        public void SaveToAsset()
        {
            if (m_Asset == null) return;

            Undo.RecordObject(m_Asset, "Save Render Graph");

            // Save node positions
            foreach (var element in graphElements.ToList())
            {
                if (element is RenderGraphNodeView nodeView)
                {
                    nodeView.NodeData.Position = nodeView.GetPosition().position;
                }
            }

            EditorUtility.SetDirty(m_Asset);
        }

        public void AddNodeToGraph(RenderGraphNodeData data, Vector2 position)
        {
            if (m_Asset == null) return;

            Undo.RecordObject(m_Asset, "Add Node");
            data.Position = position;
            m_Asset.AddNode(data);
            EditorUtility.SetDirty(m_Asset);

            var view = CreateNodeView(data);
            AddElement(view);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            ports.ForEach(port =>
            {
                if (port == startPort) return;
                if (port.node == startPort.node) return;
                if (port.direction == startPort.direction) return;
                if (port.portType != startPort.portType) return;
                compatible.Add(port);
            });
            return compatible;
        }

        private RenderGraphNodeView CreateNodeView(RenderGraphNodeData data)
        {
            return data switch
            {
                RasterPassNodeData raster => new RasterPassNodeView(raster),
                ComputePassNodeData compute => new ComputePassNodeView(compute),
                UnsafePassNodeData unsafePass => new UnsafePassNodeView(unsafePass),
                TextureNodeData texture => new TextureNodeView(texture),
                BufferNodeData buffer => new BufferNodeView(buffer),
                _ => new RenderGraphNodeView(data)
            };
        }

        private RenderGraphNodeView FindNodeView(string guid)
        {
            foreach (var element in graphElements.ToList())
            {
                if (element is RenderGraphNodeView view && view.NodeData.Guid == guid)
                    return view;
            }
            return null;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (m_Asset == null) return change;

            if (change.elementsToRemove != null)
            {
                Undo.RecordObject(m_Asset, "Remove Elements");
                foreach (var element in change.elementsToRemove)
                {
                    if (element is RenderGraphNodeView nodeView)
                    {
                        m_Asset.RemoveNode(nodeView.NodeData.Guid);
                    }
                    else if (element is Edge edge)
                    {
                        var outputData = (RenderGraphPortData)edge.output.userData;
                        var inputData = (RenderGraphPortData)edge.input.userData;
                        var outputNode = (RenderGraphNodeView)edge.output.node;
                        var inputNode = (RenderGraphNodeView)edge.input.node;
                        m_Asset.RemoveEdge(outputNode.NodeData.Guid, outputData.Id,
                            inputNode.NodeData.Guid, inputData.Id);
                    }
                }
                EditorUtility.SetDirty(m_Asset);
            }

            if (change.edgesToCreate != null)
            {
                Undo.RecordObject(m_Asset, "Create Edge");
                for (int i = change.edgesToCreate.Count - 1; i >= 0; i--)
                {
                    var edge = change.edgesToCreate[i];
                    var outputNode = (RenderGraphNodeView)edge.output.node;
                    var inputNode = (RenderGraphNodeView)edge.input.node;

                    if (m_Asset.WouldCreateCycle(outputNode.NodeData.Guid, inputNode.NodeData.Guid))
                    {
                        Debug.LogWarning($"[RenderGraph] Rejected edge: {outputNode.NodeData.NodeName} → {inputNode.NodeData.NodeName} would create a cycle.");
                        change.edgesToCreate.RemoveAt(i);
                        continue;
                    }

                    var outputData = (RenderGraphPortData)edge.output.userData;
                    var inputData = (RenderGraphPortData)edge.input.userData;
                    m_Asset.AddEdge(new RenderGraphEdgeData
                    {
                        OutputNodeGuid = outputNode.NodeData.Guid,
                        OutputPortId = outputData.Id,
                        InputNodeGuid = inputNode.NodeData.Guid,
                        InputPortId = inputData.Id
                    });
                }
                EditorUtility.SetDirty(m_Asset);
            }

            return change;
        }
    }
}
