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
            EditorApplication.wantsToQuit += RepairPersistedRestoreStateBeforeQuit;
            EditorApplication.delayCall += RegisterCallbacks;
            EditorApplication.update += RegisterCallbacks;
        }

        private static bool RepairPersistedRestoreStateBeforeQuit()
        {
            RenderGraphEditorWindowReflectionUtility.TryRepairPersistedInvalidSubgraphStacksFromUserLayouts();
            return true;
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
                graphView.RegisterCallback<ContextualMenuPopulateEvent>(OnContextualMenuPopulate, TrickleDown.TrickleDown);
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
            if (RenderPassNodeNavigationUtility.TryGetRenderPassNode(nodeModel, out var renderPassNode)
                && RenderPassNodeRenameUtility.IsTitleTarget(target, nodeView)
                && RenderPassNodeRenameUtility.BeginRename(nodeView, renderPassNode))
            {
                evt.StopImmediatePropagation();
                return;
            }

            if (!RenderPassNodeNavigationUtility.TryOpenPassScript(nodeModel))
                return;

            evt.StopImmediatePropagation();
        }

        private static void OnContextualMenuPopulate(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is not VisualElement target)
                return;

            var nodeView = FindAncestorNodeView(target);
            if (nodeView == null)
                return;

            var nodeModel = RenderGraphEditorWindowReflectionUtility.GetNodeModel(nodeView);
            if (!RenderPassNodeNavigationUtility.TryGetRenderPassNode(nodeModel, out var renderPassNode))
                return;

            evt.menu.AppendAction(
                "Rename",
                _ => RenderPassNodeRenameUtility.BeginRename(nodeView, renderPassNode),
                DropdownMenuAction.AlwaysEnabled);
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

    internal static class RenderPassNodeRenameUtility
    {
        private const string TitleElementName = "title";
        private const string TitleContainerElementName = "title-container";
        private const string TitleEditorName = "vivid-render-pass-title-editor";
        private const string UndoActionName = "Rename Render Pass";

        internal static bool Rename(RenderPassNodeData node, string requestedTitle)
        {
            if (node == null)
                return false;

            var title = ResolveTitle(node, requestedTitle);
            if (string.Equals(node.Title, title, StringComparison.Ordinal))
                return false;

            var graph = node.Graph;
            if (graph == null)
            {
                node.Title = title;
                return true;
            }

            graph.UndoBeginRecordGraph(UndoActionName);
            try
            {
                node.Title = title;
            }
            finally
            {
                graph.UndoEndRecordGraph();
            }

            return true;
        }

        internal static bool BeginRename(VisualElement nodeView, RenderPassNodeData node)
        {
            if (nodeView == null || node == null)
                return false;

            var existingEditor = nodeView.Q<TextField>(TitleEditorName);
            if (existingEditor != null)
            {
                existingEditor.Focus();
                existingEditor.SelectAll();
                return true;
            }

            var titleElement = nodeView.Q<VisualElement>(TitleElementName);
            var titleParent = titleElement?.parent;
            if (titleElement == null || titleParent == null)
                return false;

            var previousDisplay = titleElement.style.display;
            var editor = new TextField
            {
                name = TitleEditorName,
                value = node.Title ?? string.Empty,
                isDelayed = false,
            };

            editor.style.flexGrow = 1f;
            editor.style.minWidth = GetEditorMinWidth(titleElement);

            var finished = false;
            Action<bool> finish = null;
            finish = commit =>
            {
                if (finished)
                    return;

                finished = true;
                var requestedTitle = editor.value;

                titleElement.style.display = previousDisplay;
                editor.RemoveFromHierarchy();

                if (commit)
                    Rename(node, requestedTitle);

                if (titleElement is TextElement titleText)
                    titleText.text = node.Title ?? string.Empty;
            };

            editor.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    finish(true);
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    finish(false);
                    evt.StopImmediatePropagation();
                }
            });
            editor.RegisterCallback<FocusOutEvent>(_ => finish(true));
            editor.RegisterCallback<DetachFromPanelEvent>(_ => finish(false));

            var titleIndex = titleParent.IndexOf(titleElement);
            if (titleIndex >= 0)
                titleParent.Insert(titleIndex, editor);
            else
                titleParent.Add(editor);

            titleElement.style.display = DisplayStyle.None;
            editor.schedule.Execute(() =>
            {
                if (editor.panel == null)
                    return;

                editor.Focus();
                editor.SelectAll();
            });

            return true;
        }

        internal static bool IsTitleTarget(VisualElement target, VisualElement nodeView)
        {
            for (var current = target; current != null && current != nodeView; current = current.parent)
            {
                if (string.Equals(current.name, TitleEditorName, StringComparison.Ordinal))
                    return false;

                if (string.Equals(current.name, TitleElementName, StringComparison.Ordinal)
                    || string.Equals(current.name, TitleContainerElementName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveTitle(RenderPassNodeData node, string requestedTitle)
        {
            if (!string.IsNullOrWhiteSpace(requestedTitle))
                return requestedTitle.Trim();

            return node.GetPassType()?.Name ?? node.GetType().Name;
        }

        private static float GetEditorMinWidth(VisualElement titleElement)
        {
            const float defaultWidth = 120f;
            var resolvedWidth = titleElement.resolvedStyle.width;
            return float.IsNaN(resolvedWidth) || resolvedWidth <= 0f
                ? defaultWidth
                : Mathf.Max(defaultWidth, resolvedWidth);
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
        private const string CurrentGraphFieldName = "m_CurrentGraph";
        private const string LastOpenedGraphFieldName = "m_LastOpenedGraph";
        private const string CurrentGraphPropertyName = "CurrentGraph";
        private const string GraphReferencePropertyName = "GraphReference";
        private const string GraphReferenceTypeName = "Unity.GraphToolkit.Editor.GraphReference, UnityEditor.GraphToolkitModule";
        private const string LabelPropertyName = "Label";
        private const string ResolveGraphModelMethodName = "ResolveGraphModel";
        private const string ResolveSubGraphMethodName = "ResolveSubGraph";
        private const string GetSubGraphModelMethodName = "GetSubGraphModel";
        private const string GetGraphReferenceMethodName = "GetGraphReference";
        private const string GetGraphModelReferenceMethodName = "GetGraphModelReference";
        private const string GraphContainerFieldName = "m_GraphContainer";
        private const string NodeModelsPropertyName = "NodeModels";
        private const string BackingNodePropertyName = "Node";
        private const string GraphImplementationFieldName = "m_Implementation";
        private const string GraphObjectPropertyName = "GraphObject";
        private const string FilePathPropertyName = "FilePath";
        private const string DefaultGraphToolName = "UnnamedTool";
        private const string GraphToolkitWindowIdentifier = "Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp";
        private const string PersistedStateTypeName = "Unity.GraphToolkit.Editor.PersistedState, UnityEditor.GraphToolkitModule";
        private const string ToolStateComponentTypeName = "Unity.GraphToolkit.Editor.ToolStateComponent, UnityEditor.GraphToolkitModule";

        private static readonly BindingFlags InstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] PersistedGraphToolKeys = { null, DefaultGraphToolName };

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
                    if (TryRepairPersistedGraphRestoreState(windowId))
                        repairedCount++;
                }

                if (repairedCount > 0)
                {
                    FlushGraphToolkitPersistedState();
                    Debug.LogWarning(
                        $"[VividRP] Recovered {repairedCount} persisted RenderGraph editor window state(s) with invalid GraphToolkit restore data.");
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

            var hasCurrentGraph = TryGetCurrentGraphFromToolState(toolState, out var graph);
            if (!hasCurrentGraph)
                hasCurrentGraph = TryRestoreCurrentGraphModel(toolState, out graph);

            if (!HasInvalidFirstSubgraphModel(toolState))
                return hasCurrentGraph;

            if (!hasCurrentGraph && !HasRecoverableRenderGraphReference(toolState))
                return false;

            if (!TryClearSubgraphStack(toolState))
                return false;

            graphName = graph != null && !string.IsNullOrWhiteSpace(graph.Name) ? graph.Name : "RenderGraph";
            FlushGraphToolkitPersistedState();
            Debug.LogWarning(
                $"[VividRP] Recovered RenderGraph editor window '{graphName}' from an invalid GraphToolkit subgraph restore state. " +
                "The window was restored to the root graph.");
            return true;
        }

        private static bool TryRepairPersistedGraphRestoreState(Hash128 windowId)
        {
            var repaired = false;
            foreach (var graphToolKey in PersistedGraphToolKeys)
            {
                if (TryRepairPersistedGraphRestoreState(windowId, graphToolKey))
                    repaired = true;
            }

            return repaired;
        }

        private static bool TryRepairPersistedGraphRestoreState(Hash128 windowId, string graphToolKey)
        {
            var toolState = GetPersistedToolState(windowId, graphToolKey);
            if (toolState == null)
                return false;

            var repaired = false;
            var hasCurrentGraph = TryGetCurrentGraphFromToolState(toolState, out _);
            if (!hasCurrentGraph)
            {
                hasCurrentGraph = TryRestoreCurrentGraphModel(toolState, out _);
                repaired = hasCurrentGraph;
            }

            if (!HasSubgraphStackEntries(toolState))
            {
                if (repaired)
                    TryStorePersistedToolState(toolState, windowId, graphToolKey);

                return repaired;
            }

            if (!hasCurrentGraph && !HasRecoverableRenderGraphReference(toolState))
            {
                if (repaired)
                    TryStorePersistedToolState(toolState, windowId, graphToolKey);

                return repaired;
            }

            var clearedSubgraphStack = TryClearSubgraphStack(toolState);
            if (repaired || clearedSubgraphStack)
                TryStorePersistedToolState(toolState, windowId, graphToolKey);

            return repaired || clearedSubgraphStack;
        }

        private static object GetPersistedToolState(Hash128 windowId, string graphToolKey)
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
                    .Invoke(null, new object[] { null, windowId, graphToolKey });
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

        private static bool TryStorePersistedToolState(object toolState, Hash128 windowId, string graphToolKey)
        {
            if (toolState == null)
                return false;

            var persistedStateType = Type.GetType(PersistedStateTypeName);
            if (persistedStateType == null)
                return false;

            var method = persistedStateType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.Name == "StoreStateComponent"
                    && candidate.GetParameters().Length == 4);
            if (method == null)
                return false;

            try
            {
                // GetOrCreate normalizes a null component name to the component type name; StoreStateComponent does not.
                method.Invoke(null, new object[] { toolState, toolState.GetType().FullName, windowId, graphToolKey });
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

        private static bool TryRestoreCurrentGraphModel(object toolState, out RenderGraphEditorGraph graph)
        {
            graph = null;
            if (!TryFindRenderGraphReferencePath(toolState, out var graphPath))
                return false;

            try
            {
                graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(graphPath);
            }
            catch (Exception)
            {
                graph = null;
            }

            if (graph == null || !TryGetGraphModel(graph, out var graphModel))
                return false;

            TryGetValue(graphModel, GraphObjectPropertyName, out object graphObject);
            TryGetGraphReference(toolState, graphModel, out var graphReference);

            var restoredCurrentGraph = TrySetGraphInfo(
                CurrentGraphFieldName,
                toolState,
                graphModel,
                graphObject,
                graphReference,
                graph.Name);
            var restoredLastOpenedGraph = TrySetGraphInfo(
                LastOpenedGraphFieldName,
                toolState,
                graphModel,
                graphObject,
                graphReference,
                graph.Name);

            return restoredCurrentGraph || restoredLastOpenedGraph;
        }

        private static bool TryGetGraphModel(RenderGraphEditorGraph graph, out object graphModel)
        {
            graphModel = null;
            if (graph == null)
                return false;

            try
            {
                graphModel = typeof(Graph).GetField(GraphImplementationFieldName, InstanceBindings)?.GetValue(graph);
            }
            catch (Exception)
            {
                graphModel = null;
            }

            return graphModel != null;
        }

        private static bool TryGetGraphReference(object toolState, object graphModel, out object graphReference)
        {
            graphReference = null;
            if (graphModel == null)
                return false;

            var graphReferenceType = Type.GetType(GraphReferenceTypeName);
            var graphModelType = graphModel.GetType();
            var constructor = graphReferenceType?.GetConstructors(InstanceBindings)
                .FirstOrDefault(candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(graphModelType);
                });
            if (constructor != null)
            {
                try
                {
                    graphReference = constructor.Invoke(new[] { graphModel });
                    return graphReference != null;
                }
                catch (Exception)
                {
                    graphReference = null;
                }
            }

            if (TryInvokeMethod(toolState, GetGraphModelReferenceMethodName, new[] { graphModel }, out graphReference)
                && graphReference != null)
            {
                return true;
            }

            var method = graphModel.GetType().GetMethod(GetGraphReferenceMethodName, InstanceBindings);
            if (method == null)
                return false;

            try
            {
                graphReference = method.Invoke(graphModel, new object[] { true });
                return graphReference != null;
            }
            catch (TargetInvocationException)
            {
                graphReference = null;
                return false;
            }
            catch (Exception)
            {
                graphReference = null;
                return false;
            }
        }

        private static bool TrySetGraphInfo(
            string fieldName,
            object toolState,
            object graphModel,
            object graphObject,
            object graphReference,
            string graphLabel)
        {
            if (toolState == null || graphModel == null)
                return false;

            var field = toolState.GetType().GetField(fieldName, InstanceBindings);
            if (field == null)
                return false;

            var graphInfo = field.GetValue(toolState);
            if (graphInfo == null)
                return false;

            try
            {
                var graphInfoType = graphInfo.GetType();
                var graphModelProperty = graphInfoType.GetProperty(GraphModelPropertyName, InstanceBindings);
                if (graphModelProperty == null)
                    return false;

                graphModelProperty.SetValue(graphInfo, graphModel);

                var graphReferenceProperty = graphInfoType.GetProperty(GraphReferencePropertyName, InstanceBindings);
                if (graphReferenceProperty != null && graphReference != null)
                    graphReferenceProperty.SetValue(graphInfo, graphReference);

                var labelProperty = graphInfoType.GetProperty(LabelPropertyName, InstanceBindings);
                if (labelProperty != null && !string.IsNullOrWhiteSpace(graphLabel))
                    labelProperty.SetValue(graphInfo, graphLabel);

                var graphObjectProperty = graphInfoType.GetProperty(GraphObjectPropertyName, InstanceBindings);
                if (graphObjectProperty != null && graphObject != null)
                    graphObjectProperty.SetValue(graphInfo, graphObject);

                field.SetValue(toolState, graphInfo);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool CanRecoverInvalidSubgraphStack(object toolState)
        {
            if (toolState == null || !HasInvalidFirstSubgraphModel(toolState))
                return false;

            return TryGetCurrentGraphFromToolState(toolState, out _) || HasRecoverableRenderGraphReference(toolState);
        }

        private static bool HasSubgraphStackEntries(object toolState)
        {
            return TryGetSubgraphStack(toolState, out var subgraphStack) && GetCount(subgraphStack) > 0;
        }

        private static bool HasInvalidFirstSubgraphModel(object toolState)
        {
            if (!TryGetSubgraphStack(toolState, out var subgraphStack) || GetCount(subgraphStack) <= 0)
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

        private static bool TryGetSubgraphStack(object toolState, out object subgraphStack)
        {
            if (TryGetValue(toolState, SubgraphStackPropertyName, out subgraphStack) && subgraphStack != null)
                return true;

            return TryGetValue(toolState, SubgraphStackFieldName, out subgraphStack) && subgraphStack != null;
        }

        private static bool HasRecoverableRenderGraphReference(object toolState)
        {
            return TryFindRenderGraphReferencePath(toolState, out _);
        }

        private static bool TryFindRenderGraphReferencePath(object toolState, out string path)
        {
            if (toolState == null)
            {
                path = null;
                return false;
            }

            if (TryGetRenderGraphReferencePath(toolState, CurrentGraphPropertyName, out path)
                || TryGetRenderGraphReferencePath(toolState, CurrentGraphFieldName, out path)
                || TryGetRenderGraphReferencePath(toolState, LastOpenedGraphFieldName, out path))
                return true;

            if (!TryGetValue(toolState, SubgraphStackFieldName, out System.Collections.IEnumerable graphInfos) || graphInfos == null)
                return false;

            foreach (var graphInfo in graphInfos)
            {
                if (TryGetRenderGraphReferencePath(graphInfo, GraphReferencePropertyName, out path))
                    return true;
            }

            path = null;
            return false;
        }

        private static bool TryGetRenderGraphReferencePath(object source, string memberName, out string path)
        {
            path = null;
            if (!TryGetValue(source, memberName, out object referenceSource) || referenceSource == null)
                return false;

            if (TryGetValue(referenceSource, FilePathPropertyName, out string directPath)
                && IsRenderGraphAssetPath(directPath))
            {
                path = directPath;
                return true;
            }

            if (TryGetValue(referenceSource, GraphReferencePropertyName, out object graphReference)
                && graphReference != null
                && TryGetValue(graphReference, FilePathPropertyName, out string graphPath)
                && IsRenderGraphAssetPath(graphPath))
            {
                path = graphPath;
                return true;
            }

            return false;
        }

        private static bool IsRenderGraphAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith($".{RenderGraphEditorGraph.AssetExtension}", StringComparison.OrdinalIgnoreCase);
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
            var yielded = new HashSet<Hash128>();
            foreach (var layoutPath in EnumerateGraphToolkitLayoutPaths())
            {
                foreach (var windowId in EnumerateGraphToolkitWindowIds(layoutPath))
                {
                    if (yielded.Add(windowId))
                        yield return windowId;
                }
            }
        }

        internal static IEnumerable<string> EnumerateGraphToolkitLayoutPaths()
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layoutDirectory in EnumerateLayoutDirectories())
            {
                if (string.IsNullOrEmpty(layoutDirectory) || !Directory.Exists(layoutDirectory))
                    continue;

                foreach (var layoutPath in Directory.EnumerateFiles(layoutDirectory, "*.dwlt", SearchOption.TopDirectoryOnly))
                {
                    if (yielded.Add(layoutPath))
                        yield return layoutPath;
                }

                foreach (var layoutPath in Directory.EnumerateFiles(layoutDirectory, "*.wlt", SearchOption.TopDirectoryOnly))
                {
                    if (yielded.Add(layoutPath))
                        yield return layoutPath;
                }
            }
        }

        private static IEnumerable<string> EnumerateLayoutDirectories()
        {
            var projectLayoutDirectory = GetProjectLayoutDirectory();
            if (!string.IsNullOrEmpty(projectLayoutDirectory))
                yield return projectLayoutDirectory;

            var unityPreferencesLayoutDirectory = GetUnityPreferencesLayoutDirectory();
            if (string.IsNullOrEmpty(unityPreferencesLayoutDirectory))
                yield break;

            yield return Path.Combine(unityPreferencesLayoutDirectory, "current");
            yield return Path.Combine(unityPreferencesLayoutDirectory, "default");
            yield return Path.Combine(unityPreferencesLayoutDirectory, "safe_mode");
        }

        private static string GetProjectLayoutDirectory()
        {
            if (string.IsNullOrEmpty(Application.dataPath))
                return null;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? null
                : Path.Combine(projectRoot, "UserSettings", "Layouts");
        }

        private static string GetUnityPreferencesLayoutDirectory()
        {
            var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrEmpty(applicationData)
                ? null
                : Path.Combine(applicationData, "Unity", "Editor-5.x", "Preferences", "Layouts");
        }

        internal static IEnumerable<Hash128> EnumerateGraphToolkitWindowIds(string layoutPath)
        {
            if (string.IsNullOrEmpty(layoutPath) || !File.Exists(layoutPath))
                yield break;

            var lines = File.ReadAllLines(layoutPath);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (!lines[lineIndex].Contains(GraphToolkitWindowIdentifier))
                    continue;

                var blockEnd = FindSerializedObjectBlockEnd(lines, lineIndex + 1);
                if (TryReadWindowHash(lines, lineIndex + 1, blockEnd, out var windowHash))
                    yield return windowHash;
                else if (TryReadWindowId(lines, lineIndex + 1, blockEnd, out var windowId))
                    yield return windowId;

                lineIndex = blockEnd - 1;
            }
        }

        private static int FindSerializedObjectBlockEnd(string[] lines, int startIndex)
        {
            for (var lineIndex = startIndex; lineIndex < lines.Length; lineIndex++)
            {
                if (lines[lineIndex].StartsWith("--- !u!", StringComparison.Ordinal))
                    return lineIndex;
            }

            return lines.Length;
        }

        private static bool TryReadWindowHash(string[] lines, int startIndex, int endIndex, out Hash128 windowHash)
        {
            windowHash = default;
            var inWindowHash = false;

            for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
            {
                var trimmed = lines[lineIndex].Trim();
                if (trimmed.StartsWith("m_WindowHash:", StringComparison.Ordinal))
                {
                    inWindowHash = true;
                    continue;
                }

                if (inWindowHash
                    && trimmed.StartsWith("Hash:", StringComparison.Ordinal)
                    && TryParseLayoutHash128(trimmed, out windowHash))
                    return true;
            }

            return false;
        }

        private static bool TryReadWindowId(string[] lines, int startIndex, int endIndex, out Hash128 windowId)
        {
            windowId = default;
            ulong value0 = 0;
            var inWindowId = false;
            var hasValue0 = false;

            for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
            {
                var trimmed = lines[lineIndex].Trim();
                if (trimmed.StartsWith("m_WindowID:", StringComparison.Ordinal))
                {
                    inWindowId = true;
                    continue;
                }

                if (!inWindowId)
                    continue;

                if (trimmed.StartsWith("m_Value0:", StringComparison.Ordinal))
                {
                    hasValue0 = TryParseLayoutUlong(trimmed, out value0);
                    continue;
                }

                if (hasValue0
                    && trimmed.StartsWith("m_Value1:", StringComparison.Ordinal)
                    && TryParseLayoutUlong(trimmed, out var value1))
                {
                    windowId = new Hash128(value0, value1);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseLayoutHash128(string line, out Hash128 value)
        {
            value = default;
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
                return false;

            try
            {
                value = Hash128.Parse(line.Substring(separatorIndex + 1).Trim());
                return value.isValid;
            }
            catch (Exception)
            {
                value = default;
                return false;
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
