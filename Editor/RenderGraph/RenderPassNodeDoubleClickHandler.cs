using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VividRP.Editor.RenderGraph
{
    [InitializeOnLoad]
    internal static class RenderPassNodeDoubleClickHandler
    {
        private const string GraphViewEditorWindowTypeName = "Unity.GraphToolkit.Editor.GraphViewEditorWindow";
        private const string NodeViewTypeName = "Unity.GraphToolkit.Editor.NodeView";
        private const string NodeModelPropertyName = "NodeModel";
        private static readonly BindingFlags InstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
                var graphView = GetGraphView(window);
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

            var nodeView = FindAncestorByTypeName(target, NodeViewTypeName);
            if (nodeView == null)
                return;

            var nodeModel = nodeView.GetType().GetProperty(NodeModelPropertyName, InstanceBindings)?.GetValue(nodeView);
            if (!RenderPassNodeNavigationUtility.TryOpenPassScript(nodeModel))
                return;

            evt.StopImmediatePropagation();
        }

        private static void OnGraphViewDetached(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is VisualElement graphView)
                s_registeredGraphViews.Remove(graphView);
        }

        private static VisualElement GetGraphView(EditorWindow window)
        {
            if (!IsGraphViewEditorWindow(window.GetType()))
                return null;

            var property = window.GetType().GetProperty("GraphView", InstanceBindings);
            return property?.GetValue(window) as VisualElement;
        }

        private static bool IsGraphViewEditorWindow(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, GraphViewEditorWindowTypeName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static VisualElement FindAncestorByTypeName(VisualElement element, string typeName)
        {
            for (var current = element; current != null; current = current.parent)
            {
                for (var currentType = current.GetType(); currentType != null; currentType = currentType.BaseType)
                {
                    if (string.Equals(currentType.FullName, typeName, StringComparison.Ordinal))
                        return current;
                }
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
}
