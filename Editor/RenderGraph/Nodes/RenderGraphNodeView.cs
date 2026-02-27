using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    public class RenderGraphNodeView : UnityEditor.Experimental.GraphView.Node
    {
        public RenderGraphNodeData NodeData { get; private set; }
        private readonly Dictionary<string, Port> m_PortMap = new Dictionary<string, Port>();

        public RenderGraphNodeView(RenderGraphNodeData data)
        {
            NodeData = data;
            title = data.NodeName;
            viewDataKey = data.Guid;

            SetPosition(new Rect(data.Position, Vector2.zero));

            foreach (var portData in data.Ports)
            {
                var direction = portData.IsInput ? Direction.Input : Direction.Output;
                var capacity = portData.IsInput ? Port.Capacity.Single : Port.Capacity.Multi;
                var portType = PortTypeToSystemType(portData.Type);

                var port = InstantiatePort(Orientation.Horizontal, direction, capacity, portType);
                port.portName = portData.DisplayName;
                port.userData = portData;

                m_PortMap[portData.Id] = port;

                if (portData.IsInput)
                    inputContainer.Add(port);
                else
                    outputContainer.Add(port);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public Port GetPort(string portId)
        {
            m_PortMap.TryGetValue(portId, out var port);
            return port;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            if (NodeData != null)
                NodeData.Position = newPos.position;
        }

        private static System.Type PortTypeToSystemType(PortType type)
        {
            return type switch
            {
                PortType.Texture => typeof(Texture),
                PortType.Buffer => typeof(ComputeBuffer),
                PortType.RendererList => typeof(RendererListHandle),
                _ => typeof(object)
            };
        }
    }
}
