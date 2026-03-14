using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VividRP.Editor.RenderGraph
{
    [InitializeOnLoad]
    internal static class RenderPassNodeDoubleClickHandler
    {
        private static readonly HashSet<VisualElement> s_registeredGraphViews = new HashSet<VisualElement>(ReferenceComparer.Instance);

        static RenderPassNodeDoubleClickHandler()
        {
            EditorApplication.delayCall += RegisterCallbacks;
            EditorApplication.update += RegisterCallbacks;
        }

        private static void RegisterCallbacks()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var graphView = RenderGraphEditorWindowReflectionUtility.GetGraphView(window);
                if (graphView == null)
                    continue;

                if (!s_registeredGraphViews.Add(graphView))
                    continue;

                graphView.RegisterCallback<MouseDownEvent>(OnGraphViewMouseDown, TrickleDown.TrickleDown);
                graphView.RegisterCallback<DetachFromPanelEvent>(OnGraphViewDetached);
            }
        }

        private static void OnGraphViewMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2)
                return;

            if (evt.target is not VisualElement target)
                return;

            var nodeView = FindAncestorNodeView(target);
            if (nodeView == null)
                return;

            var nodeModel = RenderGraphEditorWindowReflectionUtility.GetNodeModel(nodeView);
            if (!RenderPassNodeNavigationUtility.TryOpenPassScript(nodeModel))
                return;

            evt.StopImmediatePropagation();
        }

        private static void OnGraphViewDetached(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is VisualElement graphView)
                s_registeredGraphViews.Remove(graphView);
        }

        private static VisualElement FindAncestorNodeView(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (RenderGraphEditorWindowReflectionUtility.IsNodeView(current))
                    return current;
            }

            return null;
        }

        private sealed class ReferenceComparer : IEqualityComparer<VisualElement>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(VisualElement x, VisualElement y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(VisualElement obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }

    internal static class RenderGraphEditorWindowReflectionUtility
    {
        private const string GraphViewEditorWindowTypeName = "Unity.GraphToolkit.Editor.GraphViewEditorWindow";
        private const string NodeViewTypeName = "Unity.GraphToolkit.Editor.NodeView";
        private const string GraphViewPropertyName = "GraphView";
        private const string GraphModelPropertyName = "GraphModel";
        private const string GraphContainerFieldName = "m_GraphContainer";
        private const string NodeModelsPropertyName = "NodeModels";
        private const string BackingNodePropertyName = "Node";
        private const string GraphObjectPropertyName = "GraphObject";
        private const string FilePathPropertyName = "FilePath";

        private static readonly BindingFlags InstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool IsGraphViewEditorWindow(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, GraphViewEditorWindowTypeName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        internal static VisualElement GetGraphView(EditorWindow window)
        {
            if (window == null || !IsGraphViewEditorWindow(window.GetType()))
                return null;

            return window.GetType().GetProperty(GraphViewPropertyName, InstanceBindings)?.GetValue(window) as VisualElement;
        }

        internal static VisualElement GetGraphContainer(EditorWindow window)
        {
            if (window == null)
                return null;

            var graphContainer = window.GetType().GetField(GraphContainerFieldName, InstanceBindings)?.GetValue(window) as VisualElement;
            if (graphContainer != null)
                return graphContainer;

            return GetGraphView(window)?.parent ?? window.rootVisualElement;
        }

        internal static bool IsNodeView(VisualElement element)
        {
            return IsAssignableToTypeName(element?.GetType(), NodeViewTypeName);
        }

        internal static object GetNodeModel(VisualElement nodeView)
        {
            if (nodeView == null)
                return null;

            return nodeView.GetType().GetProperty("NodeModel", InstanceBindings)?.GetValue(nodeView);
        }

        internal static bool TryGetCurrentGraph(EditorWindow window, out RenderGraphEditorGraph graph)
        {
            graph = null;
            return TryGetCurrentGraph(GetGraphView(window), out graph);
        }

        internal static bool TryGetCurrentGraph(VisualElement graphView, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (graphView == null)
                return false;

            var graphModel = graphView.GetType().GetProperty(GraphModelPropertyName, InstanceBindings)?.GetValue(graphView);
            if (graphModel == null)
                return false;

            return TryResolveGraph(graphModel, out graph);
        }

        private static bool TryResolveGraph(object graphModel, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (graphModel == null)
                return false;

            if (TryGetValue(graphModel, "Graph", out graph) && graph != null)
                return true;

            if (TryGetLiveGraphFromNodeModels(graphModel, out graph))
                return true;

            return TryLoadGraphFromGraphObject(graphModel, out graph);
        }

        private static bool TryGetLiveGraphFromNodeModels(object graphModel, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (!TryGetValue(graphModel, NodeModelsPropertyName, out System.Collections.IEnumerable nodeModels) || nodeModels == null)
                return false;

            foreach (var nodeModel in nodeModels)
            {
                var node = nodeModel?.GetType().GetProperty(BackingNodePropertyName, InstanceBindings)?.GetValue(nodeModel) as Node;
                if (node?.Graph is RenderGraphEditorGraph renderGraph)
                {
                    graph = renderGraph;
                    return true;
                }
            }

            return false;
        }

        private static bool TryLoadGraphFromGraphObject(object graphModel, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (!TryGetValue(graphModel, GraphObjectPropertyName, out object graphObject) || graphObject == null)
                return false;

            if (!TryGetValue(graphObject, FilePathPropertyName, out string graphPath) || string.IsNullOrEmpty(graphPath))
                return false;

            graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(graphPath);
            return graph != null;
        }

        private static bool TryGetValue<T>(object source, string memberName, out T value)
        {
            value = default;
            if (source == null || string.IsNullOrEmpty(memberName))
                return false;

            var type = source.GetType();
            var property = type.GetProperty(memberName, InstanceBindings);
            if (property != null)
            {
                var rawValue = property.GetValue(source);
                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
            }

            var field = type.GetField(memberName, InstanceBindings);
            if (field != null)
            {
                var rawValue = field.GetValue(source);
                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
            }

            return false;
        }

        private static bool IsAssignableToTypeName(Type type, string typeName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, typeName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    [InitializeOnLoad]
    internal static class RenderGraphExecutionOrderSidebar
    {
        private static readonly Dictionary<int, SidebarState> s_states = new Dictionary<int, SidebarState>();
        private static double s_nextWindowScanTime;

        static RenderGraphExecutionOrderSidebar()
        {
            EditorApplication.delayCall += UpdateSidebars;
            EditorApplication.update += UpdateSidebars;
        }

        private static void UpdateSidebars()
        {
            var currentTime = EditorApplication.timeSinceStartup;
            if (currentTime >= s_nextWindowScanTime)
            {
                RegisterOpenWindows();
                s_nextWindowScanTime = currentTime + 0.5d;
            }

            var closedWindowIds = new List<int>();
            foreach (var pair in s_states)
            {
                if (!pair.Value.Refresh(currentTime))
                    closedWindowIds.Add(pair.Key);
            }

            foreach (var windowId in closedWindowIds)
            {
                if (s_states.TryGetValue(windowId, out var state))
                    state.Dispose();

                s_states.Remove(windowId);
            }
        }

        private static void RegisterOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || !RenderGraphEditorWindowReflectionUtility.IsGraphViewEditorWindow(window.GetType()))
                    continue;

                var windowId = window.GetInstanceID();
                if (s_states.ContainsKey(windowId))
                    continue;

                s_states.Add(windowId, new SidebarState(window));
            }
        }

        private sealed class SidebarState
        {
            private readonly EditorWindow m_Window;
            private readonly VisualElement m_Root;
            private readonly Label m_TitleLabel;
            private readonly Label m_StatusLabel;
            private readonly ScrollView m_ScrollView;
            private string m_Signature;
            private double m_NextRefreshTime;

            internal SidebarState(EditorWindow window)
            {
                m_Window = window;
                m_Root = CreateRoot();
                m_TitleLabel = CreateTitleLabel();
                m_StatusLabel = CreateStatusLabel();
                m_ScrollView = CreateScrollView();

                m_Root.Add(m_TitleLabel);
                m_Root.Add(m_StatusLabel);
                m_Root.Add(m_ScrollView);
            }

            internal bool Refresh(double currentTime)
            {
                if (m_Window == null)
                    return false;

                var container = RenderGraphEditorWindowReflectionUtility.GetGraphContainer(m_Window);
                if (container == null)
                    return true;

                EnsureAttached(container);

                if (!RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(m_Window, out var graph))
                {
                    m_Root.style.display = DisplayStyle.None;
                    m_Signature = null;
                    return true;
                }

                m_Root.style.display = DisplayStyle.Flex;

                if (currentTime < m_NextRefreshTime)
                    return true;

                m_NextRefreshTime = currentTime + 0.25d;

                try
                {
                    var compilation = RenderGraphCompiler.Compile(graph);
                    ApplyCompilation(graph, compilation);
                }
                catch (Exception ex)
                {
                    ApplyError(graph.Name, ex);
                }

                return true;
            }

            internal void Dispose()
            {
                m_Root.RemoveFromHierarchy();
            }

            private void EnsureAttached(VisualElement container)
            {
                if (m_Root.parent == container)
                    return;

                m_Root.RemoveFromHierarchy();
                container.Add(m_Root);
            }

            private void ApplyCompilation(RenderGraphEditorGraph graph, RenderGraphCompilationResult compilation)
            {
                var signature = BuildSignature(graph.Name, compilation.ExecutionOrder);
                if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                    return;

                m_Signature = signature;
                m_TitleLabel.text = string.IsNullOrWhiteSpace(graph.Name)
                    ? "Execution Order"
                    : $"Execution Order - {graph.Name}";
                m_StatusLabel.text = compilation.ExecutionOrder.Count == 0
                    ? "No compiled render passes."
                    : $"{compilation.ExecutionOrder.Count} compiled pass{(compilation.ExecutionOrder.Count == 1 ? string.Empty : "es")}.";

                m_ScrollView.Clear();
                if (compilation.ExecutionOrder.Count == 0)
                {
                    m_ScrollView.Add(CreateInfoLabel("Add valid RenderPass nodes to see the compiled execution order."));
                    return;
                }

                foreach (var passInfo in compilation.ExecutionOrder)
                    m_ScrollView.Add(CreatePassRow(passInfo));
            }

            private void ApplyError(string graphName, Exception exception)
            {
                var errorMessage = exception?.Message ?? "Unknown compilation error.";
                var signature = $"error:{graphName}:{errorMessage}";
                if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                    return;

                m_Signature = signature;
                m_TitleLabel.text = string.IsNullOrWhiteSpace(graphName)
                    ? "Execution Order"
                    : $"Execution Order - {graphName}";
                m_StatusLabel.text = "Failed to compile the current graph.";
                m_ScrollView.Clear();
                m_ScrollView.Add(CreateInfoLabel(errorMessage));
            }

            private static string BuildSignature(string graphName, IReadOnlyList<RenderGraphCompiledPassInfo> executionOrder)
            {
                if (executionOrder == null || executionOrder.Count == 0)
                    return $"{graphName}:empty";

                return string.Join("|", executionOrder.Select(passInfo =>
                    $"{graphName}:{passInfo.ExecutionIndex}:{passInfo.DisplayName}:{passInfo.PassTypeName}:{passInfo.EnableAsyncCompute}"));
            }

            private static VisualElement CreateRoot()
            {
                var root = new VisualElement
                {
                    name = "vivid-rendergraph-execution-order-sidebar",
                    pickingMode = PickingMode.Position,
                };

                root.style.position = Position.Absolute;
                root.style.top = 8f;
                root.style.right = 8f;
                root.style.bottom = 8f;
                root.style.width = 300f;
                root.style.paddingLeft = 12f;
                root.style.paddingRight = 12f;
                root.style.paddingTop = 12f;
                root.style.paddingBottom = 12f;
                root.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 0.94f);
                root.style.borderLeftWidth = 1f;
                root.style.borderRightWidth = 1f;
                root.style.borderTopWidth = 1f;
                root.style.borderBottomWidth = 1f;
                root.style.borderLeftColor = new Color(0.23f, 0.23f, 0.25f, 1f);
                root.style.borderRightColor = new Color(0.23f, 0.23f, 0.25f, 1f);
                root.style.borderTopColor = new Color(0.23f, 0.23f, 0.25f, 1f);
                root.style.borderBottomColor = new Color(0.23f, 0.23f, 0.25f, 1f);
                root.style.borderTopLeftRadius = 6f;
                root.style.borderTopRightRadius = 6f;
                root.style.borderBottomLeftRadius = 6f;
                root.style.borderBottomRightRadius = 6f;
                root.style.display = DisplayStyle.None;
                root.style.flexDirection = FlexDirection.Column;
                return root;
            }

            private static Label CreateTitleLabel()
            {
                var label = new Label("Execution Order");
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.fontSize = 13f;
                label.style.marginBottom = 4f;
                return label;
            }

            private static Label CreateStatusLabel()
            {
                var label = new Label();
                label.style.fontSize = 11f;
                label.style.color = new Color(0.75f, 0.75f, 0.78f, 1f);
                label.style.marginBottom = 8f;
                return label;
            }

            private static ScrollView CreateScrollView()
            {
                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.style.flexGrow = 1f;
                scrollView.style.marginTop = 2f;
                return scrollView;
            }

            private static VisualElement CreatePassRow(RenderGraphCompiledPassInfo passInfo)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.paddingTop = 8f;
                row.style.paddingBottom = 8f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.22f, 1f);

                var titleText = $"{passInfo.ExecutionIndex + 1}. {passInfo.DisplayName}";
                if (passInfo.EnableAsyncCompute)
                    titleText += " [Async]";

                var titleLabel = new Label(titleText);
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.whiteSpace = WhiteSpace.Normal;
                row.Add(titleLabel);

                if (!string.Equals(passInfo.DisplayName, passInfo.PassTypeName, StringComparison.Ordinal))
                {
                    var subtitleLabel = new Label(passInfo.PassTypeName);
                    subtitleLabel.style.fontSize = 11f;
                    subtitleLabel.style.color = new Color(0.72f, 0.72f, 0.75f, 1f);
                    subtitleLabel.style.marginTop = 2f;
                    subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(subtitleLabel);
                }

                return row;
            }

            private static Label CreateInfoLabel(string text)
            {
                var label = new Label(text);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = new Color(0.78f, 0.78f, 0.8f, 1f);
                label.style.paddingTop = 4f;
                return label;
            }
        }
    }
}
