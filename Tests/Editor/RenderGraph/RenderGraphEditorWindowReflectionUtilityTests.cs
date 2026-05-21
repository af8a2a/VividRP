using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
#if UNITY_6000_6_OR_NEWER
    internal sealed class RenderGraphEditorWindowReflectionUtilityTests
    {
        [Test]
        public void TryGetCurrentGraph_UsesGraphWindowGraph_WhenWindowExposesRenderGraph()
        {
            var window = ScriptableObject.CreateInstance<TestGraphWindow>();
            var graph = new RenderGraphEditorGraph();

            try
            {
                window.GraphValue = graph;

                var result = RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(
                    window,
                    out var resolvedGraph);

                Assert.That(result, Is.True);
                Assert.That(resolvedGraph, Is.SameAs(graph));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TryGetCurrentGraph_ReturnsFalse_WhenGraphWindowHasNoRenderGraph()
        {
            var window = ScriptableObject.CreateInstance<TestGraphWindow>();

            try
            {
                var result = RenderGraphEditorWindowReflectionUtility.TryGetCurrentGraph(
                    window,
                    out var resolvedGraph);

                Assert.That(result, Is.False);
                Assert.That(resolvedGraph, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private sealed class TestGraphWindow : EditorWindow, IGraphWindow
        {
            internal Graph GraphValue { get; set; }

            public Graph Graph => GraphValue;
        }
    }
#endif
}
