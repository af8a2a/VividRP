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
    }
}
