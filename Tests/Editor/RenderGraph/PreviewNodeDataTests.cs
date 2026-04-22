using System.Linq;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class PreviewNodeDataTests
    {
        [Test]
        public void GetPreviewValue_ReturnsSameInstance_AcrossCalls()
        {
            var node = new PreviewNodeData();

            var first = node.GetPreviewValue();
            var second = node.GetPreviewValue();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Validate_LogsError_WhenGraphContainsPreviewNode()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                RenderGraphTestUtility.AddTestNode(graph, new PreviewNodeData());
                var sink = new TestErrorsAndWarnings();
                var logger = RenderGraphSubSystemTestUtility.CreateLogger(sink);

                RenderGraphEditorValidator.Validate(graph, logger);

                Assert.That(
                    sink.Errors.Any(message => message.Contains("Preview Node has been removed")),
                    Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void TrimGraph_RemovesPreviewNodes()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                RenderGraphTestUtility.AddTestNode(graph, new PreviewNodeData());

                var removed = RenderGraphEditorValidator.TrimGraph(graph);

                Assert.That(removed, Is.EqualTo(1));
                Assert.That(graph.GetNodes().OfType<PreviewNodeData>(), Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void TrimGraph_RemovesPreviewNodes_InsideSubgraphs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var subgraphNode = RenderGraphSubSystemTestUtility.CreateSubSystem(graph, out var subSystemGraph);
                Assert.That(subgraphNode, Is.Not.Null);
                RenderGraphTestUtility.AddTestNode(subSystemGraph, new PreviewNodeData());

                var removed = RenderGraphEditorValidator.TrimGraph(graph);

                Assert.That(removed, Is.EqualTo(1));
                Assert.That(subSystemGraph.GetNodes().OfType<PreviewNodeData>(), Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
