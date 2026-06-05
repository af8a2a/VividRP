using System;
using System.Collections.Generic;
using System.IO;
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
            RenderGraphEditorWindowReflectionUtility.TryRepairPersistedInvalidSubgraphStacksFromUserLayouts();
            EditorApplication.delayCall += RegisterCallbacks;
            EditorApplication.update += RegisterCallbacks;
        }

        private static void RegisterCallbacks()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                RenderGraphEditorWindowReflectionUtility.TryRepairInvalidSubgraphStack(window, out _);

                try
                {
                    if (!RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(window, out _))
                        continue;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VividRP] Failed to inspect a GraphToolkit window for RenderGraph callbacks: {ex.Message}");
                    continue;
                }

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
        private const string GraphToolPropertyName = "GraphTool";
        private const string ToolStatePropertyName = "ToolState";
        private const string SubgraphStackPropertyName = "SubgraphStack";
        private const string SubgraphStackFieldName = "m_SubgraphStack";
        private const string ResolveGraphModelMethodName = "ResolveGraphModel";
        private const string ResolveSubGraphMethodName = "ResolveSubGraph";
        private const string GetSubGraphModelMethodName = "GetSubGraphModel";
        private const string GraphContainerFieldName = "m_GraphContainer";
        private const string NodeModelsPropertyName = "NodeModels";
        private const string BackingNodePropertyName = "Node";
        private const string GraphObjectPropertyName = "GraphObject";
        private const string FilePathPropertyName = "FilePath";
        private const string DefaultGraphToolName = "UnnamedTool";
        private const string GraphToolkitWindowIdentifier = "Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp";
        private const string PersistedStateTypeName = "Unity.GraphToolkit.Editor.PersistedState, UnityEditor.GraphToolkitModule";
        private const string ToolStateComponentTypeName = "Unity.GraphToolkit.Editor.ToolStateComponent, UnityEditor.GraphToolkitModule";

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

            try
            {
                return window.GetType().GetProperty(GraphViewPropertyName, InstanceBindings)?.GetValue(window) as VisualElement;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
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

            try
            {
                return nodeView.GetType().GetProperty("NodeModel", InstanceBindings)?.GetValue(nodeView);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool TryGetCurrentGraph(EditorWindow window, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (window == null)
                return false;

            TryRepairInvalidSubgraphStack(window, out _);

#if UNITY_6000_6_OR_NEWER
            if (window is IGraphWindow graphWindow)
            {
                try
                {
                    graph = graphWindow.Graph as RenderGraphEditorGraph;
                }
                catch (Exception)
                {
                    graph = null;
                }

                return graph != null;
            }
#endif

            return TryGetCurrentGraph(GetGraphView(window), out graph);
        }

        internal static bool TryGetCurrentGraph(VisualElement graphView, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (graphView == null)
                return false;

            object graphModel;
            try
            {
                graphModel = graphView.GetType().GetProperty(GraphModelPropertyName, InstanceBindings)?.GetValue(graphView);
            }
            catch (TargetInvocationException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }

            if (graphModel == null)
                return false;

            return TryResolveGraph(graphModel, out graph);
        }

        internal static int TryRepairPersistedInvalidSubgraphStacksFromUserLayouts()
        {
            try
            {
                var repairedCount = 0;
                foreach (var windowId in EnumerateGraphToolkitWindowIdsFromUserLayouts())
                {
                    if (TryRepairPersistedInvalidSubgraphStack(windowId))
                        repairedCount++;
                }

                if (repairedCount > 0)
                {
                    FlushGraphToolkitPersistedState();
                    Debug.LogWarning(
                        $"[VividRP] Recovered {repairedCount} persisted RenderGraph editor window state(s) with invalid GraphToolkit subgraph stacks.");
                }

                return repairedCount;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VividRP] Failed to inspect persisted GraphToolkit window state: {ex.Message}");
                return 0;
            }
        }

        internal static bool TryRepairInvalidSubgraphStack(EditorWindow window, out string graphName)
        {
            graphName = null;
            if (window == null || !IsGraphViewEditorWindow(window.GetType()))
                return false;

            if (!TryGetValue(window, GraphToolPropertyName, out object graphTool) || graphTool == null)
                return false;

            if (!TryGetValue(graphTool, ToolStatePropertyName, out object toolState) || toolState == null)
                return false;

            if (!HasInvalidFirstSubgraphModel(toolState))
                return false;

            if (!TryGetCurrentGraphFromToolState(toolState, out var graph))
                return false;

            if (!TryClearSubgraphStack(toolState))
                return false;

            graphName = string.IsNullOrWhiteSpace(graph.Name) ? "RenderGraph" : graph.Name;
            FlushGraphToolkitPersistedState();
            Debug.LogWarning(
                $"[VividRP] Recovered RenderGraph editor window '{graphName}' from an invalid GraphToolkit subgraph restore state. " +
                "The window was restored to the root graph.");
            return true;
        }

        private static bool TryRepairPersistedInvalidSubgraphStack(Hash128 windowId)
        {
            var toolState = GetPersistedToolState(windowId);
            if (toolState == null)
                return false;

            if (!HasInvalidFirstSubgraphModel(toolState))
                return false;

            if (!TryGetCurrentGraphFromToolState(toolState, out _))
                return false;

            return TryClearSubgraphStack(toolState);
        }

        private static object GetPersistedToolState(Hash128 windowId)
        {
            var persistedStateType = Type.GetType(PersistedStateTypeName);
            var toolStateType = Type.GetType(ToolStateComponentTypeName);
            if (persistedStateType == null || toolStateType == null)
                return null;

            var method = persistedStateType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.Name == "GetOrCreatePersistedStateComponent"
                    && candidate.IsGenericMethodDefinition
                    && candidate.GetParameters().Length == 3);
            if (method == null)
                return null;

            try
            {
                return method.MakeGenericMethod(toolStateType)
                    .Invoke(null, new object[] { null, windowId, DefaultGraphToolName });
            }
            catch (TargetInvocationException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
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

        private static bool TryGetCurrentGraphFromToolState(object toolState, out RenderGraphEditorGraph graph)
        {
            graph = null;

            if (!TryInvokeMethod(toolState, ResolveGraphModelMethodName, null, out var graphModel)
                && !TryGetValue(toolState, GraphModelPropertyName, out graphModel))
            {
                return false;
            }

            return graphModel != null && TryResolveGraph(graphModel, out graph);
        }

        private static bool HasInvalidFirstSubgraphModel(object toolState)
        {
            if (!TryGetValue(toolState, SubgraphStackPropertyName, out object subgraphStack) || GetCount(subgraphStack) <= 0)
                return false;

            if (TryInvokeMethod(toolState, ResolveSubGraphMethodName, new object[] { 0 }, out var resolvedSubgraphModel))
                return resolvedSubgraphModel == null;

            var method = toolState.GetType().GetMethod(GetSubGraphModelMethodName, InstanceBindings);
            if (method == null)
                return false;

            try
            {
                return method.Invoke(toolState, new object[] { 0 }) == null;
            }
            catch (TargetInvocationException)
            {
                return true;
            }
        }

        private static bool TryClearSubgraphStack(object toolState)
        {
            var field = toolState.GetType().GetField(SubgraphStackFieldName, InstanceBindings);
            if (field?.GetValue(toolState) is not System.Collections.IList subgraphStack)
                return false;

            subgraphStack.Clear();
            return true;
        }

        private static int GetCount(object collection)
        {
            if (collection == null)
                return 0;

            if (collection is System.Collections.ICollection legacyCollection)
                return legacyCollection.Count;

            var count = collection.GetType().GetProperty("Count", InstanceBindings)?.GetValue(collection);
            return count is int value ? value : 0;
        }

        private static void FlushGraphToolkitPersistedState()
        {
            try
            {
                var persistedStateType = Type.GetType("Unity.GraphToolkit.Editor.PersistedState, UnityEditor.GraphToolkitModule");
                persistedStateType?.GetMethod("Flush", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            catch (TargetInvocationException)
            {
            }
            catch (Exception)
            {
            }
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
                object rawValue;
                try
                {
                    rawValue = property.GetValue(source);
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }

                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
            }

            var field = type.GetField(memberName, InstanceBindings);
            if (field != null)
            {
                object rawValue;
                try
                {
                    rawValue = field.GetValue(source);
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }

                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
            }

            return false;
        }

        private static bool TryInvokeMethod(object target, string methodName, object[] arguments, out object result)
        {
            result = null;
            if (target == null)
                return false;

            var method = target.GetType().GetMethod(methodName, InstanceBindings);
            if (method == null)
                return false;

            try
            {
                result = method.Invoke(target, arguments ?? Array.Empty<object>());
                return true;
            }
            catch (TargetInvocationException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerable<Hash128> EnumerateGraphToolkitWindowIdsFromUserLayouts()
        {
            var layoutDirectory = GetUserLayoutDirectory();
            if (string.IsNullOrEmpty(layoutDirectory) || !Directory.Exists(layoutDirectory))
                yield break;

            var yielded = new HashSet<Hash128>();
            foreach (var layoutPath in Directory.EnumerateFiles(layoutDirectory, "*.dwlt", SearchOption.TopDirectoryOnly))
            {
                foreach (var windowId in EnumerateGraphToolkitWindowIds(layoutPath))
                {
                    if (yielded.Add(windowId))
                        yield return windowId;
                }
            }
        }

        private static string GetUserLayoutDirectory()
        {
            if (string.IsNullOrEmpty(Application.dataPath))
                return null;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? null
                : Path.Combine(projectRoot, "UserSettings", "Layouts");
        }

        private static IEnumerable<Hash128> EnumerateGraphToolkitWindowIds(string layoutPath)
        {
            if (string.IsNullOrEmpty(layoutPath) || !File.Exists(layoutPath))
                yield break;

            var inGraphToolkitWindow = false;
            ulong value0 = 0;
            ulong value1 = 0;
            var hasValue0 = false;

            foreach (var line in File.ReadLines(layoutPath))
            {
                if (line.Contains(GraphToolkitWindowIdentifier))
                {
                    inGraphToolkitWindow = true;
                    value0 = 0;
                    value1 = 0;
                    hasValue0 = false;
                    continue;
                }

                if (!inGraphToolkitWindow)
                    continue;

                var trimmed = line.Trim();
                if (trimmed.StartsWith("m_Value0:", StringComparison.Ordinal))
                {
                    hasValue0 = TryParseLayoutUlong(trimmed, out value0);
                    continue;
                }

                if (hasValue0
                    && trimmed.StartsWith("m_Value1:", StringComparison.Ordinal)
                    && TryParseLayoutUlong(trimmed, out value1))
                {
                    yield return new Hash128(value0, value1);
                    inGraphToolkitWindow = false;
                }
            }
        }

        private static bool TryParseLayoutUlong(string line, out ulong value)
        {
            value = 0;
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
                return false;

            return ulong.TryParse(line.Substring(separatorIndex + 1).Trim(), out value);
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
            RenderGraphEditorWindowReflectionUtility.TryRepairPersistedInvalidSubgraphStacksFromUserLayouts();
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
                try
                {
                    if (window == null || !RenderGraphEditorWindowReflectionUtility.IsGraphViewEditorWindow(window.GetType()))
                        continue;

                    RenderGraphEditorWindowReflectionUtility.TryRepairInvalidSubgraphStack(window, out _);

                    if (window.overlayCanvas == null)
                        continue;

                    if (!RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(window, out _))
                        continue;

                    var graphView = RenderGraphEditorWindowReflectionUtility.GetGraphView(window);
                    if (graphView?.panel == null)
                        continue;

                    if (window.TryGetOverlay(RenderGraphExecutionOrderOverlay.OverlayId, out _))
                        continue;

                    var overlay = new RenderGraphExecutionOrderOverlay();
                    window.overlayCanvas.Add(overlay);
                    overlay.displayed = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VividRP] Failed to register RenderGraph execution order overlay: {ex.Message}");
                }
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
