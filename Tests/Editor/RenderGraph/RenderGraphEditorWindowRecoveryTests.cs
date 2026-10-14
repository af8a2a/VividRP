using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    internal sealed class RenderGraphEditorWindowRecoveryTests
    {
        [Test]
        public void ParentNavigation_UsesMostRecentAncestor_AndPreservesItsHistoryIndex()
        {
            var root = new NavigationGraph("Root");
            var parent = new NavigationGraph("Parent", root);
            var state = new NavigationState(new NavigationGraph("Child", parent), root, parent);

            Assert.That(RenderGraphParentNavigation.TryGetParent(state, out var target, out var index, out var title), Is.True);
            Assert.That(target, Is.SameAs(parent));
            Assert.That(index, Is.EqualTo(1));
            Assert.That(title, Is.EqualTo("Parent"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParentNavigation_UsesLocalParent_WhenHistoryIsMissingOrInvalid(bool invalidHistory)
        {
            var parent = new NavigationGraph("Parent");
            var state = new NavigationState(new NavigationGraph("Child", parent),
                invalidHistory ? new NavigationGraph[] { null } : new NavigationGraph[0]);

            Assert.That(RenderGraphParentNavigation.TryGetParent(state, out var target, out var index, out _), Is.True);
            Assert.That(target, Is.SameAs(parent));
            Assert.That(index, Is.EqualTo(-1));
        }

        [Test]
        public void ParentNavigation_UsesIncomingHistory_ForExternalSubgraphWithoutLocalParent()
        {
            var parent = new NavigationGraph("Caller");
            var state = new NavigationState(new NavigationGraph("External"), parent);

            Assert.That(RenderGraphParentNavigation.TryGetParent(state, out var target, out var index, out _), Is.True);
            Assert.That(target, Is.SameAs(parent));
            Assert.That(index, Is.Zero);
        }

        [Test]
        public void ParentNavigation_IsUnavailableAtRoot()
        {
            Assert.That(RenderGraphParentNavigation.TryGetParent(new NavigationState(new NavigationGraph("Root")),
                out _, out _, out _), Is.False);
            Assert.That(RenderGraphParentNavigation.TryGetParent(null, out _, out _, out _), Is.False);
        }

        private sealed class NavigationGraph
        {
            public string Name { get; }
            public NavigationGraph ParentGraph { get; }
            internal NavigationGraph(string name, NavigationGraph parent = null)
            {
                Name = name;
                ParentGraph = parent;
            }
        }

        private sealed class NavigationState
        {
            private readonly List<NavigationGraph> m_SubgraphStack;
            public NavigationGraph GraphModel { get; }
            internal NavigationState(NavigationGraph current, params NavigationGraph[] history)
            {
                GraphModel = current;
                m_SubgraphStack = new List<NavigationGraph>(history);
            }
            public NavigationGraph ResolveSubGraph(int index) => m_SubgraphStack[index];
            public string GetSubgraphLabel(int index) => m_SubgraphStack[index]?.Name;
        }

        [Test]
        public void CanRecoverInvalidSubgraphStack_ReturnsTrue_WhenCurrentGraphPathIsRenderGraphAsset()
        {
            var toolState = new TestToolState("Assets/TestGraph.vrdg", string.Empty, string.Empty);

            Assert.That(RenderGraphEditorWindowReflectionUtility.CanRecoverInvalidSubgraphStack(toolState), Is.True);
        }

        [Test]
        public void CanRecoverInvalidSubgraphStack_ReturnsTrue_WhenSubgraphPathIsRenderGraphAsset()
        {
            var toolState = new TestToolState(string.Empty, string.Empty, "Assets/TestGraph.vrdg");

            Assert.That(RenderGraphEditorWindowReflectionUtility.CanRecoverInvalidSubgraphStack(toolState), Is.True);
        }

        [Test]
        public void CanRecoverInvalidSubgraphStack_ReturnsTrue_WhenSubgraphStackPropertyThrows()
        {
            var toolState = new TestToolState("Assets/TestGraph.vrdg", string.Empty, string.Empty, true);

            Assert.That(RenderGraphEditorWindowReflectionUtility.CanRecoverInvalidSubgraphStack(toolState), Is.True);
        }

        [Test]
        public void CanRecoverInvalidSubgraphStack_ReturnsFalse_WhenNoRenderGraphPathCanBeFound()
        {
            var toolState = new TestToolState(string.Empty, "Assets/OtherGraph.asset", string.Empty);

            Assert.That(RenderGraphEditorWindowReflectionUtility.CanRecoverInvalidSubgraphStack(toolState), Is.False);
        }

        [Test]
        public void TryLoadGraphFromGraphObject_IgnoresNonRenderGraphAsset()
        {
            var graphModel = new TestGraphModel("Assets/Vivid Material Graph.vmatg");

            bool result = RenderGraphEditorWindowReflectionUtility.TryLoadGraphFromGraphObject(
                graphModel,
                out RenderGraphEditorGraph graph);

            Assert.That(result, Is.False);
            Assert.That(graph, Is.Null);
        }

        [Test]
        public void EnumerateGraphToolkitWindowIds_UsesWindowHash_WhenLayoutContainsWindowHash()
        {
            const string windowHash = "86326f9e0df0066193401606dc91f9d3";
            var layout = string.Join(
                Environment.NewLine,
                "--- !u!114 &22",
                "MonoBehaviour:",
                "  m_EditorClassIdentifier: UnityEditor.dll::Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp",
                "  m_WindowID:",
                "    m_Value0: 6991539412822602374",
                "    m_Value1: 15274399985384702099",
                "  m_WindowHash:",
                "    serializedVersion: 2",
                $"    Hash: {windowHash}",
                "--- !u!114 &23",
                "MonoBehaviour:");

            AssertLayoutWindowIds(layout, windowHash);
        }

        [Test]
        public void EnumerateGraphToolkitWindowIds_UsesWindowId_WhenLayoutDoesNotContainWindowHash()
        {
            var expectedWindowId = new Hash128(1UL, 2UL);
            var layout = string.Join(
                Environment.NewLine,
                "--- !u!114 &22",
                "MonoBehaviour:",
                "  m_EditorClassIdentifier: UnityEditor.dll::Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp",
                "  m_WindowID:",
                "    m_Value0: 1",
                "    m_Value1: 2",
                "--- !u!114 &23",
                "MonoBehaviour:");

            AssertLayoutWindowIds(layout, expectedWindowId.ToString());
        }

        private static void AssertLayoutWindowIds(string layout, params string[] expectedWindowIds)
        {
            var layoutPath = Path.Combine(Path.GetTempPath(), $"vividrp-layout-{Guid.NewGuid():N}.dwlt");
            try
            {
                File.WriteAllText(layoutPath, layout);
                var windowIds = RenderGraphEditorWindowReflectionUtility.EnumerateGraphToolkitWindowIds(layoutPath)
                    .Select(windowId => windowId.ToString())
                    .ToArray();

                Assert.That(windowIds, Is.EqualTo(expectedWindowIds));
            }
            finally
            {
                if (File.Exists(layoutPath))
                    File.Delete(layoutPath);
            }
        }

        private sealed class TestToolState
        {
            private readonly List<TestGraphInfo> m_SubgraphStack;
            private readonly TestGraphInfo m_CurrentGraph;
            private readonly TestGraphInfo m_LastOpenedGraph;
            private readonly bool m_ThrowOnSubgraphStackAccess;

            internal TestToolState(
                string currentGraphPath,
                string lastOpenedGraphPath,
                string subgraphPath,
                bool throwOnSubgraphStackAccess = false)
            {
                CurrentGraph = new TestGraphReference(currentGraphPath);
                m_CurrentGraph = new TestGraphInfo(currentGraphPath);
                m_LastOpenedGraph = new TestGraphInfo(lastOpenedGraphPath);
                m_ThrowOnSubgraphStackAccess = throwOnSubgraphStackAccess;
                m_SubgraphStack = new List<TestGraphInfo>
                {
                    new TestGraphInfo(subgraphPath),
                };
            }

            public TestGraphReference CurrentGraph { get; }

            public IReadOnlyList<TestGraphInfo> SubgraphStack
            {
                get
                {
                    if (m_ThrowOnSubgraphStackAccess)
                        throw new System.NullReferenceException();

                    return m_SubgraphStack;
                }
            }

            public TestGraphInfo CurrentGraphInfo => m_CurrentGraph;

            public TestGraphInfo LastOpenedGraphInfo => m_LastOpenedGraph;

            public object ResolveGraphModel()
            {
                return null;
            }

            public object ResolveSubGraph(int index)
            {
                return null;
            }
        }

        private sealed class TestGraphModel
        {
            internal TestGraphModel(string filePath)
            {
                GraphObject = new TestGraphObject(filePath);
            }

            public TestGraphObject GraphObject { get; }
        }

        private sealed class TestGraphObject
        {
            internal TestGraphObject(string filePath)
            {
                FilePath = filePath;
            }

            public string FilePath { get; }
        }

        private sealed class TestGraphInfo
        {
            internal TestGraphInfo(string path)
            {
                GraphReference = new TestGraphReference(path);
            }

            public TestGraphReference GraphReference { get; }
        }

        private readonly struct TestGraphReference
        {
            internal TestGraphReference(string filePath)
            {
                FilePath = filePath;
            }

            public string FilePath { get; }
        }
    }
}
