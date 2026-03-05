using System;
using Unity.GraphToolkit;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [Serializable]
    [Graph("RenderGraph")]
    public class RenderGraphEditor : Graph
    {
        Graph m_Graph;
    }
}