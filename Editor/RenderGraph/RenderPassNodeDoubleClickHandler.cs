using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.Overlays;
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
    internal static class RenderGraphExecutionOrderOverlayBootstrap
    {
        private static double s_nextScanTime;

        static RenderGraphExecutionOrderOverlayBootstrap()
        {
            EditorApplication.delayCall += EnsureOverlays;
            EditorApplication.update += EnsureOverlays;
        }

        private static void EnsureOverlays()
        {
            var currentTime = EditorApplication.timeSinceStartup;
            if (currentTime < s_nextScanTime)
                return;

            s_nextScanTime = currentTime + 0.5d;
            RegisterOpenWindows();
        }

        private static void RegisterOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || !RenderGraphEditorWindowReflectionUtility.IsGraphViewEditorWindow(window.GetType()))
                    continue;

                if (window.overlayCanvas == null)
                    continue;

                var hasCurrentGraph = RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(window, out _);
                if (window.TryGetOverlay(RenderGraphExecutionOrderOverlay.OverlayId, out var existingOverlay))
                {
                    existingOverlay.displayed = hasCurrentGraph;
                    continue;
                }

                if (!hasCurrentGraph)
                    continue;

                var overlay = new RenderGraphExecutionOrderOverlay();
                window.overlayCanvas.Add(overlay);
                overlay.displayed = true;
            }
        }
    }

    [Overlay(
        typeof(EditorWindow),
        OverlayId,
        "Execution Order",
        false,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel,
        defaultWidth = 320f,
        defaultHeight = 420f,
        minWidth = 240f,
        minHeight = 180f,
        group = "VividRP")]
    internal sealed class RenderGraphExecutionOrderOverlay : Overlay
    {
        internal const string OverlayId = "vividrp-rendergraph-execution-order";

        public override VisualElement CreatePanelContent()
        {
            return new RenderGraphExecutionOrderOverlayContent(this);
        }
    }

    internal sealed class RenderGraphExecutionOrderOverlayContent : VisualElement
    {
        private readonly RenderGraphExecutionOrderOverlay m_Overlay;
        private readonly Label m_TitleLabel;
        private readonly Label m_StatusLabel;
        private readonly ScrollView m_ScrollView;
        private string m_Signature;

        internal RenderGraphExecutionOrderOverlayContent(RenderGraphExecutionOrderOverlay overlay)
        {
            m_Overlay = overlay;
            style.flexGrow = 1f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 6f;
            style.paddingBottom = 6f;

            m_TitleLabel = CreateTitleLabel();
            m_StatusLabel = CreateStatusLabel();
            m_ScrollView = CreateScrollView();

            Add(m_TitleLabel);
            Add(m_StatusLabel);
            Add(m_ScrollView);

            schedule.Execute(Refresh).Every(250);
        }

        private void Refresh()
        {
            if (!RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(m_Overlay.containerWindow, out var graph))
            {
                ApplyUnavailableState();
                return;
            }

            try
            {
                ApplyCompilation(graph, RenderGraphCompiler.Compile(graph));
            }
            catch (Exception ex)
            {
                ApplyError(graph.Name, ex);
            }
        }

        private void ApplyUnavailableState()
        {
            const string signature = "unavailable";
            if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                return;

            m_Signature = signature;
            m_TitleLabel.text = "Current Graph";
            m_StatusLabel.text = "Open a VividRP RenderGraph to inspect its compiled order.";
            m_ScrollView.Clear();
        }

        private void ApplyCompilation(RenderGraphEditorGraph graph, RenderGraphCompilationResult compilation)
        {
            var signature = BuildSignature(graph.Name, compilation.ExecutionOrder);
            if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                return;

            m_Signature = signature;
            m_TitleLabel.text = string.IsNullOrWhiteSpace(graph.Name) ? "Current Graph" : graph.Name;
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
            m_TitleLabel.text = string.IsNullOrWhiteSpace(graphName) ? "Current Graph" : graphName;
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

        private static Label CreateTitleLabel()
        {
            var label = new Label("Current Graph");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 13f;
            label.style.marginBottom = 4f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label CreateStatusLabel()
        {
            var label = new Label();
            label.style.fontSize = 11f;
            label.style.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            label.style.marginBottom = 8f;
            label.style.whiteSpace = WhiteSpace.Normal;
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
